using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #328：管理窗口倒计时截止时刻改为单一可键入 HH:mm:ss 文本框（复用 TimeInputBehavior）。
// 仅经真实管理窗口、公共模板/视觉树、键盘输入与自动化 peer 验收默认北京时间、直接键入保存、
// 上下键分段步进与回绕、失焦/提交校验、焦点落位、1/2 DIP 错误边框、无布局位移、标签关系与高对比度。
internal static class CountdownDeadlineTimeInputScenario
{
    private const string ExpectedTimeError = "截止时刻请使用 HH:mm:ss。";
    // 2038-07-08 01:23:45 UTC == 2038-07-08 09:23:45 +08:00（北京时间）
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 23, 45, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) RunTypingAndStepping(string meaning)
    {
        var failures = new List<string>();
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-time-typing-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;

        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            main = CreateOffscreenMainWindow(workspace, calendar, diagnosticPath);
            ShowAndSettle(main);

            var expand = Required<Button>(main, "CountdownEditorExpandButton");
            expand.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);

            if (main.FindName("CountdownHour") is not null ||
                main.FindName("CountdownMinute") is not null ||
                main.FindName("CountdownSecond") is not null)
            {
                failures.Add("主窗口仍残留旧三联时/分/秒 ComboBox（CountdownHour/Minute/Second）。");
            }

            if (main.FindName("CountdownTime") is not TextBox time)
            {
                failures.Add("主窗口缺少单一可键入截止时刻文本框 CountdownTime。");
                return (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
            }

            var name = Required<TextBox>(main, "CountdownName");
            var date = Required<DatePicker>(main, "CountdownDate");
            var save = Required<Button>(main, "SaveCountdownButton");

            // 1. 新增态默认值等于当前北京时间 HH:mm:ss，且启用表格数字、关闭 IME
            var expectedBeijing = ChinaStandardTime.ToBeijingTime(FixtureUtc);
            var expectedDefaultTime = expectedBeijing.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (!time.IsVisible)
            {
                failures.Add("展开倒计时编辑区后 CountdownTime 不可见。");
            }

            if (!string.Equals(time.Text, expectedDefaultTime, StringComparison.Ordinal))
            {
                failures.Add($"新增默认截止时刻应为当前北京时间 {expectedDefaultTime}，实际「{time.Text}」。");
            }

            if (date.SelectedDate != expectedBeijing.AddDays(1).Date)
            {
                failures.Add($"新增默认截止日期应为次日 {expectedBeijing.AddDays(1):yyyy-MM-dd}，实际 {date.SelectedDate:yyyy-MM-dd}。");
            }

            if (Typography.GetNumeralAlignment(time) != FontNumeralAlignment.Tabular)
            {
                failures.Add($"CountdownTime 未启用表格数字（actual={Typography.GetNumeralAlignment(time)}）。");
            }

            if (InputMethod.GetIsInputMethodEnabled(time))
            {
                failures.Add("CountdownTime 未禁用 IME。");
            }

            // 2. 直接键入合法 HH:mm:ss 并保存，校验内存与落盘精确 DateTimeOffset 领域值
            name.Text = "可键入时刻测试";
            var targetDate = new DateTime(2038, 8, 15);
            date.SelectedDate = targetDate;
            time.Text = "18:45:30";
            SettleLayout(main);
            ClickSave(main, save);

            var expectedUtc = CountdownFormatting.ToUtc(targetDate, 18, 45, 30);
            var saved = workspace.Snapshot.Settings.Countdowns
                .SingleOrDefault(item => string.Equals(item.Name, "可键入时刻测试", StringComparison.Ordinal));
            var reloaded = DesktopWorkspace.Load(settingsPath).Snapshot.Settings.Countdowns
                .SingleOrDefault(item => string.Equals(item.Name, "可键入时刻测试", StringComparison.Ordinal));
            if (saved is null || reloaded is null ||
                saved.TargetUtc != expectedUtc || reloaded.TargetUtc != expectedUtc)
            {
                failures.Add(
                    $"直接键入 18:45:30 保存后领域值不符：expected={expectedUtc:O}，saved={saved?.TargetUtc:O}，reloaded={reloaded?.TargetUtc:O}。");
            }

            // 保存后编辑器重置回折叠新增态，默认时刻仍是当前北京时间
            if (time.IsVisible || !string.Equals(time.Text, expectedDefaultTime, StringComparison.Ordinal))
            {
                failures.Add($"保存后编辑器未重置为折叠默认北京时间：visible={time.IsVisible}，text=「{time.Text}」。");
            }

            // 3. 编辑已有倒计时自动展开并精确回填原时刻，修改后再次保存
            var rows = Required<Panel>(main, "CountdownRows");
            var editButton = VisualDescendants<Button>(rows)
                .FirstOrDefault(button => Equals(button.Content, "编辑"));
            if (editButton is null)
            {
                failures.Add("保存后倒计时列表未找到编辑按钮。");
            }
            else
            {
                editButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                SettleLayout(main);
                if (!time.IsVisible || !string.Equals(time.Text, "18:45:30", StringComparison.Ordinal))
                {
                    failures.Add($"编辑已有倒计时未自动展开并回填 18:45:30（visible={time.IsVisible}，text=「{time.Text}」）。");
                }

                time.Text = "00:05:59";
                SettleLayout(main);
                ClickSave(main, save);

                var editedExpectedUtc = CountdownFormatting.ToUtc(targetDate, 0, 5, 59);
                var editedReloaded = DesktopWorkspace.Load(settingsPath).Snapshot.Settings.Countdowns
                    .SingleOrDefault(item => string.Equals(item.Name, "可键入时刻测试", StringComparison.Ordinal));
                if (editedReloaded is null || editedReloaded.TargetUtc != editedExpectedUtc)
                {
                    failures.Add(
                        $"编辑后保存 00:05:59 领域值不符：expected={editedExpectedUtc:O}，actual={editedReloaded?.TargetUtc:O}。");
                }
            }

            // 4. 光标感知的时/分/秒上下键步进与边界回绕，且步进后清晰保持选区在当前分段
            expand.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            Keyboard.Focus(time);
            SettleLayout(main);

            time.Text = "23:59:59";
            SettleLayout(main);

            // 小时段：23 -> 00 -> 23
            time.CaretIndex = 0;
            AssertStep(main, time, Key.Up, "00:59:59", 0, 2, "小时 23 上步进回绕到 00", failures);
            AssertStep(main, time, Key.Down, "23:59:59", 0, 2, "小时 00 下步进回绕到 23", failures);

            // 分钟段：59 -> 00 -> 59
            time.CaretIndex = 4;
            AssertStep(main, time, Key.Up, "23:00:59", 3, 2, "分钟 59 上步进回绕到 00", failures);
            AssertStep(main, time, Key.Down, "23:59:59", 3, 2, "分钟 00 下步进回绕到 59", failures);

            // 秒段：59 -> 00 -> 59
            time.CaretIndex = 8;
            AssertStep(main, time, Key.Up, "23:59:00", 6, 2, "秒 59 上步进回绕到 00", failures);
            AssertStep(main, time, Key.Down, "23:59:59", 6, 2, "秒 00 下步进回绕到 59", failures);

            // 常规中段步进
            time.Text = "14:30:45";
            SettleLayout(main);
            time.CaretIndex = 1;
            AssertStep(main, time, Key.Up, "15:30:45", 0, 2, "小时 14 上步进到 15", failures);
            time.CaretIndex = 3;
            AssertStep(main, time, Key.Down, "15:29:45", 3, 2, "分钟 30 下步进到 29", failures);
            time.CaretIndex = 7;
            AssertStep(main, time, Key.Up, "15:29:46", 6, 2, "秒 45 上步进到 46", failures);

            // 在截止时刻单行框按 Enter 直接提交保存
            name.Text = "时刻回车保存";
            date.SelectedDate = targetDate;
            SettleLayout(main);
            Keyboard.Focus(time);
            SettleLayout(main);
            var enterHandled = WindowValidationInput.RaisePreviewKey(time, Key.Enter);
            SettleLayout(main);
            var enterExpectedUtc = CountdownFormatting.ToUtc(targetDate, 15, 29, 46);
            var enterSaved = DesktopWorkspace.Load(settingsPath).Snapshot.Settings.Countdowns
                .SingleOrDefault(item => string.Equals(item.Name, "时刻回车保存", StringComparison.Ordinal));
            if (!enterHandled || enterSaved is null || enterSaved.TargetUtc != enterExpectedUtc)
            {
                failures.Add(
                    $"截止时刻框 Enter 提交未保存步进后时间 15:29:46（handled={enterHandled}，actual={enterSaved?.TargetUtc:O}）。");
            }

            // 5. 复用契约：验证 TimeInputBehavior 在 HH:mm 模式下同样支持格式化、严格校验与分段步进（供 #329 复用）
            CheckHourMinuteReuseContract(failures);
        }
        finally
        {
            CleanupMainWindow(main, settingsPath, diagnosticPath);
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunValidationAndAccessibility(string meaning)
    {
        var failures = new List<string>();
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-time-validation-{Guid.NewGuid():N}.settings.json");
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
            main = CreateOffscreenMainWindow(workspace, calendar, diagnosticPath);
            ShowAndSettle(main);

            var expand = Required<Button>(main, "CountdownEditorExpandButton");
            expand.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);

            if (main.FindName("CountdownTime") is not TextBox time ||
                main.FindName("CountdownTimeLabel") is not TextBlock timeLabel ||
                main.FindName("CountdownTimeError") is not TextBlock timeError)
            {
                failures.Add("主窗口缺少 CountdownTime / CountdownTimeLabel / CountdownTimeError 控件。");
                return (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
            }

            var name = Required<TextBox>(main, "CountdownName");
            var date = Required<DatePicker>(main, "CountdownDate");
            var tags = Required<FrameworkElement>(main, "CountdownTags");
            var save = Required<Button>(main, "SaveCountdownButton");
            var message = Required<TextBlock>(main, "CountdownMessage");
            var editorSurface = Required<FrameworkElement>(main, "CountdownEditorSurface");

            // 1. 可见标签与输入的 LabeledBy 关系及等价可访问名（不携带重复显式名）
            if (!timeLabel.IsVisible || !string.Equals(timeLabel.Text, "截止时刻", StringComparison.Ordinal))
            {
                failures.Add($"截止时刻可见标签文本不符：visible={timeLabel.IsVisible}，text=「{timeLabel.Text}」。");
            }

            if (!ReferenceEquals(AutomationProperties.GetLabeledBy(time), timeLabel) ||
                !string.IsNullOrEmpty(AutomationProperties.GetName(time)))
            {
                failures.Add("CountdownTime 未通过 LabeledBy 指向 CountdownTimeLabel 或携带了重复显式 AutomationProperties.Name。");
            }

            var timePeer = FrameworkElementAutomationPeer.CreatePeerForElement(time);
            var labelPeer = FrameworkElementAutomationPeer.CreatePeerForElement(timeLabel);
            if (timePeer is null || labelPeer is null ||
                !ReferenceEquals(timePeer.GetLabeledBy(), labelPeer) ||
                !string.Equals(timePeer.GetName(), "截止时刻", StringComparison.Ordinal))
            {
                failures.Add(
                    $"CountdownTime 自动化 peer 标签关系或可访问名不符：name=「{timePeer?.GetName()}」，labeledByMatch={ReferenceEquals(timePeer?.GetLabeledBy(), labelPeer)}。");
            }

            // 正常态实拍（展开态焦点暖金边）
            name.Text = "有效倒计时";
            date.SelectedDate = ChinaStandardTime.ToBeijingTime(FixtureUtc).Date.AddDays(2);
            time.Text = "18:30:00";
            Keyboard.Focus(time);
            SettleLayout(main);
            RequireBorder(main, time, "MainActionFocusBrush", 2, "有效有焦点", failures);
            var validFocusedGeometry = ReadGeometry(editorSurface, time, tags, save);
            capture?.Invoke(main, "countdown-time-day-focused");

            // 2. 失焦校验覆盖全部非法类别（空值、小时越界、分钟越界、秒越界、非时刻文本）
            foreach (var (invalidInput, label) in new[]
                     {
                         ("", "空值"),
                         ("25:00:00", "小时越界"),
                         ("12:60:00", "分钟越界"),
                         ("12:00:60", "秒越界"),
                         ("abc", "非时刻文本")
                     })
            {
                announcements.Clear();
                Keyboard.Focus(time);
                SettleLayout(main);
                time.Text = invalidInput;
                SettleLayout(main);
                Keyboard.Focus(save);
                SettleLayout(main);

                if (!timeError.IsVisible ||
                    !string.Equals(timeError.Text, ExpectedTimeError, StringComparison.Ordinal))
                {
                    failures.Add($"失焦校验（{label}：「{invalidInput}」）未显示预期错误（visible={timeError.IsVisible}，text=「{timeError.Text}」）。");
                }

                if (!InputErrorState.GetHasError(time))
                {
                    failures.Add($"失焦校验（{label}）未置位 InputErrorState.HasError。");
                }

                RequireBorder(main, time, "DangerBrush", 1, $"失焦出错无焦点（{label}）", failures);
                var errorUnfocusedGeometry = ReadGeometry(editorSurface, time, tags, save);
                if (!validFocusedGeometry.Equals(errorUnfocusedGeometry))
                {
                    failures.Add($"失焦出错（{label}）挤动控件几何：before={validFocusedGeometry}，after={errorUnfocusedGeometry}。");
                }

                // 错误文本必须紧贴在截止时刻字段正下方
                var timeRect = Bounds(time, editorSurface);
                var errorRect = Bounds(timeError, editorSurface);
                if (errorRect.Top < timeRect.Bottom - 0.5 || Math.Abs(errorRect.Left - timeRect.Left) > 2.0)
                {
                    failures.Add($"错误行（{label}）未贴在截止时刻字段下方：time={timeRect}，error={errorRect}。");
                }

                if (!string.Equals(AutomationProperties.GetHelpText(time), ExpectedTimeError, StringComparison.Ordinal) ||
                    !announcements.Any(notice =>
                        notice.Id == "CountdownTimeError" &&
                        notice.Text == ExpectedTimeError &&
                        notice.Live == AutomationLiveSetting.Polite))
                {
                    failures.Add($"失焦校验（{label}）未同步来源 HelpText 或礼貌播报 CountdownTimeError。");
                }

                if (string.Equals(invalidInput, "25:00:00", StringComparison.Ordinal))
                {
                    capture?.Invoke(main, "countdown-time-day-error-unfocused");
                }

                // 重新聚焦 -> 2 DIP 危险边且无位移
                Keyboard.Focus(time);
                SettleLayout(main);
                RequireBorder(main, time, "DangerBrush", 2, $"出错重聚焦（{label}）", failures);
                var errorFocusedGeometry = ReadGeometry(editorSurface, time, tags, save);
                if (!validFocusedGeometry.Equals(errorFocusedGeometry))
                {
                    failures.Add($"出错重聚焦（{label}）挤动控件几何：before={validFocusedGeometry}，after={errorFocusedGeometry}。");
                }

                if (string.Equals(invalidInput, "25:00:00", StringComparison.Ordinal))
                {
                    capture?.Invoke(main, "countdown-time-day-error-focused");
                }

                // 修正为合法时刻 -> 错误文案、HelpText 与错误边框立即清除
                time.Text = "18:30:00";
                SettleLayout(main);
                if (timeError.IsVisible ||
                    !string.IsNullOrEmpty(timeError.Text) ||
                    InputErrorState.GetHasError(time) ||
                    !string.IsNullOrEmpty(AutomationProperties.GetHelpText(time)))
                {
                    failures.Add($"修正非法输入（{label}）后未立即清除错误状态。");
                }

                RequireBorder(main, time, "MainActionFocusBrush", 2, $"修正后暖金焦点（{label}）", failures);
            }

            // 3. 提交失败：不落盘，错误贴字段下方并镜像唯一错误行，焦点落到 CountdownTime
            time.Text = "25:00:00";
            Keyboard.Focus(save);
            SettleLayout(main);
            ClickSave(main, save);

            if (workspace.Snapshot.Settings.Countdowns.Count != 0)
            {
                failures.Add("非法截止时刻提交不应保存倒计时记录。");
            }

            if (!time.IsKeyboardFocused || !timeError.IsVisible ||
                !string.Equals(message.Text, ExpectedTimeError, StringComparison.Ordinal))
            {
                failures.Add(
                    $"提交非法时刻后未聚焦 CountdownTime 或未显示/镜像错误：focused={time.IsKeyboardFocused}，errorVisible={timeError.IsVisible}，message=「{message.Text}」。");
            }

            RequireBorder(main, time, "DangerBrush", 2, "提交失败聚焦错误", failures);

            // 4. 收起编辑区后提交非法时刻：自动重新展开并把焦点落到 CountdownTime
            time.Text = "abc";
            expand.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            if (time.IsVisible)
            {
                failures.Add("测试前置失败：点击收起按钮后 CountdownTime 仍可见。");
            }

            ClickSave(main, save);
            if (!time.IsVisible || !time.IsKeyboardFocused || !timeError.IsVisible)
            {
                failures.Add(
                    $"收起态提交非法时刻未重新展开并聚焦 CountdownTime：visible={time.IsVisible}，focused={time.IsKeyboardFocused}，errorVisible={timeError.IsVisible}。");
            }

            // 5. 高对比度主题：有效暖金映射 Highlight，出错映射 WindowText，且可见错误行存在
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

                RequireBorder(main, time, "DangerBrush", 2, "高对比度出错有焦点", failures);
                if (!timeError.IsVisible)
                {
                    failures.Add("高对比度出错态未显示独立错误行。");
                }

                capture?.Invoke(main, "countdown-time-high-contrast-error");

                Keyboard.Focus(name);
                SettleLayout(main);
                RequireBorder(main, time, "DangerBrush", 1, "高对比度出错无焦点", failures);

                time.Text = "18:30:00";
                Keyboard.Focus(time);
                SettleLayout(main);
                RequireBorder(main, time, "MainActionFocusBrush", 2, "高对比度修正后有效焦点", failures);
            }
            SettleLayout(main);
        }
        finally
        {
            UiAccessibility.Announced -= Observe;
            CleanupMainWindow(main, settingsPath, diagnosticPath);
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    private static void CheckHourMinuteReuseContract(List<string> failures)
    {
        var box = new TextBox();
        TimeInputBehavior.SetFormat(box, TimeInputFormat.HourMinute);
        TimeInputBehavior.SetIsEnabled(box, true);
        TimeInputBehavior.SetValue(box, new TimeSpan(23, 59, 30));

        var host = new Window
        {
            Width = 220,
            Height = 80,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0,
            Content = box
        };

        try
        {
            host.Show();
            WindowValidationInput.Pump(host.Dispatcher);
            host.UpdateLayout();

            if (!string.Equals(box.Text, "23:59", StringComparison.Ordinal) ||
                TimeInputBehavior.GetValue(box) != new TimeSpan(23, 59, 0) ||
                TimeInputBehavior.GetHasInvalidText(box) ||
                Typography.GetNumeralAlignment(box) != FontNumeralAlignment.Tabular ||
                InputMethod.GetIsInputMethodEnabled(box))
            {
                failures.Add($"HH:mm 复用初始化异常：text=「{box.Text}」，value={TimeInputBehavior.GetValue(box)}。");
            }

            // HH:mm 严格拒绝 HH:mm:ss 与越界输入
            foreach (var invalid in new[] { "23:59:00", "24:00", "09:60", "abc" })
            {
                box.Text = invalid;
                if (!TimeInputBehavior.GetHasInvalidText(box) || TimeInputBehavior.GetValue(box) is not null)
                {
                    failures.Add($"HH:mm 模式未拒绝非法输入「{invalid}」。");
                }
            }

            // HH:mm 分段步进与回绕
            box.Text = "23:59";
            box.CaretIndex = 0;
            var hourUp = WindowValidationInput.RaisePreviewKey(box, Key.Up);
            if (!hourUp || box.Text != "00:59" || box.SelectionStart != 0 || box.SelectionLength != 2)
            {
                failures.Add($"HH:mm 小时上步进失败：handled={hourUp}，text=「{box.Text}」，sel=({box.SelectionStart},{box.SelectionLength})。");
            }

            box.CaretIndex = 4;
            var minUp = WindowValidationInput.RaisePreviewKey(box, Key.Up);
            if (!minUp || box.Text != "00:00" || box.SelectionStart != 3 || box.SelectionLength != 2)
            {
                failures.Add($"HH:mm 分钟上步进失败：handled={minUp}，text=「{box.Text}」，sel=({box.SelectionStart},{box.SelectionLength})。");
            }

            var minDown = WindowValidationInput.RaisePreviewKey(box, Key.Down);
            if (!minDown || box.Text != "00:59" || box.SelectionStart != 3 || box.SelectionLength != 2)
            {
                failures.Add($"HH:mm 分钟下步进失败：handled={minDown}，text=「{box.Text}」，sel=({box.SelectionStart},{box.SelectionLength})。");
            }

            // 卸载行为恢复属性
            TimeInputBehavior.SetIsEnabled(box, false);
            if (box.ReadLocalValue(InputMethod.IsInputMethodEnabledProperty) != DependencyProperty.UnsetValue)
            {
                failures.Add("HH:mm 卸载行为后未恢复 IsInputMethodEnabled 本地值。");
            }
        }
        finally
        {
            host.Close();
            WindowValidationInput.Pump(host.Dispatcher);
        }
    }

    private static void AssertStep(
        MainWindow main,
        TextBox time,
        Key key,
        string expectedText,
        int expectedSelectionStart,
        int expectedSelectionLength,
        string label,
        List<string> failures)
    {
        var handled = WindowValidationInput.RaisePreviewKey(time, key);
        SettleLayout(main);
        if (!handled ||
            !string.Equals(time.Text, expectedText, StringComparison.Ordinal) ||
            time.SelectionStart != expectedSelectionStart ||
            time.SelectionLength != expectedSelectionLength)
        {
            failures.Add(
                $"{label}失败：handled={handled}，text=「{time.Text}」（期望「{expectedText}」），selection=({time.SelectionStart},{time.SelectionLength})（期望({expectedSelectionStart},{expectedSelectionLength})）。");
        }
    }

    private static MainWindow CreateOffscreenMainWindow(
        DesktopWorkspace workspace,
        HolidayCalendar calendar,
        string diagnosticPath) =>
        new(workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };

    private static void ShowAndSettle(MainWindow main)
    {
        var contentRendered = false;
        main.ContentRendered += (_, _) => contentRendered = true;
        main.Show();
        main.Activate();
        var firstFrame = WindowValidationInput.PumpRenderedFrames(
            main.Dispatcher,
            2000,
            () => contentRendered);
        SettleLayout(main);
        if (!contentRendered || !firstFrame.Completed)
        {
            throw new InvalidOperationException("管理窗口没有完成首帧。");
        }
    }

    private static void CleanupMainWindow(MainWindow? main, string settingsPath, string diagnosticPath)
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

    private static void ClickSave(MainWindow main, Button save)
    {
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
    }

    private static void RequireBorder(
        MainWindow main,
        TextBox input,
        string expectedBrushKey,
        double expectedDip,
        string state,
        List<string> failures)
    {
        input.ApplyTemplate();
        if (input.Template?.FindName("Surface", input) is not Border surface)
        {
            failures.Add($"{input.Name} 缺少模板部件 Surface。");
            return;
        }

        var expectedBrush = (Brush)main.FindResource(expectedBrushKey);
        var colorMatches = ReferenceEquals(surface.BorderBrush, expectedBrush) ||
            (surface.BorderBrush is SolidColorBrush a && expectedBrush is SolidColorBrush b && a.Color == b.Color);
        if (!colorMatches)
        {
            failures.Add($"{input.Name} {state} 边框颜色不符：expected={expectedBrushKey}({expectedBrush})，actual={surface.BorderBrush}。");
        }

        if (Math.Abs(surface.BorderThickness.Left - expectedDip) > 0.01 ||
            Math.Abs(surface.BorderThickness.Top - expectedDip) > 0.01)
        {
            failures.Add($"{input.Name} {state} 边框厚度不符：expected={expectedDip}，actual={surface.BorderThickness}。");
        }
    }

    private static EditorFieldGeometry ReadGeometry(
        Visual root,
        TextBox time,
        FrameworkElement tags,
        Button save)
    {
        time.ApplyTemplate();
        var contentHost = time.Template?.FindName("PART_ContentHost", time) as FrameworkElement
            ?? throw new InvalidOperationException("CountdownTime 缺少 PART_ContentHost。");
        return new EditorFieldGeometry(
            Bounds(time, root),
            Bounds(contentHost, root),
            Bounds(tags, root),
            Bounds(save, root));
    }

    private static Action<MainWindow, string>? CreateEvidenceWriter()
    {
        var explicitDirectory = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_ISSUE328_EVIDENCE_DIR");
        string directory;
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            directory = Path.GetFullPath(explicitDirectory);
        }
        else
        {
            var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            {
                return null;
            }

            directory = Path.Combine(
                Path.GetFullPath(sourceRoot),
                "logs",
                "ui-captures",
                $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-countdown-time-input");
        }

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
                    FileMode.Create,
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

    private static T Required<T>(MainWindow main, string name) where T : class =>
        main.FindName(name) as T
        ?? throw new InvalidOperationException($"管理窗口缺少控件 {name}（{typeof(T).Name}）。");

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in VisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private readonly record struct EditorFieldGeometry(Rect Time, Rect TimeContent, Rect Tags, Rect Save);
}
