namespace OzzieAI.Jarvis.Models;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = "user"; // user, assistant, system, tool
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int TokensUsed { get; set; }
    public string? AudioUrl { get; set; }
    public bool IsVoiceGenerated { get; set; }
    public List<McpToolCall>? ToolCalls { get; set; }
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public bool SpeakResponse { get; set; } = true;
    public string? OverrideProvider { get; set; }
    public string? OverrideModel { get; set; }
}

public class ChatStreamChunk
{
    public string ChunkId { get; set; } = Guid.NewGuid().ToString("N");
    public string Delta { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public string? FullResponse { get; set; }
    public string? AudioUrl { get; set; }
    public string? Error { get; set; }
}
