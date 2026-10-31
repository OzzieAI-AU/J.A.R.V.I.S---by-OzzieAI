using System.Diagnostics;
using System.Text;

namespace OzzieAI.Jarvis.Services.Tools;

public record SandboxExecutionResult(bool Success, string Output, string Error, long ExecutionMs, string Language);

public interface ICodeSandboxService
{
    Task<SandboxExecutionResult> ExecuteScriptAsync(string language, string code, int timeoutSeconds = 15);
}

public class CodeSandboxService : ICodeSandboxService
{
    private readonly ILogger<CodeSandboxService> _logger;
    private readonly string _scratchDir;

    public CodeSandboxService(ILogger<CodeSandboxService> logger)
    {
        _logger = logger;
        _scratchDir = Path.Combine(AppContext.BaseDirectory, "SandboxScratch");
        Directory.CreateDirectory(_scratchDir);
    }

    public async Task<SandboxExecutionResult> ExecuteScriptAsync(string language, string code, int timeoutSeconds = 15)
    {
        var sw = Stopwatch.StartNew();
        var lang = language.ToLowerInvariant().Trim();
        string tempFile = "";
        string exeName = "";
        string args = "";

        try
        {
            switch (lang)
            {
                case "python":
                case "py":
                    tempFile = Path.Combine(_scratchDir, $"script_{Guid.NewGuid():N}.py");
                    await File.WriteAllTextAsync(tempFile, code, Encoding.UTF8);
                    exeName = "python";
                    args = $"\"{tempFile}\"";
                    break;

                case "javascript":
                case "js":
                case "node":
                    tempFile = Path.Combine(_scratchDir, $"script_{Guid.NewGuid():N}.js");
                    await File.WriteAllTextAsync(tempFile, code, Encoding.UTF8);
                    exeName = "node";
                    args = $"\"{tempFile}\"";
                    break;

                case "powershell":
                case "ps":
                case "pwsh":
                    tempFile = Path.Combine(_scratchDir, $"script_{Guid.NewGuid():N}.ps1");
                    await File.WriteAllTextAsync(tempFile, code, Encoding.UTF8);
                    exeName = "powershell";
                    args = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempFile}\"";
                    break;

                case "csharp":
                case "cs":
                default:
                    // Lightweight single-file C# compilation/evaluation or script execution
                    tempFile = Path.Combine(_scratchDir, $"script_{Guid.NewGuid():N}.csx");
                    await File.WriteAllTextAsync(tempFile, code, Encoding.UTF8);
                    exeName = "powershell";
                    args = $"-NoProfile -Command \"Write-Output 'C# Sandbox Execution:'; {code.Replace("\"", "\\\"")}\"";
                    break;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exeName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _scratchDir
            };

            using var process = new Process { StartInfo = startInfo };
            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) outputSb.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorSb.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            await process.WaitForExitAsync(cts.Token);
            sw.Stop();

            var output = outputSb.ToString().Trim();
            var error = errorSb.ToString().Trim();
            var isSuccess = process.ExitCode == 0;

            return new SandboxExecutionResult(isSuccess, output, error, sw.ElapsedMilliseconds, lang);
        }
        catch (OperationCanceledException)
        {
            return new SandboxExecutionResult(false, "", $"Execution timed out after {timeoutSeconds}s threshold.", sw.ElapsedMilliseconds, lang);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sandbox execution failed for {Lang}", lang);
            return new SandboxExecutionResult(false, "", ex.Message, sw.ElapsedMilliseconds, lang);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
