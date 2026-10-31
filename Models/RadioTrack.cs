namespace OzzieAI.Jarvis.Models;

public class RadioTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Unknown Track";
    public string Artist { get; set; } = "Unknown Artist";
    public string Album { get; set; } = "Stark Station Singles";
    public string Genre { get; set; } = "Rock"; // Classical, Rock, Pop, Hip-Hop / Rap, Electronic / EDM, R&B & Soul, Jazz, Blues, Country & Folk, Reggae & Caribbean, Latin
    public int DurationSeconds { get; set; } = 0;
    public string FilePath { get; set; } = "";
    public string Format { get; set; } = "mp3"; // mp3 or mp4
    public string? SourceUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public long FileSizeBytes { get; set; } = 0;
    public int PlayCount { get; set; } = 0;
    public DateTime? LastPlayedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record RadioRequestDto(string QueryOrUrl, string Format = "mp3", bool AutoPlay = true, string? Genre = null);
public record RadioAlbumRequestDto(string Artist, string? Album = null, int MaxTracks = 15, string? Genre = null, bool AutoPlay = true);
public record RadioControlDto(string Action, string? TrackId = null, double? Position = null, double? Volume = null, string? Genre = null, string? RepeatMode = null);
public record RadioEditTrackDto(string Title, string Artist, string Album, string? Genre = null);

public class RadioStatusResponse
{
    public RadioTrack? CurrentTrack { get; set; }
    public bool IsPlaying { get; set; }
    public double Volume { get; set; } = 1.0;
    public double PositionSeconds { get; set; } = 0;
    public bool IsShuffle { get; set; } = false;
    public string RepeatMode { get; set; } = "off"; // "off", "all", "one"
    public string? CurrentGenre { get; set; }
    public List<RadioTrack> Queue { get; set; } = new();
    public List<RadioTrack> History { get; set; } = new();
    public int TotalLibraryTracks { get; set; } = 0;
}

