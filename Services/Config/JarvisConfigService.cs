using System.Text.Json;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Config;

public class JarvisConfigService : IJarvisConfigService
{
    private readonly JarvisDatabase _database;
    private readonly ILogger<JarvisConfigService> _logger;
    private AppSettingsConfig _cachedConfig;
    private readonly object _lock = new();

    public event Action<AppSettingsConfig>? OnConfigChanged;

    public JarvisConfigService(JarvisDatabase database, ILogger<JarvisConfigService> logger)
    {
        _database = database;
        _logger = logger;
        _cachedConfig = LoadConfigFromDatabase();
    }

    public AppSettingsConfig GetConfig()
    {
        lock (_lock)
        {
            return _cachedConfig;
        }
    }

    public async Task UpdateConfigAsync(AppSettingsConfig newConfig)
    {
        lock (_lock)
        {
            _cachedConfig = newConfig;
        }

        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO AppConfig (Key, Value, UpdatedAt) VALUES ('master_config', $val, $up)";
            var json = JsonSerializer.Serialize(newConfig, new JsonSerializerOptions { WriteIndented = true });
            cmd.Parameters.AddWithValue("$val", json);
            cmd.Parameters.AddWithValue("$up", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Jarvis configuration updated and persisted.");
            OnConfigChanged?.Invoke(newConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist updated configuration");
        }
    }

    private AppSettingsConfig LoadConfigFromDatabase()
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppConfig WHERE Key = 'master_config'";
            var result = cmd.ExecuteScalar();

            if (result != null && result != DBNull.Value)
            {
                var json = result.ToString();
                if (!string.IsNullOrEmpty(json))
                {
                    var config = JsonSerializer.Deserialize<AppSettingsConfig>(json);
                    if (config != null)
                    {
                        return config;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading config from SQLite. Using default configuration.");
        }

        var defaultConfig = new AppSettingsConfig();
        // Persist default config
        Task.Run(async () =>
        {
            try
            {
                using var connection = _database.CreateConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO AppConfig (Key, Value, UpdatedAt) VALUES ('master_config', $val, $up)";
                var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                cmd.Parameters.AddWithValue("$val", json);
                cmd.Parameters.AddWithValue("$up", DateTime.UtcNow.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write initial default config");
            }
        });

        return defaultConfig;
    }
}
