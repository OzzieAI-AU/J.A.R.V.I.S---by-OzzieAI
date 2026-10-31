using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Media;

public interface IVideoPlayerService
{
    Task<List<YouTubeChannel>> GetChannelsAsync(string? platform = null);
    Task<YouTubeChannel> AddChannelAsync(string channelUrlOrHandle, string? platform = null, CancellationToken ct = default);
    Task<bool> DeleteChannelAsync(string channelId);
    Task<int> SyncChannelVideosAsync(string channelId, int maxVideos = 15, CancellationToken ct = default);
    Task<List<ChannelVideo>> GetRecommendedVideosAsync(string? channelId = null, string? search = null, bool onlyDownloaded = false, bool? onlyUnwatched = null, string? platform = null, int limit = 100);
    Task<ChannelVideo?> GetVideoByIdAsync(string videoId);
    Task<ChannelVideo?> SetVideoWatchedStatusAsync(string videoId, bool isWatched);
    Task<ChannelVideo> DownloadVideoAsync(string videoId, CancellationToken ct = default);
    Task<string?> GetVideoFilePathAsync(string videoId);
    Task EnsureDefaultChannelsSeededAsync();
    Task ResyncDownloadedVideosAsync();
}
