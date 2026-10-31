using System.Text.Json.Nodes;

namespace OzzieAI.Jarvis.Services.Rea;

public class ReaAnalysisResult
{
    public bool Success { get; set; }
    public string Target { get; set; } = string.Empty;
    public string FormatType { get; set; } = string.Empty; // .NET Assembly, PE32+, ELF, Script, Data
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public List<string> ExtractedSymbols { get; set; } = new();
    public List<string> InterestingStrings { get; set; } = new();
    public List<string> DisassembledFunctions { get; set; } = new();
    public string SummaryMarkdown { get; set; } = string.Empty;
}

public interface IReaService
{
    Task<ReaAnalysisResult> AnalyzeTargetAsync(string targetPathOrIdentifier);
    Task<string> ExecuteMcpReaBridgeAsync(string toolName, JsonObject? arguments);
}
