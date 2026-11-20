using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #298：在隔离真实管理窗口里复现并守住标签改名（含名称与颜色变化）后四处界面同步：
// 1) 管理窗口倒计时行标签芯片、2) 便签卡标签芯片、3) 筛选标签芯片、4) 标签选择器列表与摘要；
// 同时核对倒计时行其他内容、键盘焦点与非零滚动位置不变。不向产品程序集新增验证钩子。
internal static class TagRenameSyncScenario
{
    private const string InitialTagName = "项目旧名";
    private const string InitialTagColor = "#F4B183";
    private const string ManagerRenamedTagName = "项目新名";
    private const string NotesTabRenamedTagName = "项目便签页改名";
    private const string ColorAndNameUpdatedTagName = "项目双更名";
    private const string UpdatedTagColor = "#B8E0D2";
    private const string DefaultColorTagName = "项目默认色";
    private const string SecondaryTagName = "常规事项";
    private const string SecondaryTagColor = "#E7F4E8";
    private const string TargetCountdownName = "倒计时 10";
    private const string FirstCountdownName = "倒计时 01";
    private const string TargetNoteText = "便签标签同步核对";

    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) Run(string meaning)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-tag-rename-298-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var failures = new List<string>();
        var observations = new List<string>();
        MainWindow? main = null;
        try
        {
            var clock = new FixedClock(FixtureUtc);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
            main = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            main.Show();
            SettleLayout(main);

            var primaryTag = CreateTag(workspace, InitialTagName, InitialTagColor, main.Dispatcher);
            var secondaryTag = CreateTag(workspace, SecondaryTagName, SecondaryTagColor, main.Dispatcher);

            for (var index = 1; index <= 10; index++)
            {
                var name = string.Create(CultureInfo.InvariantCulture, $"倒计时 {index:00}");
                var tagIds = index switch
                {
                    1 => new[] { primaryTag.Id, secondaryTag.Id },
                    10 => new[] { primaryTag.Id },
                    _ => new[] { secondaryTag.Id }
                };
                Apply(workspace, new SaveCountdownChange(
                    null,
                    name,
                    FixtureUtc.AddDays(index),
                    tagIds,
                    Array.Empty<ReminderRule>()), main.Dispatcher);
            }

            Apply(workspace, new SaveNoteChange(
                null,
                TargetNoteText,
                workspace.Snapshot.BeijingNow.Date,
                false,
                new[] { primaryTag.Id },
                Array.Empty<ReminderRule>()), main.Dispatcher);

            var countdownTags = Required<TagSelector>(main, "CountdownTags");
            var noteTags = Required<TagSelector>(main, "NoteTags");
            countdownTags.SetSelection([primaryTag.Id]);
            noteTags.SetSelection([primaryTag.Id]);
            SettleLayout(main);

            // 1. 改名前采样四处真实界面显示。
            SwitchWorkspace(main, WorkspaceKind.Notes);
            var noteBefore = ReadNoteTagChip(main, primaryTag.Id);
            SwitchWorkspace(main, WorkspaceKind.Countdowns);
            var countdownFirstBefore = ReadCountdownTagChip(main, FirstCountdownName, primaryTag.Id);
            var countdownFirstOtherBefore = ReadCountdownTagChip(main, FirstCountdownName, secondaryTag.Id);
            var countdownTargetBefore = ReadCountdownTagChip(main, TargetCountdownName, primaryTag.Id);
            var filterBefore = ReadFilterTagChip(main, primaryTag.Id);
            var countdownPickerBefore = ReadTagPicker(countdownTags, primaryTag.Id);
            var notePickerBefore = ReadTagPicker(noteTags, primaryTag.Id);

            observations.Add(
                $"BEFORE: countdownRow1={countdownFirstBefore}; countdownRow10={countdownTargetBefore}; " +
                $"noteCard={noteBefore}; filter={filterBefore}; " +
                $"countdownPicker={countdownPickerBefore}; notePicker={notePickerBefore}");

            CheckChip(countdownFirstBefore, InitialTagName, InitialTagName, InitialTagColor, "改名前倒计时首行标签", failures);
            CheckChip(countdownFirstOtherBefore, SecondaryTagName, SecondaryTagName, SecondaryTagColor, "改名前倒计时首行辅助标签", failures);
            CheckChip(countdownTargetBefore, InitialTagName, InitialTagName, InitialTagColor, "改名前倒计时末行标签", failures);
            CheckChip(noteBefore, $"# {InitialTagName}", InitialTagName, InitialTagColor, "改名前便签卡标签", failures);
            CheckFilterChip(filterBefore, InitialTagName, "改名前筛选标签", failures);
            CheckPicker(countdownPickerBefore, InitialTagName, "改名前倒计时标签选择器", failures);
            CheckPicker(notePickerBefore, InitialTagName, "改名前便签标签选择器", failures);

            // 2. 在倒计时页通过真实“标签管理”弹层改名并点击保存（不做任何其他操作）。
            var countdownList = Required<ScrollViewer>(main, "CountdownListRegion");
            countdownList.ScrollToEnd();
            SettleLayout(main);
            var scrollBeforeManagerRename = countdownList.VerticalOffset;
            if (scrollBeforeManagerRename <= 0.5)
            {
                failures.Add($"倒计时列表未进入可滚动状态：scrollable={countdownList.ScrollableHeight:0.##}，offset={scrollBeforeManagerRename:0.##}");
            }

            var targetRowSnapshotBeforeManager = ReadCountdownRowContent(main, TargetCountdownName);
            RenameTagInTagManagerUi(main, InitialTagName, ManagerRenamedTagName);

            var countdownFirstAfterManager = ReadCountdownTagChip(main, FirstCountdownName, primaryTag.Id);
            var countdownFirstOtherAfterManager = ReadCountdownTagChip(main, FirstCountdownName, secondaryTag.Id);
            var countdownTargetAfterManager = ReadCountdownTagChip(main, TargetCountdownName, primaryTag.Id);
            var targetRowSnapshotAfterManager = ReadCountdownRowContent(main, TargetCountdownName);
            var scrollAfterManagerRename = countdownList.VerticalOffset;
            var filterAfterManager = ReadFilterTagChip(main, primaryTag.Id);
            var countdownPickerAfterManager = ReadTagPicker(countdownTags, primaryTag.Id);
            var notePickerAfterManager = ReadTagPicker(noteTags, primaryTag.Id);

            // 关闭标签管理弹层并切至便签页检查便签卡（同时稍后也验证在便签页直接改名）。
            CloseTagManagerPopup(main);
            SwitchWorkspace(main, WorkspaceKind.Notes);
            var noteAfterManager = ReadNoteTagChip(main, primaryTag.Id);

            observations.Add(
                $"AFTER_TAG_MANAGER_RENAME: countdownRow1={countdownFirstAfterManager}; countdownRow10={countdownTargetAfterManager}; " +
                $"noteCard={noteAfterManager}; filter={filterAfterManager}; " +
                $"countdownPicker={countdownPickerAfterManager}; notePicker={notePickerAfterManager}; " +
                $"scroll={scrollBeforeManagerRename:0.##}->{scrollAfterManagerRename:0.##}");

            CheckChip(countdownFirstAfterManager, ManagerRenamedTagName, ManagerRenamedTagName, InitialTagColor, "标签管理改名后倒计时首行标签", failures);
            CheckChip(countdownTargetAfterManager, ManagerRenamedTagName, ManagerRenamedTagName, InitialTagColor, "标签管理改名后倒计时末行标签", failures);
            CheckChip(countdownFirstOtherAfterManager, SecondaryTagName, SecondaryTagName, SecondaryTagColor, "标签管理改名后未修改的辅助标签", failures);
            CheckRowContentUnchanged(targetRowSnapshotBeforeManager, targetRowSnapshotAfterManager, "标签管理改名后倒计时行其他内容", failures);
            if (Math.Abs(scrollAfterManagerRename - scrollBeforeManagerRename) > 0.5)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"标签管理改名后倒计时列表滚动位置改变：{scrollBeforeManagerRename:0.##} -> {scrollAfterManagerRename:0.##}"));
            }
            CheckChip(noteAfterManager, $"# {ManagerRenamedTagName}", ManagerRenamedTagName, InitialTagColor, "标签管理改名后便签卡标签", failures);
            CheckFilterChip(filterAfterManager, ManagerRenamedTagName, "标签管理改名后筛选标签", failures);
            CheckPicker(countdownPickerAfterManager, ManagerRenamedTagName, "标签管理改名后倒计时标签选择器", failures);
            CheckPicker(notePickerAfterManager, ManagerRenamedTagName, "标签管理改名后便签标签选择器", failures);

            // 3. 在便签页打开标签管理改名：便签卡当场更新，随后切回倒计时页也应立即看到新名字。
            RenameTagInTagManagerUi(main, ManagerRenamedTagName, NotesTabRenamedTagName);
            var noteImmediateAfterNotesRename = ReadNoteTagChip(main, primaryTag.Id);
            var filterAfterNotesRename = ReadFilterTagChip(main, primaryTag.Id);
            var countdownPickerAfterNotesRename = ReadTagPicker(countdownTags, primaryTag.Id);
            var notePickerAfterNotesRename = ReadTagPicker(noteTags, primaryTag.Id);
            CloseTagManagerPopup(main);
            SwitchWorkspace(main, WorkspaceKind.Countdowns);
            var countdownAfterNotesTabRename = ReadCountdownTagChip(main, FirstCountdownName, primaryTag.Id);

            observations.Add(
                $"AFTER_NOTES_TAB_RENAME: noteCardImmediate={noteImmediateAfterNotesRename}; " +
                $"countdownRowAfterReturn={countdownAfterNotesTabRename}; filter={filterAfterNotesRename}; " +
                $"countdownPicker={countdownPickerAfterNotesRename}; notePicker={notePickerAfterNotesRename}");

            CheckChip(noteImmediateAfterNotesRename, $"# {NotesTabRenamedTagName}", NotesTabRenamedTagName, InitialTagColor, "便签页改名后便签卡当场标签", failures);
            CheckChip(countdownAfterNotesTabRename, NotesTabRenamedTagName, NotesTabRenamedTagName, InitialTagColor, "便签页改名后切回倒计时页标签", failures);
            CheckFilterChip(filterAfterNotesRename, NotesTabRenamedTagName, "便签页改名后筛选标签", failures);
            CheckPicker(countdownPickerAfterNotesRename, NotesTabRenamedTagName, "便签页改名后倒计时标签选择器", failures);
            CheckPicker(notePickerAfterNotesRename, NotesTabRenamedTagName, "便签页改名后便签标签选择器", failures);

            // 4. 在倒计时行持有键盘焦点与非零滚动位置时同时更新名称与颜色（再测回退默认色）。
            var targetRowElementBefore = FindCountdownRowElement(main, TargetCountdownName);
            var targetEditButtonBefore = FindCountdownRowEditButton(targetRowElementBefore, TargetCountdownName);
            main.Opacity = 1;
            main.Activate();
            Keyboard.Focus(targetEditButtonBefore);
            SettleLayout(main);
            countdownList.ScrollToEnd();
            SettleLayout(main);

            var focusBeforeColorRename = Keyboard.FocusedElement;
            var scrollBeforeColorRename = countdownList.VerticalOffset;
            var contentBeforeColorRename = ReadCountdownRowContent(main, TargetCountdownName);
            if (!ReferenceEquals(focusBeforeColorRename, targetEditButtonBefore))
            {
                failures.Add($"颜色与名称同步前未能把键盘焦点置于倒计时行编辑按钮：actual={DescribeElement(focusBeforeColorRename)}");
            }
            if (scrollBeforeColorRename <= 0.5)
            {
                failures.Add($"颜色与名称同步前倒计时列表滚动位置未处于非零偏移：offset={scrollBeforeColorRename:0.##}");
            }

            Apply(workspace, new RenameTagChange(primaryTag.Id, ColorAndNameUpdatedTagName, UpdatedTagColor), main.Dispatcher);
            SettleLayout(main);

            var countdownTargetAfterColorRename = ReadCountdownTagChip(main, TargetCountdownName, primaryTag.Id);
            var countdownFirstAfterColorRename = ReadCountdownTagChip(main, FirstCountdownName, primaryTag.Id);
            var countdownFirstOtherAfterColorRename = ReadCountdownTagChip(main, FirstCountdownName, secondaryTag.Id);
            var contentAfterColorRename = ReadCountdownRowContent(main, TargetCountdownName);
            var targetRowElementAfterColorRename = FindCountdownRowElement(main, TargetCountdownName);
            var focusAfterColorRename = Keyboard.FocusedElement;
            var scrollAfterColorRename = countdownList.VerticalOffset;
            var filterAfterColorRename = ReadFilterTagChip(main, primaryTag.Id);
            var countdownPickerAfterColorRename = ReadTagPicker(countdownTags, primaryTag.Id);
            var notePickerAfterColorRename = ReadTagPicker(noteTags, primaryTag.Id);

            observations.Add(
                $"AFTER_NAME_AND_COLOR_RENAME: countdownRow10={countdownTargetAfterColorRename}; " +
                $"countdownRow1={countdownFirstAfterColorRename}; " +
                $"sameRowInstance={ReferenceEquals(targetRowElementBefore, targetRowElementAfterColorRename)}; " +
                $"focusPreserved={ReferenceEquals(focusAfterColorRename, targetEditButtonBefore)}; " +
                $"scroll={scrollBeforeColorRename:0.##}->{scrollAfterColorRename:0.##}");

            CheckChip(countdownTargetAfterColorRename, ColorAndNameUpdatedTagName, ColorAndNameUpdatedTagName, UpdatedTagColor, "同时改名与改色后倒计时末行标签", failures);
            CheckChip(countdownFirstAfterColorRename, ColorAndNameUpdatedTagName, ColorAndNameUpdatedTagName, UpdatedTagColor, "同时改名与改色后倒计时首行标签", failures);
            CheckChip(countdownFirstOtherAfterColorRename, SecondaryTagName, SecondaryTagName, SecondaryTagColor, "同时改名与改色后辅助标签", failures);
            CheckRowContentUnchanged(contentBeforeColorRename, contentAfterColorRename, "同时改名与改色后倒计时行其他内容", failures);
            if (!ReferenceEquals(targetRowElementBefore, targetRowElementAfterColorRename))
            {
                failures.Add("标签改名与改色重建了倒计时行控件实例，而不是原位更新标签芯片。");
            }
            if (!ReferenceEquals(focusAfterColorRename, targetEditButtonBefore))
            {
                failures.Add($"标签改名与改色丢失或移动了倒计时行键盘焦点：before={DescribeElement(focusBeforeColorRename)}，after={DescribeElement(focusAfterColorRename)}");
            }
            if (Math.Abs(scrollAfterColorRename - scrollBeforeColorRename) > 0.5)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"标签改名与改色改变了倒计时列表滚动位置：{scrollBeforeColorRename:0.##} -> {scrollAfterColorRename:0.##}"));
            }
            CheckFilterChip(filterAfterColorRename, ColorAndNameUpdatedTagName, "同时改名与改色后筛选标签", failures);
            CheckPicker(countdownPickerAfterColorRename, ColorAndNameUpdatedTagName, "同时改名与改色后倒计时标签选择器", failures);
            CheckPicker(notePickerAfterColorRename, ColorAndNameUpdatedTagName, "同时改名与改色后便签标签选择器", failures);

            // 5. 颜色改回 null（默认 TagBrush）也立即同步且保留焦点与滚动位置。
            var expectedDefaultColor = FormatBrush(main.FindResource("TagBrush") as Brush);
            Apply(workspace, new RenameTagChange(primaryTag.Id, DefaultColorTagName, null), main.Dispatcher);
            SettleLayout(main);
            var countdownTargetAfterDefaultColor = ReadCountdownTagChip(main, TargetCountdownName, primaryTag.Id);
            var focusAfterDefaultColor = Keyboard.FocusedElement;
            var scrollAfterDefaultColor = countdownList.VerticalOffset;
            SwitchWorkspace(main, WorkspaceKind.Notes);
            var noteAfterDefaultColor = ReadNoteTagChip(main, primaryTag.Id);

            observations.Add(
                $"AFTER_DEFAULT_COLOR_RENAME: countdownRow10={countdownTargetAfterDefaultColor}; " +
                $"expectedDefaultColor={expectedDefaultColor}; noteCard={noteAfterDefaultColor}; " +
                $"focusPreserved={ReferenceEquals(focusAfterDefaultColor, targetEditButtonBefore)}; " +
                $"scroll={scrollBeforeColorRename:0.##}->{scrollAfterDefaultColor:0.##}");

            CheckChip(countdownTargetAfterDefaultColor, DefaultColorTagName, DefaultColorTagName, expectedDefaultColor, "改回默认色后倒计时末行标签", failures);
            CheckChip(noteAfterDefaultColor, $"# {DefaultColorTagName}", DefaultColorTagName, expectedDefaultColor, "改回默认色后便签卡标签", failures);
            if (!ReferenceEquals(focusAfterDefaultColor, targetEditButtonBefore))
            {
                failures.Add($"标签改回默认色丢失或移动了倒计时行键盘焦点：after={DescribeElement(focusAfterDefaultColor)}");
            }
            if (Math.Abs(scrollAfterDefaultColor - scrollBeforeColorRename) > 0.5)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"标签改回默认色改变了倒计时列表滚动位置：{scrollBeforeColorRename:0.##} -> {scrollAfterDefaultColor:0.##}"));
            }

            Console.Error.WriteLine($"TAG_RENAME_298_PROBE: {string.Join(" | ", observations)}");
            return failures.Count == 0
                ? (true, meaning)
                : (false, $"{meaning}；失败 {failures.Count} 项：{string.Join("；", failures)}；观测：{string.Join(" | ", observations)}");
        }
        finally
        {
            if (main is not null)
            {
                main.Close();
                WindowValidationInput.Pump(main.Dispatcher);
            }
            DeleteSettingsFiles(settingsPath);
            if (File.Exists(diagnosticPath))
            {
                File.Delete(diagnosticPath);
            }
        }
    }

    private static TagRecord CreateTag(DesktopWorkspace workspace, string name, string color, System.Windows.Threading.Dispatcher dispatcher)
    {
        Apply(workspace, new CreateTagChange(name, color), dispatcher);
        return workspace.Snapshot.Settings.Tags.Single(tag => string.Equals(tag.Name, name, StringComparison.Ordinal));
    }

    private static void SwitchWorkspace(MainWindow main, WorkspaceKind workspace)
    {
        var tabName = workspace == WorkspaceKind.Countdowns ? "CountdownTab" : "NotesTab";
        Required<ButtonBase>(main, tabName).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
    }

    private static void RenameTagInTagManagerUi(MainWindow main, string currentName, string newName)
    {
        var popup = Required<Popup>(main, "TagManagerPopup");
        if (!popup.IsOpen)
        {
            Required<ButtonBase>(main, "TagManagerButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
        }

        var rows = Required<Panel>(main, "TagManagerRows");
        var input = LogicalDescendants<TextBox>(rows)
            .Single(box => string.Equals(AutomationProperties.GetName(box), $"标签名称 {currentName}", StringComparison.Ordinal));
        var save = LogicalDescendants<Button>(rows)
            .Single(button => string.Equals(AutomationProperties.GetName(button), $"保存标签 {currentName}", StringComparison.Ordinal));
        input.Text = newName;
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
    }

    private static void CloseTagManagerPopup(MainWindow main)
    {
        var popup = Required<Popup>(main, "TagManagerPopup");
        if (popup.IsOpen)
        {
            PopupMotionBehavior.Close(popup, immediate: true);
            SettleLayout(main);
        }
    }

    private static FrameworkElement FindCountdownRowElement(MainWindow main, string countdownName)
    {
        var rows = Required<Panel>(main, "CountdownRows");
        return rows.Children
            .OfType<FrameworkElement>()
            .Single(row => VisualDescendants<TextBlock>(row).Any(text =>
                string.Equals(text.Uid, "CountdownRowTitleText", StringComparison.Ordinal) &&
                string.Equals(text.Text, countdownName, StringComparison.Ordinal)));
    }

    private static Button FindCountdownRowEditButton(FrameworkElement row, string countdownName) =>
        VisualDescendants<Button>(row).Single(button =>
            string.Equals(AutomationProperties.GetName(button), $"编辑倒计时 {countdownName}", StringComparison.Ordinal));

    private static CountdownRowContentSnapshot ReadCountdownRowContent(MainWindow main, string countdownName)
    {
        var row = FindCountdownRowElement(main, countdownName);
        var title = VisualDescendants<TextBlock>(row)
            .Single(text => string.Equals(text.Uid, "CountdownRowTitleText", StringComparison.Ordinal))
            .Text;
        var deadline = VisualDescendants<TextBlock>(row)
            .Single(text => string.Equals(text.Uid, "CountdownRowDeadlineText", StringComparison.Ordinal))
            .Text;
        var remaining = VisualDescendants<TextBlock>(row)
            .Single(text => text.Opacity == 0 && !text.IsHitTestVisible)
            .Text;
        return new CountdownRowContentSnapshot(title, deadline, remaining);
    }

    private static TagChipSnapshot ReadCountdownTagChip(MainWindow main, string countdownName, string tagId)
    {
        var row = FindCountdownRowElement(main, countdownName);
        var band = VisualDescendants<Grid>(row)
            .Single(grid => string.Equals(grid.Uid, "CountdownRowAuxiliaryBand", StringComparison.Ordinal));
        var chip = VisualDescendants<Border>(band)
            .Single(border => string.Equals(border.Uid, tagId, StringComparison.OrdinalIgnoreCase));
        var text = chip.Child as TextBlock
            ?? throw new InvalidOperationException($"倒计时行标签芯片 {tagId} 缺少 TextBlock。");
        return new TagChipSnapshot(text.Text, chip.ToolTip as string ?? string.Empty, FormatBrush(chip.Background));
    }

    private static TagChipSnapshot ReadNoteTagChip(MainWindow main, string tagId)
    {
        var rows = Required<Panel>(main, "ActiveNoteRows");
        var chip = VisualDescendants<Border>(rows)
            .Single(border => string.Equals(border.Uid, tagId, StringComparison.OrdinalIgnoreCase));
        var text = chip.Child as TextBlock
            ?? throw new InvalidOperationException($"便签卡标签芯片 {tagId} 缺少 TextBlock。");
        return new TagChipSnapshot(text.Text, chip.ToolTip as string ?? string.Empty, FormatBrush(chip.Background));
    }

    private static FilterTagSnapshot ReadFilterTagChip(MainWindow main, string tagId)
    {
        var options = Required<Panel>(main, "FilterTagOptions");
        var toggle = options.Children
            .OfType<ToggleButton>()
            .Single(button => string.Equals(button.Uid, tagId, StringComparison.OrdinalIgnoreCase));
        return new FilterTagSnapshot(toggle.Content as string ?? string.Empty, AutomationProperties.GetName(toggle));
    }

    private static TagPickerSnapshot ReadTagPicker(TagSelector selector, string tagId)
    {
        var checkBox = LogicalDescendants<CheckBox>(selector.Popup.Child)
            .Single(item => string.Equals(item.Uid, tagId, StringComparison.OrdinalIgnoreCase));
        return new TagPickerSnapshot(
            selector.SummaryText,
            AutomationProperties.GetName(selector.PopupTrigger),
            checkBox.Content as string ?? string.Empty,
            AutomationProperties.GetName(checkBox));
    }

    private static void CheckChip(
        TagChipSnapshot actual,
        string expectedText,
        string expectedToolTip,
        string expectedColor,
        string label,
        List<string> failures)
    {
        var normalizedExpectedColor = NormalizeHexColor(expectedColor);
        if (!string.Equals(actual.Text, expectedText, StringComparison.Ordinal) ||
            !string.Equals(actual.ToolTip, expectedToolTip, StringComparison.Ordinal) ||
            !string.Equals(actual.Color, normalizedExpectedColor, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{label}不匹配：expected=({expectedText}, tooltip={expectedToolTip}, color={normalizedExpectedColor})，actual={actual}");
        }
    }

    private static void CheckFilterChip(FilterTagSnapshot actual, string expectedName, string label, List<string> failures)
    {
        var expectedAccessibleName = $"筛选标签 {expectedName}";
        if (!string.Equals(actual.Content, expectedName, StringComparison.Ordinal) ||
            !string.Equals(actual.AccessibleName, expectedAccessibleName, StringComparison.Ordinal))
        {
            failures.Add(
                $"{label}不匹配：expected=({expectedName}, name={expectedAccessibleName})，actual={actual}");
        }
    }

    private static void CheckPicker(TagPickerSnapshot actual, string expectedName, string label, List<string> failures)
    {
        var expectedTriggerName = $"已选标签：{expectedName}";
        var expectedItemName = $"标签 {expectedName}";
        if (!string.Equals(actual.Summary, expectedName, StringComparison.Ordinal) ||
            !string.Equals(actual.TriggerAccessibleName, expectedTriggerName, StringComparison.Ordinal) ||
            !string.Equals(actual.ItemContent, expectedName, StringComparison.Ordinal) ||
            !string.Equals(actual.ItemAccessibleName, expectedItemName, StringComparison.Ordinal))
        {
            failures.Add(
                $"{label}不匹配：expected=(summary={expectedName}, trigger={expectedTriggerName}, item={expectedName}, itemName={expectedItemName})，actual={actual}");
        }
    }

    private static void CheckRowContentUnchanged(
        CountdownRowContentSnapshot before,
        CountdownRowContentSnapshot after,
        string label,
        List<string> failures)
    {
        if (before != after)
        {
            failures.Add($"{label}发生变化：before={before}，after={after}");
        }
    }

    private static string NormalizeHexColor(string hex) =>
        ((Color)ColorConverter.ConvertFromString(hex)).ToString(CultureInfo.InvariantCulture);

    private static string FormatBrush(Brush? brush) =>
        brush is SolidColorBrush solid
            ? solid.Color.ToString(CultureInfo.InvariantCulture)
            : brush?.GetType().Name ?? "null";

    private static string DescribeElement(IInputElement? element) =>
        element switch
        {
            null => "null",
            FrameworkElement fe => $"{fe.GetType().Name}(Name={fe.Name}, AutomationName={AutomationProperties.GetName(fe)})",
            _ => element.GetType().Name
        };

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T match)
            {
                yield return match;
            }
            if (child is DependencyObject dependencyChild)
            {
                foreach (var descendant in LogicalDescendants<T>(dependencyChild))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static T Required<T>(MainWindow main, string name) where T : class =>
        main.FindName(name) as T
        ?? throw new InvalidOperationException($"标签改名验证窗口缺少控件 {name}（{typeof(T).Name}）");

    private sealed record TagChipSnapshot(string Text, string ToolTip, string Color)
    {
        public override string ToString() => $"text='{Text}',tooltip='{ToolTip}',color={Color}";
    }

    private sealed record FilterTagSnapshot(string Content, string AccessibleName)
    {
        public override string ToString() => $"content='{Content}',a11y='{AccessibleName}'";
    }

    private sealed record TagPickerSnapshot(
        string Summary,
        string TriggerAccessibleName,
        string ItemContent,
        string ItemAccessibleName)
    {
        public override string ToString() =>
            $"summary='{Summary}',triggerA11y='{TriggerAccessibleName}',item='{ItemContent}',itemA11y='{ItemAccessibleName}'";
    }

    private sealed record CountdownRowContentSnapshot(string Title, string Deadline, string Remaining)
    {
        public override string ToString() => $"title='{Title}',deadline='{Deadline}',remaining='{Remaining}'";
    }
}
