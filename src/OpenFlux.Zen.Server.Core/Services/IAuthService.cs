using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface IAuthService
{
    Task<(bool Success, string Token, string Username)> LoginAsync(string username, string password);
    bool ValidateToken(string token);
    void RevokeToken(string token);
    Task<bool> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<(bool Success, string Message, string NewUsername)> ChangeProfileAsync(string currentPassword, string? newUsername, string? newPassword);
    Task<CredentialsResponse> GetCredentialsAsync();
}
