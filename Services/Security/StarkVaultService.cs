using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OzzieAI.Jarvis.Data;

namespace OzzieAI.Jarvis.Services.Security;

public record VaultSecretItem(string Key, string Value, string Description, DateTime LastModified);

public interface IStarkVaultService
{
    Task StoreSecretAsync(string key, string plainText, string description);
    Task<string?> RetrieveSecretAsync(string key, string masterPasscode);
    Task<List<VaultSecretItem>> ListSecretMetadataAsync();
    Task<bool> DeleteSecretAsync(string key);
}

public class StarkVaultService : IStarkVaultService
{
    private readonly JarvisDatabase _database;
    private readonly ILogger<StarkVaultService> _logger;
    private static readonly byte[] EntropyKey = Encoding.UTF8.GetBytes("StarkNanotechAESKey2026Encryption!");

    public StarkVaultService(JarvisDatabase database, ILogger<StarkVaultService> logger)
    {
        _database = database;
        _logger = logger;
        InitializeVaultTable();
    }

    private void InitializeVaultTable()
    {
        try
        {
            using var conn = _database.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS StarkVault (
                    Key TEXT PRIMARY KEY,
                    EncryptedData TEXT NOT NULL,
                    Description TEXT,
                    LastModified TEXT NOT NULL
                );
            ";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Stark Vault table");
        }
    }

    public async Task StoreSecretAsync(string key, string plainText, string description)
    {
        var encrypted = Encrypt(plainText);
        using var conn = _database.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO StarkVault (Key, EncryptedData, Description, LastModified)
            VALUES ($k, $e, $d, $m)
        ";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$e", encrypted);
        cmd.Parameters.AddWithValue("$d", description);
        cmd.Parameters.AddWithValue("$m", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Vault stored encrypted secret: {Key}", key);
    }

    public async Task<string?> RetrieveSecretAsync(string key, string masterPasscode)
    {
        if (masterPasscode != "Stark-10" && masterPasscode != "10880")
            throw new UnauthorizedAccessException("Stark Level 10 Vault Passcode Invalid");

        using var conn = _database.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EncryptedData FROM StarkVault WHERE Key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var enc = (string?)await cmd.ExecuteScalarAsync();
        return enc != null ? Decrypt(enc) : null;
    }

    public async Task<List<VaultSecretItem>> ListSecretMetadataAsync()
    {
        var list = new List<VaultSecretItem>();
        using var conn = _database.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Description, LastModified FROM StarkVault ORDER BY Key ASC";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new VaultSecretItem(
                reader.GetString(0),
                "******** [ENCRYPTED]",
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                DateTime.Parse(reader.GetString(2))
            ));
        }
        return list;
    }

    public async Task<bool> DeleteSecretAsync(string key)
    {
        using var conn = _database.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM StarkVault WHERE Key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    private static string Encrypt(string plain)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(EntropyKey);
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var encBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var combined = new byte[aes.IV.Length + encBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
        Buffer.BlockCopy(encBytes, 0, combined, aes.IV.Length, encBytes.Length);
        return Convert.ToBase64String(combined);
    }

    private static string Decrypt(string cipher)
    {
        var combined = Convert.FromBase64String(cipher);
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(EntropyKey);
        var iv = new byte[16];
        var cipherBytes = new byte[combined.Length - 16];
        Buffer.BlockCopy(combined, 0, iv, 0, 16);
        Buffer.BlockCopy(combined, 16, cipherBytes, 0, cipherBytes.Length);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plain = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plain);
    }
}
