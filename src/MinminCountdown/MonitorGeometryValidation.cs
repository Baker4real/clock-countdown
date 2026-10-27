using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using MinminCountdown.Core;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;

namespace MinminCountdown;

internal static class MonitorGeometryValidation
{
    internal static bool RuntimeDpiChangeUpdatesWindowLayout()
    {
        var previous = SetThreadDpiAwarenessContext((IntPtr)(-4));
        if (previous == IntPtr.Zero) return false;
        ReminderRulesEditorWindow? editor = null;
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<PhysicalRect>());
        try
        {
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1280, 688), 1.5, 1.5));
            editor = new ReminderRulesEditorWindow(Array.Empty<ReminderRule>(), SystemClock.Instance);
            editor.Show();
            WindowValidationInput.Pump(editor.Dispatcher);
            var before = VisualTreeHelper.GetDpi(editor);
            var target = (int)Math.Round(before.PixelsPerInchX * 1.5);
            var observed = false;
            editor.DpiChanged += (_, _) => observed = true;
            Marshal.StructureToPtr(new PhysicalRect { Left = 2400, Top = 1200, Right = 3600, Bottom = 2200 }, pointer, false);
            SendMessageW(new WindowInteropHelper(editor).Handle, 0x02E0,
                (IntPtr)(target | (target << 16)), pointer);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                WindowValidationInput.Pump(editor.Dispatcher);
                editor.UpdateLayout();
            }
            var after = VisualTreeHelper.GetDpi(editor);
            var result = observed && Math.Abs(after.PixelsPerInchX - target) < .1 &&
                Contains(new Rect(0, 0, 1280, 688), editor) &&
                ElementReachable(editor, "AcceptReminderRulesButton") &&
                ElementReachable(editor, "CancelReminderRulesButton");
            Console.Error.WriteLine($"MONITOR_DPI_PROBE: event={observed}; before={before.PixelsPerInchX}; after={after.PixelsPerInchX}; target={target}; bounds={editor.Left},{editor.Top},{editor.ActualWidth},{editor.ActualHeight}; passed={result}");
            return result;
        }
        finally
        {
            editor?.Close();
            Marshal.FreeHGlobal(pointer);
            SetThreadDpiAwarenessContext(previous);
        }
    }

    // #240：沿用上面 #219 的真实 WM_DPICHANGED 注入，供专用窗口探针制造运行期 DPI 变化。
    // 调用方在创建窗口句柄前进入 PerMonitorV2 线程上下文，关闭窗口后还原；返回 IntPtr.Zero 表示进入失败。
    internal static IntPtr EnterPerMonitorV2ThreadDpiContextForValidation() =>
        SetThreadDpiAwarenessContext((IntPtr)(-4));

    internal static void RestoreThreadDpiContextForValidation(IntPtr previous)
    {
        if (previous != IntPtr.Zero)
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    // 建议矩形保持窗口左上角、按倍数放大物理尺寸；返回窗口 DPI 是否真的变成目标值。
    internal static bool InjectDpiChangeForValidation(Window window, double factor)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var current))
        {
            return false;
        }

        var before = VisualTreeHelper.GetDpi(window);
        var target = (int)Math.Round(before.PixelsPerInchX * factor);
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<PhysicalRect>());
        try
        {
            Marshal.StructureToPtr(new PhysicalRect
            {
                Left = current.Left,
                Top = current.Top,
                Right = current.Left + (int)Math.Round((current.Right - current.Left) * factor),
                Bottom = current.Top + (int)Math.Round((current.Bottom - current.Top) * factor)
            }, pointer, false);
            SendMessageW(handle, 0x02E0, (IntPtr)(target | (target << 16)), pointer);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                WindowValidationInput.Pump(window.Dispatcher);
                window.UpdateLayout();
            }

            var after = VisualTreeHelper.GetDpi(window);
            Console.Error.WriteLine($"REMAINING_BLOCKS_DPI_PROBE: window={window.GetType().Name}; before={before.PixelsPerInchX}; after={after.PixelsPerInchX}; target={target}");
            return Math.Abs(after.PixelsPerInchX - target) < .1;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    internal static bool ProductManifestDeclaresPerMonitorV2()
    {
        var module = LoadLibraryExW(Path.Combine(ApplicationPaths.ResolveApplicationRoot(), "app", "MinminCountdown.exe"), IntPtr.Zero, 0x22);
        if (module == IntPtr.Zero) return false;
        try
        {
            var resource = FindResourceW(module, (IntPtr)1, (IntPtr)24);
            if (resource == IntPtr.Zero) return false;
            var length = SizeofResource(module, resource);
            var pointer = LockResource(LoadResource(module, resource));
            if (pointer == IntPtr.Zero || length == 0) return false;
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            using var stream = new MemoryStream(bytes);
            var document = XDocument.Load(stream);
            return document.Descendants(XName.Get("dpiAwareness", "http://schemas.microsoft.com/SMI/2016/WindowsSettings"))
                .Any(element => element.Value.Trim() == "PerMonitorV2");
        }
        finally { FreeLibrary(module); }
    }

    internal static bool MixedDpiUsesPhysicalMonitorGeometry()
    {
        // Physical displays are [0,1920]@96 DPI and [1920,3840]@144 DPI.
        // Their monitor-relative WPF coordinate spaces overlap numerically;
        // choosing by unscaled global DIP rectangles selects the wrong screen.
        using var monitors = MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(new Rect(0, 0, 1920, 1080), 1, 1),
            new MonitorWorkArea(new Rect(1280, 0, 1280, 720), 1.5, 1.5));
        var secondary = MonitorWorkAreaResolver.ResolveForScreenRect(new Rect(1400, 100, 300, 200), 1.5, 1.5);
        var primary = MonitorWorkAreaResolver.ResolveForScreenRect(new Rect(1400, 100, 300, 200), 1, 1);
        return secondary.WorkAreaDip == new Rect(1280, 0, 1280, 720) &&
               secondary.DpiScaleX == 1.5 && primary.DpiScaleX == 1;
    }

    internal static bool FloatingWindowStaysOnSecondaryMonitor()
    {
        var settings = Path.Combine(Path.GetTempPath(), $"monitor-geometry-{Guid.NewGuid():N}.json");
        FloatingWindow? floating = null;
        MainWindow? manager = null;
        try
        {
            var workspace = DesktopWorkspace.Load(settings, new SettingsStore());
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            manager = new MainWindow(workspace, calendar, new DiagnosticLogger(settings + ".log"), AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1),
                new MonitorWorkArea(new Rect(-1536, 0, 1536, 826), 1.25, 1.25));
            floating = new FloatingWindow(workspace, calendar, manager, AppContext.BaseDirectory);
            floating.Show();
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.Left = -1400;
            floating.Top = 100;
            floating.Reanchor();
            WindowValidationInput.Pump(floating.Dispatcher);
            var retained = Math.Abs(floating.Left + 1400) < 1 &&
                   Math.Abs(floating.Top - 100) < 1 &&
                   Math.Abs(floating.ActualWidth - 520) < 1 &&
                   Math.Abs(floating.ActualHeight - 390) < 1;
            floating.Left = -1700;
            WindowValidationInput.RaisePreviewKey(floating, Key.R, ModifierKeys.Control);
            WindowValidationInput.Pump(floating.Dispatcher);
            var keyReset = Math.Abs(floating.Left + 1524) < 1;
            using var tray = new FloatingTrayController(floating,
                Path.Combine(AppContext.BaseDirectory, "countdown-icon.ico"),
                static () => true, null, attachShellNotifyIcon: false);
            floating.Left = -1700;
            tray.InvokeResetPositionForValidation();
            WindowValidationInput.Pump(floating.Dispatcher);
            return retained && keyReset && Math.Abs(floating.Left + 1524) < 1 &&
                   MonitorWorkAreaResolver.ResolveForWindow(floating).DpiScaleX == 1.25;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MONITOR_GEOMETRY_PROBE: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            floating?.Close();
            manager?.Close();
            foreach (var path in new[] { settings, settings + ".log", settings + ".backup1", settings + ".backup2" })
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    internal static bool OwnerDialogsFitSecondaryMonitor()
    {
        var owner = new Window { Width = 600, Height = 400, Left = -1400, Top = 80, ShowInTaskbar = false };
        ReminderRulesEditorWindow? editor = null;
        ConfirmationDialog? confirmation = null;
        try
        {
            using var monitors = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1),
                new MonitorWorkArea(new Rect(-1536, 0, 1536, 688), 1.5, 1.5));
            owner.Show();
            editor = new ReminderRulesEditorWindow(Array.Empty<ReminderRule>(), SystemClock.Instance) { Owner = owner };
            editor.Show();
            WindowValidationInput.Pump(editor.Dispatcher);
            confirmation = new ConfirmationDialog(owner, "确认", "显示器工作区几何验证");
            confirmation.Show();
            WindowValidationInput.Pump(confirmation.Dispatcher);
            var area = new Rect(-1536, 0, 1536, 688);
            return Contains(area, editor) && editor.ActualHeight <= 652.5 &&
                   Contains(area, confirmation) && editor.Left < 0 && confirmation.Left < 0 &&
                   ElementReachable(editor, "AcceptReminderRulesButton") &&
                   ElementReachable(editor, "CancelReminderRulesButton") &&
                   Descendants(editor).OfType<ResizeGrip>().Any(grip => IsReachable(editor, grip));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"OWNER_GEOMETRY_PROBE: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally { confirmation?.Close(); editor?.Close(); owner.Close(); }
    }

    private static bool Contains(Rect area, Window window) =>
        window.Left >= area.Left - .5 && window.Top >= area.Top - .5 &&
        window.Left + window.ActualWidth <= area.Right + .5 &&
        window.Top + window.ActualHeight <= area.Bottom + .5;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static bool ElementReachable(Window window, string id) => Descendants(window)
        .OfType<FrameworkElement>().Any(element => AutomationProperties.GetAutomationId(element) == id && IsReachable(window, element));

    private static bool IsReachable(Window window, FrameworkElement element)
    {
        if (!element.IsVisible || !element.IsEnabled || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        return new Rect(window.RenderSize).Contains(bounds) &&
               element.InputHitTest(new Point(element.ActualWidth / 2, element.ActualHeight / 2)) is not null;
    }

    internal static bool ReminderUsesSourceMonitor()
    {
        if (!ReminderPlacementUsesPositiveMixedDpi()) return false;
        var source = new Window { Width = 520, Height = 390, Left = -1400, Top = 100, ShowInTaskbar = false };
        ReminderCenterWindow? center = null;
        try
        {
            using var monitors = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1),
                new MonitorWorkArea(new Rect(-1536, 0, 1536, 826), 1.25, 1.25));
            source.Show();
            center = new ReminderCenterWindow(source);
            center.Show();
            WindowValidationInput.Pump(center.Dispatcher);
            var firstPlacement = Math.Abs(center.Left + center.ActualWidth + 18) < 1 &&
                   Math.Abs(center.Top - 18) < 1 && center.Owner is null &&
                   !center.ShowActivated && center.ShowInTaskbar;
            center.Left = -1100;
            center.Top = 120;
            SendMessageW(new WindowInteropHelper(center).Handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
            center.ReapplyPlacement();
            WindowValidationInput.Pump(center.Dispatcher);
            return firstPlacement && Math.Abs(center.Left + 1100) < 1 && Math.Abs(center.Top - 120) < 1;
        }
        finally { center?.ForceClose(); source.Close(); }
    }

    private static bool ReminderPlacementUsesPositiveMixedDpi()
    {
        var source = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
        ReminderCenterWindow? center = null;
        try
        {
            source.Show();
            WindowValidationInput.Pump(source.Dispatcher);
            var dpi = VisualTreeHelper.GetDpi(source);
            // Main [0,1920] at the current HWND DPI; right screen [1920,3840]
            // at twice that DPI. Its DIP origin overlaps the main screen.
            using var monitors = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920 / dpi.DpiScaleX, 1080 / dpi.DpiScaleY), dpi.DpiScaleX, dpi.DpiScaleY),
                new MonitorWorkArea(new Rect(960 / dpi.DpiScaleX, 0, 960 / dpi.DpiScaleX, 540 / dpi.DpiScaleY), dpi.DpiScaleX * 2, dpi.DpiScaleY * 2));
            source.Left = 2200 / dpi.DpiScaleX;
            source.Top = 100 / dpi.DpiScaleY;
            WindowValidationInput.Pump(source.Dispatcher);
            center = new ReminderCenterWindow(source);
            center.Show();
            WindowValidationInput.Pump(center.Dispatcher);
            var handle = new WindowInteropHelper(center).Handle;
            var physicalRead = GetWindowRect(handle, out var physical);
            var visibleOnRight = physicalRead && physical.Left >= 1920 && physical.Right <= 3840 &&
                physical.Top >= 0 && physical.Bottom <= 1080;
            center.ReapplyPlacement();
            WindowValidationInput.Pump(center.Dispatcher);
            var retainedOnRight = GetWindowRect(handle, out var reapplied) &&
                reapplied.Left >= 1920 && reapplied.Right <= 3840 &&
                reapplied.Top >= 0 && reapplied.Bottom <= 1080;
            Console.Error.WriteLine($"REMINDER_MIXED_DPI_PLACEMENT: physical={physical.Left},{physical.Top},{physical.Right},{physical.Bottom}; target=1920,0,3840,1080; onRight={visibleOnRight}");
            return visibleOnRight && retainedOnRight && center.Owner is null && !center.ShowActivated && center.ShowInTaskbar;
        }
        finally { center?.ForceClose(); source.Close(); }
    }

    internal static bool OpenWindowsFollowMonitorChanges()
    {
        var settings = Path.Combine(Path.GetTempPath(), $"monitor-change-{Guid.NewGuid():N}.json");
        var windows = new List<Window>();
        try
        {
            var workspace = DesktopWorkspace.Load(settings, new SettingsStore());
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitors = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1),
                new MonitorWorkArea(new Rect(-1536, 0, 1536, 826), 1.25, 1.25));
            var main = new MainWindow(workspace, calendar, new DiagnosticLogger(settings + ".log"), AppContext.BaseDirectory);
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
            var center = new ReminderCenterWindow(floating);
            var editor = new ReminderRulesEditorWindow(Array.Empty<ReminderRule>(), SystemClock.Instance) { Owner = main };
            var confirmation = new ConfirmationDialog(main, "确认", "拓扑变化验证");
            windows.AddRange(new Window[] { main, floating, center, editor, confirmation });
            foreach (var window in windows)
            {
                window.Show();
                WindowValidationInput.Pump(window.Dispatcher);
                window.Left = -1200;
                window.Top = 80;
                SendMessageW(new WindowInteropHelper(window).Handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
            }
            using var removedSecondary = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1280, 688), 1.5, 1.5));
            foreach (var window in windows)
            {
                SendMessageW(new WindowInteropHelper(window).Handle, 0x007E, IntPtr.Zero, IntPtr.Zero);
                WindowValidationInput.Pump(window.Dispatcher);
            }
            var area = new Rect(0, 0, 1280, 688);
            var displayChanged = windows.All(window => Contains(area, window));
            using var smallerWorkArea = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1024, 650), 1.5, 1.5));
            foreach (var window in windows)
            {
                window.Left = 800;
                window.Top = 600;
                SendMessageW(new WindowInteropHelper(window).Handle, 0x001A, IntPtr.Zero, IntPtr.Zero);
                WindowValidationInput.Pump(window.Dispatcher);
            }
            return displayChanged && windows.All(window => Contains(new Rect(0, 0, 1024, 650), window));
        }
        finally
        {
            foreach (var window in windows.AsEnumerable().Reverse())
            {
                if (window is ReminderCenterWindow center) center.ForceClose();
                else window.Close();
            }
            foreach (var path in new[] { settings, settings + ".log", settings + ".backup1", settings + ".backup2" })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct PhysicalRect { internal int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out PhysicalRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);
}
