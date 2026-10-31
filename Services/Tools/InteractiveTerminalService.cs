using System.Diagnostics;
using System.Text;

namespace OzzieAI.Jarvis.Services.Tools;

public record TerminalExecResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration);

public interface IInteractiveTerminalService
{
    Task<TerminalExecResult> ExecuteTerminalCommandAsync(string command, string? workingDir = null);
}

public class InteractiveTerminalService : IInteractiveTerminalService
{
    private readonly ILogger<InteractiveTerminalService> _logger;

    public InteractiveTerminalService(ILogger<InteractiveTerminalService> logger)
    {
        _logger = logger;
    }

    public async Task<TerminalExecResult> ExecuteTerminalCommandAsync(string command, string? workingDir = null)
    {
        var sw = Stopwatch.StartNew();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"",
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDir) ? Directory.GetCurrentDirectory() : workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await proc.WaitForExitAsync(cts.Token);
            sw.Stop();

            return new TerminalExecResult(proc.ExitCode, stdout.ToString(), stderr.ToString(), sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Interactive terminal command failed: {Command}", command);
            return new TerminalExecResult(-1, stdout.ToString(), ex.Message, sw.Elapsed);
        }
    }
}
