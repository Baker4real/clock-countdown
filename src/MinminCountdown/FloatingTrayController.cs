using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MinminCountdown;

internal sealed class FloatingTrayController : IDisposable
{
    private readonly FloatingWindow _floatingWindow;
    private readonly TrayIconHost _trayIcon;
    private readonly Func<bool> _canTransition;
    private bool _disposed;

    public FloatingTrayController(
        FloatingWindow floatingWindow,
        string iconPath,
        Func<bool>? canTransition = null)
    {
        _floatingWindow = floatingWindow;
        _canTransition = canTransition ?? (() => true);
        _trayIcon = new TrayIconHost(
            iconPath,
            floatingWindow.Dispatcher,
            Restore,
            RequestExit);
        _floatingWindow.MinimizeToTrayRequested += OnMinimizeRequested;
    }

    internal bool IsActive => !_disposed;

    internal bool IsTrayIconVisible => !_disposed && _trayIcon.IsVisible;

    internal event EventHandler? Minimized;

    internal event EventHandler? Restored;

    internal event EventHandler? ExitRequested;

    internal void InvokeRestoreForValidation() => _trayIcon.InvokeRestoreForValidation();

    internal void InvokeExitForValidation() => _trayIcon.InvokeExitForValidation();

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

    internal void MinimizeToTrayForActivationTest() => OnMinimizeRequested(this, EventArgs.Empty);

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
        var activateAccepted = _floatingWindow.Activate();
        var focusAccepted = _floatingWindow.Focus();
        var handle = new WindowInteropHelper(_floatingWindow).Handle;
        var foregroundAccepted = false;
        if (handle != IntPtr.Zero)
        {
            foregroundAccepted = SetForegroundWindow(handle);
        }
        _trayIcon.Hide();
        var isForeground = handle != IntPtr.Zero &&
            (GetForegroundWindow() == handle || _floatingWindow.IsActive);
        return _floatingWindow.IsVisible &&
               _floatingWindow.WindowState == System.Windows.WindowState.Normal &&
               isForeground &&
               (activateAccepted || focusAccepted || foregroundAccepted || isForeground);
    }

    private void RequestExit()
    {
        if (CanTransition())
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CanTransition() => !_disposed && _canTransition();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
