using System.Text.RegularExpressions;

namespace OzzieAI.Jarvis.Services.Projects;

public record CodeSymbol(string Name, string Kind, string FilePath, int LineNumber, string Signature);

public interface ICodebaseIndexerService
{
    Task<List<CodeSymbol>> IndexDirectoryAsync(string rootPath);
    Task<List<CodeSymbol>> SearchSymbolsAsync(string query, string? rootPath = null);
}

public class CodebaseIndexerService : ICodebaseIndexerService
{
    private readonly List<CodeSymbol> _cachedSymbols = new();
    private readonly ILogger<CodebaseIndexerService> _logger;

    public CodebaseIndexerService(ILogger<CodebaseIndexerService> logger)
    {
        _logger = logger;
    }

    public async Task<List<CodeSymbol>> IndexDirectoryAsync(string rootPath)
    {
        return await Task.Run(() =>
        {
            var results = new List<CodeSymbol>();
            if (!Directory.Exists(rootPath)) return results;

            var extensions = new[] { "*.cs", "*.js", "*.ts", "*.py" };
            foreach (var ext in extensions)
            {
                var files = Directory.EnumerateFiles(rootPath, ext, SearchOption.AllDirectories)
                    .Where(f => !f.Contains("bin") && !f.Contains("obj") && !f.Contains("node_modules") && !f.Contains(".git"));

                foreach (var file in files.Take(250))
                {
                    try
                    {
                        var lines = File.ReadAllLines(file);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            var line = lines[i].Trim();
                            // C# / TypeScript classes & interfaces
                            var classMatch = Regex.Match(line, @"\b(public|internal|private)?\s*(static\s+)?(class|interface|record|enum|struct)\s+([A-Za-z0-9_]+)");
                            if (classMatch.Success)
                            {
                                results.Add(new CodeSymbol(classMatch.Groups[4].Value, classMatch.Groups[3].Value, file, i + 1, line));
                                continue;
                            }

                            // Methods
                            var methodMatch = Regex.Match(line, @"\b(public|internal|private|protected)\s+(static\s+)?(async\s+)?([A-Za-z0-9_<>\[\]]+)\s+([A-Za-z0-9_]+)\s*\(");
                            if (methodMatch.Success)
                            {
                                results.Add(new CodeSymbol(methodMatch.Groups[5].Value, "method", file, i + 1, line));
                                continue;
                            }

                            // Python functions
                            var pyMatch = Regex.Match(line, @"^def\s+([A-Za-z0-9_]+)\s*\(");
                            if (pyMatch.Success)
                            {
                                results.Add(new CodeSymbol(pyMatch.Groups[1].Value, "function", file, i + 1, line));
                            }
                        }
                    }
                    catch {}
                }
            }

            lock (_cachedSymbols)
            {
                _cachedSymbols.Clear();
                _cachedSymbols.AddRange(results);
            }

            _logger.LogInformation("Indexed {Count} code symbols in {Path}", results.Count, rootPath);
            return results;
        });
    }

    public async Task<List<CodeSymbol>> SearchSymbolsAsync(string query, string? rootPath = null)
    {
        if (_cachedSymbols.Count == 0 && !string.IsNullOrWhiteSpace(rootPath))
        {
            await IndexDirectoryAsync(rootPath);
        }

        lock (_cachedSymbols)
        {
            return _cachedSymbols
                .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            s.Signature.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(25)
                .ToList();
        }
    }
}
