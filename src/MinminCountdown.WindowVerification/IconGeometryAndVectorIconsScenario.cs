using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown.WindowVerification;

// #307：验证全部图标几何收口为 App.xaml 共享冻结 Geometry 资源（同类图标跨窗口/代码复用同一实例、
// 管理窗实心齿轮与浮窗描边齿轮保持独立），以及四处文本字形矢量化后的描边、可访问名、命中区、Tab 与提示合同。
internal static class IconGeometryAndVectorIconsScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 10, 1, 0, 0, TimeSpan.Zero);

    private static readonly string[] SharedIconResourceKeys =
    [
        "IconMinimizeGeometry",
        "IconCloseGeometry",
        "IconDropdownTriangleGeometry",
        "IconCalendarGeometry",
        "IconChevronDownGeometry",
        "IconClockGeometry",
        "IconTagGeometry",
        "IconPencilGeometry",
        "IconCheckmarkGeometry",
        "IconMainGearGeometry",
        "IconFloatingGearOutlineGeometry",
        "IconPlusGeometry",
        "IconHomeGeometry",
        "IconTitleHeartGeometry",
        "IconTitleHouseGeometry",
        "IconFilterFunnelGeometry",
        "IconUpArrowGeometry",
        "IconBellOutlineGeometry",
        "IconBellFilledGeometry",
        "IconStickyNoteGeometry",
        "IconReminderNoteDocumentGeometry",
        "IconSearchGeometry",
        "IconTrashGeometry",
        "IconExternalLinkGeometry"
    ];

    internal static (bool Passed, string Explanation) RunSharedGeometries(string meaning)
    {
        var failures = new List<string>();
        var resources = Application.Current.Resources;
        var resolved = new Dictionary<string, Geometry>(StringComparer.Ordinal);

        foreach (var key in SharedIconResourceKeys)
        {
            if (resources[key] is not Geometry geometry)
            {
                failures.Add($"App.xaml 缺少共享图标几何资源 {key}");
                continue;
            }

            if (!geometry.IsFrozen)
            {
                failures.Add($"共享图标几何资源 {key} 未冻结（IsFrozen=false）");
            }

            resolved[key] = geometry;
        }

        if (resolved.TryGetValue("IconMainGearGeometry", out var mainGearRes) &&
            resolved.TryGetValue("IconFloatingGearOutlineGeometry", out var floatingGearRes))
        {
            Check(
                failures,
                "管理窗实心齿轮与浮窗描边齿轮保留为两份独立几何资源",
                !ReferenceEquals(mainGearRes, floatingGearRes) &&
                !string.Equals(mainGearRes.ToString(), floatingGearRes.ToString(), StringComparison.Ordinal));
        }

        if (resolved.Count == SharedIconResourceKeys.Length)
        {
            VerifySharedGeometriesAcrossWindows(resolved, failures);
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    private static void VerifySharedGeometriesAcrossWindows(
        IReadOnlyDictionary<string, Geometry> expected,
        ICollection<string> failures)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-icons-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        FloatingWindow? floating = null;
        ReminderCenterWindow? reminder = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            Apply(workspace, new CreateTagChange("工作", null), Application.Current.Dispatcher);
            SaveCountdown(workspace, "图标共享倒计时", 3, Application.Current.Dispatcher);
            SaveNoteWithReminder(workspace, "图标共享便签", Application.Current.Dispatcher);

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
            var logger = new DiagnosticLogger(diagnosticPath);

            main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            main.Show();
            SettleLayout(main);

            floating = new FloatingWindow(
                workspace,
                calendar,
                new NoopManagerHost(),
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            SettleLayout(floating);

            reminder = new ReminderCenterWindow();
            reminder.ShowInTaskbar = false;
            reminder.ShowActivated = false;
            reminder.Left = -20_000;
            reminder.Top = -20_000;
            reminder.Opacity = 0;
            var countdown = workspace.Snapshot.Settings.Countdowns[0];
            var note = workspace.Snapshot.Settings.Notes[0];
            var countdownKey = new ActiveReminderOccurrenceKey("r-cd", FixtureUtc.AddMinutes(-5));
            var noteKey = new ActiveReminderOccurrenceKey("r-note", FixtureUtc.AddMinutes(-2));
            reminder.AddEntry(new ReminderCenterItem(
                countdownKey,
                new CountdownReminderSource(countdown),
                0,
                0));
            reminder.AddEntry(new ReminderCenterItem(
                noteKey,
                new NoteReminderSource(note),
                1,
                0));
            reminder.Show();
            SettleLayout(reminder);

            // 1. Minimize (×3: MainWindow, FloatingWindow, ReminderCenter)
            var mainMin = FirstPath(Required<Button>(main, "MinimizeButton"));
            var floatingMin = FirstPath(Required<Button>(floating, "MinimizeToTrayButton"));
            var reminderMinButton = FindButtonByAutomationId(reminder, "ReminderCenterMinimizeToTaskbar");
            var reminderMin = reminderMinButton is null ? null : FirstPath(reminderMinButton);
            CheckShared(failures, "Main.MinimizeButton", mainMin, expected["IconMinimizeGeometry"]);
            CheckShared(failures, "Floating.MinimizeToTrayButton", floatingMin, expected["IconMinimizeGeometry"]);
            CheckShared(failures, "Reminder.Minimize", reminderMin, expected["IconMinimizeGeometry"]);

            // 2. Close (×2: MainWindow, FloatingWindow)
            CheckShared(failures, "Main.CloseButton", FirstPath(Required<Button>(main, "CloseButton")), expected["IconCloseGeometry"]);
            CheckShared(failures, "Floating.CloseFloatingButton", FirstPath(Required<Button>(floating, "CloseFloatingButton")), expected["IconCloseGeometry"]);

            // 3. Dropdown Triangle (×2: ComboBox, DatePicker)
            var expandButton = Required<Button>(main, "CountdownEditorExpandButton");
            expandButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            var combo = Required<ComboBox>(main, "CountdownHour");
            combo.ApplyTemplate();
            var datePicker = Required<DatePicker>(main, "CountdownDate");
            datePicker.ApplyTemplate();
            CheckShared(failures, "ComboBox.DropdownArrow", FirstPath(combo), expected["IconDropdownTriangleGeometry"]);
            CheckShared(failures, "DatePicker.DropdownArrow", FirstPath(datePicker), expected["IconDropdownTriangleGeometry"]);

            // 4. Calendar (×2: CountdownDate leading icon, NoteDate leading icon)
            var countdownDateHost = Required<FrameworkElement>(main, "CountdownDate").Parent as DependencyObject;
            var noteDateHost = Required<FrameworkElement>(main, "NoteDate").Parent as DependencyObject;
            CheckShared(failures, "Main.CountdownDateIcon", DirectChildPath(countdownDateHost), expected["IconCalendarGeometry"]);
            CheckShared(failures, "Main.NoteDateIcon", DirectChildPath(noteDateHost), expected["IconCalendarGeometry"]);

            // 5. Chevron Down (×3: CountdownEditorExpandButton, NoteDateChevron, Reminder Snooze button)
            var expandChevron = FirstPath(Required<Button>(main, "CountdownEditorExpandButton"));
            CheckShared(failures, "Main.CountdownEditorExpandChevron", expandChevron, expected["IconChevronDownGeometry"]);

            var snoozeButton = FindButtonByAutomationId(reminder, "ReminderEntrySnoozeButton");
            var snoozePaths = snoozeButton is null ? [] : VisualDescendants<WpfPath>(snoozeButton).ToArray();
            var reminderBell = snoozePaths.FirstOrDefault();
            var reminderSnoozeChevron = snoozePaths.LastOrDefault();
            CheckShared(failures, "Reminder.SnoozeBell", reminderBell, expected["IconBellFilledGeometry"]);
            CheckShared(failures, "Reminder.SnoozeChevron", reminderSnoozeChevron, expected["IconChevronDownGeometry"]);

            var ackAllButton = FindButtonByAutomationId(reminder, "ReminderCenterAcknowledgeAll");
            CheckShared(failures, "Reminder.AcknowledgeAllBell", ackAllButton is null ? null : FirstPath(ackAllButton), expected["IconBellFilledGeometry"]);

            // 6. Pencil & Trash (CountdownName leading icon + InlineActionButton Edit/Delete)
            var countdownNameHost = Required<FrameworkElement>(main, "CountdownName").Parent as DependencyObject;
            CheckShared(failures, "Main.CountdownNamePencil", DirectChildPath(countdownNameHost), expected["IconPencilGeometry"]);
            var countdownEditButton = VisualDescendants<Button>(Required<FrameworkElement>(main, "CountdownListRegion"))
                .FirstOrDefault(button => string.Equals(button.Content as string, "编辑", StringComparison.Ordinal));
            var countdownDeleteButton = VisualDescendants<Button>(Required<FrameworkElement>(main, "CountdownListRegion"))
                .FirstOrDefault(button => string.Equals(button.Content as string, "删除", StringComparison.Ordinal));
            CheckShared(failures, "Main.CountdownRowEditPencil", countdownEditButton is null ? null : FirstPath(countdownEditButton), expected["IconPencilGeometry"]);
            CheckShared(failures, "Main.CountdownRowDeleteTrash", countdownDeleteButton is null ? null : FirstPath(countdownDeleteButton), expected["IconTrashGeometry"]);

            // 7. Distinct gears (Main solid gear vs Floating stroked outline gear)
            var mainGearPath = FirstPath(Required<Button>(main, "PreferencesButton"));
            var floatingGearPath = FirstPath(Required<Button>(floating, "FloatingOpenSettings"));
            CheckShared(failures, "Main.PreferencesGear", mainGearPath, expected["IconMainGearGeometry"]);
            CheckShared(failures, "Floating.SettingsGearOutline", floatingGearPath, expected["IconFloatingGearOutlineGeometry"]);
            Check(
                failures,
                "主窗齿轮与浮窗齿轮使用不同 Geometry 实例",
                mainGearPath is not null &&
                floatingGearPath is not null &&
                !ReferenceEquals(mainGearPath.Data, floatingGearPath.Data));

            // 8. Title motifs, Filter funnel, Empty guide arrow, Home, Floating plus
            CheckShared(failures, "Main.CountdownTitleMotif", Required<WpfPath>(main, "CountdownTitleMotif"), expected["IconTitleHeartGeometry"]);
            CheckShared(failures, "Main.NotesTitleMotif", Required<WpfPath>(main, "NotesTitleMotif"), expected["IconTitleHouseGeometry"]);
            CheckShared(failures, "Main.FilterFunnel", FirstPath(Required<Button>(main, "FilterButton")), expected["IconFilterFunnelGeometry"]);
            var guideButton = Required<Button>(main, "AddEmptyCountdownButton");
            guideButton.ApplyTemplate();
            CheckShared(failures, "Main.EmptyGuideArrow", FirstPath(guideButton), expected["IconUpArrowGeometry"]);
            CheckShared(failures, "Floating.OpenManagerHome", FirstPath(Required<Button>(floating, "OpenManagerButton")), expected["IconHomeGeometry"]);
            CheckShared(failures, "Floating.AddNoteTogglePlus", FirstPath(Required<Button>(floating, "FloatingAddNoteToggle")), expected["IconPlusGeometry"]);

            // 9. Switch MainWindow to Notes workspace to inspect NoteDateChevron, Clock, Tag (×3), StickyNote, BellOutline, Search, Checkmark (×5)
            new MainWindowEmptyStateProbe(main).ShowWorkspace(WorkspaceKind.Notes);
            SettleLayout(main);

            var noteDateChevron = VisualDescendants<WpfPath>(Required<FrameworkElement>(main, "NotesListRegion"))
                .FirstOrDefault(path => string.Equals(path.Uid, "NoteDateChevron", StringComparison.Ordinal));
            CheckShared(failures, "Main.NoteDateChevron", noteDateChevron, expected["IconChevronDownGeometry"]);

            var noteSummary = VisualDescendants<TextBlock>(Required<FrameworkElement>(main, "NotesListRegion"))
                .FirstOrDefault(text => string.Equals(
                    AutomationProperties.GetAutomationId(text),
                    MainWindow.NoteCardReminderSummaryAutomationId,
                    StringComparison.Ordinal));
            var mainNoteClock = (noteSummary?.Parent as DependencyObject) is { } detailRow ? DirectChildPath(detailRow) : null;
            CheckShared(failures, "Main.NoteReminderClock", mainNoteClock, expected["IconClockGeometry"]);

            var reminderSourceIcons = VisualDescendants<Border>(reminder)
                .Where(border => string.Equals(
                    AutomationProperties.GetAutomationId(border),
                    "ReminderEntrySourceIcon",
                    StringComparison.Ordinal))
                .ToArray();
            var reminderCountdownClock = reminderSourceIcons.Length > 0 ? FirstPath(reminderSourceIcons[0]) : null;
            var reminderNoteDoc = reminderSourceIcons.Length > 1 ? FirstPath(reminderSourceIcons[1]) : null;
            CheckShared(failures, "Reminder.CountdownSourceClock", reminderCountdownClock, expected["IconClockGeometry"]);
            CheckShared(failures, "Reminder.NoteSourceDocument", reminderNoteDoc, expected["IconReminderNoteDocumentGeometry"]);

            var reminderPendingBadge = VisualDescendants<Border>(reminder)
                .FirstOrDefault(border => string.Equals(
                    AutomationProperties.GetAutomationId(border),
                    "ReminderEntryPendingBadge",
                    StringComparison.Ordinal));
            CheckShared(failures, "Reminder.RelativeTimeClock", reminderPendingBadge is null ? null : FirstPath(reminderPendingBadge), expected["IconClockGeometry"]);

            // Tag (×3: TagManagerButton, NoteTagsLabel row, TagSelector trigger)
            CheckShared(failures, "Main.TagManagerIcon", FirstPath(Required<Button>(main, "TagManagerButton")), expected["IconTagGeometry"]);
            var noteTagsLabelHost = Required<FrameworkElement>(main, "NoteTagsLabel").Parent as DependencyObject;
            CheckShared(failures, "Main.NoteTagsLabelIcon", noteTagsLabelHost is null ? null : FirstPath(noteTagsLabelHost), expected["IconTagGeometry"]);
            CheckShared(failures, "Main.NoteTagsSelectorIcon", FirstPath(Required<FrameworkElement>(main, "NoteTags")), expected["IconTagGeometry"]);

            // StickyNote, BellOutline, Search
            var noteTextLabelHost = Required<FrameworkElement>(main, "NoteTextLabel").Parent as DependencyObject;
            CheckShared(failures, "Main.NoteTextLabelIcon", DirectChildPath(noteTextLabelHost), expected["IconStickyNoteGeometry"]);
            var noteReminderLabelHost = Required<FrameworkElement>(main, "NoteReminderModeLabel").Parent as DependencyObject;
            CheckShared(failures, "Main.NoteReminderLabelIcon", DirectChildPath(noteReminderLabelHost), expected["IconBellOutlineGeometry"]);
            var noteSearchHost = Required<FrameworkElement>(main, "NoteSearch").Parent as DependencyObject;
            CheckShared(failures, "Main.NoteSearchIcon", DirectChildPath(noteSearchHost), expected["IconSearchGeometry"]);

            // Checkmarks (×5: TopmostCheck, MainNoteCompletionCheck, FilterOptionButton SelectedTick, implicit CheckBox Tick, Reminder CheckCircle)
            var topmost = Required<CheckBox>(main, "TopmostCheck");
            topmost.ApplyTemplate();
            CheckShared(failures, "Main.TopmostCheckTick", topmost.Template?.FindName("Tick", topmost) as WpfPath, expected["IconCheckmarkGeometry"]);

            var noteCompletion = VisualDescendants<CheckBox>(Required<FrameworkElement>(main, "NotesListRegion")).FirstOrDefault();
            noteCompletion?.ApplyTemplate();
            CheckShared(failures, "Main.NoteCompletionTick", noteCompletion?.Template?.FindName("Tick", noteCompletion) as WpfPath, expected["IconCheckmarkGeometry"]);

            var filterChip = Required<ToggleButton>(main, "NoteAllFilter");
            filterChip.ApplyTemplate();
            CheckShared(failures, "App.FilterOptionSelectedTick", filterChip.Template?.FindName("SelectedTick", filterChip) as WpfPath, expected["IconCheckmarkGeometry"]);

            var floatingNoteCompletion = VisualDescendants<CheckBox>(Required<FrameworkElement>(floating, "FloatingRows")).FirstOrDefault();
            floatingNoteCompletion?.ApplyTemplate();
            CheckShared(failures, "App.CheckBoxTick", floatingNoteCompletion?.Template?.FindName("Tick", floatingNoteCompletion) as WpfPath, expected["IconCheckmarkGeometry"]);

            var ackButton = VisualDescendants<Button>(reminder)
                .FirstOrDefault(button => AutomationProperties.GetName(button).StartsWith("知道了：", StringComparison.Ordinal));
            var openSourceButton = VisualDescendants<Button>(reminder)
                .FirstOrDefault(button => AutomationProperties.GetName(button).StartsWith("打开", StringComparison.Ordinal));
            CheckShared(failures, "Reminder.AcknowledgeCheckmark", ackButton is null ? null : FirstPath(ackButton), expected["IconCheckmarkGeometry"]);
            CheckShared(failures, "Reminder.OpenSourceExternalLink", openSourceButton is null ? null : FirstPath(openSourceButton), expected["IconExternalLinkGeometry"]);

            // 10. Every visible Path across all three windows has a frozen Data geometry
            VerifyAllPathsFrozen(main, "MainWindow", failures);
            VerifyAllPathsFrozen(floating, "FloatingWindow", failures);
            VerifyAllPathsFrozen(reminder, "ReminderCenterWindow", failures);
        }
        finally
        {
            reminder?.ForceClose();
            floating?.Close();
            main?.Close();
            DeleteSettingsFiles(settingsPath);
            TryDeleteFile(diagnosticPath);
        }
    }

    private static void VerifyAllPathsFrozen(DependencyObject root, string scope, ICollection<string> failures)
    {
        foreach (var path in VisualDescendants<WpfPath>(root))
        {
            if (path.Data is null)
            {
                failures.Add($"{scope} 存在 Data 为空的 Path（Name={path.Name}）");
            }
            else if (!path.Data.IsFrozen)
            {
                failures.Add($"{scope} 存在未冻结的 Path.Data（Name={path.Name}）");
            }
        }
    }

    private static void CheckShared(
        ICollection<string> failures,
        string label,
        WpfPath? actualPath,
        Geometry expectedGeometry)
    {
        if (actualPath?.Data is null)
        {
            failures.Add($"{label} 未找到带几何的 Path");
            return;
        }

        if (!ReferenceEquals(actualPath.Data, expectedGeometry))
        {
            failures.Add($"{label} 未复用共享冻结 Geometry 实例");
        }
    }

    private static WpfPath? FirstPath(DependencyObject root) =>
        VisualDescendants<WpfPath>(root).FirstOrDefault();

    private static WpfPath? DirectChildPath(DependencyObject? parent) =>
        parent is Panel panel ? panel.Children.OfType<WpfPath>().FirstOrDefault() : null;

    private static Button? FindButtonByAutomationId(DependencyObject root, string automationId) =>
        VisualDescendants<Button>(root).FirstOrDefault(button =>
            string.Equals(AutomationProperties.GetAutomationId(button), automationId, StringComparison.Ordinal));

    private static void SaveNoteWithReminder(
        DesktopWorkspace workspace,
        string text,
        System.Windows.Threading.Dispatcher dispatcher)
    {
        var due = FixtureUtc.AddDays(1);
        var rules = new ReminderRule[]
        {
            new AbsoluteOnceReminderRule(Guid.NewGuid().ToString("N"), due)
        };
        Apply(
            workspace,
            new SaveNoteChange(
                null,
                text,
                workspace.Snapshot.BeijingNow.Date,
                false,
                Array.Empty<string>(),
                rules),
            dispatcher);
    }

    private static void Check(ICollection<string> failures, string description, bool condition)
    {
        if (!condition)
        {
            failures.Add(description);
        }
    }

    private static void SettleLayout(Window window)
    {
        window.UpdateLayout();
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
    }

    private static T Required<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"未找到控件 {name}。");

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class NoopManagerHost : IManagerWorkspaceHost
    {
        public void Reveal(WorkspaceKind workspace)
        {
        }

        public void RevealPreferences(WorkspaceKind workspace)
        {
        }
    }
}
