namespace OzzieAI.Jarvis.Services.Memory;

public record VectorMemoryItem(string Id, string Content, string Category, double Score, DateTime Timestamp);

public interface IVectorMemoryService
{
    Task IndexDocumentAsync(string id, string content, string category);
    Task<List<VectorMemoryItem>> SearchSimilarAsync(string query, int maxResults = 5, string? category = null);
    Task<string> BuildAugmentedContextAsync(string query, int maxSnippets = 3);
}
