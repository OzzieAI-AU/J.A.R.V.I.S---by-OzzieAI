namespace OzzieAI.Jarvis.Models;

public class AgentItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string Tone { get; set; } = "British Butler";
    public List<string> AssignedSkillIds { get; set; } = new();
    public List<string> AssignedToolIds { get; set; } = new();
    public string ColorTheme { get; set; } = "#00d4ff"; // Hex or Theme name
    public bool IsActive { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SkillItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Engineering"; // Engineering, Tactical, Defense, Software, Medical
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty; // Markdown workflow / cheatsheet
    public List<string> PrerequisiteToolIds { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ToolItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ParametersJsonSchema { get; set; } = "{}";
    public string ExecutionType { get; set; } = "Internal"; // Internal, HttpEndpoint, McpRpc, Script
    public string? EndpointUrl { get; set; }
    public string? ScriptCode { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ToolExecutionRequest
{
    public string ToolNameOrId { get; set; } = string.Empty;
    public Dictionary<string, object>? Arguments { get; set; }
}

public class ToolExecutionResponse
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}
