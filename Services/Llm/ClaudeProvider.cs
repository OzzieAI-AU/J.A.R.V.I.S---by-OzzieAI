using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Llm;

public class ClaudeProvider : ILlmProviderService
{
    private readonly HttpClient _httpClient;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<ClaudeProvider> _logger;

    public string ProviderName => "Claude";

    public ClaudeProvider(HttpClient httpClient, IJarvisConfigService configService, ILogger<ClaudeProvider> logger)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(List<ChatMessage> history, string systemPrompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = _configService.GetConfig();
        var apiKey = config.Providers.ClaudeApiKey;
        var model = string.IsNullOrWhiteSpace(config.Providers.ClaudeModel) ? "claude-3-5-sonnet-20241022" : config.Providers.ClaudeModel;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return "Sir, the Anthropic Claude API key is not configured. Please supply your API key in the Settings Modal under Providers.";
            yield break;
        }

        var messages = new JsonArray();
        foreach (var msg in history.TakeLast(20))
        {
            if (msg.Role == "system") continue;
            messages.Add(new JsonObject
            {
                ["role"] = msg.Role == "assistant" ? "assistant" : "user",
                ["content"] = msg.Content
            });
        }

        if (messages.Count == 0)
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = "Awaiting instructions, Jarvis." });
        }

        var requestBody = new JsonObject
        {
            ["model"] = model,
            ["system"] = systemPrompt,
            ["messages"] = messages,
            ["max_tokens"] = config.Persona.MaxTokens,
            ["temperature"] = config.Persona.Temperature,
            ["stream"] = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage? response = null;
        string? errorMessage = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude request failed");
            errorMessage = $"[Claude Link Error]: {ex.Message}";
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
            yield return $"[Claude Error {status}]: {err}";
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
                var text = ParseClaudeDelta(data);
                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }
    }

    private static string? ParseClaudeDelta(string data)
    {
        try
        {
            var node = JsonNode.Parse(data);
            var type = node?["type"]?.GetValue<string>();
            if (type == "content_block_delta")
            {
                return node?["delta"]?["text"]?.GetValue<string>();
            }
        }
        catch { }
        return null;
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
        return !string.IsNullOrWhiteSpace(config.Providers.ClaudeApiKey);
    }
}
