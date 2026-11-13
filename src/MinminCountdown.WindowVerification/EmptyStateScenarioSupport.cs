using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown.WindowVerification;

// #274：空态与溢出场景共享完全等价的辅助逻辑；各自保留 DPI 编排和不同控件判定。
internal static class EmptyStateScenarioSupport
{
    internal static string SaveCountdown(
        DesktopWorkspace workspace,
        string name,
        int daysAhead,
        Dispatcher dispatcher)
    {
        Apply(workspace, new SaveCountdownChange(
            null,
            name,
            workspace.Snapshot.UtcNow.AddDays(daysAhead),
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()), dispatcher);
        return workspace.Snapshot.Settings.Countdowns.Single(record => record.Name == name).Id;
    }

    internal static string SaveNote(DesktopWorkspace workspace, string text, Dispatcher dispatcher)
    {
        Apply(workspace, new SaveNoteChange(
            null,
            text,
            workspace.Snapshot.BeijingNow.Date,
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()), dispatcher);
        return workspace.Snapshot.Settings.Notes.Single(note => note.Text == text).Id;
    }

    internal static void Apply(
        DesktopWorkspace workspace,
        DesktopWorkspaceChange change,
        Dispatcher dispatcher)
    {
        var result = workspace.Apply(change);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"空态探针写入失败：{result.Error}");
        }

        WindowValidationInput.Pump(dispatcher);
    }

    internal static void RequireEmptyData(DesktopWorkspace workspace)
    {
        var settings = workspace.Snapshot.Settings;
        if (settings.Countdowns.Count > 0 || settings.Notes.Count > 0)
        {
            throw new InvalidOperationException("空态探针要求从没有任何倒计时与便签的数据开始。");
        }
    }

    internal static void SettleLayout(MainWindow main)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            WindowValidationInput.Pump(main.Dispatcher);
            main.UpdateLayout();
        }
    }

    internal static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(new Point(0, 0), element.RenderSize));

    internal static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var descendant in VisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    internal static void DeleteSettingsFiles(string settingsPath)
    {
        foreach (var suffix in new[] { "", ".tmp", ".backup1", ".backup2", ".backup2.tmp", ".backup2.rollback" })
        {
            var path = suffix.Length == 0 ? settingsPath : settingsPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow.ToUniversalTime();
    }

    internal static class NativeWindowMessage
    {
        internal const int WmEnterSizeMove = 0x0231;
        internal const int WmExitSizeMove = 0x0232;

        [DllImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
        private static extern IntPtr SendMessage(
            IntPtr hWnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam);

        internal static void Send(IntPtr handle, int message, IntPtr wParam, IntPtr lParam)
        {
            _ = SendMessage(handle, message, wParam, lParam);
        }
    }
}
