using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Agents;

public interface IAgentService
{
    Task<List<AgentItem>> GetAllAgentsAsync();
    Task<AgentItem?> GetAgentAsync(string id);
    Task<AgentItem> GetActiveAgentAsync();
    Task SetActiveAgentAsync(string id);
    Task SaveAgentAsync(AgentItem agent);
    Task DeleteAgentAsync(string id);
    Task<string> BuildAgentSystemPromptAsync(string? agentId = null);
}
