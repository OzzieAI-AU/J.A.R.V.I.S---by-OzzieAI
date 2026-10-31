using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Llm;

namespace OzzieAI.Jarvis.Services.Media;

public class ArtistVerificationService : IArtistVerificationService
{
    private readonly LlmProviderFactory _llmFactory;
    private readonly ILogger<ArtistVerificationService> _logger;
    private static readonly HashSet<string> BannedArtistNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Albums", "Album", "Singles", "Single", "Songs", "Song", "Music", "Various", "Various Artists",
        "Top Tracks", "Discography", "Greatest Hits", "Soundtrack", "Compilation", "Unknown", "Unknown Artist",
        "Stark Artists", "Topic", "VEVO", "Official"
    };

    public ArtistVerificationService(LlmProviderFactory llmFactory, ILogger<ArtistVerificationService> logger)
    {
        _llmFactory = llmFactory;
        _logger = logger;
    }

    public async Task<VerifiedHarvestIntent> ParseUserHarvestIntentAsync(string userPrompt, string? currentlyPlayingArtist = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return new VerifiedHarvestIntent(false, null, null, false, false, null);
        }

        var existingArtists = GetExistingArtistDirectoriesSummary();

        var systemPrompt = 
            "You are Stark Industries' Media Intent Intelligence Unit for J.A.R.V.I.S.\n" +
            "Analyze the user's voice/chat command to determine if they want to harvest, download, or stream discography, albums, or singles.\n\n" +
            "CRITICAL RULES:\n" +
            "1. 'isHarvestRequest': Set to true ONLY if user asks to download, harvest, or grab albums, singles, discography, or songs.\n" +
            "2. 'artist': The real musical artist (e.g. 'AC/DC', 'Queen', 'Bob Marley', 'PinkPantheress').\n" +
            "   - ABSOLUTE BAN: NEVER, UNDER ANY CIRCUMSTANCES, SET 'artist' TO 'Albums', 'Album', 'Singles', 'Songs', or 'Music'.\n" +
            "   - If the user says 'download all albums' or 'download all their albums' WITHOUT naming an artist:\n" +
            $"     Use the Currently Playing Artist ('{currentlyPlayingArtist ?? "none"}') if valid, otherwise set 'artist' to null and set 'clarificationNeeded'.\n" +
            "3. 'album': The specific album if requested (e.g. 'Back in Black', 'Jazz', 'Paranoid'), or null if discography/all albums.\n" +
            "4. 'allAlbums': Set to true if user requested all albums, discography, or complete collection.\n" +
            "5. 'includeSingles': Set to true if singles were explicitly mentioned or discography requested.\n" +
            "6. Output ONLY a valid JSON object matching the schema below. No commentary, no codeblocks.\n\n" +
            "SCHEMA:\n" +
            "{\n" +
            "  \"isHarvestRequest\": true/false,\n" +
            "  \"artist\": \"string or null\",\n" +
            "  \"album\": \"string or null\",\n" +
            "  \"allAlbums\": true/false,\n" +
            "  \"includeSingles\": true/false,\n" +
            "  \"clarificationNeeded\": \"string or null\"\n" +
            "}";

        var userMessage = $"User Prompt: \"{userPrompt}\"\n" +
                          $"Currently Playing Artist: \"{currentlyPlayingArtist ?? "None"}\"\n" +
                          $"Existing Local Artist Vault: [{string.Join(", ", existingArtists)}]";

        try
        {
            var (provider, _, _) = await _llmFactory.GetResilientProviderAsync();
            var response = await provider.GenerateResponseAsync(new List<ChatMessage>
            {
                new() { Role = "user", Content = userMessage }
            }, systemPrompt, ct);

            var json = ExtractJson(response);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var doc = JsonNode.Parse(json);
                if (doc != null)
                {
                    var isHarvest = doc["isHarvestRequest"]?.GetValue<bool>() ?? false;
                    var artist = doc["artist"]?.GetValue<string>();
                    var album = doc["album"]?.GetValue<string>();
                    var allAlbums = doc["allAlbums"]?.GetValue<bool>() ?? false;
                    var includeSingles = doc["includeSingles"]?.GetValue<bool>() ?? false;
                    var clarification = doc["clarificationNeeded"]?.GetValue<string>();

                    // Verify against ban list
                    if (!string.IsNullOrWhiteSpace(artist) && BannedArtistNames.Contains(artist.Trim()))
                    {
                        artist = !string.IsNullOrWhiteSpace(currentlyPlayingArtist) && !BannedArtistNames.Contains(currentlyPlayingArtist)
                            ? currentlyPlayingArtist
                            : null;
                        if (artist == null) clarification = "Sir, which artist's albums would you like harvested into the vault?";
                    }

                    return new VerifiedHarvestIntent(isHarvest, artist?.Trim(), album?.Trim(), allAlbums, includeSingles, clarification);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM Intent parsing failed for prompt: {Prompt}. Engaging heuristic fallback.", userPrompt);
        }

        // Heuristic fallback
        return FallbackParseHarvestIntent(userPrompt, currentlyPlayingArtist);
    }

    public async Task<VerifiedMusicMetadata> VerifyTrackMetadataAsync(
        string rawTitle, 
        string? uploaderOrChannel, 
        string? candidateArtist, 
        string? candidateAlbum, 
        string? expectedArtist, 
        string libraryRootPath, 
        CancellationToken ct = default)
    {
        var existingDirs = GetExistingArtistDirectories(libraryRootPath);

        var systemPrompt = 
            "You are Stark Industries' Audio Intelligence Classifier.\n" +
            "Your objective: Verify and standardize the musical artist, album name, track title, and umbrella genre for accurate archival storage.\n\n" +
            "STRICT RULES:\n" +
            "1. 'artist': The canonical musical artist/band (e.g. 'AC/DC', 'Queen', 'PinkPantheress', 'Dan Lu', 'A$AP Rocky', 'Ava Max', 'Hans Zimmer').\n" +
            "   - NEVER use YouTube channels or record labels (e.g. 'RHINO', 'WaterTower Music', 'Warner Records', 'Queen Official', 'Vevo').\n" +
            "   - NEVER use words like 'Albums', 'Singles', 'Various', 'Music', 'Unknown'.\n" +
            "   - If the artist matches one of the Existing Artist Directories provided, prioritize that canonical format.\n" +
            "2. 'album': The official studio album (e.g. 'Back in Black', 'A Night at the Opera', 'Barbie The Album', 'Inception').\n" +
            "   - If it is a standalone single, non-album single, or unknown album, set album to 'Singles' and isSingle to true.\n" +
            "3. 'trackTitle': The clean track title.\n" +
            "   - Strip the artist prefix (e.g. 'Queen - Bohemian Rhapsody' -> 'Bohemian Rhapsody').\n" +
            "   - Strip video junk like '(Official Video)', '(Official Audio)', '[Official HD]', '4K Remaster', '(From Barbie The Album)', etc.\n" +
            "4. 'genre': MUST be one of:\n" +
            "   ['Classical', 'Rock', 'Pop', 'Hip-Hop / Rap', 'Electronic / EDM', 'R&B & Soul', 'Jazz', 'Blues', 'Country & Folk', 'Reggae & Caribbean', 'Latin']\n" +
            "5. Output RAW JSON ONLY. No markdown, no code fences.\n\n" +
            "SCHEMA:\n" +
            "{\n" +
            "  \"artist\": \"string\",\n" +
            "  \"album\": \"string\",\n" +
            "  \"isSingle\": true/false,\n" +
            "  \"trackTitle\": \"string\",\n" +
            "  \"genre\": \"string\"\n" +
            "}";

        var userMessage = 
            $"Raw Track Title: \"{rawTitle}\"\n" +
            $"Uploader/Channel: \"{uploaderOrChannel ?? "None"}\"\n" +
            $"Candidate Artist: \"{candidateArtist ?? "None"}\"\n" +
            $"Candidate Album: \"{candidateAlbum ?? "None"}\"\n" +
            $"Expected Artist Context: \"{expectedArtist ?? "None"}\"\n" +
            $"Existing Artist Directories: [{string.Join(", ", existingDirs)}]";

        try
        {
            var (provider, _, _) = await _llmFactory.GetResilientProviderAsync();
            var response = await provider.GenerateResponseAsync(new List<ChatMessage>
            {
                new() { Role = "user", Content = userMessage }
            }, systemPrompt, ct);

            var json = ExtractJson(response);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var doc = JsonNode.Parse(json);
                if (doc != null)
                {
                    var artist = doc["artist"]?.GetValue<string>()?.Trim();
                    var album = doc["album"]?.GetValue<string>()?.Trim();
                    var isSingle = doc["isSingle"]?.GetValue<bool>() ?? false;
                    var trackTitle = doc["trackTitle"]?.GetValue<string>()?.Trim();
                    var genre = doc["genre"]?.GetValue<string>()?.Trim();

                    if (!string.IsNullOrWhiteSpace(artist) && !BannedArtistNames.Contains(artist))
                    {
                        var canonicalDir = ResolveCanonicalArtistDirectory(artist, libraryRootPath);
                        var finalAlbum = isSingle || string.IsNullOrWhiteSpace(album) ? "Singles" : album;
                        var finalTitle = !string.IsNullOrWhiteSpace(trackTitle) ? trackTitle : CleanTrackTitleFallback(rawTitle, artist);
                        var finalGenre = !string.IsNullOrWhiteSpace(genre) ? genre : StarkRadioService.DetectGenre(artist, finalTitle);

                        return new VerifiedMusicMetadata(
                            artist,
                            finalAlbum,
                            isSingle,
                            finalTitle,
                            finalGenre,
                            canonicalDir,
                            0.95
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM track metadata verification failed for {Title}. Engaging fallback.", rawTitle);
        }

        // Deterministic Fallback
        return FallbackVerifyTrackMetadata(rawTitle, uploaderOrChannel, candidateArtist, candidateAlbum, expectedArtist, libraryRootPath);
    }

    public string ResolveCanonicalArtistDirectory(string candidateArtist, string libraryRootPath)
    {
        if (string.IsNullOrWhiteSpace(candidateArtist) || BannedArtistNames.Contains(candidateArtist.Trim()))
        {
            candidateArtist = "Stark Records";
        }

        var clean = candidateArtist.Trim();
        clean = clean.Replace("AC/DC", "AC_DC").Replace("AC⧸DC", "AC_DC");

        var candidateKey = NormalizeKey(clean);

        if (Directory.Exists(libraryRootPath))
        {
            var subDirs = Directory.GetDirectories(libraryRootPath);
            foreach (var dir in subDirs)
            {
                var dirName = Path.GetFileName(dir);
                if (BannedArtistNames.Contains(dirName)) continue;

                var dirKey = NormalizeKey(dirName);

                // 1. Exact key match (e.g. 'acdc' matches 'AC DC' and 'AC_DC')
                if (dirKey == candidateKey)
                {
                    return dirName;
                }

                // 2. Plural stem match (e.g. 'queens' matches 'Queen')
                if (candidateKey.Length > 3 && dirKey.Length > 3)
                {
                    if (candidateKey == dirKey + "s" || dirKey == candidateKey + "s")
                    {
                        return dirName;
                    }
                }
            }
        }

        // If no existing match, generate standard sanitized directory name
        var sanitized = SanitizePath(clean);
        return sanitized;
    }

    public async Task<int> ConsolidateAndHealLibraryAsync(string libraryRootPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Stark Media Library Consolidation & Healing at: {Path}", libraryRootPath);
        if (!Directory.Exists(libraryRootPath)) return 0;

        int healedCount = 0;

        // 1. HEAL 'Albums' ROGUE DIRECTORY
        var albumsDir = Path.Combine(libraryRootPath, "Albums");
        if (Directory.Exists(albumsDir))
        {
            _logger.LogInformation("Detected rogue 'Albums' directory. Dismantling and relocating misplaced tracks...");
            var files = Directory.GetFiles(albumsDir, "*.*", SearchOption.AllDirectories)
                .Where(f => IsAudioFile(f))
                .ToList();

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var meta = await VerifyTrackMetadataAsync(fileName, null, null, null, null, libraryRootPath, ct);

                var targetDir = Path.Combine(libraryRootPath, meta.CanonicalDirectoryName, meta.Album);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                var targetFile = Path.Combine(targetDir, $"{SanitizePath(meta.TrackTitle)}{Path.GetExtension(file)}");
                if (File.Exists(targetFile)) File.Delete(targetFile);

                File.Move(file, targetFile);
                _logger.LogInformation("Relocated rogue track '{File}' -> '{Dest}'", file, targetFile);
                healedCount++;
            }

            try { Directory.Delete(albumsDir, true); } catch { }
        }

        // 2. MERGE 'Ac Dc' INTO 'AC_DC'
        var acdcSpace = Path.Combine(libraryRootPath, "Ac Dc");
        var acdcUnderscore = Path.Combine(libraryRootPath, "AC_DC");
        if (Directory.Exists(acdcSpace))
        {
            if (!Directory.Exists(acdcUnderscore)) Directory.CreateDirectory(acdcUnderscore);

            var files = Directory.GetFiles(acdcSpace, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(acdcSpace, file);
                var dest = Path.Combine(acdcUnderscore, rel);
                var destDir = Path.GetDirectoryName(dest)!;
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                if (File.Exists(dest)) File.Delete(dest);
                File.Move(file, dest);
                healedCount++;
            }

            try { Directory.Delete(acdcSpace, true); } catch { }
            _logger.LogInformation("Merged 'Ac Dc' duplicate directory into 'AC_DC'.");
        }

        // 3. MERGE 'Queens' INTO 'Queen' & SPECIFIC ARTISTS
        var queensDir = Path.Combine(libraryRootPath, "Queens");
        var queenDir = Path.Combine(libraryRootPath, "Queen");
        if (Directory.Exists(queensDir))
        {
            var files = Directory.GetFiles(queensDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                string targetArtistDir;
                string targetAlbum = "Singles";

                if (fileName.Contains("Ava Max", StringComparison.OrdinalIgnoreCase))
                {
                    targetArtistDir = "Ava Max";
                }
                else if (fileName.Contains("The Blaze", StringComparison.OrdinalIgnoreCase))
                {
                    targetArtistDir = "The Blaze";
                }
                else
                {
                    targetArtistDir = "Queen";
                }

                var resolvedDir = ResolveCanonicalArtistDirectory(targetArtistDir, libraryRootPath);
                var targetDir = Path.Combine(libraryRootPath, resolvedDir, targetAlbum);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                var cleanTitle = CleanTrackTitleFallback(fileName, targetArtistDir);
                var dest = Path.Combine(targetDir, $"{SanitizePath(cleanTitle)}{Path.GetExtension(file)}");
                if (File.Exists(dest)) File.Delete(dest);

                File.Move(file, dest);
                healedCount++;
            }

            try { Directory.Delete(queensDir, true); } catch { }
            _logger.LogInformation("Healed 'Queens' directory into canonical artist vaults.");
        }

        _logger.LogInformation("Consolidation complete. Total healed items: {Count}", healedCount);
        return healedCount;
    }

    private static string NormalizeKey(string s)
    {
        return Regex.Replace(s, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
    }

    private static string SanitizePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Track";
        var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct();
        var clean = name;
        foreach (var c in invalid)
        {
            clean = clean.Replace(c, '_');
        }
        return clean.Trim().TrimEnd('.');
    }

    private static bool IsAudioFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp3" or ".m4a" or ".wav" or ".flac" or ".ogg" or ".mp4";
    }

    private List<string> GetExistingArtistDirectoriesSummary()
    {
        var libraryRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "JarvisRadio");
        return GetExistingArtistDirectories(libraryRoot);
    }

    private List<string> GetExistingArtistDirectories(string libraryRoot)
    {
        if (!Directory.Exists(libraryRoot)) return new List<string>();
        try
        {
            return Directory.GetDirectories(libraryRoot)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n) && !BannedArtistNames.Contains(n!))
                .Select(n => n!)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var match = Regex.Match(text, @"\{[\s\S]*\}");
        return match.Success ? match.Value : "";
    }

    private VerifiedHarvestIntent FallbackParseHarvestIntent(string prompt, string? currentlyPlayingArtist)
    {
        var lower = prompt.ToLowerInvariant();
        var isHarvest = (lower.Contains("download") && (lower.Contains("album") || lower.Contains("discography") || lower.Contains("single"))) ||
                        (lower.Contains("play all") && lower.Contains("songs")) ||
                        lower.Contains("download all their albums") || lower.Contains("download all albums");

        if (!isHarvest) return new VerifiedHarvestIntent(false, null, null, false, false, null);

        string? artist = null;

        if (lower.Contains("ac/dc") || lower.Contains("acdc") || lower.Contains("ad/dc") || lower.Contains("addc"))
        {
            artist = "AC/DC";
        }
        else if (lower.Contains("queen") && !lower.Contains("kings"))
        {
            artist = "Queen";
        }
        else if (lower.Contains("pinkpantheress") || lower.Contains("pink pantheress"))
        {
            artist = "PinkPantheress";
        }
        else if (lower.Contains("dan lu"))
        {
            artist = "Dan Lu";
        }
        else if (lower.Contains("shinedown"))
        {
            artist = "Shinedown";
        }
        else
        {
            var match = Regex.Match(prompt, @"(?:play all|download all|harvest)\s+(?:of\s+|the\s+)*(?<artist>[a-zA-Z0-9\s/&.-]+?)(?:'s|\s+albums|\s+singles|\s+songs|\s+discography|\s+and|,|\.|$)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var candidate = match.Groups["artist"].Value.Trim();
                if (!BannedArtistNames.Contains(candidate))
                {
                    artist = candidate;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(artist))
        {
            artist = !string.IsNullOrWhiteSpace(currentlyPlayingArtist) && !BannedArtistNames.Contains(currentlyPlayingArtist)
                ? currentlyPlayingArtist
                : null;
        }

        var allAlbums = lower.Contains("all album") || lower.Contains("discography") || lower.Contains("all their albums");
        var includeSingles = lower.Contains("single") || lower.Contains("discography");
        var clarification = artist == null ? "Sir, please specify which artist's albums you would like harvested into the vault." : null;

        return new VerifiedHarvestIntent(true, artist, null, allAlbums, includeSingles, clarification);
    }

    private VerifiedMusicMetadata FallbackVerifyTrackMetadata(
        string rawTitle, 
        string? uploader, 
        string? candidateArtist, 
        string? candidateAlbum, 
        string? expectedArtist, 
        string libraryRootPath)
    {
        string artist = expectedArtist ?? candidateArtist ?? uploader ?? "Stark Artists";

        // Check if raw title has Artist - Title
        if (rawTitle.Contains(" - "))
        {
            var parts = rawTitle.Split(" - ", 2);
            var left = parts[0].Trim();
            if (!left.Contains("Official", StringComparison.OrdinalIgnoreCase) && !left.Contains("Video", StringComparison.OrdinalIgnoreCase))
            {
                artist = left;
            }
        }

        if (BannedArtistNames.Contains(artist))
        {
            artist = !string.IsNullOrWhiteSpace(expectedArtist) ? expectedArtist : "Stark Records";
        }

        var cleanArtist = StarkRadioService.CleanArtistName(artist);
        var canonicalDir = ResolveCanonicalArtistDirectory(cleanArtist, libraryRootPath);

        var cleanTitle = CleanTrackTitleFallback(rawTitle, cleanArtist);
        var cleanAlbum = !string.IsNullOrWhiteSpace(candidateAlbum) ? StarkRadioService.CleanAlbumName(candidateAlbum) : "Singles";
        var genre = StarkRadioService.DetectGenre(cleanArtist, cleanTitle);

        return new VerifiedMusicMetadata(cleanArtist, cleanAlbum, cleanAlbum == "Singles", cleanTitle, genre, canonicalDir, 0.7);
    }

    private static string CleanTrackTitleFallback(string title, string artist)
    {
        var clean = title.Trim();
        var artistPrefixes = new[] { artist, artist.Replace("_", "/"), artist.Replace("_", " ") };
        foreach (var p in artistPrefixes)
        {
            if (clean.StartsWith(p + " - ", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring((p + " - ").Length).Trim();
                break;
            }
            if (clean.StartsWith(p + " – ", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring((p + " – ").Length).Trim();
                break;
            }
        }

        clean = Regex.Replace(clean, @"\s*[\(\[]\s*(official\s*(music\s*)?video|official\s*audio|official\s*hd\s*video|audio|lyrics|4k|hd|remaster(ed)?(\s*\d{4})?|visualizer|live)\s*[\)\]]", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*[\(\[]\s*(from\s*[""'].*?[""']\s*/\s*score)\s*[\)\]]", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*[\(\[]\s*(from\s+.*?album)\s*[\)\]]", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*[\(\[]\s*score\s*[\)\]]", "", RegexOptions.IgnoreCase);

        return SanitizePath(clean);
    }
}
