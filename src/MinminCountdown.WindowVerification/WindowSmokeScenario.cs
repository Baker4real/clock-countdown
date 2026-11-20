using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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

    internal static void RunConfirmationDialogTitle()
    {
        var scenarios = new (string Title, string Message, string ConfirmText, string ConfirmStyleKey)[]
        {
            ("删除倒计时", "确定删除「项目上线」吗？删除后 10 秒内可撤销。", "确认删除", "DangerButton"),
            ("删除便签", "确定删除「整理周报」吗？删除后 10 秒内可撤销。", "确认删除", "DangerButton"),
            ("删除所选便签", "确定删除所选 2 条便签吗？删除后 10 秒内可撤销。", "确认删除", "DangerButton"),
            ("退出敏敏子倒计时", "退出后不再提醒。确定退出吗？", "退出", "DangerButton"),
            ("完成便签", "确定完成「整理周报」吗？完成将清除该便签在提醒中心的全部条目。", "确认完成", "SecondaryButton"),
            ("删除标签", "标签会从记录中解除，但记录本身不会删除。", "确认删除", "DangerButton")
        };
        Window? owner = null;
        try
        {
            owner = new Window { Width = 640, Height = 480 };
            PrepareOffscreen(owner);
            owner.Show();
            WindowValidationInput.Pump(owner.Dispatcher);

            foreach (var scenario in scenarios)
            {
                ConfirmationDialog? dialog = null;
                try
                {
                    dialog = new ConfirmationDialog(
                        owner,
                        scenario.Title,
                        scenario.Message,
                        scenario.ConfirmText,
                        scenario.ConfirmStyleKey);
                    var cancel = ShowAndFind<Button>(dialog, "ConfirmationCancelButton");
                    var confirm = Matches<Button>(dialog, "ConfirmationConfirmButton").Single();
                    var contentRoot = Matches<FrameworkElement>(dialog, "ConfirmationContentRoot").Single();
                    var heading = Matches<TextBlock>(dialog, "ConfirmationHeading").Single();
                    var body = Matches<TextBlock>(dialog, "ConfirmationBody").Single();
                    var scroller = Matches<ScrollViewer>(dialog, "ConfirmationBodyScroller").Single();
                    if (!contentRoot.IsVisible || !heading.IsVisible || !body.IsVisible || !scroller.IsVisible || !confirm.IsVisible)
                    {
                        throw new InvalidOperationException($"确认框「{scenario.Title}」的既有自动化元素不可见。");
                    }

                    var windowTitle = ((Window)dialog).Title;
                    var windowPeerName = UIElementAutomationPeer.CreatePeerForElement(dialog)?.GetName();
                    var visibleTitles = VisualDescendants<TextBlock>(dialog)
                        .Where(text => text.IsVisible && string.Equals(text.Text, scenario.Title, StringComparison.Ordinal))
                        .ToArray();
                    if (!string.Equals(windowTitle, scenario.Title, StringComparison.Ordinal) ||
                        !string.Equals(windowPeerName, scenario.Title, StringComparison.Ordinal) ||
                        visibleTitles.Length != 1 ||
                        !ReferenceEquals(visibleTitles.Single(), heading) ||
                        !string.Equals(ReadAutomationId(visibleTitles.Single()), "ConfirmationHeading", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"确认框「{scenario.Title}」标题合同失败：windowTitle={windowTitle}，" +
                            $"windowPeerName={windowPeerName}，visibleTitles={visibleTitles.Length}。");
                    }

                    if (!cancel.IsDefault || !cancel.IsCancel || confirm.IsDefault || confirm.IsCancel ||
                        !string.Equals(AutomationProperties.GetName(cancel), "取消", StringComparison.Ordinal) ||
                        !string.Equals(AutomationProperties.GetName(confirm), scenario.ConfirmText, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"确认框「{scenario.Title}」按钮默认或可访问名称异常：" +
                            $"cancelDefault={cancel.IsDefault}，cancelIsCancel={cancel.IsCancel}，" +
                            $"confirmDefault={confirm.IsDefault}，confirmIsCancel={confirm.IsCancel}，" +
                            $"cancelName={AutomationProperties.GetName(cancel)}，confirmName={AutomationProperties.GetName(confirm)}。");
                    }
                }
                finally
                {
                    if (dialog is not null)
                    {
                        Close(dialog);
                    }
                }
            }

            if (RunConfirmationModalAction(owner, "删除便签", "确定删除这张便签吗？", ConfirmationModalAction.Enter) == true)
            {
                throw new InvalidOperationException("确认框按 Enter 不应执行危险动作。");
            }
            if (RunConfirmationModalAction(owner, "删除便签", "确定删除这张便签吗？", ConfirmationModalAction.Escape) == true)
            {
                throw new InvalidOperationException("确认框按 Esc 不应执行危险动作。");
            }
            if (RunConfirmationModalAction(owner, "删除便签", "确定删除这张便签吗？", ConfirmationModalAction.Cancel) != false)
            {
                throw new InvalidOperationException("确认框触发取消按钮应返回 false。");
            }
            if (RunConfirmationModalAction(owner, "退出敏敏子倒计时", "退出后不再提醒。确定退出吗？", ConfirmationModalAction.Enter, "退出", "DangerButton") == true ||
                RunConfirmationModalAction(owner, "退出敏敏子倒计时", "退出后不再提醒。确定退出吗？", ConfirmationModalAction.Escape, "退出", "DangerButton") == true)
            {
                throw new InvalidOperationException("退出确认框按 Enter 或 Esc 不应确认退出。");
            }
            if (RunConfirmationModalAction(owner, "删除便签", "确定删除这张便签吗？", ConfirmationModalAction.Confirm) != true)
            {
                throw new InvalidOperationException("确认框显式触发确认按钮应返回 true。");
            }
        }
        finally
        {
            if (owner is not null)
            {
                Close(owner);
            }
        }
    }

    internal static void RunConfirmationDialogResources()
    {
        static bool ColorIs(Brush? brush, Color color) =>
            brush is SolidColorBrush solid && solid.Color == color;

        static (Wpf.Ui.Markup.ThemesDictionary Themes, Wpf.Ui.Markup.ControlsDictionary Controls) InspectWpfUiDictionaries(
            ConfirmationDialog dialog)
        {
            var themes = dialog.Resources.MergedDictionaries.OfType<Wpf.Ui.Markup.ThemesDictionary>().ToArray();
            var controls = dialog.Resources.MergedDictionaries.OfType<Wpf.Ui.Markup.ControlsDictionary>().ToArray();
            if (themes.Length != 1 || controls.Length != 1 ||
                themes[0].Source?.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) != true)
            {
                throw new InvalidOperationException(
                    $"确认框局部 WPF-UI 字典结构异常：themes={themes.Length}，controls={controls.Length}，themeSource={themes.FirstOrDefault()?.Source}。");
            }
            return (themes[0], controls[0]);
        }

        static Border FindFooterSeparator(ConfirmationDialog dialog) =>
            VisualDescendants<Border>(dialog).Single(border =>
                border.BorderThickness == new Thickness(0, 1, 0, 0) &&
                border.VerticalAlignment == VerticalAlignment.Bottom);

        AssertApplicationResourcesFreeOfWpfUi("确认框打开前");
        Window? owner = null;
        ConfirmationDialog? first = null;
        ConfirmationDialog? second = null;
        ConfirmationDialog? third = null;
        try
        {
            owner = new Window { Width = 640, Height = 480 };
            PrepareOffscreen(owner);
            owner.Show();
            WindowValidationInput.Pump(owner.Dispatcher);

            first = new ConfirmationDialog(owner, "删除便签", "第一次打开确认框。");
            _ = ShowAndFind<Button>(first, "ConfirmationCancelButton");
            var firstResources = first.Resources;
            var (firstThemes, firstControls) = InspectWpfUiDictionaries(first);
            if (!ColorIs(firstResources["MessageBoxBackground"] as Brush, ApplicationTheme.DayColorFor("BackgroundBrush")) ||
                !ColorIs(firstResources["MessageBoxTopOverlay"] as Brush, ApplicationTheme.DayColorFor("BackgroundBrush")) ||
                !ColorIs(firstResources["MessageBoxSeparatorBorderBrush"] as Brush, ApplicationTheme.DayColorFor("PeachLineBrush")) ||
                !ColorIs(firstResources["ButtonBackground"] as Brush, ApplicationTheme.DayColorFor("CardBrush")) ||
                !ColorIs(firstResources["AccentButtonBackground"] as Brush, ApplicationTheme.DayColorFor("MainActionBrush")) ||
                firstResources["PaletteRedColor"] is not Color dayDanger ||
                dayDanger != ApplicationTheme.DayColorFor("DangerBrush"))
            {
                throw new InvalidOperationException("确认框日间 WPF-UI 资源映射与暖粉主题不一致。");
            }

            // 并发打开第二个确认框：复用同一份局部资源字典。
            second = new ConfirmationDialog(owner, "删除倒计时", "并发打开第二个确认框。");
            _ = ShowAndFind<Button>(second, "ConfirmationCancelButton");
            var (secondThemes, secondControls) = InspectWpfUiDictionaries(second);
            if (!ReferenceEquals(firstResources, second.Resources) ||
                !ReferenceEquals(firstThemes, secondThemes) ||
                !ReferenceEquals(firstControls, secondControls))
            {
                throw new InvalidOperationException("并发打开多个确认框时重建了 WPF-UI 资源字典，未复用同一份实例。");
            }

            Close(first);
            first = null;
            Close(second);
            second = null;

            // 全部关闭后再顺序打开第三个确认框：仍复用同一份局部资源字典。
            third = new ConfirmationDialog(owner, "删除标签", "顺序打开第三个确认框。");
            _ = ShowAndFind<Button>(third, "ConfirmationCancelButton");
            var (thirdThemes, thirdControls) = InspectWpfUiDictionaries(third);
            if (!ReferenceEquals(firstResources, third.Resources) ||
                !ReferenceEquals(firstThemes, thirdThemes) ||
                !ReferenceEquals(firstControls, thirdControls))
            {
                throw new InvalidOperationException("连续打开多个确认框时重建了 WPF-UI 资源字典，未在进程内只构建一次。");
            }

            Close(third);
            third = null;

            // 确认框全部关闭时切换系统对比度配色，再新开确认框，验证复用同一字典且配色正确。
            var paletteA = new ApplicationTheme.SystemContrastPalette(
                Color.FromRgb(10, 20, 36),
                Colors.White,
                Color.FromRgb(24, 38, 60),
                Colors.White,
                Color.FromRgb(255, 230, 0),
                Colors.Black,
                Color.FromRgb(184, 196, 212));
            var paletteB = new ApplicationTheme.SystemContrastPalette(
                Colors.White,
                Colors.Black,
                Colors.Black,
                Colors.White,
                Colors.Navy,
                Colors.White,
                Colors.Gray);

            using (var scope = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                scope.ChangeColors(paletteA);
                WindowValidationInput.Pump(owner.Dispatcher);

                ConfirmationDialog? contrastFirst = null;
                ConfirmationDialog? contrastSecond = null;
                try
                {
                    contrastFirst = new ConfirmationDialog(owner, "删除便签", "切换高对比度后新开的确认框。");
                    var cancelA = ShowAndFind<Button>(contrastFirst, "ConfirmationCancelButton");
                    var confirmA = Matches<Button>(contrastFirst, "ConfirmationConfirmButton").Single();
                    if (!ReferenceEquals(firstResources, contrastFirst.Resources))
                    {
                        throw new InvalidOperationException("切换系统对比度后新开确认框重建了 WPF-UI 资源字典。");
                    }
                    if (!ColorIs(contrastFirst.Background, paletteA.Window) ||
                        !ColorIs(contrastFirst.Foreground, paletteA.WindowText) ||
                        !ColorIs(contrastFirst.BodyText.Foreground, paletteA.WindowText) ||
                        !ColorIs(contrastFirst.Resources["MessageBoxBackground"] as Brush, paletteA.Window) ||
                        !ColorIs(contrastFirst.Resources["MessageBoxTopOverlay"] as Brush, paletteA.Window) ||
                        !ColorIs(contrastFirst.Resources["MessageBoxSeparatorBorderBrush"] as Brush, paletteA.WindowText) ||
                        !ColorIs(contrastFirst.Resources["AccentButtonBackground"] as Brush, paletteA.Highlight) ||
                        contrastFirst.Resources["PaletteRedColor"] is not Color contrastDangerA ||
                        contrastDangerA != paletteA.WindowText ||
                        !ColorIs(FindFooterSeparator(contrastFirst).BorderBrush, paletteA.WindowText) ||
                        !ReferenceEquals(cancelA.Style, Application.Current.FindResource("SecondaryButton")) ||
                        !ReferenceEquals(confirmA.Style, Application.Current.FindResource("PrimaryButton")))
                    {
                        throw new InvalidOperationException("切换系统对比度主题后新开确认框未应用当前系统配色。");
                    }

                    // 确认框打开中再次切换系统对比度配色：已开与随后新开的确认框均同步更新。
                    scope.ChangeColors(paletteB);
                    WindowValidationInput.Pump(owner.Dispatcher);
                    contrastFirst.UpdateLayout();
                    if (!ColorIs(contrastFirst.Background, paletteB.Window) ||
                        !ColorIs(contrastFirst.Foreground, paletteB.WindowText) ||
                        !ColorIs(contrastFirst.BodyText.Foreground, paletteB.WindowText) ||
                        !ColorIs(FindFooterSeparator(contrastFirst).BorderBrush, paletteB.WindowText))
                    {
                        throw new InvalidOperationException("已打开的确认框在系统对比度配色切换后未实时刷新。");
                    }

                    contrastSecond = new ConfirmationDialog(owner, "退出敏敏子倒计时", "二次切换高对比度后新开的确认框。", "退出", "DangerButton");
                    _ = ShowAndFind<Button>(contrastSecond, "ConfirmationCancelButton");
                    if (!ReferenceEquals(firstResources, contrastSecond.Resources) ||
                        !ColorIs(contrastSecond.Background, paletteB.Window) ||
                        !ColorIs(contrastSecond.BodyText.Foreground, paletteB.WindowText) ||
                        !ColorIs(FindFooterSeparator(contrastSecond).BorderBrush, paletteB.WindowText))
                    {
                        throw new InvalidOperationException("二次切换系统对比度后新开确认框未复用字典或未应用新配色。");
                    }
                }
                finally
                {
                    if (contrastSecond is not null)
                    {
                        Close(contrastSecond);
                    }
                    if (contrastFirst is not null)
                    {
                        Close(contrastFirst);
                    }
                }
            }

            // 退出系统对比度后新开确认框：仍复用同一字典并恢复暖粉日间配色。
            WindowValidationInput.Pump(owner.Dispatcher);
            ConfirmationDialog? restored = null;
            try
            {
                restored = new ConfirmationDialog(owner, "完成便签", "恢复暖粉日间后新开的确认框。", "确认完成", "SecondaryButton");
                var restoredCancel = ShowAndFind<Button>(restored, "ConfirmationCancelButton");
                if (!ReferenceEquals(firstResources, restored.Resources) ||
                    !ColorIs(restored.Background, ApplicationTheme.DayColorFor("BackgroundBrush")) ||
                    !ColorIs(restored.Foreground, ApplicationTheme.DayColorFor("InkBrush")) ||
                    !ColorIs(restored.Resources["MessageBoxBackground"] as Brush, ApplicationTheme.DayColorFor("BackgroundBrush")) ||
                    !ColorIs(restored.Resources["MessageBoxSeparatorBorderBrush"] as Brush, ApplicationTheme.DayColorFor("PeachLineBrush")) ||
                    !ColorIs(FindFooterSeparator(restored).BorderBrush, ApplicationTheme.DayColorFor("PeachLineBrush")) ||
                    restored.Resources["PaletteRedColor"] is not Color restoredDanger ||
                    restoredDanger != ApplicationTheme.DayColorFor("DangerBrush") ||
                    ReferenceEquals(restoredCancel.Style, Application.Current.FindResource("SecondaryButton")))
                {
                    throw new InvalidOperationException("退出系统对比度后新开确认框未恢复暖粉日间配色与按钮样式。");
                }
            }
            finally
            {
                if (restored is not null)
                {
                    Close(restored);
                }
            }

            AssertApplicationResourcesFreeOfWpfUi("确认框关闭后");
        }
        finally
        {
            if (third is not null)
            {
                Close(third);
            }
            if (second is not null)
            {
                Close(second);
            }
            if (first is not null)
            {
                Close(first);
            }
            if (owner is not null)
            {
                Close(owner);
            }
        }
    }

    private enum ConfirmationModalAction
    {
        Enter,
        Escape,
        Cancel,
        Confirm
    }

    private static bool? RunConfirmationModalAction(
        Window owner,
        string title,
        string message,
        ConfirmationModalAction action,
        string confirmText = "确认删除",
        string confirmStyleKey = "DangerButton")
    {
        var dispatched = false;
        var timedOut = false;
        var dialog = new ConfirmationDialog(owner, title, message, confirmText, confirmStyleKey)
        {
            ShowInTaskbar = false,
            Opacity = 0,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000
        };
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dialog.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (dialog.IsVisible)
            {
                timedOut = true;
                dialog.Close();
            }
        };
        dialog.ContentRendered += (_, _) =>
        {
            dialog.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    var cancel = Matches<Button>(dialog, "ConfirmationCancelButton").SingleOrDefault();
                    var confirm = Matches<Button>(dialog, "ConfirmationConfirmButton").SingleOrDefault();
                    if (cancel is null || confirm is null)
                    {
                        dialog.Close();
                        return;
                    }
                    if (action == ConfirmationModalAction.Cancel)
                    {
                        dispatched = WindowValidationInput.InvokeButton(cancel);
                        return;
                    }
                    if (action == ConfirmationModalAction.Confirm)
                    {
                        dispatched = WindowValidationInput.InvokeButton(confirm);
                        return;
                    }

                    dialog.Activate();
                    cancel.Focus();
                    var handle = new WindowInteropHelper(dialog).Handle;
                    var virtualKey = action == ConfirmationModalAction.Enter ? 0x0D : 0x1B;
                    dispatched =
                        handle != IntPtr.Zero &&
                        PostMessage(handle, 0x0100, new IntPtr(virtualKey), IntPtr.Zero) &&
                        PostMessage(handle, 0x0101, new IntPtr(virtualKey), IntPtr.Zero);
                    if (!dispatched)
                    {
                        dialog.Close();
                    }
                }));
            timer.Start();
        };
        try
        {
            var result = dialog.ShowDialog();
            if (!dispatched || timedOut)
            {
                throw new InvalidOperationException(
                    $"确认框模态输入 {action} 未完成：dispatched={dispatched}，timedOut={timedOut}。");
            }
            return result;
        }
        finally
        {
            timer.Stop();
            if (dialog.IsVisible)
            {
                dialog.Close();
            }
            WindowValidationInput.Pump(owner.Dispatcher);
        }
    }

    private static void AssertApplicationResourcesFreeOfWpfUi(string phase)
    {
        var forbiddenKeys = new object[]
        {
            typeof(Wpf.Ui.Controls.MessageBox),
            "MessageBoxBackground",
            "MessageBoxForeground",
            "MessageBoxTopOverlay",
            "MessageBoxSeparatorBorderBrush",
            "ButtonBackground",
            "AccentButtonBackground",
            "PaletteRedColor"
        };
        var queue = new Queue<ResourceDictionary>();
        queue.Enqueue(Application.Current.Resources);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is Wpf.Ui.Markup.ThemesDictionary or Wpf.Ui.Markup.ControlsDictionary ||
                current.Source?.OriginalString.Contains("Wpf.Ui", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new InvalidOperationException($"{phase}：WPF-UI 资源字典进入了应用级资源（{current.GetType().FullName}）。");
            }
            foreach (var key in forbiddenKeys)
            {
                if (current.Contains(key))
                {
                    throw new InvalidOperationException($"{phase}：WPF-UI 资源键进入了应用级资源（{key}）。");
                }
            }
            foreach (var merged in current.MergedDictionaries)
            {
                queue.Enqueue(merged);
            }
        }
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
