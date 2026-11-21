using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
        "IconExternalLinkGeometry",
        "IconOverflowDotsGeometry"
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

    internal static (bool Passed, string Explanation) RunVectorizedTextGlyphIcons(string meaning)
    {
        var failures = new List<string>();
        var resources = Application.Current.Resources;
        var chevronGeometry = resources["IconChevronDownGeometry"] as Geometry;
        var closeGeometry = resources["IconCloseGeometry"] as Geometry;
        var plusGeometry = resources["IconPlusGeometry"] as Geometry;
        var overflowGeometry = resources["IconOverflowDotsGeometry"] as Geometry;

        Check(failures, "共享冻结几何 IconChevronDownGeometry 可用", chevronGeometry is { IsFrozen: true });
        Check(failures, "共享冻结几何 IconCloseGeometry 可用", closeGeometry is { IsFrozen: true });
        Check(failures, "共享冻结几何 IconPlusGeometry 可用", plusGeometry is { IsFrozen: true });
        Check(failures, "共享冻结几何 IconOverflowDotsGeometry 可用", overflowGeometry is { IsFrozen: true });

        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-vector-icons-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        FloatingWindow? floating = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            const string noteText = "矢量图标验证便签";
            SaveCountdown(workspace, "矢量图标验证倒计时", 5, Application.Current.Dispatcher);
            SaveNoteWithReminder(workspace, noteText, Application.Current.Dispatcher);

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

            if (chevronGeometry is not null)
            {
                VerifyCalendarMonthButtons(main, chevronGeometry, failures);
            }

            if (closeGeometry is not null)
            {
                VerifyFloatingSearchClearButton(floating, workspace, closeGeometry, failures);
            }

            if (plusGeometry is not null)
            {
                VerifyMainAddButtons(main, workspace, plusGeometry, failures);
            }

            VerifyFloatingOverflowButton(floating, noteText, overflowGeometry, failures);
        }
        finally
        {
            floating?.Close();
            main?.Close();
            DeleteSettingsFiles(settingsPath);
            TryDeleteFile(diagnosticPath);
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    private static void VerifyCalendarMonthButtons(
        MainWindow main,
        Geometry expectedChevron,
        ICollection<string> failures)
    {
        var picker = Required<DatePicker>(main, "CountdownDate");
        picker.ApplyTemplate();
        picker.IsDropDownOpen = true;
        SettleLayout(main);
        try
        {
            var cal = PickerTemplateParts.GetCalendar(picker);
            cal?.ApplyTemplate();
            cal?.UpdateLayout();
            WindowValidationInput.Pump(main.Dispatcher);
            var calItem = cal?.Template.FindName("PART_CalendarItem", cal) as CalendarItem
                ?? (cal is null ? null : VisualDescendants<CalendarItem>(cal).FirstOrDefault());
            calItem?.ApplyTemplate();
            calItem?.UpdateLayout();
            var prev = calItem?.Template.FindName("PART_PreviousButton", calItem) as Button;
            var next = calItem?.Template.FindName("PART_NextButton", calItem) as Button;
            if (cal is null || prev is null || next is null)
            {
                failures.Add("无法从打开的日历获取 PART_PreviousButton / PART_NextButton");
                return;
            }

            CheckCalendarNavButton(prev, expectedChevron, 90, "上一个月", "Calendar.Previous", failures);
            CheckCalendarNavButton(next, expectedChevron, -90, "下一个月", "Calendar.Next", failures);

            var initialMonth = new DateTime(cal.DisplayDate.Year, cal.DisplayDate.Month, 1);
            next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var afterNext = new DateTime(cal.DisplayDate.Year, cal.DisplayDate.Month, 1);
            Check(failures, "点击日历下一个月按钮前进一步", afterNext == initialMonth.AddMonths(1));

            prev.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var afterPrev = new DateTime(cal.DisplayDate.Year, cal.DisplayDate.Month, 1);
            Check(failures, "点击日历上一个月按钮回退原月", afterPrev == initialMonth);
        }
        finally
        {
            picker.IsDropDownOpen = false;
            SettleLayout(main);
        }
    }

    private static void CheckCalendarNavButton(
        Button button,
        Geometry expectedChevron,
        double expectedAngle,
        string expectedName,
        string label,
        ICollection<string> failures)
    {
        Check(failures, $"{label} Content 不再是文本字形", button.Content is not string);
        Check(failures, $"{label} 不含 ‹/› 文本节点", !VisualDescendants<TextBlock>(button).Any());
        var path = FirstPath(button);
        CheckShared(failures, label, path, expectedChevron);
        if (path is not null)
        {
            Check(failures, $"{label} 描边粗细为 2", Math.Abs(path.StrokeThickness - 2) < 0.01);
            Check(failures, $"{label} 描边圆头圆角",
                path.StrokeStartLineCap == PenLineCap.Round &&
                path.StrokeEndLineCap == PenLineCap.Round &&
                path.StrokeLineJoin == PenLineJoin.Round);
            Check(failures, $"{label} Path 不拦截命中", !path.IsHitTestVisible);
            Check(failures, $"{label} 描边绑定按钮前景", BrushesEqual(path.Stroke, button.Foreground));
            Check(failures, $"{label} 旋转角为 {expectedAngle}°",
                path.RenderTransform is RotateTransform rotate &&
                Math.Abs(rotate.Angle - expectedAngle) < 0.01 &&
                Math.Abs(path.RenderTransformOrigin.X - 0.5) < 0.01 &&
                Math.Abs(path.RenderTransformOrigin.Y - 0.5) < 0.01);
        }

        Check(failures, $"{label} 可访问名保持为「{expectedName}」",
            string.Equals(AutomationProperties.GetName(button), expectedName, StringComparison.Ordinal));
        Check(failures, $"{label} ToolTip 保持为「{expectedName}」",
            string.Equals(button.ToolTip as string, expectedName, StringComparison.Ordinal));
        Check(failures, $"{label} 命中区保持 ≥ 36×36", button.ActualWidth >= 35.5 && button.ActualHeight >= 35.5);
        Check(failures, $"{label} 保持 Tab 可达", button.IsTabStop);
    }

    private static void VerifyFloatingSearchClearButton(
        FloatingWindow floating,
        DesktopWorkspace workspace,
        Geometry expectedClose,
        ICollection<string> failures)
    {
        var searchButton = Required<Button>(floating, "FloatingSearchNotes");
        var searchBox = Required<TextBox>(floating, "FloatingNoteSearch");
        var hintRow = Required<Grid>(floating, "FloatingNoteSearchHint");
        var hintTerm = Required<Button>(floating, "FloatingNoteSearchHintTerm");
        var hintClear = Required<Button>(floating, "FloatingNoteSearchHintClear");

        searchButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(floating);
        searchBox.Text = "矢量";
        SettleLayout(floating);
        searchButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(floating);

        Check(failures, "悬浮搜索提示条可见", hintRow.Visibility == Visibility.Visible && hintClear.IsVisible);
        Check(failures, "FloatingNoteSearchHintClear Content 不再是文本字形", hintClear.Content is not string);
        Check(failures, "FloatingNoteSearchHintClear 不含 ✕ 文本节点", !VisualDescendants<TextBlock>(hintClear).Any());

        var path = FirstPath(hintClear);
        CheckShared(failures, "Floating.SearchHintClear", path, expectedClose);
        if (path is not null)
        {
            Check(failures, "Floating.SearchHintClear 描边粗细为 1.8", Math.Abs(path.StrokeThickness - 1.8) < 0.01);
            Check(failures, "Floating.SearchHintClear 描边圆头",
                path.StrokeStartLineCap == PenLineCap.Round &&
                path.StrokeEndLineCap == PenLineCap.Round);
            Check(failures, "Floating.SearchHintClear Path 不拦截命中", !path.IsHitTestVisible);
            Check(failures, "Floating.SearchHintClear 描边绑定按钮前景", BrushesEqual(path.Stroke, hintClear.Foreground));
        }

        Check(failures, "Floating.SearchHintClear 可访问名保持为「清除搜索」",
            string.Equals(AutomationProperties.GetName(hintClear), "清除搜索", StringComparison.Ordinal));
        Check(failures, "Floating.SearchHintClear ToolTip 保持不变", hintClear.ToolTip is null);
        Check(failures, "Floating.SearchHintClear 命中区保持 ≥ 36×36",
            hintClear.ActualWidth >= 35.5 && hintClear.ActualHeight >= 35.5);
        Check(failures, "Floating.SearchHintClear 保持 Tab 顺序在词钮之后",
            hintTerm.IsTabStop && hintClear.IsTabStop &&
            Grid.GetColumn(hintClear) > Grid.GetColumn(hintTerm) &&
            hintClear.TabIndex >= hintTerm.TabIndex);

        using (ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            SettleLayout(floating);
            if (path is not null)
            {
                Check(failures, "Floating.SearchHintClear 高对比下描边跟随前景",
                    BrushesEqual(path.Stroke, hintClear.Foreground));
            }
        }

        SettleLayout(floating);
        hintClear.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(floating);
        Check(failures, "点击 FloatingNoteSearchHintClear 清空搜索词",
            string.IsNullOrEmpty(workspace.Snapshot.Filter.NoteSearch));
    }

    private static void VerifyMainAddButtons(
        MainWindow main,
        DesktopWorkspace workspace,
        Geometry expectedPlus,
        ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);

        var saveCountdown = Required<Button>(main, "SaveCountdownButton");
        CheckAddButtonState(
            saveCountdown,
            expectedPlus,
            "添加倒计时",
            "保存倒计时",
            "保存倒计时（Ctrl+S；名称框 Enter）",
            133,
            41,
            "Main.SaveCountdownButton",
            failures);

        using (ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            SettleLayout(main);
            var hcPlus = FirstPath(saveCountdown);
            Check(failures, "Main.SaveCountdownButton 高对比下加号描边跟随前景",
                hcPlus is not null && BrushesEqual(hcPlus.Stroke, saveCountdown.Foreground));
        }

        SettleLayout(main);

        // 切入倒计时编辑态：不应显示加号矢量，取消编辑后恢复加号矢量
        var countdownEdit = VisualDescendants<Button>(Required<FrameworkElement>(main, "CountdownListRegion"))
            .FirstOrDefault(button => string.Equals(button.Content as string, "编辑", StringComparison.Ordinal));
        if (countdownEdit is null)
        {
            failures.Add("未找到倒计时行编辑按钮以验证编辑态切换");
        }
        else
        {
            countdownEdit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            Check(failures, "倒计时编辑态按钮文案为「保存修改」且不含加号 Path",
                string.Equals(saveCountdown.Content as string, "保存修改", StringComparison.Ordinal) &&
                !VisualDescendants<WpfPath>(saveCountdown).Any());

            var cancelCountdown = Required<Button>(main, "CancelCountdownEdit");
            cancelCountdown.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            CheckShared(failures, "倒计时退出编辑态后恢复加号 Path", FirstPath(saveCountdown), expectedPlus);
            Check(failures, "倒计时退出编辑态后恢复「添加倒计时」且不含文本＋",
                string.Equals(saveCountdown.Content as string, "添加倒计时", StringComparison.Ordinal));
        }

        // 切到便签页验证 SaveNoteButton
        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);

        var saveNote = Required<Button>(main, "SaveNoteButton");
        CheckAddButtonState(
            saveNote,
            expectedPlus,
            "添加",
            "保存便签",
            "添加或保存便签（Ctrl+S；正文 Ctrl+Enter）",
            98,
            50,
            "Main.SaveNoteButton",
            failures);

        var note = workspace.Snapshot.Settings.Notes[0];
        main.LoadNoteIntoEditor(note);
        SettleLayout(main);
        Check(failures, "便签编辑态按钮文案为「保存」且不含加号 Path",
            string.Equals(saveNote.Content as string, "保存", StringComparison.Ordinal) &&
            !VisualDescendants<WpfPath>(saveNote).Any());

        var cancelNote = Required<Button>(main, "CancelNoteEdit");
        cancelNote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
        CheckShared(failures, "便签退出编辑态后恢复加号 Path", FirstPath(saveNote), expectedPlus);
        Check(failures, "便签退出编辑态后恢复「添加」且不含文本＋",
            string.Equals(saveNote.Content as string, "添加", StringComparison.Ordinal));
    }

    private static void CheckAddButtonState(
        Button button,
        Geometry expectedPlus,
        string expectedContent,
        string expectedAccessibleName,
        string expectedToolTip,
        double minWidth,
        double minHeight,
        string label,
        ICollection<string> failures)
    {
        Check(failures, $"{label} Content 为「{expectedContent}」（不含文本＋）",
            string.Equals(button.Content as string, expectedContent, StringComparison.Ordinal));
        var textBlocks = VisualDescendants<TextBlock>(button).ToArray();
        Check(failures, $"{label} 视觉树不含文本字形＋",
            textBlocks.Length == 1 &&
            string.Equals(textBlocks[0].Text, expectedContent, StringComparison.Ordinal) &&
            !textBlocks[0].Text.Contains('＋'));

        var path = FirstPath(button);
        CheckShared(failures, label, path, expectedPlus);
        if (path is not null)
        {
            Check(failures, $"{label} 加号描边粗细为 1.8", Math.Abs(path.StrokeThickness - 1.8) < 0.01);
            Check(failures, $"{label} 加号描边圆头",
                path.StrokeStartLineCap == PenLineCap.Round &&
                path.StrokeEndLineCap == PenLineCap.Round);
            Check(failures, $"{label} 加号 Path 不拦截命中", !path.IsHitTestVisible);
            Check(failures, $"{label} 加号描边绑定按钮前景", BrushesEqual(path.Stroke, button.Foreground));

            if (button.Template?.FindName("Surface", button) is Border surface && textBlocks.Length == 1)
            {
                var pathOrigin = path.TranslatePoint(new Point(0, 0), surface);
                var textEnd = textBlocks[0].TranslatePoint(new Point(textBlocks[0].ActualWidth, 0), surface);
                Check(failures, $"{label} 图标与文字完整落在按钮 Surface 内不被裁切",
                    pathOrigin.X >= -0.5 && textEnd.X <= surface.ActualWidth + 0.5);
            }
        }

        Check(failures, $"{label} 可访问名保持为「{expectedAccessibleName}」",
            string.Equals(AutomationProperties.GetName(button), expectedAccessibleName, StringComparison.Ordinal));
        Check(failures, $"{label} ToolTip 保持为「{expectedToolTip}」",
            string.Equals(button.ToolTip as string, expectedToolTip, StringComparison.Ordinal));
        Check(failures, $"{label} 命中区保持 ≥ {minWidth}×{minHeight}",
            button.ActualWidth >= minWidth - 0.5 && button.ActualHeight >= minHeight - 0.5);
        Check(failures, $"{label} 保持 Tab 可达", button.IsTabStop);
    }

    private static void VerifyFloatingOverflowButton(
        FloatingWindow floating,
        string noteText,
        Geometry? expectedOverflow,
        ICollection<string> failures)
    {
        var rows = Required<StackPanel>(floating, "FloatingRows");
        var trigger = VisualDescendants<Button>(rows).FirstOrDefault(button =>
            Equals(button.GetValue(FloatingWindow.NoteControlRoleProperty), FloatingWindow.NoteControlRoleOverflow));
        if (trigger is null)
        {
            failures.Add("悬浮窗便签卡缺少溢出菜单按钮");
            return;
        }

        Check(failures, "Floating.OverflowButton 不再含文本字形「…」",
            !VisualDescendants<TextBlock>(trigger).Any());
        var path = FirstPath(trigger);
        if (expectedOverflow is not null)
        {
            CheckShared(failures, "Floating.OverflowButton", path, expectedOverflow);
        }
        if (path is not null)
        {
            Check(failures, "Floating.OverflowButton 描边粗细为 2", Math.Abs(path.StrokeThickness - 2) < 0.01);
            Check(failures, "Floating.OverflowButton 描边圆头圆角",
                path.StrokeStartLineCap == PenLineCap.Round &&
                path.StrokeEndLineCap == PenLineCap.Round &&
                path.StrokeLineJoin == PenLineJoin.Round);
            Check(failures, "Floating.OverflowButton Path 不拦截命中", !path.IsHitTestVisible);
            Check(failures, "Floating.OverflowButton 描边绑定按钮前景", BrushesEqual(path.Stroke, trigger.Foreground));
        }

        var expectedName = $"更多操作：编辑或删除便签 {noteText}";
        Check(failures, $"Floating.OverflowButton 可访问名保持为「{expectedName}」",
            string.Equals(AutomationProperties.GetName(trigger), expectedName, StringComparison.Ordinal));
        Check(failures, "Floating.OverflowButton ToolTip 保持不变", trigger.ToolTip is null);
        Check(failures, "Floating.OverflowButton 命中区保持 ≥ 36×36",
            trigger.ActualWidth >= 35.5 && trigger.ActualHeight >= 35.5);
        Check(failures, "Floating.OverflowButton 保持 Tab 可达", trigger.IsTabStop);
        Check(failures, "Floating.OverflowButton 静态仍保持 #319 零预创建（ContextMenu 为 null）",
            trigger.ContextMenu is null);

        using (ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            SettleLayout(floating);
            if (path is not null)
            {
                Check(failures, "Floating.OverflowButton 高对比下描边跟随前景",
                    BrushesEqual(path.Stroke, trigger.Foreground));
            }
        }

        SettleLayout(floating);

        // 验证 #319 原生 ContextMenu 打开/关闭不受矢量化影响（通过 PresentationSource 定位懒创建共享菜单）
        floating.Activate();
        SettleLayout(floating);
        Keyboard.Focus(trigger);
        SettleLayout(floating);
        WindowValidationInput.RaiseKey(trigger, Key.Enter);
        SettleLayout(floating);
        var menu = FindOpenContextMenu();
        var menuItems = menu?.Items.OfType<MenuItem>().Select(item => item.Header as string).ToArray() ?? [];
        Check(failures, "Floating.OverflowButton 按 Enter 懒创建并打开 #319 原生 ContextMenu（编辑/删除）",
            menu is { IsOpen: true } &&
            ReferenceEquals(menu.PlacementTarget, trigger) &&
            menuItems.SequenceEqual(["编辑", "删除"], StringComparer.Ordinal));
        if (menu is { IsOpen: true })
        {
            menu.IsOpen = false;
            SettleLayout(floating);
        }
    }

    private static ContextMenu? FindOpenContextMenu()
    {
        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (source.RootVisual is not DependencyObject root)
            {
                continue;
            }

            if (root is ContextMenu direct && direct.IsOpen)
            {
                return direct;
            }

            var nested = VisualDescendants<ContextMenu>(root).FirstOrDefault(candidate => candidate.IsOpen);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool BrushesEqual(Brush? first, Brush? second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        return first is SolidColorBrush a && second is SolidColorBrush b && a.Color == b.Color;
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

            // 3. Dropdown Triangle (×2: ComboBox, DatePicker) & expanded CountdownTime clock (#328)
            var expandButton = Required<Button>(main, "CountdownEditorExpandButton");
            expandButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            var countdownTimeHost = Required<FrameworkElement>(main, "CountdownTime").Parent as DependencyObject;
            CheckShared(failures, "Main.CountdownTimeClock", DirectChildPath(countdownTimeHost), expected["IconClockGeometry"]);
            var combo = Required<ComboBox>(main, "DisplayPrecision");
            combo.ApplyTemplate();
            PickerTemplateParts.GetArrowToggle(combo)?.ApplyTemplate();
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
