using System;
using System.Diagnostics;
using System.IO;

internal static class CountdownLauncher
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(systemDirectory, "schtasks.exe"),
                Arguments = "/Run /TN \"Minmin Countdown\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(startInfo))
            {
                if (!process.WaitForExit(5000))
                {
                    return 2;
                }
                return process.ExitCode;
            }
        }
        catch
        {
            return 1;
        }
    }
}
