using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

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

    public static async Task ExtractPayloadAsync(string installDir, Action<string>? onProgress = null)
    {
        Directory.CreateDirectory(installDir);

        // 1. Try embedded resource
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(resName))
        {
            onProgress?.Invoke("Unpacking embedded application payload...");
            using var stream = asm.GetManifestResourceStream(resName)!;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(installDir, overwriteFiles: true);
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
                ZipFile.ExtractToDirectory(file, installDir, overwriteFiles: true);
                EnsureExecutables(installDir);
                return;
            }
        }

        var candidateDir = Path.Combine(baseDir, "app");
        if (Directory.Exists(candidateDir))
        {
            onProgress?.Invoke("Copying local application components...");
            CopyDirectory(candidateDir, installDir);
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

            ZipFile.ExtractToDirectory(tempZip, installDir, overwriteFiles: true);
            EnsureExecutables(installDir);
        }
        finally
        {
            try { File.Delete(tempZip); } catch { }
        }
    }

    private static void EnsureExecutables(string installDir)
    {
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

        var runtimesDir = Path.Combine(installDir, "runtimes");
        if (Directory.Exists(runtimesDir))
        {
            foreach (var f in Directory.GetFiles(runtimesDir))
            {
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
