using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Hubs;
using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Media;

public class MovieVaultService : IMovieVaultService
{
    private readonly JarvisDatabase _db;
    private readonly IHubContext<JarvisHub> _hub;
    private readonly ILogger<MovieVaultService> _logger;
    private readonly string _libraryRootPath;

    public string LibraryRootPath => _libraryRootPath;

    public MovieVaultService(JarvisDatabase db, IHubContext<JarvisHub> hub, ILogger<MovieVaultService> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;

        _libraryRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "JarvisMovies"
        );

        if (!Directory.Exists(_libraryRootPath))
        {
            Directory.CreateDirectory(_libraryRootPath);
        }

        // Background startup sync
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureSampleMoviesIfEmptyAsync();
                await ResyncLibraryFromDiskAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background MovieVaultService startup sync encountered an issue.");
            }
        });
    }

    public async Task<List<MovieItem>> GetMoviesAsync(string? search = null, bool? onlyUnwatched = null, int limit = 100)
    {
        return await _db.GetMoviesAsync(search, onlyUnwatched, limit);
    }

    public async Task<MovieItem?> GetMovieByIdAsync(string id)
    {
        return await _db.GetMovieByIdAsync(id);
    }

    public async Task<MovieItem?> ToggleWatchedAsync(string movieId, bool isWatched)
    {
        var success = await _db.UpdateMovieWatchedStatusAsync(movieId, isWatched);
        if (!success) return null;

        var movie = await _db.GetMovieByIdAsync(movieId);
        if (movie != null)
        {
            await _hub.Clients.All.SendAsync("OnMovieWatchedChanged", new
            {
                movieId = movie.Id,
                isWatched = movie.IsWatched,
                watchedAt = movie.WatchedAt
            });
        }
        return movie;
    }

    public async Task<string?> GetMovieFilePathAsync(string id)
    {
        var movie = await _db.GetMovieByIdAsync(id);
        if (movie != null && File.Exists(movie.FilePath))
        {
            return movie.FilePath;
        }
        return null;
    }

    public async Task<string?> GetMoviePosterPathAsync(string id)
    {
        var movie = await _db.GetMovieByIdAsync(id);
        if (movie == null || string.IsNullOrWhiteSpace(movie.PosterUrl)) return null;

        if (File.Exists(movie.PosterUrl))
        {
            return movie.PosterUrl;
        }

        // Check companion poster in movie directory
        if (File.Exists(movie.FilePath))
        {
            var dir = Path.GetDirectoryName(movie.FilePath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                var posterFiles = Directory.GetFiles(dir, "*poster*.*")
                    .Concat(Directory.GetFiles(dir, "*cover*.*"))
                    .Concat(Directory.GetFiles(dir, "*folder*.*"));
                var found = posterFiles.FirstOrDefault();
                if (found != null && File.Exists(found)) return found;
            }
        }

        return null;
    }

    public async Task<bool> DeleteMovieAsync(string id)
    {
        var movie = await _db.GetMovieByIdAsync(id);
        if (movie == null) return false;

        // Note: We remove the record from vault database; user files are preserved unless explicitly deleted
        return await _db.DeleteMovieAsync(id);
    }

    public async Task<int> ResyncLibraryFromDiskAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_libraryRootPath))
        {
            Directory.CreateDirectory(_libraryRootPath);
            return 0;
        }

        _logger.LogInformation("Scanning Movie Vault directory at: {Path}", _libraryRootPath);

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".webm", ".mov", ".m4v"
        };

        var allFiles = Directory.GetFiles(_libraryRootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .ToList();

        int syncedCount = 0;
        foreach (var file in allFiles)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var fi = new FileInfo(file);
                var fileName = Path.GetFileNameWithoutExtension(file);
                var parentDir = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";

                // Parse title, year, resolution
                var parsed = ParseMovieMetadata(fileName, parentDir);

                // Look for companion poster
                var posterPath = FindPosterImage(Path.GetDirectoryName(file)!, fileName);

                var relativePath = Path.GetRelativePath(_libraryRootPath, file);

                var movie = new MovieItem
                {
                    Id = GenerateDeterministicId(file),
                    Title = parsed.Title,
                    Year = parsed.Year,
                    FilePath = file,
                    RelativePath = relativePath,
                    PosterUrl = posterPath,
                    DurationSeconds = 0, // In standard media scanning, player extracts exact duration on play
                    Resolution = parsed.Resolution ?? "1080p HD",
                    FileSizeBytes = fi.Length,
                    Format = Path.GetExtension(file).TrimStart('.').ToLowerInvariant(),
                    CreatedAt = fi.CreationTimeUtc
                };

                await _db.SaveMovieAsync(movie);
                syncedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed scanning movie file: {File}", file);
            }
        }

        await _hub.Clients.All.SendAsync("OnMoviesUpdated", ct);
        _logger.LogInformation("Movie Vault resync complete. Synced {Count} movies.", syncedCount);
        return syncedCount;
    }

    private static (string Title, string? Year, string? Resolution) ParseMovieMetadata(string fileName, string parentDirName)
    {
        // Try parent directory first if it looks like "Movie Name (Year)"
        var candidate = parentDirName.Contains("(") || parentDirName.Contains("19") || parentDirName.Contains("20") 
            ? parentDirName 
            : fileName;

        string? resolution = null;
        if (Regex.IsMatch(candidate, @"4k|2160p|uhd", RegexOptions.IgnoreCase) || Regex.IsMatch(fileName, @"4k|2160p|uhd", RegexOptions.IgnoreCase))
        {
            resolution = "4K UHD";
        }
        else if (Regex.IsMatch(candidate, @"1080p|fhd", RegexOptions.IgnoreCase) || Regex.IsMatch(fileName, @"1080p|fhd", RegexOptions.IgnoreCase))
        {
            resolution = "1080p HD";
        }
        else if (Regex.IsMatch(candidate, @"720p|hd", RegexOptions.IgnoreCase) || Regex.IsMatch(fileName, @"720p|hd", RegexOptions.IgnoreCase))
        {
            resolution = "720p HD";
        }
        else
        {
            resolution = "1080p HD";
        }

        string? year = null;
        var yearMatch = Regex.Match(candidate, @"\b(19\d\d|20\d\d)\b");
        if (yearMatch.Success)
        {
            year = yearMatch.Value;
        }

        // Clean up title: remove year, resolution, tags
        var cleanTitle = candidate;
        if (!string.IsNullOrEmpty(year))
        {
            var idx = cleanTitle.IndexOf(year, StringComparison.Ordinal);
            if (idx > 0)
            {
                cleanTitle = cleanTitle.Substring(0, idx);
            }
        }

        // Remove junk patterns like [1080p], .BluRay., .x264, etc.
        cleanTitle = Regex.Replace(cleanTitle, @"[._]", " ");
        cleanTitle = Regex.Replace(cleanTitle, @"[\(\[\{].*?[\)\]\}]", "");
        cleanTitle = Regex.Replace(cleanTitle, @"\b(1080p|720p|2160p|4k|bluray|web-dl|x264|x265|hevc|dvdrip|brrip)\b", "", RegexOptions.IgnoreCase);
        cleanTitle = cleanTitle.Trim('-', ' ', '.', '(', ')', '[', ']');

        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            cleanTitle = fileName;
        }

        return (cleanTitle, year, resolution);
    }

    private static string? FindPosterImage(string dir, string fileName)
    {
        if (!Directory.Exists(dir)) return null;

        var posterNames = new[] { "poster.jpg", "poster.png", "cover.jpg", "cover.png", "folder.jpg", $"{fileName}.jpg", $"{fileName}.png" };
        foreach (var p in posterNames)
        {
            var path = Path.Combine(dir, p);
            if (File.Exists(path)) return path;
        }

        // Look for any image file in that directory
        var imgFiles = Directory.GetFiles(dir, "*.jpg").Concat(Directory.GetFiles(dir, "*.png")).ToList();
        return imgFiles.FirstOrDefault();
    }

    private static string GenerateDeterministicId(string filePath)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
        return new Guid(hash).ToString();
    }

    private async Task EnsureSampleMoviesIfEmptyAsync()
    {
        // If the library folder is completely empty, populate sample starter directories with metadata
        var files = Directory.GetFiles(_libraryRootPath, "*.*", SearchOption.AllDirectories);
        if (files.Length > 0) return;

        var sampleMovies = new[]
        {
            new { Title = "Iron Man", Year = "2008", Res = "4K UHD" },
            new { Title = "The Matrix", Year = "1999", Res = "1080p HD" },
            new { Title = "Interstellar", Year = "2014", Res = "4K UHD" }
        };

        foreach (var s in sampleMovies)
        {
            var dir = Path.Combine(_libraryRootPath, $"{s.Title} ({s.Year})");
            Directory.CreateDirectory(dir);

            var dummyMp4 = Path.Combine(dir, $"{s.Title} ({s.Year}).mp4");
            if (!File.Exists(dummyMp4))
            {
                // Write a lightweight placeholder MP4 container header or info file
                await File.WriteAllTextAsync(Path.Combine(dir, "README_STARK_VAULT.txt"), 
                    $"Stark Movie Vault Organization Guide\nTitle: {s.Title}\nYear: {s.Year}\nDrop your .mp4, .mkv, or .avi file here along with an optional poster.jpg!");
            }
        }
    }
}
