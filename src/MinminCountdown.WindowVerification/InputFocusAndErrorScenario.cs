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

// #303：只经真实管理窗口、公共模板/视觉树、自动化属性和真实窗口消息验收输入焦点与错误状态。
internal static class InputFocusAndErrorScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static void Run()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-input-focus-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var announcements = new List<(string Id, string Text, AutomationLiveSetting Live)>();
        var capture = CreateEvidenceWriter();
        MainWindow? main = null;

        void Observe(TextBlock block) => announcements.Add((
            AutomationProperties.GetAutomationId(block) ?? string.Empty,
            AutomationProperties.GetName(block) ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));

        UiAccessibility.Announced += Observe;
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
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            var contentRendered = false;
            main.ContentRendered += (_, _) => contentRendered = true;
            main.Show();
            main.Activate();
            var firstFrame = WindowValidationInput.PumpRenderedFrames(
                main.Dispatcher,
                2000,
                () => contentRendered);
            SettleLayout(main);
            Require(contentRendered && firstFrame.Completed, "管理窗口没有完成首帧。");

            var expand = Required<Button>(main, "CountdownEditorExpandButton");
            expand.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);

            var name = Required<TextBox>(main, "CountdownName");
            var date = Required<DatePicker>(main, "CountdownDate");
            var time = Required<TextBox>(main, "CountdownTime");
            var save = Required<Button>(main, "SaveCountdownButton");
            var nameError = Required<TextBlock>(main, "CountdownNameError");
            var dateError = Required<TextBlock>(main, "CountdownDateError");
            var timeError = Required<TextBlock>(main, "CountdownTimeError");
            var message = Required<TextBlock>(main, "CountdownMessage");
            Require(time.IsVisible, "展开倒计时编辑器后截止时刻输入框仍不可见。");

            name.Text = "有效输入";
            date.SelectedDate = workspace.Snapshot.BeijingNow.Date.AddDays(2);
            time.Text = "00:00:00";
            Keyboard.Focus(save);
            SettleLayout(main);

            var restingGeometry = ReadGeometry(main, name, date, time, save);

            foreach (var (input, label) in new (Control Input, string Label)[]
                     {
                         (name, "valid-focused-name"),
                         (date, "valid-focused-date"),
                         (time, "valid-focused-time")
                     })
            {
                Keyboard.Focus(input);
                SettleLayout(main);
                Require(input.IsKeyboardFocusWithin, $"{input.Name} 没有取得键盘焦点。");
                RequireState(main, input, "MainActionFocusBrush", 2, "有效且有焦点");
                Require(
                    !ReferenceEquals(ReadBoundaries(input)[0].Brush, main.FindResource("ActiveBrush")) &&
                    !ReferenceEquals(ReadBoundaries(input)[0].Brush, main.FindResource("DangerBrush")),
                    $"{input.Name} 有效焦点仍与红色/危险色混用。");
                var focusedGeometry = ReadGeometry(main, name, date, time, save);
                Require(
                    restingGeometry.Equals(focusedGeometry),
                    $"{input.Name} 有效焦点挤动布局：resting={restingGeometry}，focused={focusedGeometry}。");
                capture?.Invoke(main, label);
            }

            RequireHover(main, name, save);
            RequireHover(main, date, save);
            RequireHover(main, time, save);

            // 验证生产 ComboBox（规则编辑器 ReminderRuleMode）仍保持暖金焦点与真实悬停边界
            var rulesEditor = new ReminderRulesEditorWindow(
                Array.Empty<ReminderRule>(),
                new FixedClock(FixtureUtc))
            {
                Owner = main,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            try
            {
                rulesEditor.Show();
                rulesEditor.Activate();
                WindowValidationInput.Pump(rulesEditor.Dispatcher);
                rulesEditor.UpdateLayout();
                var ruleMode = FindByAutomationId<ComboBox>(rulesEditor, "ReminderRuleMode");
                var ruleCancel = FindByAutomationId<Button>(rulesEditor, "CancelReminderRulesButton");
                Keyboard.Focus(ruleMode);
                WindowValidationInput.Pump(rulesEditor.Dispatcher);
                rulesEditor.UpdateLayout();
                Require(ruleMode.IsKeyboardFocusWithin, "ReminderRuleMode 下拉框没有取得键盘焦点。");
                RequireState(main, ruleMode, "MainActionFocusBrush", 2, "下拉框有效且有焦点");
                RequireHover(rulesEditor, main, ruleMode, ruleCancel);
            }
            finally
            {
                rulesEditor.Close();
                WindowValidationInput.Pump(rulesEditor.Dispatcher);
                main.Activate();
                SettleLayout(main);
            }

            announcements.Clear();
            name.Text = string.Empty;
            date.SelectedDate = null;
            ClickSave(main, save);
            Require(name.IsKeyboardFocusWithin, "名称与日期同时无效时没有聚焦首个名称错误。");
            RequireState(main, name, "DangerBrush", 2, "名称出错有焦点");
            RequireState(main, date, "DangerBrush", 1, "日期出错无焦点");
            RequireAccessibleError(nameError, name, "请先填写倒计时名称。");
            RequireAccessibleError(dateError, date, "请选择截止日期。");
            Require(
                string.Equals(message.Text, nameError.Text, StringComparison.Ordinal),
                "唯一错误行没有镜像首个字段错误。");
            Require(
                announcements.Any(notice =>
                    notice.Id == "CountdownNameError" &&
                    notice.Text == "请先填写倒计时名称。" &&
                    notice.Live == AutomationLiveSetting.Polite),
                "名称错误行没有触发礼貌直播区播报。");
            capture?.Invoke(main, "error-name-focused-date-unfocused");

            var errorBeforeSwitch = ReadGeometry(main, name, date, time, save);
            Keyboard.Focus(date);
            SettleLayout(main);
            Require(date.IsKeyboardFocusWithin, "日期错误没有取得焦点。");
            RequireState(main, name, "DangerBrush", 1, "名称出错无焦点");
            RequireState(main, date, "DangerBrush", 2, "日期出错有焦点");
            var errorAfterSwitch = ReadGeometry(main, name, date, time, save);
            Require(
                errorBeforeSwitch.Equals(errorAfterSwitch),
                $"错误三态切换挤动布局：before={errorBeforeSwitch}，after={errorAfterSwitch}。");
            capture?.Invoke(main, "error-date-focused-name-unfocused");

            WithPointerHover(main, name, () =>
            {
                Require(name.IsMouseOver, "错误态悬停探针未命中名称框。");
                RequireState(main, name, "DangerBrush", 1, "出错无焦点悬停保持危险色");
            });

            name.Text = "修正后的名称";
            SettleLayout(main);
            RequireCleared(name, nameError);
            RequireState(main, name, "ControlBorderBrush", 1, "名称修正后常态");
            Require(dateError.IsVisible, "修正名称不应连带清除仍存在的日期错误。");
            RequireState(main, date, "DangerBrush", 2, "名称修正后日期仍保持出错有焦点");

            date.SelectedDate = workspace.Snapshot.BeijingNow.Date.AddDays(3);
            SettleLayout(main);
            RequireCleared(date, dateError);
            Require(string.IsNullOrEmpty(message.Text), "所有字段修正后唯一错误行没有清除。");
            RequireState(main, date, "MainActionFocusBrush", 2, "日期修正后仍有焦点");
            var restoredGeometry = ReadGeometry(main, name, date, time, save);
            Require(
                restingGeometry.Equals(restoredGeometry),
                $"修正错误后布局未回到初始几何：resting={restingGeometry}，restored={restoredGeometry}。");

            date.SelectedDate = null;
            ClickSave(main, save);
            Require(date.IsKeyboardFocusWithin, "仅日期无效时没有聚焦日期。");
            Keyboard.Focus(name);
            SettleLayout(main);
            using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                contrast.ChangeColors(new ApplicationTheme.SystemContrastPalette(
                    Colors.Black,
                    Colors.White,
                    Colors.Black,
                    Colors.White,
                    Colors.Yellow,
                    Colors.Black,
                    Colors.Gray));
                SettleLayout(main);
                RequireState(main, name, "MainActionFocusBrush", 2, "系统对比度有效焦点");
                RequireState(main, date, "DangerBrush", 1, "系统对比度错误无焦点");
                Require(
                    BrushColor(main.FindResource("MainActionFocusBrush")) == Colors.Yellow,
                    "系统对比度焦点没有映射 Highlight。");
                Require(
                    BrushColor(main.FindResource("DangerBrush")) == Colors.White,
                    "系统对比度错误没有映射 WindowText。");
                Require(dateError.IsVisible, "系统对比度错误缺少独立错误行这一非颜色线索。");
                capture?.Invoke(main, "high-contrast-focus-vs-error");

                Keyboard.Focus(date);
                SettleLayout(main);
                Require(date.IsKeyboardFocusWithin, "系统对比度下日期错误未能获焦。");
                RequireState(main, date, "DangerBrush", 2, "系统对比度错误有焦点");
            }
            SettleLayout(main);

            date.SelectedDate = workspace.Snapshot.BeijingNow.Date.AddDays(4);
            time.Text = "25:00:00";
            ClickSave(main, save);
            Require(time.IsKeyboardFocusWithin, "截止时刻无效时没有聚焦截止时刻字段。");
            RequireState(main, time, "DangerBrush", 2, "时间输入出错有焦点");
            RequireAccessibleError(timeError, time, "截止时刻请使用 HH:mm:ss。");
            time.Text = "00:00:00";
            SettleLayout(main);
            RequireCleared(time, timeError);
            RequireState(main, time, "MainActionFocusBrush", 2, "时间修正后仍有焦点");
        }
        finally
        {
            UiAccessibility.Announced -= Observe;
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

    private static void RequireHover(MainWindow main, Control input, Button save) =>
        RequireHover(main, main, input, save);

    private static void RequireHover(Window host, MainWindow resourcesOwner, Control input, Button idleFocusTarget)
    {
        Keyboard.Focus(idleFocusTarget);
        WindowValidationInput.Pump(host.Dispatcher);
        host.UpdateLayout();
        WithPointerHover(host, input, () =>
        {
            Require(!input.IsKeyboardFocusWithin, $"{input.Name} 悬停检查前不应持有键盘焦点。");
            Require(input.IsMouseOver, $"{input.Name} 没有保持真实悬停状态。");
            RequireState(resourcesOwner, input, "ActiveHoverBrush", 1, "悬停");
        });
    }

    private static void WithPointerHover(Window host, Control target, Action verify)
    {
        target.BringIntoView();
        WindowValidationInput.Pump(host.Dispatcher);
        host.UpdateLayout();
        if (PresentationSource.FromVisual(target) is not HwndSource source ||
            source.RootVisual is not Visual root)
        {
            throw new InvalidOperationException($"{target.Name} 没有可用于悬停探针的 HWND。");
        }

        var point = target.TransformToAncestor(root).Transform(
            new Point(Math.Min(target.RenderSize.Width * 0.35, 24), target.RenderSize.Height / 2));
        point = source.CompositionTarget.TransformToDevice.Transform(point);
        var coordinates = new IntPtr(((int)point.Y << 16) | ((int)point.X & 0xffff));
        host.Activate();
        SetCapture(source.Handle);
        try
        {
            Require(GetCapture() == source.Handle, "隔离验证窗口无法捕获鼠标移动消息。");
            SendMessage(source.Handle, 0x0200, IntPtr.Zero, coordinates);
            WindowValidationInput.Pump(host.Dispatcher);
            host.UpdateLayout();
            verify();
        }
        finally
        {
            if (GetCapture() == source.Handle)
            {
                ReleaseCapture();
            }
            SendMessage(source.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
            WindowValidationInput.Pump(host.Dispatcher);
            host.UpdateLayout();
        }
    }

    private static T FindByAutomationId<T>(DependencyObject root, string automationId) where T : DependencyObject
    {
        foreach (var candidate in Walk(root))
        {
            if (candidate is T match &&
                string.Equals(AutomationProperties.GetAutomationId(match), automationId, StringComparison.Ordinal))
            {
                return match;
            }
        }

        throw new InvalidOperationException($"窗口缺少 AutomationId={automationId} 的 {typeof(T).Name}。");

        static IEnumerable<DependencyObject> Walk(DependencyObject node)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(node, index);
                yield return child;
                foreach (var nested in Walk(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static void ClickSave(MainWindow main, Button save)
    {
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
    }

    private static void RequireAccessibleError(TextBlock error, Control source, string expected)
    {
        Require(error.IsVisible, $"{error.Name} 没有显示。");
        Require(string.Equals(error.Text, expected, StringComparison.Ordinal), $"{error.Name} 文案不符。");
        Require(
            string.Equals(AutomationProperties.GetName(error), expected, StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetHelpText(error), expected, StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetHelpText(source), expected, StringComparison.Ordinal) &&
            AutomationProperties.GetLiveSetting(error) == AutomationLiveSetting.Polite,
            $"{error.Name} 的名称、帮助文本、来源帮助或播报级别不符。");
    }

    private static void RequireCleared(Control source, TextBlock error)
    {
        Require(!error.IsVisible && string.IsNullOrEmpty(error.Text), $"{error.Name} 修正后没有立即清除。");
        Require(string.IsNullOrEmpty(AutomationProperties.GetHelpText(source)), $"{source.Name} 修正后仍保留错误帮助文本。");
    }

    private static void RequireState(
        MainWindow main,
        Control input,
        string expectedBrushKey,
        double expectedDip,
        string state)
    {
        input.ApplyTemplate();
        var expectedBrush = (Brush)main.FindResource(expectedBrushKey);
        var boundaries = ReadBoundaries(input);
        Require(boundaries.Length > 0, $"{input.Name} 没有可取读的输入边界。");
        foreach (var boundary in boundaries)
        {
            Require(
                BrushMatches(boundary.Brush, expectedBrush),
                $"{input.Name}（{boundary.Part}）{state} 边界角色不符：期望 {expectedBrushKey}（{expectedBrush}），实际 {boundary.Brush}。");
            Require(
                Math.Abs(boundary.Thickness.Left - expectedDip) < 0.01 &&
                Math.Abs(boundary.Thickness.Top - expectedDip) < 0.01,
                $"{input.Name}（{boundary.Part}）{state} 边厚不是 {expectedDip} DIP：{boundary.Thickness}。");
        }
    }

    private static bool BrushMatches(Brush? actual, Brush expected) =>
        ReferenceEquals(actual, expected) ||
        (actual is SolidColorBrush a && expected is SolidColorBrush b && a.Color == b.Color);

    private static (string Part, Brush? Brush, Thickness Thickness)[] ReadBoundaries(Control input)
    {
        if (input is DatePicker picker)
        {
            var dateSurface = (Border?)picker.Template?.FindName("Surface", picker)
                ?? throw new InvalidOperationException("日期框缺少单框外层 Surface。");
            var text = (Control?)picker.Template?.FindName("PART_TextBox", picker)
                ?? throw new InvalidOperationException("日期框缺少 PART_TextBox。");
            var button = (Control?)picker.Template?.FindName("PART_Button", picker)
                ?? throw new InvalidOperationException("日期框缺少 PART_Button。");
            Require(
                text.BorderThickness == default && button.BorderThickness == default,
                $"日期框内部部件不应再画独立边框：text={text.BorderThickness}，button={button.BorderThickness}。");
            return [("Surface", dateSurface.BorderBrush, dateSurface.BorderThickness)];
        }

        var surface = (Border?)input.Template?.FindName("Surface", input)
            ?? throw new InvalidOperationException($"{input.Name} 缺少 Surface。");
        return [("Surface", surface.BorderBrush, surface.BorderThickness)];
    }

    private static FieldGeometry ReadGeometry(
        MainWindow main,
        TextBox name,
        DatePicker date,
        Control time,
        Button save)
    {
        var root = (Visual)main.Content;
        name.ApplyTemplate();
        var contentHost = (FrameworkElement?)name.Template?.FindName("PART_ContentHost", name)
            ?? throw new InvalidOperationException("名称框缺少 PART_ContentHost。");
        return new FieldGeometry(
            Bounds(name, root),
            Bounds(contentHost, root),
            Bounds(date, root),
            Bounds(time, root),
            Bounds(save, root));
    }

    private static Action<MainWindow, string>? CreateEvidenceWriter()
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
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-input-focus-error");
        Directory.CreateDirectory(directory);
        return (window, label) =>
        {
            var editor = Required<FrameworkElement>(window, "CountdownEditorSurface");
            var originalOpacity = window.Opacity;
            try
            {
                if (originalOpacity == 0)
                {
                    window.Opacity = 1;
                    SettleLayout(window);
                }

                var dpi = VisualTreeHelper.GetDpi(window);
                var frame = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX)),
                    Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY)),
                    dpi.PixelsPerInchX,
                    dpi.PixelsPerInchY,
                    PixelFormats.Pbgra32);
                frame.Render(window);
                var bounds = editor.TransformToAncestor(window).TransformBounds(new Rect(editor.RenderSize));
                bounds.Inflate(10, 10);
                var left = Math.Clamp((int)Math.Floor(bounds.Left * dpi.DpiScaleX), 0, frame.PixelWidth - 1);
                var top = Math.Clamp((int)Math.Floor(bounds.Top * dpi.DpiScaleY), 0, frame.PixelHeight - 1);
                var right = Math.Clamp((int)Math.Ceiling(bounds.Right * dpi.DpiScaleX), left + 1, frame.PixelWidth);
                var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * dpi.DpiScaleY), top + 1, frame.PixelHeight);
                var crop = new CroppedBitmap(frame, new Int32Rect(left, top, right - left, bottom - top));
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(crop));
                using var stream = new FileStream(
                    Path.Combine(directory, label + ".png"),
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                encoder.Save(stream);
            }
            finally
            {
                if (originalOpacity == 0)
                {
                    window.Opacity = 0;
                    SettleLayout(window);
                }
            }
        };
    }

    private static Color BrushColor(object resource) => ((SolidColorBrush)resource).Color;

    private static T Required<T>(MainWindow main, string name) where T : class =>
        main.FindName(name) as T ?? throw new InvalidOperationException($"管理窗口缺少控件 {name}（{typeof(T).Name}）。");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    private readonly record struct FieldGeometry(Rect Name, Rect NameContent, Rect Date, Rect Hour, Rect Save);
}
