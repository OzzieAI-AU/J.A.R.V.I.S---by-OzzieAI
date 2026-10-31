namespace OzzieAI.Jarvis.Models;

public class GhostDriveInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "STARK-VAULT";
    public string MountPath { get; set; } = string.Empty;
    public string VirtualTag { get; set; } = "GHOST-V1";
    public long MaxSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024; // 10 GB
    public long UsedBytes { get; set; } = 0;
    public long FreeBytes => Math.Max(0, MaxSizeBytes - UsedBytes);
    public double PercentUsed => MaxSizeBytes > 0 ? (double)UsedBytes / MaxSizeBytes * 100.0 : 0;
    public bool IsActive { get; set; } = true;
    public string Description { get; set; } = "Holographic Virtual Mount";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DriveTelemetry
{
    public string DriveName { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string DriveFormat { get; set; } = string.Empty;
    public double TotalGb { get; set; }
    public double FreeGb { get; set; }
    public double UsedGb { get; set; }
    public double PercentFree { get; set; }
    public bool IsLowSpace { get; set; }
    public string Status { get; set; } = "Nominal";
    public bool IsGhostDrive { get; set; }
}

public class FileItemInfo
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = string.Empty;
}

public class LowSpaceAlertEvent
{
    public string DriveName { get; set; } = string.Empty;
    public double RemainingGb { get; set; }
    public double PercentFree { get; set; }
    public string AlertMessage { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
