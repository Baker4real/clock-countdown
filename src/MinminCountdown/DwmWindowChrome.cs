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

            var renderingPolicy = (int)DwmNcRenderingPolicy.DWMNCRP_ENABLED;
            var shadowResult = DwmSetWindowAttribute(
                handle,
                DwmWindowAttribute.DWMWA_NCRENDERING_POLICY,
                ref renderingPolicy,
                sizeof(int));
            if (shadowResult != Succeeded)
            {
                logger.Warning(
                    "MainWindowDwmShadowFallback",
                    new ExternalException(
                        "DwmSetWindowAttribute(DWMWA_NCRENDERING_POLICY) failed.",
                        shadowResult));
                return DwmWindowChromeResult.Fallback;
            }

            var preference = (int)DwmWindowCornerPreference.DWMWCP_ROUND;
            var result = DwmSetWindowAttribute(
                handle,
                DwmWindowAttribute.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference,
                // The Win32 contract is a 32-bit enum. Marshal.SizeOf<T>()
                // rejects enum types on current .NET runtimes, so use the
                // ABI width explicitly instead of turning a supported build
                // into the safe fallback path.
                sizeof(int));
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
        DWMWA_NCRENDERING_POLICY = 2,
        DWMWA_WINDOW_CORNER_PREFERENCE = 33
    }

    private enum DwmNcRenderingPolicy
    {
        DWMNCRP_USEWINDOWSTYLE = 0,
        DWMNCRP_DISABLED = 1,
        DWMNCRP_ENABLED = 2,
        DWMNCRP_LAST = 3
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
        ref int value,
        uint valueSize);
}

internal enum DwmWindowChromeResult
{
    NotAttempted,
    Applied,
    Fallback
}
