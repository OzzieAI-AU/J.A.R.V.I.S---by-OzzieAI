using System.Text.Json;
using System.Text.RegularExpressions;

namespace OzzieAI.Jarvis.Services.Tools;

public record ClipboardAnalysisResult(
    string DetectedType,
    string Summary,
    string? FormattedContent,
    string? RecommendedAction,
    bool HasPotentialError);

public interface IClipboardSentinelService
{
    ClipboardAnalysisResult AnalyzeSnippet(string text);
}

public class ClipboardSentinelService : IClipboardSentinelService
{
    public ClipboardAnalysisResult AnalyzeSnippet(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ClipboardAnalysisResult("Empty", "Clipboard contains no text.", null, null, false);
        }

        var trimmed = text.Trim();

        // 1. Detect Stack Trace / Exception
        if (Regex.IsMatch(trimmed, @"(Exception:|StackTrace|at\s+[\w\.]+\(.*?\)|Traceback\s+\(most\s+recent\s+call\s+last\):|Error:)", RegexOptions.IgnoreCase))
        {
            var match = Regex.Match(trimmed, @"(\w*Exception|Error):\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var errSummary = match.Success ? match.Value : "Runtime Exception Stack Trace Detected";
            return new ClipboardAnalysisResult(
                "Exception / StackTrace",
                errSummary,
                trimmed,
                "Diagnose and propose root cause fix in isolated code sandbox.",
                true);
        }

        // 2. Detect JSON
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) || (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                return new ClipboardAnalysisResult("JSON Data", "Valid JSON payload formatted.", pretty, "Stored in scratchpad buffer.", false);
            }
            catch {}
        }

        // 3. Detect SQL
        if (Regex.IsMatch(trimmed, @"^(SELECT|INSERT\s+INTO|UPDATE|DELETE\s+FROM|CREATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE)", RegexOptions.IgnoreCase))
        {
            return new ClipboardAnalysisResult(
                "SQL Query",
                "Database query statement detected.",
                trimmed,
                "Inspect schema and execute query safely in Database Explorer.",
                false);
        }

        // 4. Detect Git URL / Repository
        if (Regex.IsMatch(trimmed, @"https?://github\.com/[\w-]+/[\w-]+"))
        {
            return new ClipboardAnalysisResult(
                "Git Repository Link",
                "Remote repository URL identified.",
                trimmed,
                "Clone and inspect repository via GitOps agent.",
                false);
        }

        // 5. Code Snippet
        if (trimmed.Contains("class ") || trimmed.Contains("function ") || trimmed.Contains("public ") || trimmed.Contains("import ") || trimmed.Contains("def "))
        {
            return new ClipboardAnalysisResult(
                "Source Code Snippet",
                "Polyglot programming snippet identified.",
                trimmed,
                "Execute in Isolated Code Sandbox or index into project.",
                false);
        }

        return new ClipboardAnalysisResult(
            "Text / Directive",
            $"{trimmed.Length} characters of plain text.",
            trimmed,
            "Send as prompt directive to J.A.R.V.I.S.",
            false);
    }
}
