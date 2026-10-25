using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    public static UiQualityGateResults EvaluateUiQualityGates(UiQualityGateInputs input) =>
        new(
            input.MainReferenceDecorationScaling,
            input.MainReferenceDecorationLayerOrder,
            input.MainWorkspaceResourcesFollowTheme,
            input.MainCountdownCardSampleCount > 0 &&
                input.MainCountdownCardResourcesFollowTheme,
            input.MainNoteCardSampleCount > 0 &&
                input.MainNoteCardResourcesFollowTheme,
            input.FloatingContentSafeArea,
            input.FloatingTitleUsesSemiBoldWeight,
            input.FloatingNoteInputWraps,
            input.FloatingNoteInputScrolls);

    public static bool RunUiQualityGateIsolationBehavior()
    {
        var passing = new UiQualityGateInputs(
            true,
            true,
            true,
            1,
            true,
            1,
            true,
            true,
            true,
            true,
            true);

        VerifyOnlyTargetFails(
            passing with { MainReferenceDecorationScaling = false },
            nameof(UiQualityGateResults.MainReferenceDecorationScaling));
        VerifyOnlyTargetFails(
            passing with { MainReferenceDecorationLayerOrder = false },
            nameof(UiQualityGateResults.MainReferenceDecorationLayerOrder));
        VerifyOnlyTargetFails(
            passing with { MainWorkspaceResourcesFollowTheme = false },
            nameof(UiQualityGateResults.MainWorkspaceResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { MainCountdownCardSampleCount = 0 },
            nameof(UiQualityGateResults.MainCountdownCardResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { MainCountdownCardResourcesFollowTheme = false },
            nameof(UiQualityGateResults.MainCountdownCardResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { MainNoteCardSampleCount = 0 },
            nameof(UiQualityGateResults.MainNoteCardResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { MainNoteCardResourcesFollowTheme = false },
            nameof(UiQualityGateResults.MainNoteCardResourcesFollowTheme));
        VerifyOnlyTargetFails(
            passing with { FloatingContentSafeArea = false },
            nameof(UiQualityGateResults.FloatingContentSafeArea));
        VerifyOnlyTargetFails(
            passing with { FloatingTitleUsesSemiBoldWeight = false },
            nameof(UiQualityGateResults.FloatingTitleUsesSemiBoldWeight));
        VerifyOnlyTargetFails(
            passing with { FloatingNoteInputWraps = false },
            nameof(UiQualityGateResults.FloatingNoteInputWraps));
        VerifyOnlyTargetFails(
            passing with { FloatingNoteInputScrolls = false },
            nameof(UiQualityGateResults.FloatingNoteInputScrolls));

        return true;
    }

    public static CountdownEmptyStateGateResults EvaluateCountdownEmptyStateGates(
        CountdownEmptyStateGateInputs input) =>
        new(
            input.EmptyStatesDistinguished,
            input.PointerResetRestoresResults,
            input.KeyboardResetRestoresResults,
            input.DayThemeTextContrast);

    public static bool RunCountdownEmptyStateGateIsolationBehavior()
    {
        var passing = new CountdownEmptyStateGateInputs(true, true, true, true);
        Must(EvaluateCountdownEmptyStateGates(passing).AllPassed,
            "倒计时空态全通过夹具被错误拒绝。");
        VerifyOnlyCountdownEmptyStateGateFails(
            passing with { EmptyStatesDistinguished = false },
            nameof(CountdownEmptyStateGateResults.EmptyStatesDistinguished));
        VerifyOnlyCountdownEmptyStateGateFails(
            passing with { PointerResetRestoresResults = false },
            nameof(CountdownEmptyStateGateResults.PointerResetRestoresResults));
        VerifyOnlyCountdownEmptyStateGateFails(
            passing with { KeyboardResetRestoresResults = false },
            nameof(CountdownEmptyStateGateResults.KeyboardResetRestoresResults));
        VerifyOnlyCountdownEmptyStateGateFails(
            passing with { DayThemeTextContrast = false },
            nameof(CountdownEmptyStateGateResults.DayThemeTextContrast));
        return true;
    }

    public static NoteVisualTokenGateResults EvaluateNoteVisualTokenGates(
        NoteVisualTokenGateInputs input) =>
        new(
            input.CompactInputPaddingAligned,
            input.BadgeTokensFollowState,
            input.EmptyStateTextContrast);

    public static bool RunNoteVisualTokenGateIsolationBehavior()
    {
        var passing = new NoteVisualTokenGateInputs(true, true, true);
        Must(EvaluateNoteVisualTokenGates(passing).AllPassed,
            "主窗口便签视觉合同全通过夹具被错误拒绝。");
        VerifyOnlyNoteVisualTokenGateFails(
            passing with { CompactInputPaddingAligned = false },
            nameof(NoteVisualTokenGateResults.CompactInputPaddingAligned));
        VerifyOnlyNoteVisualTokenGateFails(
            passing with { BadgeTokensFollowState = false },
            nameof(NoteVisualTokenGateResults.BadgeTokensFollowState));
        VerifyOnlyNoteVisualTokenGateFails(
            passing with { EmptyStateTextContrast = false },
            nameof(NoteVisualTokenGateResults.EmptyStateTextContrast));
        return true;
    }

    public static bool RunFloatingEmptyStateTextIsolationBehavior()
    {
        VerifyFloatingWorkspaceOrderAndFilterAction();
        var clearFilter = DesktopWorkspaceFilter.Empty;
        Must(
            string.Equals(
                FloatingCountdownProjection.CountdownEmptyStateText(clearFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingWindow.NoteEmptyStateText(clearFilter),
                "还没有待办便签。",
                StringComparison.Ordinal),
            "悬浮窗无筛选空态文案漂移。");

        var taggedFilter = clearFilter with
        {
            SelectedTagIds = Array.AsReadOnly(new[] { "floating-empty-gate-tag" })
        };
        var untaggedFilter = clearFilter with { UntaggedOnly = true };
        var countdownStatusFilter = clearFilter with { CountdownStatus = CountdownStatusFilter.Expired };
        var noteStatusFilter = clearFilter with { NoteStatus = NoteStatusFilter.Completed };
        var noteDateFilter = clearFilter with { NoteDate = "2032-02-03" };
        var undatedNotesFilter = clearFilter with { UndatedNotesOnly = true };
        foreach (var countdownConstrained in new[] { taggedFilter, untaggedFilter, countdownStatusFilter })
        {
            Must(
                string.Equals(
                    FloatingCountdownProjection.CountdownEmptyStateText(countdownConstrained),
                    "没有匹配的倒计时。",
                    StringComparison.Ordinal),
                "悬浮窗倒计时筛选无匹配空态未使用匹配口径文案。");
        }

        foreach (var noteConstrained in new[]
                 {
                     taggedFilter, untaggedFilter, noteStatusFilter, noteDateFilter, undatedNotesFilter
                 })
        {
            Must(
                string.Equals(
                    FloatingWindow.NoteEmptyStateText(noteConstrained),
                    "没有匹配的便签。",
                    StringComparison.Ordinal),
                "悬浮窗便签筛选无匹配空态未使用匹配口径文案。");
        }

        // 搜索词空态由 BuildNoteSearchEmptyState 单独接管，仅搜索词不触发筛选无匹配文案。
        var searchOnlyFilter = clearFilter with { NoteSearch = "悬浮空态搜索词" };
        Must(
            string.Equals(
                FloatingCountdownProjection.CountdownEmptyStateText(searchOnlyFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingWindow.NoteEmptyStateText(searchOnlyFilter),
                "还没有待办便签。",
                StringComparison.Ordinal),
            "仅搜索词不应触发悬浮窗筛选无匹配文案。");

        // 便签侧约束不影响倒计时文案，倒计时侧约束不影响便签文案。
        Must(
            string.Equals(
                FloatingCountdownProjection.CountdownEmptyStateText(noteStatusFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingCountdownProjection.CountdownEmptyStateText(noteDateFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingCountdownProjection.CountdownEmptyStateText(undatedNotesFilter),
                "没有未到期的倒计时。",
                StringComparison.Ordinal) &&
            string.Equals(
                FloatingWindow.NoteEmptyStateText(countdownStatusFilter),
                "还没有待办便签。",
                StringComparison.Ordinal),
            "悬浮窗两工作区筛选约束不应串扰空态文案。");
        return true;
    }

    private static void VerifyFloatingWorkspaceOrderAndFilterAction()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"MinminCountdown-workspace-order-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        FloatingWindow? floating = null;
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        try
        {
            var notes = new[]
            {
                new NoteRecord("older", "较早日期", "2034-05-05", false, [], NoReminder.Instance),
                new NoteRecord("same-first", "A 先添加", "2034-05-06", false, [], NoReminder.Instance),
                new NoteRecord("undated", "无日期", null, false, [], NoReminder.Instance),
                new NoteRecord("same-last", "Z 后添加", "2034-05-06", false, [], NoReminder.Instance),
                new NoteRecord("done", "已完成", "2034-05-07", true, [], NoReminder.Instance)
            };
            var clock = new WorkspaceValidationClock(new DateTimeOffset(2034, 5, 6, 1, 2, 3, TimeSpan.Zero));
            var store = new SettingsStore(clock: clock);
            var countdown = new CountdownRecord("restored-countdown", "清除后恢复的倒计时", clock.UtcNow.AddDays(2), ["present-tag"]);
            store.Save(settingsPath, new CountdownSettings([countdown], notes,
                [new TagRecord("present-tag", "已有标签", "#F4B6A6")], CountdownSettings.Empty.Preferences));
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            main = new MainWindow(workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory);
            floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            PrepareOffscreenValidationWindow(floating);
            main.Show();
            floating.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            main.UpdateLayout();
            floating.UpdateLayout();
            var expectedActive = new[] { "same-last", "same-first", "older", "undated" };
            Must(VisualDescendants<CheckBox>(floating).Where(box => box.Tag is string)
                    .Select(box => (string)box.Tag).SequenceEqual(expectedActive, StringComparer.Ordinal),
                "悬浮便签未按日期倒序、同日添加倒序、无日期最后显示。");
            Must(VisualDescendants<CheckBox>(main).Where(box => box.Tag is string id && notes.Any(note => note.Id == id))
                    .Select(box => (string)box.Tag).SequenceEqual(expectedActive.Append("done"), StringComparer.Ordinal),
                "管理便签未遵循同一独立领域顺序。");

            var savedBytes = File.ReadAllBytes(settingsPath);
            foreach (var filter in new[]
            {
                DesktopWorkspaceFilter.Empty with
                {
                    SelectedTagIds = new[] { "missing-tag" }, MatchMode = TagMatchMode.All,
                    NoteStatus = NoteStatusFilter.Completed, NoteSearch = "保留搜索", NoteDate = "2034-05-06"
                },
                DesktopWorkspaceFilter.Empty with
                {
                    UntaggedOnly = true, MatchMode = TagMatchMode.All,
                    NoteStatus = NoteStatusFilter.Active, NoteSearch = "保留无日期搜索", UndatedNotesOnly = true
                },
                DesktopWorkspaceFilter.Empty with
                {
                    CountdownStatus = CountdownStatusFilter.Expired,
                    NoteStatus = NoteStatusFilter.Completed, NoteSearch = "保留状态搜索", NoteDate = "2034-05-05"
                }
            })
            {
                workspace.ApplyFilter(filter);
                floating.ShowWorkspace(WorkspaceKind.Countdowns);
                WindowValidationInput.Pump(floating.Dispatcher);
                floating.UpdateLayout();
                Must(floating.FloatingCountdownCount == 0, "倒计时清除筛选夹具未进入空态。");
                var clear = VisualDescendants<Button>(floating).SingleOrDefault(button =>
                    button.IsVisible && AutomationProperties.GetName(button) == "清除倒计时筛选");
                Must(clear is not null && Equals(clear.Content, "清除筛选"), "倒计时筛选空态缺少当前可见的清除筛选动作。");
                clear!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                WindowValidationInput.Pump(floating.Dispatcher);
                floating.UpdateLayout();
                var after = workspace.Snapshot;
                Must(after.Filter.SelectedTagIds.Count == 0 && !after.Filter.UntaggedOnly &&
                        after.Filter.CountdownStatus == CountdownStatusFilter.All,
                    "倒计时清除筛选未清除标签、无标签或状态约束。");
                Must(after.Filter.NoteStatus == filter.NoteStatus && after.Filter.NoteSearch == filter.NoteSearch &&
                        after.Filter.NoteDate == filter.NoteDate && after.Filter.UndatedNotesOnly == filter.UndatedNotesOnly &&
                        after.Filter.MatchMode == filter.MatchMode,
                    "倒计时清除筛选损坏便签专属条件或标签匹配模式。");
                Must(after.VisibleCountdowns.Select(record => record.Id).SequenceEqual(new[] { "restored-countdown" }) &&
                        floating.FloatingCountdownCount == 1 && VisualDescendants<TextBlock>(floating).Any(text => text.Text == countdown.Name),
                    "点击倒计时清除筛选后没有恢复原记录与真实行。");
                Must(File.ReadAllBytes(settingsPath).SequenceEqual(savedBytes), "清除筛选不应写入持久化设置。");
            }
            Must(workspace.Apply(new DeleteCountdownChange(countdown.Id)).Succeeded, "倒计时空态夹具删除失败。");
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { NoteStatus = NoteStatusFilter.Completed });
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.UpdateLayout();
            Must(floating.FloatingCountdownCount == 0 && !VisualDescendants<Button>(floating).Any(button =>
                    button.IsVisible && Equals(button.Content, "清除筛选")),
                "仅便签条件不应在倒计时空态提供清除筛选。");
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);
            Must((floating.FindName("FloatingTitle") as TextBlock)?.Text == "待办便签",
                "悬浮便签工作区标题仍暗示只展示今日记录。");
            foreach (var note in notes)
            {
                Must(workspace.Apply(new DeleteNoteChange(note.Id)).Succeeded, "便签默认空态夹具删除失败。");
            }
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.UpdateLayout();
            Must(VisualDescendants<TextBlock>(floating).Any(text => text.IsVisible && text.Text == "还没有待办便签。") &&
                    !VisualDescendants<Button>(floating).Any(button => button.IsVisible && Equals(button.Content, "清除筛选")),
                "无筛选便签空态应如实说明没有待办，并且不提供清除筛选。");
        }
        finally
        {
            floating?.Close();
            main?.Close();
            WindowValidationInput.Pump(Application.Current.Dispatcher);
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    public static MainNoteCompletionGateResults EvaluateMainNoteCompletionGates(
        MainNoteCompletionGateInputs input) =>
        new(
            input.DedicatedStyleApplied &&
            input.MainBoxAtLeast24Pixels &&
            input.MainTickScaled &&
            input.WarmTokensReused &&
            input.RoundedCornersPreserved &&
            input.CheckAndUncheckWork &&
            input.PersistenceFailureRollsBack &&
            input.AccessibleNamesUpdate,
            input.TagSelectorSizeUnchanged &&
            input.FloatingSizeUnchanged);

    public static bool RunMainNoteCompletionGateIsolationBehavior()
    {
        var passing = new MainNoteCompletionGateInputs(
            true, true, true, true, true, true, true, true, true, true);
        Must(EvaluateMainNoteCompletionGates(passing).AllPassed,
            "主窗口便签完成复选框全通过夹具被错误拒绝。");

        foreach (var input in new[]
        {
            passing with { DedicatedStyleApplied = false },
            passing with { MainBoxAtLeast24Pixels = false },
            passing with { MainTickScaled = false },
            passing with { WarmTokensReused = false },
            passing with { RoundedCornersPreserved = false },
            passing with { CheckAndUncheckWork = false },
            passing with { PersistenceFailureRollsBack = false },
            passing with { AccessibleNamesUpdate = false }
        })
        {
            VerifyOnlyMainNoteCompletionGateFails(
                input,
                nameof(MainNoteCompletionGateResults.MainTargetSizeAndBehavior));
        }

        foreach (var input in new[]
        {
            passing with { TagSelectorSizeUnchanged = false },
            passing with { FloatingSizeUnchanged = false }
        })
        {
            VerifyOnlyMainNoteCompletionGateFails(
                input,
                nameof(MainNoteCompletionGateResults.OtherUserSizesUnchanged));
        }

        return true;
    }

    internal const int AccessibleNameMaxLength = 512;

    internal static bool IsWellFormedAccessibleName(string name, string prefix)
    {
        return name.StartsWith(prefix, StringComparison.Ordinal) &&
               name.Length > prefix.Length &&
               name.IndexOf('\n') < 0 &&
               name.IndexOf('\r') < 0 &&
               name.Length <= AccessibleNameMaxLength;
    }

    public static bool RunAccessibleNameWhitespaceIsolationBehavior()
    {
        const string completePrefix = "完成便签 ";
        Must(
            !IsWellFormedAccessibleName("完成便签 第一行\n第二行", completePrefix),
            "含换行的可访问名称没有被弱约束拒绝。");
        Must(
            IsWellFormedAccessibleName("完成便签 验证完成交互", completePrefix),
            "无异常空白的可访问名称被弱约束误伤。");
        Must(
            IsWellFormedAccessibleName("知道了：验证完成交互", "知道了："),
            "全角冒号前缀的可访问名称被弱约束误伤。");

        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-accessible-name-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var preexistingWindows = Application.Current.Windows.Cast<Window>().ToHashSet();
        MainWindow? main = null;
        FloatingWindow? floating = null;
        ReminderCenterWindow? reminder = null;
        HolidayCalendar? calendar = null;
        try
        {
            const string noteId = "note-newline";
            const string countdownId = "countdown-newline";
            const string tagId = "tag-newline";
            const string noteText = "第一行\n第二行";
            const string countdownName = "主线\n倒计时";
            const string tagName = "标\n签";
            var settings = new CountdownSettings(
                new[]
                {
                    new CountdownRecord(
                        countdownId,
                        countdownName,
                        new DateTimeOffset(2031, 5, 1, 12, 0, 0, TimeSpan.Zero),
                        Array.Empty<string>())
                },
                new[]
                {
                    new NoteRecord(
                        noteId,
                        noteText,
                        "2031-04-05",
                        false,
                        new[] { tagId },
                        NoReminder.Instance)
                },
                new[] { new TagRecord(tagId, tagName, "#F4B6A6") },
                CountdownSettings.Empty.Preferences);
            new SettingsStore().Save(settingsPath, settings);
            var workspace = DesktopWorkspace.Load(settingsPath);
            calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            floating = new FloatingWindow(
                workspace,
                calendar,
                main,
                AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(floating);
            reminder = new ReminderCenterWindow();
            PrepareOffscreenValidationWindow(reminder);

            main.Show();
            floating.Show();
            reminder.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            main.UpdateLayout();
            floating.UpdateLayout();

            var noteTags = main.FindName("NoteTags") as TagSelector
                ?? throw new InvalidOperationException("主窗口缺少 NoteTags。");
            noteTags.SetTags(workspace.Snapshot.Settings.Tags);
            noteTags.SetSelection(new[] { tagId });
            reminder.AddEntry(new ReminderCenterItem(
                new ActiveReminderOccurrenceKey("rule-newline", DateTimeOffset.UtcNow),
                workspace.Snapshot.Settings.Notes.Single(),
                0,
                0));
            WindowValidationInput.Pump(main.Dispatcher);
            WindowValidationInput.Pump(reminder.Dispatcher);

            var mainCompletion = AutomationProperties.GetName(
                VisualDescendants<CheckBox>(main).Single(box =>
                    string.Equals(box.Tag as string, noteId, StringComparison.OrdinalIgnoreCase)));
            var mainActions = VisualDescendants<Button>(main)
                .Where(button =>
                    button.Content is "编辑" or "删除" &&
                    string.Equals(button.Tag as string, noteId, StringComparison.OrdinalIgnoreCase))
                .Select(AutomationProperties.GetName)
                .ToArray();
            var floatingCompletion = AutomationProperties.GetName(
                VisualDescendants<CheckBox>(floating).Single(box =>
                    string.Equals(box.Tag as string, noteId, StringComparison.OrdinalIgnoreCase)));
            floating.ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.UpdateLayout();
            var floatingCountdownNames = VisualDescendants<FrameworkElement>(floating)
                .Select(AutomationProperties.GetName)
                .Where(name => name.Contains("倒计时", StringComparison.Ordinal) &&
                               name.Contains("主线", StringComparison.Ordinal))
                .ToArray();
            var reminderNames = VisualDescendants<FrameworkElement>(reminder)
                .Select(AutomationProperties.GetName)
                .Where(name => name.Contains("第一行", StringComparison.Ordinal))
                .ToArray();
            var selectedTagName = AutomationProperties.GetName(noteTags.PopupTrigger);

            Must(
                mainCompletion.Contains("第一行", StringComparison.Ordinal) &&
                mainCompletion.Contains("第二行", StringComparison.Ordinal),
                "主窗口完成名称没有嵌入 settings JSON 便签正文。");
            Must(mainActions.Length == 2, "主窗口编辑/删除按钮没有渲染。");
            Must(
                floatingCompletion.Contains("第一行", StringComparison.Ordinal),
                "悬浮窗完成名称没有嵌入 settings JSON 便签正文。");
            Must(floatingCountdownNames.Length > 0, "悬浮窗倒计时名称没有嵌入 settings JSON 倒计时名。");
            Must(reminderNames.Length > 0, "提醒中心名称没有嵌入 settings JSON 便签正文。");
            Must(
                selectedTagName.Contains("标", StringComparison.Ordinal) &&
                selectedTagName.Contains("签", StringComparison.Ordinal),
                "已选标签名称没有嵌入 settings JSON 标签名。");

            var names = new[] { mainCompletion, floatingCompletion, selectedTagName }
                .Concat(mainActions)
                .Concat(floatingCountdownNames)
                .Concat(reminderNames)
                .ToArray();
            return names.All(name => name.IndexOf('\n') < 0 && name.IndexOf('\r') < 0);
        }
        finally
        {
            foreach (var window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                if (preexistingWindows.Contains(window))
                {
                    continue;
                }

                if (window is ReminderCenterWindow reminderCenter)
                {
                    reminderCenter.ForceClose();
                }
                else
                {
                    window.Close();
                }
            }

            for (var attempt = 0; attempt < 8; attempt++)
            {
                WindowValidationInput.Pump(Application.Current.Dispatcher);
                if (Application.Current.Windows.Cast<Window>().All(window =>
                        preexistingWindows.Contains(window) || !window.IsVisible))
                {
                    break;
                }
            }

            var preexistingMain = Application.Current.Windows
                .OfType<MainWindow>()
                .FirstOrDefault(window => preexistingWindows.Contains(window) && window.IsVisible);
            if (preexistingMain is not null)
            {
                preexistingMain.Activate();
                WindowValidationInput.Pump(preexistingMain.Dispatcher);
            }

            calendar?.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    public static MainCountdownRemainingGateResults EvaluateMainCountdownRemainingGates(
        MainCountdownRemainingGateInputs input) =>
        new(
            input.ExpectedTexts.Count == 6 &&
                input.RenderedTexts.SequenceEqual(input.ExpectedTexts, StringComparer.Ordinal),
                input.HasExpectedSampleCount &&
                input.ColumnsAre300Pixels &&
                input.ActualColumnsAre300Pixels &&
                input.TextIsRightAligned &&
                input.TextIsSingleLineAndUntrimmed &&
                input.TextIsFullyVisible &&
                input.UsesTabularNumerals &&
                input.PrecisionSamplesAreSaved &&
                input.FontSizingMatches);

    public static bool RunMainCountdownRemainingGateIsolationBehavior()
    {
        var expected = new[]
        {
            "4 天",
            "4 天 13 小时",
            "4 天 13 小时 18 分钟",
            "4 天 13 小时 18 分钟 59 秒",
            "2000000 天 00 小时 00 分钟 00 秒",
            "999 天 13 小时 18 分钟 59 秒"
        };
        var passing = new MainCountdownRemainingGateInputs(
            expected,
            expected,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        var passingResult = EvaluateMainCountdownRemainingGates(passing);
        Must(passingResult.UnitSpacingMatches && passingResult.FixedColumnLayoutMatches,
            "主窗口倒计时剩余时间全通过夹具被错误拒绝。");
        VerifyOnlyMainCountdownRemainingGateFails(
            passing with { ColumnsAre300Pixels = false },
            nameof(MainCountdownRemainingGateResults.FixedColumnLayoutMatches));
        VerifyOnlyMainCountdownRemainingGateFails(
            passing with { ActualColumnsAre300Pixels = false },
            nameof(MainCountdownRemainingGateResults.FixedColumnLayoutMatches));
        VerifyOnlyMainCountdownRemainingGateFails(
            passing with { FontSizingMatches = false },
            nameof(MainCountdownRemainingGateResults.FixedColumnLayoutMatches));
        VerifyOnlyMainCountdownRemainingGateFails(
            passing with
            {
                RenderedTexts = expected.Select(text => text.Replace(" ", string.Empty, StringComparison.Ordinal)).ToArray()
            },
            nameof(MainCountdownRemainingGateResults.UnitSpacingMatches));
        return true;
    }

    // #148：倒计时可读性合同（徽章对比/字号/居中、到期文字、分格 2:3 与单位字下限、
    // 选中段对比度唯一门禁）——用会失败的反例保住门禁。
    public static CountdownReadabilityGateResults EvaluateCountdownReadabilityGates(
        CountdownReadabilityGateInputs input) =>
        new(
            input.BadgeUsesInkText && input.BadgeMeetsContrast && input.BadgeSizedAndCentered,
            input.ExpiredSecondaryUsesBodyColor && input.ExpiredPrimaryUsesNumberColor,
            input.BlocksVerticalRatioMatches && input.UnitFontSizeAtFloor,
            input.SegmentContrastGateOnly);

    public static bool RunCountdownReadabilityGateIsolationBehavior()
    {
        var passing = new CountdownReadabilityGateInputs(
            true, true, true, true, true, true, true, true);
        Must(EvaluateCountdownReadabilityGates(passing).AllPassed, "倒计时可读性全通过夹具被错误拒绝。");
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { BadgeUsesInkText = false },
            nameof(CountdownReadabilityGateResults.BadgeContractMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { BadgeMeetsContrast = false },
            nameof(CountdownReadabilityGateResults.BadgeContractMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { BadgeSizedAndCentered = false },
            nameof(CountdownReadabilityGateResults.BadgeContractMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { ExpiredSecondaryUsesBodyColor = false },
            nameof(CountdownReadabilityGateResults.ExpiredTextMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { ExpiredPrimaryUsesNumberColor = false },
            nameof(CountdownReadabilityGateResults.ExpiredTextMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { BlocksVerticalRatioMatches = false },
            nameof(CountdownReadabilityGateResults.BlocksGeometryMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { UnitFontSizeAtFloor = false },
            nameof(CountdownReadabilityGateResults.BlocksGeometryMatches));
        VerifyOnlyCountdownReadabilityGateFails(
            passing with { SegmentContrastGateOnly = false },
            nameof(CountdownReadabilityGateResults.SegmentContrastGateOnly));
        return true;
    }

    // #151：倒计时编辑合同（字段错误贴源+聚焦首个无效控件、错误清除、错误行不裁切、
    // 偏好错误贴字段、主次按钮角色、白纸输入面、独立选区高亮、状态条对比、单选圆点、排版 token）
    // ——用会失败的反例保住门禁。
    public static CountdownEditingGateResults EvaluateCountdownEditingGates(
        CountdownEditingGateInputs input) =>
        new(
            input.CountdownFieldErrorsAtSource && input.FocusFirstInvalidControl,
            input.CountdownFieldErrorsClearOnCorrection,
            input.SingleErrorRowNotClipped,
            input.PreferenceErrorsBelowFields && input.PreferenceErrorsClearOnCorrection,
            input.PrimarySecondaryRolesDistinct,
            input.InputSurfacesWhitePaper,
            input.SelectionHighlightDistinct,
            input.StatusBarSurfaceErrorContrast,
            input.ReminderRadioSelectedContrast,
            input.EditorTypographyTokenized);

    public static bool RunCountdownEditingGateIsolationBehavior()
    {
        var passing = new CountdownEditingGateInputs(
            true, true, true, true, true, true, true, true, true, true, true, true);
        Must(EvaluateCountdownEditingGates(passing).AllPassed, "倒计时编辑全通过夹具被错误拒绝。");
        VerifyOnlyCountdownEditingGateFails(
            passing with { CountdownFieldErrorsAtSource = false },
            nameof(CountdownEditingGateResults.FieldErrorsAtSourceWithFocus));
        VerifyOnlyCountdownEditingGateFails(
            passing with { FocusFirstInvalidControl = false },
            nameof(CountdownEditingGateResults.FieldErrorsAtSourceWithFocus));
        VerifyOnlyCountdownEditingGateFails(
            passing with { CountdownFieldErrorsClearOnCorrection = false },
            nameof(CountdownEditingGateResults.FieldErrorsClear));
        VerifyOnlyCountdownEditingGateFails(
            passing with { SingleErrorRowNotClipped = false },
            nameof(CountdownEditingGateResults.SingleErrorRowNotClipped));
        VerifyOnlyCountdownEditingGateFails(
            passing with { PreferenceErrorsBelowFields = false },
            nameof(CountdownEditingGateResults.PreferenceErrorsAtSource));
        VerifyOnlyCountdownEditingGateFails(
            passing with { PreferenceErrorsClearOnCorrection = false },
            nameof(CountdownEditingGateResults.PreferenceErrorsAtSource));
        VerifyOnlyCountdownEditingGateFails(
            passing with { PrimarySecondaryRolesDistinct = false },
            nameof(CountdownEditingGateResults.PrimarySecondaryRolesDistinct));
        VerifyOnlyCountdownEditingGateFails(
            passing with { InputSurfacesWhitePaper = false },
            nameof(CountdownEditingGateResults.InputSurfacesWhitePaper));
        VerifyOnlyCountdownEditingGateFails(
            passing with { SelectionHighlightDistinct = false },
            nameof(CountdownEditingGateResults.SelectionHighlightDistinct));
        VerifyOnlyCountdownEditingGateFails(
            passing with { StatusBarSurfaceErrorContrast = false },
            nameof(CountdownEditingGateResults.StatusBarErrorContrast));
        VerifyOnlyCountdownEditingGateFails(
            passing with { ReminderRadioSelectedContrast = false },
            nameof(CountdownEditingGateResults.ReminderRadioSelectedContrast));
        VerifyOnlyCountdownEditingGateFails(
            passing with { EditorTypographyTokenized = false },
            nameof(CountdownEditingGateResults.EditorTypographyTokenized));
        return true;
    }

    public static bool RunCountdownIncrementalRefreshGateIsolationBehavior()
    {
        var passing = new CountdownIncrementalRefreshGateResults(
            true, true, true, true, true, true, true, true, true, true);
        Must(passing.AllPassed, "倒计时增量刷新全通过夹具被错误拒绝。");
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { IdentityPreserved = false },
            nameof(CountdownIncrementalRefreshGateResults.IdentityPreserved));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { FocusPreserved = false },
            nameof(CountdownIncrementalRefreshGateResults.FocusPreserved));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { NonZeroScrollPreserved = false },
            nameof(CountdownIncrementalRefreshGateResults.NonZeroScrollPreserved));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { PointerStatePreserved = false },
            nameof(CountdownIncrementalRefreshGateResults.PointerStatePreserved));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { CollectionChangeRebuilt = false },
            nameof(CountdownIncrementalRefreshGateResults.CollectionChangeRebuilt));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { FilterChangeRebuilt = false },
            nameof(CountdownIncrementalRefreshGateResults.FilterChangeRebuilt));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { PrecisionChangeRebuilt = false },
            nameof(CountdownIncrementalRefreshGateResults.PrecisionChangeRebuilt));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { ExpiryChangeRebuilt = false },
            nameof(CountdownIncrementalRefreshGateResults.ExpiryChangeRebuilt));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { ExpiryBackgroundFlipped = false },
            nameof(CountdownIncrementalRefreshGateResults.ExpiryBackgroundFlipped));
        VerifyOnlyCountdownIncrementalGateFails(
            passing with { ExpiryRemainingTextFlipped = false },
            nameof(CountdownIncrementalRefreshGateResults.ExpiryRemainingTextFlipped));
        return true;
    }

    /// <summary>
    ///     #212：悬浮窗渲染签名的隔离反例——同输入同签名；标题/精度/筛选/错误/结构变化必须改签名；
    ///     剩余时间仅数值变化（位数不变或变化）不得改签名。
    /// </summary>
    public static bool RunFloatingRenderSignatureGateIsolationBehavior()
    {
        var filterFingerprint = "tag-a\u001eAny\u001eFalse\u001eAll\u001eAll\u001e\u001e\u001eFalse";
        var entries = new[]
        {
            "id-1\u001e报告截止\u001e639000000000000000\u001e0 天 0 小时 0 分钟 0 秒"
        };
        var baseline = FloatingWindow.BuildFloatingRenderSignature(
            WorkspaceKind.Countdowns,
            "Second",
            filterFingerprint,
            entries);
        Must(string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint,
                    entries),
                StringComparison.Ordinal),
            "渲染签名同输入必须逐字节稳定。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint,
                    new[] { "id-1\u001e改名\u001e639000000000000000\u001e0 天 0 小时 0 分钟 0 秒" }),
                StringComparison.Ordinal),
            "标题变化必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Minute",
                    filterFingerprint,
                    entries),
                StringComparison.Ordinal),
            "显示精度变化必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint + "\u001etag-b",
                    entries),
                StringComparison.Ordinal),
            "筛选指纹变化必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Notes,
                    "Second",
                    filterFingerprint,
                    entries),
                StringComparison.Ordinal),
            "工作区变化必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint,
                    new[] { "id-1\u001e报告截止\u001e639000000000000000\u001e0 天 0 小时 0 分钟" }),
                StringComparison.Ordinal),
            "剩余时间结构变化（单位序列变化）必须改变渲染签名。");
        Must(!string.Equals(
                baseline,
                FloatingWindow.BuildFloatingRenderSignature(
                    WorkspaceKind.Countdowns,
                    "Second",
                    filterFingerprint,
                    entries.Concat(new[] { "id-2\u001e新记录\u001e639000000000000000\u001e0 天" })),
                StringComparison.Ordinal),
            "记录序列变化必须改变渲染签名。");

        Must(string.Equals(
                FloatingCountdownProjection.NormalizeRemainingStructure("12 天 03 小时 7 分钟 09 秒"),
                FloatingCountdownProjection.NormalizeRemainingStructure("9 天 3 小时 40 分钟 8 秒"),
                StringComparison.Ordinal),
            "剩余时间仅数值变化（含位数变化）必须保持同一签名结构。");
        Must(!string.Equals(
                FloatingCountdownProjection.NormalizeRemainingStructure("2 天 0 小时"),
                FloatingCountdownProjection.NormalizeRemainingStructure("2 天 0 小时 0 分钟"),
                StringComparison.Ordinal),
            "剩余时间单位序列变化必须产生不同签名结构。");
        Must(string.Equals(
                FloatingCountdownProjection.NormalizeRemainingStructure("已到期"),
                "已到期",
                StringComparison.Ordinal),
            "到期回落文案不含数字，结构归一化必须保持原文。");
        return true;
    }

    public static ConfirmationDialogGateResults EvaluateConfirmationDialogGates(
        ConfirmationDialogGateInputs input) =>
        new(
            input.ButtonsVisible,
            input.BodyReachable,
            input.CancelIsSafeDefault,
            input.ProductionMatrixComplete,
            input.BodyEndReachable,
            input.CenteredOnOwner,
            input.EnterIsSafe,
            input.CancelAndEscapeAreSafe);

    public static bool RunConfirmationDialogGateIsolationBehavior()
    {
        var passing = new ConfirmationDialogGateInputs(
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);
        Must(EvaluateConfirmationDialogGates(passing).AllPassed,
            "删除确认对话框全通过夹具被错误拒绝。");
        VerifyOnlyConfirmationGateFails(passing with { ButtonsVisible = false }, nameof(ConfirmationDialogGateResults.ButtonsVisible));
        VerifyOnlyConfirmationGateFails(passing with { BodyReachable = false }, nameof(ConfirmationDialogGateResults.BodyReachable));
        VerifyOnlyConfirmationGateFails(passing with { CancelIsSafeDefault = false }, nameof(ConfirmationDialogGateResults.CancelIsSafeDefault));
        VerifyOnlyConfirmationGateFails(passing with { ProductionMatrixComplete = false }, nameof(ConfirmationDialogGateResults.ProductionMatrixComplete));
        VerifyOnlyConfirmationGateFails(passing with { BodyEndReachable = false }, nameof(ConfirmationDialogGateResults.BodyEndReachable));
        VerifyOnlyConfirmationGateFails(passing with { CenteredOnOwner = false }, nameof(ConfirmationDialogGateResults.CenteredOnOwner));
        VerifyOnlyConfirmationGateFails(passing with { EnterIsSafe = false }, nameof(ConfirmationDialogGateResults.EnterIsSafe));
        VerifyOnlyConfirmationGateFails(passing with { CancelAndEscapeAreSafe = false }, nameof(ConfirmationDialogGateResults.CancelAndEscapeAreSafe));
        return true;
    }

    public static bool RunSecondaryWindowTypographyGateIsolationBehavior()
    {
        var passing = new SecondaryWindowTypographyGateInputs(true, true, true);
        Must(
            EvaluateSecondaryWindowTypographyGates(passing).AllPassed,
            "次级窗口字号全通过夹具被错误拒绝。");
        VerifyOnlySecondaryWindowTypographyGateFails(
            passing with { RenderedScaleMatches = false },
            nameof(SecondaryWindowTypographyGateResults.RenderedScaleMatches));
        VerifyOnlySecondaryWindowTypographyGateFails(
            passing with { ContentRootsDeclareFontSize = false },
            nameof(SecondaryWindowTypographyGateResults.ContentRootsDeclareFontSize));
        VerifyOnlySecondaryWindowTypographyGateFails(
            passing with { FloatingTypographyUnchanged = false },
            nameof(SecondaryWindowTypographyGateResults.FloatingTypographyUnchanged));
        return true;
    }

    // #35 tautology inventory (src/). Each item is one of:
    //   已删除 / 已改为真实检查 / 明确保留但非验收证据
    // 已删除:
    //   ReminderCenterWindow.SupportsKeyboardNavigation => true (0208b1e).
    // 已改为真实检查:
    //   ReminderCenterWindow.UsesScrollableList => true — replaced by
    //   ValidationReport.ValidateReminderCenterBehavior scrollsLongContent
    //   (Auto ScrollViewer, ScrollableHeight > 0, 12 wrapping bodies) and
    //   ReminderCenterScrollsLongContent.
    //   MainCountdownRemaining FormattedTexts.SequenceEqual(ExpectedTexts) after
    //   both sides were ExpectedText — report now compares RenderedTexts to
    //   independent ExpectedTexts.
    //   MainWindow.InitialFocusIsSafeAndDeterministic /
    //   TabSequenceStartsWithWorkspaceControls — report fields bind observed
    //   Loaded focus and the real workspace Tab prefix.
    // 明确保留但非验收证据:
    //   isolation passing fixtures; validate-only/production tray () => true;
    //   HasVisibleBrush non-solid fallback; floating secondary FormattedText
    //   from FormatRemaining; Core calendar/JSON => true matches.
    public static ReminderCenterKeyboardGateResults EvaluateReminderCenterKeyboardGates(
        ReminderCenterKeyboardGateInputs input) =>
        new(
            input.TabTraversalWorks,
            input.CloseGuardWorks,
            input.ShowPreservesFocusAndAccessibleName,
            input.EscapeFocusesAcknowledge);

    public static bool RunReminderCenterKeyboardGateIsolationBehavior()
    {
        var passing = new ReminderCenterKeyboardGateInputs(true, true, true, true);
        Must(EvaluateReminderCenterKeyboardGates(passing).AllPassed,
            "提醒中心键盘全通过夹具被错误拒绝。");
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { TabTraversalWorks = false },
            nameof(ReminderCenterKeyboardGateResults.TabTraversalWorks));
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { CloseGuardWorks = false },
            nameof(ReminderCenterKeyboardGateResults.CloseGuardWorks));
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { ShowPreservesFocusAndAccessibleName = false },
            nameof(ReminderCenterKeyboardGateResults.ShowPreservesFocusAndAccessibleName));
        VerifyOnlyReminderCenterKeyboardGateFails(
            passing with { EscapeFocusesAcknowledge = false },
            nameof(ReminderCenterKeyboardGateResults.EscapeFocusesAcknowledge));
        return true;
    }

    public static SecondaryWindowTypographyGateResults EvaluateSecondaryWindowTypographyGates(
        SecondaryWindowTypographyGateInputs input) =>
        new(
            input.RenderedScaleMatches,
            input.ContentRootsDeclareFontSize,
            input.FloatingTypographyUnchanged);

    public static bool RunMainShortcutGateIsolationBehavior()
    {
        var expectedCommands = new[]
        {
            "CountdownsWorkspace", "Filter", "Minimize", "NoteSearch",
            "NotesWorkspace", "SaveCountdown", "SaveNote", "SavePreferences", "TagManager"
        };
        var passingCommands = expectedCommands.ToDictionary(
            command => command,
            _ => true,
            StringComparer.Ordinal);
        var passing = new MainShortcutGateInputs(
            expectedCommands,
            passingCommands,
            true,
            true,
            true);
        Must(EvaluateMainShortcutGates(passing).AllPassed,
            "主窗口快捷键全通过夹具被错误拒绝。");

        var missingMapping = passingCommands
            .Where(pair => !string.Equals(pair.Key, "SaveCountdown", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        VerifyOnlyShortcutGateFails(
            passing with { ObservedCommands = missingMapping },
            nameof(MainShortcutGateResults.CommandsWork));
        foreach (var command in expectedCommands)
        {
            var brokenEvent = passingCommands.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            brokenEvent[command] = false;
            VerifyOnlyShortcutGateFails(
                passing with { ObservedCommands = brokenEvent },
                nameof(MainShortcutGateResults.CommandsWork));
        }
        VerifyOnlyShortcutGateFails(
            passing with { TextInputPreserved = false },
            nameof(MainShortcutGateResults.TextInputPreserved));
        VerifyOnlyShortcutGateFails(
            passing with { PopupGuarded = false },
            nameof(MainShortcutGateResults.PopupGuarded));
        VerifyOnlyShortcutGateFails(
            passing with { ModalOwnerGuarded = false },
            nameof(MainShortcutGateResults.ModalOwnerGuarded));
        return true;
    }

    public static MainShortcutGateResults EvaluateMainShortcutGates(MainShortcutGateInputs input)
    {
        var expected = input.ExpectedCommands.Order(StringComparer.Ordinal).ToArray();
        var observed = input.ObservedCommands.Keys.Order(StringComparer.Ordinal).ToArray();
        return new MainShortcutGateResults(
            expected.Length == 9 &&
                observed.SequenceEqual(expected, StringComparer.Ordinal) &&
                input.ObservedCommands.Values.All(value => value),
            input.TextInputPreserved,
            input.PopupGuarded,
            input.ModalOwnerGuarded);
    }

    public static MainKeyboardNavigationGateResults EvaluateMainKeyboardNavigationGates(
        MainKeyboardNavigationGateInputs input) =>
        new(input.InitialFocusSafe, input.CompleteTabSequence);

    public static MainStructuralAnnouncementGateResults EvaluateMainStructuralAnnouncementGates(
        MainStructuralAnnouncementGateInputs input) =>
        new(
            input.WorkspaceSelectionWithoutRedundantStatus,
            input.FilterResultAnnouncement,
            input.EmptyStateAnnouncements);

    public static bool RunMainStructuralAnnouncementGateIsolationBehavior()
    {
        var passing = new MainStructuralAnnouncementGateInputs(true, true, true);
        Must(
            EvaluateMainStructuralAnnouncementGates(passing).AllPassed,
            "主窗口结构变化通知全通过夹具被错误拒绝。");
        VerifyOnlyMainStructuralAnnouncementGateFails(
            passing with { WorkspaceSelectionWithoutRedundantStatus = false },
            nameof(MainStructuralAnnouncementGateResults.WorkspaceSelectionWithoutRedundantStatus));
        VerifyOnlyMainStructuralAnnouncementGateFails(
            passing with { FilterResultAnnouncement = false },
            nameof(MainStructuralAnnouncementGateResults.FilterResultAnnouncement));
        VerifyOnlyMainStructuralAnnouncementGateFails(
            passing with { EmptyStateAnnouncements = false },
            nameof(MainStructuralAnnouncementGateResults.EmptyStateAnnouncements));
        return true;
    }

    public static bool RunMainKeyboardNavigationGateIsolationBehavior()
    {
        var passing = new MainKeyboardNavigationGateInputs(true, true);
        Must(
            EvaluateMainKeyboardNavigationGates(passing).AllPassed,
            "主窗口键盘导航全通过夹具被错误拒绝。");
        VerifyOnlyMainKeyboardNavigationGateFails(
            passing with { InitialFocusSafe = false },
            nameof(MainKeyboardNavigationGateResults.InitialFocusSafe));
        VerifyOnlyMainKeyboardNavigationGateFails(
            passing with { CompleteTabSequence = false },
            nameof(MainKeyboardNavigationGateResults.CompleteTabSequence));
        return true;
    }

    // #153：弹层 Tab 层内循环门禁——筛选 / 标签管理 / 标签选择器三类弹层逐一收敛；#227 主行已不含溢出菜单，
    // 负向夹具证明每个输入位都会被独立击穿，评估器不可能是恒真。
    public static PopupFocusTabGateResults EvaluatePopupFocusTabGates(
        PopupFocusTabGateInputs input) =>
        new(
            input.FilterCyclesInsideLayer,
            input.TagManagerCyclesInsideLayer,
            input.TagSelectorCyclesInsideLayer);

    public static bool RunPopupFocusTabGateIsolationBehavior()
    {
        var passing = new PopupFocusTabGateInputs(true, true, true);
        Must(
            EvaluatePopupFocusTabGates(passing).AllCyclesInsideLayer,
            "弹层 Tab 层内循环全通过夹具被错误拒绝。");
        VerifyOnlyPopupFocusTabGateFails(
            passing with { FilterCyclesInsideLayer = false },
            nameof(PopupFocusTabGateResults.FilterCyclesInsideLayer));
        VerifyOnlyPopupFocusTabGateFails(
            passing with { TagManagerCyclesInsideLayer = false },
            nameof(PopupFocusTabGateResults.TagManagerCyclesInsideLayer));
        VerifyOnlyPopupFocusTabGateFails(
            passing with { TagSelectorCyclesInsideLayer = false },
            nameof(PopupFocusTabGateResults.TagSelectorCyclesInsideLayer));
        return true;
    }

    private static void VerifyOnlyPopupFocusTabGateFails(
        PopupFocusTabGateInputs input,
        string target)
    {
        var result = EvaluatePopupFocusTabGates(input);
        Must(!result.AllCyclesInsideLayer && result.FailedChecks.SequenceEqual([target], StringComparer.Ordinal),
            $"弹层 Tab 层内循环负向夹具未只击穿 {target}。");
    }
}
