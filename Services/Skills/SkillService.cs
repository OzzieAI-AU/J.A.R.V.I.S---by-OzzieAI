using System.Text.Json;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Skills;

public class SkillService : ISkillService
{
    private readonly JarvisDatabase _database;
    private readonly ILogger<SkillService> _logger;

    public SkillService(JarvisDatabase database, ILogger<SkillService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<List<SkillItem>> GetAllSkillsAsync()
    {
        var list = new List<SkillItem>();
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Category, Description, Instructions, PrerequisiteToolIds, Tags, IsBuiltIn, CreatedAt FROM Skills ORDER BY Category, Name";
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new SkillItem
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Category = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Instructions = reader.GetString(4),
                    PrerequisiteToolIds = DeserializeList(reader.GetString(5)),
                    Tags = DeserializeList(reader.GetString(6)),
                    IsBuiltIn = reader.GetInt32(7) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(8))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load skills from SQLite");
        }
        return list;
    }

    public async Task<SkillItem?> GetSkillAsync(string id)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Category, Description, Instructions, PrerequisiteToolIds, Tags, IsBuiltIn, CreatedAt FROM Skills WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new SkillItem
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Category = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Instructions = reader.GetString(4),
                    PrerequisiteToolIds = DeserializeList(reader.GetString(5)),
                    Tags = DeserializeList(reader.GetString(6)),
                    IsBuiltIn = reader.GetInt32(7) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(8))
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load skill {Id}", id);
        }
        return null;
    }

    public async Task SaveSkillAsync(SkillItem skill)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Skills (Id, Name, Category, Description, Instructions, PrerequisiteToolIds, Tags, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $c, $d, $i, $tools, $tags, $bi, $ca)
            ";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(skill.Id) ? Guid.NewGuid().ToString("N") : skill.Id);
            cmd.Parameters.AddWithValue("$n", skill.Name.Trim());
            cmd.Parameters.AddWithValue("$c", string.IsNullOrWhiteSpace(skill.Category) ? "Engineering" : skill.Category.Trim());
            cmd.Parameters.AddWithValue("$d", skill.Description?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$i", skill.Instructions.Trim());
            cmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(skill.PrerequisiteToolIds));
            cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(skill.Tags));
            cmd.Parameters.AddWithValue("$bi", skill.IsBuiltIn ? 1 : 0);
            cmd.Parameters.AddWithValue("$ca", skill.CreatedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Saved skill {Name} ({Id})", skill.Name, skill.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save skill {Name}", skill.Name);
            throw;
        }
    }

    public async Task DeleteSkillAsync(string id)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Skills WHERE Id = $id AND IsBuiltIn = 0";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Deleted skill {Id}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete skill {Id}", id);
            throw;
        }
    }

    private static List<string> DeserializeList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
