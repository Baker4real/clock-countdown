using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace MinminCountdown;

/// <summary>
/// Requests the Windows 11 DWM frame for borderless opaque windows (management window,
/// reminder center, and reminder rules editor). The client surface stays opaque so DWM
/// can own the rounded frame, system shadow, and 1 DIP border color when supported.
/// Unsupported builds, isolated/non-composited desktops, or failed attribute calls keep
/// square opaque chrome and fall back to the WPF content border with a warning diagnostic.
/// </summary>
internal static class DwmWindowChrome
{
    private const int Succeeded = 0;

    internal static readonly DwmWindowChromeEvents MainWindowEvents = new(
        Applied: "MainWindowDwmRoundedCornersApplied",
        ShadowFallback: "MainWindowDwmShadowFallback",
        RoundedCornersFallback: "MainWindowDwmRoundedCornersFallback",
        BorderColorFallback: "MainWindowDwmBorderColorFallback");

    internal static readonly DwmWindowChromeEvents ReminderCenterEvents = new(
        Applied: "ReminderCenterWindowDwmRoundedCornersApplied",
        ShadowFallback: "ReminderCenterWindowDwmShadowFallback",
        RoundedCornersFallback: "ReminderCenterWindowDwmRoundedCornersFallback",
        BorderColorFallback: "ReminderCenterWindowDwmBorderColorFallback");

    internal static readonly DwmWindowChromeEvents ReminderRulesEditorEvents = new(
        Applied: "ReminderRulesEditorWindowDwmRoundedCornersApplied",
        ShadowFallback: "ReminderRulesEditorWindowDwmShadowFallback",
        RoundedCornersFallback: "ReminderRulesEditorWindowDwmRoundedCornersFallback",
        BorderColorFallback: "ReminderRulesEditorWindowDwmBorderColorFallback");

    internal static void ConfigureOpaqueBorderlessChrome(Window window)
    {
        window.AllowsTransparency = false;
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(
            window,
            new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
    }

    internal static DwmWindowChromeResult ApplyMainWindow(
        Window window,
        DiagnosticLogger logger) =>
        ApplyFrame(window, MainWindowEvents, logger, NativeDwmWindowChromeHost.Instance);

    internal static void AttachSecondaryWindow(
        Window window,
        Border shellSurface,
        string borderColorResourceKey,
        string borderBrushResourceKey,
        DwmWindowChromeEvents events,
        DiagnosticLogger? logger = null,
        IDwmWindowChromeHost? host = null)
    {
        shellSurface.CornerRadius = new CornerRadius(0);
        shellSurface.SetResourceReference(Border.BorderBrushProperty, borderBrushResourceKey);
        ApplyContentBorderFallback(shellSurface);

        var resolvedHost = host ?? NativeDwmWindowChromeHost.Instance;
        var chromeResult = DwmWindowChromeResult.NotAttempted;
        var closed = false;

        DiagnosticLogger? ResolveLogger() =>
            logger ?? (Application.Current as App)?.DiagnosticLogger;

        void SyncBorderColor()
        {
            if (closed || chromeResult != DwmWindowChromeResult.Applied)
            {
                return;
            }

            if (TryApplyBorderColor(
                    window,
                    borderColorResourceKey,
                    borderBrushResourceKey,
                    events,
                    ResolveLogger(),
                    resolvedHost))
            {
                shellSurface.BorderThickness = new Thickness(0);
                shellSurface.Padding = new Thickness(1);
            }
            else
            {
                ApplyContentBorderFallback(shellSurface);
            }
        }

        void OnPaletteChanged(object? sender, EventArgs args) => SyncBorderColor();

        window.SourceInitialized += (_, _) =>
        {
            chromeResult = ApplyFrame(window, events, ResolveLogger(), resolvedHost);
            if (chromeResult == DwmWindowChromeResult.Applied)
            {
                SyncBorderColor();
            }
            else
            {
                ApplyContentBorderFallback(shellSurface);
            }
        };

        ApplicationTheme.PaletteChanged += OnPaletteChanged;
        window.Closed += (_, _) =>
        {
            closed = true;
            ApplicationTheme.PaletteChanged -= OnPaletteChanged;
        };
    }

    internal static uint ToColorRef(Color color) =>
        (uint)(color.R | (color.G << 8) | (color.B << 16));

    private static DwmWindowChromeResult ApplyFrame(
        Window window,
        DwmWindowChromeEvents events,
        DiagnosticLogger? logger,
        IDwmWindowChromeHost host)
    {
        if (!host.IsSupportedOperatingSystem)
        {
            logger?.Warning(events.RoundedCornersFallback);
            return DwmWindowChromeResult.Fallback;
        }

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                logger?.Warning(events.RoundedCornersFallback);
                return DwmWindowChromeResult.Fallback;
            }

            var renderingPolicy = (uint)DwmNcRenderingPolicy.DWMNCRP_ENABLED;
            var shadowResult = host.SetWindowAttribute(
                handle,
                DwmWindowAttribute.DWMWA_NCRENDERING_POLICY,
                renderingPolicy);
            if (shadowResult != Succeeded)
            {
                logger?.Warning(
                    events.ShadowFallback,
                    new ExternalException(
                        "DwmSetWindowAttribute(DWMWA_NCRENDERING_POLICY) failed.",
                        shadowResult));
                return DwmWindowChromeResult.Fallback;
            }

            var preference = (uint)DwmWindowCornerPreference.DWMWCP_ROUND;
            var result = host.SetWindowAttribute(
                handle,
                DwmWindowAttribute.DWMWA_WINDOW_CORNER_PREFERENCE,
                preference);
            if (result != Succeeded)
            {
                logger?.Warning(
                    events.RoundedCornersFallback,
                    new ExternalException(
                        "DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE) failed.",
                        result));
                return DwmWindowChromeResult.Fallback;
            }

            logger?.Info(events.Applied);
            return DwmWindowChromeResult.Applied;
        }
        catch (Exception exception)
        {
            // DWM is an optional shell integration point. A missing entry point,
            // disabled compositor, or an isolated validation desktop must never
            // prevent the window from opening.
            logger?.Warning(events.RoundedCornersFallback, exception);
            return DwmWindowChromeResult.Fallback;
        }
    }

    private static bool TryApplyBorderColor(
        Window window,
        string borderColorResourceKey,
        string borderBrushResourceKey,
        DwmWindowChromeEvents events,
        DiagnosticLogger? logger,
        IDwmWindowChromeHost host)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                logger?.Warning(events.BorderColorFallback);
                return false;
            }

            var borderColor = ResolveBorderColor(window, borderColorResourceKey, borderBrushResourceKey);
            if (borderColor is not { } color)
            {
                logger?.Warning(events.BorderColorFallback);
                return false;
            }

            var colorRef = ToColorRef(color);
            var result = host.SetWindowAttribute(
                handle,
                DwmWindowAttribute.DWMWA_BORDER_COLOR,
                colorRef);
            if (result != Succeeded)
            {
                logger?.Warning(
                    events.BorderColorFallback,
                    new ExternalException(
                        "DwmSetWindowAttribute(DWMWA_BORDER_COLOR) failed.",
                        result));
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            logger?.Warning(events.BorderColorFallback, exception);
            return false;
        }
    }

    private static Color? ResolveBorderColor(
        Window window,
        string colorResourceKey,
        string brushResourceKey)
    {
        if (window.TryFindResource(colorResourceKey) is Color color)
        {
            return color;
        }

        if (window.TryFindResource(brushResourceKey) is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return null;
    }

    private static void ApplyContentBorderFallback(Border shellSurface)
    {
        shellSurface.BorderThickness = new Thickness(1);
        shellSurface.Padding = new Thickness(0);
    }

    private enum DwmNcRenderingPolicy : uint
    {
        DWMNCRP_USEWINDOWSTYLE = 0,
        DWMNCRP_DISABLED = 1,
        DWMNCRP_ENABLED = 2,
        DWMNCRP_LAST = 3
    }

    private enum DwmWindowCornerPreference : uint
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3
    }

    private sealed class NativeDwmWindowChromeHost : IDwmWindowChromeHost
    {
        internal static readonly NativeDwmWindowChromeHost Instance = new();

        public bool IsSupportedOperatingSystem =>
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

        public int SetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attribute, uint value) =>
            DwmSetWindowAttribute(
                hwnd,
                attribute,
                ref value,
                // The Win32 contract is a 32-bit enum or COLORREF. Marshal.SizeOf<T>()
                // rejects enum types on current .NET runtimes, so use the ABI width explicitly.
                sizeof(uint));

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            DwmWindowAttribute attribute,
            ref uint value,
            uint valueSize);
    }
}

internal enum DwmWindowAttribute
{
    DWMWA_NCRENDERING_POLICY = 2,
    DWMWA_WINDOW_CORNER_PREFERENCE = 33,
    DWMWA_BORDER_COLOR = 34
}

internal interface IDwmWindowChromeHost
{
    bool IsSupportedOperatingSystem { get; }

    int SetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attribute, uint value);
}

internal sealed record DwmWindowChromeEvents(
    string Applied,
    string ShadowFallback,
    string RoundedCornersFallback,
    string BorderColorFallback);

internal enum DwmWindowChromeResult
{
    NotAttempted,
    Applied,
    Fallback
}
