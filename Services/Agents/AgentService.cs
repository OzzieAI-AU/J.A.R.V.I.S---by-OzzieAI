using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;
using OzzieAI.Jarvis.Services.Memory;
using OzzieAI.Jarvis.Services.Skills;

namespace OzzieAI.Jarvis.Services.Agents;

public class AgentService : IAgentService
{
    private readonly JarvisDatabase _database;
    private readonly ISkillService _skillService;
    private readonly IPredicateService _predicateService;
    private readonly IJarvisConfigService _configService;
    private readonly ILogger<AgentService> _logger;

    public AgentService(
        JarvisDatabase database,
        ISkillService skillService,
        IPredicateService predicateService,
        IJarvisConfigService configService,
        ILogger<AgentService> logger)
    {
        _database = database;
        _skillService = skillService;
        _predicateService = predicateService;
        _configService = configService;
        _logger = logger;
    }

    public async Task<List<AgentItem>> GetAllAgentsAsync()
    {
        var list = new List<AgentItem>();
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Role, Description, SystemPrompt, Tone, AssignedSkillIds, AssignedToolIds, ColorTheme, IsActive, IsBuiltIn, CreatedAt FROM Agents ORDER BY IsActive DESC, Name ASC";
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new AgentItem
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Role = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    SystemPrompt = reader.GetString(4),
                    Tone = reader.IsDBNull(5) ? "British Butler" : reader.GetString(5),
                    AssignedSkillIds = DeserializeList(reader.GetString(6)),
                    AssignedToolIds = DeserializeList(reader.GetString(7)),
                    ColorTheme = reader.IsDBNull(8) ? "#00d4ff" : reader.GetString(8),
                    IsActive = reader.GetInt32(9) == 1,
                    IsBuiltIn = reader.GetInt32(10) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(11))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load agents from SQLite");
        }
        return list;
    }

    public async Task<AgentItem?> GetAgentAsync(string id)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Role, Description, SystemPrompt, Tone, AssignedSkillIds, AssignedToolIds, ColorTheme, IsActive, IsBuiltIn, CreatedAt FROM Agents WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new AgentItem
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Role = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    SystemPrompt = reader.GetString(4),
                    Tone = reader.IsDBNull(5) ? "British Butler" : reader.GetString(5),
                    AssignedSkillIds = DeserializeList(reader.GetString(6)),
                    AssignedToolIds = DeserializeList(reader.GetString(7)),
                    ColorTheme = reader.IsDBNull(8) ? "#00d4ff" : reader.GetString(8),
                    IsActive = reader.GetInt32(9) == 1,
                    IsBuiltIn = reader.GetInt32(10) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(11))
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load agent {Id}", id);
        }
        return null;
    }

    public async Task<AgentItem> GetActiveAgentAsync()
    {
        var all = await GetAllAgentsAsync();
        return all.FirstOrDefault(a => a.IsActive) ?? all.FirstOrDefault() ?? new AgentItem
        {
            Id = "agent-jarvis-core",
            Name = "J.A.R.V.I.S. Core",
            Role = "Master AI Butler",
            SystemPrompt = "You are J.A.R.V.I.S., the ultimate Stark AI Butler.",
            IsActive = true
        };
    }

    public async Task SetActiveAgentAsync(string id)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var transaction = connection.BeginTransaction();

            using var deactivateCmd = connection.CreateCommand();
            deactivateCmd.Transaction = transaction;
            deactivateCmd.CommandText = "UPDATE Agents SET IsActive = 0";
            await deactivateCmd.ExecuteNonQueryAsync();

            using var activateCmd = connection.CreateCommand();
            activateCmd.Transaction = transaction;
            activateCmd.CommandText = "UPDATE Agents SET IsActive = 1 WHERE Id = $id";
            activateCmd.Parameters.AddWithValue("$id", id);
            await activateCmd.ExecuteNonQueryAsync();

            transaction.Commit();
            _logger.LogInformation("Active agent switched to {Id}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set active agent {Id}", id);
            throw;
        }
    }

    public async Task SaveAgentAsync(AgentItem agent)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Agents (Id, Name, Role, Description, SystemPrompt, Tone, AssignedSkillIds, AssignedToolIds, ColorTheme, IsActive, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $r, $d, $sp, $t, $skills, $tools, $ct, $act, $bi, $ca)
            ";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(agent.Id) ? Guid.NewGuid().ToString("N") : agent.Id);
            cmd.Parameters.AddWithValue("$n", agent.Name.Trim());
            cmd.Parameters.AddWithValue("$r", agent.Role.Trim());
            cmd.Parameters.AddWithValue("$d", agent.Description?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$sp", agent.SystemPrompt.Trim());
            cmd.Parameters.AddWithValue("$t", agent.Tone?.Trim() ?? "British Butler");
            cmd.Parameters.AddWithValue("$skills", JsonSerializer.Serialize(agent.AssignedSkillIds));
            cmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(agent.AssignedToolIds));
            cmd.Parameters.AddWithValue("$ct", agent.ColorTheme ?? "#00d4ff");
            cmd.Parameters.AddWithValue("$act", agent.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$bi", agent.IsBuiltIn ? 1 : 0);
            cmd.Parameters.AddWithValue("$ca", agent.CreatedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Saved agent {Name} ({Id})", agent.Name, agent.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save agent {Name}", agent.Name);
            throw;
        }
    }

    public async Task DeleteAgentAsync(string id)
    {
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Agents WHERE Id = $id AND IsBuiltIn = 0";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Deleted agent {Id}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete agent {Id}", id);
            throw;
        }
    }

    public async Task<string> BuildAgentSystemPromptAsync(string? agentId = null)
    {
        var agent = string.IsNullOrWhiteSpace(agentId) ? await GetActiveAgentAsync() : await GetAgentAsync(agentId) ?? await GetActiveAgentAsync();
        var config = _configService.GetConfig();

        var sb = new StringBuilder();
        sb.AppendLine($"[ACTIVE AGENT IDENTITY: {agent.Name.ToUpperInvariant()} - {agent.Role}]");
        sb.AppendLine($"Tone: {agent.Tone}");
        sb.AppendLine(agent.SystemPrompt);
        sb.AppendLine();

        // Include User Profile / About Me Personalization
        if (config.Profile != null)
        {
            var p = config.Profile;
            sb.AppendLine("[USER DOSSIER & PERSONALIZATION - ABOUT ME]:");
            sb.AppendLine($"User Full Name: {p.FullName} (Preferred Call Sign: '{p.PreferredName}')");
            sb.AppendLine($"MANDATORY HONORIFIC / SALUTATION: Address the user as '{p.Honorific}'. Respectfully acknowledge orders with '{p.Honorific}' or 'Yes, {p.Honorific}'.");
            if (!string.IsNullOrWhiteSpace(p.RoleTitle)) sb.AppendLine($"Title/Role: {p.RoleTitle}");
            if (!string.IsNullOrWhiteSpace(p.Organization)) sb.AppendLine($"Organization: {p.Organization}");
            if (!string.IsNullOrWhiteSpace(p.Address)) sb.AppendLine($"Primary Residence / Facility: {p.Address}");
            if (!string.IsNullOrWhiteSpace(p.DateOfBirth)) sb.AppendLine($"Date of Birth: {p.DateOfBirth}");
            if (!string.IsNullOrWhiteSpace(p.FavoriteColor)) sb.AppendLine($"Favorite Color / HUD Theme: {p.FavoriteColor}");
            if (!string.IsNullOrWhiteSpace(p.Preferences)) sb.AppendLine($"Preferences & Habits: {p.Preferences}");
            if (!string.IsNullOrWhiteSpace(p.SecurityClearance)) sb.AppendLine($"Security Clearance: {p.SecurityClearance}");
            if (!string.IsNullOrWhiteSpace(p.EmergencyContact)) sb.AppendLine($"Emergency Contact: {p.EmergencyContact}");
            if (!string.IsNullOrWhiteSpace(p.CustomAiInstructions)) sb.AppendLine($"Personal AI Directives: {p.CustomAiInstructions}");
            sb.AppendLine();
        }

        // Include user config persona
        if (!string.IsNullOrWhiteSpace(config.Persona.SystemInstructions))
        {
            sb.AppendLine("[GLOBAL STARK PROTOCOL INSTRUCTIONS]:");
            sb.AppendLine(config.Persona.SystemInstructions);
            sb.AppendLine();
        }

        // Attach Assigned Skills Workflows
        if (agent.AssignedSkillIds.Count > 0)
        {
            sb.AppendLine("[ASSIGNED SPECIALIZED SKILLS & WORKFLOWS]:");
            foreach (var skillId in agent.AssignedSkillIds)
            {
                var skill = await _skillService.GetSkillAsync(skillId);
                if (skill != null)
                {
                    sb.AppendLine($"### Skill: {skill.Name} ({skill.Category})");
                    sb.AppendLine($"Instructions & Playbook:\n{skill.Instructions}");
                    sb.AppendLine();
                }
            }
        }

        // Attach Dynamic Predicates Memory Context
        var predicatesContext = await _predicateService.BuildPredicateSystemContextAsync();
        if (!string.IsNullOrWhiteSpace(predicatesContext))
        {
            sb.AppendLine(predicatesContext);
        }

        return sb.ToString();
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
