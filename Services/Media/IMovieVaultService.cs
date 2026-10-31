using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Media;

public interface IMovieVaultService
{
    string LibraryRootPath { get; }
    Task<List<MovieItem>> GetMoviesAsync(string? search = null, bool? onlyUnwatched = null, int limit = 100);
    Task<MovieItem?> GetMovieByIdAsync(string id);
    Task<MovieItem?> ToggleWatchedAsync(string movieId, bool isWatched);
    Task<int> ResyncLibraryFromDiskAsync(CancellationToken ct = default);
    Task<string?> GetMovieFilePathAsync(string id);
    Task<string?> GetMoviePosterPathAsync(string id);
    Task<bool> DeleteMovieAsync(string id);
}
