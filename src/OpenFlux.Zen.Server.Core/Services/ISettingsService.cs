using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface ISettingsService
{
    Task<AppSettings> GetSettingsAsync();
    Task<AppSettings> UpdateSettingsAsync(AppSettings settings);
    Task<string> RegenerateSecretPathAsync();
    Task<bool> SetAutoStartAsync(bool enabled);
    void SaveCredentialsFile(string username, string plainPassword, string secretPath, string? publicUrl);
    void InvalidateCache();
}

