using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #297：后续窗口事实的最小承载骨架。只使用产品现有构造入口、公共视觉树和自动化 peer。
internal static class WindowSmokeScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static void RunFloating()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-window-smoke-{Guid.NewGuid():N}.settings.json");
        FloatingWindow? floating = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            floating = new FloatingWindow(
                workspace,
                calendar,
                new SmokeManagerHost(),
                AppContext.BaseDirectory);
            _ = ShowAndFind<Button>(floating, "OpenManagerButton");
        }
        finally
        {
            if (floating is not null)
            {
                Close(floating);
            }
            DeleteSettingsFiles(settingsPath);
        }
    }

    internal static void RunReminderCenter()
    {
        var center = new ReminderCenterWindow();
        try
        {
            _ = ShowAndFind<Button>(center, "ReminderCenterAcknowledgeAll");
        }
        finally
        {
            Close(center);
        }
    }

    internal static void RunReminderRulesEditor()
    {
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            new FixedClock(FixtureUtc));
        try
        {
            _ = ShowAndFind<ComboBox>(editor, "ReminderRuleMode");
        }
        finally
        {
            Close(editor);
        }
    }

    internal static void RunConfirmationDialog()
    {
        Window? owner = null;
        ConfirmationDialog? dialog = null;
        try
        {
            owner = new Window { Width = 640, Height = 480 };
            PrepareOffscreen(owner);
            owner.Show();
            WindowValidationInput.Pump(owner.Dispatcher);
            dialog = new ConfirmationDialog(
                owner,
                "窗口验证确认",
                "确认框已完成首帧，并可定位安全取消操作。");
            _ = ShowAndFind<Button>(dialog, "ConfirmationCancelButton");
        }
        finally
        {
            if (dialog is not null)
            {
                Close(dialog);
            }
            if (owner is not null)
            {
                Close(owner);
            }
        }
    }

    private static T ShowAndFind<T>(Window window, string automationId)
        where T : FrameworkElement
    {
        PrepareOffscreen(window);
        var contentRendered = false;
        EventHandler rendered = (_, _) => contentRendered = true;
        window.ContentRendered += rendered;
        try
        {
            window.Show();
            var firstFrame = WindowValidationInput.PumpRenderedFrames(
                window.Dispatcher,
                2000,
                () => contentRendered && Matches<T>(window, automationId).Length == 1);
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
            var matches = Matches<T>(window, automationId);
            if (!contentRendered || !firstFrame.Completed || matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"{window.GetType().Name} 首帧或控件定位失败：contentRendered={contentRendered}，" +
                    $"frameCompleted={firstFrame.Completed}，automationId={automationId}，matches={matches.Length}。");
            }
            if (!matches[0].IsVisible)
            {
                throw new InvalidOperationException(
                    $"{window.GetType().Name} 的 {automationId} 已完成定位，但控件不可见。");
            }
            return matches[0];
        }
        finally
        {
            window.ContentRendered -= rendered;
        }
    }

    private static T[] Matches<T>(Window window, string automationId)
        where T : FrameworkElement =>
        VisualDescendants<T>(window)
            .Where(element => string.Equals(
                ReadAutomationId(element),
                automationId,
                StringComparison.Ordinal))
            .ToArray();

    private static string ReadAutomationId(FrameworkElement element)
    {
        var explicitId = AutomationProperties.GetAutomationId(element);
        if (!string.IsNullOrEmpty(explicitId))
        {
            return explicitId;
        }

        // WPF 将 XAML x:Name 暴露为自动化标识；悬浮窗现有按钮经公共 peer 回读，不新增产品钩子。
        return UIElementAutomationPeer.CreatePeerForElement(element)?.GetAutomationId() ?? string.Empty;
    }

    private static void PrepareOffscreen(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20_000;
        window.Top = -20_000;
        window.Opacity = 0;
        window.Topmost = false;
    }

    private static void Close(Window window)
    {
        window.Close();
        WindowValidationInput.Pump(window.Dispatcher);
    }

    private sealed class SmokeManagerHost : IManagerWorkspaceHost
    {
        public void Reveal(WorkspaceKind workspace)
        {
        }

        public void RevealPreferences(WorkspaceKind workspace)
        {
        }
    }
}
