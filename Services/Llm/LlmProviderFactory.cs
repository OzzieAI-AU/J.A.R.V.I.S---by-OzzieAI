using System.Runtime.CompilerServices;
using System.Text;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Llm;

public class LlmProviderFactory
{
    private readonly IEnumerable<ILlmProviderService> _providers;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<LlmProviderFactory> _logger;

    public LlmProviderFactory(IEnumerable<ILlmProviderService> providers, IJarvisConfigService configService, ILogger<LlmProviderFactory> logger)
    {
        _providers = providers;
        _configService = configService;
        _logger = logger;
    }

    public ILlmProviderService GetActiveProvider(string? overrideProvider = null)
    {
        var target = overrideProvider ?? _configService.GetConfig().Providers.ActiveProvider;
        var match = _providers.FirstOrDefault(p => string.Equals(p.ProviderName, target, StringComparison.OrdinalIgnoreCase));
        
        return match ?? _providers.First(p => p.ProviderName == "OpenAI");
    }

    public async Task<(ILlmProviderService Provider, bool IsFallback, string FallbackReason)> GetResilientProviderAsync(string? overrideProvider = null)
    {
        var primary = GetActiveProvider(overrideProvider);
        try
        {
            if (await primary.TestConnectionAsync())
            {
                return (primary, false, "");
            }
        }
        catch { }

        // Fallback 1: Ollama Local Zero-Cloud
        var ollama = _providers.FirstOrDefault(p => p.ProviderName == "Ollama");
        if (ollama != null)
        {
            try
            {
                if (await ollama.TestConnectionAsync())
                {
                    _logger.LogInformation("Zero-Cloud Fallback: primary {Primary} unreachable, switching to local Ollama", primary.ProviderName);
                    return (ollama, true, "Local Ollama Zero-Cloud Daemon Engaged");
                }
            }
            catch { }
        }

        return (primary, true, "Stark Offline Neural Simulation");
    }

    public IEnumerable<string> GetAvailableProviderNames()
    {
        return _providers.Select(p => p.ProviderName);
    }

    /// <summary>
    /// Built-in fallback Jarvis neural heuristic when no external API key has been added yet.
    /// Provides rich, in-character Stark AI Butler assistance.
    /// </summary>
    public async IAsyncEnumerable<string> StreamBuiltinJarvisSimulationAsync(
        string userPrompt, 
        string systemContext, 
        SensorTelemetry? currentTelemetry,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = _configService.GetConfig();
        var botName = config.Persona.BotName;
        var userName = config.Persona.UserName;

        var lower = userPrompt.ToLowerInvariant();
        string response;

        if (lower.Contains("status") || lower.Contains("telemetry") || lower.Contains("diagnostic"))
        {
            var pwr = currentTelemetry?.ArcReactorOutputGigawatts ?? 3.14;
            var temp = currentTelemetry?.CoreTemperatureCelsius ?? 48.6;
            var cpu = currentTelemetry?.CpuUsagePercent ?? 12.4;
            var ram = currentTelemetry?.MemoryUsagePercent ?? 34.2;

            response = $"At your service, {userName}. Diagnostic telemetry indicates all primary Stark systems are functioning within optimal tolerances.\n\n" +
                       $"* **Arc Reactor Core Output:** `{pwr:F2} GW` (Containment Matrix: Nominal)\n" +
                       $"* **Core Temperature:** `{temp:F1} °C`\n" +
                       $"* **Computational Load:** CPU `{cpu:F1}%` | RAM `{ram:F1}%`\n" +
                       $"* **Holographic Neural Projection:** 60 FPS WebGL Online\n\n" +
                       $"All defense protocols are standing by. How would you like to proceed, Sir?";
        }
        else if (lower.Contains("arc reactor") || lower.Contains("power"))
        {
            response = $"The new element synthesized core is currently delivering 3.14 Gigawatts of clean energy into the workshop grid, Sir. Magnetic containment field efficiency is steady at 99.8%. No degradation detected.";
        }
        else if (lower.Contains("suit") || lower.Contains("armor") || lower.Contains("mark"))
        {
            response = $"The Mark LXXXV (85) Nanotech Armor is docked in the primary containment cradle, fully calibrated and primed for immediate deployment. Nano-particle density is at 100%.";
        }
        else if (lower.Contains("protocol") || lower.Contains("clean slate") || lower.Contains("house party"))
        {
            response = $"Clean Slate Protocol is currently on secure standby. Security clearance Level 10 confirmed. I await your authorization sequence if you wish to initialize secondary autonomous defense routines.";
        }
        else if (lower.Contains("provider") || lower.Contains("setting") || lower.Contains("config") || lower.Contains("api key"))
        {
            response = $"Certainly, Sir. You can configure any LLM Provider (OpenAI GPT-4o, Anthropic Claude 3.5 Sonnet, Google Gemini, Groq, Ollama Local, or DeepSeek), adjust my Persona, manipulate Predicates, or fine-tune audio synthesis directly via the Holographic Settings Modal (click the gear icon or say 'Open Settings').";
        }
        else
        {
            response = $"Right away, {userName}. I have ingested your directive: \"{userPrompt}\".\n\n" +
                       $"The workshop neural network is processing across all active cores. To empower me with continuous frontier model reasoning, simply link your OpenAI, Claude, Gemini, or Groq API key in the Holographic Settings Modal. All sensors, predicates, and 3D visual matrices remain at your command.";
        }

        var words = response.Split(' ');
        foreach (var word in words)
        {
            yield return word + " ";
            await Task.Delay(25, cancellationToken);
        }
    }
}
