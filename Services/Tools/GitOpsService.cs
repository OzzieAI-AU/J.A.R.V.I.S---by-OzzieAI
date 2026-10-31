using System.Diagnostics;
using System.Text;

namespace OzzieAI.Jarvis.Services.Tools;

public interface IGitOpsService
{
    Task<string> GetStatusAsync(string? repoPath = null);
    Task<string> GetDiffAsync(string? repoPath = null);
    Task<string> GetRecentCommitsAsync(int count = 5, string? repoPath = null);
    Task<string> CommitChangesAsync(string message, string? repoPath = null);
}

public class GitOpsService : IGitOpsService
{
    private readonly ILogger<GitOpsService> _logger;

    public GitOpsService(ILogger<GitOpsService> logger)
    {
        _logger = logger;
    }

    public async Task<string> GetStatusAsync(string? repoPath = null)
    {
        return await RunGitCommandAsync("status --short", repoPath);
    }

    public async Task<string> GetDiffAsync(string? repoPath = null)
    {
        var diff = await RunGitCommandAsync("diff --stat", repoPath);
        return string.IsNullOrWhiteSpace(diff) ? "Working tree clean. No unstaged diffs." : diff;
    }

    public async Task<string> GetRecentCommitsAsync(int count = 5, string? repoPath = null)
    {
        return await RunGitCommandAsync($"log -n {count} --oneline --decorate", repoPath);
    }

    public async Task<string> CommitChangesAsync(string message, string? repoPath = null)
    {
        if (string.IsNullOrWhiteSpace(message)) message = "Autonomous Stark J.A.R.V.I.S. sync";
        var addRes = await RunGitCommandAsync("add .", repoPath);
        var commitRes = await RunGitCommandAsync($"commit -m \"{message.Replace("\"", "\\\"")}\"", repoPath);
        return $"Git Operation Result:\n{commitRes}";
    }

    private static async Task<string> RunGitCommandAsync(string arguments, string? workingDir = null)
    {
        var dir = workingDir ?? Directory.GetCurrentDirectory();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var outText = await proc.StandardOutput.ReadToEndAsync();
            var errText = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return string.IsNullOrWhiteSpace(outText) ? errText.Trim() : outText.Trim();
        }
        catch (Exception ex)
        {
            return $"Git command execution failed: {ex.Message}";
        }
    }
}
