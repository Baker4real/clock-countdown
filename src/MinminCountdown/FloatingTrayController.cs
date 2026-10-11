using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MinminCountdown;

internal sealed class FloatingTrayController : IDisposable
{
    private readonly FloatingWindow _floatingWindow;
    private readonly ITrayIconHost _trayIcon;
    private readonly Func<bool> _canTransition;
    private readonly TrayForegroundInterop _foregroundInterop;
    private readonly Action? _openManager;
    private bool _disposed;

    public FloatingTrayController(
        FloatingWindow floatingWindow,
        string iconPath,
        Func<bool>? canTransition = null,
        Action? openManager = null)
        : this(
            floatingWindow,
            iconPath,
            canTransition,
            foregroundInterop: null,
            attachShellNotifyIcon: true,
            openManager: openManager)
    {
    }

    internal FloatingTrayController(
        FloatingWindow floatingWindow,
        string iconPath,
        Func<bool>? canTransition,
        TrayForegroundInterop? foregroundInterop)
        : this(
            floatingWindow,
            iconPath,
            canTransition,
            foregroundInterop,
            attachShellNotifyIcon: true,
            openManager: null)
    {
    }

    internal FloatingTrayController(
        FloatingWindow floatingWindow,
        string iconPath,
        Func<bool>? canTransition,
        TrayForegroundInterop? foregroundInterop,
        bool attachShellNotifyIcon,
        Action? openManager = null)
    {
        _floatingWindow = floatingWindow;
        _canTransition = canTransition ?? (() => true); // Production default: allow tray transitions unless a host supplies a guard.
        _foregroundInterop = foregroundInterop ?? TrayForegroundInterop.System;
        _openManager = openManager;
        _trayIcon = attachShellNotifyIcon
            ? new TrayIconHost(
                iconPath,
                floatingWindow.Dispatcher,
                Restore,
                RequestExit,
                ResetWindowPosition,
                OpenManager)
            : new MemoryTrayIconHost(Restore, RequestExit, ResetWindowPosition, OpenManager);
        _floatingWindow.MinimizeToTrayRequested += OnMinimizeRequested;
        // #213：应用运行期间托盘图标常驻，不等到悬浮窗隐藏才出现。
        _trayIcon.Show();
    }

    internal bool IsActive => !_disposed;

    internal bool IsTrayIconVisible => !_disposed && _trayIcon.IsVisible;

    internal event EventHandler? Minimized;

    internal event EventHandler? Restored;

    internal event EventHandler? ExitRequested;

    internal void InvokeRestoreForValidation() => _trayIcon.InvokeRestoreForValidation();

    internal void InvokeResetPositionForValidation() => _trayIcon.InvokeResetPositionForValidation();

    internal void InvokeExitForValidation() => _trayIcon.InvokeExitForValidation();

    internal void InvokeOpenManagerForValidation() => _trayIcon.InvokeOpenManagerForValidation();

    internal void InvokeIconClickForValidation() => _trayIcon.InvokeIconClickForValidation();

    internal string ActivationState => !_floatingWindow.IsVisible
        ? IsTrayIconVisible ? "Tray" : "Hidden"
        : _floatingWindow.WindowState == System.Windows.WindowState.Minimized
            ? "Minimized"
            : "VisibleNormal";

    internal int InteractiveFloatingWindowCount => System.Windows.Application.Current?.Windows
        .OfType<FloatingWindow>()
        .Count() ?? 0;

    internal long ForegroundWindowHandle => GetForegroundWindow().ToInt64();

    internal bool FloatingWindowIsActive => _floatingWindow.IsActive;

    internal bool RestoreAndActivateFromExternalRequest()
    {
        if (!CanTransition())
        {
            return false;
        }

        return RestoreCore();
    }

    internal void MinimizeToTray() => OnMinimizeRequested(this, EventArgs.Empty);

    internal void MinimizeToTrayForActivationTest() => MinimizeToTray();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _floatingWindow.MinimizeToTrayRequested -= OnMinimizeRequested;
        _trayIcon.Dispose();
        _disposed = true;
    }

    private void OnMinimizeRequested(object? sender, EventArgs eventArgs)
    {
        if (!CanTransition())
        {
            return;
        }

        _trayIcon.Show();
        _floatingWindow.Hide();
        Minimized?.Invoke(this, EventArgs.Empty);
    }

    private void Restore()
    {
        if (!RestoreAndActivateFromExternalRequest())
        {
            return;
        }

        Restored?.Invoke(this, EventArgs.Empty);
    }

    private bool RestoreCore()
    {
        if (!_floatingWindow.IsVisible)
        {
            _floatingWindow.Show();
        }

        _floatingWindow.WindowState = System.Windows.WindowState.Normal;
        var activateAccepted = _foregroundInterop.Activate(_floatingWindow);
        var focusAccepted = _foregroundInterop.Focus(_floatingWindow);
        var handle = new WindowInteropHelper(_floatingWindow).Handle;
        var foregroundAccepted = false;
        if (handle != IntPtr.Zero)
        {
            foregroundAccepted = _foregroundInterop.TrySetForeground(handle);
        }

        var isForeground = _foregroundInterop.IsForegroundOrActive(_floatingWindow, handle);
        var restored = _floatingWindow.IsVisible &&
               _floatingWindow.WindowState == System.Windows.WindowState.Normal &&
               isForeground &&
               (activateAccepted || focusAccepted || foregroundAccepted || isForeground);
        // #213：托盘图标在整个运行期间保持可见，恢复悬浮窗不再把它藏起来。
        return restored;
    }

    private void OpenManager() => _openManager?.Invoke();

    private void RequestExit()
    {
        if (CanTransition())
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // #156：托盘菜单「复位窗口位置」——非拖动地把悬浮窗夹回工作区（贴边/复位即可，
    // 不做方向键微移）。复位不是托盘切换，不受 CanTransition 守卫约束。
    private void ResetWindowPosition() => _floatingWindow.ResetPositionIntoWorkArea();

    private bool CanTransition() => !_disposed && _canTransition();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

// RestoreCore 判定成功所依赖的 shell 交互 seam；验证替身可注入确定性失败。
internal class TrayForegroundInterop
{
    internal static TrayForegroundInterop System { get; } = new();

    internal virtual bool Activate(FloatingWindow window) => window.Activate();

    internal virtual bool Focus(FloatingWindow window) => window.Focus();

    internal virtual bool TrySetForeground(IntPtr handle) => SetForegroundWindow(handle);

    internal virtual bool IsForegroundOrActive(FloatingWindow window, IntPtr handle) =>
        handle != IntPtr.Zero && (GetForegroundWindow() == handle || window.IsActive);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
