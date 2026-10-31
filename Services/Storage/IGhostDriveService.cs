using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Storage;

public interface IGhostDriveService
{
    Task<List<GhostDriveInfo>> GetAllGhostDrivesAsync();
    Task<GhostDriveInfo?> GetGhostDriveAsync(string idOrName);
    Task<GhostDriveInfo> CreateGhostDriveAsync(GhostDriveInfo drive);
    Task<bool> DeleteGhostDriveAsync(string id);
    Task<List<DriveTelemetry>> GetStorageTelemetryAsync();
    Task<List<LowSpaceAlertEvent>> CheckLowSpaceAlertsAsync();
    Task<List<FileItemInfo>> ListFilesAsync(string pathOrGhostDriveId, string? subfolder = null);
    Task<string> ReadFileContentAsync(string pathOrGhostDriveId, string? relativePath = null);
    Task<bool> WriteFileContentAsync(string pathOrGhostDriveId, string relativePath, string content);
    Task<bool> DeleteFileAsync(string pathOrGhostDriveId, string relativePath);
}
