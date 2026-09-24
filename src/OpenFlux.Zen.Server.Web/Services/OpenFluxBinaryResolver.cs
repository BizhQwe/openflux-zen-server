using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenFlux.Zen.Server.Services;

public interface IOpenFluxBinaryResolver
{
    string GetBinaryPath();
    bool IsBinaryAvailable();
}

public sealed class OpenFluxBinaryResolver : IOpenFluxBinaryResolver
{
    private readonly ILogger<OpenFluxBinaryResolver> _logger;
    private string? _cachedPath;

    public OpenFluxBinaryResolver(ILogger<OpenFluxBinaryResolver> logger)
    {
        _logger = logger;
    }

    public bool IsBinaryAvailable()
    {
        try
        {
            var path = GetBinaryPath();
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public string GetBinaryPath()
    {
        if (_cachedPath != null && File.Exists(_cachedPath))
        {
            return _cachedPath;
        }

        var customEnv = Environment.GetEnvironmentVariable("OPENFLUX_BINARY_PATH");
        if (!string.IsNullOrWhiteSpace(customEnv) && File.Exists(customEnv))
        {
            _cachedPath = customEnv;
            EnsureExecutable(_cachedPath);
            return _cachedPath;
        }

        var binaryName = DetermineBinaryName();
        var searchPaths = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "runtimes", binaryName),
            Path.Combine(AppContext.BaseDirectory, binaryName),
            Path.Combine(Directory.GetCurrentDirectory(), "runtimes", binaryName),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "runtimes", binaryName),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "runtimes", binaryName),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "runtimes", binaryName),
            Path.Combine("/opt/openflux-zen-server/runtimes", binaryName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenFluxZenServer", "runtimes", binaryName)
        };

        foreach (var p in searchPaths)
        {
            var fullPath = Path.GetFullPath(p);
            if (File.Exists(fullPath))
            {
                _logger.LogInformation("Resolved OpenFlux binary: {Path}", fullPath);
                EnsureExecutable(fullPath);
                _cachedPath = fullPath;
                return fullPath;
            }
        }

        // Fallback: look for generic 'openflux' or 'openflux.exe' in PATH
        var fallbackName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "openflux.exe" : "openflux";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, fallbackName);
            if (File.Exists(candidate))
            {
                _cachedPath = candidate;
                EnsureExecutable(_cachedPath);
                return _cachedPath;
            }
        }

        var expectedPath = Path.Combine(AppContext.BaseDirectory, "runtimes", binaryName);
        _logger.LogWarning("OpenFlux binary not found. Expected at: {Path}", expectedPath);
        return expectedPath;
    }

    private static string DetermineBinaryName()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        if (isWindows)
        {
            return isArm64 ? "openflux-windows-arm64.exe" : "openflux-windows-amd64.exe";
        }
        else
        {
            return isArm64 ? "openflux-linux-arm64" : "openflux-linux-amd64";
        }
    }

    private void EnsureExecutable(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to chmod +x on {Path}", path);
            }
        }
    }
}
