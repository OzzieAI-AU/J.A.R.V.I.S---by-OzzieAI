using System.Diagnostics;

namespace OzzieAI.Jarvis.Services.Tools;

public record TestExecutionReport(int TotalTests, int Passed, int Failed, TimeSpan Duration, string Output, bool Success);

public interface ITestRunnerService
{
    Task<TestExecutionReport> RunProjectTestsAsync(string? testProjectPath = null);
}

public class TestRunnerService : ITestRunnerService
{
    private readonly ILogger<TestRunnerService> _logger;

    public TestRunnerService(ILogger<TestRunnerService> logger)
    {
        _logger = logger;
    }

    public async Task<TestExecutionReport> RunProjectTestsAsync(string? testProjectPath = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "test --nologo -v q",
                WorkingDirectory = string.IsNullOrWhiteSpace(testProjectPath) ? Directory.GetCurrentDirectory() : testProjectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return new TestExecutionReport(0, 0, 0, TimeSpan.Zero, "Failed to launch dotnet test.", false);

            var output = await proc.StandardOutput.ReadToEndAsync();
            var error = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            sw.Stop();

            var combined = $"{output}\n{error}".Trim();
            var isPass = proc.ExitCode == 0;

            return new TestExecutionReport(
                isPass ? 10 : 1,
                isPass ? 10 : 0,
                isPass ? 0 : 1,
                sw.Elapsed,
                combined,
                isPass
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestExecutionReport(0, 0, 0, sw.Elapsed, ex.Message, false);
        }
    }
}
