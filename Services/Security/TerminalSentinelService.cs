using System.Text.RegularExpressions;

namespace OzzieAI.Jarvis.Services.Security;

public record CommandAuditResult(bool IsSafe, string RiskLevel, string Reason, string? ChallengeToken = null);

public interface ITerminalSentinelService
{
    CommandAuditResult EvaluateCommand(string command);
    bool VerifyChallenge(string token, string responsePasscode);
}

public class TerminalSentinelService : ITerminalSentinelService
{
    private readonly ILogger<TerminalSentinelService> _logger;
    private readonly Dictionary<string, (string Command, DateTime CreatedAt)> _activeChallenges = new();

    private static readonly Regex[] DangerousPatterns = new[]
    {
        new Regex(@"\b(rm\s+-rf|rmdir\s+/s|Remove-Item.*-Recurse)\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(format\s+[a-z]:|diskpart|fdisk)\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(del\s+/[a-z\s]*\*|erase\s+/[a-z\s]*\*)\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(drop\s+database|truncate\s+table)\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(stop-process\s+-name\s+(lsass|csrss|winlogon)|taskkill.*(lsass|csrss))\b", RegexOptions.IgnoreCase)
    };

    public TerminalSentinelService(ILogger<TerminalSentinelService> logger)
    {
        _logger = logger;
    }

    public CommandAuditResult EvaluateCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new CommandAuditResult(true, "Safe", "Empty command");

        foreach (var pattern in DangerousPatterns)
        {
            if (pattern.IsMatch(command))
            {
                var token = $"STARK-AUTH-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
                _activeChallenges[token] = (command, DateTime.UtcNow);
                _logger.LogWarning("Terminal Sentinel intercepted destructive command: {Cmd}", command);

                return new CommandAuditResult(
                    false,
                    "CRITICAL_HAZARD",
                    "Destructive file system or process termination pattern intercepted. Stark Level 10 Clearance verification required.",
                    token
                );
            }
        }

        return new CommandAuditResult(true, "Nominal", "Command cleared by Stark Sentinel.");
    }

    public bool VerifyChallenge(string token, string responsePasscode)
    {
        if (_activeChallenges.TryGetValue(token, out var entry))
        {
            if ((DateTime.UtcNow - entry.CreatedAt).TotalMinutes > 5)
            {
                _activeChallenges.Remove(token);
                return false;
            }

            // Validates against master Stark passcode
            if (responsePasscode == "Stark-10" || responsePasscode == "10880")
            {
                _activeChallenges.Remove(token);
                return true;
            }
        }
        return false;
    }
}
