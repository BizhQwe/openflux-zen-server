using System.Runtime.InteropServices;

namespace OpenFlux.Zen.Server.Common;

public static class AppPaths
{
    public static string ResolveAppDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("OPENFLUX_APP_DIR");
        if (!string.IsNullOrEmpty(envDir) && Directory.Exists(envDir))
        {
            return envDir;
        }

        var baseDir = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDir, "data")) || 
            File.Exists(Path.Combine(baseDir, "OpenFlux.Zen.Server.Web.dll")) ||
            File.Exists(Path.Combine(baseDir, "OpenFlux.Zen.Server.dll")))
        {
            return baseDir;
        }

        // Standard Linux installation directory
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Directory.Exists("/opt/openflux-zen-server/app"))
        {
            return "/opt/openflux-zen-server/app";
        }

        // Standard Windows installation directory
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var winDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenFluxZenServer");
            if (Directory.Exists(winDefault))
            {
                return winDefault;
            }
        }

        return baseDir;
    }

    public static string GetDataDirectory()
    {
        var dir = Path.Combine(ResolveAppDirectory(), "data");
        if (!Directory.Exists(dir))
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch { }
        }
        return dir;
    }

    public static string GetDatabasePath() => Path.Combine(GetDataDirectory(), "openflux.db");

    public static string GetCredentialsPath() => Path.Combine(GetDataDirectory(), ".credentials");
}
