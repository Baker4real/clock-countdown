using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    public static bool RunReminderCenterDeliveryFailureActionGateBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 13, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-delivery-gate-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(workspace.Apply(new SaveNoteChange(
                    null,
                    "送达失败动作门探针",
                    new DateTime(2036, 5, 13),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立送达失败动作门探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            Directory.CreateDirectory(settingsPath + ".tmp");
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var window = coordinator.ActiveWindow;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var center = window ?? throw new InvalidOperationException("提醒中心未打开。");
            Must(
                center.IsFailureVisible(key) &&
                !center.IsAcknowledgeEnabled(key) &&
                !center.IsSnoozeEnabled(key) &&
                !center.IsCompleteEnabled(key) &&
                center.IsOpenSourceEnabled(key) &&
                coordinator.ActiveEntryCount == 1,
                "送达保存失败期间没有只保留安全的打开来源动作。");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");

            clock.UtcNow = dueUtc.AddSeconds(5);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsAcknowledgeEnabled(key) &&
                center.IsSnoozeEnabled(key) &&
                center.IsCompleteEnabled(key) &&
                coordinator.ActiveEntryCount == 1 &&
                workspace.Snapshot.Settings.ReminderStates[rule.Id].LastDeliveredOccurrenceKey ==
                    ReminderOccurrenceKey.Create(rule, dueUtc).ToString(),
                "同一 occurrence 送达重试成功后动作门没有恢复。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterEnterUsesNativeButtonActivationBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 14, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-enter-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心 Enter 焦点条目甲",
                    new DateTime(2036, 5, 14),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立 Enter 焦点条目甲。");
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心 Enter 焦点条目乙",
                    new DateTime(2036, 5, 14),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立 Enter 焦点条目乙。");
            var ruleAlpha = workspace.Snapshot.Settings.Notes
                .Single(note => note.Text == "提醒中心 Enter 焦点条目甲")
                .ReminderRules.Single();
            var ruleBeta = workspace.Snapshot.Settings.Notes
                .Single(note => note.Text == "提醒中心 Enter 焦点条目乙")
                .ReminderRules.Single();
            var keyAlpha = new ActiveReminderOccurrenceKey(ruleAlpha.Id, dueUtc);
            var keyBeta = new ActiveReminderOccurrenceKey(ruleBeta.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(keyAlpha) &&
                center.IsSnoozeEnabled(keyBeta),
                "两条到点提醒没有全部进入可处置状态。");
            Must(
                VisualDescendants<Button>(center).All(button => !button.IsDefault),
                "提醒中心按钮不应再声明 IsDefault 默认按钮。");

            // #215：窗口级不再拦截 Enter——焦点在条目按钮上时，按在窗口上的 Enter 不处置任何条目。
            var snoozeBeta = FindReminderEntryButton(center, keyBeta, "稍后提醒：");
            center.Activate();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Keyboard.Focus(snoozeBeta);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                ReferenceEquals(Keyboard.FocusedElement, snoozeBeta),
                "Enter 前键盘焦点未落在条目乙的稍后提醒按钮。");
            WindowValidationInput.RaiseKey(center, Key.Enter);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                workspace.Snapshot.Settings.ReminderStates[ruleBeta.Id].ActiveSnooze is null &&
                center.IsSnoozeEnabled(keyBeta),
                "按在窗口上的 Enter 不应处置焦点条目。");
            // 焦点离开条目后按 Enter 同样不处置。
            Keyboard.ClearFocus();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            WindowValidationInput.RaiseKey(center, Key.Enter);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                workspace.Snapshot.Settings.ReminderStates[ruleBeta.Id].ActiveSnooze is null,
                "焦点离开条目后 Enter 不应处置任何条目。");

            // 按钮键盘激活交给控件原生行为：按在聚焦按钮自身的 Enter 触发该按钮命令。
            Keyboard.Focus(snoozeBeta);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                ReferenceEquals(Keyboard.FocusedElement, snoozeBeta),
                "Enter 前键盘焦点未落回条目乙的稍后提醒按钮。");
            WindowValidationInput.RaiseKey(snoozeBeta, Key.Enter);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var stateBeta = workspace.Snapshot.Settings.ReminderStates[ruleBeta.Id];
            Must(
                coordinator.ActiveEntryCount == 1 &&
                stateBeta.ActiveSnooze?.OccurrenceKey ==
                    ReminderOccurrenceKey.Create(ruleBeta, dueUtc).ToString() &&
                stateBeta.ActiveSnooze.SnoozeUntilUtc == dueUtc.AddMinutes(10),
                "聚焦按钮上的 Enter 没有原生触发该条目的 10 分钟稍后提醒。");
            Must(
                workspace.Snapshot.Settings.ReminderStates[ruleAlpha.Id].ActiveSnooze is null &&
                center.IsAcknowledgeEnabled(keyAlpha),
                "焦点外条目甲被误处置。");

            var acknowledgeAlpha = FindReminderEntryButton(center, keyAlpha, "知道了：");
            Keyboard.Focus(acknowledgeAlpha);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            WindowValidationInput.RaiseKey(acknowledgeAlpha, Key.Enter);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                workspace.Snapshot.Settings.ReminderStates[ruleAlpha.Id].LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(ruleAlpha, dueUtc).ToString(),
                "聚焦知道了按钮上的 Enter 没有原生确认最后一条并收起空中心。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterArrivalKeepsForegroundAndFlashesTaskbarBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 6, 22, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-arrival-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心到达不抢焦点条目",
                    new DateTime(2036, 6, 22),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立到达条目。");
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心到达二次条目",
                    new DateTime(2036, 6, 22),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc.AddMinutes(15)))).Succeeded,
                "无法建立到达二次条目。");
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 1 && center.IsVisible,
                "到达提醒没有进入可见提醒中心。");
            // #215：出现时不激活、不抢焦点、不持续置顶；显示在任务栏且标题带条目数。
            Must(
                !center.ShowActivated && !center.Topmost && center.ShowInTaskbar,
                "提醒中心出现时必须不激活、不置顶并显示在任务栏。");
            Must(
                !center.IsActive &&
                (Keyboard.FocusedElement is not DependencyObject focused ||
                 !center.IsAncestorOf(focused)),
                "提醒中心到达不得抢占激活态或键盘焦点。");
            Must(
                center.Title.Contains("1 条", StringComparison.Ordinal),
                "提醒中心任务栏标题必须携带条目数。");
            Must(
                center.TaskbarFlashRequestCount >= 1,
                "未激活时新提醒到达必须请求任务栏闪烁。");

            // Activate the real HWND on this thread's isolated desktop. WPF's
            // Activate() requests the foreground desktop, which CI may deny.
            // The IsActive assertion below still requires a native activation.
            SetActiveWindow(new WindowInteropHelper(center).Handle);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(center.IsActive, "到达探针未能激活提醒中心。");
            clock.UtcNow = dueUtc.AddMinutes(15);
            coordinator.Tick(dueUtc.AddMinutes(15));
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                center.TaskbarFlashRequestCount == 1 &&
                center.Title.Contains("2 条", StringComparison.Ordinal),
                "已激活的提醒中心再次到达不应重复闪烁任务栏。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetActiveWindow(IntPtr windowHandle);

    public static bool RunReminderCenterMinimizeToTaskbarPreservesEntriesBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-minimize-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心收起条目甲",
                    new DateTime(2036, 7, 8),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立收起条目甲。");
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心收起条目乙",
                    new DateTime(2036, 7, 8),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立收起条目乙。");
            var ruleAlpha = workspace.Snapshot.Settings.Notes
                .Single(note => note.Text == "提醒中心收起条目甲")
                .ReminderRules.Single();
            var keyAlpha = new ActiveReminderOccurrenceKey(ruleAlpha.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(keyAlpha),
                "两条到点提醒没有全部进入可处置状态。");

            // #215：头部「收起」按钮把窗口最小化到任务栏，条目、标题计数与关闭守卫全部保留。
            var minimize = VisualDescendants<Button>(center).FirstOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterMinimizeToTaskbar",
                    StringComparison.Ordinal)) ??
                throw new InvalidOperationException("提醒中心头部缺少「收起」按钮。");
            Must(
                minimize.Content is StackPanel content &&
                content.Children.Count == 2 &&
                content.Children[0] is System.Windows.Shapes.Path &&
                content.Children[1] is TextBlock label &&
                string.Equals(label.Text, "收起", StringComparison.Ordinal),
                "「收起」按钮必须是图标加「收起」文字。");
            Must(
                string.Equals(
                    AutomationProperties.GetName(minimize),
                    "收起提醒中心到任务栏",
                    StringComparison.Ordinal),
                "「收起」按钮缺少可访问名。");

            minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.WindowState == WindowState.Minimized && center.IsVisible,
                "点击「收起」后窗口必须最小化且保持可见。");
            Must(
                coordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(keyAlpha) &&
                center.Title.Contains("2 条", StringComparison.Ordinal),
                "收起后条目与任务栏标题计数必须保留。");
            center.Close();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsVisible && center.WindowState == WindowState.Minimized,
                "有条目时收起态仍必须拒绝直接关闭。");

            center.WindowState = WindowState.Normal;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.WindowState == WindowState.Normal &&
                coordinator.ActiveEntryCount == 2 &&
                center.IsAcknowledgeEnabled(keyAlpha),
                "从任务栏恢复后条目必须完好。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterSnoozeGatedDuringCompletionRetryBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 15, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-snooze-gate-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "完成重试期间稍后互斥探针",
                    new DateTime(2036, 5, 15),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立完成重试期间稍后互斥探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var rule = note.ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsCompleteEnabled(key) && center.IsSnoozeEnabled(key),
                "探针条目未进入可处置状态。");

            Directory.CreateDirectory(settingsPath + ".tmp");
            try
            {
                // #155：完成先经确认框（自动确认后提交），写入失败注入保持原语义。
                QueueConfirmationDialogResponse(center, confirm: true);
                Must(center.RequestComplete(key), "无法点击真实完成便签控件。");
                WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
                const string completionFailure =
                    "完成便签状态保存失败，正在自动重试。便签与提醒条目均保持不变。";
                Must(
                    center.IsFailureVisible(key) &&
                    !center.IsCompleteEnabled(key) &&
                    !center.IsSnoozeEnabled(key) &&
                    !center.IsAcknowledgeEnabled(key),
                    "完成保存失败没有禁用处置动作并展示失败反馈。");
                Must(
                    !center.RequestSnooze(key, ReminderSnoozeOption.TenMinutes, null),
                    "完成等待重试期间稍后提醒探针应被拒绝。");
                Must(
                    !coordinator.InvokeSnoozeRequestForValidation(
                        key,
                        ReminderSnoozeOption.TenMinutes,
                        null),
                    "完成等待重试期间直达调度器的稍后提醒请求应被互斥门拒绝。");
                WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
                Must(
                    VisualDescendants<TextBlock>(center).Count(text =>
                        string.Equals(
                            AutomationProperties.GetName(text),
                            completionFailure,
                            StringComparison.Ordinal)) == 1 &&
                    center.IsFailureVisible(key),
                    "互斥门拒绝后完成失败反馈应原样保留。");
                Must(
                    coordinator.ActiveEntryCount == 1 &&
                    workspace.Snapshot.Settings.ReminderStates[rule.Id].ActiveSnooze is null &&
                    !workspace.Snapshot.Settings.Notes.Single().IsCompleted,
                    "互斥门拒绝后条目与便签状态应保持不变。");
            }
            finally
            {
                DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            }

            clock.UtcNow = dueUtc.AddSeconds(6);
            coordinator.Tick(clock.UtcNow);
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveEntryCount == 0 &&
                workspace.Snapshot.Settings.Notes.Single().IsCompleted,
                "完成重试成功后条目应收起并持久化完成状态。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterCustomSnoozeErrorRoutesByCodeBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 16, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-snooze-code-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "自定义稍后错误码探针",
                    new DateTime(2036, 5, 16),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立自定义稍后错误码探针。");
            var note = workspace.Snapshot.Settings.Notes.Single();
            var rule = note.ReminderRules.Single();
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            coordinator.ActiveWindow?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                coordinator.ActiveOccurrenceConfirmed,
                "探针 occurrence 未确认送达。");

            var expiredBeijing = ChinaStandardTime.ToBeijingTime(dueUtc.AddMinutes(-30));
            var viaWorkspace = workspace.SnoozeReminder(
                ReminderSourceKind.Note,
                note.Id,
                rule.Id,
                dueUtc,
                ReminderSnoozeOption.Custom,
                expiredBeijing);
            Must(
                !viaWorkspace.Succeeded &&
                string.Equals(
                    viaWorkspace.Error,
                    "自定义稍后时间必须晚于当前北京时间。",
                    StringComparison.Ordinal),
                "过期自定义稍后的拒绝文案不应变化。");
            Must(
                viaWorkspace.SnoozeErrorCode == ReminderSnoozeErrorCode.CustomSnoozeNotInFuture,
                "过期自定义稍后要携带结构化错误码。");

            Must(
                ReminderDeliveryCoordinator.RoutesToCustomSnoozeError(
                    ReminderSnoozeOption.Custom,
                    new DesktopWorkspaceChangeResult(
                        false,
                        "改写后的任意文案",
                        SnoozeErrorCode: ReminderSnoozeErrorCode.CustomSnoozeNotInFuture)),
                "结构化错误码命中时必须路由到自定义稍后错误样式，与文案无关。");
            Must(
                !ReminderDeliveryCoordinator.RoutesToCustomSnoozeError(
                    ReminderSnoozeOption.Custom,
                    new DesktopWorkspaceChangeResult(
                        false,
                        "自定义稍后时间必须晚于当前北京时间。")),
                "错误码缺失时不得仅凭历史文案路由。");
            Must(
                !ReminderDeliveryCoordinator.RoutesToCustomSnoozeError(
                    ReminderSnoozeOption.TenMinutes,
                    new DesktopWorkspaceChangeResult(
                        false,
                        "任意文案",
                        SnoozeErrorCode: ReminderSnoozeErrorCode.CustomSnoozeNotInFuture)),
                "非自定义稍后选项不得路由到自定义错误样式。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterShowFailureRollsBackBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 17, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-show-rollback-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心展示失败回滚探针",
                    new DateTime(2036, 5, 17),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立展示失败回滚探针。");
            var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var closedWindow = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            closedWindow.Show();
            closedWindow.ForceClose();
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                logger: null,
                windowFactory: () => closedWindow);
            var threw = false;
            try
            {
                coordinator.Tick(dueUtc);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Must(threw, "提醒中心展示失败应继续向宿主抛出。");
            Must(
                coordinator.ActiveWindow is null && coordinator.ActiveEntryCount == 0,
                "展示失败后调度器不应缓存半建窗口或未展示条目。");
            Must(
                closedWindow.EntryCount == 0,
                "展示失败后失败窗口上的条目应被回滚清空。");
            var reloaded = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                !reloaded.Snapshot.Settings.ReminderStates.TryGetValue(rule.Id, out var reloadedState) ||
                (reloadedState.LastDeliveredOccurrenceKey is null &&
                 reloadedState.ActiveSnooze is null),
                "展示失败不应改写提醒设置。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterCustomSnoozeControlsVisibilityBehavior()
    {
        var key = new ActiveReminderOccurrenceKey(
            "custom-snooze-visibility-rule",
            new DateTimeOffset(2036, 6, 20, 1, 0, 0, TimeSpan.Zero));
        var window = new ReminderCenterWindow();
        PrepareOffscreenValidationWindow(window);
        window.AddEntry(new ReminderCenterItem(
            key,
            new NoteRecord(
                "custom-snooze-visibility-note",
                "自定义稍后控件可见性探针",
                "2036-06-20",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        try
        {
            window.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var snoozeChoice = FindReminderEntryControl<ComboBox>(window, key, "稍后提醒时间：");
            var customDate = FindReminderEntryControl<DatePicker>(window, key, "自定义稍后日期：");
            var customHour = FindReminderEntryControl<ComboBox>(window, key, "自定义稍后小时：");
            var customMinute = FindReminderEntryControl<ComboBox>(window, key, "自定义稍后分钟：");
            var customFields = customDate.Parent as WrapPanel
                ?? throw new InvalidOperationException("自定义稍后日期不在自定义稍后控件容器内。");
            Must(
                ReferenceEquals(customHour.Parent, customFields) &&
                ReferenceEquals(customMinute.Parent, customFields),
                "自定义稍后小时和分钟必须与日期共用同一容器。");
            Must(
                customFields.Visibility == Visibility.Collapsed &&
                customDate.Visibility == Visibility.Collapsed &&
                customHour.Visibility == Visibility.Collapsed &&
                customMinute.Visibility == Visibility.Collapsed &&
                !customDate.IsVisible &&
                !customHour.IsVisible &&
                !customMinute.IsVisible,
                "初始预设项下自定义稍后容器与三个子控件必须全部折叠。");

            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                customFields.Visibility == Visibility.Visible &&
                customDate.Visibility == Visibility.Visible &&
                customHour.Visibility == Visibility.Visible &&
                customMinute.Visibility == Visibility.Visible &&
                customDate.IsVisible &&
                customHour.IsVisible &&
                customMinute.IsVisible,
                "选择「自定义」后自定义稍后三个子控件必须随容器真实展开。");

            snoozeChoice.SelectedIndex = 0;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                customFields.Visibility == Visibility.Collapsed &&
                customDate.Visibility == Visibility.Collapsed &&
                customHour.Visibility == Visibility.Collapsed &&
                customMinute.Visibility == Visibility.Collapsed &&
                !customDate.IsVisible &&
                !customHour.IsVisible &&
                !customMinute.IsVisible,
                "切回预设项后自定义稍后三个子控件必须随容器恢复折叠。");

            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                customDate.IsVisible && customHour.IsVisible && customMinute.IsVisible,
                "再次选择「自定义」后三个子控件必须重新可见。");
            return true;
        }
        finally
        {
            if (window.IsVisible)
            {
                window.ForceClose();
            }
        }
    }

    public static bool RunReminderCenterCustomSnoozeCalendarBehavior()
    {
        var key = new ActiveReminderOccurrenceKey(
            "custom-snooze-calendar-rule",
            new DateTimeOffset(2036, 6, 21, 1, 0, 0, TimeSpan.Zero));
        var window = new ReminderCenterWindow();
        PrepareOffscreenValidationWindow(window);
        window.AddEntry(new ReminderCenterItem(
            key,
            new NoteRecord(
                "custom-snooze-calendar-note",
                "自定义稍后日历行为探针",
                "2036-06-21",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        try
        {
            window.Show();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var snoozeChoice = FindReminderEntryControl<ComboBox>(window, key, "稍后提醒时间：");
            var customDate = FindReminderEntryControl<DatePicker>(window, key, "自定义稍后日期：");
            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            window.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                customDate.Visibility == Visibility.Visible && customDate.IsVisible,
                "自定义稍后日历未处于真实可见状态，挂接断言无效。");
            Must(
                PickerTemplateParts.HasDatePickerContract(customDate),
                "自定义稍后日历未使用全应用统一日期模板。");
            customDate.ApplyTemplate();
            Must(
                customDate.Template.FindName("PART_TextBox", customDate) is DatePickerTextBox
                {
                    IsReadOnly: true,
                    Focusable: false
                },
                "自定义稍后日历文本框未保持全应用统一只读。");
            var keyboardWorks = WindowValidationInput.ExerciseDatePicker(customDate, out var keyboardDiagnostic);
            Must(
                keyboardWorks,
                "自定义稍后日历未实现打开聚焦、Esc 关闭并回收焦点的统一行为。" + keyboardDiagnostic);
            customDate.ApplyTemplate();
            Must(
                customDate.Template.FindName("PART_TextBox", customDate) is DatePickerTextBox
                {
                    IsReadOnly: true,
                    Focusable: false
                },
                "自定义稍后日历弹层关闭后未恢复只读锁定。");
            return true;
        }
        finally
        {
            if (window.IsVisible)
            {
                window.ForceClose();
            }
        }
    }

    public static bool RunReminderCenterShowFailureRollbackSurvivesEntryFailureBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 18, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-reminder-center-rollback-atomic-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "提醒中心回滚原子性探针",
                    new DateTime(2036, 5, 18),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded,
                "无法建立回滚原子性探针。");
            var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var closedWindow = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0,
                RemoveEntryThrowsForValidation = true
            };
            closedWindow.Show();
            closedWindow.ForceClose();
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                logger: null,
                windowFactory: () => closedWindow);
            var threw = false;
            try
            {
                coordinator.Tick(dueUtc);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Must(threw, "条目回滚失败仍应把展示失败抛给宿主。");
            Must(
                closedWindow.EntryCount == 1,
                "注入的条目回滚失败未命中，回滚原子性断言无效。");
            Must(
                coordinator.ActiveWindow is null && coordinator.ActiveEntryCount == 0,
                "条目回滚抛异常时协调器仍必须清理缓存窗口与活动条目。");
            var reloaded = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            Must(
                !reloaded.Snapshot.Settings.ReminderStates.TryGetValue(rule.Id, out var reloadedState) ||
                (reloadedState.LastDeliveredOccurrenceKey is null &&
                 reloadedState.ActiveSnooze is null),
                "条目回滚失败不应改写提醒设置。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
        }
    }

    public static bool RunReminderCenterOpenSourcePreservesNoteGroupStateBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-open-source-groups-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2038, 7, 9, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var workspace = DesktopWorkspace.Load(settingsPath, store);
        var dueUtc = nowUtc.AddMinutes(10);
        MustApply(workspace, new SaveNoteChange(
            null,
            "来源保持-甲",
            new DateTime(2038, 7, 10),
            false,
            Array.Empty<string>(),
            ReminderSchedule.CreateOnce(dueUtc)));
        MustApply(workspace, new SaveNoteChange(
            null,
            "来源保持-乙",
            new DateTime(2038, 7, 10),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        MustApply(workspace, new SaveNoteChange(
            null,
            "来源保持-丙",
            new DateTime(2038, 7, 9),
            true,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var relative = new CountdownRelativeReminderRule("open-source-groups-relative", 0);
        MustApply(workspace, new SaveCountdownChange(
            null,
            "来源保持-丁",
            dueUtc,
            Array.Empty<string>(),
            new ReminderRule[] { relative }));
        var noteAlpha = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "来源保持-甲");
        var noteGamma = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "来源保持-丙");
        var keyAlpha = new ActiveReminderOccurrenceKey(noteAlpha.ReminderRules.Single().Id, dueUtc);
        var keyCountdown = new ActiveReminderOccurrenceKey(relative.Id, dueUtc);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        var coordinator = new ReminderDeliveryCoordinator(
            workspace,
            logger,
            (kind, id) => main.OpenReminderSource(kind, id));
        try
        {
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var completedSegment = (ToggleButton)main.FindName("CompletedNotesSegment");
            completedSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindNoteDateDisclosure(main, noteGamma.Date, isCompleted: true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindNoteDateDisclosure(main, noteAlpha.Date, isCompleted: false)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                NoteStatusSectionIsShown(main, true) &&
                NoteStatusSectionIsHidden(main, false) &&
                !NoteDateSectionIsExpanded(main, noteGamma.Date, true) &&
                !NoteDateSectionIsExpanded(main, noteAlpha.Date, false) &&
                !main.CompletedNotesAreCollapsed,
                "初始分段与分组收放状态未建立。");

            clock.UtcNow = dueUtc;
            coordinator.Tick(dueUtc);
            WindowValidationInput.Pump(main.Dispatcher);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            Must(
                coordinator.ActiveEntryCount == 2,
                "便签甲与倒计时丁应同时到点入队。");

            Must(center.RequestOpenSource(keyAlpha), "未接受便签来源打开请求。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.CurrentWorkspace == WorkspaceKind.Notes &&
                NoteReminderHighlightVisible(main, noteAlpha) &&
                NoteStatusSectionIsShown(main, false) &&
                NoteStatusSectionIsHidden(main, true) &&
                NoteDateSectionIsExpanded(main, noteAlpha.Date, false) &&
                Keyboard.FocusedElement is Button { Tag: string focusedNote } &&
                string.Equals(focusedNote, noteAlpha.Id, StringComparison.OrdinalIgnoreCase),
                "打开来源应临时切换分段、揭示、高亮并聚焦来源便签。");

            clock.UtcNow = dueUtc.AddMinutes(1);
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                NoteStatusSectionIsShown(main, true) &&
                NoteStatusSectionIsHidden(main, false) &&
                !NoteDateSectionIsExpanded(main, noteAlpha.Date, false) &&
                !NoteDateSectionIsExpanded(main, noteGamma.Date, true) &&
                !NoteReminderHighlightVisible(main, noteAlpha),
                "重渲染后来源揭示应还原为用户保持的分段与分组收放状态。");

            Must(
                main.OpenReminderSource(ReminderSourceKind.Note, noteGamma.Id),
                "未能直接打开已完成便签来源。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                NoteReminderHighlightVisible(main, noteGamma) &&
                NoteStatusSectionIsShown(main, true) &&
                NoteStatusSectionIsHidden(main, false) &&
                NoteDateSectionIsExpanded(main, noteGamma.Date, true),
                "已完成便签来源应临时揭示且不改写持久分段状态。");

            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                NoteStatusSectionIsShown(main, true) &&
                !NoteDateSectionIsExpanded(main, noteGamma.Date, true) &&
                !NoteReminderHighlightVisible(main, noteGamma),
                "重渲染后已完成日期分组应恢复收起。");

            Must(center.RequestOpenSource(keyCountdown), "未接受倒计时来源打开请求。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.CurrentWorkspace == WorkspaceKind.Countdowns &&
                NoteStatusSectionIsHidden(main, false),
                "倒计时来源路径不应影响便签分段状态。");
            return true;
        }
        finally
        {
            coordinator.Dispose();
            if (main.IsVisible)
            {
                main.Close();
            }

            foreach (var window in Application.Current.Windows
                         .OfType<ReminderCenterWindow>()
                         .ToArray())
            {
                window.ForceClose();
            }

            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static Button FindReminderEntryButton(
        ReminderCenterWindow center,
        ActiveReminderOccurrenceKey key,
        string automationNamePrefix)
    {
        foreach (var row in VisualDescendants<Border>(center))
        {
            if (row.Tag is not ActiveReminderOccurrenceKey rowKey || rowKey != key)
            {
                continue;
            }

            foreach (var button in VisualDescendants<Button>(row))
            {
                if (AutomationProperties.GetName(button)?.StartsWith(
                        automationNamePrefix,
                        StringComparison.Ordinal) == true)
                {
                    return button;
                }
            }
        }

        throw new InvalidOperationException($"提醒中心缺少条目 {key.RuleId} 的 {automationNamePrefix} 按钮。");
    }

    private static T FindReminderEntryControl<T>(
        ReminderCenterWindow center,
        ActiveReminderOccurrenceKey key,
        string automationNamePrefix)
        where T : FrameworkElement
    {
        foreach (var row in VisualDescendants<Border>(center))
        {
            if (row.Tag is not ActiveReminderOccurrenceKey rowKey || rowKey != key)
            {
                continue;
            }

            foreach (var control in VisualDescendants<T>(row))
            {
                if (AutomationProperties.GetName(control)?.StartsWith(
                        automationNamePrefix,
                        StringComparison.Ordinal) == true)
                {
                    return control;
                }
            }
        }

        throw new InvalidOperationException($"提醒中心缺少条目 {key.RuleId} 的 {automationNamePrefix} 控件。");
    }

    private static Button FindNoteDateDisclosure(
        MainWindow main,
        string? noteDate,
        bool isCompleted)
    {
        var status = isCompleted ? "已完成" : "未完成";
        foreach (var button in VisualDescendants<Button>(main))
        {
            if (button.Uid == "NoteDateDisclosure" &&
                button.Tag is string tag &&
                string.Equals(tag, noteDate ?? string.Empty, StringComparison.Ordinal) &&
                AutomationProperties.GetName(button)?.Contains(
                    $"{status}便签",
                    StringComparison.Ordinal) == true)
            {
                return button;
            }
        }

        throw new InvalidOperationException($"找不到 {noteDate} 的{status}便签日期分组按钮。");
    }

    private static bool NoteDateSectionIsExpanded(MainWindow main, string? noteDate, bool isCompleted) =>
        AutomationProperties.GetName(FindNoteDateDisclosure(main, noteDate, isCompleted))
            ?.Contains("当前展开", StringComparison.Ordinal) == true;

    private static bool NoteStatusSectionIsShown(MainWindow main, bool isCompleted)
    {
        var rows = (StackPanel)main.FindName(isCompleted ? "CompletedNoteRows" : "ActiveNoteRows");
        var segment = (ToggleButton)main.FindName(isCompleted ? "CompletedNotesSegment" : "ActiveNotesSegment");
        return rows.Visibility == Visibility.Visible && segment.IsChecked == true;
    }

    private static bool NoteStatusSectionIsHidden(MainWindow main, bool isCompleted)
    {
        var rows = (StackPanel)main.FindName(isCompleted ? "CompletedNoteRows" : "ActiveNoteRows");
        var segment = (ToggleButton)main.FindName(isCompleted ? "CompletedNotesSegment" : "ActiveNotesSegment");
        return rows.Visibility == Visibility.Collapsed && segment.IsChecked != true;
    }

    private static bool NoteReminderHighlightVisible(MainWindow main, NoteRecord note) =>
        VisualDescendants<Border>(main).Any(border =>
            string.Equals(
                AutomationProperties.GetName(border),
                $"当前提醒来源便签：{note.Text}；未选择",
                StringComparison.Ordinal));
}
