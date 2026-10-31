using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Hubs;
using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Media;

public interface IStarkRadioService
{
    Task<RadioStatusResponse> GetStatusAsync();
    Task<List<RadioTrack>> GetAllTracksAsync(string? search = null, string? genre = null);
    Task<RadioTrack?> GetTrackByIdAsync(string id);
    Task<RadioTrack> RequestSongAsync(string queryOrUrl, string format = "mp3", bool autoPlay = true, string? genre = null, CancellationToken ct = default);
    Task<List<RadioTrack>> DownloadAlbumAsync(string artist, string? albumName = null, int maxTracks = 15, string? genre = null, bool autoPlay = true, CancellationToken ct = default);
    Task<RadioTrack?> ReplaceCorruptedTrackAsync(string? trackId = null, CancellationToken ct = default);
    Task<bool> DeleteTrackAsync(string id);
    Task<RadioTrack?> UpdateTrackMetadataAsync(string id, string title, string artist, string album, string? genre = null);
    Task<RadioStatusResponse> ControlPlaybackAsync(string action, string? trackId = null, double? position = null, double? volume = null, string? genre = null, string? repeatMode = null);
    Task<string?> GetTrackFilePathAsync(string trackId);
    Task<string> EnsureYtDlpBinaryAsync();
    Task ResyncLibraryFromDiskAsync();
}

public class StarkRadioService : IStarkRadioService
{
    private readonly JarvisDatabase _db;
    private readonly IHubContext<JarvisHub> _hub;
    private readonly ILogger<StarkRadioService> _logger;

    private readonly string _ytDlpCustomDir = @"C:\Apps\ytdlp";
    private readonly string _libraryRootPath;

    private readonly SemaphoreSlim _downloadLock = new(2, 2); // Max 2 concurrent downloads, thread-safe
    private readonly ConcurrentDictionary<string, string> _activeDownloads = new();

    // In-memory Radio State
    private RadioTrack? _currentTrack;
    private bool _isPlaying = false;
    private double _volume = 0.85;
    private double _positionSeconds = 0;
    private bool _isShuffle = false;
    private string _repeatMode = "off"; // "off", "all", "one"
    private string? _selectedGenre;
    private readonly IArtistVerificationService _verifier;
    private readonly List<RadioTrack> _queue = new();
    private readonly List<RadioTrack> _history = new();
    private readonly object _stateLock = new();

    public StarkRadioService(JarvisDatabase db, IHubContext<JarvisHub> hub, IArtistVerificationService verifier, ILogger<StarkRadioService> logger)
    {
        _db = db;
        _hub = hub;
        _verifier = verifier;
        _logger = logger;

        _libraryRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "JarvisRadio"
        );

        if (!Directory.Exists(_libraryRootPath))
        {
            Directory.CreateDirectory(_libraryRootPath);
        }

        // Background maintenance task: Ensure latest yt-dlp.exe is present and library matches disk hierarchy
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureYtDlpBinaryAsync();
                await _verifier.ConsolidateAndHealLibraryAsync(_libraryRootPath);
                await ResyncLibraryFromDiskAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background yt-dlp/library maintenance check encountered a non-fatal issue.");
            }
        });
    }

    public async Task<RadioStatusResponse> GetStatusAsync()
    {
        var all = await _db.GetAllRadioTracksAsync();
        lock (_stateLock)
        {
            return new RadioStatusResponse
            {
                CurrentTrack = _currentTrack,
                IsPlaying = _isPlaying,
                Volume = _volume,
                PositionSeconds = _positionSeconds,
                IsShuffle = _isShuffle,
                RepeatMode = _repeatMode,
                CurrentGenre = _selectedGenre,
                Queue = _queue.ToList(),
                History = _history.Take(20).ToList(),
                TotalLibraryTracks = all.Count
            };
        }
    }

    public async Task<List<RadioTrack>> GetAllTracksAsync(string? search = null, string? genre = null)
    {
        return await _db.GetAllRadioTracksAsync(search, genre);
    }

    public async Task<RadioTrack?> GetTrackByIdAsync(string id)
    {
        return await _db.GetRadioTrackByIdAsync(id);
    }

    public async Task<string?> GetTrackFilePathAsync(string trackId)
    {
        var track = await _db.GetRadioTrackByIdAsync(trackId);
        if (track != null && File.Exists(track.FilePath))
        {
            return track.FilePath;
        }
        return null;
    }

    public async Task<RadioTrack> RequestSongAsync(string queryOrUrl, string format = "mp3", bool autoPlay = true, string? genre = null, CancellationToken ct = default)
    {
        var cleanQuery = queryOrUrl.Trim();
        _logger.LogInformation("Radio request received: '{Query}', Format: {Format}", cleanQuery, format);

        // 1. Check if we already have it in local library by fuzzy title/artist matching
        if (!cleanQuery.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !cleanQuery.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var existingMatches = await _db.GetAllRadioTracksAsync(cleanQuery);
            if (existingMatches.Count > 0)
            {
                var match = existingMatches[0];
                if (File.Exists(match.FilePath))
                {
                    _logger.LogInformation("Track '{Title}' already in library. Broadcasting.", match.Title);
                    if (autoPlay)
                    {
                        await PlayTrackDirectAsync(match);
                    }
                    else
                    {
                        lock (_stateLock) { _queue.Add(match); }
                        await BroadcastQueueUpdatedAsync();
                    }
                    return match;
                }
            }
        }

        // 2. Not in library or direct URL -> Download in background with thread safety
        await _downloadLock.WaitAsync(ct);
        try
        {
            await _hub.Clients.All.SendAsync("OnRadioDownloadProgress", new
            {
                query = cleanQuery,
                status = "SEARCHING_METADATA",
                message = $"Searching and downloading '{cleanQuery}' via neural network..."
            }, ct);

            var track = await DownloadAndProcessTrackAsync(cleanQuery, format, ct, genre: genre);
            await _db.SaveRadioTrackAsync(track);

            _logger.LogInformation("Downloaded and cataloged track: {Artist} - {Title} -> {Path}", track.Artist, track.Title, track.FilePath);

            if (autoPlay)
            {
                await PlayTrackDirectAsync(track);
            }
            else
            {
                lock (_stateLock) { _queue.Add(track); }
                await BroadcastQueueUpdatedAsync();
            }

            await _hub.Clients.All.SendAsync("OnRadioTrackReady", track, ct);
            return track;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    public async Task<List<RadioTrack>> DownloadAlbumAsync(string artist, string? albumName = null, int maxTracks = 15, string? genre = null, bool autoPlay = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artist) || artist.Equals("Albums", StringComparison.OrdinalIgnoreCase) || artist.Equals("Album", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejecting invalid album harvest request for artist name '{Artist}'", artist);
            return new List<RadioTrack>();
        }

        var canonicalArtist = _verifier.ResolveCanonicalArtistDirectory(artist, _libraryRootPath);
        var cleanArtist = canonicalArtist;
        var cleanAlbum = !string.IsNullOrWhiteSpace(albumName) ? CleanAlbumName(albumName) : null;
        var detectedGenre = DetectGenre(cleanArtist, cleanAlbum ?? "Singles", genre);

        _logger.LogInformation("Album request received for Artist: '{Artist}', Album: '{Album}', MaxTracks: {Max}", cleanArtist, cleanAlbum, maxTracks);

        await _hub.Clients.All.SendAsync("OnRadioDownloadProgress", new
        {
            query = $"{cleanArtist} - {cleanAlbum}",
            status = "SCANNING_ALBUM",
            message = $"Scanning YouTube discography for '{cleanArtist}' ({cleanAlbum})..."
        }, ct);

        // Check if we already have local tracks for this artist/album
        var existingTracks = await _db.GetAllRadioTracksAsync(cleanArtist);
        if (existingTracks.Count >= 2 && autoPlay)
        {
            _logger.LogInformation("Found {Count} existing tracks for {Artist} in library. Playing first track and harvesting remaining.", existingTracks.Count, cleanArtist);
            await PlayTrackDirectAsync(existingTracks[0]);
            for (int i = 1; i < existingTracks.Count; i++)
            {
                lock (_stateLock)
                {
                    if (!_queue.Any(t => t.Id == existingTracks[i].Id))
                    {
                        _queue.Add(existingTracks[i]);
                    }
                }
            }
            await BroadcastQueueUpdatedAsync();
        }

        var exePath = await EnsureYtDlpBinaryAsync();

        // Search strictly for individual official studio tracks, NEVER "full album" mega-mixes!
        var searchTarget = string.IsNullOrWhiteSpace(cleanAlbum)
            ? $"ytsearch{maxTracks * 3}:{cleanArtist} official audio tracks"
            : $"ytsearch{maxTracks * 2}:{cleanArtist} {cleanAlbum} official audio";

        var probeArgs = $"--dump-single-json --flat-playlist --no-warnings \"{searchTarget}\"";
        var downloadedTracks = new List<RadioTrack>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ex in existingTracks)
        {
            seenTitles.Add(CleanTrackTitle(ex.Title, cleanArtist));
        }

        try
        {
            var json = await RunProcessCaptureAsync(exePath, probeArgs, _libraryRootPath, 45000, ct);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var doc = JsonNode.Parse(json);
                var entries = doc?["entries"]?.AsArray();
                if (entries != null && entries.Count > 0)
                {
                    int fetched = 0;
                    for (int i = 0; i < entries.Count && fetched < maxTracks; i++)
                    {
                        var entry = entries[i];
                        if (entry == null) continue;

                        var rawTitle = entry["title"]?.GetValue<string>() ?? $"Track {i + 1}";
                        var duration = entry["duration"]?.GetValue<int>() ?? 0;
                        var videoUrl = entry["url"]?.GetValue<string>() ?? entry["webpage_url"]?.GetValue<string>() ?? entry["id"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(videoUrl)) continue;

                        // 1. FILTER OUT CONTINUOUS MEGA-MIX / FULL-ALBUM COMPILATIONS!
                        if (duration > 660) continue; // > 11 mins is a DJ mix or compilation video
                        if (duration > 0 && duration < 45) continue; // < 45s is a teaser or intro

                        var lowerTitle = rawTitle.ToLowerInvariant();
                        if (lowerTitle.Contains("full album") || lowerTitle.Contains("compilation") || 
                            lowerTitle.Contains("top 20 best") || lowerTitle.Contains("top 10 best") || 
                            lowerTitle.Contains("mega mix") || lowerTitle.Contains("greatest hits full") || 
                            lowerTitle.Contains("all songs") || lowerTitle.Contains("greatest hits [playlist]"))
                        {
                            continue;
                        }

                        // 2. Clean track title
                        var cleanTitle = CleanTrackTitle(rawTitle, cleanArtist);
                        if (seenTitles.Contains(cleanTitle))
                        {
                            continue; // Avoid duplicates
                        }
                        seenTitles.Add(cleanTitle);

                        // 3. Determine sensible album name (specific album, metadata album, or "Singles")
                        var entryAlbum = entry["album"]?.GetValue<string>();
                        var trackAlbum = cleanAlbum ?? (!string.IsNullOrWhiteSpace(entryAlbum) ? CleanAlbumName(entryAlbum) : "Singles");

                        // Check if title or url already in library on disk
                        var existing = (await _db.GetAllRadioTracksAsync(cleanTitle)).FirstOrDefault(t => 
                            (t.Artist.Equals(cleanArtist, StringComparison.OrdinalIgnoreCase) || t.Title.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase)) &&
                            File.Exists(t.FilePath));

                        if (existing != null)
                        {
                            downloadedTracks.Add(existing);
                            fetched++;
                            if (downloadedTracks.Count == 1 && autoPlay && (_currentTrack == null || !_isPlaying))
                            {
                                await PlayTrackDirectAsync(existing);
                            }
                            else
                            {
                                lock (_stateLock) { if (!_queue.Any(t => t.Id == existing.Id)) _queue.Add(existing); }
                            }
                            continue;
                        }

                        // Download track with throttle lock
                        await _downloadLock.WaitAsync(ct);
                        try
                        {
                            var pct = (int)(((fetched + 1.0) / maxTracks) * 100);
                            await _hub.Clients.All.SendAsync("OnRadioDownloadProgress", new
                            {
                                query = $"{cleanArtist} - {cleanTitle}",
                                percent = pct,
                                status = $"HARVESTING TRACK ({fetched + 1}/{maxTracks})",
                                message = $"Harvesting [{fetched + 1}/{maxTracks}]: '{cleanTitle}' by {cleanArtist}..."
                            }, ct);

                            var downloaded = await DownloadAndProcessTrackAsync(videoUrl, "mp3", ct, genre: detectedGenre, forcedAlbum: trackAlbum, forcedArtist: cleanArtist);
                            await _db.SaveRadioTrackAsync(downloaded);
                            downloadedTracks.Add(downloaded);
                            fetched++;

                            if (downloadedTracks.Count == 1 && autoPlay && (_currentTrack == null || !_isPlaying))
                            {
                                await PlayTrackDirectAsync(downloaded);
                            }
                            else
                            {
                                lock (_stateLock) { _queue.Add(downloaded); }
                                await BroadcastQueueUpdatedAsync();
                            }

                            await _hub.Clients.All.SendAsync("OnRadioTrackReady", downloaded, ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed downloading album track: {Title}", cleanTitle);
                        }
                        finally
                        {
                            _downloadLock.Release();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Album probe failed for {Artist} {Album}", cleanArtist, cleanAlbum);
        }

        if (downloadedTracks.Count == 0)
        {
            var single = await RequestSongAsync($"{cleanArtist} - {(cleanAlbum ?? "Top Hit")}", "mp3", autoPlay, detectedGenre, ct);
            downloadedTracks.Add(single);
        }

        return downloadedTracks;
    }

    public async Task<RadioTrack?> ReplaceCorruptedTrackAsync(string? trackId = null, CancellationToken ct = default)
    {
        RadioTrack? target = null;
        if (!string.IsNullOrWhiteSpace(trackId))
        {
            target = await _db.GetRadioTrackByIdAsync(trackId);
        }
        else
        {
            lock (_stateLock) { target = _currentTrack; }
        }

        if (target == null)
        {
            var all = await _db.GetAllRadioTracksAsync();
            target = all.FirstOrDefault();
        }

        if (target == null) return null;

        _logger.LogWarning("Remediating corrupted track: '{Title}' ({Id}). Purging flawed file.", target.Title, target.Id);

        try
        {
            if (File.Exists(target.FilePath))
            {
                File.Delete(target.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to purge corrupted file {Path}", target.FilePath);
        }

        var query = !string.IsNullOrEmpty(target.SourceUrl) ? target.SourceUrl : $"{target.Artist} - {target.Title}";
        var freshTrack = await DownloadAndProcessTrackAsync(query, target.Format, ct, target.Id, target.Genre);

        freshTrack.PlayCount = target.PlayCount;
        freshTrack.Album = target.Album;
        freshTrack.Artist = target.Artist;
        freshTrack.Genre = target.Genre;
        await _db.SaveRadioTrackAsync(freshTrack);

        await _hub.Clients.All.SendAsync("OnRadioCorruptedFixed", new
        {
            trackId = target.Id,
            newTrack = freshTrack,
            message = $"Pristine audio master acquired and verified. Broadcasting '{freshTrack.Title}' now."
        }, ct);

        await PlayTrackDirectAsync(freshTrack);
        return freshTrack;
    }

    public async Task<bool> DeleteTrackAsync(string id)
    {
        var track = await _db.GetRadioTrackByIdAsync(id);
        if (track == null) return false;

        try
        {
            if (File.Exists(track.FilePath))
            {
                File.Delete(track.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete physical track file {Path}", track.FilePath);
        }

        await _db.DeleteRadioTrackAsync(id);

        lock (_stateLock)
        {
            if (_currentTrack?.Id == id)
            {
                _currentTrack = null;
                _isPlaying = false;
            }
            _queue.RemoveAll(t => t.Id == id);
        }

        await BroadcastStateAsync();
        return true;
    }

    public async Task<RadioTrack?> UpdateTrackMetadataAsync(string id, string title, string artist, string album, string? genre = null)
    {
        var track = await _db.GetRadioTrackByIdAsync(id);
        if (track == null) return null;

        track.Title = title.Trim();
        track.Artist = artist.Trim();
        track.Album = album.Trim();
        if (!string.IsNullOrWhiteSpace(genre)) track.Genre = genre.Trim();

        await _db.SaveRadioTrackAsync(track);

        lock (_stateLock)
        {
            if (_currentTrack?.Id == id)
            {
                _currentTrack = track;
            }
        }

        await BroadcastStateAsync();
        return track;
    }

    public async Task<RadioStatusResponse> ControlPlaybackAsync(string action, string? trackId = null, double? position = null, double? volume = null, string? genre = null, string? repeatMode = null)
    {
        switch (action.ToLowerInvariant())
        {
            case "play":
            case "resume":
            case "play_id":
            case "start":
                if (!string.IsNullOrWhiteSpace(trackId))
                {
                    var t = await _db.GetRadioTrackByIdAsync(trackId);
                    if (t != null)
                    {
                        await PlayTrackDirectAsync(t);
                    }
                }
                else
                {
                    RadioTrack? toPlay = null;
                    lock (_stateLock)
                    {
                        toPlay = _currentTrack;
                    }
                    if (toPlay == null)
                    {
                        var all = await _db.GetAllRadioTracksAsync(genre: _selectedGenre);
                        toPlay = all.FirstOrDefault();
                    }
                    if (toPlay != null)
                    {
                        await PlayTrackDirectAsync(toPlay);
                    }
                    else
                    {
                        lock (_stateLock) { _isPlaying = true; }
                        await BroadcastStateAsync();
                    }
                }
                break;

            case "pause":
            case "stop":
                lock (_stateLock) { _isPlaying = false; }
                await BroadcastStateAsync();
                break;

            case "next":
            case "skip":
                await PlayNextTrackAsync();
                break;

            case "prev":
            case "previous":
                await PlayPreviousTrackAsync();
                break;

            case "shuffle":
                lock (_stateLock) { _isShuffle = !_isShuffle; }
                await BroadcastStateAsync();
                break;

            case "repeat":
                lock (_stateLock)
                {
                    if (!string.IsNullOrWhiteSpace(repeatMode))
                    {
                        _repeatMode = repeatMode.ToLowerInvariant();
                    }
                    else
                    {
                        _repeatMode = _repeatMode switch
                        {
                            "off" => "all",
                            "all" => "one",
                            _ => "off"
                        };
                    }
                }
                await BroadcastStateAsync();
                break;

            case "genre":
            case "set_genre":
                lock (_stateLock) { _selectedGenre = genre; }
                await BroadcastStateAsync();
                break;

            case "play_genre":
                lock (_stateLock) { _selectedGenre = genre; }
                var genreTracks = await _db.GetAllRadioTracksAsync(genre: genre);
                if (genreTracks.Count > 0)
                {
                    var first = _isShuffle ? genreTracks[Random.Shared.Next(genreTracks.Count)] : genreTracks[0];
                    await PlayTrackDirectAsync(first);
                    lock (_stateLock)
                    {
                        _queue.Clear();
                        _queue.AddRange(genreTracks.Where(t => t.Id != first.Id));
                    }
                    await BroadcastQueueUpdatedAsync();
                }
                else
                {
                    // Harvest a starter track for this genre
                    _ = Task.Run(async () =>
                    {
                        await RequestSongAsync($"{genre} greatest hits", "mp3", true, genre);
                    });
                }
                await BroadcastStateAsync();
                break;

            case "volume":
                if (volume.HasValue)
                {
                    lock (_stateLock) { _volume = Math.Clamp(volume.Value, 0.0, 1.0); }
                    await BroadcastStateAsync();
                }
                break;

            case "seek":
                if (position.HasValue)
                {
                    lock (_stateLock) { _positionSeconds = Math.Max(0, position.Value); }
                    await BroadcastStateAsync();
                }
                break;
        }

        return await GetStatusAsync();
    }

    private async Task PlayTrackDirectAsync(RadioTrack track)
    {
        lock (_stateLock)
        {
            if (_currentTrack != null && _currentTrack.Id != track.Id)
            {
                _history.Insert(0, _currentTrack);
            }
            _currentTrack = track;
            _isPlaying = true;
            _positionSeconds = 0;
            _queue.RemoveAll(t => t.Id == track.Id);
        }

        track.PlayCount++;
        track.LastPlayedAt = DateTime.UtcNow;
        await _db.SaveRadioTrackAsync(track);

        await BroadcastStateAsync();
    }

    private async Task PlayNextTrackAsync()
    {
        RadioTrack? next = null;
        lock (_stateLock)
        {
            if (_repeatMode == "one" && _currentTrack != null)
            {
                next = _currentTrack;
            }
            else if (_queue.Count > 0)
            {
                if (_isShuffle && _queue.Count > 1)
                {
                    var idx = Random.Shared.Next(_queue.Count);
                    next = _queue[idx];
                    _queue.RemoveAt(idx);
                }
                else
                {
                    next = _queue[0];
                    _queue.RemoveAt(0);
                }
            }
        }

        if (next != null)
        {
            await PlayTrackDirectAsync(next);
            return;
        }

        // Queue is empty -> Radio DJ behavior
        var all = await _db.GetAllRadioTracksAsync(genre: _selectedGenre);
        if (all.Count > 0)
        {
            if (_isShuffle)
            {
                var candidates = all.Where(t => t.Id != _currentTrack?.Id).ToList();
                var rand = candidates.Count > 0 ? candidates[Random.Shared.Next(candidates.Count)] : all[0];
                await PlayTrackDirectAsync(rand);
            }
            else if (_repeatMode == "all")
            {
                var curIdx = _currentTrack != null ? all.FindIndex(t => t.Id == _currentTrack.Id) : -1;
                var nextIdx = (curIdx + 1) % all.Count;
                await PlayTrackDirectAsync(all[nextIdx]);
            }
            else
            {
                var rand = all[Random.Shared.Next(all.Count)];
                await PlayTrackDirectAsync(rand);
            }
        }
        else
        {
            lock (_stateLock) { _isPlaying = false; }
            await BroadcastStateAsync();
        }
    }

    private async Task PlayPreviousTrackAsync()
    {
        RadioTrack? prev = null;
        lock (_stateLock)
        {
            if (_history.Count > 0)
            {
                prev = _history[0];
                _history.RemoveAt(0);
            }
        }

        if (prev != null)
        {
            await PlayTrackDirectAsync(prev);
        }
    }

    private async Task BroadcastStateAsync()
    {
        var status = await GetStatusAsync();
        await _hub.Clients.All.SendAsync("OnRadioStateChanged", status);
    }

    private async Task BroadcastQueueUpdatedAsync()
    {
        lock (_stateLock)
        {
            _hub.Clients.All.SendAsync("OnRadioQueueUpdated", _queue.ToList());
        }
    }

    // =========================================================================
    // YT-DLP DOWNLOAD & SENSIBLE HIERARCHICAL STORAGE ENGINE (Music -> Artist -> Album)
    // =========================================================================
    private async Task<RadioTrack> DownloadAndProcessTrackAsync(string queryOrUrl, string format, CancellationToken ct, string? explicitId = null, string? genre = null, string? forcedAlbum = null, string? forcedArtist = null)
    {
        var exePath = await EnsureYtDlpBinaryAsync();

        // 1. Probe metadata using yt-dlp dump-single-json to extract Artist, Album, Title, Duration, Thumbnail
        var meta = await ProbeMetadataAsync(exePath, queryOrUrl, ct);

        // 2. LLM-verified artist, album, track title, and canonical directory resolution
        var verified = await _verifier.VerifyTrackMetadataAsync(
            meta.Title, 
            meta.Artist, 
            forcedArtist, 
            forcedAlbum, 
            forcedArtist, 
            _libraryRootPath, 
            ct);

        var artist = verified.Artist;
        var album = SanitizeFileName(verified.Album);
        var title = SanitizeFileName(verified.TrackTitle);
        var detectedGenre = !string.IsNullOrWhiteSpace(genre) ? genre : verified.Genre;

        // 3. Build hierarchical path: Music / JarvisRadio / {CanonicalArtistDir} / {Album} /
        var targetDir = Path.Combine(_libraryRootPath, verified.CanonicalDirectoryName, album);
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var outputTemplate = Path.Combine(targetDir, "%(title)s.%(ext)s");

        // 3. Formulate yt-dlp execution arguments matching YtDlpWpfApp reference
        var isAudio = format.Equals("mp3", StringComparison.OrdinalIgnoreCase);
        var ffmpegDir = Path.GetDirectoryName(exePath) ?? _ytDlpCustomDir;

        var sb = new StringBuilder();
        sb.Append($"--no-cache-dir --rm-cache-dir ");
        sb.Append($"--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36\" ");
        sb.Append($"--ffmpeg-location \"{ffmpegDir}\" ");
        sb.Append($"--embed-thumbnail --add-metadata ");

        if (isAudio)
        {
            sb.Append($"-x --audio-format mp3 --audio-quality 0 ");
        }
        else
        {
            sb.Append($"-f \"bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/b\" ");
        }

        sb.Append($"-o \"{outputTemplate}\" ");

        var isUrl = queryOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    queryOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        var targetInput = isUrl ? queryOrUrl : $"ytsearch1:{queryOrUrl}";
        sb.Append($"\"{targetInput}\"");

        var args = sb.ToString();
        _logger.LogInformation("Dispatched yt-dlp: {Args}", args);

        var output = await RunProcessCaptureAsync(exePath, args, targetDir, 180000, ct);

        // 4. Locate final resulting file in targetDir
        var extFilter = isAudio ? "*.mp3" : "*.mp4";
        var files = Directory.GetFiles(targetDir, extFilter)
            .OrderByDescending(File.GetLastWriteTime)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"Download failed to create file in {targetDir}. Engine output:\n{output}");
        }

        var downloadedPath = files[0];
        var desiredPath = Path.Combine(targetDir, $"{title}.{(isAudio ? "mp3" : "mp4")}");
        if (!downloadedPath.Equals(desiredPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(desiredPath)) File.Delete(desiredPath);
                File.Move(downloadedPath, desiredPath);
                downloadedPath = desiredPath;
            }
            catch { }
        }

        var fileInfo = new FileInfo(downloadedPath);

        return new RadioTrack
        {
            Id = explicitId ?? Guid.NewGuid().ToString(),
            Title = title,
            Artist = artist,
            Album = album,
            Genre = detectedGenre,
            DurationSeconds = meta.DurationSeconds,
            FilePath = downloadedPath,
            Format = isAudio ? "mp3" : "mp4",
            SourceUrl = meta.SourceUrl ?? queryOrUrl,
            ThumbnailUrl = meta.ThumbnailUrl,
            FileSizeBytes = fileInfo.Length,
            PlayCount = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<(string Title, string Artist, string Album, int DurationSeconds, string? ThumbnailUrl, string? SourceUrl)> ProbeMetadataAsync(string exePath, string queryOrUrl, CancellationToken ct)
    {
        var isUrl = queryOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    queryOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var input = isUrl ? queryOrUrl : $"ytsearch1:{queryOrUrl}";

        var args = $"--dump-single-json --no-warnings --flat-playlist \"{input}\"";
        try
        {
            var json = await RunProcessCaptureAsync(exePath, args, _libraryRootPath, 30000, ct);
            if (!string.IsNullOrWhiteSpace(json) && json.Trim().StartsWith("{"))
            {
                var doc = JsonNode.Parse(json);
                if (doc != null)
                {
                    var item = doc["entries"]?[0] ?? doc;
                    var title = item?["title"]?.GetValue<string>() ?? queryOrUrl;
                    var artist = item?["artist"]?.GetValue<string>() ?? 
                                 item?["creator"]?.GetValue<string>() ?? 
                                 item?["uploader"]?.GetValue<string>() ?? "Stark Artists";
                    var album = item?["album"]?.GetValue<string>() ?? "Singles";
                    var duration = item?["duration"]?.GetValue<int>() ?? 0;
                    var thumbnail = item?["thumbnail"]?.GetValue<string>() ?? item?["thumbnails"]?[0]?["url"]?.GetValue<string>();
                    var webpage = item?["webpage_url"]?.GetValue<string>() ?? (isUrl ? queryOrUrl : null);

                    if (artist == "Stark Artists" || artist.Contains("Topic") || artist.Contains("VEVO") ||
                        artist.Contains("Official") || artist.Contains("Records") || artist.Contains("Music") ||
                        artist.Contains("Radio") || artist.Contains("Collection"))
                    {
                        if (title.Contains(" - "))
                        {
                            var parts = title.Split(" - ", 2);
                            artist = parts[0].Trim();
                            title = parts[1].Trim();
                        }
                    }

                    return (title, artist, album, duration, thumbnail, webpage);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProbeMetadataAsync fallback for {Query}", queryOrUrl);
        }

        var fallbackTitle = queryOrUrl;
        var fallbackArtist = "Stark Records";
        if (queryOrUrl.Contains(" - "))
        {
            var p = queryOrUrl.Split(" - ", 2);
            fallbackArtist = p[0].Trim();
            fallbackTitle = p[1].Trim();
        }

        return (fallbackTitle, fallbackArtist, "Stark Radio Singles", 0, null, isUrl ? queryOrUrl : null);
    }

    public static string DetectGenre(string artist, string title, string? fallbackGenre = null)
    {
        if (!string.IsNullOrWhiteSpace(fallbackGenre))
        {
            var valid = new[] { "Classical", "Rock", "Pop", "Hip-Hop / Rap", "Electronic / EDM", "R&B & Soul", "Jazz", "Blues", "Country & Folk", "Reggae & Caribbean", "Latin" };
            var match = valid.FirstOrDefault(v => v.Equals(fallbackGenre, StringComparison.OrdinalIgnoreCase) || fallbackGenre.Contains(v, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        var text = $"{artist} {title}".ToLowerInvariant();

        if (text.Contains("opera") || text.Contains("orchestra") || text.Contains("symphony") || text.Contains("classical") || text.Contains("beethoven") || text.Contains("mozart") || text.Contains("bach") || text.Contains("chopin") || text.Contains("vivaldi") || text.Contains("concerto"))
            return "Classical";

        if (text.Contains("jazz") || text.Contains("bebop") || text.Contains("swing") || text.Contains("miles davis") || text.Contains("coltrane") || text.Contains("armstrong") || text.Contains("sax"))
            return "Jazz";

        if (text.Contains("blues") || text.Contains("b.b. king") || text.Contains("muddy waters") || text.Contains("clapton") || text.Contains("stevie ray"))
            return "Blues";

        if (text.Contains("hip-hop") || text.Contains("hip hop") || text.Contains("rap") || text.Contains("trap") || text.Contains("eminem") || text.Contains("tupac") || text.Contains("drake") || text.Contains("kendrick") || text.Contains("snoop") || text.Contains("jay-z"))
            return "Hip-Hop / Rap";

        if (text.Contains("edm") || text.Contains("electronic") || text.Contains("techno") || text.Contains("house") || text.Contains("trance") || text.Contains("dubstep") || text.Contains("daft punk") || text.Contains("avicii") || text.Contains("skrillex") || text.Contains("deadmau5"))
            return "Electronic / EDM";

        if (text.Contains("r&b") || text.Contains("soul") || text.Contains("motown") || text.Contains("funk") || text.Contains("disco") || text.Contains("stevie wonder") || text.Contains("aretha") || text.Contains("marvin gaye"))
            return "R&B & Soul";

        if (text.Contains("country") || text.Contains("folk") || text.Contains("bluegrass") || text.Contains("americana") || text.Contains("johnny cash") || text.Contains("dolly parton") || text.Contains("willie nelson"))
            return "Country & Folk";

        if (text.Contains("reggae") || text.Contains("caribbean") || text.Contains("dancehall") || text.Contains("ska") || text.Contains("bob marley") || text.Contains("island"))
            return "Reggae & Caribbean";

        if (text.Contains("latin") || text.Contains("salsa") || text.Contains("bossa nova") || text.Contains("reggaeton") || text.Contains("bad bunny") || text.Contains("shakira") || text.Contains("daddy yankee") || text.Contains("santana"))
            return "Latin";

        if (text.Contains("pop") || text.Contains("taylor swift") || text.Contains("michael jackson") || text.Contains("dua lipa") || text.Contains("bruno mars") || text.Contains("k-pop") || text.Contains("bts"))
            return "Pop";

        return "Rock"; // Default: AC/DC, Led Zeppelin, Metal, Classic Rock, etc.
    }

    public async Task<string> EnsureYtDlpBinaryAsync()
    {
        var targetDir = Directory.Exists(_ytDlpCustomDir) ? _ytDlpCustomDir : @"C:\Apps\ytdlp";
        try
        {
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
        }
        catch
        {
            targetDir = Path.Combine(AppContext.BaseDirectory, "ytdlp");
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
        }

        var fullYtDlpPath = Path.Combine(targetDir, "yt-dlp.exe");
        var downloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        bool shouldUpdate = false;
        if (!File.Exists(fullYtDlpPath))
        {
            shouldUpdate = true;
        }
        else
        {
            try
            {
                var age = DateTime.Now - File.GetLastWriteTime(fullYtDlpPath);
                if (age.TotalDays > 7)
                {
                    shouldUpdate = true;
                }
            }
            catch { }
        }

        if (shouldUpdate)
        {
            _logger.LogInformation("Checking and downloading latest official yt-dlp.exe from {Url} to {Path}...", downloadUrl, fullYtDlpPath);
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "J.A.R.V.I.S.-Stark-Radio-Harvester");
                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                var tempFile = fullYtDlpPath + ".tmp";
                if (File.Exists(tempFile)) File.Delete(tempFile);

                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }

                if (File.Exists(fullYtDlpPath))
                {
                    try { File.Delete(fullYtDlpPath); } catch { }
                }
                File.Move(tempFile, fullYtDlpPath, overwrite: true);

                _logger.LogInformation("yt-dlp.exe is verified up-to-date at {Path}", fullYtDlpPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not auto-update yt-dlp.exe: {Msg}. Using existing executable if present.", ex.Message);
            }
        }

        var ffmpegPath = Path.Combine(targetDir, "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            var altFfmpeg = @"C:\Apps\ytdlp\ffmpeg.exe";
            if (File.Exists(altFfmpeg) && targetDir != @"C:\Apps\ytdlp")
            {
                try { File.Copy(altFfmpeg, ffmpegPath, overwrite: true); } catch { }
            }
        }

        if (File.Exists(fullYtDlpPath)) return fullYtDlpPath;

        var localCandidate = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        if (File.Exists(localCandidate)) return localCandidate;

        return fullYtDlpPath;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct();
        var clean = name;
        foreach (var c in invalid)
        {
            clean = clean.Replace(c, '_');
        }
        return clean.Trim().TrimEnd('.');
    }

    private static async Task<string> RunProcessCaptureAsync(string file, string args, string cwd, int timeoutMs, CancellationToken ct)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        await proc.WaitForExitAsync(timeoutCts.Token);

        var combined = stdout.ToString();
        if (string.IsNullOrWhiteSpace(combined)) combined = stderr.ToString();
        return combined.Trim();
    }

    public static string CleanArtistName(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return "Unknown Artist";
        var clean = artist.Trim();
        clean = Regex.Replace(clean, @"\s*-\s*Topic$", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*VEVO$", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*Official(\s*Channel)?$", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*Records$", "", RegexOptions.IgnoreCase);
        clean = clean.Replace("AC/DC", "AC_DC").Replace("AC⧸DC", "AC_DC");
        return SanitizeFileName(clean);
    }

    public static string CleanAlbumName(string? album)
    {
        if (string.IsNullOrWhiteSpace(album)) return "Singles";
        var clean = album.Trim();
        if (clean.Equals("Stark Radio Singles", StringComparison.OrdinalIgnoreCase) || 
            clean.Equals("Singles", StringComparison.OrdinalIgnoreCase))
        {
            return "Singles";
        }
        if (clean.Contains("Discography", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Greatest Hits Full", StringComparison.OrdinalIgnoreCase))
        {
            return "Greatest Hits";
        }
        return SanitizeFileName(clean);
    }

    public static string CleanTrackTitle(string title, string? artist = null)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Track";
        var clean = title.Trim();

        if (!string.IsNullOrWhiteSpace(artist))
        {
            var artistVariant1 = artist.Replace("_", "/");
            var artistVariant2 = artist.Replace("_", " ");
            var patterns = new[] { artist, artistVariant1, artistVariant2 };
            foreach (var a in patterns)
            {
                if (clean.StartsWith(a + " - ", StringComparison.OrdinalIgnoreCase))
                {
                    clean = clean.Substring((a + " - ").Length).Trim();
                    break;
                }
                if (clean.StartsWith(a + ": ", StringComparison.OrdinalIgnoreCase))
                {
                    clean = clean.Substring((a + ": ").Length).Trim();
                    break;
                }
            }
        }

        if (clean.Contains(" - ") && string.IsNullOrWhiteSpace(artist))
        {
            var parts = clean.Split(" - ", 2);
            clean = parts[1].Trim();
        }

        clean = Regex.Replace(clean, @"\s*[\(\[]\s*(official\s*(music\s*)?video|official\s*audio|official\s*hd\s*video|audio|lyrics|4k|hd|remaster(ed)?(\s*\d{4})?|visualizer|live)\s*[\)\]]", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*[\(\[]\s*(from\s*[""'].*?[""']\s*/\s*score)\s*[\)\]]", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*[\(\[]\s*score\s*[\)\]]", "", RegexOptions.IgnoreCase);
        clean = clean.Trim();

        return SanitizeFileName(clean);
    }

    public async Task ResyncLibraryFromDiskAsync()
    {
        _logger.LogInformation("Starting Stark Radio library synchronization from disk at: {Root}", _libraryRootPath);

        if (!Directory.Exists(_libraryRootPath))
        {
            Directory.CreateDirectory(_libraryRootPath);
            return;
        }

        // 1. Check existing tracks in DB and delete any dead records where File.Exists is false
        var existingDbTracks = await _db.GetAllRadioTracksAsync();
        var validDbPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in existingDbTracks)
        {
            if (!File.Exists(track.FilePath))
            {
                _logger.LogInformation("Removing missing/orphan track from DB: {Title} by {Artist} ({Path})", track.Title, track.Artist, track.FilePath);
                await _db.DeleteRadioTrackAsync(track.Id);
            }
            else
            {
                validDbPaths.Add(Path.GetFullPath(track.FilePath));
            }
        }

        // 2. Recursively scan _libraryRootPath for audio/video files
        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".mp4", ".m4a", ".wav", ".ogg" };
        var allFiles = Directory.GetFiles(_libraryRootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        foreach (var file in allFiles)
        {
            var fullPath = Path.GetFullPath(file);
            if (validDbPaths.Contains(fullPath))
            {
                continue; // Already verified and indexed
            }

            // Derive Artist and Album from folder hierarchy:
            // Expected: Music / JarvisRadio / {Artist} / {Album} / {TrackTitle}.ext
            var relPath = Path.GetRelativePath(_libraryRootPath, fullPath);
            var parts = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string artist;
            string album;
            string trackTitle;

            if (parts.Length >= 3)
            {
                artist = parts[0];
                album = parts[1];
                trackTitle = Path.GetFileNameWithoutExtension(parts[2]);
            }
            else if (parts.Length == 2)
            {
                artist = parts[0];
                album = "Singles";
                trackTitle = Path.GetFileNameWithoutExtension(parts[1]);
            }
            else
            {
                artist = "Stark Records";
                album = "Singles";
                trackTitle = Path.GetFileNameWithoutExtension(parts[0]);
            }

            var cleanArtist = CleanArtistName(artist);
            var cleanAlbum = CleanAlbumName(album);
            var cleanTitle = CleanTrackTitle(trackTitle, cleanArtist);
            var detectedGenre = DetectGenre(cleanArtist, cleanTitle);
            var fileInfo = new FileInfo(fullPath);

            var newTrack = new RadioTrack
            {
                Id = Guid.NewGuid().ToString(),
                Title = cleanTitle,
                Artist = cleanArtist,
                Album = cleanAlbum,
                Genre = detectedGenre,
                DurationSeconds = 0,
                FilePath = fullPath,
                Format = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant(),
                SourceUrl = null,
                ThumbnailUrl = null,
                FileSizeBytes = fileInfo.Length,
                PlayCount = 0,
                CreatedAt = fileInfo.CreationTimeUtc
            };

            await _db.SaveRadioTrackAsync(newTrack);
            _logger.LogInformation("Indexed local track into DB: '{Title}' by '{Artist}' ({Album})", cleanTitle, cleanArtist, cleanAlbum);
        }

        // 3. Update in-memory state and notify connected SignalR clients
        await BroadcastStateAsync();
    }
}
