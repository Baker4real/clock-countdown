using System;
using System.Diagnostics;
using System.IO;

internal static class CountdownLauncher
{
    private const string HostDirectoryName = "app";
    private const string HostFileName = "MinminCountdown.exe";
    private const string InstalledHostPath = @"D:\敏敏子倒计时\app\MinminCountdown.exe";

    [STAThread]
    private static int Main()
    {
        try
        {
            var hostPath = ResolveHostPath(ResolveLauncherDirectory());
            if (hostPath == null)
            {
                return 1;
            }

            var hostDirectory = Path.GetDirectoryName(hostPath);
            var applicationRoot = hostDirectory == null ? null : Path.GetDirectoryName(hostDirectory);
            if (string.IsNullOrWhiteSpace(applicationRoot))
            {
                return 1;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                UseShellExecute = false,
                WorkingDirectory = applicationRoot
            };
            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return 1;
                }

                return 0;
            }
        }
        catch
        {
            return 1;
        }
    }

    private static string ResolveLauncherDirectory()
    {
        var launcherPath = typeof(CountdownLauncher).Assembly.Location;
        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            launcherPath = Process.GetCurrentProcess().MainModule.FileName;
        }

        return Path.GetDirectoryName(Path.GetFullPath(launcherPath));
    }

    private static string ResolveHostPath(string launcherDirectory)
    {
        var sidecarHost = Path.GetFullPath(Path.Combine(launcherDirectory, HostDirectoryName, HostFileName));
        if (File.Exists(sidecarHost))
        {
            return sidecarHost;
        }

        var installedHost = Path.GetFullPath(InstalledHostPath);
        if (File.Exists(installedHost))
        {
            return installedHost;
        }

        return null;
    }
}
