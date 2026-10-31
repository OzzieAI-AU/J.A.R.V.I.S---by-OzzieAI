using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Llm;

public class GroqProvider : ILlmProviderService
{
    private readonly HttpClient _httpClient;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<GroqProvider> _logger;

    public string ProviderName => "Groq";

    public GroqProvider(HttpClient httpClient, IJarvisConfigService configService, ILogger<GroqProvider> logger)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(List<ChatMessage> history, string systemPrompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = _configService.GetConfig();
        var apiKey = config.Providers.GroqApiKey;
        var model = string.IsNullOrWhiteSpace(config.Providers.GroqModel) ? "llama-3.3-70b-versatile" : config.Providers.GroqModel;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return "Sir, the Groq API key is unconfigured. Please provide it in the Settings Modal under Providers to leverage Groq's high-speed inference.";
            yield break;
        }

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
            ["temperature"] = config.Persona.Temperature,
            ["max_tokens"] = config.Persona.MaxTokens,
            ["stream"] = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions")
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage? response = null;
        string? errorMessage = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Groq request failed");
            errorMessage = $"[Groq Link Error]: {ex.Message}";
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
            yield return $"[Groq Error {status}]: {err}";
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("data: "))
            {
                var data = line["data: ".Length..].Trim();
                if (data == "[DONE]") break;
                var delta = ParseGroqDelta(data);
                if (!string.IsNullOrEmpty(delta))
                {
                    yield return delta;
                }
            }
        }
    }

    private static string? ParseGroqDelta(string data)
    {
        try
        {
            var node = JsonNode.Parse(data);
            return node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
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
        return !string.IsNullOrWhiteSpace(config.Providers.GroqApiKey);
    }
}
