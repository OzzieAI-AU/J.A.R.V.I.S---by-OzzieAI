using System.Text.Json.Nodes;

namespace OzzieAI.Jarvis.Models;

public class McpToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonObject Parameters { get; set; } = new();
}

public class McpToolCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public JsonObject Arguments { get; set; } = new();
}

public class McpToolResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class McpRequest
{
    public string JsonRpc { get; set; } = "2.0";
    public string Method { get; set; } = string.Empty;
    public JsonObject? Params { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
}

public class McpResponse
{
    public string JsonRpc { get; set; } = "2.0";
    public JsonNode? Result { get; set; }
    public JsonObject? Error { get; set; }
    public string Id { get; set; } = string.Empty;
}
