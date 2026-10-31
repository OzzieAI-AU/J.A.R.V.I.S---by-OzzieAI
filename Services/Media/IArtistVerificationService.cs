using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Media;

public record VerifiedMusicMetadata(
    string Artist,
    string Album,
    bool IsSingle,
    string TrackTitle,
    string Genre,
    string CanonicalDirectoryName,
    double Confidence
);

public record VerifiedHarvestIntent(
    bool IsHarvestRequest,
    string? Artist,
    string? Album,
    bool AllAlbums,
    bool IncludeSingles,
    string? ClarificationNeeded
);

public interface IArtistVerificationService
{
    /// <summary>
    /// Uses LLM to parse a user voice/chat directive to accurately verify the artist, album, and harvest scope.
    /// Never falls back to 'Albums' or dummy names. If unspecified, clarifies or uses active context.
    /// </summary>
    Task<VerifiedHarvestIntent> ParseUserHarvestIntentAsync(string userPrompt, string? currentlyPlayingArtist = null, CancellationToken ct = default);

    /// <summary>
    /// Uses LLM to verify and standardize candidate metadata extracted from YouTube / yt-dlp.
    /// Guarantees canonical artist name, correct album vs singles classification, clean track title, and umbrella genre.
    /// </summary>
    Task<VerifiedMusicMetadata> VerifyTrackMetadataAsync(string rawTitle, string? uploaderOrChannel, string? candidateArtist, string? candidateAlbum, string? expectedArtist, string libraryRootPath, CancellationToken ct = default);

    /// <summary>
    /// Scans the library root path and returns the canonical existing directory name for an artist if one exists,
    /// preventing double-ups (e.g. 'AC DC' vs 'AC_DC', 'Queen' vs 'Queens').
    /// </summary>
    string ResolveCanonicalArtistDirectory(string candidateArtist, string libraryRootPath);

    /// <summary>
    /// Cleans up any existing double-ups or rogue directories ('Albums', 'Ac Dc', 'Queens') on disk and in DB.
    /// </summary>
    Task<int> ConsolidateAndHealLibraryAsync(string libraryRootPath, CancellationToken ct = default);
}
