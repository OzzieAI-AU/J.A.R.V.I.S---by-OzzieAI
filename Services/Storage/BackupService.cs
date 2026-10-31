using System.IO.Compression;

namespace OzzieAI.Jarvis.Services.Storage;

public record SnapshotItem(string Id, string FileName, long SizeBytes, DateTime CreatedAt, string Description);

public interface IBackupService
{
    Task<SnapshotItem> CreateSnapshotAsync(string description = "Manual Stark Snapshot");
    Task<List<SnapshotItem>> GetSnapshotsAsync();
    Task<bool> RestoreSnapshotAsync(string snapshotId);
}

public class BackupService : IBackupService
{
    private readonly ILogger<BackupService> _logger;
    private readonly string _snapshotDir;
    private readonly string _dbPath;
    private readonly string _ghostDriveDir;

    public BackupService(IConfiguration config, ILogger<BackupService> logger)
    {
        _logger = logger;
        _snapshotDir = Path.Combine(AppContext.BaseDirectory, "Snapshots");
        _dbPath = config.GetValue<string>("DatabasePath") ?? "jarvis_data.db";
        _ghostDriveDir = Path.Combine(AppContext.BaseDirectory, "GhostDrives");
        Directory.CreateDirectory(_snapshotDir);
    }

    public async Task<SnapshotItem> CreateSnapshotAsync(string description = "Manual Stark Snapshot")
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"stark_snapshot_{timestamp}_{id}.zip";
        var zipPath = Path.Combine(_snapshotDir, fileName);

        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            if (File.Exists(_dbPath))
            {
                zip.CreateEntryFromFile(_dbPath, "jarvis_data.db", CompressionLevel.Optimal);
            }
            if (Directory.Exists(_ghostDriveDir))
            {
                foreach (var file in Directory.EnumerateFiles(_ghostDriveDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(_ghostDriveDir, file);
                    zip.CreateEntryFromFile(file, Path.Combine("GhostDrives", rel), CompressionLevel.Optimal);
                }
            }
        });

        var fileInfo = new FileInfo(zipPath);
        _logger.LogInformation("Stark snapshot created: {File} ({Size} bytes)", fileName, fileInfo.Length);

        return new SnapshotItem(id, fileName, fileInfo.Length, DateTime.UtcNow, description);
    }

    public Task<List<SnapshotItem>> GetSnapshotsAsync()
    {
        var list = new List<SnapshotItem>();
        if (!Directory.Exists(_snapshotDir)) return Task.FromResult(list);

        foreach (var file in Directory.EnumerateFiles(_snapshotDir, "stark_snapshot_*.zip"))
        {
            var info = new FileInfo(file);
            var name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('_');
            var id = parts.Length >= 4 ? parts[3] : name;
            list.Add(new SnapshotItem(id, info.Name, info.Length, info.CreationTimeUtc, "Automated Snapshot"));
        }

        return Task.FromResult(list.OrderByDescending(x => x.CreatedAt).ToList());
    }

    public Task<bool> RestoreSnapshotAsync(string snapshotId)
    {
        var file = Directory.EnumerateFiles(_snapshotDir, $"*_{snapshotId}.zip").FirstOrDefault();
        if (file == null || !File.Exists(file)) return Task.FromResult(false);

        try
        {
            using var zip = ZipFile.OpenRead(file);
            var dbEntry = zip.GetEntry("jarvis_data.db");
            if (dbEntry != null)
            {
                var backupExisting = $"{_dbPath}.bak";
                if (File.Exists(_dbPath)) File.Copy(_dbPath, backupExisting, true);
                dbEntry.ExtractToFile(_dbPath, true);
            }
            _logger.LogInformation("Stark snapshot restored: {Id}", snapshotId);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore snapshot {Id}", snapshotId);
            return Task.FromResult(false);
        }
    }
}
