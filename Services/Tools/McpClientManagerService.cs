using System.Text.Json;
using System.Text.Json.Nodes;
using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Tools;

public record McpServerEndpoint(string Id, string Name, string TransportType, string Target, bool IsConnected, DateTime LastPing);

public interface IMcpClientManagerService
{
    Task<List<McpServerEndpoint>> GetRegisteredServersAsync();
    Task<bool> RegisterServerAsync(string id, string name, string transportType, string target);
    Task<JsonNode?> CallMcpToolAsync(string serverId, string toolName, JsonObject arguments);
    Task<List<ToolItem>> DiscoverRemoteToolsAsync(string serverId);
}

public class McpClientManagerService : IMcpClientManagerService
{
    private readonly ILogger<McpClientManagerService> _logger;
    private readonly List<McpServerEndpoint> _servers = new()
    {
        new("mcp-github", "GitHub Protocol Daemon", "stdio", "npx -y @modelcontextprotocol/server-github", true, DateTime.UtcNow),
        new("mcp-postgres", "PostgreSQL Database Engine", "stdio", "npx -y @modelcontextprotocol/server-postgres", false, DateTime.UtcNow.AddMinutes(-12)),
        new("mcp-filesystem", "Local Ghost Partition Gateway", "stdio", "npx -y @modelcontextprotocol/server-filesystem GhostDrives/", true, DateTime.UtcNow)
    };

    public McpClientManagerService(ILogger<McpClientManagerService> logger)
    {
        _logger = logger;
    }

    public Task<List<McpServerEndpoint>> GetRegisteredServersAsync()
    {
        return Task.FromResult(_servers.ToList());
    }

    public Task<bool> RegisterServerAsync(string id, string name, string transportType, string target)
    {
        _servers.RemoveAll(s => s.Id == id);
        _servers.Add(new McpServerEndpoint(id, name, transportType, target, true, DateTime.UtcNow));
        _logger.LogInformation("Registered new MCP Server daemon: {Name} ({Transport})", name, transportType);
        return Task.FromResult(true);
    }

    public Task<JsonNode?> CallMcpToolAsync(string serverId, string toolName, JsonObject arguments)
    {
        _logger.LogInformation("Calling MCP tool: {Tool} on server {Server}", toolName, serverId);
        var mockResult = new JsonObject
        {
            ["success"] = true,
            ["server"] = serverId,
            ["tool"] = toolName,
            ["output"] = $"MCP tool '{toolName}' executed successfully on daemon '{serverId}'."
        };
        return Task.FromResult<JsonNode?>(mockResult);
    }

    public Task<List<ToolItem>> DiscoverRemoteToolsAsync(string serverId)
    {
        var tools = new List<ToolItem>
        {
            new()
            {
                Id = $"mcp-{serverId}-query",
                Name = $"mcp_{serverId.Replace("-", "_")}_query",
                Description = $"Remote MCP capability routed to {serverId} protocol bridge.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"directive\":{\"type\":\"string\"}}}",
                ExecutionType = "MCP",
                IsBuiltIn = false
            }
        };
        return Task.FromResult(tools);
    }
}
