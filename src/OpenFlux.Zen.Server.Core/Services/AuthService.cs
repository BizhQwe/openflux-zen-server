using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenFlux.Zen.Server.Data;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public sealed class AuthService : IAuthService
{
    private readonly ILogger<AuthService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settingsService;
    private readonly string _credentialsFilePath;
    private readonly HashSet<string> _activeTokens = new();
    private readonly HashSet<string> _revokedTokens = new();
    private readonly object _tokenLock = new();

    public AuthService(ILogger<AuthService> logger, IServiceScopeFactory scopeFactory, ISettingsService settingsService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _settingsService = settingsService;
        _credentialsFilePath = OpenFlux.Zen.Server.Common.AppPaths.GetCredentialsPath();
    }

    public async Task<(bool Success, string Token, string Username)> LoginAsync(string username, string password)
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (!string.Equals(settings.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "", "");
        }

        if (!VerifyPassword(password, settings.PasswordHash, settings.PasswordSalt))
        {
            return (false, "", "");
        }

        var token = GenerateSignedToken(settings.Username, settings.PasswordHash);
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
            if (_revokedTokens.Contains(token)) return false;
            if (_activeTokens.Contains(token)) return true;
        }

        // Validate HMAC-SHA256 signed token: payload.signature
        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        try
        {
            var payloadB64 = parts[0];
            var sigB64 = parts[1];

            // Normalize base64 URL safe
            var mod = payloadB64.Length % 4;
            var paddedPayload = mod == 0 ? payloadB64 : payloadB64 + new string('=', 4 - mod);
            paddedPayload = paddedPayload.Replace('-', '+').Replace('_', '/');
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(paddedPayload));

            var payloadParts = payload.Split(':');
            if (payloadParts.Length != 3) return false;

            var username = payloadParts[0];
            if (!long.TryParse(payloadParts[1], out var expiresAt)) return false;

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt) return false;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = db.Settings.FirstOrDefault(s => s.Id == 1);
            if (settings == null) return false;

            if (!string.Equals(settings.Username, username, StringComparison.OrdinalIgnoreCase)) return false;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.PasswordHash));
            var expectedSigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
            var expectedSigB64 = Convert.ToBase64String(expectedSigBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sigB64), Encoding.UTF8.GetBytes(expectedSigB64)))
            {
                lock (_tokenLock)
                {
                    _activeTokens.Add(token);
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Token validation exception");
            return false;
        }

        return false;
    }

    public void RevokeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        lock (_tokenLock)
        {
            _activeTokens.Remove(token);
            _revokedTokens.Add(token);
        }
    }

    private static string GenerateSignedToken(string username, string keyMaterial)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var payload = $"{username}:{expiresAt}:{nonce}";
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keyMaterial));
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        var sigB64 = Convert.ToBase64String(sigBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{payloadB64}.{sigB64}";
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var (success, _, _) = await ChangeProfileAsync(currentPassword, null, newPassword);
        return success;
    }

    public async Task<(bool Success, string Message, string NewUsername)> ChangeProfileAsync(string currentPassword, string? newUsername, string? newPassword)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null) return (false, "Настройки не найдены", "");

        if (!VerifyPassword(currentPassword, settings.PasswordHash, settings.PasswordSalt))
        {
            return (false, "Неверный текущий пароль", settings.Username);
        }

        bool updated = false;
        string effectivePassword = "******";
        if (File.Exists(_credentialsFilePath))
        {
            try
            {
                var content = File.ReadAllText(_credentialsFilePath);
                var doc = JsonSerializer.Deserialize<JsonElement>(content);
                if (doc.TryGetProperty("password", out var pwd))
                {
                    effectivePassword = pwd.GetString() ?? effectivePassword;
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(newUsername) && !string.Equals(newUsername.Trim(), settings.Username, StringComparison.Ordinal))
        {
            settings.Username = newUsername.Trim();
            updated = true;
        }

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            var (hash, salt) = HashPassword(newPassword);
            settings.PasswordHash = hash;
            settings.PasswordSalt = salt;
            effectivePassword = newPassword;
            updated = true;
        }

        if (updated)
        {
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            _settingsService.SaveCredentialsFile(settings.Username, effectivePassword, settings.SecretPath, settings.PublicUrl);
            _logger.LogInformation("Profile updated successfully for user {User}", settings.Username);
        }

        return (true, "Учётные данные успешно обновлены", settings.Username);
    }

    public async Task<CredentialsResponse> GetCredentialsAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
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
