using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Llm;

public class OllamaProvider : ILlmProviderService
{
    private readonly HttpClient _httpClient;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<OllamaProvider> _logger;

    public string ProviderName => "Ollama";

    public OllamaProvider(HttpClient httpClient, IJarvisConfigService configService, ILogger<OllamaProvider> logger)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(List<ChatMessage> history, string systemPrompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = _configService.GetConfig();
        var baseUrl = string.IsNullOrWhiteSpace(config.Providers.OllamaBaseUrl) ? "http://localhost:11434" : config.Providers.OllamaBaseUrl.TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(config.Providers.OllamaModel) ? "llama3.2" : config.Providers.OllamaModel;

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt }
        };

        foreach (var msg in history.TakeLast(20))
        {
            messages.Add(new JsonObject { ["role"] = msg.Role, ["content"] = msg.Content });
        }

        var requestBody = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
            ["options"] = new JsonObject
            {
                ["temperature"] = config.Persona.Temperature
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat")
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? response = null;
        string? errorMessage = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama local connection failed");
            errorMessage = $"[Ollama Offline]: Unable to connect to local instance at {baseUrl}. Ensure Ollama is running (`ollama serve`). Error: {ex.Message}";
        }

        if (errorMessage != null)
        {
            yield return errorMessage;
            yield break;
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            var err = response != null ? await response.Content.ReadAsStringAsync(cancellationToken) : "No response";
            var status = response?.StatusCode.ToString() ?? "Unknown";
            yield return $"[Ollama Error {status}]: {err}";
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var text = ParseOllamaLine(line);
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private static string? ParseOllamaLine(string line)
    {
        try
        {
            var node = JsonNode.Parse(line);
            return node?["message"]?["content"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> GenerateResponseAsync(List<ChatMessage> history, string systemPrompt, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        await foreach (var token in StreamResponseAsync(history, systemPrompt, cancellationToken))
        {
            sb.Append(token);
        }
        return sb.ToString();
    }

    public async Task<bool> TestConnectionAsync()
    {
        var config = _configService.GetConfig();
        var baseUrl = string.IsNullOrWhiteSpace(config.Providers.OllamaBaseUrl) ? "http://localhost:11434" : config.Providers.OllamaBaseUrl.TrimEnd('/');
        try
        {
            var res = await _httpClient.GetAsync($"{baseUrl}/api/tags");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
