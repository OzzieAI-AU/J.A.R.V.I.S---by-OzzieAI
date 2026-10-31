using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using OzzieAI.Jarvis.Data;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Config;

namespace OzzieAI.Jarvis.Services.Security;

public class AuthService : IAuthService
{
    private readonly IJarvisConfigService _configService;
    private readonly JarvisDatabase _database;
    private readonly ILogger<AuthService> _logger;

    private static readonly ConcurrentDictionary<string, (int Attempts, DateTime LockoutUntil)> LoginTracker = new();

    public AuthService(IJarvisConfigService configService, JarvisDatabase database, ILogger<AuthService> logger)
    {
        _configService = configService;
        _database = database;
        _logger = logger;
    }

    public bool IsLoginRequired()
    {
        return _configService.GetConfig().Security.RequireLogin;
    }

    public async Task<AuthResponse> AuthenticateAsync(string passcode, string ipAddress)
    {
        var config = _configService.GetConfig().Security;

        // Check lockout
        if (LoginTracker.TryGetValue(ipAddress, out var tracker))
        {
            if (tracker.LockoutUntil > DateTime.UtcNow)
            {
                var remainingSec = Math.Ceiling((tracker.LockoutUntil - DateTime.UtcNow).TotalSeconds);
                return new AuthResponse
                {
                    Success = false,
                    Message = $"SECURITY LOCKOUT ACTIVE: Too many invalid clearance attempts. Try again in {remainingSec} seconds.",
                    Clearance = "LOCKED"
                };
            }
        }

        bool isValid = false;

        if (!string.IsNullOrWhiteSpace(config.MasterPasscodeHash))
        {
            isValid = VerifyPasswordHash(passcode, config.MasterPasscodeHash);
        }
        else
        {
            // Default check against MasterPasscode
            isValid = string.Equals(passcode.Trim(), config.MasterPasscode.Trim(), StringComparison.Ordinal);
        }

        if (!isValid)
        {
            // Record failed attempt
            var attempts = LoginTracker.AddOrUpdate(ipAddress, 
                _ => (1, DateTime.MinValue),
                (_, old) => (old.Attempts + 1, old.Attempts + 1 >= config.MaxLoginAttempts ? DateTime.UtcNow.AddMinutes(config.LockoutMinutes) : DateTime.MinValue));

            _logger.LogWarning("Clearance rejected for IP {IpAddress}. Failed attempts: {Attempts}", ipAddress, attempts.Attempts);

            return new AuthResponse
            {
                Success = false,
                Message = "ACCESS DENIED: Invalid Stark Clearance Passcode.",
                Clearance = "UNAUTHORIZED"
            };
        }

        // Reset tracker on success
        LoginTracker.TryRemove(ipAddress, out _);

        // Generate JWT Token
        var token = GenerateJwtToken(config.JwtSecret, config.SessionExpiryMinutes);
        var expiresAt = DateTime.UtcNow.AddMinutes(config.SessionExpiryMinutes);

        // Persist session to SQLite
        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO AuthSessions (Token, ClearanceLevel, CreatedAt, ExpiresAt) VALUES ($t, $c, $cr, $ex)";
            cmd.Parameters.AddWithValue("$t", token);
            cmd.Parameters.AddWithValue("$c", "LEVEL_10_OWNER");
            cmd.Parameters.AddWithValue("$cr", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$ex", expiresAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist authentication session");
        }

        _logger.LogInformation("Stark Level 10 Clearance granted for session {Token}", token[..Math.Min(16, token.Length)]);

        return new AuthResponse
        {
            Success = true,
            Token = token,
            Clearance = "LEVEL_10_OWNER",
            Protocol = "STARK-CLEARANCE-ACTIVE",
            Message = "STARK CLEARANCE VERIFIED: Welcome back, Sir. All systems initialized.",
            ExpiresAt = expiresAt
        };
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var config = _configService.GetConfig().Security;
        if (!config.RequireLogin) return true;

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(config.JwtSecret.PadRight(32));

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.FromMinutes(2)
            }, out _);

            // Also check DB session
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ExpiresAt FROM AuthSessions WHERE Token = $t";
            cmd.Parameters.AddWithValue("$t", token);
            var result = await cmd.ExecuteScalarAsync();

            if (result != null && DateTime.TryParse(result.ToString(), out var expiresAt))
            {
                return expiresAt > DateTime.UtcNow;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task LogoutAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        try
        {
            using var connection = _database.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM AuthSessions WHERE Token = $t";
            cmd.Parameters.AddWithValue("$t", token);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout session removal");
        }
    }

    public void SetMasterPasscode(string newPasscode)
    {
        var config = _configService.GetConfig();
        config.Security.MasterPasscode = newPasscode;
        config.Security.MasterPasscodeHash = HashPassword(newPasscode);
        _configService.UpdateConfigAsync(config).Wait();
    }

    private static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32
        );
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPasswordHash(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;

        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] expectedHash = Convert.FromBase64String(parts[1]);

        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32
        );

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string GenerateJwtToken(string secret, int expiryMinutes)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(secret.PadRight(32));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "Tony Stark"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("Clearance", "Level_10")
            }),
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
