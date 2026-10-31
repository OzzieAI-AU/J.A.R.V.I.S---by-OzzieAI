using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Voice;

public class VoiceService : IVoiceService
{
    private readonly HttpClient _httpClient;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<VoiceService> _logger;

    public VoiceService(HttpClient httpClient, IJarvisConfigService configService, ILogger<VoiceService> logger)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async Task<byte[]?> SynthesizeSpeechAsync(string text, string? engine = null, string? voice = null)
    {
        var config = _configService.GetConfig();
        var selectedEngine = engine ?? config.Voice.TtsEngine;
        var sanitizedText = SanitizeTextForSpeech(text);

        if (string.IsNullOrWhiteSpace(sanitizedText)) return null;

        if (string.Equals(selectedEngine, "ElevenLabs", StringComparison.OrdinalIgnoreCase))
        {
            return await SynthesizeElevenLabsAsync(sanitizedText, config);
        }
        else if (string.Equals(selectedEngine, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return await SynthesizeOpenAiAsync(sanitizedText, voice ?? config.Voice.OpenAiTtsVoice, config);
        }

        return null;
    }

    public string SanitizeTextForSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var config = _configService.GetConfig();
        if (!config.Voice.EnableTtsSanitization) return text;

        var cleaned = text;

        if (config.Voice.StripMarkdownFromTts)
        {
            // Remove code blocks
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"```[\s\S]*?```", " [code block omitted] ");
            // Remove inline code backticks
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"`([^`]+)`", "$1");
            // Remove markdown images/links
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"!\[.*?\]\(.*?\)", "");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\[(.*?)\]\(.*?\)", "$1");
            // Remove markdown headers
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^#{1,6}\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            // Remove blockquotes
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^>\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            // Clean table markdown separators
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\|[-:\s|]+\|", "");
            // Replace table cell pipes with commas or spaces
            cleaned = cleaned.Replace("|", ", ");
        }

        // Remove configured exclusion symbols
        var exclusionSymbols = config.Voice.TtsExclusionSymbols;
        if (!string.IsNullOrWhiteSpace(exclusionSymbols))
        {
            var symbols = exclusionSymbols.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var sym in symbols)
            {
                cleaned = cleaned.Replace(sym, " ");
            }
        }

        // Always strip asterisks, backticks, hashes, tildes, underscores, bullets
        cleaned = cleaned.Replace("*", " ")
                         .Replace("#", " ")
                         .Replace("`", " ")
                         .Replace("~", " ")
                         .Replace("_", " ")
                         .Replace("•", " ")
                         .Replace("›", " ")
                         .Replace("»", " ")
                         .Replace("«", " ");

        if (config.Voice.StripEmojisFromTts)
        {
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\p{Cs}|\p{So}", "");
        }

        // Collapse multiple spaces
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    private async Task<byte[]?> SynthesizeElevenLabsAsync(string text, AppSettingsConfig config)
    {
        var apiKey = config.Voice.ElevenLabsApiKey;
        var voiceId = string.IsNullOrWhiteSpace(config.Voice.ElevenLabsVoiceId) ? "pNInz6obpgDQGcFmaJgB" : config.Voice.ElevenLabsVoiceId;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("ElevenLabs API Key not configured.");
            return null;
        }

        try
        {
            var body = new JsonObject
            {
                ["text"] = text,
                ["model_id"] = config.Voice.ElevenLabsModelId,
                ["voice_settings"] = new JsonObject
                {
                    ["stability"] = 0.5,
                    ["similarity_boost"] = 0.8
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("xi-api-key", apiKey);

            var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadAsByteArrayAsync();
            }
            _logger.LogError("ElevenLabs TTS failed: {StatusCode}", res.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs synthesis error");
        }
        return null;
    }

    private async Task<byte[]?> SynthesizeOpenAiAsync(string text, string voice, AppSettingsConfig config)
    {
        var apiKey = config.Providers.OpenAiApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("OpenAI API key missing for TTS.");
            return null;
        }

        try
        {
            var body = new JsonObject
            {
                ["model"] = "tts-1",
                ["input"] = text,
                ["voice"] = string.IsNullOrWhiteSpace(voice) ? "onyx" : voice
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadAsByteArrayAsync();
            }
            _logger.LogError("OpenAI TTS failed: {StatusCode}", res.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI TTS error");
        }
        return null;
    }

    public async Task<string?> TranscribeAudioAsync(byte[] audioData, string filename, string? engine = null)
    {
        var config = _configService.GetConfig();
        var selectedEngine = engine ?? config.Voice.SttEngine;

        if (string.Equals(selectedEngine, "GroqWhisper", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.Providers.GroqApiKey))
        {
            return await TranscribeGroqWhisperAsync(audioData, filename, config.Providers.GroqApiKey);
        }

        if (!string.IsNullOrWhiteSpace(config.Providers.OpenAiApiKey))
        {
            return await TranscribeOpenAiWhisperAsync(audioData, filename, config.Providers.OpenAiApiKey);
        }

        return null;
    }

    private async Task<string?> TranscribeOpenAiWhisperAsync(byte[] audioData, string filename, string apiKey)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
            content.Add(audioContent, "file", filename);
            content.Add(new StringContent("whisper-1"), "model");

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions")
            {
                Content = content
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(json);
                return node?["text"]?.GetValue<string>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI Whisper transcription error");
        }
        return null;
    }

    private async Task<string?> TranscribeGroqWhisperAsync(byte[] audioData, string filename, string apiKey)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
            content.Add(audioContent, "file", filename);
            content.Add(new StringContent("whisper-large-v3"), "model");

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions")
            {
                Content = content
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(json);
                return node?["text"]?.GetValue<string>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Groq Whisper transcription error");
        }
        return null;
    }

    public Task<List<string>> GetAvailableProviderVoicesAsync(string provider)
    {
        if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new List<string> { "onyx", "alloy", "echo", "fable", "nova", "shimmer" });
        }
        if (string.Equals(provider, "ElevenLabs", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new List<string>
            {
                "pNInz6obpgDQGcFmaJgB (British Butler - Adam)",
                "21m00Tcm4TlvDq8ikWAM (Rachel)",
                "AZnzlk1XvdvUeBnXmlld (Domi)",
                "EXAVITQu4vr4xnSDxMaL (Bella)",
                "ErXwobaYiN019PkySvjV (Antoni)"
            });
        }

        return Task.FromResult(new List<string>());
    }
}
