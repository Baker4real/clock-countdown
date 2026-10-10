using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

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
/// #211：显示器工作区解析。输入一个窗口（或它的屏幕矩形），返回所在显示器
/// 的工作区（DIP）和 DPI。生产路径基于 Win32 显示器 API（WPF DIP 换算为
/// 物理像素后定位显示器，再按同一尺度换回 DIP）；验证通过
/// <see cref="OverrideForValidation"/> 注入固定矩形与 DPI，使窗口几何探针
/// 不依赖真实显示拓扑。本票只有主管理窗口接入；其他窗口归 #219。
/// </summary>
internal static class MonitorWorkAreaResolver
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    private static MonitorWorkArea? s_validationOverride;

    internal static MonitorWorkArea ResolveForWindow(Window window)
    {
        if (s_validationOverride is { } injectedForWindow)
        {
            return injectedForWindow;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
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
        if (s_validationOverride is { } injected)
        {
            return injected;
        }

        if (!IsUsableScale(dpiScaleX) || !IsUsableScale(dpiScaleY))
        {
            dpiScaleX = 1;
            dpiScaleY = 1;
        }

        var physical = new NativeRect
        {
            Left = (int)Math.Floor(windowRectDip.Left * dpiScaleX),
            Top = (int)Math.Floor(windowRectDip.Top * dpiScaleY),
            Right = (int)Math.Ceiling(windowRectDip.Right * dpiScaleX),
            Bottom = (int)Math.Ceiling(windowRectDip.Bottom * dpiScaleY)
        };
        var monitor = MonitorFromRect(ref physical, MonitorDefaultToNearest);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info))
        {
            // 解析失败退回主显示器工作区（与悬浮窗既有 SystemParameters.WorkArea
            // 口径一致），不让外壳集成点阻止管理窗口打开。
            return new MonitorWorkArea(SystemParameters.WorkArea, dpiScaleX, dpiScaleY);
        }

        var work = info.Work;
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
        s_validationOverride = workArea;
        return new OverrideScope(previous);
    }

    private static bool IsUsableScale(double scale) =>
        !double.IsNaN(scale) && !double.IsInfinity(scale) && scale > 0;

    private sealed class OverrideScope : IDisposable
    {
        private readonly MonitorWorkArea? _previous;
        private bool _disposed;

        internal OverrideScope(MonitorWorkArea? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            s_validationOverride = _previous;
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

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref NativeMonitorInfo info);
}
