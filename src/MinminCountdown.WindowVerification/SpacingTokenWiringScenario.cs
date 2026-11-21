using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #311：验证四个间距 token（SpacingUnit/SpacingFieldRow/SpacingBlockGap/SpacingCardPadding）
// 在主窗口、悬浮窗、提醒中心、规则编辑器与共享 TagSelector 上真正按语义消费资源值，
// 而数值碰巧相同但语义不同的字面量保持不变。
internal static class SpacingTokenWiringScenario
{
    private const double Tolerance = 0.01;
    private const double DefaultUnit = 8;
    private const double DefaultFieldRow = 10;
    private const double DefaultBlockGap = 12;
    private const double DefaultCardPadding = 14;
    private const double ProbeUnit = 17;
    private const double ProbeFieldRow = 19;
    private const double ProbeBlockGap = 23;
    private const double ProbeCardPadding = 25;
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) Run(string meaning)
    {
        var failures = new List<string>();
        var resources = Application.Current.Resources;
        var originalUnit = resources["SpacingUnit"];
        var originalFieldRow = resources["SpacingFieldRow"];
        var originalBlockGap = resources["SpacingBlockGap"];
        var originalCardPadding = resources["SpacingCardPadding"];

        CheckValue("App.xaml 默认 SpacingUnit", originalUnit as double? ?? double.NaN, DefaultUnit, failures);
        CheckValue("App.xaml 默认 SpacingFieldRow", originalFieldRow as double? ?? double.NaN, DefaultFieldRow, failures);
        CheckValue("App.xaml 默认 SpacingBlockGap", originalBlockGap as double? ?? double.NaN, DefaultBlockGap, failures);
        CheckValue("App.xaml 默认 SpacingCardPadding", originalCardPadding as double? ?? double.NaN, DefaultCardPadding, failures);

        try
        {
            resources["SpacingUnit"] = ProbeUnit;
            resources["SpacingFieldRow"] = ProbeFieldRow;
            resources["SpacingBlockGap"] = ProbeBlockGap;
            resources["SpacingCardPadding"] = ProbeCardPadding;

            VerifyMainWindow(failures);
            VerifyFloatingWindow(failures);
            VerifyReminderCenterWindow(failures);
            VerifyReminderRulesEditorWindow(failures);
            VerifyTagSelector(failures);
        }
        finally
        {
            resources["SpacingUnit"] = originalUnit;
            resources["SpacingFieldRow"] = originalFieldRow;
            resources["SpacingBlockGap"] = originalBlockGap;
            resources["SpacingCardPadding"] = originalCardPadding;
        }

        // 还原默认 token 值后再次构造主窗与悬浮窗确认默认值不变。
        VerifyWindowDefaults(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    private static void VerifyMainWindow(ICollection<string> failures)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-spacing-main-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            Apply(workspace, new CreateTagChange("工作", null), Application.Current.Dispatcher);
            SaveCountdown(workspace, "间距验收倒计时", 3, Application.Current.Dispatcher);
            SaveNote(workspace, "间距验收便签正文", Application.Current.Dispatcher);

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
            main.Show();
            SettleLayout(main);
            new MainWindowEmptyStateProbe(main).ShowWorkspace(WorkspaceKind.Notes);
            SettleLayout(main);

            // SpacingFieldRow (10 -> 19)
            CheckValue("Main.CountdownEditorHeading.Margin.Bottom", Required<FrameworkElement>(main, "CountdownEditorHeading").Margin.Bottom, ProbeFieldRow, failures);
            CheckValue("Main.MainCountdownTimeTagRow.Margin.Top", Required<FrameworkElement>(main, "MainCountdownTimeTagRow").Margin.Top, ProbeFieldRow, failures);
            CheckValue("Main.MainCountdownActionRow.Margin.Top", Required<FrameworkElement>(main, "MainCountdownActionRow").Margin.Top, ProbeFieldRow, failures);

            // SpacingBlockGap (12 -> 23)
            var countdownBody = Required<Grid>(main, "CountdownWorkspaceBody");
            CheckValue("Main.CountdownWorkspaceBody.Margin.Top", countdownBody.Margin.Top, ProbeBlockGap, failures);
            CheckValue("Main.CountdownWorkspaceBody.Margin.Left", countdownBody.Margin.Left, 28, failures);
            CheckValue("Main.CountdownWorkspaceBody.Margin.Right", countdownBody.Margin.Right, 29, failures);
            CheckValue("Main.CountdownWorkspaceBody.Row2.Height", countdownBody.RowDefinitions[2].Height.Value, ProbeBlockGap, failures);

            var notesBody = Required<Grid>(main, "NotesWorkspaceBody");
            CheckValue("Main.NotesWorkspaceBody.Margin.Top", notesBody.Margin.Top, ProbeBlockGap, failures);
            CheckValue("Main.NotesWorkspaceBody.Margin.Left", notesBody.Margin.Left, 34, failures);
            CheckValue("Main.NotesWorkspaceBody.Margin.Right", notesBody.Margin.Right, 35, failures);
            CheckValue("Main.NotesWorkspaceBody.Row2.Height", notesBody.RowDefinitions[2].Height.Value, ProbeBlockGap, failures);

            // SpacingUnit (8 -> 17)
            foreach (var errorName in new[]
                     {
                         "CountdownNameError",
                         "CountdownDateError",
                         "CountdownTimeError",
                         "CountdownMessage",
                         "NoteTextError",
                         "NoteDateError",
                         "MonthlySalaryError",
                         "WorkStartError",
                         "WorkEndError"
                     })
            {
                CheckValue($"Main.{errorName}.Margin.Top", Required<FrameworkElement>(main, errorName).Margin.Top, ProbeUnit, failures);
            }

            var commandBar = Required<Grid>(main, "MainCommandBarZone");
            CheckValue("Main.MainCommandBarZone.Col3.Width", commandBar.ColumnDefinitions[3].Width.Value, ProbeUnit, failures);
            CheckValue("Main.PreferencesButton.Margin.Right", Required<FrameworkElement>(main, "PreferencesButton").Margin.Right, ProbeUnit, failures);
            CheckValue("Main.MainNotePrimaryInputs.Col3.Width", Required<Grid>(main, "MainNotePrimaryInputs").ColumnDefinitions[3].Width.Value, ProbeUnit, failures);
            CheckValue("Main.NoteSearchLabel.Margin.Right", Required<FrameworkElement>(main, "NoteSearchLabel").Margin.Right, ProbeUnit, failures);

            var notesListGrid = Required<ScrollViewer>(main, "NotesListRegion").Content as Grid
                ?? throw new InvalidOperationException("NotesListRegion 缺少内容 Grid。");
            CheckValue("Main.NotesListRegion.Content.Margin.Top", notesListGrid.Margin.Top, ProbeUnit, failures);
            var notesEmpty = Required<FrameworkElement>(main, "NotesEmptyStateContent");
            CheckValue("Main.NotesEmptyStateContent.Margin.Bottom", notesEmpty.Margin.Bottom, ProbeUnit, failures);
            CheckValue("Main.NotesEmptyStateContent.Margin.Left", notesEmpty.Margin.Left, 15, failures);
            CheckValue("Main.NotesEmptyStateContent.Margin.Right", notesEmpty.Margin.Right, 15, failures);

            var untagged = Required<ToggleButton>(main, "UntaggedOnly");
            CheckValue("Main.UntaggedOnly.Margin.Right", untagged.Margin.Right, ProbeUnit, failures);
            CheckValue("Main.UntaggedOnly.Margin.Bottom", untagged.Margin.Bottom, ProbeUnit, failures);
            var dynamicTagOption = Required<WrapPanel>(main, "FilterTagOptions").Children
                .OfType<ToggleButton>()
                .FirstOrDefault(toggle => !ReferenceEquals(toggle, untagged));
            if (dynamicTagOption is null)
            {
                failures.Add("Main.FilterTagOptions 未生成动态标签芯片");
            }
            else
            {
                CheckValue("Main.FilterTagOption.Margin.Right", dynamicTagOption.Margin.Right, ProbeUnit, failures);
                CheckValue("Main.FilterTagOption.Margin.Bottom", dynamicTagOption.Margin.Bottom, ProbeUnit, failures);
            }

            foreach (var leftUnitName in new[]
                     {
                         "AllTagMode",
                         "CountdownActiveFilter",
                         "CountdownExpiredFilter",
                         "NoteActiveFilter",
                         "NoteCompletedFilter",
                         "NoteUndatedFilter",
                         "ClearNoteDateFilter"
                     })
            {
                CheckValue($"Main.{leftUnitName}.Margin.Left", Required<FrameworkElement>(main, leftUnitName).Margin.Left, ProbeUnit, failures);
            }
            CheckValue("Main.NoteDateFilterLabel.Margin.Right", Required<FrameworkElement>(main, "NoteDateFilterLabel").Margin.Right, ProbeUnit, failures);

            // 动态便签列表：日期章节（SpacingBlockGap）、便签卡（SpacingCardPadding / SpacingUnit）
            var activeNoteRows = Required<Panel>(main, "ActiveNoteRows");
            var dateSection = VisualDescendants<Grid>(activeNoteRows)
                .FirstOrDefault(grid => string.Equals(grid.Uid, "NoteDateTimelineSection", StringComparison.Ordinal));
            if (dateSection is null)
            {
                failures.Add("Main.ActiveNoteRows 未生成 NoteDateTimelineSection");
            }
            else
            {
                CheckValue("Main.NoteDateTimelineSection.Margin.Bottom", dateSection.Margin.Bottom, ProbeBlockGap, failures);
            }

            var noteCard = VisualDescendants<Border>(activeNoteRows)
                .FirstOrDefault(border => string.Equals(border.Uid, "SelectableNoteRow", StringComparison.Ordinal));
            if (noteCard is null)
            {
                failures.Add("Main.ActiveNoteRows 未生成 SelectableNoteRow");
            }
            else
            {
                CheckValue("Main.NoteCard.Padding.Left", noteCard.Padding.Left, ProbeCardPadding, failures);
                CheckValue("Main.NoteCard.Margin.Bottom", noteCard.Margin.Bottom, ProbeUnit, failures);
            }

            // 碰巧同值但不同义的字面量不得随 token 变化
            CheckValue("Main.TopmostCheck.Margin.Top(excluded)", Required<FrameworkElement>(main, "TopmostCheck").Margin.Top, 8, failures);
            CheckValue("Main.NotesTitleMotif.Margin.Left(excluded)", Required<FrameworkElement>(main, "NotesTitleMotif").Margin.Left, 10, failures);
            CheckValue("Main.MainCommandBarZone.Col5.Width(excluded)", commandBar.ColumnDefinitions[5].Width.Value, 12, failures);
            CheckValue("Main.AddEmptyCountdownButton.Margin.Top(excluded)", Required<FrameworkElement>(main, "AddEmptyCountdownButton").Margin.Top, 10, failures);
            CheckValue("Main.AddEmptyNoteButton.Margin.Top(excluded)", Required<FrameworkElement>(main, "AddEmptyNoteButton").Margin.Top, 10, failures);
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

    private static void VerifyFloatingWindow(ICollection<string> failures)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-spacing-floating-{Guid.NewGuid():N}.settings.json");
        FloatingWindow? floating = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            SaveNote(workspace, "悬浮间距验收便签", Application.Current.Dispatcher);
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            floating = new FloatingWindow(
                workspace,
                calendar,
                new NoopManagerHost(),
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.UpdateLayout();

            // SpacingUnit (8 -> 17)
            CheckValue("Floating.FloatingNotesButton.Margin.Left", Required<FrameworkElement>(floating, "FloatingNotesButton").Margin.Left, ProbeUnit, failures);
            CheckValue("Floating.FloatingNoteSearchHintClear.Margin.Left", Required<FrameworkElement>(floating, "FloatingNoteSearchHintClear").Margin.Left, ProbeUnit, failures);
            CheckValue("Floating.FloatingManageReminderRules.Margin.Left", Required<FrameworkElement>(floating, "FloatingManageReminderRules").Margin.Left, ProbeUnit, failures);
            CheckValue("Floating.FloatingAddNote.Margin.Left", Required<FrameworkElement>(floating, "FloatingAddNote").Margin.Left, ProbeUnit, failures);
            CheckValue("Floating.FloatingDeleteUndoText.Margin.Right", Required<FrameworkElement>(floating, "FloatingDeleteUndoText").Margin.Right, ProbeUnit, failures);
            CheckValue("Floating.FloatingSearchNotes.Margin.Left", Required<FrameworkElement>(floating, "FloatingSearchNotes").Margin.Left, ProbeUnit, failures);
            CheckValue("Floating.FloatingOpenSettings.Margin.Left", Required<FrameworkElement>(floating, "FloatingOpenSettings").Margin.Left, ProbeUnit, failures);

            var quickAddDateRow = Required<FrameworkElement>(floating, "FloatingNoteDate").Parent as Grid
                ?? throw new InvalidOperationException("FloatingNoteDate 缺少父 Grid。");
            CheckValue("Floating.QuickAddDateRow.Margin.Top", quickAddDateRow.Margin.Top, ProbeUnit, failures);

            var rows = Required<Panel>(floating, "FloatingRows");
            var noteCard = rows.Children.OfType<Border>().FirstOrDefault(border => Math.Abs(border.Width - 319) <= 0.5);
            if (noteCard is null)
            {
                failures.Add("Floating.FloatingRows 未生成便签卡片");
            }
            else
            {
                CheckValue("Floating.NoteCard.Margin.Bottom", noteCard.Margin.Bottom, ProbeUnit, failures);
            }

            // SpacingBlockGap (12 -> 23)
            var trailingPadding = rows.Children.OfType<FrameworkElement>().LastOrDefault();
            if (trailingPadding is null || trailingPadding is Border)
            {
                failures.Add("Floating.FloatingRows 缺少尾随底部内边距元素");
            }
            else
            {
                CheckValue("Floating.NotesListBottomPadding.Height", trailingPadding.Height, ProbeBlockGap, failures);
            }

            // 碰巧同值但不同义的字面量不得随 token 变化
            CheckValue("Floating.FloatingAddNoteToggle.Padding.Left(excluded)", Required<Control>(floating, "FloatingAddNoteToggle").Padding.Left, 14, failures);
            CheckValue("Floating.FloatingHeaderSurface.Padding.Top(excluded)", Required<Border>(floating, "FloatingHeaderSurface").Padding.Top, 8, failures);
            CheckValue("Floating.FloatingNoteEntry.Padding.Left(excluded)", Required<Border>(floating, "FloatingNoteEntry").Padding.Left, 10, failures);
            CheckValue("Floating.FloatingOpenSettings.Padding.Left(excluded)", Required<Control>(floating, "FloatingOpenSettings").Padding.Left, 12, failures);
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

    private static void VerifyReminderCenterWindow(ICollection<string> failures)
    {
        var center = new ReminderCenterWindow
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            var key = new ActiveReminderOccurrenceKey("spacing-rule", FixtureUtc.AddMinutes(-3));
            center.AddEntry(new ReminderCenterItem(
                key,
                new NoteRecord(
                    "spacing-note",
                    "提醒中心间距验收便签",
                    "2038-07-08",
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()),
                0,
                0));
            center.Show();
            WindowValidationInput.Pump(center.Dispatcher);
            center.UpdateLayout();

            var minimize = FindByAutomationId<Button>(center, "ReminderCenterMinimizeToTaskbar");
            CheckValue("ReminderCenter.MinimizeButton.Margin.Left", minimize.Margin.Left, ProbeUnit, failures);

            var entryCard = VisualDescendants<Border>(center)
                .FirstOrDefault(border => border.Tag is ActiveReminderOccurrenceKey);
            if (entryCard is null)
            {
                failures.Add("ReminderCenter 未找到条目卡片 Border");
            }
            else
            {
                CheckValue("ReminderCenter.EntryCard.Padding.Left", entryCard.Padding.Left, ProbeCardPadding, failures);
                CheckValue("ReminderCenter.EntryCard.Margin.Bottom", entryCard.Margin.Bottom, ProbeUnit, failures);

                var divider = VisualDescendants<Border>(entryCard)
                    .FirstOrDefault(border => Math.Abs(border.Height - 1) <= Tolerance);
                if (divider is null)
                {
                    failures.Add("ReminderCenter 条目卡缺少分隔线 Border");
                }
                else
                {
                    CheckValue("ReminderCenter.EntryDivider.Margin.Top", divider.Margin.Top, ProbeBlockGap, failures);
                }
            }

            var deliveryError = FindByAutomationId<TextBlock>(center, "ReminderEntryDeliveryError");
            CheckValue("ReminderCenter.DeliveryError.Margin.Top", deliveryError.Margin.Top, ProbeUnit, failures);
            var secondaryRow = FindByAutomationId<WrapPanel>(center, "ReminderEntryActionsSecondaryRow");
            CheckValue("ReminderCenter.SecondaryActions.Margin.Top", secondaryRow.Margin.Top, ProbeUnit, failures);

            // 碰巧同值但不同义的字面量不得随 token 变化
            var root = FindByAutomationId<Grid>(center, "ReminderCenterContentRoot");
            CheckValue("ReminderCenter.ContentRoot.Margin.Left(excluded)", root.Margin.Left, 14, failures);
            var header = FindByAutomationId<Border>(center, "ReminderCenterDragHeader");
            CheckValue("ReminderCenter.DragHeader.Margin.Bottom(excluded)", header.Margin.Bottom, 14, failures);
        }
        finally
        {
            center.Close();
            WindowValidationInput.Pump(center.Dispatcher);
        }
    }

    private static void VerifyReminderRulesEditorWindow(ICollection<string> failures)
    {
        var editor = new ReminderRulesEditorWindow(
            new ReminderRule[]
            {
                new AbsoluteOnceReminderRule("rule-1", FixtureUtc.AddHours(2))
            },
            new FixedClock(FixtureUtc))
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            editor.Show();
            WindowValidationInput.Pump(editor.Dispatcher);
            editor.UpdateLayout();

            var cancel = FindByAutomationId<Button>(editor, "CancelReminderRulesButton");
            CheckValue("RuleEditor.CancelButton.Margin.Left", cancel.Margin.Left, ProbeUnit, failures);

            var firstDueDate = FindByAutomationId<WpfUiDatePicker>(editor, "ReminderRuleFirstDueDate");
            CheckValue("RuleEditor.FirstDueDate.Margin.Right", firstDueDate.Margin.Right, ProbeUnit, failures);

            // FirstDueDate 所在行是表单第 2 行（rowIndex=1），其顶距应消费 SpacingFieldRow
            var firstDueRow = firstDueDate.Parent as FrameworkElement
                ?? throw new InvalidOperationException("ReminderRuleFirstDueDate 缺少父行容器。");
            CheckValue("RuleEditor.FirstDueRow.Margin.Top", firstDueRow.Margin.Top, ProbeFieldRow, failures);

            var previewSurface = FindByAutomationId<Border>(editor, "ReminderRulePreviewSurface");
            CheckValue("RuleEditor.PreviewSurface.Margin.Top", previewSurface.Margin.Top, ProbeFieldRow, failures);
            var previewLabel = AutomationProperties.GetLabeledBy(previewSurface) as TextBlock
                ?? throw new InvalidOperationException("ReminderRulePreviewSurface 缺少 LabeledBy 标签。");
            CheckValue("RuleEditor.PreviewLabel.Margin.Top", previewLabel.Margin.Top, ProbeFieldRow, failures);
            CheckValue("RuleEditor.PreviewLabel.Margin.Right(excluded)", previewLabel.Margin.Right, 10, failures);
        }
        finally
        {
            editor.Close();
            WindowValidationInput.Pump(editor.Dispatcher);
        }
    }

    private static void VerifyTagSelector(ICollection<string> failures)
    {
        var selector = new TagSelector();
        var popup = selector.Children.OfType<Popup>().Single();
        var surface = popup.Child as Border
            ?? throw new InvalidOperationException("TagSelector Popup 缺少 Border 表面。");
        var panel = surface.Child as StackPanel
            ?? throw new InvalidOperationException("TagSelector 表面缺少 StackPanel。");
        var search = panel.Children.OfType<TextBox>().Single();
        var create = panel.Children.OfType<Button>().Single();
        var trigger = selector.Children.OfType<Button>().Single();
        var triggerText = ((StackPanel)trigger.Content).Children.OfType<TextBlock>().Single();

        CheckValue("TagSelector.Surface.Margin.Top", surface.Margin.Top, ProbeUnit, failures);
        CheckValue("TagSelector.Search.Margin.Bottom", search.Margin.Bottom, ProbeUnit, failures);
        CheckValue("TagSelector.Create.Margin.Top", create.Margin.Top, ProbeUnit, failures);
        // 碰巧同值但不同义
        CheckValue("TagSelector.Panel.Margin.Left(excluded)", panel.Margin.Left, 10, failures);
        CheckValue("TagSelector.TriggerText.Margin.Left(excluded)", triggerText.Margin.Left, 12, failures);
    }

    private static void VerifyWindowDefaults(ICollection<string> failures)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-spacing-default-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        FloatingWindow? floating = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            main = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                AppContext.BaseDirectory);

            CheckValue("Default.CountdownEditorHeading.Margin.Bottom", Required<FrameworkElement>(main, "CountdownEditorHeading").Margin.Bottom, DefaultFieldRow, failures);
            CheckValue("Default.CountdownWorkspaceBody.Row2.Height", Required<Grid>(main, "CountdownWorkspaceBody").RowDefinitions[2].Height.Value, DefaultBlockGap, failures);
            CheckValue("Default.CountdownNameError.Margin.Top", Required<FrameworkElement>(main, "CountdownNameError").Margin.Top, DefaultUnit, failures);

            floating = new FloatingWindow(
                workspace,
                calendar,
                new NoopManagerHost(),
                AppContext.BaseDirectory);
            CheckValue("Default.FloatingNotesButton.Margin.Left", Required<FrameworkElement>(floating, "FloatingNotesButton").Margin.Left, DefaultUnit, failures);
            CheckValue("Default.FloatingSearchNotes.Margin.Left", Required<FrameworkElement>(floating, "FloatingSearchNotes").Margin.Left, DefaultUnit, failures);
            CheckValue("Default.FloatingOpenSettings.Margin.Left", Required<FrameworkElement>(floating, "FloatingOpenSettings").Margin.Left, DefaultUnit, failures);
            CheckValue("Default.FloatingNoteSearchHintClear.Margin.Left", Required<FrameworkElement>(floating, "FloatingNoteSearchHintClear").Margin.Left, DefaultUnit, failures);
        }
        finally
        {
            if (floating is not null)
            {
                floating.Close();
                WindowValidationInput.Pump(floating.Dispatcher);
            }
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

    private static void CheckValue(string label, double actual, double expected, ICollection<string> failures)
    {
        if (Math.Abs(actual - expected) > Tolerance)
        {
            failures.Add($"{label}: expected={expected:0.##}, actual={actual:0.##}");
        }
    }

    private static T Required<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"缺少控件 {name}（{typeof(T).Name}）。");

    private static T FindByAutomationId<T>(DependencyObject root, string automationId) where T : DependencyObject =>
        VisualDescendants<T>(root)
            .FirstOrDefault(candidate => string.Equals(AutomationProperties.GetAutomationId(candidate), automationId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"缺少 AutomationId={automationId}（{typeof(T).Name}）。");

    private sealed class NoopManagerHost : IManagerWorkspaceHost
    {
        public void Reveal(WorkspaceKind workspace) { }
        public void RevealPreferences(WorkspaceKind workspace) { }
    }
}
