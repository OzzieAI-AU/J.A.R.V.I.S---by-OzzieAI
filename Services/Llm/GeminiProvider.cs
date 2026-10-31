using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Llm;

public class GeminiProvider : ILlmProviderService
{
    private readonly HttpClient _httpClient;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<GeminiProvider> _logger;

    public string ProviderName => "Gemini";

    public GeminiProvider(HttpClient httpClient, IJarvisConfigService configService, ILogger<GeminiProvider> logger)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(List<ChatMessage> history, string systemPrompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = _configService.GetConfig();
        var apiKey = config.Providers.GeminiApiKey;
        var model = string.IsNullOrWhiteSpace(config.Providers.GeminiModel) ? "gemini-2.5-flash" : config.Providers.GeminiModel;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return "Sir, the Google Gemini API key is currently unconfigured. Please enter your key in the Settings Modal under Providers to initialize Gemini cognition.";
            yield break;
        }

        var contents = new JsonArray();
        foreach (var msg in history.TakeLast(20))
        {
            if (msg.Role == "system") continue;
            var role = msg.Role == "assistant" ? "model" : "user";
            contents.Add(new JsonObject
            {
                ["role"] = role,
                ["parts"] = new JsonArray { new JsonObject { ["text"] = msg.Content } }
            });
        }

        if (contents.Count == 0)
        {
            contents.Add(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = "Status report, Jarvis." } }
            });
        }

        var requestBody = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemPrompt } }
            },
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = config.Persona.Temperature,
                ["maxOutputTokens"] = config.Persona.MaxTokens
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
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
            _logger.LogError(ex, "Gemini request failed");
            errorMessage = $"[Gemini Link Error]: {ex.Message}";
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
            yield return $"[Gemini Error {status}]: {err}";
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
                var text = ParseGeminiDelta(data);
                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }
    }

    private static string? ParseGeminiDelta(string data)
    {
        try
        {
            var node = JsonNode.Parse(data);
            return node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
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
        return !string.IsNullOrWhiteSpace(config.Providers.GeminiApiKey);
    }
}
