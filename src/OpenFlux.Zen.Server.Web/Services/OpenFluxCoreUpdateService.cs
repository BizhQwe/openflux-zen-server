using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFlux.Zen.Server.Common;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Services;

public sealed class OpenFluxCoreUpdateService : IOpenFluxCoreUpdateService
{
    private const string GitHubApiLatestRelease = "https://api.github.com/repos/p1neappleXpress/OpenFlux/releases/latest";
    private const string GitHubApiReleases = "https://api.github.com/repos/p1neappleXpress/OpenFlux/releases?per_page=30";
    private const string GitHubApiReleaseByTag = "https://api.github.com/repos/p1neappleXpress/OpenFlux/releases/tags/";
    private const string GitHubAtomReleases = "https://github.com/p1neappleXpress/OpenFlux/releases.atom";
    private const string FallbackDefaultVersion = "v0.3.0";

    private static List<ReleaseItemDto>? _cachedReleases;
    private static DateTime? _releasesCacheTime;

    private readonly ILogger<OpenFluxCoreUpdateService> _logger;
    private readonly IOpenFluxBinaryResolver _binaryResolver;
    private readonly ITunnelManager _tunnelManager;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private volatile bool _isUpdating;

    public OpenFluxCoreUpdateService(
        ILogger<OpenFluxCoreUpdateService> logger,
        IOpenFluxBinaryResolver binaryResolver,
        ITunnelManager tunnelManager)
    {
        _logger = logger;
        _binaryResolver = binaryResolver;
        _tunnelManager = tunnelManager;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenFluxZenServer-CoreUpdater/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
    }

    public async Task<List<ReleaseItemDto>> GetAvailableReleasesAsync(CancellationToken ct = default)
    {
        if (_cachedReleases != null && _releasesCacheTime != null &&
            DateTime.UtcNow - _releasesCacheTime.Value < TimeSpan.FromMinutes(5))
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
                if (releases != null)
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
                _logger.LogWarning("GitHub REST API releases check for OpenFlux returned status code: {Code}. Falling back to Atom feed...", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch OpenFlux core releases list from GitHub API. Falling back to Atom feed...");
        }

        // Fallback: Atom feed (no rate limits)
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
            _logger.LogWarning(ex, "Failed to parse OpenFlux releases from GitHub Atom feed");
            return new List<ReleaseItemDto>();
        }
    }

    public async Task<OpenFluxCoreVersionInfo> GetVersionInfoAsync(bool forceCheck = false, CancellationToken ct = default)
    {
        var meta = await LoadMetadataAsync();
        var binaryPath = _binaryResolver.GetBinaryPath();
        var binaryName = _binaryResolver.GetExpectedBinaryName();
        long binarySize = 0;

        if (File.Exists(binaryPath))
        {
            try
            {
                var fi = new FileInfo(binaryPath);
                binarySize = fi.Length;
            }
            catch { }
        }

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
                _logger.LogWarning(ex, "Failed to check latest OpenFlux release from GitHub");
                meta.LastCheckedAt = DateTime.UtcNow;
                try { await SaveMetadataAsync(meta); } catch { }
            }
        }

        var isUpdateAvailable = !string.IsNullOrWhiteSpace(meta.LatestVersion) &&
                                IsNewerVersion(meta.LatestVersion, meta.CurrentVersion);

        return new OpenFluxCoreVersionInfo
        {
            CurrentVersion = meta.CurrentVersion,
            LatestVersion = meta.LatestVersion ?? meta.CurrentVersion,
            IsUpdateAvailable = isUpdateAvailable,
            ReleaseUrl = meta.ReleaseUrl ?? "https://github.com/p1neappleXpress/OpenFlux/releases",
            ReleaseNotes = meta.ReleaseNotes,
            PublishedAt = meta.PublishedAt,
            BinaryName = binaryName,
            BinaryPath = binaryPath,
            BinarySizeBytes = binarySize,
            LastCheckedAt = meta.LastCheckedAt,
            IsUpdating = _isUpdating
        };
    }

    public async Task<OpenFluxCoreUpdateResult> UpdateCoreAsync(string? targetVersion = null, CancellationToken ct = default)
    {
        if (!await _updateLock.WaitAsync(0, ct))
        {
            return new OpenFluxCoreUpdateResult
            {
                Success = false,
                Message = "Update is already in progress. Please wait."
            };
        }

        _isUpdating = true;
        var meta = await LoadMetadataAsync();
        var previousVersion = meta.CurrentVersion;
        var runningTunnelIds = new List<Guid>();

        try
        {
            GitHubRelease? release = null;
            if (!string.IsNullOrWhiteSpace(targetVersion))
            {
                _logger.LogInformation("Fetching official OpenFlux release for tag {Tag}...", targetVersion);
                release = await FetchReleaseByTagAsync(targetVersion.Trim(), ct);
            }
            else
            {
                _logger.LogInformation("Starting OpenFlux core update check...");
                release = await FetchLatestReleaseAsync(ct);
            }

            var targetBinaryName = _binaryResolver.GetExpectedBinaryName();
            var targetBinaryPath = ResolveWritableBinaryPath(targetBinaryName);
            var targetDir = Path.GetDirectoryName(targetBinaryPath)!;
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Build prioritized candidate download URLs
            var downloadUrls = new List<string>();

            var targetAsset = release?.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, targetBinaryName, StringComparison.OrdinalIgnoreCase));
            if (targetAsset != null && !string.IsNullOrWhiteSpace(targetAsset.BrowserDownloadUrl))
            {
                downloadUrls.Add(targetAsset.BrowserDownloadUrl);
            }

            if (!string.IsNullOrWhiteSpace(targetVersion))
            {
                downloadUrls.Add($"https://github.com/p1neappleXpress/OpenFlux/releases/download/{targetVersion.Trim()}/{targetBinaryName}");
            }
            if (release != null && !string.IsNullOrWhiteSpace(release.TagName))
            {
                downloadUrls.Add($"https://github.com/p1neappleXpress/OpenFlux/releases/download/{release.TagName.Trim()}/{targetBinaryName}");
            }
            downloadUrls.Add($"https://github.com/p1neappleXpress/OpenFlux/releases/latest/download/{targetBinaryName}");
            downloadUrls.Add($"https://github.com/p1neappleXpress/OpenFlux/releases/download/{FallbackDefaultVersion}/{targetBinaryName}");

            downloadUrls = downloadUrls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var tempDownloadPath = Path.Combine(targetDir, $"{targetBinaryName}.download_{Guid.NewGuid():N}");
            bool downloadSuccess = false;
            string? lastError = null;

            foreach (var url in downloadUrls)
            {
                try
                {
                    _logger.LogInformation("Downloading official OpenFlux core asset from {Url} to {TempPath}...", url, tempDownloadPath);
                    using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Download attempt from {Url} returned HTTP {StatusCode}", url, response.StatusCode);
                        continue;
                    }

                    await using (var stream = await response.Content.ReadAsStreamAsync(ct))
                    await using (var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        await stream.CopyToAsync(fileStream, ct);
                    }

                    var downloadedFi = new FileInfo(tempDownloadPath);
                    if (downloadedFi.Exists && downloadedFi.Length >= 1_000_000)
                    {
                        downloadSuccess = true;
                        _logger.LogInformation("Successfully downloaded {BinaryName} ({SizeMb} MB) from {Url}",
                            targetBinaryName, Math.Round(downloadedFi.Length / 1024.0 / 1024.0, 2), url);
                        break;
                    }
                    else
                    {
                        try { File.Delete(tempDownloadPath); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    _logger.LogWarning(ex, "Download attempt failed from {Url}", url);
                    try { if (File.Exists(tempDownloadPath)) File.Delete(tempDownloadPath); } catch { }
                }
            }

            if (!downloadSuccess)
            {
                return new OpenFluxCoreUpdateResult
                {
                    Success = false,
                    Message = $"Failed to download OpenFlux core binary from GitHub releases. {(lastError != null ? $"Error: {lastError}" : "")}".Trim(),
                    PreviousVersion = previousVersion
                };
            }

            // Ensure executable permissions on downloaded file for Linux/macOS
            SetExecutablePermissions(tempDownloadPath);

            // Temporarily stop running tunnels
            try
            {
                var allTunnels = await _tunnelManager.GetAllAsync();
                runningTunnelIds = allTunnels.Where(t => t.Status == TunnelStatus.Running).Select(t => t.Id).ToList();
                foreach (var tid in runningTunnelIds)
                {
                    _logger.LogInformation("Stopping tunnel {Id} for core binary replacement...", tid);
                    await _tunnelManager.StopAsync(tid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to gracefully stop running tunnels prior to binary update.");
            }

            // Small delay to ensure process handles are fully released
            await Task.Delay(500, ct);

            // Safe binary replacement
            var backupPath = Path.Combine(targetDir, $"{targetBinaryName}.old_{DateTime.UtcNow:yyyyMMddHHmmss}");
            try
            {
                if (File.Exists(targetBinaryPath))
                {
                    File.Move(targetBinaryPath, backupPath, overwrite: true);
                }

                File.Move(tempDownloadPath, targetBinaryPath, overwrite: true);
                SetExecutablePermissions(targetBinaryPath);

                // Attempt to clean up old backup
                try
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replace binary file {Path}. Rolling back...", targetBinaryPath);
                if (File.Exists(backupPath) && !File.Exists(targetBinaryPath))
                {
                    try { File.Move(backupPath, targetBinaryPath, overwrite: true); } catch { }
                }
                try { File.Delete(tempDownloadPath); } catch { }

                return new OpenFluxCoreUpdateResult
                {
                    Success = false,
                    Message = $"File replacement failed: {ex.Message}",
                    PreviousVersion = previousVersion
                };
            }

            // Invalidate cached path in resolver
            _binaryResolver.InvalidateCache();

            // Update metadata
            var resolvedVersion = release?.TagName ?? targetVersion?.Trim() ?? FallbackDefaultVersion;
            meta.CurrentVersion = resolvedVersion;
            meta.LatestVersion = resolvedVersion;
            meta.InstalledAt = DateTime.UtcNow;
            meta.LastCheckedAt = DateTime.UtcNow;
            meta.ReleaseUrl = release?.HtmlUrl ?? $"https://github.com/p1neappleXpress/OpenFlux/releases/tag/{resolvedVersion}";
            meta.ReleaseNotes = release?.Body ?? "Downloaded official OpenFlux release binary";
            meta.PublishedAt = release?.PublishedAt ?? DateTime.UtcNow;
            await SaveMetadataAsync(meta);

            _logger.LogInformation("OpenFlux core updated successfully to {Version} ({Path})", resolvedVersion, targetBinaryPath);

            // Restart previously running tunnels
            foreach (var tid in runningTunnelIds)
            {
                try
                {
                    _logger.LogInformation("Restarting tunnel {Id} after core update...", tid);
                    await _tunnelManager.StartAsync(tid);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restart tunnel {Id} after core update.", tid);
                }
            }

            return new OpenFluxCoreUpdateResult
            {
                Success = true,
                Message = $"OpenFlux core successfully updated to {resolvedVersion}",
                PreviousVersion = previousVersion,
                NewVersion = resolvedVersion
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while updating OpenFlux core");
            return new OpenFluxCoreUpdateResult
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

    private string ResolveWritableBinaryPath(string binaryName)
    {
        var current = _binaryResolver.GetBinaryPath();
        if (File.Exists(current))
        {
            return current;
        }

        var runtimesDir = Path.Combine(AppPaths.ResolveAppDirectory(), "runtimes");
        return Path.Combine(runtimesDir, binaryName);
    }

    private void SetExecutablePermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path,
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
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            proc?.WaitForExit(3000);
        }
        catch { }
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
                _logger.LogWarning("GitHub release check for OpenFlux returned status code: {Code}. Falling back to Atom feed...", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch latest GitHub release from API for OpenFlux. Falling back to Atom feed...");
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
                _logger.LogWarning("GitHub release check for OpenFlux tag {Tag} returned status code: {Code}. Using synthetic release info...", tagName, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch OpenFlux release for tag {Tag}. Using synthetic release info...", tagName);
        }

        // Fallback: Return synthetic GitHubRelease with standard download URLs
        return new GitHubRelease
        {
            TagName = tagName,
            Name = tagName,
            HtmlUrl = $"https://github.com/p1neappleXpress/OpenFlux/releases/tag/{tagName}"
        };
    }

    private async Task<CoreVersionMetadata> LoadMetadataAsync()
    {
        var path = AppPaths.GetCoreVersionFilePath();
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var meta = JsonSerializer.Deserialize<CoreVersionMetadata>(json, new JsonSerializerOptions
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
                _logger.LogWarning(ex, "Failed to read core version metadata file {Path}", path);
            }
        }

        var defaultMeta = new CoreVersionMetadata
        {
            CurrentVersion = FallbackDefaultVersion,
            InstalledAt = DateTime.UtcNow
        };
        await SaveMetadataAsync(defaultMeta);
        return defaultMeta;
    }

    private async Task SaveMetadataAsync(CoreVersionMetadata metadata)
    {
        try
        {
            var path = AppPaths.GetCoreVersionFilePath();
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
            _logger.LogWarning(ex, "Failed to persist core version metadata to file");
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

    private sealed class CoreVersionMetadata
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
