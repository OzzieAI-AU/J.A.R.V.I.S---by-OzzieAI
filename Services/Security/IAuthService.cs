using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Services.Security;

public interface IAuthService
{
    Task<AuthResponse> AuthenticateAsync(string passcode, string ipAddress);
    Task<bool> ValidateTokenAsync(string token);
    Task LogoutAsync(string token);
    void SetMasterPasscode(string newPasscode);
    bool IsLoginRequired();
}
