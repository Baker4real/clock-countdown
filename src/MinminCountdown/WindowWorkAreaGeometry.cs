using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MinminCountdown;

internal static class WindowWorkAreaGeometry
{
    internal static void TrackChanges(Window window, Action apply, Action? markManual = null)
    {
        HwndSource? source = null;
        var queued = false;
        var closed = false;
        void Queue()
        {
            if (closed || queued || !window.IsVisible) return;
            queued = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                queued = false;
                if (!closed && window.IsVisible) apply();
            }));
        }
        IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message is 0x007E or 0x001A) Queue(); // display/work-area settings changed
            if (message == 0x0232) markManual?.Invoke();
            return IntPtr.Zero;
        }
        void OnSourceInitialized(object? sender, EventArgs args)
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(Hook);
        }
        void OnDpiChanged(object sender, DpiChangedEventArgs args) => Queue();
        window.SourceInitialized += OnSourceInitialized;
        window.DpiChanged += OnDpiChanged;
        window.Closed += (_, _) =>
        {
            closed = true;
            source?.RemoveHook(Hook);
            window.SourceInitialized -= OnSourceInitialized;
            window.DpiChanged -= OnDpiChanged;
        };
    }

    internal static void Fit(Window window, Rect workArea, double margin, bool centerOnOwner = false)
    {
        var usable = Rect.Inflate(workArea, -margin, -margin);
        if (usable.IsEmpty || usable.Width <= 0 || usable.Height <= 0) return;
        window.MaxWidth = Math.Max(window.MinWidth, usable.Width);
        window.MaxHeight = Math.Max(window.MinHeight, usable.Height);
        var width = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
        var height = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;
        window.Width = Math.Clamp(width, window.MinWidth, window.MaxWidth);
        window.Height = Math.Clamp(height, window.MinHeight, window.MaxHeight);
        if (centerOnOwner && window.Owner is { } owner)
        {
            window.Left = owner.Left + (owner.ActualWidth - window.Width) / 2;
            window.Top = owner.Top + (owner.ActualHeight - window.Height) / 2;
        }
        window.Left = Math.Clamp(window.Left, usable.Left, Math.Max(usable.Left, usable.Right - window.Width));
        window.Top = Math.Clamp(window.Top, usable.Top, Math.Max(usable.Top, usable.Bottom - window.Height));
    }
}
