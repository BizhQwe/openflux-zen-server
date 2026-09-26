using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenFlux.Zen.Server.Data;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _credentialsFilePath;
    private volatile AppSettings? _cachedSettings;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public SettingsService(ILogger<SettingsService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _credentialsFilePath = OpenFlux.Zen.Server.Common.AppPaths.GetCredentialsPath();
    }

    public void InvalidateCache()
    {
        _cachedSettings = null;
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        if (_cachedSettings != null)
        {
            return _cachedSettings;
        }

        await _cacheLock.WaitAsync();
        try
        {
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
            if (settings == null)
            {
                settings = await InitializeDefaultSettingsAsync(db);
            }
            _cachedSettings = settings;
            return settings;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<AppSettings> UpdateSettingsAsync(AppSettings updated)
    {
        await _cacheLock.WaitAsync();
        try
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
            if (!string.IsNullOrWhiteSpace(updated.Language))
            {
                current.Language = updated.Language.Trim().ToLowerInvariant();
            }
            current.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            _cachedSettings = current;
            return current;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<string> RegenerateSecretPathAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
            if (settings == null)
            {
                settings = await InitializeDefaultSettingsAsync(db);
            }

            var newSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            settings.SecretPath = newSecret;
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            _cachedSettings = settings;
            _logger.LogInformation("Regenerated secret path: {Path}", newSecret);
            return newSecret;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<bool> SetAutoStartAsync(bool enabled)
    {
        await _cacheLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
            if (settings == null)
            {
                settings = await InitializeDefaultSettingsAsync(db);
            }

            settings.AutoStartEnabled = enabled;
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            _cachedSettings = settings;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var action = enabled ? "enable" : "disable";
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = $"{action} openflux-zen-server.service",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p?.WaitForExit(3000);

                if (File.Exists("/etc/systemd/system/openflux-zrok.service"))
                {
                    using var p2 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "systemctl",
                        Arguments = $"{action} openflux-zrok.service",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    p2?.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update systemctl autostart configuration");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var startType = enabled ? "auto" : "demand";
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"config OpenFluxZenServer start= {startType}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Windows service autostart configuration");
            }
        }

        _logger.LogInformation("Autostart configuration updated: {Enabled}", enabled);
        return true;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<bool> SetLanguageAsync(string language)
    {
        var lang = string.Equals(language?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
        await _cacheLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
            if (settings == null)
            {
                settings = await InitializeDefaultSettingsAsync(db);
            }

            settings.Language = lang;
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            _cachedSettings = settings;
            _logger.LogInformation("Language updated: {Language}", lang);
            return true;
        }
        finally
        {
            _cacheLock.Release();
        }
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

    public void UpdatePublicUrlInCredentials(string publicUrl, string? localtunnelPassword = null)
    {
        try
        {
            if (File.Exists(_credentialsFilePath))
            {
                var content = File.ReadAllText(_credentialsFilePath);
                using var doc = JsonDocument.Parse(content);
                var dict = new Dictionary<string, object?>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n)) dict[prop.Name] = n;
                    else if (prop.Value.ValueKind == JsonValueKind.True) dict[prop.Name] = true;
                    else if (prop.Value.ValueKind == JsonValueKind.False) dict[prop.Name] = false;
                    else dict[prop.Name] = prop.Value.GetString();
                }
                dict["publicUrl"] = publicUrl;
                if (!string.IsNullOrEmpty(localtunnelPassword)) dict["localtunnelPassword"] = localtunnelPassword;
                dict["updatedAt"] = DateTime.UtcNow.ToString("o");
                var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_credentialsFilePath, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update public URL in credentials file");
        }
    }

    private async Task<AppSettings> InitializeDefaultSettingsAsync(AppDbContext db)
    {
        string? credUser = null;
        string? credPass = null;
        string? credSecret = null;
        string? credLang = null;
        string? credHost = null;
        string? credPubUrl = null;
        string? credMode = null;
        string? credDomain = null;
        string? credZrokToken = null;
        int? credPort = null;
        if (File.Exists(_credentialsFilePath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(_credentialsFilePath);
                var doc = JsonSerializer.Deserialize<JsonElement>(content);
                if (doc.TryGetProperty("username", out var u)) credUser = u.GetString();
                if (doc.TryGetProperty("password", out var pwd)) credPass = pwd.GetString();
                if (doc.TryGetProperty("secretPath", out var s)) credSecret = s.GetString();
                if (doc.TryGetProperty("language", out var l)) credLang = l.GetString();
                if (doc.TryGetProperty("host", out var h)) credHost = h.GetString();
                if (doc.TryGetProperty("publicUrl", out var pub)) credPubUrl = pub.GetString();
                if (doc.TryGetProperty("publishMode", out var pm)) credMode = pm.GetString();
                if (doc.TryGetProperty("domain", out var dm)) credDomain = dm.GetString();
                if (doc.TryGetProperty("zrokToken", out var zt)) credZrokToken = zt.GetString();
                if (doc.TryGetProperty("port", out var pt) && pt.TryGetInt32(out var pVal)) credPort = pVal;
            }
            catch { }
        }

        var initialUser = Environment.GetEnvironmentVariable("OPENFLUX_ADMIN_USER") ?? credUser ?? ("zen_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant());
        var initialPassword = Environment.GetEnvironmentVariable("OPENFLUX_ADMIN_PASSWORD") ?? credPass ?? AuthService.GenerateRandomPassword(16);
        var initialSecret = Environment.GetEnvironmentVariable("OPENFLUX_SECRET_PATH") ?? credSecret ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var initialLang = Environment.GetEnvironmentVariable("OPENFLUX_LANGUAGE") ?? credLang ?? "ru";
        var initialPort = 5000;
        if (int.TryParse(Environment.GetEnvironmentVariable("OPENFLUX_PORT"), out var portNum))
        {
            initialPort = portNum;
        }
        else if (credPort.HasValue)
        {
            initialPort = credPort.Value;
        }

        var initialHost = Environment.GetEnvironmentVariable("OPENFLUX_HOST") ?? credHost ?? "127.0.0.1";
        var initialPubUrl = Environment.GetEnvironmentVariable("OPENFLUX_PUBLIC_URL") ?? credPubUrl;
        var initialMode = Environment.GetEnvironmentVariable("OPENFLUX_PUBLISH_MODE") ?? credMode ?? "local";

        var (hash, salt) = AuthService.HashPassword(initialPassword);
        var settings = new AppSettings
        {
            Id = 1,
            Username = initialUser,
            PasswordHash = hash,
            PasswordSalt = salt,
            SecretPath = initialSecret,
            ListenHost = initialHost,
            ListenPort = initialPort,
            PublicUrl = initialPubUrl,
            PublishMode = initialMode,
            Domain = credDomain,
            ZrokToken = credZrokToken,
            AutoStartEnabled = true,
            Language = initialLang,
            UpdatedAt = DateTime.UtcNow
        };

        db.Settings.Add(settings);
        await db.SaveChangesAsync();

        SaveCredentialsFile(initialUser, initialPassword, initialSecret, initialPubUrl);
        _logger.LogInformation("Initialized default admin settings. Secret path: /{Secret}/", initialSecret);

        return settings;
    }
}
