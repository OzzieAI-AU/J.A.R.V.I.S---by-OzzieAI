using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Config;

public interface IJarvisConfigService
{
    AppSettingsConfig GetConfig();
    Task UpdateConfigAsync(AppSettingsConfig newConfig);
    event Action<AppSettingsConfig>? OnConfigChanged;
}
