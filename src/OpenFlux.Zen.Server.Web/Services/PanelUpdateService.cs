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
    private const string GitHubApiReleases = "https://api.github.com/repos/BizhQwe/openflux-zen-server/releases?per_page=30";
    private const string GitHubApiReleaseByTag = "https://api.github.com/repos/BizhQwe/openflux-zen-server/releases/tags/";
    private const string GitHubAtomReleases = "https://github.com/BizhQwe/openflux-zen-server/releases.atom";
    private const string FallbackDefaultVersion = "v1.0.69";

    private static List<ReleaseItemDto>? _cachedReleases;
    private static DateTime? _releasesCacheTime;

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

    public async Task<List<ReleaseItemDto>> GetAvailableReleasesAsync(CancellationToken ct = default)
    {
        if (_cachedReleases != null && _releasesCacheTime != null && DateTime.UtcNow - _releasesCacheTime.Value < TimeSpan.FromMinutes(5))
        {
            return _cachedReleases;
        }

        try
        {
            using var response = await _httpClient.GetAsync(GitHubApiReleases, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (releases != null && releases.Count > 0)
                {
                    var result = releases
                        .Where(r => !string.IsNullOrWhiteSpace(r.TagName))
                        .Select(r => new ReleaseItemDto
                        {
                            TagName = r.TagName,
                            Name = string.IsNullOrWhiteSpace(r.Name) ? r.TagName : r.Name,
                            PublishedAt = r.PublishedAt,
                            Prerelease = r.Prerelease,
                            HtmlUrl = r.HtmlUrl
                        }).ToList();

                    _cachedReleases = result;
                    _releasesCacheTime = DateTime.UtcNow;
                    return result;
                }
            }
            else
            {
                _logger.LogWarning("GitHub REST API releases check returned status code: {Code}. Falling back to Atom feed...", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch panel releases from GitHub API. Falling back to Atom feed...");
        }

        // Fallback: Atom feed (has NO rate limit and always works!)
        var atomReleases = await FetchReleasesFromAtomFeedAsync(ct);
        if (atomReleases.Count > 0)
        {
            _cachedReleases = atomReleases;
            _releasesCacheTime = DateTime.UtcNow;
            return atomReleases;
        }

        return _cachedReleases ?? new List<ReleaseItemDto>();
    }

    private async Task<List<ReleaseItemDto>> FetchReleasesFromAtomFeedAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubAtomReleases);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko)");
            using var response = await _httpClient.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode) return new List<ReleaseItemDto>();

            var xml = await response.Content.ReadAsStringAsync(ct);
            var result = new List<ReleaseItemDto>();

            var entryMatches = System.Text.RegularExpressions.Regex.Matches(
                xml,
                @"<entry>[\s\S]*?<link[^>]+href=""(?<url>https://github\.com/[^""]+/releases/tag/(?<tag>[^""]+))""[\s\S]*?<title>(?<title>[^<]*)</title>[\s\S]*?<updated>(?<date>[^<]*)</updated>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (System.Text.RegularExpressions.Match m in entryMatches)
            {
                var tag = m.Groups["tag"].Value.Trim();
                var title = m.Groups["title"].Value.Trim();
                var url = m.Groups["url"].Value.Trim();
                var dateStr = m.Groups["date"].Value.Trim();
                DateTime.TryParse(dateStr, out var publishedAt);

                if (!string.IsNullOrWhiteSpace(tag))
                {
                    result.Add(new ReleaseItemDto
                    {
                        TagName = tag,
                        Name = string.IsNullOrWhiteSpace(title) ? tag : title,
                        HtmlUrl = url,
                        PublishedAt = publishedAt != default ? publishedAt : DateTime.UtcNow
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse releases from GitHub Atom feed");
            return new List<ReleaseItemDto>();
        }
    }

    public async Task<PanelVersionInfo> GetVersionInfoAsync(bool forceCheck = false, CancellationToken ct = default)
    {
        var meta = await LoadMetadataAsync();

        var shouldCheckRemote = forceCheck ||
                                meta.LastCheckedAt == null ||
                                DateTime.UtcNow - meta.LastCheckedAt.Value > TimeSpan.FromMinutes(15);

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
                }
                meta.LastCheckedAt = DateTime.UtcNow;
                await SaveMetadataAsync(meta);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check latest OpenFlux Zen Server release from GitHub");
                meta.LastCheckedAt = DateTime.UtcNow;
                try { await SaveMetadataAsync(meta); } catch { }
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

    public async Task<PanelUpdateResult> UpdatePanelAsync(string? targetVersion = null, CancellationToken ct = default)
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
            GitHubRelease? release;
            if (!string.IsNullOrWhiteSpace(targetVersion))
            {
                _logger.LogInformation("Fetching OpenFlux Zen Server release for tag {Tag}...", targetVersion);
                release = await FetchReleaseByTagAsync(targetVersion.Trim(), ct);
            }
            else
            {
                _logger.LogInformation("Checking latest OpenFlux Zen Server release package...");
                release = await FetchLatestReleaseAsync(ct);
            }

            if (release == null)
            {
                return new PanelUpdateResult
                {
                    Success = false,
                    Message = !string.IsNullOrWhiteSpace(targetVersion)
                        ? $"Unable to fetch release metadata for tag '{targetVersion}' from GitHub."
                        : "Unable to fetch latest release metadata from GitHub.",
                    PreviousVersion = previousVersion
                };
            }

            var rid = GetCurrentRid();
            var targetAssetName = $"openflux-zen-server-{rid}.zip";
            var targetAsset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, targetAssetName, StringComparison.OrdinalIgnoreCase));

            var downloadUrl = targetAsset?.BrowserDownloadUrl;
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                downloadUrl = $"https://github.com/BizhQwe/openflux-zen-server/releases/download/{release.TagName.Trim()}/{targetAssetName}";
            }

            var tempDir = Path.GetTempPath();
            var tempZip = Path.Combine(tempDir, $"oflux_panel_update_{Guid.NewGuid():N}.zip");
            var stageDir = Path.Combine(tempDir, $"oflux_panel_stage_{Guid.NewGuid():N}");

            _logger.LogInformation("Downloading panel release archive {Asset} from {Url}...", targetAssetName, downloadUrl);

            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
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

            var appDir = Path.TrimEndingDirectorySeparator(AppPaths.ResolveAppDirectory());
            _logger.LogInformation("Applying update to target application directory: {AppDir}...", appDir);

            // Launch updater based on OS platform
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Update metadata before restarting
                meta.CurrentVersion = release.TagName;
                meta.LatestVersion = release.TagName;
                meta.InstalledAt = DateTime.UtcNow;
                meta.LastCheckedAt = DateTime.UtcNow;
                meta.ReleaseUrl = release.HtmlUrl;
                meta.ReleaseNotes = release.Body;
                meta.PublishedAt = release.PublishedAt;
                await SaveMetadataAsync(meta);

                LaunchWindowsUpdaterScript(tempZip, stageDir, appDir);
            }
            else
            {
                // In Linux, safely replace all files in-place using POSIX unlink, then schedule restart
                await ApplyLinuxUpdateInProcessAsync(tempZip, stageDir, appDir, meta, release);
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

    private async Task ApplyLinuxUpdateInProcessAsync(
        string tempZip,
        string stageDir,
        string appDir,
        PanelVersionMetadata meta,
        GitHubRelease release)
    {
        _logger.LogInformation("Applying Linux update in-place from {StageDir} to {AppDir}...", stageDir, appDir);

        try
        {
            CopyDirectorySafe(stageDir, appDir);
            UpdateLinuxGlobalCli(appDir);
        }
        finally
        {
            try { Directory.Delete(stageDir, true); } catch { }
            try { File.Delete(tempZip); } catch { }
        }

        // Persist new version metadata now that files have been successfully replaced
        meta.CurrentVersion = release.TagName;
        meta.LatestVersion = release.TagName;
        meta.InstalledAt = DateTime.UtcNow;
        meta.LastCheckedAt = DateTime.UtcNow;
        meta.ReleaseUrl = release.HtmlUrl;
        meta.ReleaseNotes = release.Body;
        meta.PublishedAt = release.PublishedAt;
        await SaveMetadataAsync(meta);

        // Schedule delayed restart to allow the HTTP response to be flushed to the browser
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500);
                RestartLinuxService(appDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delayed Linux server restart failed");
            }
        });
    }

    private void CopyDirectorySafe(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);

            // Never overwrite data directory (databases, credentials, configs)
            if (relativePath.StartsWith("data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("data" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relativePath, "data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destFile = Path.Combine(targetDir, relativePath);
            var destDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            if (File.Exists(destFile))
            {
                try
                {
                    // POSIX unlink: safely removes file name from directory while running process keeps its inode
                    File.Delete(destFile);
                }
                catch
                {
                    try
                    {
                        var backup = destFile + ".old." + Guid.NewGuid().ToString("N");
                        File.Move(destFile, backup);
                        try { File.Delete(backup); } catch { }
                    }
                    catch { }
                }
            }

            File.Copy(file, destFile, overwrite: true);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var fileName = Path.GetFileName(destFile);
                if (fileName == "OpenFlux.Zen.Server.Web" || fileName == "OpenFluxZenServer" || fileName.StartsWith("openflux"))
                {
                    try
                    {
                        File.SetUnixFileMode(destFile,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                    catch { }

                    try
                    {
                        using var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{destFile}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        proc?.WaitForExit(2000);
                    }
                    catch { }
                }
            }
        }
    }

    private static void UpdateLinuxGlobalCli(string appDir)
    {
        var cliSrc = Path.Combine(appDir, "OpenFluxZenServer");
        if (!File.Exists(cliSrc) || !Directory.Exists("/usr/local/bin"))
        {
            return;
        }

        var links = new[]
        {
            "/usr/local/bin/OpenFluxZenServer",
            "/usr/local/bin/openfluxzenserver",
            "/usr/local/bin/openflux",
            "/usr/local/bin/openflux-zen-server"
        };

        foreach (var link in links)
        {
            try
            {
                if (File.Exists(link))
                {
                    File.Delete(link);
                }
                File.Copy(cliSrc, link, overwrite: true);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(link,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }
            catch { }
        }
    }

    private void RestartLinuxService(string appDir)
    {
        _logger.LogInformation("Executing restart of OpenFlux Zen Server Linux service...");

        // 1. Try systemctl daemon-reload and restart with --no-block
        try
        {
            var reloadPsi = new ProcessStartInfo
            {
                FileName = "systemctl",
                Arguments = "daemon-reload",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var reloadProc = Process.Start(reloadPsi);
            reloadProc?.WaitForExit(2000);

            var restartPsi = new ProcessStartInfo
            {
                FileName = "systemctl",
                Arguments = "restart --no-block openflux-zen-server.service",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var restartProc = Process.Start(restartPsi);
            restartProc?.WaitForExit(3000);

            if (restartProc != null && restartProc.ExitCode == 0)
            {
                _logger.LogInformation("systemctl restart --no-block dispatched successfully. Terminating process to allow systemd to spin up new version.");
                Thread.Sleep(500);
                Environment.Exit(0);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "systemctl restart --no-block invocation failed");
        }

        // 2. Fallback: if not running under systemd or systemctl failed
        try
        {
            var exePath = Path.Combine(appDir, "OpenFlux.Zen.Server.Web");
            if (File.Exists(exePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nohup",
                    Arguments = $"\"{exePath}\" >/dev/null 2>&1 &",
                    WorkingDirectory = appDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to spawn server process in fallback restart");
        }

        Environment.Exit(0);
    }

    private void LaunchWindowsUpdaterScript(string tempZip, string stageDir, string appDir)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"oflux_apply_panel_{Guid.NewGuid():N}.bat");
        var scriptContent = $@"@echo off
timeout /t 2 /nobreak >nul
taskkill /f /im OpenFlux.Zen.Server.Web.exe >nul 2>&1
timeout /t 1 /nobreak >nul
del /f /q ""{appDir}\OpenFlux.Zen.Server.Web.exe"" >nul 2>&1
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
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (release != null) return release;
            }
            else
            {
                _logger.LogWarning("GitHub release check for panel returned status code: {Code}. Falling back to Atom feed...", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch latest GitHub release from API for panel. Falling back to Atom feed...");
        }

        // Fallback: Atom feed (has NO rate limit and always works!)
        try
        {
            var atomReleases = await FetchReleasesFromAtomFeedAsync(ct);
            if (atomReleases.Count > 0)
            {
                var first = atomReleases[0];
                return new GitHubRelease
                {
                    TagName = first.TagName,
                    Name = first.Name,
                    HtmlUrl = first.HtmlUrl ?? "",
                    PublishedAt = first.PublishedAt
                };
            }
        }
        catch { }

        return null;
    }

    private async Task<GitHubRelease?> FetchReleaseByTagAsync(string tagName, CancellationToken ct)
    {
        try
        {
            var url = $"{GitHubApiReleaseByTag}{Uri.EscapeDataString(tagName)}";
            using var response = await _httpClient.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (release != null) return release;
            }
            else
            {
                _logger.LogWarning("GitHub release check for panel tag {Tag} returned status code: {Code}. Using synthetic release info...", tagName, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch panel release for tag {Tag}. Using synthetic release info...", tagName);
        }

        // Fallback: Return synthetic GitHubRelease with standard download URLs
        return new GitHubRelease
        {
            TagName = tagName,
            Name = tagName,
            HtmlUrl = $"https://github.com/BizhQwe/openflux-zen-server/releases/tag/{tagName}"
        };
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
                    if (IsNewerVersion(FallbackDefaultVersion, meta.CurrentVersion))
                    {
                        meta.CurrentVersion = FallbackDefaultVersion;
                        await SaveMetadataAsync(meta);
                    }
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

    private static bool IsNewerVersion(string? latestTag, string? currentTag)
    {
        if (string.IsNullOrWhiteSpace(latestTag) || string.IsNullOrWhiteSpace(currentTag))
        {
            return false;
        }

        if (string.Equals(latestTag.Trim(), currentTag.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var vLatest = NormalizeVersionString(latestTag);
        var vCurrent = NormalizeVersionString(currentTag);

        if (string.Equals(vLatest, vCurrent, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Version.TryParse(vLatest, out var parsedLatest) && Version.TryParse(vCurrent, out var parsedCurrent))
        {
            return parsedLatest > parsedCurrent;
        }

        return false;
    }

    private static string NormalizeVersionString(string tag)
    {
        var clean = tag.Trim().TrimStart('v', 'V');
        var dashIdx = clean.IndexOf('-');
        if (dashIdx > 0)
        {
            clean = clean.Substring(0, dashIdx);
        }
        var plusIdx = clean.IndexOf('+');
        if (plusIdx > 0)
        {
            clean = clean.Substring(0, plusIdx);
        }
        return clean.Trim();
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

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

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
