using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #318：沿 #297 的真实悬浮窗与公共视觉树 seam 验收快速新增出错态布局：
// 字段区在命令区限高内滚动，“添加”动作行固定在命令区底部不随字段滚动，
// 出错态下“添加”完整位于视口内且可点击，展开回顶、收起态命中高度与 Tab 顺序均保持不变。
internal static class FloatingQuickAddErrorLayoutScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) Run(string meaning)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-floating-quickadd-318-{Guid.NewGuid():N}.settings.json");
        FloatingWindow? floating = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            var today = workspace.Snapshot.BeijingNow.Date;
            SeedNote(workspace, "下午和同事同步下周安排。", today);
            SeedNote(workspace, "核对本周版本清单与发布准备。", today.AddDays(-1));

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            floating = new FloatingWindow(
                workspace,
                calendar,
                new StubManagerHost(),
                AppContext.BaseDirectory);
            ShowAndWaitFirstFrame(floating);

            var root = floating.Content as FrameworkElement
                ?? throw new InvalidOperationException("悬浮窗根视觉不可用。");
            var taskCard = FindNamed<Border>(floating, "TaskCard");
            var notesButton = FindNamed<ToggleButton>(floating, "FloatingNotesButton");
            var commandArea = FindNamed<FrameworkElement>(floating, "FloatingNotesCommandArea");
            var commandScroller = FindNamed<ScrollViewer>(floating, "FloatingNotesCommandScroller");
            var noteEntry = FindNamed<Border>(floating, "FloatingNoteEntry");
            var noteText = FindNamed<TextBox>(floating, "FloatingNoteText");
            var noteTextError = FindNamed<TextBlock>(floating, "FloatingNoteTextError");
            var noteDate = FindNamed<DatePicker>(floating, "FloatingNoteDate");
            var noteDateError = FindNamed<TextBlock>(floating, "FloatingNoteDateError");
            var manageReminderRules = FindNamed<Button>(floating, "FloatingManageReminderRules");
            var tagsSelector = FindNamed<TagSelector>(floating, "FloatingTags");
            var addButton = FindNamed<Button>(floating, "FloatingAddNote");
            var commandBar = FindNamed<FrameworkElement>(floating, "FloatingNotesCommandBar");
            var addToggle = FindNamed<Button>(floating, "FloatingAddNoteToggle");
            var searchButton = FindNamed<Button>(floating, "FloatingSearchNotes");
            var settingsButton = FindNamed<Button>(floating, "FloatingOpenSettings");

            Click(notesButton, floating);
            Require(noteEntry.Visibility == Visibility.Collapsed, "切换到便签工作区后快速新增默认应处于收起态。");
            Require(addToggle.ActualHeight >= 37.5 && searchButton.ActualHeight >= 37.5 && settingsButton.ActualHeight >= 37.5,
                $"收起态底部命令三按钮命中高度必须保持 38 DIP：toggle={addToggle.ActualHeight:0.##}，search={searchButton.ActualHeight:0.##}，settings={settingsButton.ActualHeight:0.##}。");
            var collapsedBarBounds = Bounds(commandBar, root);
            var collapsedCommandHeight = Math.Max(commandArea.ActualHeight, commandScroller.ActualHeight);
            Require(collapsedCommandHeight <= 170.5,
                $"收起态命令区高度不应超过 170 DIP：actual={collapsedCommandHeight:0.##}。");

            // 1. 展开常态：命令区守住 170 限高，“添加”完整可见可点；滚动字段时“添加”纹丝不动、正文向上滚动。
            Click(addToggle, floating);
            Require(noteEntry.IsVisible && addButton.IsVisible && addButton.IsEnabled,
                "展开快速新增后表单与“添加”按钮必须可见可用。");
            var expandedCommandLimitHeight = ResolveCommandOuterElement(commandArea, commandScroller).ActualHeight;
            Require(expandedCommandLimitHeight <= 170.5,
                $"展开态命令区高度必须守住 170 DIP 限高，实际为 {expandedCommandLimitHeight:0.##}。");
            RequireCompletelyVisibleAndClickable(addButton, root, taskCard, "展开常态下的“添加”按钮");

            var longText = new string('敏', 60);
            noteText.Text = longText;
            Settle(floating);
            Require(commandScroller.ScrollableHeight > 0.5,
                $"输入长正文后字段区应真实可滚动，实际 ScrollableHeight={commandScroller.ScrollableHeight:0.##}。");
            var addBoundsBeforeScroll = Bounds(addButton, root);
            var textBoundsBeforeScroll = Bounds(noteText, root);
            commandScroller.ScrollToBottom();
            Settle(floating);
            Require(commandScroller.VerticalOffset > 0.5,
                $"字段区滚到底后 VerticalOffset 应大于 0，实际为 {commandScroller.VerticalOffset:0.##}。");
            var addBoundsAfterScroll = Bounds(addButton, root);
            var textBoundsAfterScroll = Bounds(noteText, root);
            Require(textBoundsAfterScroll.Top < textBoundsBeforeScroll.Top - 0.5,
                $"滚动字段区时正文输入框应随之上移：before={textBoundsBeforeScroll.Top:0.##}，after={textBoundsAfterScroll.Top:0.##}。");
            Require(Math.Abs(addBoundsAfterScroll.Top - addBoundsBeforeScroll.Top) <= 0.5 &&
                    Math.Abs(addBoundsAfterScroll.Bottom - addBoundsBeforeScroll.Bottom) <= 0.5,
                $"字段区滚动时“添加”所在动作行必须固定在命令区底部不得移动：before={addBoundsBeforeScroll}，after={addBoundsAfterScroll}。");
            RequireCompletelyVisibleAndClickable(addButton, root, taskCard, "字段滚到底时的“添加”按钮");

            // 2. 正文留空出错态：多出一行错误，“添加”仍完整位于视口内并可点击；错误文案与输入框同时可见。
            var captureDirectory = CreateCaptureDirectory();
            noteText.Text = string.Empty;
            commandScroller.ScrollToHome();
            Settle(floating);
            ActivateForKeyboard(floating);
            Click(addButton, floating);
            Require(noteTextError.IsVisible &&
                    string.Equals(noteTextError.Text, "请先写下便签内容。", StringComparison.Ordinal),
                $"正文留空提交后应显示字段错误，实际 visible={noteTextError.IsVisible}，text=「{noteTextError.Text}」。");
            Require(noteText.IsKeyboardFocusWithin, "正文留空出错后焦点应回到正文输入框。");
            RequireCompletelyInsideClipViewport(noteText, commandScroller, root, "正文出错态下的正文输入框");
            RequireCompletelyInsideClipViewport(noteTextError, commandScroller, root, "正文出错态下的错误文案");
            RequireCompletelyVisibleAndClickable(addButton, root, taskCard, "正文出错态下的“添加”按钮");
            RequireCompletelyVisibleAndClickable(addToggle, root, taskCard, "正文出错态下的“收起”按钮");
            Capture(floating, root, captureDirectory, "floating-quick-add-error.png");

            // 修正正文后不滚动即可直接点击“添加”提交成功并自动收起。
            var countBeforeSubmit = workspace.Snapshot.Settings.Notes.Count;
            noteText.Text = "出错修正后直接提交";
            Settle(floating);
            RequireCompletelyVisibleAndClickable(addButton, root, taskCard, "修正正文后的“添加”按钮");
            Click(addButton, floating);
            Require(workspace.Snapshot.Settings.Notes.Count == countBeforeSubmit + 1 &&
                    noteEntry.Visibility == Visibility.Collapsed,
                "修正正文后直接点击“添加”必须保存成功并收起快速新增表单。");

            // 3. 日期留空出错态：日期错误可见且“添加”仍完整位于视口内可点击。
            Click(addToggle, floating);
            noteText.Text = "日期错误校验便签";
            noteDate.SelectedDate = null;
            Settle(floating);
            Click(addButton, floating);
            Require(noteDateError.IsVisible &&
                    string.Equals(noteDateError.Text, "请选择便签日期。", StringComparison.Ordinal),
                $"日期留空提交后应显示日期字段错误，实际 visible={noteDateError.IsVisible}，text=「{noteDateError.Text}」。");
            Require(noteDate.IsKeyboardFocusWithin, "日期留空出错后焦点应回到日期框。");
            RequireCompletelyInsideClipViewport(noteDate, commandScroller, root, "日期出错态下的日期框");
            RequireCompletelyInsideClipViewport(noteDateError, commandScroller, root, "日期出错态下的错误文案");
            RequireCompletelyVisibleAndClickable(addButton, root, taskCard, "日期出错态下的“添加”按钮");
            Capture(floating, root, captureDirectory, "floating-quick-add-date-error.png");

            noteDate.SelectedDate = today;
            Settle(floating);
            Click(addButton, floating);
            Require(workspace.Snapshot.Settings.Notes.Count == countBeforeSubmit + 2 &&
                    noteEntry.Visibility == Visibility.Collapsed,
                "补回日期后直接点击“添加”必须保存成功并收起表单。");

            // 4. 每次展开从表单顶部开始（点“收起”和提交成功两条收起路径均守住），收起后命令栏几何与命中高度不变。
            VerifyReopensAtTop(
                floating,
                root,
                commandScroller,
                noteEntry,
                noteText,
                addToggle,
                longText,
                "点“收起”收起",
                () => Click(addToggle, floating));
            VerifyReopensAtTop(
                floating,
                root,
                commandScroller,
                noteEntry,
                noteText,
                addToggle,
                "提交复位便签-" + longText,
                "提交成功收起",
                () => Click(addButton, floating));

            var restoredBarBounds = Bounds(commandBar, root);
            Require(Math.Abs(restoredBarBounds.X - collapsedBarBounds.X) <= 0.5 &&
                    Math.Abs(restoredBarBounds.Y - collapsedBarBounds.Y) <= 0.5 &&
                    Math.Abs(restoredBarBounds.Width - collapsedBarBounds.Width) <= 0.5 &&
                    Math.Abs(restoredBarBounds.Height - collapsedBarBounds.Height) <= 0.5,
                $"收起后底部命令行几何必须与常态一致：before={collapsedBarBounds}，after={restoredBarBounds}。");
            Require(addToggle.ActualHeight >= 37.5 && searchButton.ActualHeight >= 37.5 && settingsButton.ActualHeight >= 37.5,
                $"收起后底部命令三按钮命中高度必须保持 38 DIP：toggle={addToggle.ActualHeight:0.##}，search={searchButton.ActualHeight:0.##}，settings={settingsButton.ActualHeight:0.##}。");

            // 5. Tab 顺序不变：正文 → 日期 → 提醒规则 → 标签 → 添加 → 收起 → 搜索 → 设置。
            Click(addToggle, floating);
            ActivateForKeyboard(floating);
            Require(Keyboard.Focus(noteText) == noteText, "未能把键盘焦点送到快速新增正文框。");
            Settle(floating);
            var expectedOrder = new (string Name, DependencyObject Container)[]
            {
                ("FloatingNoteText", noteText),
                ("FloatingNoteDate", noteDate),
                ("FloatingManageReminderRules", manageReminderRules),
                ("FloatingTags", tagsSelector),
                ("FloatingAddNote", addButton),
                ("FloatingAddNoteToggle", addToggle),
                ("FloatingSearchNotes", searchButton),
                ("FloatingOpenSettings", settingsButton)
            };
            VerifyTabSequence(floating, expectedOrder);

            Click(addToggle, floating);
            return (true, meaning);
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

    private static void VerifyReopensAtTop(
        FloatingWindow floating,
        FrameworkElement root,
        ScrollViewer commandScroller,
        Border noteEntry,
        TextBox noteText,
        Button addToggle,
        string textBeforeCollapse,
        string pathLabel,
        Action collapse)
    {
        Click(addToggle, floating);
        noteText.Text = textBeforeCollapse;
        Settle(floating);
        commandScroller.ScrollToBottom();
        Settle(floating);
        Require(noteEntry.IsVisible && commandScroller.VerticalOffset > 0.5,
            $"{pathLabel}前未形成滚动偏移：offset={commandScroller.VerticalOffset:0.##}。");

        collapse();
        Settle(floating);
        Require(noteEntry.Visibility == Visibility.Collapsed,
            $"{pathLabel}后快速新增未收起。");

        Click(addToggle, floating);
        noteText.Text = new string('敏', 60);
        Settle(floating);
        var entryTop = Bounds(noteEntry, root).Top;
        var scrollerTop = Bounds(commandScroller, root).Top;
        Require(noteEntry.IsVisible &&
                commandScroller.ScrollableHeight > 0.5 &&
                commandScroller.VerticalOffset <= 0.5 &&
                Math.Abs(entryTop - scrollerTop) <= 0.5,
            $"{pathLabel}后再次展开未回到表单顶部：offset={commandScroller.VerticalOffset:0.##}，" +
            $"scrollable={commandScroller.ScrollableHeight:0.##}，entryTop={entryTop:0.##}，scrollerTop={scrollerTop:0.##}。");

        Click(addToggle, floating);
        noteText.Text = string.Empty;
        Settle(floating);
    }

    private static void VerifyTabSequence(
        FloatingWindow floating,
        IReadOnlyList<(string Name, DependencyObject Container)> expectedOrder)
    {
        var visited = new List<string>();
        var current = Keyboard.FocusedElement as DependencyObject;
        Require(current is not null, "Tab 顺序验收前键盘焦点为空。");
        var initialMatch = MatchContainer(current!, expectedOrder);
        Require(initialMatch == expectedOrder[0].Name,
            $"Tab 起始焦点应落在 {expectedOrder[0].Name}，实际落在 {DescribeFocused(current)}。");
        visited.Add(initialMatch!);

        for (var step = 0; step < 32 && visited.Count < expectedOrder.Count; step++)
        {
            var focusedElement = Keyboard.FocusedElement as UIElement
                ?? throw new InvalidOperationException($"第 {step + 1} 步 Tab 时焦点元素不可遍历。");
            Require(focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                $"第 {step + 1} 步 Tab 未能推进焦点（当前：{DescribeFocused(focusedElement)}）。");
            Settle(floating);
            var next = Keyboard.FocusedElement as DependencyObject
                ?? throw new InvalidOperationException($"第 {step + 1} 步 Tab 后焦点变空。");
            var matched = MatchContainer(next, expectedOrder);
            Require(matched is not null,
                $"Tab 链路在第 {step + 1} 步跳到预期序列之外：{DescribeFocused(next)}，已走过 [{string.Join(" -> ", visited)}]。");
            if (string.Equals(matched, visited[^1], StringComparison.Ordinal))
            {
                // 例如 DatePicker 内部包含文本框与日历按钮两个停靠点，属于同一逻辑控件。
                continue;
            }
            visited.Add(matched!);
        }

        var expectedNames = expectedOrder.Select(item => item.Name).ToArray();
        Require(visited.SequenceEqual(expectedNames, StringComparer.Ordinal),
            $"展开态快速新增 Tab 顺序发生变化：期望 [{string.Join(" -> ", expectedNames)}]，实际 [{string.Join(" -> ", visited)}]。");
    }

    private static string? MatchContainer(
        DependencyObject focused,
        IReadOnlyList<(string Name, DependencyObject Container)> expectedOrder)
    {
        foreach (var item in expectedOrder)
        {
            if (IsSameOrDescendant(focused, item.Container))
            {
                return item.Name;
            }
        }
        return null;
    }

    private static string DescribeFocused(DependencyObject? element) => element switch
    {
        FrameworkElement fe when !string.IsNullOrEmpty(fe.Name) => $"{fe.GetType().Name}({fe.Name})",
        FrameworkElement fe => fe.GetType().Name,
        null => "<null>",
        _ => element.GetType().Name
    };

    private static void RequireCompletelyVisibleAndClickable(
        Button button,
        FrameworkElement root,
        FrameworkElement taskCard,
        string label)
    {
        Require(button.IsVisible && button.IsEnabled, $"{label}必须可见且可用。");
        var buttonBounds = Bounds(button, root);
        var cardBounds = Bounds(taskCard, root);
        Require(buttonBounds.Width > 0 && buttonBounds.Height >= 37.5,
            $"{label}尺寸异常：{buttonBounds}。");
        Require(buttonBounds.Left >= cardBounds.Left - 0.5 &&
                buttonBounds.Top >= cardBounds.Top - 0.5 &&
                buttonBounds.Right <= cardBounds.Right + 0.5 &&
                buttonBounds.Bottom <= cardBounds.Bottom + 0.5 &&
                buttonBounds.Bottom <= root.ActualHeight + 0.5,
            $"{label}超出卡片或窗口视口：button={buttonBounds}，card={cardBounds}，windowH={root.ActualHeight:0.##}。");

        // 检查所有会裁切的视觉祖先（例如 ScrollContentPresenter / ClipToBounds=True）。
        for (var ancestor = VisualTreeHelper.GetParent(button);
             ancestor is not null;
             ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is FrameworkElement clipElement &&
                (clipElement is ScrollContentPresenter || clipElement.ClipToBounds))
            {
                var clipBounds = Bounds(clipElement, root);
                Require(buttonBounds.Top >= clipBounds.Top - 0.5 &&
                        buttonBounds.Bottom <= clipBounds.Bottom + 0.5 &&
                        buttonBounds.Left >= clipBounds.Left - 0.5 &&
                        buttonBounds.Right <= clipBounds.Right + 0.5,
                    $"{label}被祖先 {clipElement.GetType().Name}({clipElement.Name}) 裁切：" +
                    $"button={buttonBounds}，clip={clipBounds}。");
            }
            if (ReferenceEquals(ancestor, root))
            {
                break;
            }
        }

        // 命中测试：按钮中心、上 1/4 和下 3/4 处均必须命中按钮自身或其子树，证明完整可点。
        var probeYRatios = new[] { 0.25, 0.5, 0.75 };
        foreach (var ratio in probeYRatios)
        {
            var probePoint = new Point(
                buttonBounds.Left + buttonBounds.Width * 0.5,
                buttonBounds.Top + buttonBounds.Height * ratio);
            var hit = root.InputHitTest(probePoint) as DependencyObject;
            Require(hit is not null && IsSameOrDescendant(hit, button),
                $"{label}在纵比 {ratio:0.00} 点 ({probePoint.X:0.##},{probePoint.Y:0.##}) 不可点击，实际命中 {DescribeFocused(hit)}。");
        }
    }

    private static void RequireCompletelyInsideClipViewport(
        FrameworkElement element,
        ScrollViewer scroller,
        FrameworkElement root,
        string label)
    {
        Require(element.IsVisible && element.ActualHeight > 0, $"{label}必须可见且非零高。");
        var elementBounds = Bounds(element, root);
        var scrollerBounds = Bounds(scroller, root);
        Require(elementBounds.Top >= scrollerBounds.Top - 0.5 &&
                elementBounds.Bottom <= scrollerBounds.Bottom + 0.5,
            $"{label}未完整落在滚动视口内：element={elementBounds}，scroller={scrollerBounds}。");
    }

    private static FrameworkElement ResolveCommandOuterElement(
        FrameworkElement commandArea,
        ScrollViewer commandScroller) =>
        IsSameOrDescendant(commandScroller, commandArea) ? commandArea : commandScroller;

    private static bool IsSameOrDescendant(DependencyObject candidate, DependencyObject ancestor)
    {
        for (var current = candidate; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject element) =>
        element is Visual
            ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    private static T FindNamed<T>(FrameworkElement root, string name)
        where T : FrameworkElement =>
        root.FindName(name) as T
            ?? VisualDescendants<T>(root).SingleOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"找不到悬浮窗控件：{name}（{typeof(T).Name}）。");

    private static void SeedNote(DesktopWorkspace workspace, string text, DateTime date)
    {
        var result = workspace.Apply(new SaveNoteChange(
            null,
            text,
            date,
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"写入悬浮窗验证便签失败：{result.Error}");
        }
    }

    private static void Click(ButtonBase button, Window window)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Settle(window);
    }

    private static void ShowAndWaitFirstFrame(Window window)
    {
        PrepareOffscreen(window);
        var contentRendered = false;
        EventHandler rendered = (_, _) => contentRendered = true;
        window.ContentRendered += rendered;
        try
        {
            window.Show();
            var frame = WindowValidationInput.PumpRenderedFrames(
                window.Dispatcher,
                2000,
                () => contentRendered);
            Settle(window);
            Require(contentRendered && frame.Completed,
                $"悬浮窗首帧未完成：contentRendered={contentRendered}，frameCompleted={frame.Completed}。");
        }
        finally
        {
            window.ContentRendered -= rendered;
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
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-floating-quick-add-error");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Capture(Window window, FrameworkElement root, string? directory, string fileName)
    {
        if (directory is null)
        {
            return;
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
            Path.Combine(directory, fileName),
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

    private sealed class StubManagerHost : IManagerWorkspaceHost
    {
        public void Reveal(WorkspaceKind workspace)
        {
        }

        public void RevealPreferences(WorkspaceKind workspace)
        {
        }
    }
}
