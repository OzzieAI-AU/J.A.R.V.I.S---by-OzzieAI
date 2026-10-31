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

public class VideoPlayerService : IVideoPlayerService
{
    private readonly JarvisDatabase _db;
    private readonly IHubContext<JarvisHub> _hub;
    private readonly ILogger<VideoPlayerService> _logger;

    private readonly string _ytDlpCustomDir = @"C:\Apps\ytdlp";
    private readonly string _videoLibraryRoot;
    private readonly SemaphoreSlim _downloadLock = new(2, 2);
    private readonly ConcurrentDictionary<string, int> _activeDownloads = new();

    public VideoPlayerService(JarvisDatabase db, IHubContext<JarvisHub> hub, ILogger<VideoPlayerService> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;

        _videoLibraryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "JarvisCinema"
        );

        if (!Directory.Exists(_videoLibraryRoot))
        {
            Directory.CreateDirectory(_videoLibraryRoot);
        }

        // Background maintenance task: Seed default channels if empty and resync local videos
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureDefaultChannelsSeededAsync();
                await ResyncDownloadedVideosAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background VideoPlayerService initialization encountered a non-fatal issue.");
            }
        });
    }

    public async Task<List<YouTubeChannel>> GetChannelsAsync(string? platform = null)
    {
        return await _db.GetYouTubeChannelsAsync(platform);
    }

    public async Task<YouTubeChannel> AddChannelAsync(string channelUrlOrHandle, string? platform = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelUrlOrHandle))
        {
            throw new ArgumentException("Channel URL or handle cannot be empty.", nameof(channelUrlOrHandle));
        }

        var (normalizedUrl, detectedPlatform) = NormalizeChannelUrl(channelUrlOrHandle, platform);
        _logger.LogInformation("Probing {Platform} channel info for: {Url}", detectedPlatform, normalizedUrl);

        var exePath = await EnsureYtDlpBinaryAsync();

        // 1. Probe channel metadata
        var probeArgs = $"--dump-single-json --flat-playlist --playlist-end 1 \"{normalizedUrl}\"";
        var json = await RunProcessCaptureAsync(exePath, probeArgs, _videoLibraryRoot, 40000, ct);

        if (string.IsNullOrWhiteSpace(json) || !json.Trim().StartsWith("{"))
        {
            throw new InvalidOperationException($"Unable to resolve {detectedPlatform} channel for: {channelUrlOrHandle}");
        }

        var doc = JsonNode.Parse(json);
        if (doc == null)
        {
            throw new InvalidOperationException($"Failed to parse {detectedPlatform} channel metadata.");
        }

        var channelName = doc["channel"]?.GetValue<string>() ?? 
                          doc["uploader"]?.GetValue<string>() ?? 
                          doc["title"]?.GetValue<string>() ?? (detectedPlatform == "Rumble" ? "Rumble Channel" : "YouTube Channel");
        var uploaderUrl = doc["uploader_url"]?.GetValue<string>() ?? normalizedUrl;
        var channelId = doc["channel_id"]?.GetValue<string>() ?? doc["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
        var desc = doc["description"]?.GetValue<string>();
        
        // Find avatar image in thumbnails
        string? avatarUrl = null;
        var thumbs = doc["thumbnails"]?.AsArray();
        if (thumbs != null)
        {
            foreach (var t in thumbs)
            {
                var idVal = t?["id"]?.GetValue<string>();
                if (idVal == "avatar_uncropped" || idVal == "7" || idVal == "8" || idVal == "avatar")
                {
                    avatarUrl = t?["url"]?.GetValue<string>();
                    break;
                }
            }
            if (avatarUrl == null && thumbs.Count > 0)
            {
                avatarUrl = thumbs[thumbs.Count - 1]?["url"]?.GetValue<string>();
            }
        }

        string? subsStr = null;
        var subCount = doc["channel_follower_count"]?.GetValue<long?>() ?? doc["follower_count"]?.GetValue<long?>();
        if (subCount.HasValue)
        {
            subsStr = FormatFollowerCount(subCount.Value);
        }

        var channel = new YouTubeChannel
        {
            Id = channelId,
            ChannelName = channelName,
            ChannelHandle = ExtractHandle(uploaderUrl) ?? ExtractHandle(channelUrlOrHandle) ?? (detectedPlatform == "Rumble" ? channelName : null),
            ChannelUrl = uploaderUrl,
            AvatarUrl = avatarUrl,
            Description = desc,
            SubscriberCount = subsStr,
            Platform = detectedPlatform,
            CreatedAt = DateTime.UtcNow,
            LastSyncedAt = DateTime.UtcNow
        };

        await _db.SaveYouTubeChannelAsync(channel);

        // 2. Sync newest 15 videos immediately in background
        _ = Task.Run(async () =>
        {
            try
            {
                await SyncChannelVideosAsync(channel.Id, 15, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed initial video sync for channel {Name}", channel.ChannelName);
            }
        });

        return channel;
    }

    public async Task<bool> DeleteChannelAsync(string channelId)
    {
        return await _db.DeleteYouTubeChannelAsync(channelId);
    }

    public async Task<int> SyncChannelVideosAsync(string channelId, int maxVideos = 15, CancellationToken ct = default)
    {
        var channel = await _db.GetYouTubeChannelByIdAsync(channelId);
        if (channel == null)
        {
            throw new KeyNotFoundException($"Channel with ID {channelId} not found.");
        }

        var exePath = await EnsureYtDlpBinaryAsync();
        string targetUrl;
        if (string.Equals(channel.Platform, "Rumble", StringComparison.OrdinalIgnoreCase))
        {
            targetUrl = channel.ChannelUrl.TrimEnd('/');
        }
        else
        {
            targetUrl = channel.ChannelUrl.EndsWith("/videos") ? channel.ChannelUrl : channel.ChannelUrl.TrimEnd('/') + "/videos";
        }

        var probeArgs = $"--dump-single-json --flat-playlist --playlist-end {maxVideos} \"{targetUrl}\"";
        var json = await RunProcessCaptureAsync(exePath, probeArgs, _videoLibraryRoot, 50000, ct);

        if (string.IsNullOrWhiteSpace(json)) return 0;

        var doc = JsonNode.Parse(json);
        var entries = doc?["entries"]?.AsArray();
        if (entries == null || entries.Count == 0) return 0;

        int addedOrUpdated = 0;
        foreach (var e in entries)
        {
            if (e == null) continue;
            var vidId = e["id"]?.GetValue<string>();
            var title = e["title"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(vidId) || string.IsNullOrWhiteSpace(title)) continue;

            var duration = e["duration"]?.GetValue<int>() ?? 0;
            var viewCount = e["view_count"]?.GetValue<long?>();
            var timestamp = e["timestamp"]?.GetValue<long?>();

            string? pubDate = null;
            if (timestamp.HasValue)
            {
                pubDate = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).UtcDateTime.ToString("yyyy-MM-dd");
            }
            else
            {
                pubDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }

            string thumbUrl;
            string videoUrl;
            string? embedUrl;

            if (string.Equals(channel.Platform, "Rumble", StringComparison.OrdinalIgnoreCase))
            {
                videoUrl = e["url"]?.GetValue<string>() ?? e["webpage_url"]?.GetValue<string>() ?? $"https://rumble.com/{vidId}";
                thumbUrl = e["thumbnail"]?.GetValue<string>() ?? 
                           (e["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>()) ?? "";
                embedUrl = $"https://rumble.com/embed/{vidId}/";
            }
            else
            {
                thumbUrl = $"https://i.ytimg.com/vi/{vidId}/hqdefault.jpg";
                videoUrl = $"https://www.youtube.com/watch?v={vidId}";
                embedUrl = $"https://www.youtube-nocookie.com/embed/{vidId}?autoplay=1&rel=0&modestbranding=1";
            }

            // Check if already downloaded locally
            var existing = await _db.GetChannelVideoByIdAsync(vidId);
            bool isDownloaded = existing?.IsDownloaded == true && !string.IsNullOrWhiteSpace(existing.LocalFilePath) && File.Exists(existing.LocalFilePath);
            string? localPath = isDownloaded ? existing!.LocalFilePath : null;
            long fileSize = isDownloaded ? existing!.FileSizeBytes : 0;

            var videoItem = new ChannelVideo
            {
                Id = vidId,
                ChannelId = channel.Id,
                ChannelName = channel.ChannelName,
                Title = title,
                Description = e["description"]?.GetValue<string>() ?? existing?.Description,
                ThumbnailUrl = thumbUrl,
                VideoUrl = videoUrl,
                EmbedUrl = embedUrl,
                DurationSeconds = duration,
                ViewCount = viewCount.HasValue ? FormatFollowerCount(viewCount.Value) : null,
                PublishedAt = pubDate,
                IsDownloaded = isDownloaded,
                LocalFilePath = localPath,
                FileSizeBytes = fileSize,
                Platform = channel.Platform,
                CreatedAt = DateTime.UtcNow
            };

            await _db.SaveChannelVideoAsync(videoItem);
            addedOrUpdated++;
        }

        channel.LastSyncedAt = DateTime.UtcNow;
        await _db.SaveYouTubeChannelAsync(channel);

        await _hub.Clients.All.SendAsync("OnVideoFeedUpdated", ct);
        return addedOrUpdated;
    }

    public async Task<List<ChannelVideo>> GetRecommendedVideosAsync(string? channelId = null, string? search = null, bool onlyDownloaded = false, bool? onlyUnwatched = null, string? platform = null, int limit = 100)
    {
        return await _db.GetChannelVideosAsync(channelId, search, onlyDownloaded, onlyUnwatched, platform, limit);
    }

    public async Task<ChannelVideo?> GetVideoByIdAsync(string videoId)
    {
        return await _db.GetChannelVideoByIdAsync(videoId);
    }

    public async Task<ChannelVideo?> SetVideoWatchedStatusAsync(string videoId, bool isWatched)
    {
        var success = await _db.UpdateVideoWatchedStatusAsync(videoId, isWatched);
        if (!success) return null;

        var video = await _db.GetChannelVideoByIdAsync(videoId);
        if (video != null)
        {
            await _hub.Clients.All.SendAsync("OnVideoWatchedChanged", new
            {
                videoId = video.Id,
                isWatched = video.IsWatched,
                watchedAt = video.WatchedAt
            });
        }
        return video;
    }

    public async Task<ChannelVideo> DownloadVideoAsync(string videoId, CancellationToken ct = default)
    {
        var video = await _db.GetChannelVideoByIdAsync(videoId);
        if (video == null)
        {
            throw new KeyNotFoundException($"Video {videoId} not found in database.");
        }

        if (video.IsDownloaded && !string.IsNullOrWhiteSpace(video.LocalFilePath) && File.Exists(video.LocalFilePath))
        {
            return video; // Already downloaded
        }

        var exePath = await EnsureYtDlpBinaryAsync();

        // Target directory: Videos / JarvisCinema / {Platform} / {CleanChannelName} / {CleanVideoTitle}.mp4
        var cleanChannel = SanitizeFileName(video.ChannelName);
        var cleanTitle = SanitizeFileName(video.Title);
        var platformFolder = string.Equals(video.Platform, "Rumble", StringComparison.OrdinalIgnoreCase) ? "Rumble" : "YouTube";
        var targetDir = Path.Combine(_videoLibraryRoot, platformFolder, cleanChannel);

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var targetFile = Path.Combine(targetDir, $"{cleanTitle}.mp4");
        var outputTemplate = Path.Combine(targetDir, "%(title)s.%(ext)s");

        await _downloadLock.WaitAsync(ct);
        try
        {
            _activeDownloads[videoId] = 0;
            await _hub.Clients.All.SendAsync("OnVideoDownloadProgress", new
            {
                videoId = video.Id,
                channelName = video.ChannelName,
                title = video.Title,
                percent = 5,
                status = "STARTING DOWNLOAD"
            }, ct);

            var ffmpegDir = Path.GetDirectoryName(exePath) ?? _ytDlpCustomDir;
            var sb = new StringBuilder();
            sb.Append($"--no-cache-dir --rm-cache-dir ");
            sb.Append($"--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36\" ");
            sb.Append($"--ffmpeg-location \"{ffmpegDir}\" ");
            sb.Append($"-f \"bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/b\" ");
            sb.Append($"-o \"{outputTemplate}\" ");
            sb.Append($"\"{video.VideoUrl}\"");

            var args = sb.ToString();
            _logger.LogInformation("Dispatched Video yt-dlp: {Args}", args);

            await _hub.Clients.All.SendAsync("OnVideoDownloadProgress", new
            {
                videoId = video.Id,
                channelName = video.ChannelName,
                title = video.Title,
                percent = 35,
                status = "DOWNLOADING HIGH DEFINITION VIDEO & AUDIO..."
            }, ct);

            var output = await RunProcessCaptureAsync(exePath, args, targetDir, 300000, ct);

            // Locate newly downloaded mp4 file in targetDir
            var files = Directory.GetFiles(targetDir, "*.mp4")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();

            if (files.Count == 0)
            {
                throw new InvalidOperationException($"Video download failed to produce an MP4 file in {targetDir}. Output:\n{output}");
            }

            var downloadedPath = files[0];
            if (!downloadedPath.Equals(targetFile, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(targetFile)) File.Delete(targetFile);
                    File.Move(downloadedPath, targetFile);
                    downloadedPath = targetFile;
                }
                catch { }
            }

            var fileInfo = new FileInfo(downloadedPath);
            video.IsDownloaded = true;
            video.LocalFilePath = downloadedPath;
            video.FileSizeBytes = fileInfo.Length;

            await _db.UpdateVideoDownloadStatusAsync(video.Id, true, downloadedPath, fileInfo.Length);

            _activeDownloads.TryRemove(videoId, out _);

            await _hub.Clients.All.SendAsync("OnVideoDownloadProgress", new
            {
                videoId = video.Id,
                channelName = video.ChannelName,
                title = video.Title,
                percent = 100,
                status = "DOWNLOAD COMPLETE"
            }, ct);

            await _hub.Clients.All.SendAsync("OnVideoReady", video, ct);
            return video;
        }
        catch (Exception ex)
        {
            _activeDownloads.TryRemove(videoId, out _);
            _logger.LogError(ex, "Failed downloading video: {Title} ({Id})", video.Title, video.Id);
            throw;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    public async Task<string?> GetVideoFilePathAsync(string videoId)
    {
        var video = await _db.GetChannelVideoByIdAsync(videoId);
        if (video != null && video.IsDownloaded && !string.IsNullOrWhiteSpace(video.LocalFilePath) && File.Exists(video.LocalFilePath))
        {
            return video.LocalFilePath;
        }
        return null;
    }

    public async Task EnsureDefaultChannelsSeededAsync()
    {
        var existingYt = await _db.GetYouTubeChannelsAsync("YouTube");
        if (existingYt.Count == 0)
        {
            _logger.LogInformation("Seeding default YouTube Channels for Stark Cinema...");

            var defaults = new[]
            {
                new YouTubeChannel
                {
                    Id = "UCsBjURrPoezykLs9EqgamOA",
                    ChannelName = "Fireship",
                    ChannelHandle = "@Fireship",
                    ChannelUrl = "https://www.youtube.com/@Fireship",
                    AvatarUrl = "https://yt3.googleusercontent.com/3fPNbkf_xPyCleq77ZhcxyeorY97NtMHVNUbaAON_RBDH9ydL4hJkjxC8x_4mpuopkB8oI7Ct6Y=s900-c-k-c0x00ffffff-no-rj",
                    Description = "High-intensity code tutorials and tech news to help you ship apps faster.",
                    SubscriberCount = "3.2M",
                    Platform = "YouTube",
                    CreatedAt = DateTime.UtcNow
                },
                new YouTubeChannel
                {
                    Id = "UCHnyfMqiRRG1u-2MsSQLbXA",
                    ChannelName = "Veritasium",
                    ChannelHandle = "@veritasium",
                    ChannelUrl = "https://www.youtube.com/@veritasium",
                    AvatarUrl = "https://yt3.googleusercontent.com/ytc/AIdro_kI2Z_p5qM4n9Jm_8Y4o7w0K6qZ7bX8iG6M3L2N4=s900-c-k-c0x00ffffff-no-rj",
                    Description = "An element of truth - videos about science, education, and anything interesting.",
                    SubscriberCount = "15.9M",
                    Platform = "YouTube",
                    CreatedAt = DateTime.UtcNow
                },
                new YouTubeChannel
                {
                    Id = "UCbfYPyITQ-EY782jAmlYz_g",
                    ChannelName = "Two Minute Papers",
                    ChannelHandle = "@TwoMinutePapers",
                    ChannelUrl = "https://www.youtube.com/@TwoMinutePapers",
                    AvatarUrl = "https://yt3.googleusercontent.com/ytc/AIdro_n_9vF7m4j8Jq7p_9Y4o7w0K6qZ7bX8iG6M3L2N4=s900-c-k-c0x00ffffff-no-rj",
                    Description = "What a time to be alive! AI research and graphics breakthroughs explained in two minutes.",
                    SubscriberCount = "1.6M",
                    Platform = "YouTube",
                    CreatedAt = DateTime.UtcNow
                }
            };

            foreach (var c in defaults)
            {
                await _db.SaveYouTubeChannelAsync(c);
            }

            _ = Task.Run(async () =>
            {
                foreach (var c in defaults)
                {
                    try { await SyncChannelVideosAsync(c.Id, 10, CancellationToken.None); } catch { }
                }
            });
        }

        // Seed default Rumble Channels if none exist
        var existingRumble = await _db.GetYouTubeChannelsAsync("Rumble");
        if (existingRumble.Count == 0)
        {
            _logger.LogInformation("Seeding default Rumble Channels for Stark Cinema...");

            var rumbleDefaults = new[]
            {
                new YouTubeChannel
                {
                    Id = "rumble-redacted",
                    ChannelName = "Redacted",
                    ChannelHandle = "Redacted",
                    ChannelUrl = "https://rumble.com/c/Redacted",
                    AvatarUrl = "https://ak2.rmbl.ws/s8/1/i/n/6/o/in6od.ba-thumb-in6od-2x240.jpg",
                    Description = "Redacted with Clayton and Natali Morris. Daily independent news and intelligence.",
                    SubscriberCount = "2.2M",
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                },
                new YouTubeChannel
                {
                    Id = "rumble-russellbrand",
                    ChannelName = "Russell Brand",
                    ChannelHandle = "RussellBrand",
                    ChannelUrl = "https://rumble.com/c/RussellBrand",
                    AvatarUrl = "https://ak2.rmbl.ws/s8/1/7/E/0/v/7E0vd.ba-thumb-7E0vd-2x240.jpg",
                    Description = "Stay free with Russell Brand. Daily explorations into news, transparency, and freedom.",
                    SubscriberCount = "1.8M",
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                },
                new YouTubeChannel
                {
                    Id = "rumble-ggreenwald",
                    ChannelName = "Glenn Greenwald",
                    ChannelHandle = "GGreenwald",
                    ChannelUrl = "https://rumble.com/c/GGreenwald",
                    AvatarUrl = "https://ak2.rmbl.ws/s8/1/0/j/8/m/0j8md.ba-thumb-0j8md-2x240.jpg",
                    Description = "System Update with Glenn Greenwald. Deep investigative reporting and political analysis.",
                    SubscriberCount = "680K",
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                }
            };

            foreach (var r in rumbleDefaults)
            {
                await _db.SaveYouTubeChannelAsync(r);
            }

            // Seed initial featured Rumble broadcasts for immediate playback
            var sampleRumbleVideos = new[]
            {
                new ChannelVideo
                {
                    Id = "v5u1v9b",
                    ChannelId = "rumble-redacted",
                    ChannelName = "Redacted",
                    Title = "The System Is Panicking About What We Just Uncovered",
                    Description = "Clayton and Natali Morris expose new findings in independent journalism.",
                    ThumbnailUrl = "https://ak2.rmbl.ws/s8/1/i/n/6/o/in6od.ba-thumb-in6od-2x240.jpg",
                    VideoUrl = "https://rumble.com/v5u1v9b-the-system-is-panicking.html",
                    EmbedUrl = "https://rumble.com/embed/v5u1v9b/",
                    DurationSeconds = 1845,
                    ViewCount = "240K",
                    PublishedAt = DateTime.UtcNow.AddHours(-14).ToString("O"),
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                },
                new ChannelVideo
                {
                    Id = "v5ti8ox",
                    ChannelId = "rumble-redacted",
                    ChannelName = "Redacted",
                    Title = "They Tried To Hide This Intelligence Report From The Public",
                    Description = "Global intelligence briefings and live updates with the Redacted team.",
                    ThumbnailUrl = "https://ak2.rmbl.ws/s8/1/i/n/6/o/in6od.ba-thumb-in6od-2x240.jpg",
                    VideoUrl = "https://rumble.com/v5ti8ox-intelligence-report-exposed.html",
                    EmbedUrl = "https://rumble.com/embed/v5ti8ox/",
                    DurationSeconds = 2120,
                    ViewCount = "310K",
                    PublishedAt = DateTime.UtcNow.AddDays(-1).ToString("O"),
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                },
                new ChannelVideo
                {
                    Id = "v5twhd7",
                    ChannelId = "rumble-russellbrand",
                    ChannelName = "Russell Brand",
                    Title = "Is Everything We Were Told A Complete Lie? Stay Free",
                    Description = "Russell Brand breaks down breaking news, political rhetoric, and spiritual wellness.",
                    ThumbnailUrl = "https://ak2.rmbl.ws/s8/1/7/E/0/v/7E0vd.ba-thumb-7E0vd-2x240.jpg",
                    VideoUrl = "https://rumble.com/v5twhd7-stay-free-with-russell-brand.html",
                    EmbedUrl = "https://rumble.com/embed/v5twhd7/",
                    DurationSeconds = 3140,
                    ViewCount = "450K",
                    PublishedAt = DateTime.UtcNow.AddHours(-22).ToString("O"),
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                },
                new ChannelVideo
                {
                    Id = "v5t0739",
                    ChannelId = "rumble-ggreenwald",
                    ChannelName = "Glenn Greenwald",
                    Title = "System Update: The Censorship Industrial Complex Exposed",
                    Description = "Investigative journalist Glenn Greenwald presents critical evidence.",
                    ThumbnailUrl = "https://ak2.rmbl.ws/s8/1/0/j/8/m/0j8md.ba-thumb-0j8md-2x240.jpg",
                    VideoUrl = "https://rumble.com/v5t0739-system-update-censorship.html",
                    EmbedUrl = "https://rumble.com/embed/v5t0739/",
                    DurationSeconds = 2650,
                    ViewCount = "180K",
                    PublishedAt = DateTime.UtcNow.AddDays(-2).ToString("O"),
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                }
            };

            _ = Task.Run(async () =>
            {
                foreach (var r in rumbleDefaults)
                {
                    try { await SyncChannelVideosAsync(r.Id, 10, CancellationToken.None); } catch { }
                }
            });
        }

        // Always ensure sample Rumble broadcasts exist if feed is empty
        var existingRumbleVideos = await _db.GetChannelVideosAsync(platform: "Rumble");
        if (existingRumbleVideos.Count == 0)
        {
            var sampleRumbleVideos = new[]
            {
                new ChannelVideo
                {
                    Id = "v5u1v9b",
                    ChannelId = "rumble-redacted",
                    ChannelName = "Redacted",
                    Title = "The System Is Panicking About What We Just Uncovered",
                    Description = "Clayton and Natali Morris expose new findings in independent journalism.",
                    ThumbnailUrl = "https://ak2.rmbl.ws/s8/1/i/n/6/o/in6od.ba-thumb-in6od-2x240.jpg",
                    VideoUrl = "https://rumble.com/v5u1v9b-the-system-is-panicking.html",
                    EmbedUrl = "https://rumble.com/embed/v5u1v9b/",
                    DurationSeconds = 1845,
                    ViewCount = "240K",
                    PublishedAt = DateTime.UtcNow.AddHours(-14).ToString("O"),
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                },
                new ChannelVideo
                {
                    Id = "v5ti8ox",
                    ChannelId = "rumble-redacted",
                    ChannelName = "Redacted",
                    Title = "They Tried To Hide This Intelligence Report From The Public",
                    Description = "Global intelligence briefings and live updates with the Redacted team.",
                    ThumbnailUrl = "https://ak2.rmbl.ws/s8/1/i/n/6/o/in6od.ba-thumb-in6od-2x240.jpg",
                    VideoUrl = "https://rumble.com/v5ti8ox-intelligence-report-exposed.html",
                    EmbedUrl = "https://rumble.com/embed/v5ti8ox/",
                    DurationSeconds = 2120,
                    ViewCount = "310K",
                    PublishedAt = DateTime.UtcNow.AddDays(-1).ToString("O"),
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                },
                new ChannelVideo
                {
                    Id = "v5twhd7",
                    ChannelId = "rumble-russellbrand",
                    ChannelName = "Russell Brand",
                    Title = "Is Everything We Were Told A Complete Lie? Stay Free",
                    Description = "Russell Brand breaks down breaking news, political rhetoric, and spiritual wellness.",
                    ThumbnailUrl = "https://ak2.rmbl.ws/s8/1/7/E/0/v/7E0vd.ba-thumb-7E0vd-2x240.jpg",
                    VideoUrl = "https://rumble.com/v5twhd7-stay-free-with-russell-brand.html",
                    EmbedUrl = "https://rumble.com/embed/v5twhd7/",
                    DurationSeconds = 3140,
                    ViewCount = "450K",
                    PublishedAt = DateTime.UtcNow.AddHours(-22).ToString("O"),
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                },
                new ChannelVideo
                {
                    Id = "v5t0739",
                    ChannelId = "rumble-ggreenwald",
                    ChannelName = "Glenn Greenwald",
                    Title = "System Update: The Censorship Industrial Complex Exposed",
                    Description = "Investigative journalist Glenn Greenwald presents critical evidence.",
                    ThumbnailUrl = "https://ak2.rmbl.ws/s8/1/0/j/8/m/0j8md.ba-thumb-0j8md-2x240.jpg",
                    VideoUrl = "https://rumble.com/v5t0739-system-update-censorship.html",
                    EmbedUrl = "https://rumble.com/embed/v5t0739/",
                    DurationSeconds = 2650,
                    ViewCount = "180K",
                    PublishedAt = DateTime.UtcNow.AddDays(-2).ToString("O"),
                    Platform = "Rumble",
                    CreatedAt = DateTime.UtcNow
                }
            };

            foreach (var sv in sampleRumbleVideos)
            {
                await _db.SaveChannelVideoAsync(sv);
            }
        }
    }

    public async Task ResyncDownloadedVideosAsync()
    {
        if (!Directory.Exists(_videoLibraryRoot)) return;

        var files = Directory.GetFiles(_videoLibraryRoot, "*.mp4", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            // Check if any video has title matching
            var existingMatches = await _db.GetChannelVideosAsync(search: fileName, limit: 5);
            var match = existingMatches.FirstOrDefault(v => v.Title.Equals(fileName, StringComparison.OrdinalIgnoreCase) || 
                                                            fileName.Contains(v.Id, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                var fi = new FileInfo(file);
                await _db.UpdateVideoDownloadStatusAsync(match.Id, true, file, fi.Length);
            }
        }
    }

    private (string NormalizedUrl, string Platform) NormalizeChannelUrl(string input, string? preferredPlatform = null)
    {
        var clean = input.Trim();
        bool isRumble = clean.Contains("rumble.com", StringComparison.OrdinalIgnoreCase) || 
                        string.Equals(preferredPlatform, "Rumble", StringComparison.OrdinalIgnoreCase) ||
                        clean.StartsWith("rumble:", StringComparison.OrdinalIgnoreCase);

        if (isRumble)
        {
            clean = Regex.Replace(clean, @"^rumble:\s*", "", RegexOptions.IgnoreCase).Trim();
            if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (clean.StartsWith("rumble.com", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("www.rumble.com", StringComparison.OrdinalIgnoreCase))
                {
                    clean = "https://" + clean;
                }
                else if (clean.StartsWith("c/") || clean.StartsWith("user/"))
                {
                    clean = "https://rumble.com/" + clean;
                }
                else
                {
                    clean = $"https://rumble.com/c/{clean.TrimStart('@')}";
                }
            }
            return (clean, "Rumble");
        }

        if (clean.StartsWith("@"))
        {
            return ($"https://www.youtube.com/{clean}/videos", "YouTube");
        }
        if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (clean.StartsWith("youtube.com", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("www.youtube.com", StringComparison.OrdinalIgnoreCase))
            {
                clean = "https://" + clean;
            }
            else
            {
                clean = $"https://www.youtube.com/@{clean}/videos";
            }
        }
        if (!clean.EndsWith("/videos") && !clean.Contains("/watch"))
        {
            clean = clean.TrimEnd('/') + "/videos";
        }
        return (clean, "YouTube");
    }

    private string? ExtractHandle(string urlOrHandle)
    {
        if (urlOrHandle.Contains("@"))
        {
            var match = Regex.Match(urlOrHandle, @"@([a-zA-Z0-9_\-\.]+)");
            if (match.Success) return match.Value;
        }
        return null;
    }

    private string FormatFollowerCount(long count)
    {
        if (count >= 1_000_000) return $"{(count / 1_000_000.0):0.#}M";
        if (count >= 1_000) return $"{(count / 1_000.0):0.#}K";
        return count.ToString("N0");
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

    private async Task<string> EnsureYtDlpBinaryAsync()
    {
        var targetDir = Directory.Exists(_ytDlpCustomDir) ? _ytDlpCustomDir : @"C:\Apps\ytdlp";
        var fullYtDlpPath = Path.Combine(targetDir, "yt-dlp.exe");
        if (File.Exists(fullYtDlpPath)) return fullYtDlpPath;

        var localCandidate = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        if (File.Exists(localCandidate)) return localCandidate;

        return fullYtDlpPath;
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
}
