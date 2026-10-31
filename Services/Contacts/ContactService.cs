using System.Data;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Contacts;

public class ContactService : IContactService
{
    private readonly JarvisDatabase _database;
    private readonly ILogger<ContactService> _logger;

    public ContactService(JarvisDatabase database, ILogger<ContactService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<List<ContactItem>> GetAllContactsAsync(string? search = null, string? tag = null, int? minClearance = null)
    {
        var list = new List<ContactItem>();
        try
        {
            using var conn = _database.CreateConnection();
            var cmd = conn.CreateCommand();

            var sql = "SELECT Id, FullName, CallSign, Role, Organization, PrimaryPhone, SecondaryPhone, PrimaryEmail, WorkEmail, ClearanceLevel, Tags, Notes, AvatarColor, MetadataJson, CreatedAt, UpdatedAt, AvatarUrl FROM Contacts WHERE 1=1";

            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (FullName LIKE $s OR CallSign LIKE $s OR Role LIKE $s OR Organization LIKE $s OR PrimaryEmail LIKE $s OR PrimaryPhone LIKE $s)";
                cmd.Parameters.AddWithValue("$s", $"%{search.Trim()}%");
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                sql += " AND Tags LIKE $t";
                cmd.Parameters.AddWithValue("$t", $"%{tag.Trim()}%");
            }

            if (minClearance.HasValue)
            {
                sql += " AND ClearanceLevel >= $mc";
                cmd.Parameters.AddWithValue("$mc", minClearance.Value);
            }

            sql += " ORDER BY ClearanceLevel DESC, FullName ASC";
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(MapReader(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get contacts");
        }
        return list;
    }

    public async Task<ContactItem?> GetContactByIdAsync(string id)
    {
        try
        {
            using var conn = _database.CreateConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, FullName, CallSign, Role, Organization, PrimaryPhone, SecondaryPhone, PrimaryEmail, WorkEmail, ClearanceLevel, Tags, Notes, AvatarColor, MetadataJson, CreatedAt, UpdatedAt, AvatarUrl FROM Contacts WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapReader(reader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get contact by ID: {Id}", id);
        }
        return null;
    }

    public async Task<ContactItem?> FindContactByNameOrCallSignAsync(string nameOrCallSign)
    {
        if (string.IsNullOrWhiteSpace(nameOrCallSign)) return null;
        var q = nameOrCallSign.Trim().ToLowerInvariant();

        try
        {
            using var conn = _database.CreateConnection();
            
            // 1. Direct and substring matching
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, FullName, CallSign, Role, Organization, PrimaryPhone, SecondaryPhone, PrimaryEmail, WorkEmail, ClearanceLevel, Tags, Notes, AvatarColor, MetadataJson, CreatedAt, UpdatedAt, AvatarUrl 
                    FROM Contacts 
                    WHERE LOWER(FullName) = $q 
                       OR LOWER(CallSign) = $q 
                       OR LOWER(FullName) LIKE $like 
                       OR LOWER(CallSign) LIKE $like
                    ORDER BY ClearanceLevel DESC
                    LIMIT 1
                ";
                cmd.Parameters.AddWithValue("$q", q);
                cmd.Parameters.AddWithValue("$like", $"%{q}%");

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapReader(reader);
                }
            }

            // 2. Tokenized word-level matching (e.g. 'Nick Fury' -> 'Nicholas J. Fury')
            var words = q.Split(new[] { ' ', '-', '.', ',', '?', '\'' }, StringSplitOptions.RemoveEmptyEntries)
                         .Where(w => w.Length > 1)
                         .ToList();

            if (words.Count > 0)
            {
                using var cmd2 = conn.CreateCommand();
                var conditions = new List<string>();
                for (int i = 0; i < words.Count; i++)
                {
                    var param = $"$w{i}";
                    var word = words[i];
                    var alt = word == "nick" ? "nichol" : (word == "tony" ? "anthony" : word);
                    conditions.Add($"(LOWER(FullName) LIKE {param} OR LOWER(CallSign) LIKE {param} OR LOWER(Role) LIKE {param} OR LOWER(Organization) LIKE {param} OR LOWER(Notes) LIKE {param})");
                    cmd2.Parameters.AddWithValue(param, $"%{alt}%");
                }

                cmd2.CommandText = $@"
                    SELECT Id, FullName, CallSign, Role, Organization, PrimaryPhone, SecondaryPhone, PrimaryEmail, WorkEmail, ClearanceLevel, Tags, Notes, AvatarColor, MetadataJson, CreatedAt, UpdatedAt, AvatarUrl 
                    FROM Contacts 
                    WHERE {string.Join(" AND ", conditions)}
                    ORDER BY ClearanceLevel DESC
                    LIMIT 1
                ";

                using var reader2 = await cmd2.ExecuteReaderAsync();
                if (await reader2.ReadAsync())
                {
                    return MapReader(reader2);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search contact by query: {Query}", nameOrCallSign);
        }
        return null;
    }

    public async Task<ContactItem> CreateContactAsync(ContactItem contact)
    {
        if (string.IsNullOrWhiteSpace(contact.Id)) contact.Id = Guid.NewGuid().ToString("N");
        contact.CreatedAt = DateTime.UtcNow;
        contact.UpdatedAt = DateTime.UtcNow;

        using var conn = _database.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Contacts (Id, FullName, CallSign, Role, Organization, PrimaryPhone, SecondaryPhone, PrimaryEmail, WorkEmail, ClearanceLevel, Tags, Notes, AvatarColor, MetadataJson, CreatedAt, UpdatedAt, AvatarUrl)
            VALUES ($id, $fn, $cs, $r, $o, $pp, $sp, $pe, $we, $cl, $tags, $n, $ac, $mj, $ca, $ua, $au)
        ";
        cmd.Parameters.AddWithValue("$id", contact.Id);
        cmd.Parameters.AddWithValue("$fn", contact.FullName);
        cmd.Parameters.AddWithValue("$cs", (object?)contact.CallSign ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$r", (object?)contact.Role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$o", (object?)contact.Organization ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pp", (object?)contact.PrimaryPhone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sp", (object?)contact.SecondaryPhone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pe", (object?)contact.PrimaryEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$we", (object?)contact.WorkEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cl", contact.ClearanceLevel);
        cmd.Parameters.AddWithValue("$tags", (object?)contact.Tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$n", (object?)contact.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ac", string.IsNullOrWhiteSpace(contact.AvatarColor) ? "#00d4ff" : contact.AvatarColor);
        cmd.Parameters.AddWithValue("$mj", string.IsNullOrWhiteSpace(contact.MetadataJson) ? "{}" : contact.MetadataJson);
        cmd.Parameters.AddWithValue("$ca", contact.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ua", contact.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$au", (object?)contact.AvatarUrl ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Created contact: {Name} ({Id})", contact.FullName, contact.Id);
        return contact;
    }

    public async Task<ContactItem> UpdateContactAsync(ContactItem contact)
    {
        contact.UpdatedAt = DateTime.UtcNow;

        using var conn = _database.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Contacts 
            SET FullName = $fn,
                CallSign = $cs,
                Role = $r,
                Organization = $o,
                PrimaryPhone = $pp,
                SecondaryPhone = $sp,
                PrimaryEmail = $pe,
                WorkEmail = $we,
                ClearanceLevel = $cl,
                Tags = $tags,
                Notes = $n,
                AvatarColor = $ac,
                MetadataJson = $mj,
                UpdatedAt = $ua,
                AvatarUrl = $au
            WHERE Id = $id
        ";
        cmd.Parameters.AddWithValue("$id", contact.Id);
        cmd.Parameters.AddWithValue("$fn", contact.FullName);
        cmd.Parameters.AddWithValue("$cs", (object?)contact.CallSign ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$r", (object?)contact.Role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$o", (object?)contact.Organization ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pp", (object?)contact.PrimaryPhone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sp", (object?)contact.SecondaryPhone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pe", (object?)contact.PrimaryEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$we", (object?)contact.WorkEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cl", contact.ClearanceLevel);
        cmd.Parameters.AddWithValue("$tags", (object?)contact.Tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$n", (object?)contact.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ac", string.IsNullOrWhiteSpace(contact.AvatarColor) ? "#00d4ff" : contact.AvatarColor);
        cmd.Parameters.AddWithValue("$mj", string.IsNullOrWhiteSpace(contact.MetadataJson) ? "{}" : contact.MetadataJson);
        cmd.Parameters.AddWithValue("$ua", contact.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$au", (object?)contact.AvatarUrl ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Updated contact: {Name} ({Id})", contact.FullName, contact.Id);
        return contact;
    }

    public async Task<bool> DeleteContactAsync(string id)
    {
        using var conn = _database.CreateConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Contacts WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Deleted contact: {Id}", id);
        return rows > 0;
    }

    public async Task<List<ContactItem>> SearchContactsAsync(string query)
    {
        return await GetAllContactsAsync(search: query);
    }

    private static ContactItem MapReader(SqliteDataReader reader)
    {
        return new ContactItem
        {
            Id = reader.GetString(0),
            FullName = reader.GetString(1),
            CallSign = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Role = reader.IsDBNull(3) ? "" : reader.GetString(3),
            Organization = reader.IsDBNull(4) ? "" : reader.GetString(4),
            PrimaryPhone = reader.IsDBNull(5) ? "" : reader.GetString(5),
            SecondaryPhone = reader.IsDBNull(6) ? "" : reader.GetString(6),
            PrimaryEmail = reader.IsDBNull(7) ? "" : reader.GetString(7),
            WorkEmail = reader.IsDBNull(8) ? "" : reader.GetString(8),
            ClearanceLevel = reader.GetInt32(9),
            Tags = reader.IsDBNull(10) ? "" : reader.GetString(10),
            Notes = reader.IsDBNull(11) ? "" : reader.GetString(11),
            AvatarColor = reader.IsDBNull(12) ? "#00d4ff" : reader.GetString(12),
            MetadataJson = reader.IsDBNull(13) ? "{}" : reader.GetString(13),
            CreatedAt = DateTime.Parse(reader.GetString(14)),
            UpdatedAt = DateTime.Parse(reader.GetString(15)),
            AvatarUrl = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetString(16) : ""
        };
    }
}
