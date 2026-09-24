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

    public SettingsService(ILogger<SettingsService> logger, IServiceScopeFactory scopeFactory)
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

        var newSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        settings.SecretPath = newSecret;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("Regenerated secret path: {Path}", newSecret);
        return newSecret;
    }

    public async Task<bool> SetAutoStartAsync(bool enabled)
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
        var initialUser = Environment.GetEnvironmentVariable("OPENFLUX_ADMIN_USER") ?? ("zen_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant());
        var initialPassword = Environment.GetEnvironmentVariable("OPENFLUX_ADMIN_PASSWORD") ?? AuthService.GenerateRandomPassword(16);
        var initialSecret = Environment.GetEnvironmentVariable("OPENFLUX_SECRET_PATH") ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var initialPort = 5000;
        if (int.TryParse(Environment.GetEnvironmentVariable("OPENFLUX_PORT"), out var p))
        {
            initialPort = p;
        }

        var (hash, salt) = AuthService.HashPassword(initialPassword);
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
}
