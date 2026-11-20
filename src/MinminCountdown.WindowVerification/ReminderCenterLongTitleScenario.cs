using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #321：沿 #297 的真实窗口与公共视觉树 seam 验收长标题，不向产品添加验证钩子。
internal static class ReminderCenterLongTitleScenario
{
    private const string LongTitle =
        "周五下班前请记得把季度复盘的结论、风险与下周行动项整理到共享文档，逐项确认负责人和截止时间，" +
        "再检查客户反馈里尚未回复的问题，把需要跨团队协调的事项单独标注，并在离开办公室前同步最终进展。";

    private const string ShortCountdownTitle = "发布评审截止";

    private const string TwoLineTitle = "确认下周版本发布清单与负责人安排情况";

    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static void Run()
    {
        RunLongAndShortEntryScenario();
        RunBoundaryAndResizeScenario();
    }

    private static void RunLongAndShortEntryScenario()
    {
        var longKey = new ActiveReminderOccurrenceKey("long-title-rule", FixtureUtc.AddMinutes(-5));
        var shortKey = new ActiveReminderOccurrenceKey("short-title-rule", FixtureUtc.AddMinutes(-2));
        var center = new ReminderCenterWindow();
        var captureDirectory = CreateCaptureDirectory();
        try
        {
            center.AddEntry(new ReminderCenterItem(
                longKey,
                new NoteRecord(
                    "long-title-note",
                    LongTitle,
                    "2038-07-08",
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()),
                0,
                0));
            center.AddEntry(new ReminderCenterItem(
                shortKey,
                new CountdownReminderSource(new CountdownRecord(
                    "short-title-countdown",
                    ShortCountdownTitle,
                    FixtureUtc.AddDays(3),
                    Array.Empty<string>())),
                1,
                0));
            PrepareOffscreen(center);
            center.Show();
            center.SetDeliveryReady(longKey);
            center.SetDeliveryReady(shortKey);
            center.UpdateTime(FixtureUtc);
            Settle(center);

            Require(!center.IsActive && !center.Topmost, "提醒中心显示或送达就绪时抢占了焦点或置顶。");
            var longRow = FindRow(center, longKey);
            var shortRow = FindRow(center, shortKey);
            var body = VisualDescendants<TextBlock>(longRow).Single(candidate => candidate.Text == LongTitle);
            var shortBody = VisualDescendants<TextBlock>(shortRow).Single(candidate => candidate.Text == ShortCountdownTitle);
            var scroller = VisualDescendants<ScrollViewer>(center)
                .Single(candidate => AutomationProperties.GetName(candidate) == "提醒条目列表");
            var originalFontSize = body.FontSize;
            var originalFontWeight = body.FontWeight;
            Require(Math.Abs(originalFontSize - 20) < 0.01 && originalFontWeight == FontWeights.SemiBold,
                $"标题应保持 SecondaryWindowSectionFontSize(20) 与 SemiBold，实际为 {originalFontSize}/{originalFontWeight}。");

            // 短标题不得被截断，也不得出现展开入口。
            Require(double.IsPositiveInfinity(shortBody.MaxHeight) &&
                    shortBody.TextTrimming == TextTrimming.None &&
                    FindDisclosures(shortRow).Length == 0,
                "不超过两行的短标题不应截断或出现展开入口。");

            // 长标题默认收起为两行省略，且动作按钮仍留在视口内。
            var disclosure = RequireDisclosure(longRow, expanded: false);
            var expectedFullHeight = MeasureUnwrappedHeight(body);
            Require(body.LineStackingStrategy == LineStackingStrategy.BlockLineHeight && body.LineHeight > 0,
                "长标题必须使用显式行高以稳定限定两行高度。");
            var twoLineHeight = body.LineHeight * 2;
            Require(Math.Abs(body.MaxHeight - twoLineHeight) < 0.01,
                $"长标题收起态 MaxHeight 应等于两行高 {twoLineHeight:0.##}，实际为 {body.MaxHeight:0.##}。");
            Require(body.TextTrimming == TextTrimming.CharacterEllipsis,
                "长标题收起态没有以省略号截断。");
            Require(body.ActualHeight > body.LineHeight + 0.5 && body.ActualHeight <= twoLineHeight + 0.5,
                $"长标题收起态渲染高度应为两行（{body.LineHeight:0.##}..{twoLineHeight:0.##}），实际为 {body.ActualHeight:0.##}。");
            Require(expectedFullHeight > twoLineHeight + body.LineHeight,
                $"测试夹具正文在当前宽度下展开高仅 {expectedFullHeight:0.##}，未超过两行（{twoLineHeight:0.##}）。");
            Require(disclosure.IsVisible && disclosure.IsEnabled && disclosure.Focusable && disclosure.IsTabStop,
                "长标题的展开入口不可见、不可用或不能经键盘到达。");
            Require(string.Equals(ReadButtonText(disclosure), "展开全文", StringComparison.Ordinal),
                $"收起态展开入口文案应为「展开全文」，实际为「{ReadButtonText(disclosure)}」。");
            Require(AutomationProperties.GetName(longRow)?.Contains(LongTitle, StringComparison.Ordinal) == true,
                "收起态整行可访问名没有保留完整正文。");
            Require(VisualDescendants<TextBlock>(center).All(static text => text.FontWeight != FontWeights.Bold) &&
                    VisualDescendants<Button>(center).All(static button => button.FontWeight != FontWeights.Bold),
                "提醒中心引入了 Bold 字重，破坏常规/半粗字重白名单。");

            var openSource = Action(longRow, "打开便签来源：");
            var openSourceBounds = Bounds(openSource, scroller);
            Require(openSourceBounds.Bottom <= scroller.ViewportHeight + 0.5,
                $"长标题收起后首条目动作按钮仍被挤出视口：bottom={openSourceBounds.Bottom:0.##}，viewport={scroller.ViewportHeight:0.##}。");

            var collapsedHeight = body.ActualHeight;
            Capture(center, captureDirectory, "collapsed");

            // 键盘聚焦、Tab 可达与单层焦点环验证。
            ActivateForKeyboard(center);
            var minimize = VisualDescendants<Button>(center)
                .Single(candidate => AutomationProperties.GetAutomationId(candidate) == "ReminderCenterMinimizeToTaskbar");
            Require(Keyboard.Focus(minimize) == minimize &&
                    minimize.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) &&
                    ReferenceEquals(Keyboard.FocusedElement, disclosure),
                "Tab 导航未能从头部顺序到达长标题展开入口。");
            Settle(center);
            Require(KeyboardFocusPolicy.KeyboardFocusDrawnOnce(disclosure),
                "长标题展开入口键盘聚焦时没有保持单层常驻焦点环。");

            // Space 展开全文。
            WindowValidationInput.RaiseKey(disclosure, Key.Space);
            Settle(center);
            var expandedDisclosure = RequireDisclosure(longRow, expanded: true);
            Require(ReferenceEquals(disclosure, expandedDisclosure),
                "展开操作重建了展开/收起按钮，未能保持控件身份。");
            Require(string.Equals(ReadButtonText(disclosure), "收起", StringComparison.Ordinal),
                $"展开态收起入口文案应为「收起」，实际为「{ReadButtonText(disclosure)}」。");
            Require(double.IsPositiveInfinity(body.MaxHeight) &&
                    body.TextTrimming == TextTrimming.None &&
                    body.ActualHeight > collapsedHeight + body.LineHeight &&
                    Math.Abs(body.ActualHeight - expectedFullHeight) <= 1.5,
                $"键盘 Space 没有完整展开长标题：maxHeight={body.MaxHeight}，actual={body.ActualHeight:0.##}，expected={expectedFullHeight:0.##}。");
            Require(body.Text == LongTitle &&
                    Math.Abs(body.FontSize - originalFontSize) < 0.01 &&
                    body.FontWeight == originalFontWeight,
                "展开后正文不完整，或标题字号、字重发生变化。");
            Require(AutomationProperties.GetName(longRow)?.Contains(LongTitle, StringComparison.Ordinal) == true,
                "展开后整行可访问名丢失完整正文。");
            Require(ReferenceEquals(Keyboard.FocusedElement, disclosure),
                "键盘 Space 展开后焦点离开标题展开/收起入口。");

            Keyboard.ClearFocus();
            Settle(center);
            Capture(center, captureDirectory, "expanded");
            Require(Keyboard.Focus(disclosure) == disclosure, "展开后无法把焦点还给标题收起入口。");

            // 相对时间原位更新不得改变展开状态、焦点和滚动位置。
            Require(scroller.ScrollableHeight > 0, "长标题展开夹具没有形成真实纵向滚动，无法验收原位更新。");
            scroller.ScrollToVerticalOffset(scroller.ScrollableHeight / 2);
            Settle(center);
            var expandedHeight = body.ActualHeight;
            var scrollOffset = scroller.VerticalOffset;
            var pendingBadge = VisualDescendants<Border>(longRow)
                .Single(candidate => AutomationProperties.GetAutomationId(candidate) == "ReminderEntryPendingBadge");
            var previousRelativeTime = AutomationProperties.GetName(pendingBadge);
            center.UpdateTime(FixtureUtc.AddMinutes(8));
            Settle(center);

            Require(ReferenceEquals(body, VisualDescendants<TextBlock>(longRow).Single(candidate => candidate.Text == LongTitle)) &&
                    ReferenceEquals(disclosure, RequireDisclosure(longRow, expanded: true)),
                "相对时间更新重建了标题或展开入口。");
            Require(AutomationProperties.GetName(pendingBadge) != previousRelativeTime,
                "相对时间没有原位更新。");
            Require(double.IsPositiveInfinity(body.MaxHeight) &&
                    Math.Abs(body.ActualHeight - expandedHeight) < 0.01 &&
                    Math.Abs(body.FontSize - originalFontSize) < 0.01 &&
                    body.FontWeight == originalFontWeight &&
                    ReferenceEquals(Keyboard.FocusedElement, disclosure) &&
                    Math.Abs(scroller.VerticalOffset - scrollOffset) < 0.01,
                "相对时间更新改变了展开状态、字号字重、焦点或滚动位置。");

            // Enter 再次收起。
            WindowValidationInput.RaiseKey(disclosure, Key.Enter);
            Settle(center);
            Require(ReferenceEquals(disclosure, RequireDisclosure(longRow, expanded: false)),
                "键盘 Enter 收起后重建了标题展开入口。");
            Require(string.Equals(ReadButtonText(disclosure), "展开全文", StringComparison.Ordinal) &&
                    Math.Abs(body.MaxHeight - twoLineHeight) < 0.01 &&
                    body.TextTrimming == TextTrimming.CharacterEllipsis &&
                    Math.Abs(body.ActualHeight - collapsedHeight) < 0.01 &&
                    Math.Abs(body.FontSize - originalFontSize) < 0.01 &&
                    body.FontWeight == originalFontWeight &&
                    ReferenceEquals(Keyboard.FocusedElement, disclosure),
                "键盘 Enter 没有把完整标题收起为两行并保持焦点与字号字重。");

            // 送达失败时禁用处置，保留打开来源与标题展开入口及焦点。
            center.ShowDeliveryFailure(longKey);
            Settle(center);
            Require(!Action(longRow, "知道了：").IsEnabled &&
                    !Action(longRow, "稍后提醒：").IsEnabled &&
                    !Action(longRow, "完成便签：").IsEnabled &&
                    Action(longRow, "打开便签来源：").IsEnabled,
                "标题披露改变了失败时禁用处置、保留打开来源的合同。");
            Require(ReferenceEquals(Keyboard.FocusedElement, disclosure) && disclosure.IsVisible && disclosure.IsEnabled,
                "失败信息更新移走了标题入口焦点或禁用了展开入口。");
        }
        finally
        {
            center.ForceClose();
            WindowValidationInput.Pump(center.Dispatcher);
        }
    }

    private static void RunBoundaryAndResizeScenario()
    {
        var twoLineKey = new ActiveReminderOccurrenceKey("two-line-boundary-rule", FixtureUtc);
        var center = new ReminderCenterWindow { Width = 460, Height = 420 };
        try
        {
            center.AddEntry(new ReminderCenterItem(
                twoLineKey,
                new NoteRecord(
                    "two-line-boundary-note",
                    TwoLineTitle,
                    "2038-07-08",
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()),
                0,
                0));
            PrepareOffscreen(center);
            center.Show();
            center.SetDeliveryReady(twoLineKey);
            Settle(center);

            var row = FindRow(center, twoLineKey);
            var body = VisualDescendants<TextBlock>(row).Single(candidate => candidate.Text == TwoLineTitle);
            var fullHeightAt460 = MeasureUnwrappedHeight(body);
            Require(fullHeightAt460 > body.LineHeight + 0.5 && fullHeightAt460 <= body.LineHeight * 2 + 0.5,
                $"两行边界夹具在 460 宽下应恰为两行（{body.LineHeight:0.##}..{body.LineHeight * 2:0.##}），实际为 {fullHeightAt460:0.##}。");
            Require(double.IsPositiveInfinity(body.MaxHeight) &&
                    body.TextTrimming == TextTrimming.None &&
                    FindDisclosures(row).Length == 0,
                "恰好两行的标题不应被误截断或显示展开入口。");
        }
        finally
        {
            center.ForceClose();
            WindowValidationInput.Pump(center.Dispatcher);
        }
    }

    private static Border FindRow(ReminderCenterWindow center, ActiveReminderOccurrenceKey key) =>
        VisualDescendants<Border>(center)
            .Single(candidate => candidate.Tag is ActiveReminderOccurrenceKey candidateKey && candidateKey == key);

    private static Button[] FindDisclosures(DependencyObject row) =>
        VisualDescendants<Button>(row)
            .Where(candidate => string.Equals(
                AutomationProperties.GetAutomationId(candidate),
                "ReminderEntryTitleDisclosure",
                StringComparison.Ordinal))
            .ToArray();

    private static Button RequireDisclosure(DependencyObject row, bool expanded)
    {
        var action = expanded ? "收起" : "展开";
        var state = expanded ? "当前展开" : "当前收起";
        var matches = VisualDescendants<Button>(row)
            .Where(candidate => AutomationProperties.GetName(candidate)?.StartsWith(
                action + "标题", StringComparison.Ordinal) == true)
            .ToArray();
        Require(matches.Length == 1, $"标题{action}入口数量应为 1，实际为 {matches.Length}。");
        var name = AutomationProperties.GetName(matches[0]) ?? string.Empty;
        Require(name.Contains(state, StringComparison.Ordinal) && name.Contains(LongTitle, StringComparison.Ordinal),
            $"标题{action}入口的可访问名必须包含「{state}」与完整对象，实际为「{name}」。");
        return matches[0];
    }

    private static string ReadButtonText(Button button) =>
        button.Content as string ??
        VisualDescendants<TextBlock>(button).Select(static text => text.Text).FirstOrDefault() ??
        string.Empty;

    private static double MeasureUnwrappedHeight(TextBlock body)
    {
        var probe = new TextBlock
        {
            Text = body.Text,
            FontFamily = body.FontFamily,
            FontSize = body.FontSize,
            FontWeight = body.FontWeight,
            FontStyle = body.FontStyle,
            FontStretch = body.FontStretch,
            LineHeight = body.LineHeight,
            LineStackingStrategy = body.LineStackingStrategy,
            TextWrapping = body.TextWrapping,
            TextTrimming = TextTrimming.None,
            FlowDirection = body.FlowDirection
        };
        TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(body));
        TextOptions.SetTextRenderingMode(probe, TextOptions.GetTextRenderingMode(body));
        probe.Measure(new Size(body.ActualWidth, double.PositiveInfinity));
        return probe.DesiredSize.Height;
    }

    private static Button Action(DependencyObject row, string automationNamePrefix) =>
        VisualDescendants<Button>(row).Single(candidate =>
            AutomationProperties.GetName(candidate)?.StartsWith(automationNamePrefix, StringComparison.Ordinal) == true);

    private static void ActivateForKeyboard(Window window)
    {
        window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetActiveWindow(handle);
        }
        Settle(window);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetActiveWindow(IntPtr windowHandle);

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static void PrepareOffscreen(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20_000;
        window.Top = -20_000;
        window.Opacity = 0;
        window.Topmost = false;
    }

    private static string? CreateCaptureDirectory()
    {
        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return null;
        }

        var directory = Path.Combine(
            Path.GetFullPath(sourceRoot),
            "logs",
            "ui-captures",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-reminder-long-title");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Capture(Window window, string? directory, string state)
    {
        if (directory is null)
        {
            return;
        }
        if (window.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("长标题实拍缺少提醒中心根视觉。");
        }

        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        window.Opacity = 0.01;
        Settle(window);
        var dpi = VisualTreeHelper.GetDpi(root);
        var dpiX = dpi.PixelsPerInchX > 0 ? dpi.PixelsPerInchX : 96;
        var dpiY = dpi.PixelsPerInchY > 0 ? dpi.PixelsPerInchY : 96;
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth * dpiX / 96.0));
        var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight * dpiY / 96.0));
        var bitmap = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(
            Path.Combine(directory, $"reminder-center-long-title-{state}.png"),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        encoder.Save(stream);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
