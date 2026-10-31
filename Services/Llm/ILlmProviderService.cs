using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Llm;

public interface ILlmProviderService
{
    string ProviderName { get; }
    IAsyncEnumerable<string> StreamResponseAsync(List<ChatMessage> history, string systemPrompt, CancellationToken cancellationToken = default);
    Task<string> GenerateResponseAsync(List<ChatMessage> history, string systemPrompt, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync();
}
