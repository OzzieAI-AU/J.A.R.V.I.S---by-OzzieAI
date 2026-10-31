using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using OzzieAI.Jarvis.Data;

namespace OzzieAI.Jarvis.Services.Memory;

public class VectorMemoryService : IVectorMemoryService
{
    private readonly JarvisDatabase _database;
    private readonly ILogger<VectorMemoryService> _logger;
    private readonly ConcurrentDictionary<string, (string Content, string Category, DateTime Timestamp, Dictionary<string, double> Vector)> _memoryStore = new();

    public VectorMemoryService(JarvisDatabase database, ILogger<VectorMemoryService> logger)
    {
        _database = database;
        _logger = logger;
        Task.Run(WarmupMemoryStoreAsync);
    }

    public Task IndexDocumentAsync(string id, string content, string category)
    {
        if (string.IsNullOrWhiteSpace(content)) return Task.CompletedTask;
        var vector = ComputeTfVector(content);
        _memoryStore[id] = (content, category, DateTime.UtcNow, vector);
        return Task.CompletedTask;
    }

    public Task<List<VectorMemoryItem>> SearchSimilarAsync(string query, int maxResults = 5, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(query) || _memoryStore.IsEmpty)
            return Task.FromResult(new List<VectorMemoryItem>());

        var queryVec = ComputeTfVector(query);
        var scored = new List<VectorMemoryItem>();

        foreach (var (id, entry) in _memoryStore)
        {
            if (category != null && !entry.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                continue;

            var similarity = ComputeCosineSimilarity(queryVec, entry.Vector);
            if (similarity > 0.05)
            {
                scored.Add(new VectorMemoryItem(id, entry.Content, entry.Category, Math.Round(similarity, 4), entry.Timestamp));
            }
        }

        var results = scored.OrderByDescending(x => x.Score).Take(maxResults).ToList();
        return Task.FromResult(results);
    }

    public async Task<string> BuildAugmentedContextAsync(string query, int maxSnippets = 3)
    {
        var matches = await SearchSimilarAsync(query, maxSnippets);
        if (matches.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n[RELEVANT STARK NEURAL MEMORIES & RAG DOSSIERS]:");
        foreach (var m in matches)
        {
            sb.AppendLine($"• ({m.Category} | Relevance: {m.Score:P0}): {m.Content}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private async Task WarmupMemoryStoreAsync()
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Key, Value, Category FROM Predicates";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var k = reader.GetString(0);
                var v = reader.GetString(1);
                var c = reader.GetString(2);
                await IndexDocumentAsync($"pred_{k}", $"Predicate [{k}]: {v} (Category: {c})", "Predicate");
            }
            _logger.LogInformation("Vector RAG memory warmed up with {Count} baseline items.", _memoryStore.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector RAG memory warmup skipped");
        }
    }

    private static Dictionary<string, double> ComputeTfVector(string text)
    {
        var terms = Regex.Matches(text.ToLowerInvariant(), @"\b[a-z0-9_]{3,}\b")
                         .Select(m => m.Value)
                         .Where(w => !StopWords.Contains(w))
                         .ToList();

        var freq = new Dictionary<string, double>();
        if (terms.Count == 0) return freq;

        foreach (var t in terms)
        {
            freq[t] = freq.GetValueOrDefault(t, 0.0) + 1.0;
        }

        // Normalize vector magnitude
        var sumSquares = freq.Values.Sum(v => v * v);
        var norm = Math.Sqrt(sumSquares);
        if (norm > 0)
        {
            foreach (var key in freq.Keys.ToList())
            {
                freq[key] /= norm;
            }
        }
        return freq;
    }

    private static double ComputeCosineSimilarity(Dictionary<string, double> v1, Dictionary<string, double> v2)
    {
        double dot = 0.0;
        foreach (var (k, val) in v1)
        {
            if (v2.TryGetValue(k, out var val2))
            {
                dot += val * val2;
            }
        }
        return dot;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "is", "in", "it", "to", "of", "for", "with", "on", "that", "this", "are", "was", "as", "at", "by", "an", "be", "from", "or", "your", "you", "will"
    };
}
