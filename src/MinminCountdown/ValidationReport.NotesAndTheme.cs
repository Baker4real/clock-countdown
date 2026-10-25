using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MinminCountdown.Core;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown;

internal static partial class ValidationReport
{
    private static bool AreNotesGroupedByDay(IReadOnlyList<NoteRecord> notes)
    {
        var seen = new HashSet<(bool IsCompleted, string? Date)>();
        bool? currentCompletion = null;
        string? currentDate = null;
        foreach (var note in notes)
        {
            if (
                currentCompletion == note.IsCompleted &&
                string.Equals(currentDate, note.Date, StringComparison.Ordinal))
            {
                continue;
            }

            currentCompletion = note.IsCompleted;
            currentDate = note.Date;
            if (!seen.Add((note.IsCompleted, note.Date)))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> GetRenderedNoteDateOrder(Panel rows)
    {
        return GetNoteDateSections(rows)
            .Select(section => section.Key.Length == 0 ? "未设置日期" : section.Key)
            .ToArray();
    }

    private static bool HasNoteStatusSegmentSwitch(
        ToggleButton activeSegment,
        ToggleButton completedSegment,
        TextBlock activeLabel,
        TextBlock completedLabel,
        TextBlock activeCount,
        TextBlock completedCount,
        Border activeBadge,
        Border completedBadge,
        Panel activeRows,
        Panel completedRows,
        int expectedActiveCount,
        int expectedCompletedCount)
    {
        var countsMatch =
            string.Equals(
                activeCount.Text,
                expectedActiveCount.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal) &&
            string.Equals(
                completedCount.Text,
                expectedCompletedCount.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        var labelsFixed =
            string.Equals(activeLabel.Text, "未完成", StringComparison.Ordinal) &&
            string.Equals(completedLabel.Text, "已完成", StringComparison.Ordinal);
        var segmentsVisibleAndEnabled =
            activeSegment.Visibility == Visibility.Visible &&
            completedSegment.Visibility == Visibility.Visible &&
            activeSegment.IsEnabled &&
            completedSegment.IsEnabled;
        var activeSelected =
            activeSegment.IsChecked == true &&
            completedSegment.IsChecked == false &&
            activeRows.Visibility == Visibility.Visible &&
            completedRows.Visibility == Visibility.Collapsed;
        return countsMatch &&
            labelsFixed &&
            segmentsVisibleAndEnabled &&
            activeSelected &&
            BadgeFollowsSegmentState(activeBadge, expectedActiveCount, isActive: true) &&
            BadgeFollowsSegmentState(completedBadge, expectedCompletedCount, isActive: false);
    }

    private static bool BadgeFollowsSegmentState(Border badge, int expectedCount, bool isActive)
    {
        if (badge.Child is not TextBlock count ||
            !string.Equals(
                count.Text,
                expectedCount.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            return false;
        }

        var expectedBackground = Application.Current.FindResource(
            isActive ? "PeachBadgeBrush" : "PeachBadgeMutedBrush");
        // #148：徽章文字必须达到信息文本对比门槛——激活段主文字（InkBrush，8.69），
        // 未激活段正文色（BodyBrush，6.09）；不再接受白字/弱化字钉死的旧合同。
        var expectedForeground = Application.Current.FindResource(
            isActive ? "InkBrush" : "BodyBrush");
        return ReferenceEquals(badge.Background, expectedBackground) &&
            ReferenceEquals(count.Foreground, expectedForeground) &&
            expectedBackground is SolidColorBrush backgroundBrush &&
            expectedForeground is SolidColorBrush foregroundBrush &&
            ContrastRatio(backgroundBrush.Color, foregroundBrush.Color) >= 4.5;
    }

    private static bool HasNoteSegmentAccessibleName(
        ToggleButton segment,
        string expectedStatus,
        string countText)
    {
        var name = AutomationProperties.GetName(segment);
        return
            !string.IsNullOrWhiteSpace(name) &&
            name.Contains($"查看{expectedStatus}便签", StringComparison.Ordinal) &&
            name.Contains(countText, StringComparison.Ordinal) &&
            name.Contains("条", StringComparison.Ordinal);
    }

    private static bool HasNoteDateTimelineVisual(NoteDateSection section)
    {
        var content = section.Toggle.Content as Grid;
        var label = GetNoteDateLabel(section.Toggle);
        var count = content?.Children
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(text.Uid, "NoteDateCount", StringComparison.Ordinal));
        var root = section.Root as Grid;
        var timeline = root?.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => string.Equals(grid.Uid, "NoteDateTimeline", StringComparison.Ordinal));
        var line = timeline?.Children
            .OfType<Border>()
            .Any(border => string.Equals(border.Uid, "NoteTimelineLine", StringComparison.Ordinal)) == true;
        var node = timeline?.Children
            .OfType<WpfEllipse>()
            .Any(ellipse => string.Equals(ellipse.Uid, "NoteTimelineNode", StringComparison.Ordinal)) == true;
        return
            string.Equals(section.Root.Uid, "NoteDateTimelineSection", StringComparison.Ordinal) &&
            Math.Abs(section.Root.Margin.Bottom - 12) <= 0.01 &&
            root?.ColumnDefinitions.Count > 0 &&
            root.ColumnDefinitions[0].Width.IsAbsolute &&
            Math.Abs(root.ColumnDefinitions[0].Width.Value - 22) <= 0.01 &&
            timeline is not null &&
            Math.Abs(timeline.Width - 22) <= 0.01 &&
            line &&
            node &&
            label is not null &&
            !string.IsNullOrWhiteSpace(label.Text) &&
            !label.Text.StartsWith("展开", StringComparison.Ordinal) &&
            !label.Text.StartsWith("收起", StringComparison.Ordinal) &&
            count is not null &&
            string.Equals(
                count.Text,
                $"{section.Rows.Children.Count} 条",
                StringComparison.Ordinal) &&
            DisclosureChevronMatches(
                section.Toggle,
                section.Rows.Visibility == Visibility.Visible);
    }

    private static TextBlock? GetNoteDateLabel(Button toggle)
    {
        return (toggle.Content as Grid)?.Children
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(text.Uid, "NoteDateLabel", StringComparison.Ordinal));
    }

    private static bool HasNoteDisclosureAccessibleName(Button toggle, string expectedObject)
    {
        var name = AutomationProperties.GetName(toggle);
        return
            !string.IsNullOrWhiteSpace(name) &&
            (name.StartsWith("展开", StringComparison.Ordinal) ||
             name.StartsWith("收起", StringComparison.Ordinal)) &&
            (name.Contains("当前展开", StringComparison.Ordinal) ||
             name.Contains("当前收起", StringComparison.Ordinal)) &&
            name.Contains(expectedObject, StringComparison.Ordinal) &&
            name.Contains("条", StringComparison.Ordinal);
    }

    private static bool HasNoteRowActionAccessibleNames(DependencyObject root)
    {
        var actions = Descendants<Button>(root)
            .Where(button =>
                button.Content is "编辑" or "删除")
            .ToArray();
        return actions.Length == 0 ||
               actions.All(button =>
               {
                   var action = (string)button.Content;
                   var name = AutomationProperties.GetName(button);
                   var prefix = $"{action}便签 ";
                   return ValidationExercises.IsWellFormedAccessibleName(name, prefix);
               });
    }

    private static bool DisclosureChevronMatches(Button toggle, bool isExpanded)
    {
        var chevron = (toggle.Content as Grid)?.Children
            .OfType<WpfPath>()
            .FirstOrDefault(path => string.Equals(path.Uid, "NoteDateChevron", StringComparison.Ordinal));
        return chevron is not null && DisclosureChevronMatches(chevron, isExpanded);
    }

    private static bool DisclosureChevronMatches(WpfPath chevron, bool isExpanded)
    {
        return
            chevron.RenderTransform is RotateTransform rotation &&
            Math.Abs(rotation.Angle - (isExpanded ? 0 : -90)) < 0.01;
    }

    private static IReadOnlyList<string> GetRenderedNoteIds(DependencyObject root) =>
        Descendants<CheckBox>(root)
            .Where(checkBox => checkBox.Tag is string)
            .Select(checkBox => (string)checkBox.Tag)
            .ToArray();

    private static NoteSectionExerciseResult ExerciseNoteSections(
        MainWindow main,
        StackPanel activeRows,
        StackPanel completedRows)
    {
        var activeSegment = Required<ToggleButton>(main, "ActiveNotesSegment");
        var completedSegment = Required<ToggleButton>(main, "CompletedNotesSegment");
        var originalWorkspace = main.CurrentWorkspace;

        try
        {
            main.ShowWorkspace(WorkspaceKind.Notes);
            if (completedSegment.IsChecked == true)
            {
                activeSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            var segmentsSwitchExclusively = ExerciseNoteSegmentSwitch(
                main,
                activeRows,
                completedRows,
                activeSegment,
                completedSegment);

            var activeSections = GetNoteDateSections(activeRows);
            var completedSections = GetNoteDateSections(completedRows);
            var activeKey = activeSections.FirstOrDefault()?.Key;
            var completedKey = completedSections.FirstOrDefault()?.Key;
            var dateSectionsCanCollapse = true;
            var dateSectionsAreIndependent = true;
            var dateSectionStateSurvivesRefresh = true;

            if (activeKey is not null)
            {
                var activeSection = FindNoteDateSection(activeRows, activeKey)!;
                activeSection.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dateSectionsCanCollapse &=
                    activeSection.Rows.Visibility == Visibility.Collapsed &&
                    DisclosureChevronMatches(activeSection.Toggle, isExpanded: false);
                dateSectionsAreIndependent &=
                    OtherNoteDateSectionsAreVisible(activeRows, activeKey) &&
                    GetNoteDateSections(completedRows).All(section => section.Rows.Visibility == Visibility.Visible);

                main.ShowWorkspace(WorkspaceKind.Notes);
                activeSection = FindNoteDateSection(activeRows, activeKey)!;
                dateSectionStateSurvivesRefresh &= activeSection.Rows.Visibility == Visibility.Collapsed;
                dateSectionsAreIndependent &=
                    OtherNoteDateSectionsAreVisible(activeRows, activeKey) &&
                    GetNoteDateSections(completedRows).All(section => section.Rows.Visibility == Visibility.Visible);
            }

            if (completedKey is not null)
            {
                var completedSection = FindNoteDateSection(completedRows, completedKey)!;
                completedSection.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dateSectionsCanCollapse &=
                    completedSection.Rows.Visibility == Visibility.Collapsed &&
                    DisclosureChevronMatches(completedSection.Toggle, isExpanded: false);
                dateSectionsAreIndependent &=
                    OtherNoteDateSectionsAreVisible(completedRows, completedKey) &&
                    (activeKey is null ||
                     FindNoteDateSection(activeRows, activeKey)!.Rows.Visibility == Visibility.Collapsed);

                main.ShowWorkspace(WorkspaceKind.Notes);
                completedSection = FindNoteDateSection(completedRows, completedKey)!;
                dateSectionStateSurvivesRefresh &= completedSection.Rows.Visibility == Visibility.Collapsed;
                dateSectionsAreIndependent &=
                    OtherNoteDateSectionsAreVisible(completedRows, completedKey) &&
                    (activeKey is null ||
                     FindNoteDateSection(activeRows, activeKey)!.Rows.Visibility == Visibility.Collapsed);
            }

            if (activeKey is not null && completedKey is not null)
            {
                FindNoteDateSection(activeRows, activeKey)!.Toggle.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                dateSectionsAreIndependent &=
                    FindNoteDateSection(activeRows, activeKey)!.Rows.Visibility == Visibility.Visible &&
                    FindNoteDateSection(completedRows, completedKey)!.Rows.Visibility == Visibility.Collapsed;
            }

            return new NoteSectionExerciseResult(
                dateSectionsCanCollapse,
                dateSectionsAreIndependent,
                dateSectionStateSurvivesRefresh,
                segmentsSwitchExclusively);
        }
        finally
        {
            ExpandAllNoteDateSections(activeRows);
            ExpandAllNoteDateSections(completedRows);
            if (completedSegment.IsChecked == true)
            {
                activeSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            main.ShowWorkspace(originalWorkspace);
        }
    }

    private static bool ExerciseNoteSegmentSwitch(
        MainWindow main,
        StackPanel activeRows,
        StackPanel completedRows,
        ToggleButton activeSegment,
        ToggleButton completedSegment)
    {
        var activeRowsExpected = ParseBadgeCount(activeSegment) > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        var completedRowsExpected = ParseBadgeCount(completedSegment) > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        completedSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var switchedExclusively =
            completedSegment.IsChecked == true &&
            activeSegment.IsChecked == false &&
            completedRows.Visibility == completedRowsExpected &&
            activeRows.Visibility == Visibility.Collapsed;
        main.ShowWorkspace(WorkspaceKind.Notes);
        switchedExclusively &=
            completedSegment.IsChecked == true &&
            completedRows.Visibility == completedRowsExpected &&
            activeRows.Visibility == Visibility.Collapsed;

        activeSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        switchedExclusively &=
            activeSegment.IsChecked == true &&
            completedSegment.IsChecked == false &&
            activeRows.Visibility == activeRowsExpected &&
            completedRows.Visibility == Visibility.Collapsed;
        main.ShowWorkspace(WorkspaceKind.Notes);
        switchedExclusively &=
            activeSegment.IsChecked == true &&
            activeRows.Visibility == activeRowsExpected &&
            completedRows.Visibility == Visibility.Collapsed;
        return switchedExclusively;
    }

    private static IReadOnlyList<NoteDateSection> GetNoteDateSections(Panel rows)
    {
        var result = new List<NoteDateSection>();
        foreach (var section in rows.Children.OfType<FrameworkElement>())
        {
            var toggle = Descendants<Button>(section)
                .FirstOrDefault(button =>
                    string.Equals(button.Uid, "NoteDateDisclosure", StringComparison.Ordinal));
            var sectionRows = Descendants<StackPanel>(section)
                .FirstOrDefault(panel =>
                    string.Equals(panel.Uid, "NoteDateRows", StringComparison.Ordinal));
            if (toggle is not null && sectionRows is not null)
            {
                result.Add(new NoteDateSection(
                    toggle.Tag as string ?? string.Empty,
                    section,
                    toggle,
                    sectionRows));
            }
        }

        return result;
    }

    private static NoteDateSection? FindNoteDateSection(StackPanel rows, string key) =>
        GetNoteDateSections(rows).FirstOrDefault(section =>
            string.Equals(section.Key, key, StringComparison.Ordinal));

    private static bool OtherNoteDateSectionsAreVisible(StackPanel rows, string exceptKey) =>
        GetNoteDateSections(rows)
            .Where(section => !string.Equals(section.Key, exceptKey, StringComparison.Ordinal))
            .All(section => section.Rows.Visibility == Visibility.Visible);

    private static void ExpandAllNoteDateSections(StackPanel rows)
    {
        foreach (var section in GetNoteDateSections(rows).Where(section =>
                     section.Rows.Visibility == Visibility.Collapsed))
        {
            section.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private static bool HasApplicationStyle(Type key) =>
        Application.Current.Resources[key] is Style;

    private static bool HasTemplate(Control control)
    {
        control.ApplyTemplate();
        return control.Template is not null;
    }

    private static MainThemeExerciseResult RunMainThemeExercise(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        var backgroundColors = new List<string>();
        var characterOpacities = new List<int>();
        var characterPanelColors = new List<string>();
        var reminderLabelFollowsTheme = true;
        var segmentSelectedTextContrast = true;
        var characterPanelDocksFollowTheme = true;
        var referenceDecorationsFollowTheme = true;
        var reminderLabel = Required<TextBlock>(main, "NoteReminderModeLabel");
        var characterPanel = Required<Border>(main, "CharacterPanelSurface");
        var originalWorkspace = main.CurrentWorkspace;

        try
        {
            theme.Apply();
            main.ApplyTheme();
            main.UpdateTime(restoreUtc);
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            main.UpdateLayout();
            backgroundColors.Add(BrushColor(main.Background));
            characterOpacities.Add((int)Math.Round(main.CharacterBackgroundOpacity * 100));
            characterPanelColors.Add(BrushColor(characterPanel.Background));
            reminderLabelFollowsTheme &=
                ReferenceEquals(reminderLabel.Foreground, Application.Current.FindResource("BodyBrush"));
            segmentSelectedTextContrast &= MainSegmentButtonsHaveAccessibleSelectedStates(main);
            characterPanelDocksFollowTheme &= main.CharacterPanelDocksFollowActiveTheme;
            referenceDecorationsFollowTheme &= main.MainReferenceDecorationsFollowActiveTheme;

            main.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            main.UpdateLayout();
            characterPanelDocksFollowTheme &= main.CharacterPanelDocksFollowActiveTheme;
            referenceDecorationsFollowTheme &= main.MainReferenceDecorationsFollowActiveTheme;
        }
        finally
        {
            main.ShowWorkspace(originalWorkspace);
            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            main.UpdateLayout();
        }

        return new MainThemeExerciseResult(
            backgroundColors,
            characterOpacities,
            characterPanelColors,
            reminderLabelFollowsTheme,
            segmentSelectedTextContrast,
            characterPanelDocksFollowTheme,
            referenceDecorationsFollowTheme);
    }

    private static MainWorkspaceThemeValidation ValidateMainWorkspaceResourcesWithoutRerender(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc,
        string assetRoot)
    {
        var liveResult = EvaluateMainWorkspaceResourcesWithoutRerender(
            theme,
            main,
            restoreUtc);
        if (liveResult.CountdownCardSampleCount > 0 && liveResult.NoteCardSampleCount > 0)
        {
            return liveResult;
        }

        return RunIsolatedMainWorkspaceThemeValidation(theme, restoreUtc, assetRoot);
    }

    private static MainWorkspaceThemeValidation RunIsolatedMainWorkspaceThemeValidation(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc,
        string assetRoot)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-theme-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(temporaryRoot, "settings.json");
        var diagnosticPath = Path.Combine(temporaryRoot, "validation.jsonl");
        MainWindow? validationMain = null;
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var store = new SettingsStore(clock: new ValidationClock(restoreUtc));
            store.Save(
                settingsPath,
                new CountdownSettings(
                    new[]
                    {
                        new CountdownRecord(
                            "validation-theme-countdown",
                            "主题资源验证倒计时",
                            restoreUtc.AddDays(2),
                            Array.Empty<string>())
                    },
                    new[]
                    {
                        new NoteRecord(
                            "validation-theme-note",
                            "主题资源验证便签",
                            ChinaStandardTime.ToBeijingTime(restoreUtc)
                                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            false,
                            Array.Empty<string>(),
                            NoReminder.Instance)
                    },
                    Array.Empty<TagRecord>(),
                    CountdownSettings.Empty.Preferences));
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            using var calendar = new HolidayCalendar(temporaryRoot);
            validationMain = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                assetRoot)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            validationMain.Show();
            PumpDispatcher();
            validationMain.UpdateLayout();
            return EvaluateMainWorkspaceResourcesWithoutRerender(
                theme,
                validationMain,
                restoreUtc);
        }
        finally
        {
            validationMain?.Close();
            TryDeleteSettingsFiles(settingsPath);
            TryDeleteFile(settingsPath + ".tmp");
            TryDeleteFile(diagnosticPath);
            var holidayDirectory = Path.Combine(temporaryRoot, "holiday-cache");
            if (Directory.Exists(holidayDirectory) &&
                !Directory.EnumerateFileSystemEntries(holidayDirectory).Any())
            {
                Directory.Delete(holidayDirectory);
            }
            if (Directory.Exists(temporaryRoot) &&
                !Directory.EnumerateFileSystemEntries(temporaryRoot).Any())
            {
                Directory.Delete(temporaryRoot);
            }
        }
    }

    private static MainWorkspaceThemeValidation EvaluateMainWorkspaceResourcesWithoutRerender(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        static bool UsesResource(DependencyObject element, DependencyProperty property, string key) =>
            ReferenceEquals(element.GetValue(property), Application.Current.FindResource(key));

        static bool UsesAnyResource(
            DependencyObject element,
            DependencyProperty property,
            params string[] keys) =>
            keys.Any(key => UsesResource(element, property, key));

        static bool BodyTextUsesTheme(StackPanel body) =>
            body.Children
                .OfType<TextBlock>()
                .All(text => UsesAnyResource(
                    text,
                    TextBlock.ForegroundProperty,
                    "InkBrush",
                    "BodyBrush",
                    "MutedBrush",
                    "DangerBrush"));

        static bool TagTextUsesTheme(StackPanel body) =>
            body.Children
                .OfType<WrapPanel>()
                .SelectMany(panel => panel.Children.OfType<Border>())
                .Select(chip => chip.Child)
                .OfType<TextBlock>()
                .All(text => UsesResource(text, TextBlock.ForegroundProperty, "TagChipTextBrush"));

        static bool CardBodiesUseTheme(Border card, bool requirePrimaryColumn)
        {
            if (card.Child is not Grid grid)
            {
                return false;
            }

            var bodies = grid.Children
                .OfType<StackPanel>()
                .Where(panel => !requirePrimaryColumn || Grid.GetColumn(panel) is 0 or 1)
                .ToArray();
            var directTexts = grid.Children
                .OfType<TextBlock>()
                .Where(text => !requirePrimaryColumn || Grid.GetColumn(text) is 0 or 1)
                .ToArray();
            var directTagTexts = grid.Children
                .OfType<WrapPanel>()
                .Where(panel => !requirePrimaryColumn || Grid.GetColumn(panel) is 0 or 1)
                .SelectMany(panel => panel.Children.OfType<Border>())
                .Select(chip => chip.Child)
                .OfType<TextBlock>()
                .ToArray();
            return (directTexts.Length > 0 ||
                    bodies.Any(body => body.Children.OfType<TextBlock>().Any())) &&
                directTexts.All(text => UsesAnyResource(
                    text,
                    TextBlock.ForegroundProperty,
                    "InkBrush",
                    "BodyBrush",
                    "MutedBrush",
                    "DangerBrush")) &&
                directTagTexts.All(text =>
                    UsesResource(text, TextBlock.ForegroundProperty, "TagChipTextBrush")) &&
                bodies.All(BodyTextUsesTheme) &&
                bodies.All(TagTextUsesTheme);
        }

        var originalWorkspace = main.CurrentWorkspace;
        try
        {
            theme.Apply();
            main.ApplyTheme();
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            main.UpdateLayout();

            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            main.UpdateLayout();

            var countdownPanelsFollowTheme = new[]
                {
                    main.FindName("CountdownWorkspaceBackdrop") as Border,
                    main.FindName("CountdownEditorSurface") as Border,
                    main.FindName("CountdownPreferencesSurface") as Border
                }
                .All(panel =>
                    panel is not null &&
                    UsesResource(panel, Border.BackgroundProperty, "BackgroundBrush") &&
                    UsesResource(panel, Border.BorderBrushProperty, "PeachLineBrush"));
            var notePanel = main.FindName("NoteEditorPanel") as Border;
            var panelsFollowTheme =
                countdownPanelsFollowTheme &&
                notePanel is not null &&
                UsesResource(notePanel, Border.BackgroundProperty, "WorkspacePanelBrush") &&
                UsesResource(notePanel, Border.BorderBrushProperty, "WorkspacePanelLineBrush");
            var titleFollowsTheme =
                main.FindName("CountdownEditorTitle") is TextBlock editorTitle &&
                UsesResource(editorTitle, TextBlock.ForegroundProperty, "InkBrush");
            var countdownCards = Required<StackPanel>(main, "CountdownRows")
                .Children
                .OfType<Border>()
                .ToArray();
            var countdownCardsFollowTheme = countdownCards.All(card =>
                    UsesAnyResource(card, Border.BackgroundProperty, "CardBrush", "ExpiredBrush") &&
                    UsesResource(card, Border.BorderBrushProperty, "PeachLineBrush") &&
                    CardBodiesUseTheme(card, requirePrimaryColumn: true));

            theme.Apply();
            main.ApplyTheme();
            main.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            main.UpdateLayout();

            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            main.UpdateLayout();

            var noteCards = new[]
                {
                    Required<StackPanel>(main, "ActiveNoteRows"),
                    Required<StackPanel>(main, "CompletedNoteRows")
                }
                .SelectMany(rows => Descendants<Border>(rows))
                .Where(card =>
                    Math.Abs(card.CornerRadius.TopLeft - 8) <= 0.01 &&
                    Math.Abs(card.Padding.Left - 14) <= 0.01)
                .ToArray();
            var noteCardsFollowTheme = noteCards.All(card =>
                    UsesAnyResource(card, Border.BackgroundProperty, "NoteBrush", "NoteWhiteBrush") &&
                    UsesAnyResource(card, Border.BorderBrushProperty, "NoteLineBrush", "PeachLineBrush") &&
                    CardBodiesUseTheme(card, requirePrimaryColumn: false));

            return new MainWorkspaceThemeValidation(
                panelsFollowTheme && titleFollowsTheme,
                countdownCards.Length,
                countdownCardsFollowTheme,
                noteCards.Length,
                noteCardsFollowTheme);
        }
        finally
        {
            theme.Apply();
            main.ApplyTheme();
            main.ShowWorkspace(originalWorkspace);
            PumpDispatcher();
            main.UpdateLayout();
        }
    }

    private static bool ValidateTagPopupThemeTransition(
        ApplicationTheme theme,
        TagSelector selector,
        DateTimeOffset restoreUtc)
    {
        var wasOpen = selector.IsPopupOpen;
        selector.IsPopupOpen = true;
        try
        {
            theme.Apply();
            PumpDispatcher();
            return selector.PopupSurfaceFollowsActiveTheme;
        }
        finally
        {
            selector.IsPopupOpen = wasOpen;
            theme.Apply();
            PumpDispatcher();
        }
    }

    private static bool ValidateWarmYellowPalette(ApplicationTheme theme, DateTimeOffset restoreUtc)
    {
        static bool HasExpectedColor(string key, string expected)
        {
            if (ColorConverter.ConvertFromString(expected) is not Color expectedColor)
            {
                return false;
            }

            return Application.Current.FindResource(key) switch
            {
                Color color => color == expectedColor,
                SolidColorBrush brush => brush.Color == expectedColor,
                _ => false
            };
        }

        var defaultTagColorsMatch = MainWindow.DefaultTagColors.SequenceEqual(
            new[] { "#F4B6A6", "#F4D58D", "#F1D5C5", "#F9DFDB", "#FFF0E4" },
            StringComparer.OrdinalIgnoreCase);
        try
        {
            theme.Apply();
            var dayMatches = new[]
            {
                ("CanvasColor", "#FFFDF3EB"),
                ("BackgroundColor", "#FFFDF0E4"),
                ("CardColor", "#FFFEFCFB"),
                ("PanelColor", "#FFFFFDFC"),
                ("CardAltColor", "#FFFEF9F5"),
                ("NoteWhiteColor", "#FFFEFDFD"),
                ("NoteColor", "#FFFEF5DC"),
                ("NoteLineColor", "#FFF6D9B2"),
                ("ActiveHoverColor", "#FFB84A36"),
                ("MutedColor", "#FF82665F"),
                ("MainActionColor", "#FFC23E68"),
                ("MainActionHoverColor", "#FFB0355D"),
                ("MainActionPressedColor", "#FF9B2D50"),
                ("DangerColor", "#FFB74C39"),
                ("PrimaryCountdownNumberColor", "#FFC0102F"),
                ("PeachLineColor", "#FFF6DFD3"),
                ("TagColor", "#FFF7E8"),
                ("TagChipTextColor", "#FF3B190D"),
                ("SegmentSelectedColor", "#FFFEEFE1"),
                ("SegmentSelectedForegroundColor", "#FF3B190D"),
                ("PinkTintColor", "#FFFEDDDE"),
                ("PinkAccentStrongColor", "#FFC23E68"),
                ("WorkspacePanelColor", "#00FFFDFC"),
                ("WorkspacePanelLineColor", "#00F6DFD3"),
                ("FloatingDayPanelBrush", "#FFFEFCFB"),
                ("FloatingDayHeaderBrush", "#FFFEF5DC"),
                ("FloatingDayNoteInputBrush", "#FFFEF5DC"),
                ("FloatingDayPlushGlowBrush", "#B9794E"),
                ("FloatingDayCinnamorollDockBrush", "#FFF0D2")
            }.All(pair => HasExpectedColor(pair.Item1, pair.Item2));

            return dayMatches && defaultTagColorsMatch;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static bool ValidateNoteDisclosureThemeTokens(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        static bool HasExpectedColor(string key, string expected)
        {
            return
                ColorConverter.ConvertFromString(expected) is Color expectedColor &&
                Application.Current.FindResource(key) is Color actualColor &&
                actualColor == expectedColor;
        }

        try
        {
            theme.Apply();
            var dayMatches = new[]
            {
                ("NoteActiveRailColor", "#CD623E"),
                ("NoteCompletedRailColor", "#8A7069"),
                ("NoteTimelineColor", "#F6DFD3"),
                ("PeachTintColor", "#FEEFE1"),
                ("PeachRailColor", "#CD623E"),
                ("PeachBadgeColor", "#FBAE90"),
                ("PeachBadgeMutedColor", "#F3DED8")
            }.All(pair => HasExpectedColor(pair.Item1, pair.Item2));

            return dayMatches;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static bool ValidatePrimaryButtonHoverTextContrast(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        var primaryStyle = Application.Current.FindResource("PrimaryButton") as Style;
        var button = new Button { Style = primaryStyle, Content = "验证主行动" };
        button.ApplyTemplate();
        // #194 moved the hover color from a trigger setter to the shared
        // animation behavior. Inspect the materialized template's actual binding.
        var interaction = button.Template?.FindName("InteractionSurface", button) as FrameworkElement;
        var hasDedicatedHoverState = interaction is not null &&
            MotionBehavior.GetIsEnabled(interaction) &&
            string.Equals(MotionBehavior.GetHoverBackgroundResourceKey(interaction),
                "MainActionHoverBrush", StringComparison.Ordinal) &&
            string.Equals(MotionBehavior.GetHoverBorderBrushResourceKey(interaction),
                "MainActionHoverBrush", StringComparison.Ordinal);
        try
        {
            theme.Apply();
            if (Application.Current.FindResource("MainActionHoverBrush") is not Brush hover ||
                Application.Current.FindResource("MainActionTextBrush") is not Brush hoverForeground ||
                !ApplicationTheme.MeetsContrast(hover, hoverForeground, 4.5))
            {
                return false;
            }

            return hasDedicatedHoverState;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static (string Previous, string Next) ValidateCalendarNavigationNames(
        MainWindow main,
        DatePicker picker)
    {
        var originalWorkspace = main.CurrentWorkspace;
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            picker.BringIntoView();
            main.UpdateLayout();
            PumpDispatcher();
            return WindowValidationInput.ReadCalendarNavigationNames(picker);
        }
        finally
        {
            main.ShowWorkspace(originalWorkspace);
            PumpDispatcher();
        }
    }

    private static bool ValidateMainActionTextContrast(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        var themedButtons = new[]
        {
            Required<Button>(main, "SaveCountdownButton"),
            Required<Button>(main, "SaveNoteButton")
        };
        var style = themedButtons[0].Style;
        if (style is null || themedButtons.Any(button => !ReferenceEquals(button.Style, style)))
        {
            return false;
        }

        // #217：保存设置作为次级按钮使用 CardAlt + ControlBorder，保留主次角色差异。
        // 样式定义在 MainWindow.xaml MainContentProjection 的 Grid.Resources 内，
        // 必须经按钮自身沿逻辑树解析，Window.TryFindResource 看不到嵌套资源字典。
        var savePreferencesButton = Required<Button>(main, "SavePreferencesButton");
        if (savePreferencesButton.TryFindResource("MainSecondarySolidButton") is not Style secondarySolidStyle ||
            !ReferenceEquals(savePreferencesButton.Style, secondarySolidStyle) ||
            !ApplicationTheme.MeetsContrast(
                (Brush)Application.Current.FindResource("CardAltBrush"),
                (Brush)Application.Current.FindResource("InkBrush"), 4.5))
        {
            return false;
        }

        try
        {
            theme.Apply();
            if (Application.Current.FindResource("MainActionTextBrush") is not Brush foreground ||
                new[]
                {
                    "MainActionBrush",
                    "MainActionHoverBrush",
                    "MainActionPressedBrush",
                    "PinkAccentStrongBrush"
                }.Any(key =>
                    Application.Current.FindResource(key) is not Brush background ||
                    !ApplicationTheme.MeetsContrast(background, foreground, 4.5)))
            {
                return false;
            }

            return true;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static FloatingQuickAddThemeValidation ValidateFloatingQuickAddLocalTheme(
        ApplicationTheme applicationTheme,
        FloatingWindow floating)
    {
        try
        {
            applicationTheme.Apply();
            floating.ApplyTheme();
            PumpDispatcher();
            var independent = !ReferenceEquals(
                floating.FindResource("CanvasBrush"),
                Application.Current.FindResource("CanvasBrush"));
            var day = floating.ProbeQuickAddThemeResources();
            var dayBindings = floating.ProbeQuickAddPaletteBindings();
            var dayCanvas = ResourceColor(floating, "CanvasBrush");
            var dayActive = ResourceColor(floating, "ActiveBrush");

            return new FloatingQuickAddThemeValidation(
                day,
                dayCanvas,
                dayActive,
                independent &&
                dayCanvas == "#FFFEFCFB" &&
                ReferenceEquals(floating.FindResource("ActiveBrush"), Application.Current.FindResource("ActiveBrush")),
                dayBindings);
        }
        finally
        {
            applicationTheme.Apply();
            floating.ApplyTheme();
            PumpDispatcher();
        }
    }

    private static string ResourceColor(FrameworkElement element, string resourceKey) =>
        element.FindResource(resourceKey) is SolidColorBrush brush
            ? brush.Color.ToString()
            : string.Empty;
}
