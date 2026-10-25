using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationReport
{
    private static TextFit MeasureTextFit(FrameworkElement element, string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                (FontFamily)Application.Current.FindResource("UiFontFamily"),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal),
            (double)Application.Current.FindResource("TypeCaptionFontSize"),
            Brushes.Black,
            VisualTreeHelper.GetDpi(element).PixelsPerDip);
        var available = double.IsNaN(element.Width)
            ? Math.Max(0, element.ActualWidth - 22)
            : Math.Max(0, element.Width - 22);
        return new TextFit(available >= formatted.Width, available, formatted.Width);
    }

    private static TextFit MeasureDatePickerTextFit(DatePicker picker)
    {
        picker.ApplyTemplate();
        if (picker.Template?.FindName("PART_TextBox", picker) is not DatePickerTextBox textBox)
        {
            return new TextFit(false, 0, 0);
        }

        var dpi = VisualTreeHelper.GetDpi(textBox);
        var formatted = new FormattedText(
            DateDisplay.FullDate(new DateOnly(2031, 12, 31)),
            CultureInfo.CurrentCulture,
            textBox.FlowDirection,
            new Typeface(
                textBox.FontFamily,
                textBox.FontStyle,
                textBox.FontWeight,
                textBox.FontStretch),
            textBox.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        var available = Math.Max(
            0,
            textBox.ActualWidth -
            textBox.Padding.Left -
            textBox.Padding.Right -
            textBox.BorderThickness.Left -
            textBox.BorderThickness.Right);
        var required =
            formatted.WidthIncludingTrailingWhitespace +
            Math.Max(0, formatted.OverhangTrailing) +
            (2 / dpi.DpiScaleX);
        return new TextFit(available >= required, available, required);
    }

    private static TextFit MeasureButtonFit(ContentControl button)
    {
        var text = button.Content?.ToString() ?? string.Empty;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch),
            button.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(button).PixelsPerDip);
        var available = Math.Max(
            0,
            button.ActualWidth -
            button.Padding.Left -
            button.Padding.Right -
            button.BorderThickness.Left -
            button.BorderThickness.Right);
        return new TextFit(available >= formatted.Width, available, formatted.Width);
    }

    private static TextFit MeasureTextFit(
        TextBlock textBlock,
        FrameworkElement visibleAncestor)
    {
        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var formatted = FormatText(textBlock, dpi);
        var origin = textBlock.TranslatePoint(new Point(0, 0), visibleAncestor);
        var visibleBounds = Rect.Intersect(
            new Rect(origin, textBlock.RenderSize),
            new Rect(new Point(0, 0), visibleAncestor.RenderSize));
        var available = visibleBounds.IsEmpty ? 0 : visibleBounds.Width;
        var required =
            formatted.WidthIncludingTrailingWhitespace +
            Math.Max(0, formatted.OverhangTrailing) +
            (2 / dpi.DpiScaleX);
        return new TextFit(available >= required, available, required);
    }

    private static FormattedText FormatText(TextBlock textBlock, DpiScale dpi) =>
        new(
            textBlock.Text,
            CultureInfo.CurrentCulture,
            textBlock.FlowDirection,
            new Typeface(
                textBlock.FontFamily,
                textBlock.FontStyle,
                textBlock.FontWeight,
                textBlock.FontStretch),
            textBlock.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);

    private static Grid? FindFloatingRemainingCluster(
        StackPanel rows,
        string countdownName)
    {
        foreach (var row in rows.Children.OfType<Border>())
        {
            var texts = Descendants<TextBlock>(row).ToArray();
            if (!texts.Any(text =>
                    string.Equals(text.Text, countdownName, StringComparison.Ordinal)))
            {
                continue;
            }

            return Descendants<Grid>(row).FirstOrDefault(grid =>
                string.Equals(grid.Tag as string, FloatingWindow.RemainingBlocksTag, StringComparison.Ordinal));
        }

        return null;
    }

    private static FloatingSecondaryRemainingMeasurement CaptureFloatingSecondaryRemaining(
        string precision,
        string expectedText,
        string formattedText,
        Grid? cluster,
        FloatingWindow floating,
        bool preferenceSaved)
    {
        var valueBlocks = RemainingBlockPairs(cluster).Select(pair => pair.Value).ToArray();
        var columnIs194Pixels =
            cluster is not null &&
            Math.Abs(cluster.Width - 194) <= 0.01 &&
            Math.Abs(cluster.ActualWidth - 194) <= 0.5;
        var isRightAligned =
            cluster is not null &&
            cluster.HorizontalAlignment == HorizontalAlignment.Right;
        var isSingleLineAndUntrimmed =
            RemainingClusterIsSingleLine(cluster) &&
            (cluster is null ||
             !(AutomationProperties.GetName(cluster) ?? string.Empty).Contains('…'));
        var isFullyVisible =
            cluster is not null &&
            isSingleLineAndUntrimmed &&
            string.Equals(
                AutomationProperties.GetItemStatus(cluster),
                "KeyTextFontFloorSatisfied",
                StringComparison.Ordinal) &&
            FitsInsideFloatingViewport(cluster, floating);
        return new FloatingSecondaryRemainingMeasurement(
            precision,
            expectedText,
            formattedText,
            cluster is null ? string.Empty : AutomationProperties.GetName(cluster) ?? string.Empty,
            Math.Round(cluster?.Width ?? 0, 2),
            Math.Round(cluster?.ActualWidth ?? 0, 2),
            Math.Round(valueBlocks.FirstOrDefault()?.FontSize ?? 0, 2),
            columnIs194Pixels,
            isRightAligned,
            isSingleLineAndUntrimmed,
            isFullyVisible,
            valueBlocks.Length > 0 &&
                valueBlocks.All(valueBlock =>
                    System.Windows.Documents.Typography.GetNumeralAlignment(valueBlock) ==
                    FontNumeralAlignment.Tabular),
            preferenceSaved);
    }

    private static bool FitsInsideFloatingViewport(
        FrameworkElement element,
        FloatingWindow floating)
    {
        var bodyScroller = Required<ScrollViewer>(floating, "FloatingBodyScroller");
        bodyScroller.ApplyTemplate();
        if (bodyScroller.Template.FindName(
                "PART_ScrollContentPresenter",
                bodyScroller) is not ScrollContentPresenter viewport)
        {
            return false;
        }

        var elementBounds = BoundsIn(element, viewport);
        var viewportBounds = new Rect(new Point(0, 0), viewport.RenderSize);
        return elementBounds.Left + 0.5 >= viewportBounds.Left &&
               elementBounds.Top + 0.5 >= viewportBounds.Top &&
               elementBounds.Right <= viewportBounds.Right + 0.5 &&
               elementBounds.Bottom <= viewportBounds.Bottom + 0.5;
    }

    private static bool TextBlockLinesFit(TextBlock textBlock)
    {
        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var typeface = new Typeface(
            textBlock.FontFamily,
            textBlock.FontStyle,
            textBlock.FontWeight,
            textBlock.FontStretch);
        return textBlock.Text
            .Split('\n')
            .All(line =>
            {
                var formatted = new FormattedText(
                    line,
                    CultureInfo.CurrentCulture,
                    textBlock.FlowDirection,
                    typeface,
                    textBlock.FontSize,
                    Brushes.Black,
                    dpi.PixelsPerDip);
                var required =
                    formatted.WidthIncludingTrailingWhitespace +
                    Math.Max(0, formatted.OverhangTrailing);
                const double minimumWidthReserve = 2;
                return required + minimumWidthReserve <= textBlock.ActualWidth;
            });
    }

    private static (bool Passed, FloatingReferenceActualCapture Capture) VerifyWorkdayCompletion(
        FloatingWindow window, DesktopWorkspace workspace, MutableValidationClock clock, string settingsPath)
    {
        var original = clock.UtcNow;
        var savedBytes = File.ReadAllBytes(settingsPath);
        var savedSettings = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
        var preferences = workspace.Snapshot.Settings.Preferences;
        var root = Required<Grid>(window, "TaskWrap");
        var rows = Required<StackPanel>(window, "FloatingRows");
        var income = Required<TextBlock>(window, "FloatingIncome");
        var end = new DateTimeOffset(2030, 7, 15, 18, 0, 0, TimeSpan.FromHours(8)).ToUniversalTime();
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("#236 " + message);
        }
        void At(DateTimeOffset time)
        {
            clock.UtcNow = time;
            window.UpdateTime(time);
            PumpDispatcher();
            window.UpdateLayout();
        }
        bool HasCompletion() => Descendants<TextBlock>(rows).Any(text => text.Text == "下班啦");
        void ToggleIncome()
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(income);
            Require(peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider, "金额缺少 Invoke。");
            ((IInvokeProvider)peer!.GetPattern(PatternInterface.Invoke)).Invoke();
            PumpDispatcher();
        }
        At(end.AddSeconds(-1));
        Require(!HasCompletion() && Descendants<TextBlock>(rows).Any(text => text.Text == "距离下班"),
            "下班前一秒应保留距离下班。");
        var beforeGeneration = window.FloatingRowsRebuildGenerationForValidation;
        At(end);
        Require(HasCompletion() && rows.Children[0] is Border &&
            Descendants<TextBlock>((Border)rows.Children[0]).Any(text => text.Text == "今日已赚 ￥169.54"),
            "精确下班时刻必须替换为带当日封顶金额的收尾卡。");
        var card = (Border)rows.Children[0];
        Require(window.FloatingRowsRebuildGenerationForValidation > beforeGeneration &&
            !Descendants<TextBlock>(card).Any(text => text.Text == "超期待！") &&
            !Descendants<Grid>(card).Any(grid => grid.Tag as string == FloatingWindow.RemainingBlocksTag) &&
            !Descendants<Control>(card).Any(), "收尾卡不应具有倒计时分格、徽章、编辑或规则控件。");
        var secondary = (Border)rows.Children[1];
        Require(window.FloatingCountdownCount == 1 && window.FloatingCountdownTexts.Count == 1 &&
            AutomationProperties.GetName((DependencyObject)secondary.Child).StartsWith("倒计时 七月倒计时", StringComparison.Ordinal),
            "系统收尾必须排除用户计数且普通首行仍为次项。");
        var due = workspace.GetDueReminderOccurrences(end);
        Require(due.Count == 1 && due[0].Source is NoteReminderSource { Id: "reference-note-1" },
            "下班系统行不能增加提醒 occurrence。");
        Require(Descendants<TextBlock>(card).All(text => text.FontSize >= 12 && SingleLineTextFits(text)) &&
            BoundsIn(Descendants<TextBlock>(card).First(), root).Right <= 396,
            "收尾标题与金额必须使用可读字号并保留正文安全区。");
        var generation = window.FloatingRowsRebuildGenerationForValidation;
        At(end.AddSeconds(1));
        Require(ReferenceEquals(rows.Children[0], card) &&
            generation == window.FloatingRowsRebuildGenerationForValidation,
            "收尾期间 tick 不应重建主卡。");
        WaitForStableFloatingReferenceLayout(window, root, rows, 2);
        ClearCaptureKeyboardFocus(root);
        var capture = VisualReferenceBaseline.Floating.ActualCapture("floating-workday-complete-day",
            EncodePng(ScaleToFrame(CaptureVisual(root), 520, 390)),
            "Countdowns", string.Empty, "下班啦，今日已赚 ￥169.54", 0, 0, "下班啦", "七月倒计时");
        ToggleIncome();
        At(end.AddHours(1));
        Require(Descendants<TextBlock>(card).Any(text => text.Text == "今日已赚 ¥••••") &&
            income.Text == "今日已赚 ¥••••", "收尾卡和底栏必须共用运行期遮罩。");
        var pending = new Queue<AutomationPeer>();
        var seen = new HashSet<AutomationPeer>();
        pending.Enqueue(UIElementAutomationPeer.CreatePeerForElement(window)!);
        while (pending.TryDequeue(out var peer))
        {
            if (!seen.Add(peer)) continue;
            var texts = new List<string> { peer.GetName(), peer.GetHelpText(), peer.GetItemStatus() };
            if (peer.GetPattern(PatternInterface.Text) is ITextProvider text) texts.Add(text.DocumentRange.GetText(-1));
            if (peer.GetPattern(PatternInterface.Value) is IValueProvider value) texts.Add(value.Value);
            Require(texts.All(text => !text.Contains("169.54", StringComparison.Ordinal)),
                "隐藏态真实 AutomationPeer 子树仍含收尾金额。");
            foreach (var child in peer.GetChildren() ?? []) pending.Enqueue(child);
        }
        Require(Descendants<FrameworkElement>(window).All(element =>
            !Convert.ToString(element.ToolTip, CultureInfo.InvariantCulture)!.Contains("169.54", StringComparison.Ordinal)),
            "隐藏态 ToolTip 仍含收尾金额。");
        window.ShowWorkspace(WorkspaceKind.Notes);
        window.ShowWorkspace(WorkspaceKind.Countdowns);
        At(end.AddHours(2));
        Require(Descendants<TextBlock>(rows).Count(text => text.Text == "今日已赚 ¥••••") == 1 &&
            income.Text == "今日已赚 ¥••••", "切页后收尾遮罩丢失。");
        ToggleIncome();
        workspace.ApplyFilter(workspace.Snapshot.Filter with { UntaggedOnly = true });
        At(end);
        Require(!HasCompletion() && window.FloatingCountdownCount == 1, "激活筛选后收尾应退出且用户记录保留。");
        workspace.ApplyFilter(workspace.Snapshot.Filter with { UntaggedOnly = false });
        At(end);
        Require(HasCompletion(), "清除筛选后应恢复收尾。");
        Require(workspace.Apply(new SavePreferencesChange(preferences.DisplayPrecision, 0m, preferences.WorkStart, preferences.WorkEnd)).Succeeded,
            "零薪资夹具失败。");
        At(end);
        Require(HasCompletion() && !Descendants<TextBlock>(rows).Any(text => text.Text.Contains("今日已赚", StringComparison.Ordinal)),
            "零薪资收尾不能伪造金额。");
        Require(workspace.Apply(new SavePreferencesChange(preferences.DisplayPrecision, preferences.MonthlySalary,
            preferences.WorkStart, preferences.WorkEnd)).Succeeded, "薪资恢复夹具失败。");
        At(end.AddHours(6).AddSeconds(-1));
        Require(HasCompletion(), "北京时间午夜前应保留当日收尾。");
        At(end.AddHours(6));
        Require(!HasCompletion(), "北京时间次日零点必须去除昨日收尾。");
        At(end.AddDays(1));
        Require(!HasCompletion(), "日历明确非工作日不能产生收尾。");
        At(end.AddDays(2));
        Require(HasCompletion(), "后续真实工作日到点应产生当天收尾。");
        At(end.AddDays(2).AddHours(6));
        Require(!HasCompletion() && Descendants<TextBlock>(rows).Any(text => text.Text == "距离下班") &&
            income.Text == "今日已赚 ￥0.00", "下一工作日零点必须显示新日倒计时及归零金额。");
        At(end.AddYears(1));
        Require(!HasCompletion(), "日历不覆盖的年份不能使用旧年收尾事实。");
        Require(SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == savedSettings &&
            File.ReadAllBytes(settingsPath).SequenceEqual(savedBytes), "收尾状态改变了持久记录或规则。");
        At(original);
        return (true, capture);
    }

    private static bool VerifyFloatingIncomePrivacy(
        FloatingWindow window, DesktopWorkspace workspace, HolidayCalendar calendar, MainWindow manager,
        string assetRoot, string settingsPath, MutableValidationClock clock)
    {
        var income = Required<TextBlock>(window, "FloatingIncome");
        var originalTime = clock.UtcNow;
        var savedBytes = File.ReadAllBytes(settingsPath);
        var savedSettings = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("#229 金额隐私：" + message);
        }
        void RequireHidden()
        {
            PumpDispatcher();
            window.UpdateLayout();
            Require(income.Text == "今日已赚 ¥••••", "隐藏态可见文案没有遮罩。");
            var root = UIElementAutomationPeer.CreatePeerForElement(window)!;
            var pending = new Queue<AutomationPeer>();
            var seen = new HashSet<AutomationPeer>();
            pending.Enqueue(root);
            var foundMaskedPeer = false;
            while (pending.TryDequeue(out var peer))
            {
                if (!seen.Add(peer)) continue;
                var texts = new List<string> { peer.GetName(), peer.GetHelpText(), peer.GetItemStatus() };
                if (peer.GetPattern(PatternInterface.Value) is IValueProvider value) texts.Add(value.Value);
                if (peer.GetPattern(PatternInterface.Text) is ITextProvider text) texts.Add(text.DocumentRange.GetText(-1));
                foundMaskedPeer |= peer.GetName() == "今日已赚 ¥••••";
                Require(texts.All(text => !text.Contains("41.13", StringComparison.Ordinal) && !text.Contains("59.97", StringComparison.Ordinal)),
                    "真实 AutomationPeer 子树仍暴露金额。");
                foreach (var child in peer.GetChildren() ?? []) pending.Enqueue(child);
            }
            Require(foundMaskedPeer, "自动化树没有提供遮罩名称。");
            Require(Descendants<FrameworkElement>(window).All(element =>
                    !Convert.ToString(element.ToolTip, CultureInfo.InvariantCulture)!.Contains("41.13", StringComparison.Ordinal) &&
                    !Convert.ToString(element.ToolTip, CultureInfo.InvariantCulture)!.Contains("59.97", StringComparison.Ordinal)),
                "ToolTip 暴露金额。");
        }
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Require(income.Text == "今日已赚 ￥41.13", "未在金额上按下的释放不应切换金额。");
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        income.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            { RoutedEvent = UIElement.MouseLeaveEvent });
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        Require(income.Text == "今日已赚 ￥41.13", "按下后移出金额区域不应触发切换。");
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        income.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        RequireHidden();
        var focusRing = Required<Border>(window, "FloatingIncomeFocusRing");
        Require(IsTransparent(focusRing.BorderBrush), "鼠标操作后残留键盘焦点环。");
        Required<Control>(window, "FloatingNotesButton").Focus();
        window.Activate();
        income.Focus();
        Require(income.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)) &&
            Keyboard.FocusedElement is UIElement previous && !ReferenceEquals(previous, income) &&
            previous.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && income.IsKeyboardFocused,
            "金额不能通过实际 Tab 路径从前一个控件进入。");
        PumpDispatcher();
        Require(income.IsKeyboardFocused && focusRing.BorderThickness == new Thickness(2) &&
            BrushColor(focusRing.BorderBrush) == BrushColor((Brush)window.FindResource("ActiveBrush")),
            "金额实际获键盘焦点后没有 2 DIP 暖边。");
        clock.UtcNow = originalTime.AddHours(1);
        window.UpdateTime(clock.UtcNow);
        RequireHidden();
        clock.UtcNow = originalTime.AddYears(1);
        window.UpdateTime(clock.UtcNow);
        Require(!income.Text.StartsWith("今日已赚", StringComparison.Ordinal) &&
            UIElementAutomationPeer.CreatePeerForElement(income)?.GetPattern(PatternInterface.Invoke) is null,
            "日历无该年金额时不应保留金额或金额动作。");
        clock.UtcNow = originalTime.AddHours(1);
        window.UpdateTime(clock.UtcNow);
        RequireHidden();
        window.ShowWorkspace(WorkspaceKind.Notes);
        RequireHidden();
        window.ShowWorkspace(WorkspaceKind.Countdowns);
        window.ApplyTheme();
        window.UpdateTime(clock.UtcNow);
        RequireHidden();
        Require(File.ReadAllBytes(settingsPath).SequenceEqual(savedBytes) &&
            SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) == savedSettings, "切换、tick 或切页写入设置。");

        FloatingWindow? restarted = null;
        try
        {
            restarted = new FloatingWindow(DesktopWorkspace.Load(settingsPath,
                new SettingsStore(clock: clock)), calendar, manager, assetRoot)
            { ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Opacity = 0.01 };
            restarted.Show();
            restarted.UpdateTime(clock.UtcNow);
            PumpDispatcher();
            Require(restarted.IncomeText == "今日已赚 ￥59.97", "新实例未恢复显示当前金额。");
        }
        finally { restarted?.Close(); }

        var peer = UIElementAutomationPeer.CreatePeerForElement(income);
        Require(income.Focusable && KeyboardNavigation.GetIsTabStop(income) &&
            peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider, "金额入口缺少键盘或 Invoke 语义。");
        ((IInvokeProvider)peer!.GetPattern(PatternInterface.Invoke)).Invoke();
        PumpDispatcher();
        Require(income.Text == "今日已赚 ￥59.97", "Invoke 后未显示最新金额。");
        WindowValidationInput.RaiseKey(income, Key.Enter);
        RequireHidden();
        var preferences = workspace.Snapshot.Settings.Preferences;
        Require(workspace.Apply(new SavePreferencesChange(preferences.DisplayPrecision, 0m, preferences.WorkStart, preferences.WorkEnd)).Succeeded, "零薪资夹具失败。");
        window.UpdateTime(clock.UtcNow);
        Require(income.Text == FloatingBottomStatus.CountdownEncouragementText &&
            UIElementAutomationPeer.CreatePeerForElement(income) is null, "鼓励语合同改变。");
        Require(workspace.Apply(new SavePreferencesChange(preferences.DisplayPrecision, preferences.MonthlySalary,
            preferences.WorkStart, preferences.WorkEnd)).Succeeded, "薪资恢复夹具失败。");
        window.UpdateTime(clock.UtcNow);
        RequireHidden();
        WindowValidationInput.RaiseKey(income, Key.Space);
        clock.UtcNow = originalTime;
        window.UpdateTime(originalTime);
        Require(income.Text == "今日已赚 ￥41.13", "空格切换后没有恢复显示。");
        Require(File.ReadAllBytes(settingsPath).SequenceEqual(savedBytes), "运行期遮罩状态进入设置文件。");
        // Restore the original reference composition after advancing the fixture clock.
        window.ShowWorkspace(WorkspaceKind.Notes);
        window.ShowWorkspace(WorkspaceKind.Countdowns);
        window.UpdateTime(originalTime);
        PumpDispatcher();
        return true;
    }

    private static void VerifyFloatingZeroDayBlocks(FloatingWindow window)
    {
        foreach (var sample in new[]
        {
            ("0 天", ""), ("0 天 06 小时", "时"), ("0 天 06 小时 49 分钟", "时,分"),
            ("0 天 06 小时 49 分钟 00 秒", "时,分,秒"),
            ("1 天 06 小时 49 分钟 00 秒", "天,时,分,秒"),
            ("999 天 13 小时 18 分钟 59 秒", "天,时,分,秒"),
            ("2000000 天 00 小时 00 分钟 00 秒", "天,时,分,秒")
        })
        {
            foreach (var isPrimary in new[] { true, false })
            {
                var element = window.BuildRemainingBlocksForValidation(sample.Item1, isPrimary);
                element.Measure(new Size(194, 60));
                element.Arrange(new Rect(0, 0, 194, element.DesiredSize.Height));
                var cluster = element as Grid;
                var pairs = RemainingBlockPairs(cluster).ToArray();
                if (string.Join(",", pairs.Select(pair => pair.Unit.Text)) != sample.Item2 ||
                    (sample.Item2.Length == 0 && element is not TextBlock { Text: "少于 1 天" }) ||
                    (sample.Item2.Length > 0 && (cluster is null || Math.Abs(cluster.Width - 194) > 0.01 ||
                        !RemainingClusterIsSingleLine(cluster) || pairs.Any(pair => pair.Value.FontSize < 12))))
                    throw new InvalidOperationException($"#229 零天省略或非零分格合同失败：{sample.Item1}, primary={isPrimary}");
            }
        }
    }

    private static void VerifyWorkdayPrimaryTitleAndBadge(StackPanel rows)
    {
        var card = (Border)rows.Children[0];
        var title = Descendants<TextBlock>(card).Single(text => text.Text == "距离下班");
        var deadline = Descendants<TextBlock>(card).Single(text => text.Text == "7 月 15 日 18:00");
        var badge = Descendants<Border>(card).Single(border => border.Child is TextBlock text && text.Text == "超期待！");
        var titleBounds = BoundsIn(title, card);
        var deadlineBounds = BoundsIn(deadline, card);
        var badgeBounds = BoundsIn(badge, card);
        if (title.ActualHeight > 20.01 || !SingleLineTextFits(title) ||
            titleBounds.Right > card.ActualWidth - 54 ||
            Math.Abs(deadlineBounds.Top + deadlineBounds.Height / 2 - badgeBounds.Top - badgeBounds.Height / 2) > 2 ||
            badgeBounds.Left < deadlineBounds.Right || badge.IsHitTestVisible || badge.Focusable)
            throw new InvalidOperationException($"#229 主项四字标题或截止行徽章不符合合同：title={titleBounds}; deadline={deadlineBounds}; badge={badgeBounds}");
    }

    private static bool SingleLineTextFits(TextBlock text)
    {
        var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace + Math.Max(0, formatted.OverhangTrailing) <= text.ActualWidth;
    }

    private static bool WorkdayCountdownUsesPrimaryVisual(
        FloatingWindow window,
        StackPanel rows,
        DateTimeOffset nowUtc,
        DateTimeOffset workdayTargetUtc)
    {
        if (rows.Children.Count != 2 ||
            rows.Children[0] is not Border { Child: Grid primaryRoot } ||
            rows.Children[1] is not Border { Child: Grid secondaryRoot })
        {
            return false;
        }

        var primaryRemaining = CountdownFormatting.FormatRemaining(
            workdayTargetUtc,
            nowUtc,
            "Second");
        var secondaryTargetUtc = nowUtc
            .AddDays(4)
            .AddHours(13)
            .AddMinutes(18)
            .AddSeconds(59);
        var secondaryRemainingForAutomation = CountdownFormatting.FormatRemaining(
            secondaryTargetUtc,
            nowUtc,
            "Second");
        var primaryTexts = Descendants<TextBlock>(primaryRoot).ToArray();
        var secondaryTexts = Descendants<TextBlock>(secondaryRoot).ToArray();
        var primaryTitle = primaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, "距离下班", StringComparison.Ordinal));
        var primaryDeadline = primaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, "7 月 15 日 18:00", StringComparison.Ordinal));
        var secondaryTitle = secondaryTexts.SingleOrDefault(text =>
            string.Equals(text.Text, "七月倒计时", StringComparison.Ordinal));
        var primaryNames = rows.Children
            .OfType<Border>()
            .Select(card => card.Child as Grid)
            .Where(root => root is not null)
            .Select(root => AutomationProperties.GetName(root!))
            .Where(name => name.StartsWith("主要倒计时 ", StringComparison.Ordinal))
            .ToArray();
        var pinkTintSoft = ResourceBrush(window, "PinkTintSoftBrush");
        var cardAlt = ResourceBrush(window, "CardAltBrush");
        var accent = ResourceBrush(window, "FloatingDayPrimaryCountdownAccentBrush");
        var pinkBlock = ResourceBrush(window, "PinkBlockBrush");
        var secondaryBlock = ResourceBrush(window, "SecondaryBlockBrush");
        var numberBrush = ResourceBrush(window, "PrimaryCountdownNumberBrush");
        var pinkBadge = ResourceBrush(window, "PinkBadgeBrush");
        var ink = ResourceBrush(window, "InkBrush");
        var muted = ResourceBrush(window, "FloatingDayMutedBrush");
        var primaryDecorations = primaryRoot.Children.OfType<Border>().ToArray();
        var secondaryDecorations = secondaryRoot.Children.OfType<Border>().ToArray();
        var primaryCluster = FloatingRemainingCluster(primaryRoot);
        var secondaryCluster = FloatingRemainingCluster(secondaryRoot);
        var primaryBlockPairs = RemainingBlockPairs(primaryCluster).ToArray();
        var secondaryBlockPairs = RemainingBlockPairs(secondaryCluster).ToArray();
        var badgeBlocks = Descendants<Border>(primaryRoot)
            .Where(border => border.Child is TextBlock badgeText &&
                string.Equals(badgeText.Text, "超期待！", StringComparison.Ordinal))
            .ToArray();
        var badge = badgeBlocks.Length == 1 ? badgeBlocks[0] : null;
        var badgeText = badge?.Child as TextBlock;

        return
            primaryNames.Length == 1 &&
            string.Equals(
                AutomationProperties.GetName(primaryRoot),
                $"主要倒计时 距离下班，{primaryRemaining}",
                StringComparison.Ordinal) &&
            string.Equals(
                AutomationProperties.GetName(secondaryRoot),
                $"倒计时 七月倒计时，{secondaryRemainingForAutomation}",
                StringComparison.Ordinal) &&
            Math.Abs(primaryRoot.MinHeight - 68) <= 0.01 &&
            Math.Abs(secondaryRoot.MinHeight - 39) <= 0.01 &&
            primaryDecorations.Length == 3 &&
            secondaryDecorations.Length == 2 &&
            Math.Abs(primaryDecorations[0].Margin.Right - 54) <= 0.01 &&
            Math.Abs(primaryDecorations[0].Opacity - 0.8) <= 0.01 &&
            Math.Abs(primaryDecorations[0].CornerRadius.TopLeft - 8) <= 0.01 &&
            !primaryDecorations[0].IsHitTestVisible &&
            ReferenceEquals(primaryDecorations[0].Background, pinkTintSoft) &&
            Math.Abs(primaryDecorations[1].Width - 3) <= 0.01 &&
            Math.Abs(primaryDecorations[1].Height - 42) <= 0.01 &&
            !primaryDecorations[1].IsHitTestVisible &&
            ReferenceEquals(primaryDecorations[1].Background, accent) &&
            Math.Abs(primaryDecorations[2].Opacity - 0.78) <= 0.01 &&
            Math.Abs(secondaryDecorations[0].Opacity - 0.72) <= 0.01 &&
            ReferenceEquals(secondaryDecorations[0].Background, cardAlt) &&
            Math.Abs(secondaryDecorations[1].Opacity - 0.48) <= 0.01 &&
            primaryTitle is not null &&
            Math.Abs(primaryTitle.FontSize - 16) <= 0.01 &&
            primaryTitle.FontWeight == FontWeights.SemiBold &&
            primaryTitle.TextWrapping == TextWrapping.Wrap &&
            primaryTitle.TextTrimming == TextTrimming.CharacterEllipsis &&
            primaryTitle.LineStackingStrategy == LineStackingStrategy.BlockLineHeight &&
            Math.Abs(primaryTitle.LineHeight - 20) <= 0.01 &&
            Math.Abs(primaryTitle.MaxHeight - 40) <= 0.01 &&
            primaryTitle.IsVisible && primaryTitle.ActualHeight > 0 &&
            primaryTitle.ActualHeight <= 40.01 &&
            string.Equals(BrushColor(primaryTitle.Foreground), "#FF3B190D", StringComparison.Ordinal) &&
            primaryDeadline is not null &&
            Math.Abs(primaryDeadline.FontSize - 13) <= 0.01 &&
            string.Equals(BrushColor(primaryDeadline.Foreground), "#FF82665F", StringComparison.Ordinal) &&
            Typography.GetNumeralAlignment(primaryDeadline) == FontNumeralAlignment.Tabular &&
            badge is not null &&
            !badge.IsHitTestVisible &&
            Math.Abs(badge.CornerRadius.TopLeft - 8) <= 0.01 &&
            badge.VerticalAlignment == VerticalAlignment.Center &&
            ReferenceEquals(badge.Background, pinkBadge) &&
            pinkBadge is not null &&
            ink is not null &&
            ContrastRatio(pinkBadge.Color, ink.Color) >= 4.5 &&
            badgeText is not null &&
            Math.Abs(badgeText.FontSize - 13) <= 0.01 &&
            badgeText.FontWeight == FontWeights.SemiBold &&
            ReferenceEquals(badgeText.Foreground, ink) &&
            primaryCluster is not null &&
            Math.Abs(primaryCluster.Width - 194) <= 0.01 &&
            primaryCluster.HorizontalAlignment == HorizontalAlignment.Right &&
            string.Equals(
                AutomationProperties.GetName(primaryCluster),
                primaryRemaining,
                StringComparison.Ordinal) &&
            primaryBlockPairs.Length == 3 &&
            primaryBlockPairs.All(pair => pair.Unit.Text != "天") &&
            primaryBlockPairs.All(pair =>
                ReferenceEquals(pair.Block.Background, pinkBlock) &&
                Math.Abs(pair.Block.CornerRadius.TopLeft - 7) <= 0.01 &&
                pair.Value.FontWeight == FontWeights.SemiBold &&
                Math.Abs(pair.Value.FontSize - 26) <= 0.01 &&
                ReferenceEquals(pair.Value.Foreground, numberBrush) &&
                Typography.GetNumeralAlignment(pair.Value) == FontNumeralAlignment.Tabular &&
                pair.Value.TextWrapping == TextWrapping.NoWrap &&
                pair.Unit.Foreground == muted) &&
            secondaryTitle is not null &&
            Math.Abs(secondaryTitle.FontSize - 13) <= 0.01 &&
            string.Equals(BrushColor(secondaryTitle.Foreground), "#FF3B190D", StringComparison.Ordinal) &&
            secondaryCluster is not null &&
            Math.Abs(secondaryCluster.Width - 194) <= 0.01 &&
            secondaryCluster.HorizontalAlignment == HorizontalAlignment.Right &&
            string.Equals(
                AutomationProperties.GetName(secondaryCluster),
                secondaryRemainingForAutomation,
                StringComparison.Ordinal) &&
            secondaryBlockPairs.Length == 4 &&
            secondaryBlockPairs.All(pair =>
                ReferenceEquals(pair.Block.Background, secondaryBlock) &&
                Math.Abs(pair.Block.CornerRadius.TopLeft - 5) <= 0.01 &&
                pair.Value.FontWeight == FontWeights.SemiBold &&
                Math.Abs(pair.Value.FontSize - 20) <= 0.01 &&
                string.Equals(BrushColor(pair.Value.Foreground), "#FF3B190D", StringComparison.Ordinal) &&
                Typography.GetNumeralAlignment(pair.Value) == FontNumeralAlignment.Tabular &&
                pair.Value.TextWrapping == TextWrapping.NoWrap &&
                pair.Unit.Foreground == muted);
    }

    private static SolidColorBrush? ResourceBrush(FloatingWindow window, string key) =>
        window.TryFindResource(key) as SolidColorBrush;

    private static Grid? FloatingRemainingCluster(DependencyObject rowRoot) =>
        Descendants<Grid>(rowRoot).FirstOrDefault(grid =>
            string.Equals(grid.Tag as string, FloatingWindow.RemainingBlocksTag, StringComparison.Ordinal));

    private static IEnumerable<(Border Block, TextBlock Value, TextBlock Unit)> RemainingBlockPairs(
        Grid? cluster)
    {
        if (cluster is null)
        {
            yield break;
        }

        foreach (var pairPanel in Descendants<StackPanel>(cluster))
        {
            if (pairPanel.Children.OfType<Border>().FirstOrDefault() is not { } block ||
                block.Child is not TextBlock value ||
                pairPanel.Children.OfType<TextBlock>().LastOrDefault() is not { } unit)
            {
                continue;
            }

            yield return (block, value, unit);
        }
    }

    private static bool RemainingClusterIsSingleLine(Grid? cluster) =>
        RemainingBlockPairs(cluster).Select(pair => pair.Value).All(value =>
            value.TextWrapping == TextWrapping.NoWrap &&
            value.TextTrimming == TextTrimming.None &&
            !value.Text.Contains('\r') &&
            !value.Text.Contains('\n') &&
            !value.Text.Contains('…') &&
            TextBlockLinesFit(value));

    private static bool PrimaryRemainingTextFitsOnOneLine(
        DependencyObject rows,
        DateTimeOffset targetUtc,
        DateTimeOffset nowUtc)
    {
        var expectedRemaining = CountdownFormatting.FormatRemaining(
            targetUtc,
            nowUtc,
            "Second");
        var cluster = Descendants<Grid>(rows).FirstOrDefault(candidate =>
            string.Equals(
                AutomationProperties.GetName(candidate),
                expectedRemaining,
                StringComparison.Ordinal) &&
            string.Equals(
                candidate.Tag as string,
                FloatingWindow.RemainingBlocksTag,
                StringComparison.Ordinal));
        var valueBlocks = RemainingBlockPairs(cluster).Select(pair => pair.Value).ToArray();
        return cluster is not null &&
               Math.Abs(cluster.Width - 194) <= 0.01 &&
               valueBlocks.Length == (targetUtc - nowUtc < TimeSpan.FromDays(1) ? 3 : 4) &&
               valueBlocks.All(value =>
                    Math.Abs(value.FontSize - 26) <= 0.01 &&
                   value.FontWeight == FontWeights.SemiBold &&
                   value.TextWrapping == TextWrapping.NoWrap &&
                   value.TextTrimming == TextTrimming.None &&
                   !value.Text.Contains('\n') &&
                   TextBlockLinesFit(value)) &&
               RemainingClusterIsSingleLine(cluster);
    }

    private static bool PrimaryDeadlineTextFullyVisible(
        DependencyObject rows,
        string expectedText)
    {
        return MeasurePrimaryDeadline(rows, expectedText, scrollbarVisible: false).FullyVisible;
    }

    private static FloatingPrimaryDeadlineMeasurement MeasurePrimaryDeadline(
        DependencyObject rows,
        string expectedText,
        bool scrollbarVisible)
    {
        var text = Descendants<TextBlock>(rows).SingleOrDefault(candidate =>
            candidate.Text.StartsWith(expectedText, StringComparison.Ordinal));
        if (text is null)
        {
            return new FloatingPrimaryDeadlineMeasurement(
                scrollbarVisible,
                string.Empty,
                0,
                0,
                false);
        }

        var dpi = VisualTreeHelper.GetDpi(text);
        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentCulture,
            text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        var requiredWidth = formatted.WidthIncludingTrailingWhitespace +
                            Math.Max(0, formatted.OverhangTrailing);
        var fullyVisible =
            string.Equals(text.Text, expectedText, StringComparison.Ordinal) &&
            text.TextWrapping == TextWrapping.NoWrap &&
            text.TextTrimming == TextTrimming.None &&
            !text.Text.Contains('…') &&
            text.ActualWidth + 0.01 >= requiredWidth &&
            TextBlockLinesFit(text);
        return new FloatingPrimaryDeadlineMeasurement(
            scrollbarVisible,
            text.Text,
            Math.Round(text.ActualWidth, 2),
            Math.Round(requiredWidth, 2),
            fullyVisible);
    }

    private static FloatingTrayInteractionExerciseResult RunFloatingTrayInteractionExercise(
        MainWindow main,
        FloatingWindow floating,
        Button minimizeButton,
        FloatingTrayController trayController,
        DesktopWorkspace workspace)
    {
        var wasVisible = floating.IsVisible;
        var exitRequested = false;
        EventHandler exitHandler = (_, _) => exitRequested = true;
        trayController.ExitRequested += exitHandler;
        try
        {
            var residentWhileVisible = floating.IsVisible && trayController.IsTrayIconVisible;
            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            var minimizeWorks =
                residentWhileVisible &&
                !floating.IsVisible &&
                trayController.IsTrayIconVisible;

            trayController.InvokeIconClickForValidation();
            PumpDispatcher();
            var clickRestores =
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                trayController.IsTrayIconVisible;

            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            trayController.InvokeRestoreForValidation();
            PumpDispatcher();
            var menuRestores =
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                trayController.IsTrayIconVisible;
            var headersMatch = TrayMenuHeadersMatch(floating);
            var managerPreserved = TrayOpenManagerPreservesDraftAndFilter(main, workspace, trayController);
            if (!minimizeWorks || !clickRestores || !menuRestores || !headersMatch || !managerPreserved)
            {
                throw new InvalidOperationException(
                    "#213 托盘探针失败：" +
                    $"residentMinimize={minimizeWorks} click={clickRestores} menu={menuRestores} " +
                    $"headers={headersMatch} manager={managerPreserved}");
            }

            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();
            trayController.InvokeExitForValidation();
            PumpDispatcher();
            return new FloatingTrayInteractionExerciseResult(
                trayController.IsActive,
                minimizeWorks,
                clickRestores && menuRestores && headersMatch && managerPreserved,
                exitRequested,
                RunFailedRestoreKeepsTrayStateProbe(floating));
        }
        finally
        {
            trayController.ExitRequested -= exitHandler;
            if (!floating.IsVisible)
            {
                floating.Show();
                PumpDispatcher();
            }

            if (wasVisible && !floating.IsVisible)
            {
                floating.Show();
            }
        }
    }

    private static bool TrayMenuHeadersMatch(FloatingWindow floating)
    {
        var (menu, openManager, restore, resetPosition, exit) = TrayMenuFactory.Create(
            static () => { },
            static () => { },
            static () => { },
            static () => { });
        IDisposable? menuInput = null;
        try
        {
            // Establish native keyboard focus on this thread before menu mode.
            // This window is not the menu PlacementTarget or resource owner.
            floating.Activate();
            floating.Focus();
            WindowValidationInput.Pump(floating.Dispatcher);
            if (TrayProbeGetFocus() == IntPtr.Zero)
                throw new InvalidOperationException("#232 菜单打开前当前线程没有 Win32 键盘焦点。");
            // The production TaskbarIcon owns a standalone menu, outside window aliases.
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.IsOpen = true;
            WindowValidationInput.Pump(menu.Dispatcher);
            var items = new[] { openManager, restore, resetPosition, exit };
            foreach (var item in items) item.ApplyTemplate();
            if (!ReferenceEquals(menu.Background, Application.Current.FindResource("CanvasBrush")) ||
                !ReferenceEquals(menu.BorderBrush, Application.Current.FindResource("ControlBorderBrush")) ||
                !items.All(item => item.FontSize == (double)Application.Current.FindResource("TypeBodyFontSize") &&
                    item.FontFamily.Source == ((FontFamily)Application.Current.FindResource("UiFontFamily")).Source &&
                    ReferenceEquals(item.Foreground, Application.Current.FindResource("InkBrush"))))
            {
                throw new InvalidOperationException("#232 托盘实际菜单未使用主题文字、背景、边界和字号 token。" +
                    $" background={menu.Background}; border={menu.BorderBrush}; items=" +
                    string.Join("|", items.Select(item => $"{item.Header}:highlight={item.IsHighlighted},font={item.FontFamily.Source},size={item.FontSize},foreground={item.Foreground}")));
            }
            menuInput = WindowValidationInput.AcquireContextMenuInputForValidation(menu);
            // Use ContextMenu's normal keyboard navigation, which selects the
            // first item through NavigateToStart and real keyboard focus.
            Keyboard.Focus(menu);
            WindowValidationInput.Pump(menu.Dispatcher);
            WindowValidationInput.RaiseKey(menu, Key.Down);
            WindowValidationInput.Pump(menu.Dispatcher);
            if (!openManager.IsKeyboardFocused || !openManager.IsHighlighted ||
                !ReferenceEquals(openManager.Background, Application.Current.FindResource("MainActionBrush")) ||
                !ReferenceEquals(openManager.Foreground, Application.Current.FindResource("MainActionTextBrush")))
                throw new InvalidOperationException("#232 托盘真实键盘高亮未使用主行动配色。" +
                    $" open={menu.IsOpen}; focused={Keyboard.FocusedElement}; enabled={openManager.IsEnabled}; visible={openManager.IsVisible}; focusable={openManager.Focusable}; highlighted={openManager.IsHighlighted}; background={openManager.Background}; foreground={openManager.Foreground}");

            static bool ColorIs(Brush brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;
            var dayCanvas = ((SolidColorBrush)menu.Background).Color;
            var dayHighlight = ((SolidColorBrush)openManager.Background).Color;
            using (var preference = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                PumpDispatcher();
                if (!ColorIs(menu.Background, SystemColors.WindowColor) ||
                    !ColorIs(menu.BorderBrush, SystemColors.WindowTextColor) ||
                    !ColorIs(openManager.Background, SystemColors.HighlightColor) ||
                    !ColorIs(openManager.Foreground, SystemColors.HighlightTextColor))
                    throw new InvalidOperationException("#232 已打开托盘未响应系统高对比开关。");
                var palette = new ApplicationTheme.SystemContrastPalette(
                    Colors.Black, Colors.White, Colors.Navy, Colors.White,
                    Colors.Yellow, Colors.Black, Colors.Silver);
                preference.ChangeColors(palette);
                PumpDispatcher();
                if (!ColorIs(menu.Background, palette.Window) ||
                    !ColorIs(menu.BorderBrush, palette.WindowText) ||
                    !ColorIs(openManager.Background, palette.Highlight) ||
                    !ColorIs(openManager.Foreground, palette.HighlightText))
                    throw new InvalidOperationException("#232 已打开托盘未响应运行中系统配色变化。");
            }
            PumpDispatcher();
            if (!ColorIs(menu.Background, dayCanvas) || !ColorIs(openManager.Background, dayHighlight))
                throw new InvalidOperationException("#232 关闭系统高对比后托盘未恢复主题。");
        }
        finally
        {
            try
            {
                menu.IsOpen = false;
                WindowValidationInput.Pump(menu.Dispatcher);
            }
            finally
            {
                menuInput?.Dispose();
            }
        }
        return string.Equals(openManager.Header as string, "打开管理窗口", StringComparison.Ordinal) &&
            string.Equals(restore.Header as string, "打开悬浮窗", StringComparison.Ordinal) &&
            string.Equals(resetPosition.Header as string, "复位窗口位置", StringComparison.Ordinal) &&
            string.Equals(exit.Header as string, "退出", StringComparison.Ordinal);
    }

    [DllImport("user32.dll", EntryPoint = "GetFocus", ExactSpelling = true)]
    private static extern IntPtr TrayProbeGetFocus();

    private static bool TrayOpenManagerPreservesDraftAndFilter(
        MainWindow main,
        DesktopWorkspace workspace,
        FloatingTrayController trayController)
    {
        var noteText = Required<TextBox>(main, "NoteText");
        var previousDraft = noteText.Text;
        var previousFilter = workspace.Snapshot.Filter;
        var mainWasVisible = main.IsVisible;
        try
        {
            noteText.Text = "托盘恢复草稿";
            workspace.ApplyFilter(previousFilter with { NoteSearch = "托盘保留筛选" });
            PumpDispatcher();
            ManagerWindowRestore.HideManagerOnly(main);
            PumpDispatcher();
            trayController.InvokeOpenManagerForValidation();
            PumpDispatcher();
            return main.IsVisible &&
                string.Equals(noteText.Text, "托盘恢复草稿", StringComparison.Ordinal) &&
                string.Equals(workspace.Snapshot.Filter.NoteSearch, "托盘保留筛选", StringComparison.Ordinal);
        }
        finally
        {
            noteText.Text = previousDraft;
            workspace.ApplyFilter(previousFilter);
            if (mainWasVisible && !main.IsVisible)
            {
                main.Show();
            }

            PumpDispatcher();
        }
    }

    private static bool RunFailedRestoreKeepsTrayStateProbe(FloatingWindow floating)
    {
        var interop = new GatedTrayForegroundInterop();
        var controller = new FloatingTrayController(
            floating,
            Path.Combine(AppContext.BaseDirectory, "countdown-icon.ico"),
            static () => true,
            interop,
            attachShellNotifyIcon: false);
        try
        {
            controller.MinimizeToTray();
            PumpDispatcher();
            var failureReturnsFalse = !controller.RestoreAndActivateFromExternalRequest();
            PumpDispatcher();
            var trayIconKeptAfterFailure = controller.IsTrayIconVisible;

            interop.AllowRestores();
            var retrySucceeded = controller.RestoreAndActivateFromExternalRequest();
            PumpDispatcher();
            var retryRestoresFully =
                retrySucceeded &&
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                controller.IsTrayIconVisible;

            return failureReturnsFalse && trayIconKeptAfterFailure && retryRestoresFully;
        }
        finally
        {
            controller.Dispose();
            if (!floating.IsVisible)
            {
                floating.Show();
                PumpDispatcher();
            }
        }
    }

    private static bool ProbeCalendarReminderStartLabels()
    {
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            new ValidationClock(new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero)))
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        editor.Show();
        PumpDispatcher();
        try
        {
            var mode = Descendants<ComboBox>(editor).Single(comboBox =>
                string.Equals(
                    AutomationProperties.GetAutomationId(comboBox),
                    "ReminderRuleMode",
                    StringComparison.Ordinal));
            var date = Descendants<DatePicker>(editor).Single();
            var hour = Descendants<ComboBox>(editor).Single(comboBox =>
                string.Equals(
                    AutomationProperties.GetAutomationId(comboBox),
                    "ReminderRuleFirstDueHour",
                    StringComparison.Ordinal));
            var minute = Descendants<ComboBox>(editor).Single(comboBox =>
                string.Equals(
                    AutomationProperties.GetAutomationId(comboBox),
                    "ReminderRuleFirstDueMinute",
                    StringComparison.Ordinal));
            var onceLabelsMatch =
                FirstDueRowLabel(editor) == "首次提醒" &&
                AutomationProperties.GetName(date) == "首次提醒日期" &&
                AutomationProperties.GetName(hour) == "首次提醒小时" &&
                AutomationProperties.GetName(minute) == "首次提醒分钟";
            mode.SelectedIndex = 2;
            PumpDispatcher();
            var calendarLabelsMatch =
                FirstDueRowLabel(editor) == "开始日期" &&
                AutomationProperties.GetName(date) == "开始日期" &&
                AutomationProperties.GetName(hour) == "本地时刻小时" &&
                AutomationProperties.GetName(minute) == "本地时刻分钟";
            mode.SelectedIndex = 1;
            PumpDispatcher();
            var intervalLabelsMatch =
                FirstDueRowLabel(editor) == "首次提醒" &&
                AutomationProperties.GetName(date) == "首次提醒日期";
            return onceLabelsMatch && calendarLabelsMatch && intervalLabelsMatch;
        }
        finally
        {
            editor.Close();
            PumpDispatcher();
        }
    }

    private static string FirstDueRowLabel(ReminderRulesEditorWindow editor)
    {
        return Descendants<TextBlock>(editor)
            .Select(text => text.Text)
            .FirstOrDefault(value =>
                value is "首次提醒" or "开始日期") ??
               string.Empty;
    }

    private static ProductionDatePickerValidation ValidateProductionDatePickers(
        MainWindow main,
        FloatingWindow floating)
    {
        var attached = new List<DatePickerProductionMeasurement>();
        var originalMainWorkspace = main.CurrentWorkspace;
        var filterPopup = Required<Popup>(main, "FilterPopup");
        var originalFilterOpen = filterPopup.IsOpen;
        var floatingWasCountdowns =
            Required<ToggleButton>(floating, "FloatingCountdownsButton").IsChecked == true;
        var quickAddWasExpanded = Required<Border>(floating, "FloatingNoteEntry").Visibility == Visibility.Visible;
        ReminderRulesEditorWindow? rulesEditor = null;
        ReminderCenterWindow? reminderCenter = null;
        Window? negativeHost = null;
        var reminderHourOptionCount = 0;
        var reminderMinuteOptionCount = 0;
        try
        {
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            attached.Add(MeasureDatePicker("Main.CountdownDate", Required<DatePicker>(main, "CountdownDate")));

            main.ShowWorkspace(WorkspaceKind.Notes);
            filterPopup.IsOpen = true;
            PumpDispatcher();
            attached.Add(MeasureDatePicker("Main.NoteDate", Required<DatePicker>(main, "NoteDate")));
            filterPopup.IsOpen = true;
            PumpDispatcher();
            attached.Add(MeasureDatePicker(
                "Main.NoteDateFilter",
                Required<DatePicker>(main, "NoteDateFilter")));

            floating.ShowWorkspace(WorkspaceKind.Notes);
            floating.SetQuickAddExpanded(true);
            PumpDispatcher();
            attached.Add(MeasureDatePicker(
                "Floating.NoteDate",
                Required<DatePicker>(floating, "FloatingNoteDate")));

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
            attached.Add(MeasureDatePicker(
                "ReminderRules.FirstDueDate",
                Descendants<DatePicker>(rulesEditor).Single()));
            reminderHourOptionCount = Descendants<ComboBox>(rulesEditor)
                .Single(comboBox =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(comboBox),
                        "ReminderRuleFirstDueHour",
                        StringComparison.Ordinal))
                .Items.Count;
            reminderMinuteOptionCount = Descendants<ComboBox>(rulesEditor)
                .Single(comboBox =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(comboBox),
                        "ReminderRuleFirstDueMinute",
                        StringComparison.Ordinal))
                .Items.Count;

            var reminderKey = new ActiveReminderOccurrenceKey(
                "date-picker-validation-rule",
                new DateTimeOffset(2031, 4, 5, 1, 0, 0, TimeSpan.Zero));
            reminderCenter = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            reminderCenter.AddEntry(new ReminderCenterItem(
                reminderKey,
                new NoteRecord(
                    "date-picker-validation-note",
                    "日期控件验证",
                    "2031-04-05",
                    false,
                    Array.Empty<string>(),
                    NoReminder.Instance),
                0,
                0));
            reminderCenter.Show();
            PumpDispatcher();
            var snoozeChoice = Descendants<ComboBox>(reminderCenter).Single(comboBox =>
                AutomationProperties.GetName(comboBox).StartsWith(
                    "稍后提醒时间：",
                    StringComparison.Ordinal));
            snoozeChoice.SelectedItem = snoozeChoice.Items
                .OfType<ComboBoxItem>()
                .Single(item => Equals(item.Tag, ReminderSnoozeOption.Custom));
            PumpDispatcher();
            var reminderCenterMeasurement = MeasureDatePicker(
                "ReminderCenter.CustomSnoozeDate",
                Descendants<DatePicker>(reminderCenter).Single());
            var reminderCenterComboBoxes = Descendants<ComboBox>(reminderCenter).ToArray();
            var reminderCenterComboBoxThemedCount = reminderCenterComboBoxes.Count(HasTemplate);

            // Keep an independent negative: the same warm, read-only date
            // template with its text activation surface removed. Its arrow
            // remains usable, but clicking the text must not open the calendar.
            var negativePicker = new DatePicker();
            negativeHost = new Window
            {
                Content = negativePicker,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0,
                Width = 240,
                Height = 100
            };
            negativeHost.Show();
            PumpDispatcher();
            negativePicker.ApplyTemplate();
            ((ToggleButton)negativePicker.Template.FindName("PART_TextToggle", negativePicker))
                .Visibility = Visibility.Collapsed;
            PumpDispatcher();
            var negativeMeasurement = MeasureDatePicker(
                "Negative.UnattachedDatePicker",
                negativePicker);

            return new ProductionDatePickerValidation(
                attached,
                reminderCenterMeasurement,
                negativeMeasurement,
                reminderHourOptionCount,
                reminderMinuteOptionCount,
                reminderCenterComboBoxes.Length,
                reminderCenterComboBoxThemedCount);
        }
        finally
        {
            negativeHost?.Close();
            rulesEditor?.Close();
            reminderCenter?.ForceClose();
            filterPopup.IsOpen = originalFilterOpen;
            main.ShowWorkspace(originalMainWorkspace);
            floating.SetQuickAddExpanded(quickAddWasExpanded);
            floating.ShowWorkspace(
                floatingWasCountdowns ? WorkspaceKind.Countdowns : WorkspaceKind.Notes);
            PumpDispatcher();
        }
    }

    private static DatePickerProductionMeasurement MeasureDatePicker(
        string name,
        DatePicker picker)
    {
        picker.ApplyTemplate();
        var textBox = picker.Template.FindName("PART_TextBox", picker) as DatePickerTextBox;
        var pointer = WindowValidationInput.ExerciseDatePickerTextPointer(picker);
        var buttonOpensCalendar = WindowValidationInput.ExerciseDatePickerButton(picker);
        var attached = PickerTemplateParts.HasDatePickerContract(picker);
        var keyboardLifecycleWorks = attached && WindowValidationInput.ExerciseDatePicker(picker);
        picker.ApplyTemplate();
        textBox = picker.Template.FindName("PART_TextBox", picker) as DatePickerTextBox;
        var openCalendarButton = picker.Template.FindName("PART_Button", picker) as Button;
        return new DatePickerProductionMeasurement(
            name,
            attached,
            textBox is not null,
            textBox?.IsReadOnly == true,
            textBox?.Focusable == false,
            pointer,
            buttonOpensCalendar,
            keyboardLifecycleWorks,
            AutomationProperties.GetName(picker),
            openCalendarButton is null
                ? string.Empty
                : AutomationProperties.GetName(openCalendarButton) ?? string.Empty,
            MeasureDatePickerTextFit(picker),
            WindowValidationInput.ExerciseDatePickerSelectedDateText(picker));
    }

    private static bool ValidateCountdownCardActionNames(
        MainWindow main,
        DesktopWorkspace workspace)
    {
        var originalWorkspace = main.CurrentWorkspace;
        var probeName = "可访问名称探针倒计时";
        string? probeId = null;
        try
        {
            if (workspace.Snapshot.Settings.Countdowns.Count == 0)
            {
                var saved = workspace.Apply(new SaveCountdownChange(
                    null,
                    probeName,
                    workspace.Snapshot.UtcNow.AddDays(3),
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
                if (!saved.Succeeded)
                {
                    return false;
                }

                probeId = workspace.Snapshot.Settings.Countdowns
                    .FirstOrDefault(record =>
                        string.Equals(record.Name, probeName, StringComparison.Ordinal))
                    ?.Id;
                if (string.IsNullOrWhiteSpace(probeId))
                {
                    return false;
                }
            }

            main.ShowWorkspace(WorkspaceKind.Countdowns);
            PumpDispatcher();
            main.UpdateLayout();
            var records = workspace.Snapshot.Settings.Countdowns;
            var rows = Required<StackPanel>(main, "CountdownRows");
            var buttons = Descendants<Button>(rows).ToArray();
            foreach (var record in records)
            {
                var edits = buttons.Where(button =>
                    string.Equals(button.Content as string, "编辑", StringComparison.Ordinal) &&
                    string.Equals(button.Tag as string, record.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                var deletes = buttons.Where(button =>
                    string.Equals(button.Content as string, "删除", StringComparison.Ordinal) &&
                    string.Equals(button.Tag as string, record.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (edits.Length != 1 || deletes.Length != 1)
                {
                    return false;
                }

                var expectedEdit = UiAccessibility.FoldAccessibleName($"编辑倒计时 {record.Name}");
                var expectedDelete = UiAccessibility.FoldAccessibleName($"删除倒计时 {record.Name}");
                if (!string.Equals(
                        AutomationProperties.GetName(edits[0]),
                        expectedEdit,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        AutomationProperties.GetName(deletes[0]),
                        expectedDelete,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return records.Count > 0;
        }
        finally
        {
            if (probeId is not null)
            {
                workspace.Apply(new DeleteCountdownChange(probeId));
                PumpDispatcher();
            }

            main.ShowWorkspace(originalWorkspace);
            PumpDispatcher();
        }
    }
}
