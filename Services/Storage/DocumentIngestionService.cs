using OzzieAI.Jarvis.Services.Memory;

namespace OzzieAI.Jarvis.Services.Storage;

public record IngestionReport(string FileName, int ChunksIndexed, bool Success, string? Error);

public interface IDocumentIngestionService
{
    Task<IngestionReport> IngestFileAsync(string filePath, string category = "Document");
}

public class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IVectorMemoryService _vectorMemory;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(IVectorMemoryService vectorMemory, ILogger<DocumentIngestionService> logger)
    {
        _vectorMemory = vectorMemory;
        _logger = logger;
    }

    public async Task<IngestionReport> IngestFileAsync(string filePath, string category = "Document")
    {
        try
        {
            if (!File.Exists(filePath)) return new IngestionReport(filePath, 0, false, "File does not exist.");

            var text = await File.ReadAllTextAsync(filePath);
            var chunks = ChunkText(text, 500);

            var fileName = Path.GetFileName(filePath);
            foreach (var chunk in chunks)
            {
                await _vectorMemory.IndexDocumentAsync(Guid.NewGuid().ToString(), $"[Source: {fileName}] {chunk}", category);
            }

            _logger.LogInformation("Ingested {Count} chunks from {File} into vector memory", chunks.Count, fileName);
            return new IngestionReport(fileName, chunks.Count, true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest document: {Path}", filePath);
            return new IngestionReport(Path.GetFileName(filePath), 0, false, ex.Message);
        }
    }

    private static List<string> ChunkText(string text, int chunkSize)
    {
        var result = new List<string>();
        var paragraphs = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paragraphs)
        {
            if (p.Length <= chunkSize)
            {
                result.Add(p.Trim());
            }
            else
            {
                for (int i = 0; i < p.Length; i += chunkSize)
                {
                    result.Add(p.Substring(i, Math.Min(chunkSize, p.Length - i)).Trim());
                }
            }
        }
        return result;
    }
}
