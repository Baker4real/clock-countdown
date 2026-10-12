using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;

namespace MinminCountdown;

/// <summary>
/// Device-independent work area of the monitor hosting a window, together
/// with the DPI scale used for the physical-pixel conversion.
/// </summary>
internal sealed record MonitorWorkArea(
    Rect WorkAreaDip,
    double DpiScaleX,
    double DpiScaleY);

/// <summary>
/// 用物理屏幕矩形选择显示器，再按目标显示器 DPI 返回工作区。
/// 已创建的窗口直接读取 HWND 矩形，避免跨屏期间使用过期 WPF DPI 定位。
/// </summary>
internal static class MonitorWorkAreaResolver
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    private static MonitorWorkArea? s_validationOverride;
    private static IReadOnlyList<MonitorWorkArea>? s_validationMonitors;

    internal static MonitorWorkArea ResolveForWindow(Window window)
    {
        if (s_validationMonitors is null && s_validationOverride is { } injectedForWindow)
        {
            return injectedForWindow;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (s_validationMonitors is null && handle != IntPtr.Zero && GetWindowRect(handle, out var physical))
            return ResolvePhysicalRect(physical, dpi.DpiScaleX, dpi.DpiScaleY);
        var width = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
        var height = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;
        var left = double.IsNaN(window.Left) ? 0 : window.Left;
        var top = double.IsNaN(window.Top) ? 0 : window.Top;
        return ResolveForScreenRect(
            new Rect(left, top, Math.Max(0, width), Math.Max(0, height)),
            dpi.DpiScaleX,
            dpi.DpiScaleY);
    }

    internal static MonitorWorkArea ResolveForScreenRect(
        Rect windowRectDip,
        double dpiScaleX,
        double dpiScaleY)
    {
        if (!IsUsableScale(dpiScaleX) || !IsUsableScale(dpiScaleY))
        {
            dpiScaleX = 1;
            dpiScaleY = 1;
        }
        return ResolvePhysicalRect(ToPhysical(windowRectDip, dpiScaleX, dpiScaleY), dpiScaleX, dpiScaleY);
    }

    private static MonitorWorkArea ResolvePhysicalRect(NativeRect physical, double dpiScaleX, double dpiScaleY)
    {
        if (s_validationMonitors is { } monitors)
        {
            var windowRect = ToRect(physical);
            var selected = monitors.Select(monitor => (Monitor: monitor,
                    Physical: ToPhysical(monitor.WorkAreaDip, monitor.DpiScaleX, monitor.DpiScaleY)))
                .OrderByDescending(item =>
                {
                    var intersection = Rect.Intersect(windowRect, ToRect(item.Physical));
                    return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
                })
                .ThenBy(item =>
                {
                    var center = new Point(windowRect.Left + windowRect.Width / 2,
                        windowRect.Top + windowRect.Height / 2);
                    var area = ToRect(item.Physical);
                    var dx = center.X - Math.Clamp(center.X, area.Left, area.Right);
                    var dy = center.Y - Math.Clamp(center.Y, area.Top, area.Bottom);
                    return dx * dx + dy * dy;
                }).First();
            return FromPhysical(selected.Physical, selected.Monitor.DpiScaleX, selected.Monitor.DpiScaleY);
        }

        if (s_validationOverride is { } injected)
        {
            return injected;
        }

        var monitor = MonitorFromRect(ref physical, MonitorDefaultToNearest);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info))
        {
            // 解析失败退回主显示器工作区（与悬浮窗既有 SystemParameters.WorkArea
            // 口径一致），不让外壳集成点阻止管理窗口打开。
            return new MonitorWorkArea(SystemParameters.WorkArea, dpiScaleX, dpiScaleY);
        }

        if (GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
        {
            dpiScaleX = dpiX / 96.0;
            dpiScaleY = dpiY / 96.0;
        }
        return FromPhysical(info.Work, dpiScaleX, dpiScaleY);
    }

    private static NativeRect ToPhysical(Rect rect, double scaleX, double scaleY) => new()
    {
        Left = (int)Math.Floor(rect.Left * scaleX), Top = (int)Math.Floor(rect.Top * scaleY),
        Right = (int)Math.Ceiling(rect.Right * scaleX), Bottom = (int)Math.Ceiling(rect.Bottom * scaleY)
    };

    private static Rect ToRect(NativeRect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static MonitorWorkArea FromPhysical(NativeRect work, double dpiScaleX, double dpiScaleY)
    {
        return new MonitorWorkArea(
            new Rect(
                work.Left / dpiScaleX,
                work.Top / dpiScaleY,
                (work.Right - work.Left) / dpiScaleX,
                (work.Bottom - work.Top) / dpiScaleY),
            dpiScaleX,
            dpiScaleY);
    }

    /// <summary>
    /// 验证注入：在 Dispose 前把所有解析钉到固定工作区与 DPI，可嵌套（恢复上一值）。
    /// 仅限验证入口调用；生产路径永不设置。
    /// </summary>
    internal static IDisposable OverrideForValidation(MonitorWorkArea workArea)
    {
        var previous = s_validationOverride;
        var previousMonitors = s_validationMonitors;
        s_validationOverride = workArea;
        s_validationMonitors = null;
        return new OverrideScope(previous, previousMonitors);
    }

    internal static IDisposable OverrideForValidation(params MonitorWorkArea[] monitors)
    {
        if (monitors.Length == 0) throw new ArgumentException("至少需要一个显示器。", nameof(monitors));
        var scope = new OverrideScope(s_validationOverride, s_validationMonitors);
        s_validationMonitors = monitors.ToArray();
        return scope;
    }

    private static bool IsUsableScale(double scale) =>
        !double.IsNaN(scale) && !double.IsInfinity(scale) && scale > 0;

    private sealed class OverrideScope : IDisposable
    {
        private readonly MonitorWorkArea? _previous;
        private readonly IReadOnlyList<MonitorWorkArea>? _previousMonitors;
        private bool _disposed;

        internal OverrideScope(MonitorWorkArea? previous, IReadOnlyList<MonitorWorkArea>? previousMonitors)
        {
            _previous = previous;
            _previousMonitors = previousMonitors;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            s_validationOverride = _previous;
            s_validationMonitors = _previousMonitors;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("shcore.dll", ExactSpelling = true)]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref NativeMonitorInfo info);
}
