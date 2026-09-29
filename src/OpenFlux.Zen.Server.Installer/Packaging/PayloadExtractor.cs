using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenFlux.Zen.Server.Installer.Platform;

namespace OpenFlux.Zen.Server.Installer.Packaging;

public static class PayloadExtractor
{
    public static string GetCurrentRid()
    {
        var os = OperatingSystem.IsWindows() ? "win" : "linux";
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };
        return $"{os}-{arch}";
    }

    public static string GetOpenFluxBinaryName()
    {
        var isWin = OperatingSystem.IsWindows();
        var isArm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        if (isWin)
        {
            return isArm64 ? "openflux-windows-arm64.exe" : "openflux-windows-amd64.exe";
        }
        return isArm64 ? "openflux-linux-arm64" : "openflux-linux-amd64";
    }

    public static async Task ExtractPayloadAsync(string installDir, Action<string>? onProgress = null)
    {
        Directory.CreateDirectory(installDir);

        // 1. Try embedded resource
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(resName))
        {
            onProgress?.Invoke("Unpacking application components...");
            using (var stream = asm.GetManifestResourceStream(resName)!)
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ExtractArchiveSafe(archive, installDir);
            }
            await EnsureOpenFluxBinaryAsync(installDir, onProgress);
            EnsureExecutables(installDir);
            return;
        }

        // 2. Try adjacent payload file or folder
        var baseDir = AppContext.BaseDirectory;
        var rid = GetCurrentRid();
        var candidateFiles = new[]
        {
            Path.Combine(baseDir, "payload.zip"),
            Path.Combine(baseDir, $"openflux-zen-server-{rid}.zip"),
            Path.Combine(baseDir, "payload"),
        };

        foreach (var file in candidateFiles)
        {
            if (File.Exists(file))
            {
                onProgress?.Invoke($"Extracting local package: {Path.GetFileName(file)}...");
                ExtractZipSafe(file, installDir);
                await EnsureOpenFluxBinaryAsync(installDir, onProgress);
                EnsureExecutables(installDir);
                return;
            }
        }

        var candidateDir = Path.Combine(baseDir, "app");
        if (Directory.Exists(candidateDir))
        {
            onProgress?.Invoke("Copying local application components...");
            CopyDirectory(candidateDir, installDir);
            await EnsureOpenFluxBinaryAsync(installDir, onProgress);
            EnsureExecutables(installDir);
            return;
        }

        // 3. Fallback: Download from GitHub Releases
        onProgress?.Invoke($"Downloading pre-built release package for {rid} from GitHub...");
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenFlux-Installer");
        var downloadUrl = $"https://github.com/BizhQwe/openflux-zen-server/releases/latest/download/openflux-zen-server-{rid}.zip";

        var tempZip = Path.Combine(Path.GetTempPath(), $"openflux-setup-{rid}.zip");
        try
        {
            using (var s = await client.GetStreamAsync(downloadUrl))
            using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await s.CopyToAsync(fs);
            }

            ExtractZipSafe(tempZip, installDir);
            await EnsureOpenFluxBinaryAsync(installDir, onProgress);
            EnsureExecutables(installDir);
        }
        finally
        {
            try { File.Delete(tempZip); } catch { }
        }
    }

    public static async Task EnsureOpenFluxBinaryAsync(string installDir, Action<string>? onProgress = null)
    {
        var binaryName = GetOpenFluxBinaryName();
        var runtimesDir = Path.Combine(installDir, "runtimes");
        Directory.CreateDirectory(runtimesDir);
        var binaryPath = Path.Combine(runtimesDir, binaryName);

        if (File.Exists(binaryPath) && new FileInfo(binaryPath).Length > 1_000_000)
        {
            onProgress?.Invoke($"Official OpenFlux core engine already present ({binaryName}).");
            return;
        }

        onProgress?.Invoke($"Downloading official OpenFlux core engine ({binaryName}) from GitHub (p1neappleXpress/OpenFlux)...");

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenFluxZenServer-Installer/1.0");

        var downloadUrls = new[]
        {
            $"https://github.com/p1neappleXpress/OpenFlux/releases/latest/download/{binaryName}",
            $"https://github.com/p1neappleXpress/OpenFlux/releases/download/v0.2.0/{binaryName}"
        };

        var tempPath = Path.Combine(runtimesDir, $"{binaryName}.tmp_{Guid.NewGuid():N}");
        bool downloaded = false;

        foreach (var url in downloadUrls)
        {
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    await using (var s = await response.Content.ReadAsStreamAsync())
                    await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        await s.CopyToAsync(fs);
                    }

                    var fi = new FileInfo(tempPath);
                    if (fi.Exists && fi.Length > 1_000_000)
                    {
                        if (File.Exists(binaryPath)) File.Delete(binaryPath);
                        File.Move(tempPath, binaryPath, overwrite: true);
                        downloaded = true;
                        var sizeMb = Math.Round(fi.Length / 1024.0 / 1024.0, 2);
                        onProgress?.Invoke($"Downloaded official OpenFlux core ({sizeMb} MB).");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"Download attempt from {url} failed: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        if (!downloaded && !File.Exists(binaryPath))
        {
            onProgress?.Invoke("Warning: Could not download OpenFlux core binary. It will be downloaded on first start or via Web Control Panel.");
        }
    }

    private static void ExtractArchiveSafe(ZipArchive archive, string destinationDir)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                var dirPath = Path.Combine(destinationDir, entry.FullName);
                Directory.CreateDirectory(dirPath);
                continue;
            }

            var destPath = Path.Combine(destinationDir, entry.FullName);
            var parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            bool success = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    entry.ExtractToFile(destPath, overwrite: true);
                    success = true;
                    break;
                }
                catch (IOException)
                {
                    SystemOperations.StopExistingServer();
                    Thread.Sleep(500 * (attempt + 1));
                }
            }

            if (!success)
            {
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }
    }

    private static void ExtractZipSafe(string zipPath, string destinationDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        ExtractArchiveSafe(archive, destinationDir);
    }

    private static void EnsureExecutables(string installDir)
    {
        var dataDir = Path.Combine(installDir, "data");
        Directory.CreateDirectory(dataDir);

        var coreVerFile = Path.Combine(dataDir, "openflux-core-version.json");
        if (!File.Exists(coreVerFile))
        {
            try
            {
                var initJson = $"{{\n  \"CurrentVersion\": \"v0.2.0\",\n  \"InstalledAt\": \"{DateTime.UtcNow:O}\"\n}}";
                File.WriteAllText(coreVerFile, initJson);
            }
            catch { }
        }

        var runtimesDir = Path.Combine(installDir, "runtimes");
        if (Directory.Exists(runtimesDir))
        {
            foreach (var f in Directory.GetFiles(runtimesDir))
            {
                if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var targetCookie = Path.Combine(dataDir, Path.GetFileName(f));
                    if (!File.Exists(targetCookie))
                    {
                        try { File.Copy(f, targetCookie, overwrite: false); } catch { }
                    }
                }
            }
        }

        if (OperatingSystem.IsWindows()) return;

        var executables = new[]
        {
            "OpenFlux.Zen.Server.Web",
            "OpenFluxZenServer"
        };

        foreach (var exe in executables)
        {
            var p = Path.Combine(installDir, exe);
            if (File.Exists(p))
            {
                try
                {
                    File.SetUnixFileMode(p, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { }
            }
        }

        if (Directory.Exists(runtimesDir))
        {
            foreach (var f in Directory.GetFiles(runtimesDir))
            {
                if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { }
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectory(dir, dest);
        }
    }
}
