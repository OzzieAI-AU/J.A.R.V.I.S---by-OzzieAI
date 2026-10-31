using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Tools;

public interface IToolService
{
    Task<List<ToolItem>> GetAllToolsAsync();
    Task<ToolItem?> GetToolAsync(string idOrName);
    Task SaveToolAsync(ToolItem tool);
    Task DeleteToolAsync(string id);
    Task<ToolExecutionResponse> ExecuteToolAsync(ToolExecutionRequest request);
}
