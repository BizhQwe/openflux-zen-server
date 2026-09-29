using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFlux.Zen.Server.Common;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public sealed class PanelUpdateService : IPanelUpdateService
{
    private const string GitHubApiLatestRelease = "https://api.github.com/repos/BizhQwe/openflux-zen-server/releases/latest";
    private const string FallbackDefaultVersion = "v1.0.30";

    private readonly ILogger<PanelUpdateService> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private volatile bool _isUpdating;

    public PanelUpdateService(ILogger<PanelUpdateService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(4)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenFluxZenServer-PanelUpdater/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
    }

    public async Task<PanelVersionInfo> GetVersionInfoAsync(bool forceCheck = false, CancellationToken ct = default)
    {
        var meta = await LoadMetadataAsync();

        var shouldCheckRemote = forceCheck ||
                                meta.LastCheckedAt == null ||
                                DateTime.UtcNow - meta.LastCheckedAt.Value > TimeSpan.FromHours(1);

        if (shouldCheckRemote)
        {
            try
            {
                var remoteRelease = await FetchLatestReleaseAsync(ct);
                if (remoteRelease != null)
                {
                    meta.LatestVersion = remoteRelease.TagName;
                    meta.ReleaseUrl = remoteRelease.HtmlUrl;
                    meta.ReleaseNotes = remoteRelease.Body;
                    meta.PublishedAt = remoteRelease.PublishedAt;
                    meta.LastCheckedAt = DateTime.UtcNow;
                    await SaveMetadataAsync(meta);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check latest OpenFlux Zen Server release from GitHub");
            }
        }

        var isUpdateAvailable = !string.IsNullOrWhiteSpace(meta.LatestVersion) &&
                                IsNewerVersion(meta.LatestVersion, meta.CurrentVersion);

        return new PanelVersionInfo
        {
            CurrentVersion = meta.CurrentVersion,
            LatestVersion = meta.LatestVersion ?? meta.CurrentVersion,
            IsUpdateAvailable = isUpdateAvailable,
            ReleaseUrl = meta.ReleaseUrl ?? "https://github.com/BizhQwe/openflux-zen-server/releases",
            ReleaseNotes = meta.ReleaseNotes,
            PublishedAt = meta.PublishedAt,
            LastCheckedAt = meta.LastCheckedAt,
            IsUpdating = _isUpdating
        };
    }

    public async Task<PanelUpdateResult> UpdatePanelAsync(CancellationToken ct = default)
    {
        if (!await _updateLock.WaitAsync(0, ct))
        {
            return new PanelUpdateResult
            {
                Success = false,
                Message = "Update is already in progress. Please wait."
            };
        }

        _isUpdating = true;
        var meta = await LoadMetadataAsync();
        var previousVersion = meta.CurrentVersion;

        try
        {
            _logger.LogInformation("Checking latest OpenFlux Zen Server release package...");
            var release = await FetchLatestReleaseAsync(ct);
            if (release == null)
            {
                return new PanelUpdateResult
                {
                    Success = false,
                    Message = "Unable to fetch latest release metadata from GitHub.",
                    PreviousVersion = previousVersion
                };
            }

            var rid = GetCurrentRid();
            var targetAssetName = $"openflux-zen-server-{rid}.zip";
            var targetAsset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, targetAssetName, StringComparison.OrdinalIgnoreCase));

            if (targetAsset == null || string.IsNullOrWhiteSpace(targetAsset.BrowserDownloadUrl))
            {
                var msg = $"Matching release package '{targetAssetName}' not found in release {release.TagName}.";
                _logger.LogError(msg);
                return new PanelUpdateResult
                {
                    Success = false,
                    Message = msg,
                    PreviousVersion = previousVersion
                };
            }

            var tempDir = Path.GetTempPath();
            var tempZip = Path.Combine(tempDir, $"oflux_panel_update_{Guid.NewGuid():N}.zip");
            var stageDir = Path.Combine(tempDir, $"oflux_panel_stage_{Guid.NewGuid():N}");

            _logger.LogInformation("Downloading panel release archive {Asset} ({Size} bytes) from {Url}...",
                targetAsset.Name, targetAsset.Size, targetAsset.BrowserDownloadUrl);

            using (var response = await _httpClient.GetAsync(targetAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await stream.CopyToAsync(fileStream, ct);
            }

            var zipFi = new FileInfo(tempZip);
            if (!zipFi.Exists || zipFi.Length < 10_000_000)
            {
                try { File.Delete(tempZip); } catch { }
                return new PanelUpdateResult
                {
                    Success = false,
                    Message = "Downloaded panel archive is incomplete or corrupted.",
                    PreviousVersion = previousVersion
                };
            }

            // Extract into staging directory
            Directory.CreateDirectory(stageDir);
            ZipFile.ExtractToDirectory(tempZip, stageDir, overwriteFiles: true);

            // Ensure stageDir does not overwrite user data directory
            var stageData = Path.Combine(stageDir, "data");
            if (Directory.Exists(stageData))
            {
                try { Directory.Delete(stageData, true); } catch { }
            }

            var appDir = AppPaths.ResolveAppDirectory();
            _logger.LogInformation("Applying update to target application directory: {AppDir}...", appDir);

            // Update metadata before restarting
            meta.CurrentVersion = release.TagName;
            meta.LatestVersion = release.TagName;
            meta.InstalledAt = DateTime.UtcNow;
            meta.LastCheckedAt = DateTime.UtcNow;
            meta.ReleaseUrl = release.HtmlUrl;
            meta.ReleaseNotes = release.Body;
            meta.PublishedAt = release.PublishedAt;
            await SaveMetadataAsync(meta);

            // Launch detached self-updater script
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                LaunchWindowsUpdaterScript(tempZip, stageDir, appDir);
            }
            else
            {
                LaunchLinuxUpdaterScript(tempZip, stageDir, appDir);
            }

            return new PanelUpdateResult
            {
                Success = true,
                Message = $"OpenFlux Zen Server is updating to {release.TagName} and restarting. The panel will reload in a few moments.",
                PreviousVersion = previousVersion,
                NewVersion = release.TagName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update OpenFlux Zen Server panel");
            return new PanelUpdateResult
            {
                Success = false,
                Message = $"Update failed: {ex.Message}",
                PreviousVersion = previousVersion
            };
        }
        finally
        {
            _isUpdating = false;
            _updateLock.Release();
        }
    }

    private void LaunchLinuxUpdaterScript(string tempZip, string stageDir, string appDir)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"oflux_apply_panel_{Guid.NewGuid():N}.sh");
        var scriptContent = $@"#!/bin/bash
sleep 2
# Copy new binaries and assets over application directory
cp -rf ""{stageDir}/""* ""{appDir}/""
chmod +x ""{appDir}/OpenFlux.Zen.Server.Web"" ""{appDir}/OpenFluxZenServer"" 2>/dev/null || true
if [ -d ""/usr/local/bin"" ]; then
    cp -f ""{appDir}/OpenFluxZenServer"" ""/usr/local/bin/OpenFluxZenServer"" 2>/dev/null || true
    chmod +x ""/usr/local/bin/OpenFluxZenServer"" 2>/dev/null || true
fi
systemctl restart openflux-zen-server.service 2>/dev/null || true
rm -rf ""{stageDir}"" ""{tempZip}"" ""$0""
";
        File.WriteAllText(scriptPath, scriptContent);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = scriptPath,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            _logger.LogInformation("Detached Linux update script launched: {Script}", scriptPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute detached Linux update script");
        }
    }

    private void LaunchWindowsUpdaterScript(string tempZip, string stageDir, string appDir)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"oflux_apply_panel_{Guid.NewGuid():N}.bat");
        var scriptContent = $@"@echo off
timeout /t 2 /nobreak >nul
taskkill /f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1
xcopy ""{stageDir}\*"" ""{appDir}\"" /s /e /y /q >nul 2>&1
if exist ""{appDir}\OpenFluxZenServer.exe"" (
    copy /y ""{appDir}\OpenFluxZenServer.exe"" ""%SystemRoot%\System32\OpenFluxZenServer.exe"" >nul 2>&1
)
schtasks /run /tn ""OpenFluxZenServer"" >nul 2>&1
if errorlevel 1 (
    start """" ""{appDir}\OpenFlux.Zen.Server.Web.exe""
)
rd /s /q ""{stageDir}"" >nul 2>&1
del ""{tempZip}"" >nul 2>&1
del ""%~f0"" >nul 2>&1
";
        File.WriteAllText(scriptPath, scriptContent);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            _logger.LogInformation("Detached Windows update script launched: {Script}", scriptPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute detached Windows update script");
        }
    }

    private static string GetCurrentRid()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };
        return $"{os}-{arch}";
    }

    private async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(GitHubApiLatestRelease, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub release check for panel returned status code: {Code}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch latest GitHub release for OpenFlux Zen Server panel");
            return null;
        }
    }

    private async Task<PanelVersionMetadata> LoadMetadataAsync()
    {
        var path = AppPaths.GetPanelVersionFilePath();
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var meta = JsonSerializer.Deserialize<PanelVersionMetadata>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (meta != null && !string.IsNullOrWhiteSpace(meta.CurrentVersion))
                {
                    return meta;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read panel version metadata file {Path}", path);
            }
        }

        var defaultMeta = new PanelVersionMetadata
        {
            CurrentVersion = FallbackDefaultVersion,
            InstalledAt = DateTime.UtcNow
        };
        await SaveMetadataAsync(defaultMeta);
        return defaultMeta;
    }

    private async Task SaveMetadataAsync(PanelVersionMetadata metadata)
    {
        try
        {
            var path = AppPaths.GetPanelVersionFilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist panel version metadata to file");
        }
    }

    private static bool IsNewerVersion(string latestTag, string currentTag)
    {
        if (string.Equals(latestTag, currentTag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var vLatest = latestTag.TrimStart('v', 'V');
        var vCurrent = currentTag.TrimStart('v', 'V');

        if (Version.TryParse(vLatest, out var parsedLatest) && Version.TryParse(vCurrent, out var parsedCurrent))
        {
            return parsedLatest > parsedCurrent;
        }

        return !string.Equals(latestTag, currentTag, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PanelVersionMetadata
    {
        public string CurrentVersion { get; set; } = FallbackDefaultVersion;
        public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastCheckedAt { get; set; }
        public string? LatestVersion { get; set; }
        public string? ReleaseUrl { get; set; }
        public string? ReleaseNotes { get; set; }
        public DateTime? PublishedAt { get; set; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
