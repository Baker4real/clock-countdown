using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MinminCountdown;

/// <summary>
/// Requests the Windows 11 DWM frame for the borderless management window.
/// The client surface stays opaque so DWM can own both the rounded frame and
/// the system shadow. Unsupported builds and isolated/non-composited desktops
/// keep the existing WPF chrome and are reported as a safe fallback.
/// </summary>
internal static class DwmWindowChrome
{
    private const int Succeeded = 0;

    internal static DwmWindowChromeResult ApplyMainWindow(
        Window window,
        DiagnosticLogger logger)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            logger.Warning("MainWindowDwmRoundedCornersFallback");
            return DwmWindowChromeResult.Fallback;
        }

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                logger.Warning("MainWindowDwmRoundedCornersFallback");
                return DwmWindowChromeResult.Fallback;
            }

            var preference = DwmWindowCornerPreference.DWMWCP_ROUND;
            var result = DwmSetWindowAttribute(
                handle,
                DwmWindowAttribute.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference,
                (uint)Marshal.SizeOf<DwmWindowCornerPreference>());
            if (result != Succeeded)
            {
                logger.Warning(
                    "MainWindowDwmRoundedCornersFallback",
                    new ExternalException(
                        "DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE) failed.",
                        result));
                return DwmWindowChromeResult.Fallback;
            }

            logger.Info("MainWindowDwmRoundedCornersApplied");
            return DwmWindowChromeResult.Applied;
        }
        catch (Exception exception)
        {
            // DWM is an optional shell integration point. A missing entry point,
            // disabled compositor, or an isolated validation desktop must never
            // prevent the management window from opening.
            logger.Warning("MainWindowDwmRoundedCornersFallback", exception);
            return DwmWindowChromeResult.Fallback;
        }
    }

    private enum DwmWindowAttribute
    {
        DWMWA_WINDOW_CORNER_PREFERENCE = 33
    }

    private enum DwmWindowCornerPreference
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        DwmWindowAttribute attribute,
        ref DwmWindowCornerPreference value,
        uint valueSize);
}

internal enum DwmWindowChromeResult
{
    NotAttempted,
    Applied,
    Fallback
}
