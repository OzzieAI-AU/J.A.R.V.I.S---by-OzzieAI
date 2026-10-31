using System.Globalization;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Storage;

public class GhostDriveService : IGhostDriveService
{
    private readonly JarvisDatabase _database;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<GhostDriveService> _logger;
    private readonly string _defaultStorageRoot;

    public GhostDriveService(
        JarvisDatabase database,
        IJarvisConfigService configService,
        IWebHostEnvironment env,
        ILogger<GhostDriveService> logger)
    {
        _database = database;
        _configService = configService;
        _logger = logger;
        _defaultStorageRoot = Path.Combine(env.ContentRootPath, "GhostDrives");
        Directory.CreateDirectory(_defaultStorageRoot);

        // Ensure default drives exist
        _ = EnsureDefaultGhostDrivesAsync();
    }

    private async Task EnsureDefaultGhostDrivesAsync()
    {
        try
        {
            var drives = await GetAllGhostDrivesAsync();
            if (drives.Count == 0)
            {
                var vaultPath = Path.Combine(_defaultStorageRoot, "STARK-VAULT");
                Directory.CreateDirectory(vaultPath);
                await File.WriteAllTextAsync(Path.Combine(vaultPath, "README.txt"), "STARK-VAULT Encrypted Ghost Drive initialized.\nAccessible by J.A.R.V.I.S. Neural Core.");

                await CreateGhostDriveAsync(new GhostDriveInfo
                {
                    Name = "STARK-VAULT",
                    MountPath = vaultPath,
                    VirtualTag = "GHOST-V1",
                    MaxSizeBytes = 20L * 1024 * 1024 * 1024, // 20 GB
                    Description = "Primary Classified Stark Industries Virtual Mount"
                });

                var archivePath = Path.Combine(_defaultStorageRoot, "MARK-ARCHIVE");
                Directory.CreateDirectory(archivePath);
                await File.WriteAllTextAsync(Path.Combine(archivePath, "suit_telemetry_manifest.json"), "{\"manifest\": \"Mark 85 Armor Schematics & Flight Logs\"}");

                await CreateGhostDriveAsync(new GhostDriveInfo
                {
                    Name = "MARK-ARCHIVE",
                    MountPath = archivePath,
                    VirtualTag = "GHOST-V2",
                    MaxSizeBytes = 10L * 1024 * 1024 * 1024, // 10 GB
                    Description = "Armor Blueprints & Suit Telemetry Archive"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring default ghost drives");
        }
    }

    public async Task<List<GhostDriveInfo>> GetAllGhostDrivesAsync()
    {
        var list = new List<GhostDriveInfo>();
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, MountPath, VirtualTag, MaxSizeBytes, IsActive, Description, CreatedAt FROM GhostDrives ORDER BY CreatedAt ASC";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var drive = new GhostDriveInfo
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    MountPath = reader.GetString(2),
                    VirtualTag = reader.GetString(3),
                    MaxSizeBytes = reader.GetInt64(4),
                    IsActive = reader.GetInt32(5) == 1,
                    Description = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    CreatedAt = DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind)
                };

                // Calculate used bytes from folder
                if (Directory.Exists(drive.MountPath))
                {
                    try
                    {
                        var dir = new DirectoryInfo(drive.MountPath);
                        drive.UsedBytes = dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                    }
                    catch {}
                }

                list.Add(drive);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing ghost drives");
        }
        return list;
    }

    public async Task<GhostDriveInfo?> GetGhostDriveAsync(string idOrName)
    {
        var all = await GetAllGhostDrivesAsync();
        return all.FirstOrDefault(d => d.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || 
                                       d.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
                                       d.VirtualTag.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<GhostDriveInfo> CreateGhostDriveAsync(GhostDriveInfo drive)
    {
        if (string.IsNullOrWhiteSpace(drive.Id)) drive.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(drive.MountPath))
        {
            drive.MountPath = Path.Combine(_defaultStorageRoot, drive.Name);
        }

        Directory.CreateDirectory(drive.MountPath);

        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO GhostDrives (Id, Name, MountPath, VirtualTag, MaxSizeBytes, IsActive, Description, CreatedAt)
                VALUES ($id, $name, $path, $tag, $max, $active, $desc, $created)
            ";
            cmd.Parameters.AddWithValue("$id", drive.Id);
            cmd.Parameters.AddWithValue("$name", drive.Name);
            cmd.Parameters.AddWithValue("$path", drive.MountPath);
            cmd.Parameters.AddWithValue("$tag", drive.VirtualTag);
            cmd.Parameters.AddWithValue("$max", drive.MaxSizeBytes);
            cmd.Parameters.AddWithValue("$active", drive.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$desc", drive.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$created", drive.CreatedAt.ToString("o"));

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Registered Ghost Drive {Name} at {Path}", drive.Name, drive.MountPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating ghost drive {Name}", drive.Name);
            throw;
        }

        return drive;
    }

    public async Task<bool> DeleteGhostDriveAsync(string id)
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM GhostDrives WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting ghost drive {Id}", id);
            return false;
        }
    }

    public async Task<List<DriveTelemetry>> GetStorageTelemetryAsync()
    {
        var list = new List<DriveTelemetry>();
        var cfg = _configService.GetConfig();

        // 1. Physical Host Drives
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                var freeGb = d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                var usedGb = totalGb - freeGb;
                var pctFree = totalGb > 0 ? (freeGb / totalGb) * 100.0 : 0;

                var isLow = pctFree < cfg.Storage.LowSpaceAlertThresholdPercent || freeGb < cfg.Storage.LowSpaceAlertThresholdGb;

                list.Add(new DriveTelemetry
                {
                    DriveName = d.Name,
                    VolumeLabel = !string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.VolumeLabel : "Local Disk",
                    DriveFormat = d.DriveFormat,
                    TotalGb = Math.Round(totalGb, 1),
                    FreeGb = Math.Round(freeGb, 1),
                    UsedGb = Math.Round(usedGb, 1),
                    PercentFree = Math.Round(pctFree, 1),
                    IsLowSpace = isLow,
                    Status = isLow ? "CRITICAL: LOW CAPACITY" : "Nominal",
                    IsGhostDrive = false
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting physical drive telemetry");
        }

        // 2. Ghost Virtual Drives
        var ghostDrives = await GetAllGhostDrivesAsync();
        foreach (var gd in ghostDrives)
        {
            var totalGb = gd.MaxSizeBytes / (1024.0 * 1024.0 * 1024.0);
            var usedGb = gd.UsedBytes / (1024.0 * 1024.0 * 1024.0);
            var freeGb = Math.Max(0, totalGb - usedGb);
            var pctFree = totalGb > 0 ? (freeGb / totalGb) * 100.0 : 0;
            var isLow = pctFree < cfg.Storage.LowSpaceAlertThresholdPercent;

            list.Add(new DriveTelemetry
            {
                DriveName = $"[{gd.VirtualTag}] {gd.Name}",
                VolumeLabel = "Stark Holographic Partition",
                DriveFormat = "VIRTUAL-FS",
                TotalGb = Math.Round(totalGb, 1),
                FreeGb = Math.Round(freeGb, 1),
                UsedGb = Math.Round(usedGb, 2),
                PercentFree = Math.Round(pctFree, 1),
                IsLowSpace = isLow,
                Status = isLow ? "CRITICAL: GHOST DRIVE FULL" : "Active & Mounted",
                IsGhostDrive = true
            });
        }

        return list;
    }

    public async Task<List<LowSpaceAlertEvent>> CheckLowSpaceAlertsAsync()
    {
        var alerts = new List<LowSpaceAlertEvent>();
        var telemetry = await GetStorageTelemetryAsync();

        foreach (var d in telemetry)
        {
            if (d.IsLowSpace)
            {
                alerts.Add(new LowSpaceAlertEvent
                {
                    DriveName = d.DriveName,
                    RemainingGb = d.FreeGb,
                    PercentFree = d.PercentFree,
                    AlertMessage = $"Warning Sir: Drive '{d.DriveName}' is operating at critically low capacity. Only {d.FreeGb:F1} GB ({d.PercentFree:F1}%) remaining."
                });
            }
        }

        return alerts;
    }

    public async Task<List<FileItemInfo>> ListFilesAsync(string pathOrGhostDriveId, string? subfolder = null)
    {
        var folder = await ResolvePathAsync(pathOrGhostDriveId);
        if (!string.IsNullOrWhiteSpace(subfolder))
        {
            folder = Path.Combine(folder, subfolder);
        }

        var results = new List<FileItemInfo>();
        if (!Directory.Exists(folder)) return results;

        var dirInfo = new DirectoryInfo(folder);

        // Directories
        foreach (var d in dirInfo.EnumerateDirectories())
        {
            results.Add(new FileItemInfo
            {
                Name = d.Name,
                FullPath = d.FullName,
                RelativePath = Path.GetRelativePath(folder, d.FullName),
                IsDirectory = true,
                SizeBytes = 0,
                LastModified = d.LastWriteTimeUtc,
                Extension = "[DIR]"
            });
        }

        // Files
        foreach (var f in dirInfo.EnumerateFiles())
        {
            results.Add(new FileItemInfo
            {
                Name = f.Name,
                FullPath = f.FullName,
                RelativePath = Path.GetRelativePath(folder, f.FullName),
                IsDirectory = false,
                SizeBytes = f.Length,
                LastModified = f.LastWriteTimeUtc,
                Extension = f.Extension
            });
        }

        return results;
    }

    public async Task<string> ReadFileContentAsync(string pathOrGhostDriveId, string? relativePath = null)
    {
        var targetPath = await ResolvePathAsync(pathOrGhostDriveId);
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            targetPath = Path.Combine(targetPath, relativePath);
        }

        if (File.Exists(targetPath))
        {
            return await File.ReadAllTextAsync(targetPath);
        }

        throw new FileNotFoundException($"File not found at: {targetPath}");
    }

    public async Task<bool> WriteFileContentAsync(string pathOrGhostDriveId, string relativePath, string content)
    {
        var basePath = await ResolvePathAsync(pathOrGhostDriveId);
        var targetFile = Path.Combine(basePath, relativePath);

        var parent = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

        await File.WriteAllTextAsync(targetFile, content);
        return true;
    }

    public async Task<bool> DeleteFileAsync(string pathOrGhostDriveId, string relativePath)
    {
        var basePath = await ResolvePathAsync(pathOrGhostDriveId);
        var targetFile = Path.Combine(basePath, relativePath);

        if (File.Exists(targetFile))
        {
            File.Delete(targetFile);
            return true;
        }

        return false;
    }

    private async Task<string> ResolvePathAsync(string pathOrGhostDriveId)
    {
        // Check if it's an ID or name of a Ghost Drive
        var gd = await GetGhostDriveAsync(pathOrGhostDriveId);
        if (gd != null) return gd.MountPath;

        // Otherwise treat as direct path
        if (Path.IsPathRooted(pathOrGhostDriveId)) return pathOrGhostDriveId;
        return Path.Combine(_defaultStorageRoot, pathOrGhostDriveId);
    }
}
