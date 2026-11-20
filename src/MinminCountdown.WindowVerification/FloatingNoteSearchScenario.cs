using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #315：从真实入口观察展开后的焦点与连续触发；重入探针只排在 Dispatcher 输入队列，不读取产品实现。
internal static class FloatingNoteSearchScenario
{
    private const string SearchTerm = "布局";
    private const string SharedFilterTerm = "管理窗共享词";
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static void Run()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-note-search-{Guid.NewGuid():N}.settings.json");
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
                new SearchManagerHost(),
                AppContext.BaseDirectory);
            Show(floating);
            SaveNote(workspace, "布局后的搜索便签", floating.Dispatcher);

            var search = Required<TextBox>(floating, "FloatingNoteSearch");
            var searchSlot = Required<Grid>(floating, "FloatingNoteSearchSlot");
            var inputRow = Required<Grid>(floating, "FloatingNoteSearchInputRow");
            var hint = Required<Grid>(floating, "FloatingNoteSearchHint");
            var searchButton = Required<Button>(floating, "FloatingSearchNotes");
            var hintTerm = Required<Button>(floating, "FloatingNoteSearchHintTerm");
            var hintClear = Required<Button>(floating, "FloatingNoteSearchHintClear");
            var notesButton = Required<ToggleButton>(floating, "FloatingNotesButton");
            var countdownsButton = Required<ToggleButton>(floating, "FloatingCountdownsButton");
            var failures = new List<string>();

            Check(failures, "无词收起时不显示搜索槽", searchSlot.Visibility == Visibility.Collapsed);
            Check(failures, "无词收起时不显示输入行", inputRow.Visibility == Visibility.Collapsed);
            Check(failures, "无词收起时不显示提示条", hint.Visibility == Visibility.Collapsed);
            Check(failures, "无词收起时搜索框不在 Tab 顺序内", !search.IsTabStop);

            ObserveEntry(
                floating,
                search,
                inputRow,
                notesButton,
                "Ctrl+F",
                () => WindowValidationInput.RaisePreviewKey(floating, Key.F, ModifierKeys.Control),
                failures);

            search.Text = SearchTerm;
            Settle(floating);
            Check(
                failures,
                "搜索词写入共享工作区筛选",
                string.Equals(workspace.Snapshot.Filter.NoteSearch, SearchTerm, StringComparison.Ordinal));
            Check(
                failures,
                "Esc 入口被处理",
                WindowValidationInput.RaisePreviewKey(floating, Key.Escape));
            Settle(floating);
            CheckCleared(failures, workspace, search, searchSlot, inputRow, hint);

            ObserveEntry(
                floating,
                search,
                inputRow,
                notesButton,
                "搜索按钮",
                () => RaiseClick(searchButton),
                failures);

            search.Text = SearchTerm;
            Settle(floating);
            Check(
                failures,
                "按钮入口仍写入共享筛选",
                string.Equals(workspace.Snapshot.Filter.NoteSearch, SearchTerm, StringComparison.Ordinal));
            Check(failures, "再次点击搜索按钮的收起入口可用", RaiseClick(searchButton));
            Settle(floating);
            CheckCollapsedWithTerm(failures, workspace, search, searchSlot, inputRow, hint, hintTerm, SearchTerm);

            ObserveEntry(
                floating,
                search,
                inputRow,
                notesButton,
                "提示条词钮",
                () => RaiseClick(hintTerm),
                failures);
            Check(failures, "提示条词钮重新展开时保留搜索词", string.Equals(search.Text, SearchTerm, StringComparison.Ordinal));
            Check(failures, "展开态隐藏提示条", hint.Visibility == Visibility.Collapsed);

            // 自动化 Invoke 路径同样支持收起与提示条重展开并聚焦。
            Check(failures, "搜索按钮 Invoke 收起入口可用", WindowValidationInput.InvokeButton(searchButton));
            Settle(floating);
            CheckCollapsedWithTerm(failures, workspace, search, searchSlot, inputRow, hint, hintTerm, SearchTerm);
            Check(failures, "提示条词钮 Invoke 展开入口可用", WindowValidationInput.InvokeButton(hintTerm));
            Settle(floating);
            Check(
                failures,
                "提示条词钮 Invoke 展开后聚焦搜索输入框",
                inputRow.Visibility == Visibility.Visible &&
                search.IsVisible &&
                ReferenceEquals(Keyboard.FocusedElement, search));

            // 提示条清除钮收起并清空共享搜索词。
            Check(failures, "再次收起以检查提示条清除钮", RaiseClick(searchButton));
            Settle(floating);
            Check(failures, "提示条清除钮可点击", RaiseClick(hintClear));
            Settle(floating);
            CheckCleared(failures, workspace, search, searchSlot, inputRow, hint);

            // 模拟管理窗口写入共享筛选词，悬浮窗收起态同步显示提示条，Esc 清空并收起。
            workspace.ApplyFilter(workspace.Snapshot.Filter with { NoteSearch = SharedFilterTerm });
            Settle(floating);
            CheckCollapsedWithTerm(failures, workspace, search, searchSlot, inputRow, hint, hintTerm, SharedFilterTerm);
            Check(
                failures,
                "提示条状态按 Esc 清空并收起",
                WindowValidationInput.RaisePreviewKey(floating, Key.Escape));
            Settle(floating);
            CheckCleared(failures, workspace, search, searchSlot, inputRow, hint);

            // 连续冲突触发（布局完成前）：展开后立即 Esc，不得残余焦点或展开态。
            Check(failures, "连续触发 Ctrl+F 被处理", WindowValidationInput.RaisePreviewKey(floating, Key.F, ModifierKeys.Control));
            Check(failures, "连续触发立即 Esc 被处理", WindowValidationInput.RaisePreviewKey(floating, Key.Escape));
            Settle(floating);
            CheckCleared(failures, workspace, search, searchSlot, inputRow, hint);
            Check(failures, "展开后立即 Esc 不把焦点留在收起的搜索框", !ReferenceEquals(Keyboard.FocusedElement, search));

            // 连续点击搜索按钮（展开 -> 收起），最终保持收起且不聚焦搜索框。
            RaiseClick(searchButton);
            RaiseClick(searchButton);
            Settle(floating);
            CheckCleared(failures, workspace, search, searchSlot, inputRow, hint);
            Check(failures, "展开后立即收起不把焦点留在搜索框", !ReferenceEquals(Keyboard.FocusedElement, search));

            // 连续三次点击搜索按钮（展开 -> 收起 -> 展开），最终展开并聚焦搜索框。
            RaiseClick(searchButton);
            RaiseClick(searchButton);
            RaiseClick(searchButton);
            Settle(floating);
            Check(
                failures,
                "搜索按钮连续三次触发最终展开并聚焦",
                inputRow.Visibility == Visibility.Visible &&
                search.IsVisible &&
                search.IsTabStop &&
                ReferenceEquals(Keyboard.FocusedElement, search));

            // 展开后在布局前切回倒计时工作区，搜索槽收起且焦点不落回搜索框。
            Check(failures, "复位后再次触发 Ctrl+F", WindowValidationInput.RaisePreviewKey(floating, Key.Escape));
            Settle(floating);
            Check(failures, "切工作区前展开搜索", WindowValidationInput.RaisePreviewKey(floating, Key.F, ModifierKeys.Control));
            RaiseClick(countdownsButton);
            Settle(floating);
            Check(
                failures,
                "展开后立即切回倒计时工作区不出现状态错乱",
                countdownsButton.IsChecked == true &&
                searchSlot.Visibility == Visibility.Collapsed &&
                inputRow.Visibility == Visibility.Collapsed &&
                !search.IsTabStop &&
                !ReferenceEquals(Keyboard.FocusedElement, search));

            // 从倒计时工作区连续按两次 Ctrl+F，切到便签并聚焦搜索框。
            Check(failures, "倒计时页首按 Ctrl+F 被处理", WindowValidationInput.RaisePreviewKey(floating, Key.F, ModifierKeys.Control));
            Check(failures, "倒计时页连按第二次 Ctrl+F 被处理", WindowValidationInput.RaisePreviewKey(floating, Key.F, ModifierKeys.Control));
            Settle(floating);
            Check(
                failures,
                "倒计时页连按两次 Ctrl+F 切到便签并聚焦搜索框",
                notesButton.IsChecked == true &&
                inputRow.Visibility == Visibility.Visible &&
                search.IsVisible &&
                search.IsTabStop &&
                ReferenceEquals(Keyboard.FocusedElement, search));

            Check(
                failures,
                "最后一次 Esc 入口被处理",
                WindowValidationInput.RaisePreviewKey(floating, Key.Escape));
            Settle(floating);
            CheckCleared(failures, workspace, search, searchSlot, inputRow, hint);

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(string.Join("；", failures));
            }
        }
        finally
        {
            if (floating is not null)
            {
                floating.Close();
                WindowValidationInput.Pump(floating.Dispatcher);
            }
            DeleteSettingsFiles(settingsPath);
        }
    }

    private static void ObserveEntry(
        FloatingWindow floating,
        TextBox search,
        Grid inputRow,
        ToggleButton notesButton,
        string entryName,
        Func<bool> trigger,
        ICollection<string> failures)
    {
        var triggerReturned = false;
        var queuedTriggerRan = false;
        var queuedTriggerAccepted = false;
        var reentered = false;
        floating.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            queuedTriggerRan = true;
            reentered = !triggerReturned;
            queuedTriggerAccepted = WindowValidationInput.RaisePreviewKey(
                floating,
                Key.F,
                ModifierKeys.Control);
        }));

        var triggerAccepted = trigger();
        triggerReturned = true;
        Settle(floating);

        Check(failures, $"{entryName} 入口被处理", triggerAccepted);
        Check(failures, $"{entryName} 的连续触发已执行", queuedTriggerRan && queuedTriggerAccepted);
        Check(failures, $"{entryName} 返回前没有重入", !reentered);
        Check(failures, $"{entryName} 切到便签工作区", notesButton.IsChecked == true);
        Check(failures, $"{entryName} 展开输入行", inputRow.Visibility == Visibility.Visible && search.IsVisible && search.IsTabStop);
        Check(failures, $"{entryName} 展开后聚焦搜索输入框", ReferenceEquals(Keyboard.FocusedElement, search));
    }

    private static bool RaiseClick(ButtonBase button)
    {
        if (!button.IsVisible || !button.IsEnabled)
        {
            return false;
        }

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return true;
    }

    private static void CheckCollapsedWithTerm(
        ICollection<string> failures,
        DesktopWorkspace workspace,
        TextBox search,
        Grid searchSlot,
        Grid inputRow,
        Grid hint,
        Button hintTerm,
        string expectedTerm)
    {
        Check(failures, "有词收起时显示搜索槽", searchSlot.Visibility == Visibility.Visible);
        Check(failures, "有词收起时显示提示条", hint.Visibility == Visibility.Visible);
        Check(failures, "有词收起时隐藏输入行", inputRow.Visibility == Visibility.Collapsed);
        Check(failures, "有词收起时搜索框移出 Tab 顺序且释放焦点", !search.IsTabStop && !search.IsKeyboardFocusWithin);
        Check(
            failures,
            "提示条保留搜索词和可访问名",
            string.Equals(hintTerm.Content as string, $"搜索:{expectedTerm}", StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetName(hintTerm), $"展开搜索：{expectedTerm}", StringComparison.Ordinal) &&
            string.Equals(workspace.Snapshot.Filter.NoteSearch, expectedTerm, StringComparison.Ordinal));
    }

    private static void CheckCleared(
        ICollection<string> failures,
        DesktopWorkspace workspace,
        TextBox search,
        Grid searchSlot,
        Grid inputRow,
        Grid hint)
    {
        Check(failures, "清空搜索输入框", search.Text.Length == 0);
        Check(failures, "清空共享筛选", string.IsNullOrWhiteSpace(workspace.Snapshot.Filter.NoteSearch));
        Check(failures, "收起搜索槽", searchSlot.Visibility == Visibility.Collapsed);
        Check(failures, "收起输入行", inputRow.Visibility == Visibility.Collapsed);
        Check(failures, "收起提示条", hint.Visibility == Visibility.Collapsed);
        Check(failures, "收起后搜索框不在 Tab 顺序内", !search.IsTabStop);
    }

    private static void Settle(FloatingWindow floating)
    {
        WindowValidationInput.Pump(floating.Dispatcher);
        floating.UpdateLayout();
        WindowValidationInput.Pump(floating.Dispatcher);
    }

    private static void Check(ICollection<string> failures, string description, bool passed)
    {
        if (!passed)
        {
            failures.Add(description);
        }
    }

    private static void Show(FloatingWindow floating)
    {
        floating.ShowInTaskbar = false;
        floating.ShowActivated = false;
        floating.WindowStartupLocation = WindowStartupLocation.Manual;
        floating.Left = -20_000;
        floating.Top = -20_000;
        floating.Opacity = 0;
        floating.Topmost = false;
        var contentRendered = false;
        EventHandler rendered = (_, _) => contentRendered = true;
        floating.ContentRendered += rendered;
        try
        {
            floating.Show();
            var firstFrame = WindowValidationInput.PumpRenderedFrames(
                floating.Dispatcher,
                2000,
                () => contentRendered);
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.UpdateLayout();
            floating.Activate();
            WindowValidationInput.Pump(floating.Dispatcher);
            if (!contentRendered || !firstFrame.Completed)
            {
                throw new InvalidOperationException("悬浮搜索场景未完成真实首帧。");
            }
        }
        finally
        {
            floating.ContentRendered -= rendered;
        }
    }

    private static T Required<T>(Window window, string automationId) where T : FrameworkElement
    {
        var matches = VisualDescendants<T>(window)
            .Where(element => string.Equals(ReadElementId(element), automationId, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException($"悬浮搜索控件定位失败：{automationId}，matches={matches.Length}。");
    }

    private static string ReadElementId(FrameworkElement element)
    {
        var explicitId = AutomationProperties.GetAutomationId(element);
        if (!string.IsNullOrEmpty(explicitId))
        {
            return explicitId;
        }

        var peerId = UIElementAutomationPeer.CreatePeerForElement(element)?.GetAutomationId();
        return !string.IsNullOrEmpty(peerId)
            ? peerId
            : element.Name;
    }

    private sealed class SearchManagerHost : IManagerWorkspaceHost
    {
        public void Reveal(WorkspaceKind workspace)
        {
        }

        public void RevealPreferences(WorkspaceKind workspace)
        {
        }
    }
}
