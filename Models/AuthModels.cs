namespace OzzieAI.Jarvis.Models;

public class LoginRequest
{
    public string Passcode { get; set; } = string.Empty;
    public string? ClearanceLevel { get; set; }
}

public class AuthResponse
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Message { get; set; }
    public string Clearance { get; set; } = "GUEST";
    public string Protocol { get; set; } = "STARK-CLEARANCE-10";
    public DateTime? ExpiresAt { get; set; }
}

public class UserSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string ClearanceLevel { get; set; } = "OWNER_LEVEL_10";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public string IpAddress { get; set; } = string.Empty;
}
