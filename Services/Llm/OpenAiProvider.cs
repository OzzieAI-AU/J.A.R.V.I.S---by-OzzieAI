using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Llm;

public class OpenAiProvider : ILlmProviderService
{
    private readonly HttpClient _httpClient;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<OpenAiProvider> _logger;

    public string ProviderName => "OpenAI";

    public OpenAiProvider(HttpClient httpClient, IJarvisConfigService configService, ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(List<ChatMessage> history, string systemPrompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = _configService.GetConfig();
        var apiKey = config.Providers.OpenAiApiKey;
        var model = string.IsNullOrWhiteSpace(config.Providers.OpenAiModel) ? "gpt-4o" : config.Providers.OpenAiModel;
        var baseUrl = string.IsNullOrWhiteSpace(config.Providers.OpenAiBaseUrl) ? "https://api.openai.com/v1" : config.Providers.OpenAiBaseUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return "Sir, the OpenAI API key is currently unconfigured. You may enter your key in the Holographic Settings Modal under Providers to activate real-time cognitive processing.";
            yield break;
        }

        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            }
        };

        foreach (var msg in history.TakeLast(20))
        {
            messages.Add(new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content
            });
        }

        var requestBody = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = config.Persona.Temperature,
            ["max_tokens"] = config.Persona.MaxTokens,
            ["stream"] = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
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
            _logger.LogError(ex, "OpenAI network request failed");
            errorMessage = $"[Jarvis Telemetry Error]: Connection to OpenAI endpoint failed: {ex.Message}";
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
            _logger.LogError("OpenAI API error {StatusCode}: {Error}", status, err);
            yield return $"[OpenAI Link Error {status}]: Unable to establish neural link. {err}";
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

                var deltaText = ParseDeltaContent(data);
                if (!string.IsNullOrEmpty(deltaText))
                {
                    yield return deltaText;
                }
            }
        }
    }

    private static string? ParseDeltaContent(string data)
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
        if (string.IsNullOrWhiteSpace(config.Providers.OpenAiApiKey)) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{config.Providers.OpenAiBaseUrl.TrimEnd('/')}/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Providers.OpenAiApiKey);
            var res = await _httpClient.SendAsync(request);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
