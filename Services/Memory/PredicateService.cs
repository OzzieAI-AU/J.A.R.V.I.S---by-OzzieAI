using System.Text;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Memory;

public class PredicateService : IPredicateService
{
    private readonly JarvisDatabase _database;
    private readonly ILogger<PredicateService> _logger;

    public PredicateService(JarvisDatabase database, ILogger<PredicateService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<List<PredicateItem>> GetAllPredicatesAsync()
    {
        var list = new List<PredicateItem>();
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Key, Value, Category, Description, LastUpdated FROM Predicates ORDER BY Category, Key";
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new PredicateItem
                {
                    Key = reader.GetString(0),
                    Value = reader.GetString(1),
                    Category = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    LastUpdated = DateTime.Parse(reader.GetString(4))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load predicates from SQLite");
        }
        return list;
    }

    public async Task<PredicateItem?> GetPredicateAsync(string key)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Key, Value, Category, Description, LastUpdated FROM Predicates WHERE Key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new PredicateItem
                {
                    Key = reader.GetString(0),
                    Value = reader.GetString(1),
                    Category = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    LastUpdated = DateTime.Parse(reader.GetString(4))
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load predicate {Key}", key);
        }
        return null;
    }

    public async Task SavePredicateAsync(PredicateItem item)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Predicates (Key, Value, Category, Description, LastUpdated)
                VALUES ($k, $v, $c, $d, $u)
            ";
            cmd.Parameters.AddWithValue("$k", item.Key.Trim());
            cmd.Parameters.AddWithValue("$v", item.Value.Trim());
            cmd.Parameters.AddWithValue("$c", string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category.Trim());
            cmd.Parameters.AddWithValue("$d", item.Description?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Saved predicate: {Key} = {Value}", item.Key, item.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save predicate {Key}", item.Key);
            throw;
        }
    }

    public async Task DeletePredicateAsync(string key)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Predicates WHERE Key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Deleted predicate: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete predicate {Key}", key);
            throw;
        }
    }

    public async Task<string> BuildPredicateSystemContextAsync()
    {
        var predicates = await GetAllPredicatesAsync();
        if (predicates.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("\n[ACTIVE STARK SYSTEM PREDICATES & REAL-TIME LAB STATE]:");
        foreach (var p in predicates)
        {
            sb.AppendLine($"- {p.Key} ({p.Category}): {p.Value}" + (!string.IsNullOrWhiteSpace(p.Description) ? $" [{p.Description}]" : ""));
        }
        sb.AppendLine("[END SYSTEM PREDICATES]\n");
        return sb.ToString();
    }
}
