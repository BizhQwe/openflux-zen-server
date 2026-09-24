using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenFlux.Zen.Server.Data;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public interface IAuthService
{
    Task<(bool Success, string Token, string Username)> LoginAsync(string username, string password);
    bool ValidateToken(string token);
    Task<bool> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<CredentialsResponse> GetCredentialsAsync();
}

public interface ISettingsService
{
    Task<AppSettings> GetSettingsAsync();
    Task<AppSettings> UpdateSettingsAsync(AppSettings settings);
    Task<string> RegenerateSecretPathAsync();
    void SaveCredentialsFile(string username, string plainPassword, string secretPath, string? publicUrl);
}

public sealed class AuthService : IAuthService, ISettingsService
{
    private readonly ILogger<AuthService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _credentialsFilePath;
    private readonly HashSet<string> _activeTokens = new();
    private readonly object _tokenLock = new();

    public AuthService(ILogger<AuthService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _credentialsFilePath = OpenFlux.Zen.Server.Common.AppPaths.GetCredentialsPath();
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = await InitializeDefaultSettingsAsync(db);
        }
        return settings;
    }

    public async Task<AppSettings> UpdateSettingsAsync(AppSettings updated)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var current = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
        if (current == null)
        {
            current = await InitializeDefaultSettingsAsync(db);
        }

        current.Username = updated.Username;
        current.SecretPath = updated.SecretPath;
        current.ListenHost = updated.ListenHost;
        current.ListenPort = updated.ListenPort;
        current.PublicUrl = updated.PublicUrl;
        current.PublishMode = updated.PublishMode;
        current.Domain = updated.Domain;
        current.ZrokToken = updated.ZrokToken;
        current.ZrokShareUrl = updated.ZrokShareUrl;
        current.AutoStartEnabled = updated.AutoStartEnabled;
        current.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return current;
    }

    public async Task<string> RegenerateSecretPathAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = await InitializeDefaultSettingsAsync(db);
        }

        var newSecret = "zen-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        settings.SecretPath = newSecret;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("Regenerated secret path: {Path}", newSecret);
        return newSecret;
    }

    public async Task<(bool Success, string Token, string Username)> LoginAsync(string username, string password)
    {
        var settings = await GetSettingsAsync();
        if (!string.Equals(settings.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "", "");
        }

        if (!VerifyPassword(password, settings.PasswordHash, settings.PasswordSalt))
        {
            return (false, "", "");
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        lock (_tokenLock)
        {
            _activeTokens.Add(token);
        }

        return (true, token, settings.Username);
    }

    public bool ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        lock (_tokenLock)
        {
            return _activeTokens.Contains(token);
        }
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null) return false;

        if (!VerifyPassword(currentPassword, settings.PasswordHash, settings.PasswordSalt))
        {
            return false;
        }

        var (hash, salt) = HashPassword(newPassword);
        settings.PasswordHash = hash;
        settings.PasswordSalt = salt;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        SaveCredentialsFile(settings.Username, newPassword, settings.SecretPath, settings.PublicUrl);
        _logger.LogInformation("Password changed successfully for user {User}", settings.Username);
        return true;
    }

    public async Task<CredentialsResponse> GetCredentialsAsync()
    {
        var settings = await GetSettingsAsync();
        string plainPassword = "******";

        if (File.Exists(_credentialsFilePath))
        {
            try
            {
                var content = File.ReadAllText(_credentialsFilePath);
                var doc = JsonSerializer.Deserialize<JsonElement>(content);
                if (doc.TryGetProperty("password", out var pwd))
                {
                    plainPassword = pwd.GetString() ?? plainPassword;
                }
            }
            catch { }
        }

        var localUrl = $"http://127.0.0.1:{settings.ListenPort}/{settings.SecretPath}/";
        return new CredentialsResponse
        {
            Username = settings.Username,
            Password = plainPassword,
            SecretPath = settings.SecretPath,
            LocalUrl = localUrl,
            PublicUrl = !string.IsNullOrWhiteSpace(settings.PublicUrl) ? settings.PublicUrl : localUrl
        };
    }

    public void SaveCredentialsFile(string username, string plainPassword, string secretPath, string? publicUrl)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_credentialsFilePath)!);
            var data = new
            {
                username,
                password = plainPassword,
                secretPath,
                publicUrl,
                updatedAt = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_credentialsFilePath, json);

            // Set file permissions to owner-only on Linux
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    File.SetUnixFileMode(_credentialsFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write credentials file");
        }
    }

    private async Task<AppSettings> InitializeDefaultSettingsAsync(AppDbContext db)
    {
        var initialUser = Environment.GetEnvironmentVariable("OPENFLUX_ADMIN_USER") ?? "admin";
        var initialPassword = Environment.GetEnvironmentVariable("OPENFLUX_ADMIN_PASSWORD") ?? GenerateRandomPassword(16);
        var initialSecret = Environment.GetEnvironmentVariable("OPENFLUX_SECRET_PATH") ?? ("zen-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant());
        var initialPort = 5000;
        if (int.TryParse(Environment.GetEnvironmentVariable("OPENFLUX_PORT"), out var p))
        {
            initialPort = p;
        }

        var (hash, salt) = HashPassword(initialPassword);
        var settings = new AppSettings
        {
            Id = 1,
            Username = initialUser,
            PasswordHash = hash,
            PasswordSalt = salt,
            SecretPath = initialSecret,
            ListenHost = "127.0.0.1",
            ListenPort = initialPort,
            PublishMode = "local",
            AutoStartEnabled = true,
            UpdatedAt = DateTime.UtcNow
        };

        db.Settings.Add(settings);
        await db.SaveChangesAsync();

        SaveCredentialsFile(initialUser, initialPassword, initialSecret, null);
        _logger.LogInformation("Initialized default admin settings. Secret path: /{Secret}/", initialSecret);

        return settings;
    }

    public static (string Hash, string Salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToBase64String(saltBytes);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            iterations: 100_000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
        var hash = Convert.ToBase64String(hashBytes);
        return (hash, salt);
    }

    public static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(storedSalt);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32);
            var computedHash = Convert.ToBase64String(hashBytes);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHash),
                Encoding.UTF8.GetBytes(storedHash));
        }
        catch
        {
            return false;
        }
    }

    public static string GenerateRandomPassword(int length = 16)
    {
        const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$";
        var res = new StringBuilder();
        var bytes = RandomNumberGenerator.GetBytes(length);
        foreach (byte b in bytes)
        {
            res.Append(validChars[b % validChars.Length]);
        }
        return res.ToString();
    }
}
