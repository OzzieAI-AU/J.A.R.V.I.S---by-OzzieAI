namespace OzzieAI.Jarvis.Models;

public class YouTubeChannel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ChannelName { get; set; } = "Video Channel";
    public string? ChannelHandle { get; set; }
    public string ChannelUrl { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? Description { get; set; }
    public string? SubscriberCount { get; set; }
    public int VideoCount { get; set; } = 0;
    public string Platform { get; set; } = "YouTube"; // "YouTube" or "Rumble"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncedAt { get; set; }
}

public class ChannelVideo
{
    public string Id { get; set; } = ""; // YouTube Video ID or Rumble Video ID
    public string ChannelId { get; set; } = "";
    public string ChannelName { get; set; } = "Video Channel";
    public string Title { get; set; } = "Untitled Video";
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string VideoUrl { get; set; } = "";
    public string? EmbedUrl { get; set; }
    public int DurationSeconds { get; set; } = 0;
    public string? ViewCount { get; set; }
    public string? PublishedAt { get; set; }
    public bool IsDownloaded { get; set; } = false;
    public string? LocalFilePath { get; set; }
    public long FileSizeBytes { get; set; } = 0;
    public bool IsWatched { get; set; } = false;
    public DateTime? WatchedAt { get; set; }
    public string Platform { get; set; } = "YouTube"; // "YouTube" or "Rumble"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MovieItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Untitled Movie";
    public string? Year { get; set; }
    public string FilePath { get; set; } = "";
    public string? RelativePath { get; set; }
    public string? PosterUrl { get; set; }
    public int DurationSeconds { get; set; } = 0;
    public string? Resolution { get; set; } // "4K UHD", "1080p HD", "720p", "SD"
    public long FileSizeBytes { get; set; } = 0;
    public string? Format { get; set; } // "mp4", "mkv", "avi", "webm", etc.
    public bool IsWatched { get; set; } = false;
    public DateTime? WatchedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record AddChannelRequestDto(string ChannelUrlOrHandle, string? Platform = null);
public record DownloadVideoRequestDto(string VideoId, string? Quality = "best");
public record ToggleWatchedRequestDto(string VideoId, bool IsWatched);
public record VideoFeedQueryDto(string? ChannelId = null, string? Search = null, bool OnlyDownloaded = false, bool? OnlyUnwatched = null, string? Platform = null, int Limit = 100);

public record ToggleMovieWatchedRequestDto(string MovieId, bool IsWatched);
public record MovieQueryDto(string? Search = null, bool? OnlyUnwatched = null, int Limit = 100);
