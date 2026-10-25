using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown;

internal static partial class ValidationReport
{
    private static bool ValidateToolTipThemeTextContrast(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        try
        {
            theme.Apply();
            var toolTip = new ToolTip { Content = "主题提示" };
            toolTip.ApplyTemplate();
            var content = toolTip.Template.FindName("Content", toolTip) as TextBlock;
            return
                toolTip.Background is Brush background &&
                toolTip.Foreground is Brush foreground &&
                ApplicationTheme.MeetsContrast(background, foreground, 4.5) &&
                toolTip.MaxWidth <= 360 &&
                content?.TextWrapping is TextWrapping.Wrap;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static bool ValidateTagChipTextContrast(
        ApplicationTheme theme,
        MainWindow main,
        DateTimeOffset restoreUtc)
    {
        try
        {
            theme.Apply();
            main.ApplyTheme();
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(restoreUtc));
            PumpDispatcher();
            return main.TagChipsHaveAccessibleThemeText;
        }
        finally
        {
            theme.Apply();
            main.ApplyTheme();
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(restoreUtc));
            PumpDispatcher();
        }
    }

    private static MainNoteCompletionValidation ValidateMainNoteCompletion(
        MainWindow main,
        FloatingWindow floating,
        TagSelector selector,
        DesktopWorkspace workspace)
    {
        var originalMainWorkspace = main.CurrentWorkspace;
        var originalFloatingWorkspace = floating.CurrentWorkspaceForValidation;
        var originalFilter = workspace.Snapshot.Filter;
        var originalTags = workspace.Snapshot.Settings.Tags;
        var settingsTemporaryPath = workspace.Snapshot.SettingsPath + ".tmp";
        var temporaryPathAlreadyBlocked = Directory.Exists(settingsTemporaryPath);
        var probeText = $"便签完成复选框探针-{Guid.NewGuid():N}";
        string? probeId = workspace.Snapshot.Settings.Notes.FirstOrDefault(note => !note.IsCompleted)?.Id;
        var createdProbe = false;
        var createdFailureBlock = false;
        try
        {
            if (probeId is null && !temporaryPathAlreadyBlocked)
            {
                var saved = workspace.Apply(new SaveNoteChange(
                    null,
                    probeText,
                    workspace.Snapshot.BeijingNow.Date,
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
                probeId = saved.Succeeded
                    ? workspace.Snapshot.Settings.Notes.Single(note =>
                        string.Equals(note.Text, probeText, StringComparison.Ordinal)).Id
                    : null;
                createdProbe = probeId is not null;
            }

            if (probeId is null)
            {
                return MainNoteCompletionValidation.Failed;
            }

            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            main.ShowWorkspace(WorkspaceKind.Notes);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            main.UpdateLayout();
            floating.UpdateLayout();

            CheckBox MainCheck() => Descendants<CheckBox>(main)
                .Single(check => string.Equals(check.Tag as string, probeId, StringComparison.OrdinalIgnoreCase));
            CheckBox FloatingCheck() => Descendants<CheckBox>(floating)
                .Single(check => string.Equals(check.Tag as string, probeId, StringComparison.OrdinalIgnoreCase));

            var probeTag = new TagRecord($"completion-style-{Guid.NewGuid():N}", "尺寸探针", "#F4B6A6");
            selector.SetTags(originalTags.Concat([probeTag]).ToArray());
            selector.IsPopupOpen = true;
            PumpDispatcher();
            selector.UpdateLayout();
            var tagCheck = Descendants<CheckBox>(selector.Popup.Child)
                .Single(check => string.Equals(check.Tag as string, probeTag.Id, StringComparison.OrdinalIgnoreCase));
            var dedicatedStyle = Required<Grid>(main, "MainContentProjection")
                .FindResource("MainNoteCompletionCheck") as Style;

            var mainMeasurement = MeasureNoteCompletionCheckBox(
                "MainWindow",
                MainCheck(),
                dedicatedStyle);
            var tagMeasurement = MeasureNoteCompletionCheckBox(
                "TagSelector",
                tagCheck,
                dedicatedStyle);
            var floatingMeasurement = MeasureNoteCompletionCheckBox(
                "FloatingWindow",
                FloatingCheck(),
                dedicatedStyle);

            var noteText = workspace.Snapshot.Settings.Notes.Single(note =>
                string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).Text;
            var initialName = AutomationProperties.GetName(MainCheck());
            MainCheck().IsChecked = true;
            PumpDispatcher();
            var checkedWorks = workspace.Snapshot.Settings.Notes.Single(note =>
                string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).IsCompleted;
            var checkedName = AutomationProperties.GetName(MainCheck());

            MainCheck().IsChecked = false;
            PumpDispatcher();
            var uncheckedWorks = !workspace.Snapshot.Settings.Notes.Single(note =>
                string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).IsCompleted;
            var uncheckedName = AutomationProperties.GetName(MainCheck());

            var rollbackWorks = false;
            if (!temporaryPathAlreadyBlocked)
            {
                Directory.CreateDirectory(settingsTemporaryPath);
                createdFailureBlock = true;
                var failureCheck = MainCheck();
                failureCheck.IsChecked = true;
                PumpDispatcher();
                rollbackWorks =
                    failureCheck.IsChecked == false &&
                    !workspace.Snapshot.Settings.Notes.Single(note =>
                        string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).IsCompleted &&
                    AutomationProperties.GetName(failureCheck) == $"完成便签 {noteText}";
            }
            else
            {
                var failureCheck = MainCheck();
                failureCheck.IsChecked = true;
                PumpDispatcher();
                rollbackWorks =
                    failureCheck.IsChecked == false &&
                    !workspace.Snapshot.Settings.Notes.Single(note =>
                        string.Equals(note.Id, probeId, StringComparison.OrdinalIgnoreCase)).IsCompleted;
            }

            var interaction = new MainNoteCompletionInteractionValidation(
                checkedWorks,
                uncheckedWorks,
                rollbackWorks,
                initialName == $"完成便签 {noteText}" &&
                checkedName == $"恢复便签 {noteText}" &&
                uncheckedName == $"完成便签 {noteText}");
            return new MainNoteCompletionValidation(
                mainMeasurement,
                tagMeasurement,
                floatingMeasurement,
                interaction);
        }
        finally
        {
            if (createdFailureBlock && Directory.Exists(settingsTemporaryPath))
            {
                Directory.Delete(settingsTemporaryPath);
            }

            if (createdProbe && probeId is not null)
            {
                workspace.Apply(new DeleteNoteChange(probeId));
            }

            selector.IsPopupOpen = false;
            selector.SetTags(originalTags);
            workspace.ApplyFilter(originalFilter);
            main.ShowWorkspace(originalMainWorkspace);
            floating.ShowWorkspace(originalFloatingWorkspace);
            PumpDispatcher();
        }
    }

    private static MainNoteCompletionMeasurement MeasureNoteCompletionCheckBox(
        string user,
        CheckBox checkBox,
        Style? dedicatedStyle)
    {
        checkBox.ApplyTemplate();
        checkBox.UpdateLayout();
        var box = checkBox.Template.FindName("Box", checkBox) as Border;
        var tick = checkBox.Template.FindName("Tick", checkBox) as WpfPath;
        return new MainNoteCompletionMeasurement(
            user,
            Math.Round(box?.ActualWidth ?? 0, 2),
            Math.Round(box?.ActualHeight ?? 0, 2),
            Math.Round(tick?.Width ?? 0, 2),
            Math.Round(tick?.Height ?? 0, 2),
            box?.CornerRadius.TopLeft ?? 0,
            dedicatedStyle is not null && ReferenceEquals(checkBox.Style, dedicatedStyle),
            box is not null && tick is not null &&
            ReferenceEquals(box.Background, Application.Current.FindResource("CanvasBrush")) &&
            ReferenceEquals(box.BorderBrush, Application.Current.FindResource("ControlBorderBrush")) &&
            ReferenceEquals(tick.Stroke, Application.Current.FindResource("MainActionTextBrush")),
            ReferenceEquals(checkBox.Style, Application.Current.TryFindResource("CompletionCheckBox")));
    }

    private static CountdownEmptyStateContrastValidation ValidateCountdownEmptyStateContrast(
        ApplicationTheme theme,
        MainWindow main)
    {
        var heading = Required<TextBlock>(main, "EmptyCountdownMessage");
        var emptyMessage = Required<TextBlock>(main, "EmptyCountdownHint");
        var content = Required<StackPanel>(main, "CountdownEmptyStateContent");
        var rows = Required<StackPanel>(main, "CountdownRows");
        var workspace = Required<Canvas>(main, "CountdownWorkspace");
        var backdrop = Required<Border>(main, "CountdownWorkspaceBackdrop");
        var originalWorkspace = main.CurrentWorkspace;
        var originalVisibility = emptyMessage.Visibility;
        var originalHeadingVisibility = heading.Visibility;
        var originalContentVisibility = content.Visibility;
        var originalRowsVisibility = rows.Visibility;
        var measurements = new List<CountdownEmptyStateContrastMeasurement>();
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            content.Visibility = Visibility.Visible;
            rows.Visibility = Visibility.Collapsed;
            heading.Visibility = Visibility.Visible;
            emptyMessage.Visibility = Visibility.Visible;
            theme.Apply();
            main.ApplyTheme();
            main.UpdateLayout();
            PumpDispatcher();

            var foreground = emptyMessage.Foreground as SolidColorBrush;
            var background = backdrop.Background as SolidColorBrush;
            var foregroundUsesMutedBrush =
                ReferenceEquals(emptyMessage.Foreground, Application.Current.FindResource("MutedBrush"));
            var actualBackgroundMatchesBackdrop =
                ReferenceEquals(backdrop.Background, Application.Current.FindResource("BackgroundBrush"));
            var transparentAncestors = EmptyStateAncestorsAreTransparent(emptyMessage, workspace) &&
                EmptyStateAncestorsAreTransparent(heading, workspace);
            var textBounds = BoundsIn(emptyMessage, main);
            var backdropBounds = BoundsIn(backdrop, main);
            var textWithinBackdrop = emptyMessage.IsVisible && textBounds.Width > 0 &&
                textBounds.Height > 0 && backdropBounds.Contains(textBounds);
            var headingForeground = heading.Foreground as SolidColorBrush;
            var headingBounds = BoundsIn(heading, main);

            measurements.Add(new CountdownEmptyStateContrastMeasurement(
                "Day",
                foreground?.Color.ToString() ?? string.Empty,
                background?.Color.ToString() ?? string.Empty,
                foreground is not null && background is not null
                    ? Math.Round(ContrastRatio(foreground.Color, background.Color), 3)
                    : 0,
                foregroundUsesMutedBrush,
                actualBackgroundMatchesBackdrop,
                transparentAncestors,
                textWithinBackdrop,
                headingForeground?.Color.ToString() ?? string.Empty,
                headingForeground is not null && background is not null
                    ? Math.Round(ContrastRatio(headingForeground.Color, background.Color), 3)
                    : 0,
                ReferenceEquals(heading.Foreground, Application.Current.FindResource("InkBrush")),
                heading.IsVisible && headingBounds.Width > 0 && headingBounds.Height > 0 &&
                    backdropBounds.Contains(headingBounds)));

            return new CountdownEmptyStateContrastValidation(
                measurements.All(measurement =>
                    measurement.ContrastRatio >= 4.5 &&
                    measurement.ForegroundUsesMutedBrush &&
                    measurement.ActualBackgroundMatchesBackdrop &&
                    measurement.TransparentAncestors &&
                    measurement.TextWithinBackdrop &&
                    measurement.HeadingContrastRatio >= 4.5 &&
                    measurement.HeadingUsesInkBrush &&
                    measurement.HeadingTextWithinBackdrop),
                measurements);
        }
        finally
        {
            emptyMessage.Visibility = originalVisibility;
            heading.Visibility = originalHeadingVisibility;
            content.Visibility = originalContentVisibility;
            rows.Visibility = originalRowsVisibility;
            main.ShowWorkspace(originalWorkspace);
            theme.Apply();
            main.ApplyTheme();
            main.UpdateLayout();
            PumpDispatcher();
        }
    }

    private static NoteEmptyStateContrastValidation ValidateNoteEmptyStateContrast(
        ApplicationTheme theme,
        MainWindow main)
    {
        var heading = Required<TextBlock>(main, "EmptyNotesMessage");
        var emptyMessage = Required<TextBlock>(main, "EmptyNotesHint");
        var content = Required<Grid>(main, "NotesEmptyStateContent");
        var activeRows = Required<StackPanel>(main, "ActiveNoteRows");
        var completedRows = Required<StackPanel>(main, "CompletedNoteRows");
        var workspace = Required<Canvas>(main, "NotesWorkspace");
        var originalWorkspace = main.CurrentWorkspace;
        var originalVisibility = emptyMessage.Visibility;
        var originalHeadingVisibility = heading.Visibility;
        var originalContentVisibility = content.Visibility;
        var originalActiveRowsVisibility = activeRows.Visibility;
        var originalCompletedRowsVisibility = completedRows.Visibility;
        var measurements = new List<CountdownEmptyStateContrastMeasurement>();
        try
        {
            main.ShowWorkspace(WorkspaceKind.Notes);
            content.Visibility = Visibility.Visible;
            activeRows.Visibility = Visibility.Collapsed;
            completedRows.Visibility = Visibility.Collapsed;
            heading.Visibility = Visibility.Visible;
            emptyMessage.Visibility = Visibility.Visible;
            theme.Apply();
            main.ApplyTheme();
            main.UpdateLayout();
            PumpDispatcher();

            var foreground = emptyMessage.Foreground as SolidColorBrush;
            var background = main.Background as SolidColorBrush;
            var foregroundUsesMutedBrush =
                ReferenceEquals(emptyMessage.Foreground, Application.Current.FindResource("MutedBrush"));
            var actualBackgroundMatchesBackdrop =
                ReferenceEquals(main.Background, Application.Current.FindResource("BackgroundBrush"));
            var transparentAncestors = EmptyStateAncestorsAreTransparent(emptyMessage, workspace) &&
                EmptyStateAncestorsAreTransparent(heading, workspace);
            var textBounds = BoundsIn(emptyMessage, main);
            var backdropBounds = BoundsIn(workspace, main);
            var textWithinBackdrop = emptyMessage.IsVisible && textBounds.Width > 0 &&
                textBounds.Height > 0 && backdropBounds.Contains(textBounds);
            var headingForeground = heading.Foreground as SolidColorBrush;
            var headingBounds = BoundsIn(heading, main);

            measurements.Add(new CountdownEmptyStateContrastMeasurement(
                "Day",
                foreground?.Color.ToString() ?? string.Empty,
                background?.Color.ToString() ?? string.Empty,
                foreground is not null && background is not null
                    ? Math.Round(ContrastRatio(foreground.Color, background.Color), 3)
                    : 0,
                foregroundUsesMutedBrush,
                actualBackgroundMatchesBackdrop,
                transparentAncestors,
                textWithinBackdrop,
                headingForeground?.Color.ToString() ?? string.Empty,
                headingForeground is not null && background is not null
                    ? Math.Round(ContrastRatio(headingForeground.Color, background.Color), 3)
                    : 0,
                ReferenceEquals(heading.Foreground, Application.Current.FindResource("InkBrush")),
                heading.IsVisible && headingBounds.Width > 0 && headingBounds.Height > 0 &&
                    backdropBounds.Contains(headingBounds)));

            return new NoteEmptyStateContrastValidation(
                measurements.All(measurement =>
                    measurement.ContrastRatio >= 4.5 &&
                    measurement.ForegroundUsesMutedBrush &&
                    measurement.ActualBackgroundMatchesBackdrop &&
                    measurement.TransparentAncestors &&
                    measurement.TextWithinBackdrop &&
                    measurement.HeadingContrastRatio >= 4.5 &&
                    measurement.HeadingUsesInkBrush &&
                    measurement.HeadingTextWithinBackdrop),
                measurements);
        }
        finally
        {
            emptyMessage.Visibility = originalVisibility;
            heading.Visibility = originalHeadingVisibility;
            content.Visibility = originalContentVisibility;
            activeRows.Visibility = originalActiveRowsVisibility;
            completedRows.Visibility = originalCompletedRowsVisibility;
            main.ShowWorkspace(originalWorkspace);
            theme.Apply();
            main.ApplyTheme();
            main.UpdateLayout();
            PumpDispatcher();
        }
    }

    private static bool ValidateNoteStatusBadgeTokensFollowState(
        ApplicationTheme theme,
        MainWindow main)
    {
        var activeSegment = Required<ToggleButton>(main, "ActiveNotesSegment");
        var completedSegment = Required<ToggleButton>(main, "CompletedNotesSegment");
        var activeBadge = Required<Border>(main, "ActiveNotesBadge");
        var completedBadge = Required<Border>(main, "CompletedNotesBadge");
        try
        {
            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            return
                activeSegment.IsChecked == true &&
                BadgeFollowsSegmentState(activeBadge, ParseBadgeCount(activeSegment), isActive: true) &&
                BadgeFollowsSegmentState(completedBadge, ParseBadgeCount(completedSegment), isActive: false);
        }
        finally
        {
            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
        }
    }

    private static int ParseBadgeCount(ToggleButton segment)
    {
        return segment.Content is Grid content &&
            content.Children
                .OfType<Border>()
                .FirstOrDefault(border => border.Child is TextBlock)?.Child is TextBlock count &&
            int.TryParse(count.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
    }

    private static bool EmptyStateAncestorsAreTransparent(
        DependencyObject element,
        DependencyObject stopAt)
    {
        for (var current = VisualTreeHelper.GetParent(element);
             current is not null && !ReferenceEquals(current, stopAt);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is Panel { Background: { } panelBackground } &&
                !IsTransparent(panelBackground))
            {
                return false;
            }

            if (current is Control { Background: { } controlBackground } &&
                !IsTransparent(controlBackground))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTransparent(Brush brush) =>
        brush is SolidColorBrush solid && solid.Color.A == 0;

    private static string BrushColor(Brush? brush) =>
        brush is SolidColorBrush solidColorBrush
            ? solidColorBrush.Color.ToString()
            : string.Empty;

    private static bool BrushMatchesExpected(Brush? actual, object expected) =>
        ReferenceEquals(actual, expected) ||
        (actual is SolidColorBrush actualBrush &&
         expected is SolidColorBrush expectedBrush &&
         actualBrush.Color == expectedBrush.Color);

    private static double CompositedContrast(Brush foreground, double opacity, Brush background)
    {
        if (foreground is not SolidColorBrush fg || background is not SolidColorBrush bg ||
            bg.Color.A != 255 || bg.Opacity != 1) return 0;
        var alpha = fg.Color.A / 255d * fg.Opacity * opacity;
        byte Channel(byte front, byte back) => (byte)Math.Round(front * alpha + back * (1 - alpha));
        var visible = Color.FromRgb(Channel(fg.Color.R, bg.Color.R),
            Channel(fg.Color.G, bg.Color.G), Channel(fg.Color.B, bg.Color.B));
        return ContrastRatio(visible, bg.Color);
    }

    private static double RenderedOuterContrast(FrameworkElement root, Border border, bool verticalOnly = false)
    {
        var frame = CaptureVisual(root);
        var bounds = BoundsIn(border, root);
        var neighbors = new List<Point>();
        foreach (var fraction in new[] { .25, .5, .75 })
        {
            var y = bounds.Top + bounds.Height * fraction;
            neighbors.Add(new Point(bounds.Left - 1, y));
            neighbors.Add(new Point(bounds.Right + 1, y));
            if (!verticalOnly)
            {
                var x = bounds.Left + bounds.Width * fraction;
                neighbors.Add(new Point(x, bounds.Top - 1));
                neighbors.Add(new Point(x, bounds.Bottom + 1));
            }
        }
        var minimum = double.MaxValue;
        foreach (var point in neighbors)
        {
            var x = (int)Math.Floor(point.X);
            var y = (int)Math.Floor(point.Y);
            if (x < 0 || y < 0 || x >= frame.PixelWidth || y >= frame.PixelHeight)
            {
                Console.Error.WriteLine($"RASTER_SAMPLE_OUTSIDE: point={x},{y}; bounds={bounds}; frame={frame.PixelWidth}x{frame.PixelHeight}");
                return 0;
            }
            var pixel = new byte[4];
            frame.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            // A transparent frame is not a color. Fail closed rather than interpreting its zero RGB as black.
            if (pixel[3] != 255)
            {
                Console.Error.WriteLine($"RASTER_SAMPLE_ALPHA: point={x},{y}; alpha={pixel[3]}; bounds={bounds}; frame={frame.PixelWidth}x{frame.PixelHeight}; opacity={Window.GetWindow(root)?.Opacity}");
                return 0;
            }
            var neighbor = new SolidColorBrush(Color.FromRgb(pixel[2], pixel[1], pixel[0]));
            minimum = Math.Min(minimum, CompositedContrast(border.BorderBrush, border.Opacity, neighbor));
        }
        return minimum;
    }

    private static bool ValidateRasterNeighborContrastSensitivity()
    {
        var image = new Image { Stretch = Stretch.Fill };
        var border = new Border { Margin = new Thickness(16), BorderThickness = new Thickness(2),
            BorderBrush = (Brush)Application.Current.FindResource("ControlBorderBrush"), Background = Brushes.Transparent };
        var root = new Grid { Width = 64, Height = 64 };
        root.Children.Add(image);
        root.Children.Add(border);
        var host = new Window { Content = root, Width = 64, Height = 64, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Left = -20_000, Top = -20_000, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            host.Show();
            var edgeColor = ((SolidColorBrush)border.BorderBrush).Color;
            var colors = new[] { Colors.White, Colors.Black, edgeColor };
            var measured = new List<double>();
            foreach (var color in colors)
            {
                var pixels = new byte[64 * 64 * 4];
                for (var index = 0; index < pixels.Length; index += 4)
                {
                    pixels[index] = color.B;
                    pixels[index + 1] = color.G;
                    pixels[index + 2] = color.R;
                    pixels[index + 3] = 255;
                }
                image.Source = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
                root.UpdateLayout();
                PumpDispatcher();
                measured.Add(RenderedOuterContrast(root, border));
            }
            // Two different raster neighbors must change the reading. Matching the boundary is a real failing fixture.
            Console.Error.WriteLine($"RASTER_NEIGHBOR_CONTRAST: {string.Join(",", measured)}");
            return measured[0] >= 3 && measured[1] >= 3 && measured[2] < 1.01 &&
                Math.Abs(measured[0] - measured[1]) > .5;
        }
        finally { host.Close(); }
    }

    private static bool ValidateSystemContrastUpdatesOpenWindows(MainWindow main, FloatingWindow floating)
    {
        static bool ColorIs(Brush? brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;
        var mainInput = (TextBox)main.FindName("CountdownName");
        var floatingSurface = (Border)floating.FindName("TaskCard");
        var originalText = mainInput.Text;
        var originalBackground = ((SolidColorBrush)main.Background).Color;
        bool enabled;
        using (ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            PumpDispatcher();
            enabled = ColorIs(main.Background, SystemColors.WindowColor) &&
                ColorIs(mainInput.Foreground, SystemColors.WindowTextColor) &&
                ColorIs(mainInput.BorderBrush, SystemColors.WindowTextColor) &&
                ColorIs(floatingSurface.Background, SystemColors.WindowColor) &&
                ((FrameworkElement)main.FindName("MainReferenceDecorationLayer")).Visibility == Visibility.Collapsed &&
                ((FrameworkElement)floating.FindName("FloatingDayRasterScene")).Visibility == Visibility.Collapsed &&
                mainInput.Text == originalText;
            Console.Error.WriteLine($"SYSTEM_CONTRAST_COLORS: main={main.Background}; input={mainInput.Foreground}; border={mainInput.BorderBrush}; floating={floatingSurface.Background}; window={SystemColors.WindowColor}; text={SystemColors.WindowTextColor}; mainDecor={((FrameworkElement)main.FindName("MainReferenceDecorationLayer")).Visibility}; floatingDecor={((FrameworkElement)floating.FindName("FloatingDayRasterScene")).Visibility}");
        }
        PumpDispatcher();
        var restored = ColorIs(main.Background, originalBackground) &&
            ReferenceEquals(mainInput, main.FindName("CountdownName")) && mainInput.Text == originalText;
        Console.Error.WriteLine($"SYSTEM_CONTRAST_OPEN_WINDOWS: enabled={enabled}; restored={restored}");
        return enabled && restored;
    }

    private static bool ValidateSystemContrastAccessibleStates(MainWindow main, FloatingWindow floating)
    {
        static bool ColorIs(Brush? brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        var workspace = main.CurrentWorkspace;
        var focus = Keyboard.FocusedElement;
        var input = Required<TextBox>(main, "CountdownName");
        var originalText = input.Text;
        var floatingVisible = floating.IsVisible;
        var now = new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero);
        var reminder = new ReminderCenterWindow { ShowInTaskbar = false, Opacity = 0 };
        reminder.AddEntry(new ReminderCenterItem(new ActiveReminderOccurrenceKey("contrast-rule", now),
            new NoteRecord("contrast-note", "系统对比度提醒", "2031-04-05", false, [], NoReminder.Instance), 0, 0));
        reminder.SetDeliveryReady(new ActiveReminderOccurrenceKey("contrast-rule", now));
        var editor = new ReminderRulesEditorWindow([], new ValidationClock(now))
            { Owner = main, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        var dialog = new ConfirmationDialog(main, "对比度确认", "保留用户草稿与当前选区")
            { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            if (!floatingVisible) floating.Show();
            reminder.Show(); editor.Show(); dialog.Show();
            PumpDispatcher();
            var reminderSurface = (Border)reminder.Content;
            var editorSurface = (Border)editor.Content;
            var dialogSurface = (Border)dialog.Content;
            var dayColors = new[] { reminderSurface.Background, editorSurface.Background, dialog.Background }
                .Select(brush => ((SolidColorBrush)brush).Color).ToArray();
            var originalRows = Descendants<FrameworkElement>(reminder)
                .Where(element => AutomationProperties.GetAutomationId(element) == "ReminderEntryPendingBadge").ToArray();
            input.Text = "对比度草稿保留";
            input.Select(1, 3);
            main.Activate(); Keyboard.Focus(input); PumpDispatcher();
            var wasFocused = input.IsKeyboardFocused;
            var generation = main.CountdownRowRebuildGenerationForValidation;
            using (ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                PumpDispatcher();
                checks["DraftSelectionFocus"] = wasFocused && input.IsKeyboardFocused &&
                    input.Text == "对比度草稿保留" && input.SelectionStart == 1 && input.SelectionLength == 3 &&
                    main.CountdownRowRebuildGenerationForValidation == generation;
                checks["ReminderSurface"] = ColorIs(reminderSurface.Background, SystemColors.WindowColor) &&
                    ColorIs(reminderSurface.BorderBrush, SystemColors.WindowTextColor) && reminder.EntryCount == 1 &&
                    Descendants<TextBlock>(reminder).Any(text => text.Text == "系统对比度提醒" && ColorIs(text.Foreground, SystemColors.WindowTextColor));
                checks["RuleEditorSurface"] = ColorIs(editorSurface.Background, SystemColors.WindowColor) &&
                    ColorIs(editorSurface.BorderBrush, SystemColors.WindowTextColor) &&
                    Descendants<TextBlock>(editor).Any(text => text.Text == "提醒规则" && ColorIs(text.Foreground, SystemColors.WindowTextColor));
                checks["ConfirmationSurface"] = ColorIs(dialog.Background, SystemColors.WindowColor) &&
                    ColorIs(dialog.Foreground, SystemColors.WindowTextColor) &&
                    ColorIs(dialogSurface.BorderBrush, SystemColors.WindowTextColor) &&
                    ColorIs(dialog.BodyText.Foreground, SystemColors.WindowTextColor);
                checks["TextSelection"] = ColorIs(input.SelectionBrush, SystemColors.HighlightColor) &&
                    input.SelectionOpacity == 1 && ColorIs(input.SelectionTextBrush, SystemColors.HighlightTextColor);
                var actions = new (string Name, Window Window, Button Button)[]
                {
                    ("Main", main, Required<Button>(main, "SaveCountdownButton")),
                    ("Floating", floating, Required<Button>(floating, "OpenManagerButton")),
                    ("Reminder", reminder, Descendants<Button>(reminder).Single(button => AutomationProperties.GetAutomationId(button) == "ReminderCenterAcknowledgeAll")),
                    ("Rules", editor, Descendants<Button>(editor).First(button => button.IsVisible && ReferenceEquals(button.Style, Application.Current.FindResource("PrimaryButton")))),
                    ("Confirmation", dialog, dialog.CancelButton)
                };
                foreach (var action in actions)
                {
                    action.Window.Activate(); Keyboard.Focus(action.Button);
                    FocusVisualState.SetIsKeyboardFocusVisible(action.Button, true);
                    PumpDispatcher();
                    var ring = action.Button.Template?.FindName("FocusRing", action.Button) as Border;
                    checks[action.Name + "Focus"] = action.Button.IsKeyboardFocused && ring is not null &&
                        ColorIs(ring.BorderBrush, SystemColors.HighlightColor);
                    if (!checks[action.Name + "Focus"])
                        Console.Error.WriteLine($"SYSTEM_CONTRAST_FOCUS {action.Name}: keyboard={action.Button.IsKeyboardFocused}; ring={ring?.BorderBrush}; visible={action.Button.IsVisible}; state={action.Window.WindowState}");
                    var enabled = action.Button.IsEnabled;
                    action.Button.SetCurrentValue(UIElement.IsEnabledProperty, false); PumpDispatcher();
                    checks[action.Name + "Disabled"] = ColorIs(action.Button.Foreground, SystemColors.GrayTextColor);
                    action.Button.SetCurrentValue(UIElement.IsEnabledProperty, enabled);
                }
                var toggle = Required<ToggleButton>(floating, "FloatingCountdownsButton");
                var originalChecked = toggle.IsChecked;
                toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true); PumpDispatcher();
                var selected = toggle.Template?.FindName("Surface", toggle) as Border;
                checks["FloatingSelection"] = selected is not null && ColorIs(selected.Background, SystemColors.HighlightColor) &&
                    ColorIs(toggle.Foreground, SystemColors.HighlightTextColor);
                toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, originalChecked);
                var mode = Descendants<ComboBox>(editor).Single(combo => AutomationProperties.GetAutomationId(combo) == "ReminderRuleMode");
                editor.Activate(); mode.IsDropDownOpen = true; PumpDispatcher();
                var selectedItem = mode.ItemContainerGenerator.ContainerFromIndex(mode.SelectedIndex) as ComboBoxItem;
                var selectedSurface = selectedItem?.Template?.FindName("Surface", selectedItem) as Border;
                checks["RulePopupSelection"] = mode.IsDropDownOpen && selectedSurface is not null &&
                    ColorIs(selectedSurface.Background, SystemColors.HighlightColor) &&
                    ColorIs(selectedItem!.Foreground, SystemColors.HighlightTextColor);
                mode.IsDropDownOpen = false;
                main.ShowWorkspace(WorkspaceKind.Notes); PumpDispatcher();
                checks["DecorationsStayHiddenAfterNavigation"] = new[] { "MainReferenceDecorationLayer", "CharacterBackgroundLayer", "CharacterPanelLayer", "CountdownDecoTextTop", "CountdownDecoTextBottom", "NotesDecoText", "CountdownEmptyMascot", "NotesEmptyMascot" }
                    .All(name => !Required<FrameworkElement>(main, name).IsVisible) &&
                    new[] { "FloatingDayRasterScene", "FloatingPlushScene", "FloatingNotesDecoTextLeft", "FloatingNotesDecoText" }
                    .All(name => !Required<FrameworkElement>(floating, name).IsVisible);
            }
            PumpDispatcher();
            checks["AllSurfacesRestore"] = ColorIs(reminderSurface.Background, dayColors[0]) &&
                ColorIs(editorSurface.Background, dayColors[1]) && ColorIs(dialog.Background, dayColors[2]);
            checks["RowsRemain"] = originalRows.Length == 1 && originalRows.SequenceEqual(Descendants<FrameworkElement>(reminder)
                .Where(element => AutomationProperties.GetAutomationId(element) == "ReminderEntryPendingBadge"));
            checks["FloatingRasterRestores"] = Required<FrameworkElement>(floating, "FloatingDayRasterScene").Visibility == Visibility.Visible;
        }
        finally
        {
            dialog.Close(); editor.Close(); reminder.ForceClose();
            if (!floatingVisible) floating.Hide();
            input.Text = originalText;
            main.ShowWorkspace(workspace);
            if (focus is UIElement previous) Keyboard.Focus(previous);
        }
        Console.Error.WriteLine("SYSTEM_CONTRAST_STATES: " + JsonSerializer.Serialize(checks));
        return checks.Count > 15 && checks.Values.All(value => value);
    }

    private static bool ValidateSystemContrastCustomSurfaces(string assetRoot)
    {
        static bool ColorIs(Brush? brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"MinminCountdown-system-contrast-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(temporaryRoot, "settings.json");
        var diagnosticPath = Path.Combine(temporaryRoot, "validation.jsonl");
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        MainWindow? main = null;
        FloatingWindow? floating = null;
        FloatingWindow? laterFloating = null;
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var now = new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero);
            var store = new SettingsStore(clock: new ValidationClock(now));
            store.Save(settingsPath, new CountdownSettings([], [new NoteRecord("contrast-note", "系统色日期印记", "2031-04-05", false, ["contrast-tag"], NoReminder.Instance)],
                [new TagRecord("contrast-tag", "系统色标签", "#FFD36B")], CountdownSettings.Empty.Preferences));
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            using var calendar = new HolidayCalendar(temporaryRoot);
            main = new MainWindow(workspace, calendar, new DiagnosticLogger(diagnosticPath), assetRoot)
                { ShowInTaskbar = false, Opacity = 0 };
            floating = new FloatingWindow(workspace, calendar, main, assetRoot)
                { ShowInTaskbar = false, Opacity = 0 };
            main.Show(); floating.Show(); main.ShowWorkspace(WorkspaceKind.Notes); floating.ShowWorkspace(WorkspaceKind.Notes);
            PumpDispatcher();
            var chip = Descendants<Border>(main).First(border => border.Uid == "contrast-tag");
            var stamp = Descendants<Border>(floating).First(border => border.BorderThickness == new Thickness(2, 0, 0, 0));
            var oldChip = ((SolidColorBrush)chip.Background).Color;
            var oldStampEdge = ((SolidColorBrush)stamp.BorderBrush).Color;
            var noteRows = Required<StackPanel>(floating, "FloatingRows").Children.Cast<UIElement>().ToArray();
            Border? laterStamp = null;
            floating.SetQuickAddExpanded(true);
            var picker = Required<DatePicker>(floating, "FloatingNoteDate");
            picker.SelectedDate = new DateTime(2031, 4, 5);
            picker.DisplayDate = picker.SelectedDate.Value;
            picker.IsDropDownOpen = true; PumpDispatcher();
            var calendarPopup = PickerTemplateParts.GetCalendar(picker)!;
            var selectedDay = Descendants<CalendarDayButton>(calendarPopup).Single(day => day.IsSelected);
            floating.Activate(); Keyboard.Focus(selectedDay); PumpDispatcher();
            var daySurface = (Border)selectedDay.Template.FindName("Surface", selectedDay);
            // 主题画刷必须冻结并整枚替换：就地改色会让渲染线程遍历共享画刷的监听者（0xC0000409 崩溃路径）。
            var dayCanvas = (SolidColorBrush)Application.Current.FindResource("CanvasBrush");
            using (var preference = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                PumpDispatcher();
                checks["OpenDatePopupMaps"] = picker.IsDropDownOpen &&
                    ColorIs(calendarPopup.Background, SystemColors.WindowColor) &&
                    ColorIs(calendarPopup.BorderBrush, SystemColors.WindowTextColor) &&
                    ColorIs(daySurface.Background, SystemColors.HighlightColor) &&
                    ColorIs(selectedDay.Foreground, SystemColors.HighlightTextColor);
                checks["OpenDatePopupFocus"] = selectedDay.IsKeyboardFocused &&
                    ColorIs(daySurface.BorderBrush, SystemColors.HighlightColor);
                checks["TagMaps"] = ColorIs(chip.Background, SystemColors.ControlColor);
                checks["StampMaps"] = ColorIs(stamp.BorderBrush, SystemColors.WindowTextColor);
                var palette = new ApplicationTheme.SystemContrastPalette(
                    Color.FromRgb(8, 18, 32), Colors.White, Color.FromRgb(20, 30, 46), Colors.White,
                    Colors.Yellow, Colors.Black, Colors.Silver);
                preference.ChangeColors(palette); PumpDispatcher();
                checks["PaletteReplacesFrozenBrushes"] = dayCanvas.IsFrozen &&
                    new[] { "CanvasBrush", "FloatingDaySurfaceBrush", "SystemContrastControlBrush" }
                        .All(key => Application.Current.FindResource(key) is SolidColorBrush { IsFrozen: true }) &&
                    Application.Current.FindResource("CanvasBrush") is SolidColorBrush changedCanvas &&
                    !ReferenceEquals(changedCanvas, dayCanvas) && changedCanvas.Color == palette.Window;
                checks["OpenDatePopupChangesColor"] = picker.IsDropDownOpen &&
                    ColorIs(calendarPopup.Background, palette.Window) &&
                    ColorIs(daySurface.Background, palette.Highlight) &&
                    ColorIs(daySurface.BorderBrush, palette.Highlight) &&
                    ColorIs(selectedDay.Foreground, palette.HighlightText);
                picker.IsDropDownOpen = false;
                checks["ChangedWhileEnabled"] = ColorIs(main.Background, palette.Window) &&
                    ColorIs(Required<Border>(floating, "TaskCard").Background, palette.Window) &&
                    ColorIs(chip.Background, palette.Control) && ColorIs(stamp.BorderBrush, palette.WindowText);
                laterFloating = new FloatingWindow(workspace, calendar, main, assetRoot) { ShowInTaskbar = false, Opacity = 0 };
                laterFloating.Show(); laterFloating.ShowWorkspace(WorkspaceKind.Notes); PumpDispatcher();
                laterStamp = Descendants<Border>(laterFloating).First(border => border.BorderThickness == new Thickness(2, 0, 0, 0));
                checks["LaterStampUsesSystemColor"] = ColorIs(laterStamp.BorderBrush, palette.WindowText);
                var newDialog = new ConfirmationDialog(main, "新建窗口", "使用当前系统颜色") { ShowActivated = false, Opacity = 0 };
                try
                {
                    newDialog.Show(); PumpDispatcher();
                    checks["NewWindowUsesCurrentPalette"] = ColorIs(newDialog.Background, palette.Window) &&
                        ColorIs(newDialog.BodyText.Foreground, palette.WindowText);
                    newDialog.CancelButton.IsEnabled = false; PumpDispatcher();
                    checks["ChangedDisabledColor"] = ColorIs(newDialog.CancelButton.Foreground, palette.GrayText);
                }
                finally { newDialog.Close(); }
                preference.ChangeColors(palette with { Highlight = Colors.Cyan, Window = Colors.Black }); PumpDispatcher();
                checks["SecondColorNotification"] = ColorIs(main.Background, Colors.Black) &&
                    ColorIs(Required<Border>(floating, "TaskCard").Background, Colors.Black);
                var lightPalette = new ApplicationTheme.SystemContrastPalette(
                    Colors.White, Colors.Black, Colors.Black, Colors.White,
                    Colors.Navy, Colors.White, Colors.Gray);
                preference.ChangeColors(lightPalette); PumpDispatcher();
                checks["LightTagTextPair"] = ColorIs(chip.Background, lightPalette.Control) &&
                    chip.Child is TextBlock chipText && ColorIs(chipText.Foreground, lightPalette.ControlText);
                checks["LightFloatingTextPair"] = ColorIs(Required<Border>(floating, "TaskCard").Background, lightPalette.Window) &&
                    ColorIs(Required<TextBlock>(floating, "FloatingIncome").Foreground, lightPalette.WindowText);
                var secondary = Required<Button>(main, "SavePreferencesButton");
                checks["LightSecondaryTextPair"] = ColorIs(secondary.Background, lightPalette.Window) &&
                    ColorIs(secondary.Foreground, lightPalette.WindowText);
                var preferencesPopup = Required<Popup>(main, "PreferencesPopup");
                preferencesPopup.IsOpen = true; PumpDispatcher();
                secondary.IsEnabled = false; PumpDispatcher();
                var disabledSurface = secondary.Template.FindName("Surface", secondary) as Border;
                checks["LightDisabledBoundary"] = preferencesPopup.IsOpen && disabledSurface is not null &&
                    ColorIs(disabledSurface.Background, lightPalette.Control) &&
                    ColorIs(disabledSurface.BorderBrush, lightPalette.GrayText) &&
                    ColorIs(secondary.Foreground, lightPalette.GrayText);
                if (!checks["LightDisabledBoundary"])
                    Console.Error.WriteLine($"SYSTEM_CONTRAST_DISABLED: open={preferencesPopup.IsOpen}; enabled={secondary.IsEnabled}; inherited={ApplicationTheme.GetIsSystemContrast(secondary)}; foreground={secondary.Foreground}; background={secondary.Background}; border={secondary.BorderBrush}; actualBackground={disabledSurface?.Background}; actualBorder={disabledSurface?.BorderBrush}");
                secondary.IsEnabled = true; preferencesPopup.IsOpen = false;
            }
            PumpDispatcher();
            checks["CustomColorsRestore"] = ColorIs(chip.Background, oldChip) && ColorIs(stamp.BorderBrush, oldStampEdge);
            checks["LaterStampRestoresDayColor"] = laterStamp is not null && ColorIs(laterStamp.BorderBrush, oldStampEdge);
            checks["CustomRowsRemain"] = noteRows.SequenceEqual(Required<StackPanel>(floating, "FloatingRows").Children.Cast<UIElement>());
        }
        finally
        {
            laterFloating?.Close(); floating?.Close(); main?.Close();
            TryDeleteSettingsFiles(settingsPath); TryDeleteFile(diagnosticPath);
            var cache = Path.Combine(temporaryRoot, "holiday-cache");
            if (Directory.Exists(cache) && !Directory.EnumerateFileSystemEntries(cache).Any()) Directory.Delete(cache);
            if (!Directory.EnumerateFileSystemEntries(temporaryRoot).Any()) Directory.Delete(temporaryRoot);
        }
        Console.Error.WriteLine("SYSTEM_CONTRAST_CUSTOM: " + JsonSerializer.Serialize(checks));
        return checks.Count == 18 && checks.Values.All(value => value);
    }

    private static IReadOnlyDictionary<string, bool> ValidateIdleControlBoundaries(MainWindow main)
    {
        var projection = Required<FrameworkElement>(main, "MainContentProjection");
        var controls = new (string Name, Control Control, string Part)[]
        {
            ("CheckBox", new CheckBox(), "Box"),
            ("Pin", new CheckBox { Style = (Style)projection.FindResource("MainTopmostCheck") }, "Box"),
            ("Completion", new CheckBox { Style = (Style)projection.FindResource("MainNoteCompletionCheck") }, "Box"),
            ("Radio", new RadioButton(), "Ring"),
            ("MainRadio", new RadioButton { Style = (Style)projection.FindResource("MainReminderModeRadio") }, "Ring"),
            ("Combo", new ComboBox(), "Surface"),
            ("Date", new WpfUiDatePicker(), "PART_TextBox"),
            ("Secondary", new Button { Style = (Style)Application.Current.FindResource("SecondaryButton") }, "Surface"),
            ("MainSecondary", new Button { Style = (Style)projection.FindResource("MainSecondaryButton") }, "Surface"),
            ("SavePreferencesSecondary", new Button { Style = Required<Button>(main, "SavePreferencesButton").Style }, "Surface"),
            ("ScrollBar", new ScrollBar { Maximum = 100, ViewportSize = 20, Height = 100 }, "PART_Track")
        };
        var panel = new StackPanel();
        foreach (var (_, control, _) in controls) panel.Children.Add(control);
        var host = new Window { Content = panel, Width = 240, Height = 700, ShowInTaskbar = false,
            Left = -20_000, Top = -20_000, WindowStartupLocation = WindowStartupLocation.Manual, Opacity = 0 };
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            checks["RasterNeighborContrastSensitivity"] = ValidateRasterNeighborContrastSensitivity();
            host.Show();
            PumpDispatcher();
            foreach (var (name, control, partName) in controls)
            {
                control.ApplyTemplate();
                var part = control.Template?.FindName(partName, control);
                Brush? boundary = part switch
                {
                    Border border => border.BorderBrush,
                    System.Windows.Shapes.Shape shape => shape.Stroke,
                    TextBox text => text.BorderBrush,
                    Track track => Descendants<Border>(track.Thumb).FirstOrDefault()?.Background,
                    _ => null
                };
                checks[name] = boundary is not null &&
                    BrushMatchesExpected(boundary, Application.Current.FindResource("ControlBorderBrush")) &&
                    new[] { "BackgroundBrush", "CanvasBrush", "CardBrush", "NoteBrush", "NoteWhiteBrush" }
                        .All(key => ApplicationTheme.MeetsContrast((Brush)Application.Current.FindResource(key), boundary, 3));
                if (name.Contains("Secondary", StringComparison.Ordinal))
                {
                    checks[name] &= part is Border surface &&
                        BrushMatchesExpected(surface.Background, Application.Current.FindResource("CardAltBrush"));
                }
            }
            var editor = new ReminderRulesEditorWindow(
                [new AbsoluteOnceReminderRule("secondary-probe", new DateTimeOffset(2031, 1, 1, 1, 0, 0, TimeSpan.Zero))],
                new ValidationClock(new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            { Owner = main, ShowInTaskbar = false, Opacity = 0 };
            try
            {
                editor.Show();
                PumpDispatcher();
                var edit = Descendants<Button>(editor).Single(button => Equals(button.Content, "编辑"));
                edit.Command.Execute(edit.CommandParameter);
                PumpDispatcher();
                foreach (var id in new[] { "CancelReminderRulesButton", "CancelReminderRuleEditButton" })
                {
                    var button = Descendants<Button>(editor).Single(control => AutomationProperties.GetAutomationId(control) == id);
                    button.ApplyTemplate();
                    checks[id] = button.IsVisible && button.IsEnabled &&
                        button.Template.FindName("Surface", button) is Border surface &&
                        BrushMatchesExpected(surface.Background, Application.Current.FindResource("CardAltBrush")) &&
                        BrushMatchesExpected(surface.BorderBrush, Application.Current.FindResource("ControlBorderBrush"));
                }
            }
            finally { editor.Close(); }
            Console.Error.WriteLine($"IDLE_BOUNDARY_FAILURES: {string.Join(",", checks.Where(pair => !pair.Value).Select(pair => pair.Key))}");
            return checks;
        }
        finally { host.Close(); }
    }

    private static IReadOnlyDictionary<string, bool> ValidateSelectedControlSignals(MainWindow main)
    {
        var projection = Required<FrameworkElement>(main, "MainContentProjection");
        var completionStyle = Application.Current.TryFindResource("CompletionCheckBox") as Style;
        var chip = new ToggleButton { Content = "已完成", Style = Required<ToggleButton>(main, "NoteCompletedFilter").Style };
        var completion = new CheckBox { Style = (Style)projection.FindResource("MainNoteCompletionCheck") };
        var floatingCompletion = new CheckBox { Style = completionStyle };
        var radio = new RadioButton();
        var panel = new StackPanel();
        foreach (var control in new Control[] { chip, completion, floatingCompletion, radio }) panel.Children.Add(control);
        var host = new Window { Content = panel, Width = 240, Height = 240, ShowInTaskbar = false,
            Left = -20_000, Top = -20_000, WindowStartupLocation = WindowStartupLocation.Manual, Opacity = 0 };
        using var motion = ApplicationTheme.OverrideAnimationPreferenceForValidation(false);
        try
        {
            host.Show();
            PumpDispatcher();
            var tick = chip.Template?.FindName("SelectedTick", chip) as WpfPath;
            var absentBeforeSelection = tick is null || tick.Visibility != Visibility.Visible;
            chip.IsChecked = true;
            completion.IsChecked = true;
            floatingCompletion.IsChecked = true;
            radio.IsChecked = true;
            PumpDispatcher();
            tick = chip.Template?.FindName("SelectedTick", chip) as WpfPath;
            var surface = chip.Template?.FindName("Surface", chip) as Border;
            bool CompletionUsesSuccess(CheckBox control)
            {
                var box = control.Template?.FindName("Box", control) as Border;
                var mark = control.Template?.FindName("Tick", control) as WpfPath;
                return box is not null && mark?.Visibility == Visibility.Visible &&
                    BrushMatchesExpected(box.Background, Application.Current.FindResource("SuccessBrush")) &&
                    ApplicationTheme.MeetsContrast(box.Background, mark.Stroke, 3) &&
                    ApplicationTheme.MeetsContrast((Brush)Application.Current.FindResource("BackgroundBrush"), box.Background, 3);
            }
            var dot = radio.Template?.FindName("Dot", radio) as System.Windows.Shapes.Ellipse;
            return new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["Chip"] = absentBeforeSelection && tick?.Visibility == Visibility.Visible && surface is not null &&
                    BrushMatchesExpected(surface.Background, Application.Current.FindResource("PeachTintBrush")) &&
                    BrushMatchesExpected(surface.BorderBrush, Application.Current.FindResource("PeachRailBrush")) &&
                    ApplicationTheme.MeetsContrast(surface.Background, tick.Stroke, 3),
                ["MainCompletion"] = CompletionUsesSuccess(completion),
                ["FloatingCompletion"] = completionStyle is not null && CompletionUsesSuccess(floatingCompletion),
                ["Radio"] = dot?.Visibility == Visibility.Visible &&
                    ApplicationTheme.MeetsContrast((Brush)Application.Current.FindResource("BackgroundBrush"), dot.Fill, 3)
            };
        }
        finally { host.Close(); }
    }

    private static IReadOnlyDictionary<string, bool> ValidateFocusOuterNeighbors(MainWindow main)
    {
        var projection = Required<FrameworkElement>(main, "MainContentProjection");
        var controls = new (string Name, Control Control, string Part, string Expected)[]
        {
            ("Button", new Button(), "FocusRing", "ActiveBrush"),
            ("Primary", new Button { Style = (Style)Application.Current.FindResource("PrimaryButton") }, "FocusRing", "MainActionFocusBrush"),
            ("MainPrimary", new Button { Style = Required<Button>(main, "SaveCountdownButton").Style }, "FocusRing", "MainActionFocusBrush"),
            ("FloatingAccent", new Button { Style = (Style)Application.Current.FindResource("FloatingAccentButton") }, "FocusRing", "MainActionFocusBrush"),
            ("FloatingCapsule", new ToggleButton { IsChecked = true, Style = (Style)Application.Current.FindResource("FloatingHeaderSegmentButton") }, "FocusRing", "MainActionFocusBrush"),
            ("PinUnchecked", new CheckBox { IsChecked = false, Style = (Style)projection.FindResource("MainTopmostCheck") }, "Box", "ActiveBrush"),
            ("PinChecked", new CheckBox { IsChecked = true, Style = (Style)projection.FindResource("MainTopmostCheck") }, "Box", "MainActionFocusBrush")
        };
        var panel = new StackPanel();
        foreach (var (_, control, _, _) in controls)
        {
            control.MinHeight = 40;
            panel.Children.Add(control);
        }
        var host = new Window { Content = panel, Width = 240, Height = 360, ShowInTaskbar = false,
            Left = -20_000, Top = -20_000, WindowStartupLocation = WindowStartupLocation.Manual, Opacity = 0 };
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            host.Show();
            host.Activate();
            PumpDispatcher();
            foreach (var (name, control, part, expected) in controls)
            {
                Keyboard.Focus(control);
                FocusVisualState.SetIsKeyboardFocusVisible(control, true);
                PumpDispatcher();
                var ring = control.Template?.FindName(part, control) as Border;
                checks[name] = control.IsKeyboardFocused && ring is not null &&
                    ring.BorderThickness.Left >= 2 &&
                    BrushMatchesExpected(ring.BorderBrush, Application.Current.FindResource(expected)) &&
                    new[] { "BackgroundBrush", "CanvasBrush", "CardBrush", "NoteBrush", "NoteWhiteBrush", "CardAltBrush" }
                        .All(key => ApplicationTheme.MeetsContrast((Brush)Application.Current.FindResource(key), ring.BorderBrush, 3));
            }
            return checks;
        }
        finally { host.Close(); }
    }

    private static bool ValidateMainIdleTextInputBoundaryContrast(MainWindow main)
    {
        var input = Required<TextBox>(main, "CountdownName");
        var previousFocus = Keyboard.FocusedElement;
        try
        {
            // Measure the materialized idle border, rather than a palette entry that
            // the input might not actually consume (#217).
            Keyboard.Focus(Required<ToggleButton>(main, "CountdownTab"));
            PumpDispatcher();
            input.ApplyTemplate();
            if (input.IsKeyboardFocused ||
                input.Template?.FindName("Surface", input) is not Border surface ||
                surface.BorderBrush is not SolidColorBrush border ||
                surface.BorderThickness.Left <= 0)
            {
                return false;
            }

            return new[] { "BackgroundBrush", "CanvasBrush", "CardBrush", "NoteBrush", "NoteWhiteBrush" }
                .All(key => Application.Current.FindResource(key) is Brush background &&
                    ApplicationTheme.MeetsContrast(background, border, 3)) &&
                ApplicationTheme.MeetsContrast(surface.Background, border, 3);
        }
        finally
        {
            if (previousFocus is not null)
            {
                Keyboard.Focus(previousFocus);
                PumpDispatcher();
            }
        }
    }

    private static bool ValidateThemeContrast(ApplicationTheme theme, DateTimeOffset restoreUtc)
    {
        var dialogForeground = Color.FromRgb(0xFF, 0xFD, 0xFC);
        var dialogBackground = Color.FromRgb(0xA2, 0x44, 0x38);
        var dialogPair = ContrastRatio(dialogForeground, dialogBackground) >= 4.5;
        try
        {
            theme.Apply();
            var canvas = (Color)Application.Current.FindResource("CanvasColor");
            var ink = (Color)Application.Current.FindResource("InkColor");
            var body = (Color)Application.Current.FindResource("BodyColor");
            var muted = (Color)Application.Current.FindResource("MutedColor");
            var tag = (Color)Application.Current.FindResource("TagColor");
            var note = (Color)Application.Current.FindResource("NoteColor");
            var peachTint = (Color)Application.Current.FindResource("PeachTintColor");
            var segmentSelected = (Color)Application.Current.FindResource("SegmentSelectedColor");
            var segmentSelectedForeground =
                (Color)Application.Current.FindResource("SegmentSelectedForegroundColor");
            var pairs =
                ContrastRatio(canvas, ink) >= 7 &&
                ContrastRatio(canvas, body) >= 4.5 &&
                ContrastRatio(canvas, muted) >= 4.5 &&
                ContrastRatio(note, ink) >= 4.5 &&
                ContrastRatio(tag, (Color)Application.Current.FindResource("TagChipTextColor")) >= 4.5 &&
                ContrastRatio(segmentSelected, segmentSelectedForeground) >= 4.5;
            if (!pairs)
            {
                return false;
            }

            var focus = (Color)Application.Current.FindResource("ActiveColor");
            if (ContrastRatio(focus, canvas) < 3 ||
                ContrastRatio(focus, peachTint) < 3 ||
                ContrastRatio(focus, note) < 3)
            {
                return false;
            }

            return dialogPair;
        }
        finally
        {
            theme.Apply();
        }
    }

    private static ReminderCenterSourceLabelContrastValidation ValidateReminderCenterSourceLabelContrast(
        ApplicationTheme theme,
        DateTimeOffset restoreUtc)
    {
        var window = new ReminderCenterWindow
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        window.AddEntry(new ReminderCenterItem(
            new ActiveReminderOccurrenceKey(
                "source-label-note-rule",
                new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero)),
            new NoteRecord(
                "source-label-note",
                "来源标签验证便签",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        window.AddEntry(new ReminderCenterItem(
            new ActiveReminderOccurrenceKey(
                "source-label-countdown-rule",
                new DateTimeOffset(2031, 4, 5, 2, 0, 0, TimeSpan.Zero)),
            new CountdownReminderSource(new CountdownRecord(
                "source-label-countdown",
                "来源标签验证倒计时",
                new DateTimeOffset(2031, 5, 1, 0, 0, 0, TimeSpan.Zero),
                Array.Empty<string>())),
            1,
            0));
        var measurements = new List<ReminderCenterSourceLabelContrastMeasurement>();
        var relativeTimeContrastRatios = new List<double>();
        try
        {
            window.Show();
            PumpDispatcher();
            {
                theme.Apply();
                PumpDispatcher();
                var expectedFontSize = (double)Application.Current.FindResource("SecondaryWindowMinorFontSize");
                var ink = Application.Current.FindResource("InkBrush");
                var reminderCard = Application.Current.FindResource("ReminderCardBrush");
                foreach (var kind in new[] { "便签", "倒计时" })
                {
                    var label = Descendants<TextBlock>(window)
                        .Single(text => string.Equals(text.Text, kind, StringComparison.Ordinal));
                    var card = FindAncestor<Border>(label);
                    var foreground = label.Foreground as SolidColorBrush;
                    var background = card?.Background as SolidColorBrush;
                    var ratio = foreground is not null && background is not null
                        ? ContrastRatio(foreground.Color, background.Color)
                        : 0;
                    measurements.Add(new ReminderCenterSourceLabelContrastMeasurement(
                        "Day",
                        kind,
                        foreground?.Color.ToString() ?? string.Empty,
                        background?.Color.ToString() ?? string.Empty,
                        ratio,
                        ReferenceEquals(label.Foreground, ink),
                        label.FontWeight == FontWeights.Normal &&
                            Math.Abs(label.FontSize - expectedFontSize) < 0.01 &&
                            Descendants<TextBlock>(card!).Any(text =>
                                text.Text == (kind == "便签" ? "来源标签验证便签" : "来源标签验证倒计时") &&
                                Math.Abs(text.FontSize - 20) < 0.01 &&
                                text.FontWeight == FontWeights.SemiBold),
                        ReferenceEquals(card?.Background, reminderCard)));
                }
                foreach (var badge in Descendants<Border>(window).Where(border =>
                    AutomationProperties.GetAutomationId(border) == "ReminderEntryPendingBadge"))
                {
                    var text = Descendants<TextBlock>(badge).Single();
                    relativeTimeContrastRatios.Add(
                        text.Foreground is SolidColorBrush foreground && badge.Background is SolidColorBrush background
                            ? ContrastRatio(foreground.Color, background.Color)
                            : 0);
                }
            }

            return new ReminderCenterSourceLabelContrastValidation(
                measurements.Count == 2 &&
                    measurements.All(measurement => measurement.ContrastRatio >= 4.5),
                measurements.All(measurement => measurement.UsesInkBrush),
                measurements.All(measurement => measurement.EmphasisPreserved),
                measurements.All(measurement => measurement.ReminderCardBackground),
                measurements,
                relativeTimeContrastRatios);
        }
        finally
        {
            theme.Apply();
            window.ForceClose();
        }
    }

    private static SystemDialogPrimaryButtonContrastValidation ValidateSystemDialogPrimaryButtonContrast()
    {
        var recovery = SettingsRecoveryWindow.CreateForValidation("设置恢复对比度验证", true);
        var activation = InstanceActivationFailureWindow.CreateForValidation();
        try
        {
            var recoveryButton = LogicalDescendants<Button>(recovery)
                .Single(button =>
                    string.Equals(AutomationProperties.GetAutomationId(button), "SettingsRecoveryRecover", StringComparison.Ordinal));
            var activationButton = LogicalDescendants<Button>(activation)
                .Single(button =>
                    string.Equals(
                        AutomationProperties.GetName(button),
                        InstanceActivationFailureWindow.DismissButtonText,
                        StringComparison.Ordinal));
            var measurements = new[]
            {
                MeasureSystemDialogPrimaryButton("SettingsRecovery", recoveryButton),
                MeasureSystemDialogPrimaryButton("InstanceActivationFailure", activationButton)
            };
            // #150：静态色必须是现役主画布/主文字/主行动（及其文字）的副本，
            // 窗口本身是无边框暖粉壳（无系统默认窗口铬）。
            var usesActiveStaticPalette =
                UsesActiveWarmShell(recovery) &&
                UsesActiveWarmShell(activation);
            return new SystemDialogPrimaryButtonContrastValidation(
                measurements.All(measurement => measurement.ContrastRatio >= 4.5),
                measurements.All(measurement =>
                    string.Equals(measurement.Background, "#FFC23E68", StringComparison.Ordinal) &&
                    string.Equals(measurement.Foreground, "#FFFFFDFC", StringComparison.Ordinal)),
                usesActiveStaticPalette,
                measurements);
        }
        finally
        {
            recovery.Close();
            activation.Close();
        }
    }

    private static bool UsesActiveWarmShell(Window window)
    {
        var canvasColor = Application.Current.FindResource("CanvasColor") as Color?;
        var shellLineColor = Application.Current.FindResource("PeachLineColor") as Color?;
        var inkColor = Application.Current.FindResource("InkColor") as Color?;
        var mainActionColor = Application.Current.FindResource("MainActionColor") as Color?;
        var mainActionTextColor = Application.Current.FindResource("MainActionTextColor") as Color?;
        var button = LogicalDescendants<Button>(window).First();
        return window.WindowStyle == WindowStyle.None &&
               window.AllowsTransparency &&
               Equals(window.Background, Brushes.Transparent) &&
               window.Content is Border shell &&
               Equals(shell.BorderThickness, new Thickness(1)) &&
               canvasColor is { } canvas &&
               shell.Background is SolidColorBrush shellBackground &&
               shellBackground.Color == canvas &&
               shellLineColor is { } shellLine &&
               shell.BorderBrush is SolidColorBrush shellBorder &&
               shellBorder.Color == shellLine &&
               inkColor is { } ink &&
               window.Foreground is SolidColorBrush foreground &&
               foreground.Color == ink &&
               mainActionColor is { } mainAction &&
               button.Background is SolidColorBrush buttonBackground &&
               buttonBackground.Color == mainAction &&
               mainActionTextColor is { } mainActionText &&
               button.Foreground is SolidColorBrush buttonText &&
               buttonText.Color == mainActionText;
    }

    private static SystemDialogPrimaryButtonContrastMeasurement MeasureSystemDialogPrimaryButton(
        string window,
        Button button)
    {
        var background = button.Background as SolidColorBrush;
        var foreground = button.Foreground as SolidColorBrush;
        var ratio = background is not null && foreground is not null
            ? ContrastRatio(background.Color, foreground.Color)
            : 0;
        return new SystemDialogPrimaryButtonContrastMeasurement(
            window,
            foreground?.Color.ToString() ?? string.Empty,
            background?.Color.ToString() ?? string.Empty,
            ratio);
    }

    private static KeyboardFocusRingContrastValidation ValidateKeyboardFocusRingContrast(
        ApplicationTheme theme,
        MainWindow main,
        FloatingWindow floating,
        DateTimeOffset restoreUtc)
    {
        var probe = new Button { Content = "焦点环验证", MinWidth = 80 };
        var host = new Window
        {
            Content = probe,
            Width = 200,
            Height = 80,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        var measurements = new List<KeyboardFocusContrastMeasurement>();
        var ringMeasurements = new List<KeyboardFocusRingMeasurement>();
        var tabRingGapAvoidsSelectedFill = false;
        try
        {
            host.Show();
            host.Activate();
            PumpDispatcher();
            {
                theme.Apply();
                main.ApplyTheme();
                PumpDispatcher();
                var expected = Application.Current.FindResource("ActiveBrush");
                Keyboard.Focus(probe);
                PumpDispatcher();
                probe.ApplyTemplate();
                var surface = probe.Template?.FindName("Surface", probe) as Border;
                var probeUsesExpected = BrushMatchesExpected(surface?.BorderBrush, expected);

                // #152：选中粉底页签（PinkTintBrush）上的焦点环——环与粉底之间常驻 1px 间隙，
                // 实际邻色是容器 CardAltBrush；对实际邻色以主题公式核 ≥3:1。
                // 每处环按上下文钉唯一期望 token（常规 ActiveBrush / 实心粉邻色暖金），
                // 不存在「不达标但匹配旧强调色则放过」的分支。
                var countdownTab = Required<ToggleButton>(main, "CountdownTab");
                main.Activate();
                var originalTabChecked = countdownTab.IsChecked;
                countdownTab.IsChecked = true;
                Keyboard.Focus(countdownTab);
                PumpDispatcher();
                countdownTab.ApplyTemplate();
                var tabRing = countdownTab.Template?.FindName("FocusRing", countdownTab) as Border;
                var tabSurface = countdownTab.Template?.FindName("Surface", countdownTab) as Border;
                var usesExpected =
                    probeUsesExpected &&
                    BrushMatchesExpected(tabRing?.BorderBrush, expected);
                ringMeasurements.Add(new KeyboardFocusRingMeasurement(
                    "Day",
                    BrushColor(tabRing?.BorderBrush),
                    usesExpected,
                    tabRing is not null &&
                        new[]
                        {
                            tabRing.BorderThickness.Left,
                            tabRing.BorderThickness.Top,
                            tabRing.BorderThickness.Right,
                            tabRing.BorderThickness.Bottom
                        }.Max() >= 2));

                // 环与选中粉底之间有 1px 间隙：Surface 外缩 3（环 2px + 间隙 1px），
                // 环的邻色是容器浅色而不是粉底本身。
                tabRingGapAvoidsSelectedFill =
                    tabSurface is not null &&
                    Math.Abs(tabSurface.Margin.Left - 3) <= 0.01 &&
                    Math.Abs(tabSurface.Margin.Top - 3) <= 0.01 &&
                    Math.Abs(tabSurface.Margin.Right - 3) <= 0.01 &&
                    Math.Abs(tabSurface.Margin.Bottom - 3) <= 0.01 &&
                    tabSurface.Background is SolidColorBrush selectedFill &&
                    Application.Current.FindResource("PinkTintColor") is Color pinkTint &&
                    selectedFill.Color == pinkTint;

                if (tabRing?.BorderBrush is SolidColorBrush focusBrush)
                {
                foreach (var pair in new[]
                         {
                             ("Canvas", (Color)Application.Current.FindResource("CanvasColor")),
                             ("PeachTint", (Color)Application.Current.FindResource("PeachTintColor")),
                             ("Note", (Color)Application.Current.FindResource("NoteColor")),
                             ("CardAlt", (Color)Application.Current.FindResource("CardAltColor"))
                         })
                    {
                        measurements.Add(new KeyboardFocusContrastMeasurement(
                            pair.Item1,
                            ContrastRatio(focusBrush.Color, pair.Item2)));
                    }
                }

                countdownTab.IsChecked = originalTabChecked;
                PumpDispatcher();
            }

            var pin = Required<CheckBox>(main, "TopmostCheck");
            theme.Apply();
            main.ApplyTheme();
            PumpDispatcher();
            main.Activate();
            Keyboard.Focus(pin);
            PumpDispatcher();
            pin.ApplyTemplate();
            var pinBox = pin.Template?.FindName("Box", pin) as Border;
            var pinKeepsWarmGold =
                pinBox?.BorderBrush is SolidColorBrush pinBrush &&
                Application.Current.FindResource("MainActionFocusBrush") is SolidColorBrush pinExpected &&
                pinBrush.Color == pinExpected.Color;

            var hoverUnchanged =
                Application.Current.FindResource("ActiveBrush") is SolidColorBrush active &&
                Application.Current.FindResource("MainActionFocusBrush") is SolidColorBrush focusToken &&
                active.Color != focusToken.Color;

            // #217：焦点外环量度真实外侧标题栏邻色，不能用内侧选中填充替代。
            var floatingCapsule = ValidateFloatingCapsuleFocusRing(floating);

            return new KeyboardFocusRingContrastValidation(
                measurements.Count == 4 && measurements.All(measurement => measurement.ContrastRatio >= 3),
                ringMeasurements.All(measurement => measurement.UsesExpectedToken && measurement.RingVisible),
                pinKeepsWarmGold,
                hoverUnchanged,
                tabRingGapAvoidsSelectedFill,
                floatingCapsule.MeetsNeighborContrast,
                floatingCapsule.UsesWarmGoldToken,
                measurements,
                ringMeasurements);
        }
        finally
        {
            theme.Apply();
            main.ApplyTheme();
            host.Close();
            PumpDispatcher();
        }
    }

    private static (bool MeetsNeighborContrast, bool UsesWarmGoldToken)
        ValidateFloatingCapsuleFocusRing(FloatingWindow floating)
    {
        var countdownButton = Required<ToggleButton>(floating, "FloatingCountdownsButton");
        var notesButton = Required<ToggleButton>(floating, "FloatingNotesButton");
        var originalWorkspace = notesButton.IsChecked == true ? WorkspaceKind.Notes : WorkspaceKind.Countdowns;
        try
        {
            floating.Activate();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            var root = Required<Grid>(floating, "TaskWrap");
            var notes = MeasureFromAdjacentButton(countdownButton, notesButton, FocusNavigationDirection.Next, root);
            floating.ShowWorkspace(WorkspaceKind.Countdowns);
            var countdowns = MeasureFromAdjacentButton(notesButton, countdownButton, FocusNavigationDirection.Previous, root);
            return (
                notes.MeetsNeighborContrast && countdowns.MeetsNeighborContrast,
                notes.UsesWarmGoldToken && countdowns.UsesWarmGoldToken);
        }
        finally
        {
            floating.ShowWorkspace(originalWorkspace);
            PumpDispatcher();
        }

        static (bool MeetsNeighborContrast, bool UsesWarmGoldToken) MeasureFromAdjacentButton(
            ToggleButton adjacent,
            ToggleButton selected,
            FocusNavigationDirection direction,
            FrameworkElement root)
        {
            Keyboard.Focus(adjacent);
            PumpDispatcher();
            var moved = adjacent.MoveFocus(new TraversalRequest(direction));
            PumpDispatcher();
            selected.ApplyTemplate();
            var focusRing = selected.Template?.FindName("FocusRing", selected) as Border;
            if (!moved || !selected.IsKeyboardFocused || selected.IsChecked != true ||
                focusRing?.BorderBrush is not SolidColorBrush ring ||
                Application.Current.FindResource("MainActionFocusBrush") is not SolidColorBrush warmGold)
            {
                return (false, false);
            }

            var usesWarmGold = ring.Color == warmGold.Color;
            var thickness = new[]
            {
                focusRing.BorderThickness.Left,
                focusRing.BorderThickness.Top,
                focusRing.BorderThickness.Right,
                focusRing.BorderThickness.Bottom
            }.Min();
            var contrastPasses = usesWarmGold && thickness >= 2 &&
                RenderedOuterContrast(root, focusRing) >= 3;
            var pointerClearsKeyboardRing = WindowValidationInput.ClickForFocusValidation(selected) &&
                !FocusVisualState.GetIsKeyboardFocusVisible(selected) &&
                focusRing.BorderBrush is SolidColorBrush pointerRing && pointerRing.Color.A == 0;
            return (contrastPasses && pointerClearsKeyboardRing, usesWarmGold);
        }
    }

    private static ComboBoxTextClickValidation ValidateComboBoxTextClick(MainWindow main)
    {
        ReminderCenterWindow? reminderCenter = null;
        ReminderRulesEditorWindow? rulesEditor = null;
        var originalWorkspace = main.CurrentWorkspace;
        var expanded = Required<FrameworkElement>(main, "MainCountdownTimeTagRow").Visibility == Visibility.Visible;
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            if (!expanded)
                Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            var mainResults = new List<ComboBoxPointerExerciseResult>
            {
                MeasureCombo("Main.CountdownHour", Required<ComboBox>(main, "CountdownHour")),
                MeasureCombo("Main.CountdownMinute", Required<ComboBox>(main, "CountdownMinute")),
                MeasureCombo("Main.CountdownSecond", Required<ComboBox>(main, "CountdownSecond"))
            };
            Required<Button>(main, "PreferencesButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpDispatcher();
            mainResults.Add(MeasureCombo("Main.DisplayPrecision", Required<ComboBox>(main, "DisplayPrecision"), Required<Popup>(main, "PreferencesPopup")));
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "PreferencesPopup"));

            reminderCenter = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            reminderCenter.AddEntry(new ReminderCenterItem(
                new ActiveReminderOccurrenceKey(
                    "combo-click-rule",
                    new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero)),
                new NoteRecord(
                    "combo-click-note",
                    "下拉点击验证",
                    "2031-04-05",
                    false,
                    Array.Empty<string>(),
                    NoReminder.Instance),
                0,
                0));
            reminderCenter.Show();
            PumpDispatcher();
            var snoozeChoice = Descendants<ComboBox>(reminderCenter).Single(comboBox =>
                AutomationProperties.GetName(comboBox).StartsWith("稍后提醒时间：", StringComparison.Ordinal));
            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            PumpDispatcher();
            var reminderResults = Descendants<ComboBox>(reminderCenter)
                .Select(comboBox => MeasureCombo(AutomationProperties.GetName(comboBox), comboBox))
                .ToArray();

            rulesEditor = new ReminderRulesEditorWindow(
                Array.Empty<ReminderRule>(),
                new ValidationClock(new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero)))
            {
                Owner = main,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            rulesEditor.Show();
            PumpDispatcher();
            var editorResults = new List<ComboBoxPointerExerciseResult>();
            var editorCombos = Descendants<ComboBox>(rulesEditor).ToArray();
            var editorMode = editorCombos.Single(comboBox =>
                AutomationProperties.GetAutomationId(comboBox) == "ReminderRuleMode");
            var calendarFrequency = editorCombos.Single(comboBox =>
                AutomationProperties.GetAutomationId(comboBox) == "ReminderRuleCalendarFrequency");
            foreach (var comboBox in editorCombos)
            {
                var id = AutomationProperties.GetAutomationId(comboBox);
                // Exercise each selector in a reachable visible editor mode.
                // Routed-event injection previously also clicked collapsed
                // controls, which does not represent a user's pointer path.
                editorMode.SelectedIndex = id == "ReminderRuleInterval" ? 1 :
                    id.StartsWith("ReminderRuleCalendar", StringComparison.Ordinal) ? 2 : 0;
                calendarFrequency.SelectedIndex = 3;
                PumpDispatcher();
                editorResults.Add(MeasureCombo(id, comboBox));
            }

            var all = mainResults.Concat(reminderResults).Concat(editorResults).ToArray();
            return new ComboBoxTextClickValidation(
                all.All(result => result.ReleaseOpened && result.Attached && result.ArrowColumnUnchanged),
                all.All(result => result.ArrowPressOpens),
                all.All(result => result.KeyboardF4Opens && result.KeyboardAltDownOpens && result.KeyboardArrowsKeepOpen && result.KeyboardEscapeCloses),
                all.All(result => result.DisabledClickDoesNotOpen),
                all.All(result => result.PopupItemClickSelects),
                all.All(result => result.ArrowIsNotTabStop),
                all.Length,
                all);
        }
        finally
        {
            rulesEditor?.Close();
            reminderCenter?.ForceClose();
            WindowValidationInput.ClosePopupForCleanup(Required<Popup>(main, "PreferencesPopup"));
            if (!expanded && Required<FrameworkElement>(main, "MainCountdownTimeTagRow").Visibility == Visibility.Visible)
                Required<Button>(main, "CountdownEditorExpandButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            main.ShowWorkspace(originalWorkspace);
        }
    }

    private static ComboBoxPointerExerciseResult MeasureCombo(string name, ComboBox comboBox, Popup? containingPopup = null)
    {
        var result = WindowValidationInput.ExerciseComboBox(comboBox, containingPopup);
        if (!result.ArrowIsNotTabStop)
            Console.Error.WriteLine($"COMBO_TAB_DIAGNOSTIC {name}; visible={comboBox.IsVisible}; result={result}");
        return result;
    }

    private static SecondaryWindowHeaderDragValidation ValidateSecondaryWindowDrag(MainWindow main)
    {
        var reminder = new ReminderCenterWindow
        {
            ShowInTaskbar = false,
            Opacity = 0
        };
        reminder.AddEntry(new ReminderCenterItem(
            new ActiveReminderOccurrenceKey(
                "header-drag-rule",
                new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero)),
            new NoteRecord(
                "header-drag-note",
                "头部拖动验证",
                "2031-04-05",
                false,
                Array.Empty<string>(),
                NoReminder.Instance),
            0,
            0));
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            new ValidationClock(new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero)))
        {
            Owner = main,
            ShowInTaskbar = false,
            Opacity = 0
        };
        try
        {
            reminder.Show();
            PumpDispatcher();
            reminder.UpdateLayout();
            var workArea = MonitorWorkAreaResolver.ResolveForWindow(reminder).WorkAreaDip;
            var firstLeft = reminder.Left;
            var firstTop = reminder.Top;
            var firstAppearanceTopRight =
                Math.Abs(firstLeft - (workArea.Right - reminder.ActualWidth - 18)) <= 1.5 &&
                Math.Abs(firstTop - (workArea.Top + 18)) <= 1.5;

            var reminderRequestsBefore = reminder.NativeWindowDragRequestCount;
            reminder.DragHeader.RaiseEvent(CreateHeaderDragGesture());
            PumpDispatcher();
            var reminderHeaderRequestsDrag =
                reminder.NativeWindowDragRequestCount == reminderRequestsBefore + 1 &&
                reminder.NativeWindowDragCompletionCount == 0;
            var movedLeft = workArea.Left + 48;
            var movedTop = workArea.Top + 64;
            reminder.Left = movedLeft;
            reminder.Top = movedTop;
            reminder.ReapplyPlacement();
            PumpDispatcher();
            var reminderKeepsManual =
                reminder.HasManualWindowPlacement &&
                Math.Abs(reminder.Left - movedLeft) <= 0.5 &&
                Math.Abs(reminder.Top - movedTop) <= 0.5;

            var interactive = Descendants<Button>(reminder).First();
            interactive.RaiseEvent(CreateHeaderDragGesture());
            PumpDispatcher();
            var reminderInteractiveDoesNotDrag =
                reminder.NativeWindowDragRequestCount == reminderRequestsBefore + 1;

            var closingCancelled = false;
            reminder.Closing += (_, args) => closingCancelled = args.Cancel;
            reminder.Close();
            PumpDispatcher();
            var closeCancelledWithItems = closingCancelled && reminder.IsVisible && reminder.EntryCount > 0;

            editor.Show();
            PumpDispatcher();
            editor.UpdateLayout();
            var editorRequestsBefore = editor.NativeWindowDragRequestCount;
            editor.DragHeader.RaiseEvent(CreateHeaderDragGesture());
            PumpDispatcher();
            var editorHeaderRequestsDrag =
                editor.NativeWindowDragRequestCount == editorRequestsBefore + 1 &&
                editor.NativeWindowDragCompletionCount == 0;
            var editorMovedLeft = workArea.Left + 72;
            editor.Left = editorMovedLeft;
            editor.ReapplyPlacement();
            PumpDispatcher();
            var editorKeepsManual =
                editor.HasManualWindowPlacement &&
                Math.Abs(editor.Left - editorMovedLeft) <= 0.5;
            var editorAccept = Descendants<Button>(editor).First(button =>
                string.Equals(button.Content?.ToString(), "完成", StringComparison.Ordinal));
            var editorRequestsBeforeInteractive = editor.NativeWindowDragRequestCount;
            editorAccept.RaiseEvent(CreateHeaderDragGesture());
            PumpDispatcher();
            var editorInteractiveDoesNotDrag =
                editor.NativeWindowDragRequestCount == editorRequestsBeforeInteractive;
            var resizeModeUnchanged =
                reminder.ResizeMode == ResizeMode.CanResizeWithGrip &&
                editor.ResizeMode == ResizeMode.CanResizeWithGrip;

            return new SecondaryWindowHeaderDragValidation(
                reminderHeaderRequestsDrag && editorHeaderRequestsDrag,
                reminderKeepsManual && editorKeepsManual,
                reminderInteractiveDoesNotDrag && editorInteractiveDoesNotDrag,
                firstAppearanceTopRight,
                closeCancelledWithItems,
                resizeModeUnchanged);
        }
        finally
        {
            editor.Close();
            reminder.ForceClose();
        }
    }

    private static MouseButtonEventArgs CreateHeaderDragGesture() =>
        new(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent
        };

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static bool MainSegmentButtonsHaveAccessibleSelectedStates(MainWindow main)
    {
        var countdownTab = Required<ToggleButton>(main, "CountdownTab");
        var notesTab = Required<ToggleButton>(main, "NotesTab");
        var originalCountdownChecked = countdownTab.IsChecked;
        var originalNotesChecked = notesTab.IsChecked;
        try
        {
            countdownTab.IsChecked = true;
            notesTab.IsChecked = false;
            var countdownContrast = HasSelectedSegmentContrast(main, countdownTab);

            countdownTab.IsChecked = false;
            notesTab.IsChecked = true;
            return countdownContrast && HasSelectedSegmentContrast(main, notesTab);
        }
        finally
        {
            countdownTab.IsChecked = originalCountdownChecked;
            notesTab.IsChecked = originalNotesChecked;
        }
    }

    private static bool HasSelectedSegmentContrast(
        MainWindow main,
        ToggleButton button)
    {
        main.UpdateLayout();
        button.ApplyTemplate();
        var surface = button.Template?.FindName("Surface", button) as Border;
        // #148：选中段文字必须以主题公式对实际邻色达到 4.5:1；
        // 删除「不达标但对上方向稿采样对则放过」的旧分支，避免把错误验收成绿。
        return surface?.Background is SolidColorBrush background &&
            button.Foreground is SolidColorBrush foreground &&
            ApplicationTheme.MeetsContrast(background, foreground, 4.5);
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255d;
                return normalized <= 0.03928
                    ? normalized / 12.92
                    : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }

        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
               (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }
}
