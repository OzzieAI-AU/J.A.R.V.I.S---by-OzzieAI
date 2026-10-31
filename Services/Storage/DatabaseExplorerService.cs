using Microsoft.Data.Sqlite;
using System.Data;
using System.Text;

namespace OzzieAI.Jarvis.Services.Storage;

public record TableSchemaInfo(string TableName, int ColumnCount, int EstimatedRowCount);
public record SqlQueryResult(List<string> Columns, List<List<string>> Rows, int AffectedRows, string? Error);

public interface IDatabaseExplorerService
{
    Task<List<TableSchemaInfo>> GetSchemaOverviewAsync();
    Task<SqlQueryResult> ExecuteSafeQueryAsync(string sql);
}

public class DatabaseExplorerService : IDatabaseExplorerService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseExplorerService> _logger;

    public DatabaseExplorerService(IConfiguration config, ILogger<DatabaseExplorerService> logger)
    {
        _logger = logger;
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "jarvis_data.db");
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task<List<TableSchemaInfo>> GetSchemaOverviewAsync()
    {
        var tables = new List<TableSchemaInfo>();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            using var reader = await cmd.ExecuteReaderAsync();

            var tableNames = new List<string>();
            while (await reader.ReadAsync())
            {
                tableNames.Add(reader.GetString(0));
            }
            reader.Close();

            foreach (var tName in tableNames)
            {
                using var cntCmd = conn.CreateCommand();
                cntCmd.CommandText = $"SELECT COUNT(*) FROM \"{tName}\";";
                var count = Convert.ToInt32(await cntCmd.ExecuteScalarAsync());

                using var pragmaCmd = conn.CreateCommand();
                pragmaCmd.CommandText = $"PRAGMA table_info(\"{tName}\");";
                var cols = 0;
                using var pReader = await pragmaCmd.ExecuteReaderAsync();
                while (await pReader.ReadAsync()) cols++;

                tables.Add(new TableSchemaInfo(tName, cols, count));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inspect database schema");
        }
        return tables;
    }

    public async Task<SqlQueryResult> ExecuteSafeQueryAsync(string sql)
    {
        var cols = new List<string>();
        var rows = new List<List<string>>();

        var trimmed = sql.Trim();
        // Safety guard: only allow SELECT or PRAGMA queries in safe explorer
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase))
        {
            return new SqlQueryResult(cols, rows, 0, "Security Block: Only read-only queries (SELECT / PRAGMA) are permitted in Database Explorer.");
        }

        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                cols.Add(reader.GetName(i));
            }

            int count = 0;
            while (await reader.ReadAsync() && count < 100)
            {
                var row = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row.Add(reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "");
                }
                rows.Add(row);
                count++;
            }

            return new SqlQueryResult(cols, rows, rows.Count, null);
        }
        catch (Exception ex)
        {
            return new SqlQueryResult(cols, rows, 0, ex.Message);
        }
    }
}
