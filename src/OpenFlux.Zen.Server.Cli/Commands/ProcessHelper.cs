using System.Diagnostics;

namespace OpenFlux.Zen.Server.Cli.Commands;

public static class ProcessHelper
{
    public static void RunCommand(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { }
    }

    public static (int ExitCode, string Output) RunCommandWithOutput(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "");
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return (p.ExitCode, text.Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    public static void RunBash(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"{cmd}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { }
    }

    public static (int ExitCode, string Output) RunBashWithOutput(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"{cmd}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "");
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return (p.ExitCode, text.Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
