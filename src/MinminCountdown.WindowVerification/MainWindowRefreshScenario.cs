using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #302：在隔离真实管理窗口、悬浮窗、托盘控制器与提醒中心上验证两项刷新优化：
// 1) 便签搜索合并约 150ms 输入后单次渲染、按 Enter 立即单次渲染无尾随重复、取消/共享筛选同步一致，
//    且不重建标签选择器/筛选标签/标签管理行，仅标签集合变化时更新；
// 2) 管理窗口隐藏期间多次工作区改动不重建行控件并保留草稿与筛选条件，经托盘菜单、悬浮窗入口与
//    提醒“打开来源”重新显示时在首帧前刷新一次，首帧内容与最新设置一致。
internal static class MainWindowRefreshScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) RunNoteSearchAndTagControls(string meaning)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-refresh-search-302-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var failures = new List<string>();
        var observations = new List<string>();
        MainWindow? main = null;
        FloatingWindow? floating = null;
        try
        {
            var clock = new MutableClock(FixtureUtc);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
            var logger = new DiagnosticLogger(diagnosticPath);
            var lifecycle = new ManagerWindowLifecycle(
                () => CreateHiddenOffscreenMainWindow(workspace, calendar, logger),
                static window => window.ApplyTheme());
            main = lifecycle.EnsureCreated();
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            floating = CreateOffscreenFloatingWindow(workspace, calendar, lifecycle);

            main.Show();
            floating.Show();
            SettleLayout(main);
            SettleFloating(floating);

            var workTag = CreateTag(workspace, "工作", "#F4B6A6", main.Dispatcher);
            var studyTag = CreateTag(workspace, "学习", "#F4D58D", main.Dispatcher);
            Apply(workspace, new SaveCountdownChange(
                null,
                "搜索回归倒计时",
                FixtureUtc.AddDays(5),
                [workTag.Id],
                Array.Empty<ReminderRule>()), main.Dispatcher);
            Apply(workspace, new SaveNoteChange(
                null,
                "便签搜索目标一",
                workspace.Snapshot.BeijingNow.Date,
                false,
                [workTag.Id],
                Array.Empty<ReminderRule>()), main.Dispatcher);
            Apply(workspace, new SaveNoteChange(
                null,
                "特殊筛选条目二",
                workspace.Snapshot.BeijingNow.Date,
                false,
                [studyTag.Id],
                Array.Empty<ReminderRule>()), main.Dispatcher);
            Apply(workspace, new SaveNoteChange(
                null,
                "浮窗同步条目三",
                workspace.Snapshot.BeijingNow.Date,
                false,
                [workTag.Id],
                Array.Empty<ReminderRule>()), main.Dispatcher);
            Apply(workspace, new SaveNoteChange(
                null,
                "已完成便签搜索目标四",
                workspace.Snapshot.BeijingNow.Date,
                true,
                [studyTag.Id],
                Array.Empty<ReminderRule>()), main.Dispatcher);

            SwitchWorkspace(main, WorkspaceKind.Notes);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            SettleLayout(main);
            SettleFloating(floating);

            var noteSearch = Required<TextBox>(main, "NoteSearch");
            var activeNoteRows = Required<Panel>(main, "ActiveNoteRows");
            var completedNoteRows = Required<Panel>(main, "CompletedNoteRows");
            var countdownTags = Required<TagSelector>(main, "CountdownTags");
            var noteTags = Required<TagSelector>(main, "NoteTags");
            var filterTagOptions = Required<WrapPanel>(main, "FilterTagOptions");
            var tagManagerRows = Required<Panel>(main, "TagManagerRows");
            var clearFilterButton = Required<Button>(main, "ClearFilterButton");
            var floatingSearch = Required<TextBox>(floating, "FloatingNoteSearch");
            var floatingRows = Required<Panel>(floating, "FloatingRows");

            var initialTagControls = CaptureTagControls(countdownTags, noteTags, filterTagOptions, tagManagerRows);
            Require(
                initialTagControls.CountdownPickerItems.Length == 2 &&
                initialTagControls.NotePickerItems.Length == 2 &&
                initialTagControls.FilterChips.Length == 3 &&
                initialTagControls.TagManagerRows.Length == 2,
                $"初始标签控件数量异常：{initialTagControls.Describe()}。",
                failures);

            var stateChangedLog = new List<SearchRenderObservation>();
            EventHandler onStateChanged = (_, _) =>
            {
                stateChangedLog.Add(new SearchRenderObservation(
                    workspace.Snapshot.Filter.NoteSearch ?? string.Empty,
                    CaptureNoteCards(activeNoteRows),
                    CaptureNoteCards(completedNoteRows)));
            };
            workspace.StateChanged += onStateChanged;
            try
            {
                // 1) 连续输入多字符：约 150ms 合并窗口内不渲染，窗口到期后仅单次渲染（无双重建），且共享筛选同步到悬浮窗。
                var activeBeforeTyping = CaptureNoteCards(activeNoteRows);
                var completedBeforeTyping = CaptureNoteCards(completedNoteRows);
                stateChangedLog.Clear();

                var typingWatch = Stopwatch.StartNew();
                foreach (var prefix in new[] { "便", "便签", "便签搜", "便签搜索" })
                {
                    noteSearch.Text = prefix;
                    if (!string.Equals(prefix, "便签搜索", StringComparison.Ordinal))
                    {
                        WaitDuration(main.Dispatcher, 25);
                    }
                }

                var afterLastChar = Stopwatch.StartNew();
                WindowValidationInput.Pump(main.Dispatcher);
                var immediateStateChanges = stateChangedLog.Count;
                var immediateActiveSame = SameReferences(activeBeforeTyping, CaptureNoteCards(activeNoteRows));
                var immediateCompletedSame = SameReferences(completedBeforeTyping, CaptureNoteCards(completedNoteRows));
                var immediateFilterEmpty = string.IsNullOrEmpty(workspace.Snapshot.Filter.NoteSearch);

                Check(
                    failures,
                    "连续输入最后字符刚落地时不应提前触发筛选或重建便签列表",
                    immediateStateChanges == 0 && immediateActiveSame && immediateCompletedSame && immediateFilterEmpty,
                    $"stateChanges={immediateStateChanges}, activeSame={immediateActiveSame}, completedSame={immediateCompletedSame}, filter='{workspace.Snapshot.Filter.NoteSearch}'");

                var debouncedFired = WindowValidationInput.WaitUntil(
                    main.Dispatcher,
                    () => stateChangedLog.Count > 0,
                    timeoutMilliseconds: 600);
                var debounceElapsedMs = afterLastChar.Elapsed.TotalMilliseconds;
                // 再等一轮合并时长，确认没有尾随重复渲染。
                WaitDuration(main.Dispatcher, 240);
                SettleLayout(main);
                SettleFloating(floating);

                var activeAfterDebounce = CaptureNoteCards(activeNoteRows);
                var completedAfterDebounce = CaptureNoteCards(completedNoteRows);
                var activeTextsAfterDebounce = ReadNoteCardTexts(activeNoteRows);
                var completedTextsAfterDebounce = ReadNoteCardTexts(completedNoteRows);
                var floatingTextsAfterDebounce = ReadFloatingNoteTexts(floatingRows);
                observations.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"DEBOUNCE: totalTypingMs={typingWatch.Elapsed.TotalMilliseconds:0.#}, afterLastCharMs={debounceElapsedMs:0.#}, stateChanges={stateChangedLog.Count}, active=[{string.Join(',', activeTextsAfterDebounce)}], completed=[{string.Join(',', completedTextsAfterDebounce)}]"));

                Check(
                    failures,
                    "连续输入合并约 150ms 后应精确触发一次筛选与应用",
                    debouncedFired && debounceElapsedMs >= 90 && stateChangedLog.Count == 1 &&
                    string.Equals(stateChangedLog[0].FilterTerm, "便签搜索", StringComparison.Ordinal),
                    $"fired={debouncedFired}, elapsedMs={debounceElapsedMs:0.#}, stateChanges={stateChangedLog.Count}, terms=[{string.Join('|', stateChangedLog.Select(x => x.FilterTerm))}]");
                Check(
                    failures,
                    "合并应用只重建便签列表一次（不得先由 StateChanged 再由 TextChanged 二次重建）",
                    stateChangedLog.Count == 1 &&
                    !SameReferences(activeBeforeTyping, activeAfterDebounce) &&
                    SameReferences(stateChangedLog[0].ActiveCards, activeAfterDebounce) &&
                    SameReferences(stateChangedLog[0].CompletedCards, completedAfterDebounce),
                    $"activeSameAsFirstPass={(stateChangedLog.Count > 0 && SameReferences(stateChangedLog[0].ActiveCards, activeAfterDebounce))}, completedSameAsFirstPass={(stateChangedLog.Count > 0 && SameReferences(stateChangedLog[0].CompletedCards, completedAfterDebounce))}");
                Check(
                    failures,
                    "合并搜索结果在管理窗口（未完成/已完成）与悬浮窗保持一致",
                    activeTextsAfterDebounce.SequenceEqual(["便签搜索目标一"]) &&
                    completedTextsAfterDebounce.SequenceEqual(["已完成便签搜索目标四"]) &&
                    string.Equals(workspace.Snapshot.Filter.NoteSearch, "便签搜索", StringComparison.Ordinal) &&
                    floatingTextsAfterDebounce.SequenceEqual(["便签搜索目标一"]),
                    $"active=[{string.Join(',', activeTextsAfterDebounce)}], completed=[{string.Join(',', completedTextsAfterDebounce)}], floating=[{string.Join(',', floatingTextsAfterDebounce)}]");
                VerifyTagControlsPreserved(
                    "合并搜索后",
                    initialTagControls,
                    CaptureTagControls(countdownTags, noteTags, filterTagOptions, tagManagerRows),
                    failures);

                // 2) 输入后立即按 Enter：同步单次渲染，且之后 260ms 不再触发尾随重复渲染。
                var activeBeforeEnter = CaptureNoteCards(activeNoteRows);
                var completedBeforeEnter = CaptureNoteCards(completedNoteRows);
                stateChangedLog.Clear();

                noteSearch.Text = "特";
                WaitDuration(main.Dispatcher, 20);
                noteSearch.Text = "特殊";
                Check(
                    failures,
                    "按 Enter 前不应提前重建便签列表",
                    stateChangedLog.Count == 0 && SameReferences(activeBeforeEnter, CaptureNoteCards(activeNoteRows)),
                    $"stateChanges={stateChangedLog.Count}");

                var enterWatch = Stopwatch.StartNew();
                var enterHandled = WindowValidationInput.RaisePreviewKey(noteSearch, Key.Enter);
                var enterElapsedMs = enterWatch.Elapsed.TotalMilliseconds;
                var activeRightAfterEnter = CaptureNoteCards(activeNoteRows);
                var completedRightAfterEnter = CaptureNoteCards(completedNoteRows);
                var activeTextsAfterEnter = ReadNoteCardTexts(activeNoteRows);
                var floatingTextsAfterEnter = ReadFloatingNoteTexts(floatingRows);

                Check(
                    failures,
                    "按 Enter 应立即单次应用筛选并取消合并计时",
                    enterHandled &&
                    enterElapsedMs < 100 &&
                    stateChangedLog.Count == 1 &&
                    string.Equals(stateChangedLog[0].FilterTerm, "特殊", StringComparison.Ordinal) &&
                    SameReferences(stateChangedLog[0].ActiveCards, activeRightAfterEnter) &&
                    SameReferences(stateChangedLog[0].CompletedCards, completedRightAfterEnter) &&
                    activeTextsAfterEnter.SequenceEqual(["特殊筛选条目二"]) &&
                    completedRightAfterEnter.Length == 0 &&
                    floatingTextsAfterEnter.SequenceEqual(["特殊筛选条目二"]),
                    $"handled={enterHandled}, elapsedMs={enterElapsedMs:0.#}, stateChanges={stateChangedLog.Count}, samePass={(stateChangedLog.Count > 0 && SameReferences(stateChangedLog[0].ActiveCards, activeRightAfterEnter))}, active=[{string.Join(',', activeTextsAfterEnter)}], floating=[{string.Join(',', floatingTextsAfterEnter)}]");

                WaitDuration(main.Dispatcher, 260);
                var activeAfterEnterWait = CaptureNoteCards(activeNoteRows);
                Check(
                    failures,
                    "按 Enter 后等待超过合并时长不得产生尾随重复渲染",
                    stateChangedLog.Count == 1 && SameReferences(activeRightAfterEnter, activeAfterEnterWait),
                    $"stateChanges={stateChangedLog.Count}, sameAfterWait={SameReferences(activeRightAfterEnter, activeAfterEnterWait)}");
                VerifyTagControlsPreserved(
                    "Enter 立即搜索后",
                    initialTagControls,
                    CaptureTagControls(countdownTags, noteTags, filterTagOptions, tagManagerRows),
                    failures);

                // 3) 清除筛选与悬浮窗外部筛选同步均取消挂起的合并计时，双向语义保持不变。
                noteSearch.Text = "待清除挂起词";
                clearFilterButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                SettleLayout(main);
                WaitDuration(main.Dispatcher, 240);
                Check(
                    failures,
                    "清除筛选应取消挂起的搜索合并并清空搜索词与结果",
                    string.IsNullOrEmpty(noteSearch.Text) &&
                    string.IsNullOrEmpty(workspace.Snapshot.Filter.NoteSearch) &&
                    ReadNoteCardTexts(activeNoteRows).Length == 3,
                    $"text='{noteSearch.Text}', filter='{workspace.Snapshot.Filter.NoteSearch}', count={ReadNoteCardTexts(activeNoteRows).Length}");

                noteSearch.Text = "待外部覆盖词";
                WindowValidationInput.RaisePreviewKey(floating, Key.F, ModifierKeys.Control);
                SettleFloating(floating);
                floatingSearch.Text = "浮窗同步";
                SettleFloating(floating);
                SettleLayout(main);
                WaitDuration(main.Dispatcher, 240);
                Check(
                    failures,
                    "悬浮窗写入共享搜索词应取消管理窗挂起输入并同步双向列表",
                    string.Equals(noteSearch.Text, "浮窗同步", StringComparison.Ordinal) &&
                    string.Equals(workspace.Snapshot.Filter.NoteSearch, "浮窗同步", StringComparison.Ordinal) &&
                    ReadNoteCardTexts(activeNoteRows).SequenceEqual(["浮窗同步条目三"]) &&
                    ReadFloatingNoteTexts(floatingRows).SequenceEqual(["浮窗同步条目三"]),
                    $"mainText='{noteSearch.Text}', filter='{workspace.Snapshot.Filter.NoteSearch}', mainRows=[{string.Join(',', ReadNoteCardTexts(activeNoteRows))}]");

                noteSearch.Text = "待浮窗清空词";
                WindowValidationInput.RaisePreviewKey(floating, Key.Escape);
                SettleFloating(floating);
                SettleLayout(main);
                WaitDuration(main.Dispatcher, 240);
                Check(
                    failures,
                    "悬浮窗清空共享搜索词应取消管理窗挂起输入并恢复全部列表",
                    string.IsNullOrEmpty(noteSearch.Text) &&
                    string.IsNullOrEmpty(workspace.Snapshot.Filter.NoteSearch) &&
                    ReadNoteCardTexts(activeNoteRows).Length == 3 &&
                    ReadFloatingNoteTexts(floatingRows).Length == 3,
                    $"mainText='{noteSearch.Text}', filter='{workspace.Snapshot.Filter.NoteSearch}'");

                // 4) 切换筛选标签芯片继续原位同步，不重建四处标签控件。
                var workFilterToggle = filterTagOptions.Children
                    .OfType<ToggleButton>()
                    .Single(button => string.Equals(button.Uid, workTag.Id, StringComparison.OrdinalIgnoreCase));
                workFilterToggle.IsChecked = true;
                workFilterToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                SettleLayout(main);
                SettleFloating(floating);
                Check(
                    failures,
                    "勾选筛选标签后管理窗与悬浮窗共享标签筛选结果一致",
                    ReadNoteCardTexts(activeNoteRows).Length == 2 &&
                    ReadFloatingNoteTexts(floatingRows).Length == 2,
                    $"main={ReadNoteCardTexts(activeNoteRows).Length}, floating={ReadFloatingNoteTexts(floatingRows).Length}");
                VerifyTagControlsPreserved(
                    "筛选标签勾选与清除全过程后",
                    initialTagControls,
                    CaptureTagControls(countdownTags, noteTags, filterTagOptions, tagManagerRows),
                    failures);

                clearFilterButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                SettleLayout(main);
                VerifyTagControlsPreserved(
                    "重置筛选后",
                    initialTagControls,
                    CaptureTagControls(countdownTags, noteTags, filterTagOptions, tagManagerRows),
                    failures);

                // 5) 只有标签集合本身变化（新增、改名、改色、删除）时才更新四处标签控件。
                var createdTag = CreateTag(workspace, "临时标签", "#F1D5C5", main.Dispatcher);
                var afterCreate = CaptureTagControls(countdownTags, noteTags, filterTagOptions, tagManagerRows);
                Check(
                    failures,
                    "新增标签后标签选择器、筛选标签与标签管理行均同步增加",
                    afterCreate.CountdownPickerItems.Length == 3 &&
                    afterCreate.NotePickerItems.Length == 3 &&
                    afterCreate.FilterChips.Length == 4 &&
                    afterCreate.TagManagerRows.Length == 3 &&
                    afterCreate.FilterChips.OfType<ToggleButton>().Any(toggle => Equals(toggle.Content, "临时标签")),
                    afterCreate.Describe());

                Apply(workspace, new RenameTagChange(createdTag.Id, "已改名临时标签", createdTag.Color), main.Dispatcher);
                var afterRename = CaptureTagControls(countdownTags, noteTags, filterTagOptions, tagManagerRows);
                Check(
                    failures,
                    "标签改名后四处标签控件均同步显示新名称",
                    afterRename.CountdownPickerItems.Any(box => Equals(box.Content, "已改名临时标签")) &&
                    afterRename.NotePickerItems.Any(box => Equals(box.Content, "已改名临时标签")) &&
                    afterRename.FilterChips.OfType<ToggleButton>().Any(toggle => Equals(toggle.Content, "已改名临时标签")) &&
                    ReadTagManagerNames(tagManagerRows).Contains("已改名临时标签", StringComparer.Ordinal),
                    $"tagManagerNames=[{string.Join(',', ReadTagManagerNames(tagManagerRows))}]");

                var managerRowsBeforeRecolor = afterRename.TagManagerRows;
                Apply(workspace, new RenameTagChange(createdTag.Id, "已改名临时标签", "#B8E0D2"), main.Dispatcher);
                var afterRecolor = CaptureTagControls(countdownTags, noteTags, filterTagOptions, tagManagerRows);
                Check(
                    failures,
                    "标签仅改色时按标签集合签名重建标签管理行以捕获新颜色",
                    !SameReferences(managerRowsBeforeRecolor, afterRecolor.TagManagerRows),
                    "改色后 TagManagerRows 未随标签颜色签名刷新");

                Apply(workspace, new DeleteTagChange(createdTag.Id), main.Dispatcher);
                var afterDelete = CaptureTagControls(countdownTags, noteTags, filterTagOptions, tagManagerRows);
                Check(
                    failures,
                    "删除标签后四处标签控件同步恢复原数量",
                    afterDelete.CountdownPickerItems.Length == 2 &&
                    afterDelete.NotePickerItems.Length == 2 &&
                    afterDelete.FilterChips.Length == 3 &&
                    afterDelete.TagManagerRows.Length == 2,
                    afterDelete.Describe());
            }
            finally
            {
                workspace.StateChanged -= onStateChanged;
            }
        }
        finally
        {
            CloseWindow(floating);
            CloseWindow(main);
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }

        return FormatResult(meaning, failures, observations);
    }

    internal static (bool Passed, string Explanation) RunHiddenDeferAndFirstFrameRefresh(string meaning)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-refresh-hidden-302-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var failures = new List<string>();
        var observations = new List<string>();
        MainWindow? main = null;
        FloatingWindow? floating = null;
        FloatingTrayController? trayController = null;
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableClock(FixtureUtc);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
            var logger = new DiagnosticLogger(diagnosticPath);
            var lifecycle = new ManagerWindowLifecycle(
                () => CreateHiddenOffscreenMainWindow(workspace, calendar, logger),
                static window => window.ApplyTheme());
            main = lifecycle.EnsureCreated();
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            floating = CreateOffscreenFloatingWindow(workspace, calendar, lifecycle);
            trayController = new FloatingTrayController(
                floating,
                Path.Combine(AppContext.BaseDirectory, "countdown-icon.ico"),
                static () => true,
                foregroundInterop: null,
                attachShellNotifyIcon: false,
                openManager: () => lifecycle.RestorePreservingState());
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                logger,
                (kind, id) => lifecycle.OpenReminderSource(kind, id),
                () => CreateOffscreenReminderCenter(floating, logger));

            floating.Show();
            SettleFloating(floating);

            var countdownAId = SaveCountdown(workspace, "初始倒计时A", 3, main.Dispatcher);
            var countdownBId = SaveCountdown(workspace, "初始倒计时B", 5, main.Dispatcher);
            var floatingNoteAId = SaveNote(workspace, "浮窗入口-初始便签", main.Dispatcher);

            lifecycle.Reveal(WorkspaceKind.Countdowns);
            SettleLayout(main);

            var countdownRows = Required<Panel>(main, "CountdownRows");
            var activeNoteRows = Required<Panel>(main, "ActiveNoteRows");
            var countdownNameDraft = Required<TextBox>(main, "CountdownName");
            var noteTextDraft = Required<TextBox>(main, "NoteText");
            var noteSearch = Required<TextBox>(main, "NoteSearch");
            var displayPrecision = Required<ComboBox>(main, "DisplayPrecision");
            var monthlySalary = Required<TextBox>(main, "MonthlySalary");
            var workStart = Required<TextBox>(main, "WorkStart");
            var workEnd = Required<TextBox>(main, "WorkEnd");
            var openManagerButton = Required<Button>(floating, "OpenManagerButton");

            // === 路径 A：托盘菜单“打开管理窗口” ===
            countdownNameDraft.Text = "托盘草稿倒计时";
            noteTextDraft.Text = "托盘草稿便签";
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with
            {
                CountdownStatus = CountdownStatusFilter.Active,
                NoteSearch = "托盘过滤词"
            });
            SettleLayout(main);

            var countdownRowsBeforeTrayHide = CaptureChildren(countdownRows);
            Require(countdownRowsBeforeTrayHide.Length == 2, "托盘路径隐藏前应有 2 条倒计时行。", failures);

            ManagerWindowRestore.HideManagerOnly(main);
            WindowValidationInput.Pump(main.Dispatcher);
            Require(!main.IsVisible, "托盘路径前管理窗口未进入隐藏态。", failures);

            // 隐藏期间执行多次改动：新增/编辑/删除倒计时、新增便签、修改显示设置。
            Apply(workspace, new SaveCountdownChange(
                null,
                "隐藏新增倒计时-托盘",
                FixtureUtc.AddDays(4),
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()), main.Dispatcher);
            Check(
                failures,
                "隐藏期间新增倒计时不得重建管理窗口行控件",
                SameReferences(countdownRowsBeforeTrayHide, CaptureChildren(countdownRows)),
                "新增倒计时触发了隐藏管理窗重建");

            Apply(workspace, new SaveCountdownChange(
                countdownAId,
                "隐藏编辑倒计时A-托盘",
                FixtureUtc.AddDays(2),
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()), main.Dispatcher);
            Apply(workspace, new DeleteCountdownChange(countdownBId), main.Dispatcher);
            SaveNote(workspace, "托盘过滤词-隐藏新增便签", main.Dispatcher);
            Apply(workspace, new SavePreferencesChange(
                "Hour",
                12345m,
                new TimeSpan(9, 30, 0),
                new TimeSpan(18, 30, 0)), main.Dispatcher);

            Check(
                failures,
                "隐藏期间多次增改删与偏好改动后行控件实例保持完全未重建",
                !main.IsVisible && SameReferences(countdownRowsBeforeTrayHide, CaptureChildren(countdownRows)),
                $"visible={main.IsVisible}, sameRows={SameReferences(countdownRowsBeforeTrayHide, CaptureChildren(countdownRows))}");

            var trayShow = ObserveFirstVisibleFrame(
                main,
                () => trayController.InvokeOpenManagerForValidation(),
                () => new CountdownShowSnapshot(
                    CaptureChildren(countdownRows),
                    ReadCountdownTitles(countdownRows),
                    displayPrecision.SelectedIndex,
                    monthlySalary.Text));

            observations.Add(
                $"TRAY_SHOW: atVisible=[{string.Join(',', trayShow.AtVisibleChanged?.Titles ?? [])}], " +
                $"firstFrame=[{string.Join(',', trayShow.AtFirstFrame?.Titles ?? [])}], " +
                $"precision={trayShow.AtFirstFrame?.PrecisionIndex}, salary={trayShow.AtFirstFrame?.SalaryText}");

            Check(
                failures,
                "托盘菜单重新显示时在首帧前单次刷新且首帧呈现最新倒计时与设置",
                main.IsVisible &&
                main.WindowState == WindowState.Normal &&
                trayShow.AtVisibleChanged is not null &&
                trayShow.AtFirstFrame is not null &&
                trayShow.AtVisibleChanged.Titles.SequenceEqual(["隐藏编辑倒计时A-托盘", "隐藏新增倒计时-托盘"]) &&
                trayShow.AtFirstFrame.Titles.SequenceEqual(["隐藏编辑倒计时A-托盘", "隐藏新增倒计时-托盘"]) &&
                trayShow.AtFirstFrame.PrecisionIndex == 1 &&
                string.Equals(trayShow.AtFirstFrame.SalaryText, 12345m.ToString("N2", CultureInfo.CurrentCulture), StringComparison.Ordinal) &&
                SameReferences(trayShow.AtVisibleChanged.Rows, trayShow.AtFirstFrame.Rows) &&
                SameReferences(trayShow.AtFirstFrame.Rows, CaptureChildren(countdownRows)),
                $"atVisible=[{string.Join(',', trayShow.AtVisibleChanged?.Titles ?? [])}], firstFrame=[{string.Join(',', trayShow.AtFirstFrame?.Titles ?? [])}], sameRows={(trayShow.AtVisibleChanged is not null && trayShow.AtFirstFrame is not null && SameReferences(trayShow.AtVisibleChanged.Rows, trayShow.AtFirstFrame.Rows))}");
            Check(
                failures,
                "托盘菜单隐藏并重显后保留未保存草稿与筛选条件",
                string.Equals(countdownNameDraft.Text, "托盘草稿倒计时", StringComparison.Ordinal) &&
                string.Equals(noteTextDraft.Text, "托盘草稿便签", StringComparison.Ordinal) &&
                workspace.Snapshot.Filter.CountdownStatus == CountdownStatusFilter.Active &&
                string.Equals(workspace.Snapshot.Filter.NoteSearch, "托盘过滤词", StringComparison.Ordinal) &&
                string.Equals(noteSearch.Text, "托盘过滤词", StringComparison.Ordinal),
                $"cdDraft='{countdownNameDraft.Text}', noteDraft='{noteTextDraft.Text}', search='{noteSearch.Text}'");

            // === 路径 B：悬浮窗入口（OpenManagerButton） ===
            SwitchWorkspace(main, WorkspaceKind.Notes);
            countdownNameDraft.Text = "浮窗入口草稿倒计时";
            noteTextDraft.Text = "浮窗入口草稿便签";
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { NoteSearch = "浮窗入口" });
            SettleLayout(main);

            var noteCardsBeforeFloatingHide = CaptureNoteCards(activeNoteRows);
            Require(
                noteCardsBeforeFloatingHide.Length == 1 &&
                ReadNoteCardTexts(activeNoteRows).SequenceEqual(["浮窗入口-初始便签"]),
                $"浮窗入口路径隐藏前便签行异常：[{string.Join(',', ReadNoteCardTexts(activeNoteRows))}]。",
                failures);

            ManagerWindowRestore.HideManagerOnly(main);
            WindowValidationInput.Pump(main.Dispatcher);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            SettleFloating(floating);

            Apply(workspace, new SaveNoteChange(
                null,
                "浮窗入口-隐藏新增便签",
                workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()), main.Dispatcher);
            Apply(workspace, new SaveNoteChange(
                floatingNoteAId,
                "浮窗入口-隐藏编辑便签",
                workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()), main.Dispatcher);
            var tempDeletedId = SaveNote(workspace, "浮窗入口-临时删除便签", main.Dispatcher);
            Apply(workspace, new DeleteNoteChange(tempDeletedId), main.Dispatcher);
            Apply(workspace, new SavePreferencesChange(
                "Minute",
                15000m,
                new TimeSpan(8, 30, 0),
                new TimeSpan(17, 30, 0)), main.Dispatcher);

            Check(
                failures,
                "隐藏期间多次便签增改删后管理窗口便签行控件未重建",
                !main.IsVisible && SameReferences(noteCardsBeforeFloatingHide, CaptureNoteCards(activeNoteRows)),
                $"visible={main.IsVisible}, sameCards={SameReferences(noteCardsBeforeFloatingHide, CaptureNoteCards(activeNoteRows))}");

            var floatingShow = ObserveFirstVisibleFrame(
                main,
                () => openManagerButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)),
                () => new NoteShowSnapshot(
                    CaptureNoteCards(activeNoteRows),
                    ReadNoteCardTexts(activeNoteRows),
                    displayPrecision.SelectedIndex,
                    workStart.Text,
                    workEnd.Text));

            observations.Add(
                $"FLOATING_SHOW: atVisible=[{string.Join(',', floatingShow.AtVisibleChanged?.Texts ?? [])}], " +
                $"firstFrame=[{string.Join(',', floatingShow.AtFirstFrame?.Texts ?? [])}], " +
                $"work={floatingShow.AtFirstFrame?.WorkStartText}-{floatingShow.AtFirstFrame?.WorkEndText}");

            Check(
                failures,
                "悬浮窗入口重新显示时在首帧前单次刷新且首帧呈现最新便签与设置",
                main.IsVisible &&
                main.CurrentWorkspace == WorkspaceKind.Notes &&
                floatingShow.AtVisibleChanged is not null &&
                floatingShow.AtFirstFrame is not null &&
                floatingShow.AtVisibleChanged.Texts.Length == 2 &&
                floatingShow.AtVisibleChanged.Texts.Contains("浮窗入口-隐藏新增便签", StringComparer.Ordinal) &&
                floatingShow.AtVisibleChanged.Texts.Contains("浮窗入口-隐藏编辑便签", StringComparer.Ordinal) &&
                !floatingShow.AtVisibleChanged.Texts.Contains("浮窗入口-临时删除便签", StringComparer.Ordinal) &&
                floatingShow.AtFirstFrame.PrecisionIndex == 2 &&
                string.Equals(floatingShow.AtFirstFrame.WorkStartText, "08:30", StringComparison.Ordinal) &&
                string.Equals(floatingShow.AtFirstFrame.WorkEndText, "17:30", StringComparison.Ordinal) &&
                SameReferences(floatingShow.AtVisibleChanged.Cards, floatingShow.AtFirstFrame.Cards) &&
                SameReferences(floatingShow.AtFirstFrame.Cards, CaptureNoteCards(activeNoteRows)),
                $"atVisible=[{string.Join(',', floatingShow.AtVisibleChanged?.Texts ?? [])}], firstFrame=[{string.Join(',', floatingShow.AtFirstFrame?.Texts ?? [])}], sameCards={(floatingShow.AtVisibleChanged is not null && floatingShow.AtFirstFrame is not null && SameReferences(floatingShow.AtVisibleChanged.Cards, floatingShow.AtFirstFrame.Cards))}");
            Check(
                failures,
                "悬浮窗入口隐藏并重显后保留未保存草稿与筛选条件",
                string.Equals(countdownNameDraft.Text, "浮窗入口草稿倒计时", StringComparison.Ordinal) &&
                string.Equals(noteTextDraft.Text, "浮窗入口草稿便签", StringComparison.Ordinal) &&
                string.Equals(workspace.Snapshot.Filter.NoteSearch, "浮窗入口", StringComparison.Ordinal) &&
                string.Equals(noteSearch.Text, "浮窗入口", StringComparison.Ordinal),
                $"cdDraft='{countdownNameDraft.Text}', noteDraft='{noteTextDraft.Text}', search='{noteSearch.Text}'");

            // === 路径 C：提醒中心“打开来源” ===
            SwitchWorkspace(main, WorkspaceKind.Countdowns);
            countdownNameDraft.Text = "提醒来源草稿倒计时";
            noteTextDraft.Text = "提醒来源草稿便签";
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { NoteSearch = "屏蔽提醒来源的筛选词" });
            SettleLayout(main);

            var countdownRowsBeforeReminderHide = CaptureChildren(countdownRows);
            ManagerWindowRestore.HideManagerOnly(main);
            WindowValidationInput.Pump(main.Dispatcher);

            var reminderDueUtc = clock.UtcNow.AddMinutes(5);
            Apply(workspace, new SaveNoteChange(
                null,
                "隐藏期间到期提醒便签",
                workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                new ReminderRule[] { new AbsoluteOnceReminderRule("refresh-302-reminder-rule", reminderDueUtc) }), main.Dispatcher);
            var remindedNote = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "隐藏期间到期提醒便签");
            SaveCountdown(workspace, "提醒期间隐藏新增倒计时", 6, main.Dispatcher);

            clock.UtcNow = reminderDueUtc;
            coordinator.Tick(reminderDueUtc);
            var centerReady = WindowValidationInput.WaitUntil(
                main.Dispatcher,
                () => coordinator.ActiveWindow is { IsVisible: true } && coordinator.ActiveOccurrenceConfirmed,
                timeoutMilliseconds: 1000);
            Require(centerReady && coordinator.ActiveWindow is not null, "提醒中心未完成送达确认，无法点击打开来源。", failures);

            Check(
                failures,
                "隐藏期间新增提醒便签、倒计时与送达落盘均不重建管理窗口行控件",
                !main.IsVisible && SameReferences(countdownRowsBeforeReminderHide, CaptureChildren(countdownRows)),
                $"visible={main.IsVisible}, sameRows={SameReferences(countdownRowsBeforeReminderHide, CaptureChildren(countdownRows))}");

            var openSourceButton = VisualDescendants<Button>(coordinator.ActiveWindow!)
                .Single(button => AutomationProperties.GetName(button)?.StartsWith("打开便签来源：", StringComparison.Ordinal) == true);

            var reminderShow = ObserveFirstVisibleFrame(
                main,
                () =>
                {
                    if (!WindowValidationInput.InvokeButton(openSourceButton))
                    {
                        failures.Add("无法经 UIA Invoke 触发提醒中心“打开来源”按钮。");
                    }
                },
                () => new ReminderSourceShowSnapshot(
                    CaptureNoteCards(activeNoteRows),
                    ReadNoteCardTexts(activeNoteRows),
                    main.CurrentWorkspace));

            observations.Add(
                $"REMINDER_SHOW: atVisible=[{string.Join(',', reminderShow.AtVisibleChanged?.Texts ?? [])}], " +
                $"firstFrame=[{string.Join(',', reminderShow.AtFirstFrame?.Texts ?? [])}], " +
                $"focused={(Keyboard.FocusedElement as Button)?.Tag}");

            Check(
                failures,
                "提醒“打开来源”重新显示时在首帧前单次刷新并揭示最新提醒来源便签",
                main.IsVisible &&
                main.CurrentWorkspace == WorkspaceKind.Notes &&
                reminderShow.AtVisibleChanged is not null &&
                reminderShow.AtFirstFrame is not null &&
                reminderShow.AtVisibleChanged.Workspace == WorkspaceKind.Notes &&
                reminderShow.AtVisibleChanged.Texts.Contains("隐藏期间到期提醒便签", StringComparer.Ordinal) &&
                reminderShow.AtFirstFrame.Texts.Contains("隐藏期间到期提醒便签", StringComparer.Ordinal) &&
                SameReferences(reminderShow.AtVisibleChanged.Cards, reminderShow.AtFirstFrame.Cards) &&
                SameReferences(reminderShow.AtFirstFrame.Cards, CaptureNoteCards(activeNoteRows)) &&
                Keyboard.FocusedElement is Button { Tag: string focusedId } &&
                string.Equals(focusedId, remindedNote.Id, StringComparison.OrdinalIgnoreCase),
                $"atVisible=[{string.Join(',', reminderShow.AtVisibleChanged?.Texts ?? [])}], firstFrame=[{string.Join(',', reminderShow.AtFirstFrame?.Texts ?? [])}], focused={DescribeElement(Keyboard.FocusedElement)}");
            Check(
                failures,
                "提醒“打开来源”重新显示后保留未保存草稿与原有筛选条件",
                string.Equals(countdownNameDraft.Text, "提醒来源草稿倒计时", StringComparison.Ordinal) &&
                string.Equals(noteTextDraft.Text, "提醒来源草稿便签", StringComparison.Ordinal) &&
                string.Equals(workspace.Snapshot.Filter.NoteSearch, "屏蔽提醒来源的筛选词", StringComparison.Ordinal) &&
                string.Equals(noteSearch.Text, "屏蔽提醒来源的筛选词", StringComparison.Ordinal),
                $"cdDraft='{countdownNameDraft.Text}', noteDraft='{noteTextDraft.Text}', search='{noteSearch.Text}'");
        }
        finally
        {
            coordinator?.Dispose();
            trayController?.Dispose();
            CloseWindow(floating);
            CloseWindow(main);
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }

        return FormatResult(meaning, failures, observations);
    }

    private static FirstFrameObservation<T> ObserveFirstVisibleFrame<T>(
        MainWindow main,
        Action triggerShow,
        Func<T> capture) where T : class
    {
        T? atVisibleChanged = null;
        T? atFirstFrame = null;

        DependencyPropertyChangedEventHandler visibleHandler = (_, args) =>
        {
            if (args.NewValue is true && atVisibleChanged is null)
            {
                atVisibleChanged = capture();
            }
        };
        EventHandler renderingHandler = (_, _) =>
        {
            if (main.IsVisible && atFirstFrame is null)
            {
                atFirstFrame = capture();
            }
        };

        main.IsVisibleChanged += visibleHandler;
        CompositionTarget.Rendering += renderingHandler;
        try
        {
            triggerShow();
            WindowValidationInput.PumpRenderedFrames(
                main.Dispatcher,
                timeoutMilliseconds: 500,
                () => atFirstFrame is not null);
            SettleLayout(main);
        }
        finally
        {
            main.IsVisibleChanged -= visibleHandler;
            CompositionTarget.Rendering -= renderingHandler;
        }

        return new FirstFrameObservation<T>(atVisibleChanged, atFirstFrame);
    }

    private static MainWindow CreateHiddenOffscreenMainWindow(
        DesktopWorkspace workspace,
        HolidayCalendar calendar,
        DiagnosticLogger logger) =>
        new(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };

    private static FloatingWindow CreateOffscreenFloatingWindow(
        DesktopWorkspace workspace,
        HolidayCalendar calendar,
        IManagerWorkspaceHost manager) =>
        new(workspace, calendar, manager, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };

    private static ReminderCenterWindow CreateOffscreenReminderCenter(
        Window? owner,
        DiagnosticLogger logger) =>
        new(owner, logger)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };

    private static TagRecord CreateTag(
        DesktopWorkspace workspace,
        string name,
        string color,
        Dispatcher dispatcher)
    {
        var result = workspace.Apply(new CreateTagChange(name, color));
        if (!result.Succeeded || result.CreatedTag is null)
        {
            throw new InvalidOperationException($"创建测试标签失败：{result.Error}");
        }

        WindowValidationInput.Pump(dispatcher);
        return result.CreatedTag;
    }

    private static void SwitchWorkspace(MainWindow main, WorkspaceKind workspace)
    {
        var tab = Required<ToggleButton>(main, workspace == WorkspaceKind.Notes ? "NotesTab" : "CountdownTab");
        tab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
    }

    private static void SettleFloating(FloatingWindow floating)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.UpdateLayout();
        }
    }

    private static void WaitDuration(Dispatcher dispatcher, int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        EventHandler? onTick = null;
        onTick = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= onTick;
            frame.Continue = false;
        };
        timer.Tick += onTick;
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static T Required<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T
        ?? throw new InvalidOperationException($"找不到控件 {name}（期望 {typeof(T).Name}）。");

    private static UIElement[] CaptureChildren(Panel panel) =>
        panel.Children.OfType<UIElement>().ToArray();

    private static Border[] CaptureNoteCards(Panel rowsPanel) =>
        VisualDescendants<Border>(rowsPanel)
            .Where(border => string.Equals(border.Uid, "SelectableNoteRow", StringComparison.Ordinal))
            .ToArray();

    private static string[] ReadNoteCardTexts(Panel rowsPanel) =>
        VisualDescendants<TextBlock>(rowsPanel)
            .Where(text => string.Equals(text.Uid, "NoteRowBodyText", StringComparison.Ordinal))
            .Select(text => text.Text)
            .ToArray();

    private static string[] ReadCountdownTitles(Panel rowsPanel) =>
        VisualDescendants<TextBlock>(rowsPanel)
            .Where(text => string.Equals(text.Uid, "CountdownRowTitleText", StringComparison.Ordinal))
            .Select(text => text.Text)
            .ToArray();

    private static string[] ReadFloatingNoteTexts(Panel floatingRows) =>
        VisualDescendants<CheckBox>(floatingRows)
            .Select(box => AutomationProperties.GetName(box) ?? string.Empty)
            .Where(name => name.StartsWith("完成便签 ", StringComparison.Ordinal))
            .Select(name => name["完成便签 ".Length..])
            .ToArray();

    private static string[] ReadTagManagerNames(Panel tagManagerRows) =>
        tagManagerRows.Children
            .OfType<Grid>()
            .SelectMany(row => row.Children.OfType<TextBox>())
            .Select(box => box.Text)
            .ToArray();

    private static TagControlsSnapshot CaptureTagControls(
        TagSelector countdownTags,
        TagSelector noteTags,
        WrapPanel filterTagOptions,
        Panel tagManagerRows) =>
        new(
            LogicalDescendants<CheckBox>(countdownTags.Popup.Child).ToArray(),
            LogicalDescendants<CheckBox>(noteTags.Popup.Child).ToArray(),
            filterTagOptions.Children.OfType<UIElement>().ToArray(),
            tagManagerRows.Children.OfType<UIElement>().ToArray());

    private static void VerifyTagControlsPreserved(
        string step,
        TagControlsSnapshot expected,
        TagControlsSnapshot actual,
        List<string> failures)
    {
        Check(
            failures,
            $"{step}不重建倒计时标签选择器控件",
            SameReferences(expected.CountdownPickerItems, actual.CountdownPickerItems),
            $"before={expected.CountdownPickerItems.Length}, after={actual.CountdownPickerItems.Length}");
        Check(
            failures,
            $"{step}不重建便签标签选择器控件",
            SameReferences(expected.NotePickerItems, actual.NotePickerItems),
            $"before={expected.NotePickerItems.Length}, after={actual.NotePickerItems.Length}");
        Check(
            failures,
            $"{step}不重建筛选标签芯片控件",
            SameReferences(expected.FilterChips, actual.FilterChips),
            $"before={expected.FilterChips.Length}, after={actual.FilterChips.Length}");
        Check(
            failures,
            $"{step}不重建标签管理行控件",
            SameReferences(expected.TagManagerRows, actual.TagManagerRows),
            $"before={expected.TagManagerRows.Length}, after={actual.TagManagerRows.Length}");
    }

    private static bool SameReferences<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual) where T : class
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!ReferenceEquals(expected[index], actual[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
            {
                continue;
            }

            if (dependencyObject is T match)
            {
                yield return match;
            }

            foreach (var descendant in LogicalDescendants<T>(dependencyObject))
            {
                yield return descendant;
            }
        }
    }

    private static void CloseWindow(Window? window)
    {
        if (window is null)
        {
            return;
        }

        window.Close();
        WindowValidationInput.Pump(window.Dispatcher);
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string DescribeElement(IInputElement? element) =>
        element is FrameworkElement framework
            ? $"{framework.GetType().Name}(Name='{framework.Name}', Tag='{framework.Tag}')"
            : element?.GetType().Name ?? "<null>";

    private static void Check(List<string> failures, string label, bool condition, string detail)
    {
        if (!condition)
        {
            failures.Add($"{label}（{detail}）");
        }
    }

    private static void Require(bool condition, string message, List<string> failures)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static (bool Passed, string Explanation) FormatResult(
        string meaning,
        List<string> failures,
        List<string> observations) =>
        failures.Count == 0
            ? (true, $"{meaning}；实测：{string.Join(" | ", observations)}")
            : (false, $"{meaning}；失败：{string.Join("；", failures)}；实测：{string.Join(" | ", observations)}");

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow.ToUniversalTime();
    }

    private sealed record SearchRenderObservation(
        string FilterTerm,
        Border[] ActiveCards,
        Border[] CompletedCards);

    private sealed record TagControlsSnapshot(
        CheckBox[] CountdownPickerItems,
        CheckBox[] NotePickerItems,
        UIElement[] FilterChips,
        UIElement[] TagManagerRows)
    {
        public string Describe() =>
            $"CountdownTags={CountdownPickerItems.Length}, NoteTags={NotePickerItems.Length}, FilterTags={FilterChips.Length}, TagManagerRows={TagManagerRows.Length}";
    }

    private sealed record FirstFrameObservation<T>(T? AtVisibleChanged, T? AtFirstFrame) where T : class;

    private sealed record CountdownShowSnapshot(
        UIElement[] Rows,
        string[] Titles,
        int PrecisionIndex,
        string SalaryText);

    private sealed record NoteShowSnapshot(
        Border[] Cards,
        string[] Texts,
        int PrecisionIndex,
        string WorkStartText,
        string WorkEndText);

    private sealed record ReminderSourceShowSnapshot(
        Border[] Cards,
        string[] Texts,
        WorkspaceKind Workspace);
}
