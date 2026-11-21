using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #329：将 #328 可复用的 TimeInputBehavior（HH:mm）铺到规则编辑器规则时间、提醒中心自定义稍后时间
// 与管理窗口显示设置上下班时间三处。全部通过真实窗口、公共视觉树、键盘输入与自动化 peer 验收。
internal static class TimeInputSpreadScenario
{
    // 2038-07-08 01:00:00 UTC == 2038-07-08 09:00:00 +08:00（北京时间）
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    private const string IncompletePreviewText = "请完善上方设置以生成预览";
    private const string RuleFirstDueIncompleteError = "请选择完整的首次提醒日期和时间。";
    private const string RuleCalendarIncompleteError = "请选择完整的日历起始日期和本地时间。";
    private const string RulePastFirstDueError = "首次提醒时间必须晚于当前北京时间。";
    private const string CustomSnoozeIncompleteError = "请选择完整的自定义稍后日期和时间。";
    private const string CustomSnoozeNotInFutureError = "自定义稍后时间必须晚于当前北京时间。";
    private const string WorkStartFormatError = "上班时间请使用 HH:mm。";
    private const string WorkEndFormatError = "下班时间请使用 HH:mm。";
    private const string WorkTimeOrderError = "下班时间必须晚于上班时间。";

    private static string? _evidenceDirectory;
    private static bool _evidenceDirectoryInitialized;

    internal static (bool Passed, string Explanation) RunRuleEditorTimeInput(string meaning)
    {
        var failures = new List<string>();
        var capture = GetEvidenceWriter();
        var clock = new FixedClock(FixtureUtc);
        var editor = CreateOffscreenRuleEditor(Array.Empty<ReminderRule>(), clock, FixtureUtc.AddDays(30));

        try
        {
            ShowAndSettle(editor);
            ActivateForKeyboard(editor);

            // 1. 旧时/分下拉框必须完全移除，单一可键入 HH:mm 文本框存在
            if (FindAllByAutomationId<FrameworkElement>(editor, "ReminderRuleFirstDueHour").Length > 0 ||
                FindAllByAutomationId<FrameworkElement>(editor, "ReminderRuleFirstDueMinute").Length > 0)
            {
                failures.Add("规则编辑器仍残留旧小时/分钟下拉框（ReminderRuleFirstDueHour/Minute）。");
            }

            var timeMatches = FindAllByAutomationId<TextBox>(editor, "ReminderRuleFirstDueTime");
            if (timeMatches.Length != 1)
            {
                failures.Add($"规则编辑器缺少单一可键入时间框 ReminderRuleFirstDueTime（count={timeMatches.Length}）。");
                return (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
            }

            var time = timeMatches[0];
            var mode = FindSingleByAutomationId<ComboBox>(editor, "ReminderRuleMode");
            var date = FindSingleByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate");
            var error = FindSingleByAutomationId<TextBlock>(editor, "ReminderRuleFirstDueError");
            var preview = FindSingleByAutomationId<TextBlock>(editor, "ReminderRulePreview");
            var saveRule = FindSingleByAutomationId<Button>(editor, "SaveReminderRuleButton");

            // 2. 默认值与输入行为契约（表格数字、禁 IME）
            var expectedDefaultBeijing = new ReminderEditing()
                .GetDefaultFirstDueBeijing(
                    ReminderEditingMode.Once,
                    ReminderIntervalSelection.ThirtyMinutes,
                    string.Empty,
                    FixtureUtc)
                .FirstDueBeijing!.Value;
            var expectedDefaultText = expectedDefaultBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (!string.Equals(time.Text, expectedDefaultText, StringComparison.Ordinal))
            {
                failures.Add($"规则编辑器默认时刻不符：expected=「{expectedDefaultText}」，actual=「{time.Text}」。");
            }

            if (Typography.GetNumeralAlignment(time) != FontNumeralAlignment.Tabular ||
                InputMethod.GetIsInputMethodEnabled(time))
            {
                failures.Add($"规则时间框未启用表格数字或未禁用 IME（numeral={Typography.GetNumeralAlignment(time)}，ime={InputMethod.GetIsInputMethodEnabled(time)}）。");
            }

            // 3. 首次提醒模式与日历模式下的 LabeledBy 与自动化名
            AssertRuleTimeLabelAndName(editor, time, "首次提醒", "首次提醒时刻", "提醒一次", failures);
            mode.SelectedIndex = 3; // 按日历重复
            SettleWindow(editor);
            AssertRuleTimeLabelAndName(editor, time, "开始日期", "本地时刻", "按日历重复", failures);

            // 日历模式直接键入 08:15，实时预览立即同步
            date.SelectedDate = new DateTime(2038, 7, 9);
            time.Text = "08:15";
            SettleWindow(editor);
            if (!string.Equals(preview.Text, "按日历重复 · 每 1 日 · 08:15", StringComparison.Ordinal))
            {
                failures.Add($"日历模式键入 08:15 后实时预览不符：actual=「{preview.Text}」。");
            }

            // 日历模式非法输入失焦校验（保留日历错误文案，且只标红时间框不标红日期框）
            Keyboard.Focus(time);
            SettleWindow(editor);
            time.Text = "25:00";
            SettleWindow(editor);
            if (!string.Equals(preview.Text, IncompletePreviewText, StringComparison.Ordinal))
            {
                failures.Add($"日历模式非法时间未使预览回退为不完整提示：actual=「{preview.Text}」。");
            }

            Keyboard.Focus(saveRule);
            SettleWindow(editor);
            if (!error.IsVisible ||
                !string.Equals(error.Text, RuleCalendarIncompleteError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(time) ||
                InputErrorState.GetHasError(date))
            {
                failures.Add(
                    $"日历模式非法时间失焦校验异常：visible={error.IsVisible}，text=「{error.Text}」，timeErr={InputErrorState.GetHasError(time)}，dateErr={InputErrorState.GetHasError(date)}。");
            }

            // 修正为合法时刻立即清错
            time.Text = "08:15";
            SettleWindow(editor);
            if (error.IsVisible || InputErrorState.GetHasError(time))
            {
                failures.Add("日历模式修正合法时间后未立即清除错误与边框。");
            }

            // 切回提醒一次模式验收步进、失焦、提交、边框、几何、高对比度与实拍
            mode.SelectedIndex = 0;
            date.SelectedDate = new DateTime(2038, 7, 9);
            time.Text = "16:45";
            Keyboard.Focus(time);
            SettleWindow(editor);
            RequireTextBoxBorder(editor, time, "MainActionFocusBrush", 2, "规则时间有效有焦点", failures);
            var restingGeometry = ReadTextBoxGeometry((Visual)editor.Content, time, date);
            if (!string.Equals(preview.Text, "提醒一次 · 2038 年 7 月 9 日 16:45", StringComparison.Ordinal))
            {
                failures.Add($"提醒一次模式键入 16:45 后实时预览不符：actual=「{preview.Text}」。");
            }

            capture?.Invoke(editor, (FrameworkElement)editor.Content, "rule-editor-time-focused");

            // 4. 上下键分段步进与回绕 + 预览联动
            time.Text = "23:59";
            SettleWindow(editor);
            time.CaretIndex = 0;
            AssertStep(editor, time, Key.Up, "00:59", 0, 2, "规则时间小时 23 上步进回绕", failures);
            if (!string.Equals(preview.Text, "提醒一次 · 2038 年 7 月 9 日 00:59", StringComparison.Ordinal))
            {
                failures.Add($"规则时间小时步进后预览未联动：actual=「{preview.Text}」。");
            }

            AssertStep(editor, time, Key.Down, "23:59", 0, 2, "规则时间小时 00 下步进回绕", failures);
            time.CaretIndex = 4;
            AssertStep(editor, time, Key.Up, "23:00", 3, 2, "规则时间分钟 59 上步进回绕", failures);
            AssertStep(editor, time, Key.Down, "23:59", 3, 2, "规则时间分钟 00 下步进回绕", failures);

            // 5. 失焦与提交校验（空值、越界、非时刻文本）+ 错误位置紧贴下方 + 1/2 DIP 边框 + 零位移
            foreach (var (invalidInput, label) in new[]
                     {
                         ("", "空值"),
                         ("25:00", "小时越界"),
                         ("12:60", "分钟越界"),
                         ("abc", "非时刻文本")
                     })
            {
                Keyboard.Focus(time);
                SettleWindow(editor);
                time.Text = invalidInput;
                SettleWindow(editor);
                Keyboard.Focus(saveRule);
                SettleWindow(editor);

                if (!error.IsVisible ||
                    !string.Equals(error.Text, RuleFirstDueIncompleteError, StringComparison.Ordinal) ||
                    !InputErrorState.GetHasError(time) ||
                    !string.Equals(AutomationProperties.GetHelpText(time), RuleFirstDueIncompleteError, StringComparison.Ordinal))
                {
                    failures.Add($"规则时间失焦校验（{label}）失败：visible={error.IsVisible}，text=「{error.Text}」，hasError={InputErrorState.GetHasError(time)}。");
                }

                RequireTextBoxBorder(editor, time, "DangerBrush", 1, $"规则时间出错无焦点（{label}）", failures);
                var errorUnfocusedGeo = ReadTextBoxGeometry((Visual)editor.Content, time, date);
                if (!restingGeometry.Equals(errorUnfocusedGeo))
                {
                    failures.Add($"规则时间出错无焦点（{label}）挤动几何：before={restingGeometry}，after={errorUnfocusedGeo}。");
                }

                var timeRect = Bounds(time, (Visual)editor.Content);
                var errorRect = Bounds(error, (Visual)editor.Content);
                if (errorRect.Top < timeRect.Bottom - 0.5)
                {
                    failures.Add($"规则时间错误行（{label}）未贴在时间字段下方：time={timeRect}，error={errorRect}。");
                }

                Keyboard.Focus(time);
                SettleWindow(editor);
                RequireTextBoxBorder(editor, time, "DangerBrush", 2, $"规则时间出错有焦点（{label}）", failures);
                var errorFocusedGeo = ReadTextBoxGeometry((Visual)editor.Content, time, date);
                if (!restingGeometry.Equals(errorFocusedGeo))
                {
                    failures.Add($"规则时间出错有焦点（{label}）挤动几何：before={restingGeometry}，after={errorFocusedGeo}。");
                }

                if (string.Equals(invalidInput, "25:00", StringComparison.Ordinal))
                {
                    capture?.Invoke(editor, (FrameworkElement)editor.Content, "rule-editor-time-error");
                }

                time.Text = "16:45";
                SettleWindow(editor);
                if (error.IsVisible || InputErrorState.GetHasError(time))
                {
                    failures.Add($"规则时间修正（{label}）后未清除错误。");
                }

                RequireTextBoxBorder(editor, time, "MainActionFocusBrush", 2, $"规则时间修正后暖金焦点（{label}）", failures);
            }

            // 提交非法时间：拦截保存并聚焦时间框
            time.Text = "25:00";
            Keyboard.Focus(saveRule);
            SettleWindow(editor);
            InvokeButton(editor, saveRule);
            if (!time.IsKeyboardFocused || !error.IsVisible || !InputErrorState.GetHasError(time))
            {
                failures.Add($"规则时间非法提交未聚焦时间框或未报错：focused={time.IsKeyboardFocused}，visible={error.IsVisible}。");
            }

            // 提交过去时间：保留精确过去时间错误文案并标红边框
            var pastBeijing = ChinaStandardTime.ToBeijingTime(FixtureUtc.AddMinutes(-15));
            date.SelectedDate = pastBeijing.Date;
            time.Text = pastBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            SettleWindow(editor);
            InvokeButton(editor, saveRule);
            if (!error.IsVisible ||
                !string.Equals(error.Text, RulePastFirstDueError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(time))
            {
                failures.Add($"规则过去时间提交错误不符：visible={error.IsVisible}，text=「{error.Text}」，hasError={InputErrorState.GetHasError(time)}。");
            }

            // 6. 高对比度主题
            using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                contrast.ChangeColors(new ApplicationTheme.SystemContrastPalette(
                    Colors.Black, Colors.White, Colors.Black, Colors.White, Colors.Yellow, Colors.Black, Colors.Gray));
                Keyboard.Focus(time);
                SettleWindow(editor);
                RequireTextBoxBorder(editor, time, "DangerBrush", 2, "规则时间高对比度出错有焦点", failures);
                time.Text = "16:45";
                SettleWindow(editor);
                RequireTextBoxBorder(editor, time, "MainActionFocusBrush", 2, "规则时间高对比度有效有焦点", failures);
            }
            SettleWindow(editor);
        }
        finally
        {
            CloseWindow(editor);
        }

        // 7. 模态提交：直接键入一次性规则 + 日历规则，检查精确持久化领域值与编辑回填
        var guidSource = new ValidationGuidSource(
            Guid.Parse("32900001-0000-0000-0000-000000000001"),
            Guid.Parse("32900002-0000-0000-0000-000000000002"));
        var modalResult = RunRuleEditorModal(
            Array.Empty<ReminderRule>(),
            clock,
            guidSource,
            FixtureUtc.AddDays(30),
            modalEditor =>
            {
                var mode = FindSingleByAutomationId<ComboBox>(modalEditor, "ReminderRuleMode");
                var date = FindSingleByAutomationId<DatePicker>(modalEditor, "ReminderRuleFirstDueDate");
                var time = FindSingleByAutomationId<TextBox>(modalEditor, "ReminderRuleFirstDueTime");
                var saveRule = FindSingleByAutomationId<Button>(modalEditor, "SaveReminderRuleButton");
                var accept = FindSingleByAutomationId<Button>(modalEditor, "AcceptReminderRulesButton");

                // 规则 1：一次性规则 2038-07-09 16:45
                mode.SelectedIndex = 0;
                date.SelectedDate = new DateTime(2038, 7, 9);
                time.Text = "16:45";
                SettleWindow(modalEditor);
                InvokeButton(modalEditor, saveRule);

                // 规则 2：按日历重复 每天 08:15，经脏态「完成」自动提交
                mode.SelectedIndex = 3;
                date.SelectedDate = new DateTime(2038, 7, 10);
                time.Text = "08:15";
                SettleWindow(modalEditor);
                InvokeButton(modalEditor, accept);
            });

        var expectedOnceUtc = CountdownFormatting.ToUtc(new DateTime(2038, 7, 9), 16, 45, 0);
        if (modalResult.DialogResult != true ||
            modalResult.Rules.Count != 2 ||
            modalResult.Rules[0] is not AbsoluteOnceReminderRule onceRule ||
            onceRule.ScheduledUtc != expectedOnceUtc ||
            modalResult.Rules[1] is not CalendarReminderRule calendarRule ||
            calendarRule.LocalTime != new TimeOnly(8, 15))
        {
            failures.Add($"规则编辑器键入保存领域值不符：dialog={modalResult.DialogResult}，count={modalResult.Rules.Count}。");
        }
        else
        {
            // 重新打开编辑已有日历规则，确认原样回填 08:15
            var editEditor = CreateOffscreenRuleEditor(modalResult.Rules, clock, FixtureUtc.AddDays(30));
            try
            {
                ShowAndSettle(editEditor);
                var editCalendarButton = VisualDescendants<Button>(editEditor).Single(button =>
                    string.Equals(button.Content as string, "编辑", StringComparison.Ordinal) &&
                    string.Equals(button.Tag as string, calendarRule.Id, StringComparison.Ordinal));
                InvokeButton(editEditor, editCalendarButton);
                var editTime = FindSingleByAutomationId<TextBox>(editEditor, "ReminderRuleFirstDueTime");
                if (!string.Equals(editTime.Text, "08:15", StringComparison.Ordinal))
                {
                    failures.Add($"编辑已有日历规则未回填 08:15：actual=「{editTime.Text}」。");
                }
            }
            finally
            {
                CloseWindow(editEditor);
            }
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunReminderCenterCustomSnoozeTimeInput(string meaning)
    {
        var failures = new List<string>();
        var capture = GetEvidenceWriter();
        var dueUtc = FixtureUtc;
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-329-snooze-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;

        try
        {
            var clock = new MutableClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            if (!workspace.Apply(new SaveNoteChange(
                null,
                "核对发版前接口回归结果",
                new DateTime(2038, 7, 8),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateRepeating(dueUtc, 15))).Succeeded)
            {
                failures.Add("无法创建自定义稍后测试便签。");
                return (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
            }

            var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow
                ?? throw new InvalidOperationException("提醒中心未打开。");
            PrepareOffscreen(center);
            SettleWindow(center);

            var row = VisualDescendants<Border>(center)
                .Single(candidate => candidate.Tag is ActiveReminderOccurrenceKey candidateKey && candidateKey == key);

            // 1. 懒创建契约保持不变：选「自定义」前不存在任何日期、下拉或自定义时间框
            if (VisualDescendants<DatePicker>(row).Any() ||
                VisualDescendants<ComboBox>(row).Any() ||
                FindAllByAutomationId<TextBox>(row, "ReminderEntryCustomSnoozeTime").Length > 0)
            {
                failures.Add("提醒中心条目在未选择「自定义」前预创建了自定义稍后控件。");
            }

            var snoozeButton = VisualDescendants<Button>(row).Single(button =>
                (AutomationProperties.GetName(button) ?? string.Empty).StartsWith("稍后提醒：", StringComparison.Ordinal));
            var snoozeMenu = snoozeButton.ContextMenu
                ?? throw new InvalidOperationException("稍后提醒按钮缺少 ContextMenu。");
            SelectMenuItemByHeader(center, snoozeButton, snoozeMenu, "自定义");
            SettleWindow(center);

            if (VisualDescendants<ComboBox>(row).Any())
            {
                failures.Add("选择「自定义」后仍残留旧小时/分钟 ComboBox。");
            }

            var customTimeMatches = FindAllByAutomationId<TextBox>(row, "ReminderEntryCustomSnoozeTime");
            if (customTimeMatches.Length != 1)
            {
                failures.Add($"选择「自定义」后未懒创建单一可键入时间框 ReminderEntryCustomSnoozeTime（count={customTimeMatches.Length}）。");
                return (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
            }

            var customTime = customTimeMatches[0];
            var customDate = VisualDescendants<DatePicker>(row).Single();
            var customConfirm = FindSingleByAutomationId<Button>(row, "ReminderEntryCustomSnoozeConfirm");
            var customError = FindSingleByAutomationId<TextBlock>(row, "ReminderEntryCustomError");

            // 再次选择「自定义」复用同一批控件实例
            SelectMenuItemByHeader(center, snoozeButton, snoozeMenu, "自定义");
            SettleWindow(center);
            if (!ReferenceEquals(customTime, FindSingleByAutomationId<TextBox>(row, "ReminderEntryCustomSnoozeTime")))
            {
                failures.Add("再次选择「自定义」未复用同一个自定义时间输入框实例。");
            }

            // 2. 默认值、行为属性与 LabeledBy 关系
            if (customDate.SelectedDate is not null || !string.IsNullOrEmpty(customTime.Text))
            {
                failures.Add($"自定义稍后默认值变更：date={customDate.SelectedDate}，time=「{customTime.Text}」。");
            }

            if (Typography.GetNumeralAlignment(customTime) != FontNumeralAlignment.Tabular ||
                InputMethod.GetIsInputMethodEnabled(customTime))
            {
                failures.Add("自定义稍后时间框未启用表格数字或未禁用 IME。");
            }

            var labeledBy = AutomationProperties.GetLabeledBy(customTime) as TextBlock;
            var timePeer = FrameworkElementAutomationPeer.CreatePeerForElement(customTime);
            var labelPeer = labeledBy is null ? null : FrameworkElementAutomationPeer.CreatePeerForElement(labeledBy);
            if (labeledBy is null ||
                !labeledBy.IsVisible ||
                string.IsNullOrWhiteSpace(labeledBy.Text) ||
                timePeer is null ||
                labelPeer is null ||
                !ReferenceEquals(timePeer.GetLabeledBy(), labelPeer) ||
                !(AutomationProperties.GetName(customTime) ?? string.Empty).StartsWith("自定义稍后时间：", StringComparison.Ordinal))
            {
                failures.Add(
                    $"自定义稍后时间 LabeledBy 或可访问名异常：label=「{labeledBy?.Text ?? "<null>"}」，name=「{AutomationProperties.GetName(customTime)}」。");
            }

            // 460 与 400 DIP 下均不裁切、不重叠
            VerifyReminderCustomRowGeometry(row, customDate, labeledBy, customTime, customConfirm, "460", failures);
            center.Width = 400;
            SettleWindow(center);
            VerifyReminderCustomRowGeometry(row, customDate, labeledBy, customTime, customConfirm, "400", failures);
            center.Width = 460;
            SettleWindow(center);

            // 3. 聚焦暖金边 + 实拍 + 上下键步进与回绕
            ActivateForKeyboard(center);
            var futureBeijing = ChinaStandardTime.ToBeijingTime(dueUtc.AddMinutes(45));
            customDate.SelectedDate = futureBeijing.Date;
            customTime.Text = futureBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            Keyboard.Focus(customTime);
            SettleWindow(center);
            RequireTextBoxBorder(center, customTime, "MainActionFocusBrush", 2, "自定义稍后时间有效有焦点", failures);
            var restingGeometry = ReadTextBoxGeometry(row, customTime, customConfirm);
            capture?.Invoke(center, (FrameworkElement)center.Content, "reminder-center-custom-snooze-focused");

            customTime.Text = "23:59";
            SettleWindow(center);
            customTime.CaretIndex = 0;
            AssertStep(center, customTime, Key.Up, "00:59", 0, 2, "自定义稍后小时 23 上步进回绕", failures);
            AssertStep(center, customTime, Key.Down, "23:59", 0, 2, "自定义稍后小时 00 下步进回绕", failures);
            customTime.CaretIndex = 4;
            AssertStep(center, customTime, Key.Up, "23:00", 3, 2, "自定义稍后分钟 59 上步进回绕", failures);
            AssertStep(center, customTime, Key.Down, "23:59", 3, 2, "自定义稍后分钟 00 下步进回绕", failures);

            // 4. 非法格式失焦与提交校验 + 错误位置紧贴下方 + #303 错误边框
            var beforeCanonical = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            Keyboard.Focus(customTime);
            SettleWindow(center);
            customTime.Text = "25:00";
            SettleWindow(center);
            Keyboard.Focus(customConfirm);
            SettleWindow(center);
            if (!customError.IsVisible ||
                !string.Equals(customError.Text, CustomSnoozeIncompleteError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(customTime) ||
                InputErrorState.GetHasError(customDate))
            {
                failures.Add(
                    $"自定义稍后非法格式失焦校验异常：visible={customError.IsVisible}，text=「{customError.Text}」，timeErr={InputErrorState.GetHasError(customTime)}，dateErr={InputErrorState.GetHasError(customDate)}。");
            }

            RequireTextBoxBorder(center, customTime, "DangerBrush", 1, "自定义稍后非法无焦点", failures);
            var errorGeo = ReadTextBoxGeometry(row, customTime, customConfirm);
            if (!restingGeometry.Equals(errorGeo))
            {
                failures.Add($"自定义稍后出错边框挤动几何：before={restingGeometry}，after={errorGeo}。");
            }

            VerifyErrorBelowCustomSnooze(row, customTime, customConfirm, customError, "非法格式", failures);

            InvokeButton(center, customConfirm);
            if (coordinator.ActiveEntryCount != 1 ||
                SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) != beforeCanonical)
            {
                failures.Add("非法格式提交不应持久化或关闭提醒条目。");
            }

            // 5. 严格晚于当前北京时间校验（等于当前北京时间 & 早于当前北京时间）
            foreach (var (candidateUtc, label) in new[]
                     {
                         (dueUtc, "等于当前北京时间"),
                         (dueUtc.AddMinutes(-10), "早于当前北京时间")
                     })
            {
                var candidateBeijing = ChinaStandardTime.ToBeijingTime(candidateUtc);
                customDate.SelectedDate = candidateBeijing.Date;
                customTime.Text = candidateBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
                SettleWindow(center);
                InvokeButton(center, customConfirm);

                if (!customError.IsVisible ||
                    !string.Equals(customError.Text, CustomSnoozeNotInFutureError, StringComparison.Ordinal) ||
                    !InputErrorState.GetHasError(customTime) ||
                    !InputErrorState.GetHasError(customDate) ||
                    coordinator.ActiveEntryCount != 1 ||
                    !center.IsSnoozeEnabled(key) ||
                    SettingsContract.SerializeCanonical(workspace.Snapshot.Settings) != beforeCanonical)
                {
                    failures.Add(
                        $"自定义稍后（{label}）未保持严格晚于当前北京时间规则：visible={customError.IsVisible}，text=「{customError.Text}」，timeErr={InputErrorState.GetHasError(customTime)}，dateErr={InputErrorState.GetHasError(customDate)}。");
                }

                VerifyErrorBelowCustomSnooze(row, customTime, customConfirm, customError, label, failures);
            }

            Keyboard.Focus(customTime);
            SettleWindow(center);
            RequireTextBoxBorder(center, customTime, "DangerBrush", 2, "自定义稍后过去时间有焦点", failures);
            capture?.Invoke(center, (FrameworkElement)center.Content, "reminder-center-custom-snooze-error");

            // 6. 高对比度主题
            using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                contrast.ChangeColors(new ApplicationTheme.SystemContrastPalette(
                    Colors.Black, Colors.White, Colors.Black, Colors.White, Colors.Yellow, Colors.Black, Colors.Gray));
                SettleWindow(center);
                RequireTextBoxBorder(center, customTime, "DangerBrush", 2, "自定义稍后高对比度出错有焦点", failures);
                customTime.Text = futureBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
                SettleWindow(center);
                RequireTextBoxBorder(center, customTime, "MainActionFocusBrush", 2, "自定义稍后高对比度修正后有效焦点", failures);
            }
            SettleWindow(center);

            // 7. 合法未来时间键入后提交并精确持久化，关闭空中心
            customDate.SelectedDate = futureBeijing.Date;
            customTime.Text = futureBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            SettleWindow(center);
            if (customError.IsVisible || InputErrorState.GetHasError(customTime))
            {
                failures.Add("修正自定义稍后时间为合法未来时间后未清除错误与错误边框。");
            }

            InvokeButton(center, customConfirm);
            var state = workspace.Snapshot.Settings.ReminderStates[rule.Id];
            var persisted = new SettingsStore(clock: clock).Load(settingsPath).Settings.ReminderStates[rule.Id];
            var expectedUntilUtc = CountdownFormatting.ToUtc(futureBeijing.Date, futureBeijing.Hour, futureBeijing.Minute, 0);
            if (coordinator.ActiveEntryCount != 0 ||
                coordinator.ActiveWindowCount != 0 ||
                state.ActiveSnooze?.SnoozeUntilUtc != expectedUntilUtc ||
                persisted != state)
            {
                failures.Add(
                    $"合法自定义稍后持久化不符：entries={coordinator.ActiveEntryCount}，actual={state.ActiveSnooze?.SnoozeUntilUtc:O}，expected={expectedUntilUtc:O}。");
            }
        }
        finally
        {
            coordinator?.Dispose();
            DeleteSettingsFiles(settingsPath);
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunWorkHoursTimeInput(string meaning)
    {
        var failures = new List<string>();
        var capture = GetEvidenceWriter();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-329-workhours-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var diagnosticPath = Path.Combine(tempRoot, "diagnostic.jsonl");
        var announcements = new List<(string Id, string Text, AutomationLiveSetting Live)>();
        MainWindow? main = null;

        void Observe(TextBlock block) => announcements.Add((
            AutomationProperties.GetAutomationId(block) ?? string.Empty,
            AutomationProperties.GetName(block) ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));

        UiAccessibility.Announced += Observe;
        try
        {
            // 2030-07-15 为周一（工作日）；同时构造确定性日历供工作日与今日已赚计算验收
            var workdayMorningUtc = new DateTimeOffset(2030, 7, 15, 13, 0, 0, TimeSpan.FromHours(8)).ToUniversalTime();
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(workdayMorningUtc)));
            var year2030Json = BuildWeekdayCalendarJson(2030);
            using var calendar = new HolidayCalendar(
                tempRoot,
                (year, _) => year == 2030
                    ? Task.FromResult(year2030Json)
                    : Task.FromException<string>(new HttpRequestException($"no fixture for {year}")));

            main = new MainWindow(workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            ShowMainWindow(main);
            WaitForTask(calendar.RefreshAsync(workdayMorningUtc, CancellationToken.None), main);

            var preferencesButton = Required<Button>(main, "PreferencesButton");
            var preferencesPopup = Required<Popup>(main, "PreferencesPopup");
            var preferencesSurface = Required<Border>(main, "CountdownPreferencesSurface");
            var salary = Required<TextBox>(main, "MonthlySalary");
            var workStart = Required<TextBox>(main, "WorkStart");
            var workEnd = Required<TextBox>(main, "WorkEnd");
            var workStartLabel = Required<TextBlock>(main, "WorkStartLabel");
            var workEndLabel = Required<TextBlock>(main, "WorkEndLabel");
            var workStartError = Required<TextBlock>(main, "WorkStartError");
            var workEndError = Required<TextBlock>(main, "WorkEndError");
            var savePreferences = Required<Button>(main, "SavePreferencesButton");
            var statusText = Required<TextBlock>(main, "StatusText");

            preferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.PumpRenderedFrames(
                main.Dispatcher,
                600,
                () => preferencesPopup.IsOpen && preferencesSurface.Opacity >= 0.999);
            SettleWindow(main);
            if (!preferencesPopup.IsOpen)
            {
                failures.Add("点击齿轮未能打开显示设置弹层。");
                return (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
            }

            // 1. 默认值、TimeInputBehavior 行为属性与 LabeledBy 关系
            if (!string.Equals(workStart.Text, "09:00", StringComparison.Ordinal) ||
                !string.Equals(workEnd.Text, "18:00", StringComparison.Ordinal))
            {
                failures.Add($"上下班时间默认值变更：start=「{workStart.Text}」，end=「{workEnd.Text}」。");
            }

            foreach (var (box, label, expectedLabelText) in new[]
                     {
                         (workStart, workStartLabel, "上班时间"),
                         (workEnd, workEndLabel, "下班时间")
                     })
            {
                if (!TimeInputBehavior.GetIsEnabled(box) ||
                    TimeInputBehavior.GetFormat(box) != TimeInputFormat.HourMinute ||
                    Typography.GetNumeralAlignment(box) != FontNumeralAlignment.Tabular ||
                    InputMethod.GetIsInputMethodEnabled(box))
                {
                    failures.Add($"{box.Name} 未正确接入 HH:mm TimeInputBehavior（enabled={TimeInputBehavior.GetIsEnabled(box)}，format={TimeInputBehavior.GetFormat(box)}，numeral={Typography.GetNumeralAlignment(box)}，ime={InputMethod.GetIsInputMethodEnabled(box)}）。");
                }

                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(box);
                var labelPeer = FrameworkElementAutomationPeer.CreatePeerForElement(label);
                if (!ReferenceEquals(AutomationProperties.GetLabeledBy(box), label) ||
                    !string.IsNullOrEmpty(AutomationProperties.GetName(box)) ||
                    peer is null ||
                    labelPeer is null ||
                    !ReferenceEquals(peer.GetLabeledBy(), labelPeer) ||
                    !string.Equals(peer.GetName(), expectedLabelText, StringComparison.Ordinal))
                {
                    failures.Add($"{box.Name} 的 LabeledBy 或等价可访问名异常（peerName=「{peer?.GetName()}」）。");
                }
            }

            // 2. 有效有焦点暖金边框 + 零布局位移 + 实拍
            Keyboard.Focus(workStart);
            SettleWindow(main);
            RequireTextBoxBorder(main, workStart, "MainActionFocusBrush", 2, "上班时间有效有焦点", failures);
            var startRestingGeo = ReadTextBoxGeometry(preferencesSurface, workStart, workEnd);
            capture?.Invoke(main, preferencesSurface, "display-settings-work-hours-focused");

            // 3. 上下班时间各自光标感知分段上下键步进与边界回绕
            workStart.Text = "23:59";
            SettleWindow(main);
            workStart.CaretIndex = 0;
            AssertStep(main, workStart, Key.Up, "00:59", 0, 2, "上班时间小时 23 上步进回绕", failures);
            AssertStep(main, workStart, Key.Down, "23:59", 0, 2, "上班时间小时 00 下步进回绕", failures);
            workStart.CaretIndex = 4;
            AssertStep(main, workStart, Key.Up, "23:00", 3, 2, "上班时间分钟 59 上步进回绕", failures);
            AssertStep(main, workStart, Key.Down, "23:59", 3, 2, "上班时间分钟 00 下步进回绕", failures);

            workStart.Text = "09:00";
            SettleWindow(main);
            Keyboard.Focus(workEnd);
            SettleWindow(main);
            RequireTextBoxBorder(main, workEnd, "MainActionFocusBrush", 2, "下班时间有效有焦点", failures);
            workEnd.Text = "18:00";
            SettleWindow(main);
            workEnd.CaretIndex = 0;
            AssertStep(main, workEnd, Key.Up, "19:00", 0, 2, "下班时间小时 18 上步进", failures);
            workEnd.CaretIndex = 4;
            AssertStep(main, workEnd, Key.Down, "19:59", 3, 2, "下班时间分钟 00 下步进回绕", failures);

            workStart.Text = "09:00";
            workEnd.Text = "18:00";
            SettleWindow(main);

            // 4. 非法格式失焦 & 提交校验（上班与下班）
            foreach (var (invalidInput, label) in new[]
                     {
                         ("", "空值"),
                         ("25:00", "小时越界"),
                         ("12:60", "分钟越界"),
                         ("9点", "非时刻文本")
                     })
            {
                announcements.Clear();
                Keyboard.Focus(workStart);
                SettleWindow(main);
                workStart.Text = invalidInput;
                SettleWindow(main);
                Keyboard.Focus(savePreferences);
                SettleWindow(main);

                if (!workStartError.IsVisible ||
                    !string.Equals(workStartError.Text, WorkStartFormatError, StringComparison.Ordinal) ||
                    !InputErrorState.GetHasError(workStart) ||
                    !string.Equals(AutomationProperties.GetHelpText(workStart), WorkStartFormatError, StringComparison.Ordinal) ||
                    !announcements.Any(notice =>
                        notice.Id == "WorkStartError" &&
                        notice.Text == WorkStartFormatError &&
                        notice.Live == AutomationLiveSetting.Polite))
                {
                    failures.Add($"上班时间失焦校验（{label}）失败：visible={workStartError.IsVisible}，text=「{workStartError.Text}」，hasError={InputErrorState.GetHasError(workStart)}。");
                }

                RequireTextBoxBorder(main, workStart, "DangerBrush", 1, $"上班时间出错无焦点（{label}）", failures);
                var startRect = Bounds(workStart, preferencesSurface);
                var startErrRect = Bounds(workStartError, preferencesSurface);
                if (startErrRect.Top < startRect.Bottom - 0.5 || Math.Abs(startErrRect.Left - startRect.Left) > 2.0)
                {
                    failures.Add($"WorkStartError 未贴在上班时间下方：field={startRect}，error={startErrRect}。");
                }

                var startErrorGeo = ReadTextBoxGeometry(preferencesSurface, workStart, workEnd);
                if (!startRestingGeo.Equals(startErrorGeo))
                {
                    failures.Add($"上班时间出错无焦点（{label}）挤动几何：before={startRestingGeo}，after={startErrorGeo}。");
                }

                Keyboard.Focus(workStart);
                SettleWindow(main);
                RequireTextBoxBorder(main, workStart, "DangerBrush", 2, $"上班时间出错有焦点（{label}）", failures);

                workStart.Text = "09:00";
                SettleWindow(main);
                if (workStartError.IsVisible || InputErrorState.GetHasError(workStart))
                {
                    failures.Add($"上班时间修正（{label}）后未立即清除错误。");
                }
            }

            // 下班时间非法格式提交：错误落在 WorkEndError，镜像状态栏，聚焦 WorkEnd，不落盘
            workEnd.Text = "24:00";
            SettleWindow(main);
            savePreferences.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleWindow(main);
            if (!workEndError.IsVisible ||
                !string.Equals(workEndError.Text, WorkEndFormatError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(workEnd) ||
                !workEnd.IsKeyboardFocusWithin ||
                !string.Equals(statusText.Text, WorkEndFormatError, StringComparison.Ordinal) ||
                workspace.Snapshot.Settings.Preferences.WorkEnd != new TimeSpan(18, 0, 0))
            {
                failures.Add($"下班时间非法格式提交校验失败：visible={workEndError.IsVisible}，text=「{workEndError.Text}」，hasError={InputErrorState.GetHasError(workEnd)}，focused={workEnd.IsKeyboardFocusWithin}。");
            }

            // 5. WorkStart >= WorkEnd 顺序校验（相等与倒挂）在失焦与提交时均落在 WorkEndError 并带错误边框
            foreach (var (startVal, endVal, label) in new[]
                     {
                         ("18:00", "18:00", "上下班相等"),
                         ("18:00", "09:00", "下班早于上班")
                     })
            {
                workStart.Text = startVal;
                workEnd.Text = endVal;
                Keyboard.Focus(workEnd);
                SettleWindow(main);
                Keyboard.Focus(savePreferences);
                SettleWindow(main);

                if (!workEndError.IsVisible ||
                    !string.Equals(workEndError.Text, WorkTimeOrderError, StringComparison.Ordinal) ||
                    !InputErrorState.GetHasError(workEnd))
                {
                    failures.Add($"上下班顺序失焦校验（{label}）未在 WorkEndError 报错或未置位错误边框：visible={workEndError.IsVisible}，text=「{workEndError.Text}」，hasError={InputErrorState.GetHasError(workEnd)}。");
                }

                savePreferences.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                SettleWindow(main);
                if (!workEndError.IsVisible ||
                    !string.Equals(workEndError.Text, WorkTimeOrderError, StringComparison.Ordinal) ||
                    !InputErrorState.GetHasError(workEnd) ||
                    !workEnd.IsKeyboardFocusWithin ||
                    !string.Equals(statusText.Text, WorkTimeOrderError, StringComparison.Ordinal) ||
                    workspace.Snapshot.Settings.Preferences.WorkStart != new TimeSpan(9, 0, 0))
                {
                    failures.Add($"上下班顺序提交校验（{label}）失败：visible={workEndError.IsVisible}，text=「{workEndError.Text}」，status=「{statusText.Text}」。");
                }

                var endRect = Bounds(workEnd, preferencesSurface);
                var endErrRect = Bounds(workEndError, preferencesSurface);
                if (endErrRect.Top < endRect.Bottom - 0.5 || Math.Abs(endErrRect.Left - endRect.Left) > 2.0)
                {
                    failures.Add($"WorkEndError（{label}）未贴在下班时间下方：field={endRect}，error={endErrRect}。");
                }
            }

            capture?.Invoke(main, preferencesSurface, "display-settings-work-hours-error");

            // 高对比度主题下出错与有效焦点均可区分
            using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                contrast.ChangeColors(new ApplicationTheme.SystemContrastPalette(
                    Colors.Black, Colors.White, Colors.Black, Colors.White, Colors.Yellow, Colors.Black, Colors.Gray));
                Keyboard.Focus(workEnd);
                SettleWindow(main);
                RequireTextBoxBorder(main, workEnd, "DangerBrush", 2, "下班时间高对比度出错有焦点", failures);
                workEnd.Text = "19:00";
                SettleWindow(main);
                RequireTextBoxBorder(main, workEnd, "MainActionFocusBrush", 2, "下班时间高对比度修正后有效焦点", failures);
            }
            SettleWindow(main);

            // 6. 合法上下班时间保存后精确落盘并驱动工作日截止与今日已赚计算
            salary.Text = "23000"; // 2030-07 有 23 个工作日，日薪精确 1000.00 元
            workStart.Text = "08:30";
            workEnd.Text = "17:30"; // 9 小时工作日；13:00 正好过半（4.5 / 9 = 50% -> 500.00 元）
            SettleWindow(main);
            if (workStartError.IsVisible || workEndError.IsVisible ||
                InputErrorState.GetHasError(workStart) || InputErrorState.GetHasError(workEnd))
            {
                failures.Add("修正为合法上下班时间 08:30-17:30 后未清除全部错误与错误边框。");
            }

            Keyboard.Focus(workEnd);
            SettleWindow(main);
            var enterHandled = WindowValidationInput.RaisePreviewKey(workEnd, Key.Enter);
            SettleWindow(main);

            var savedPrefs = workspace.Snapshot.Settings.Preferences;
            var reloadedPrefs = DesktopWorkspace.Load(settingsPath).Snapshot.Settings.Preferences;
            if (!enterHandled ||
                savedPrefs.WorkStart != new TimeSpan(8, 30, 0) ||
                savedPrefs.WorkEnd != new TimeSpan(17, 30, 0) ||
                reloadedPrefs.WorkStart != new TimeSpan(8, 30, 0) ||
                reloadedPrefs.WorkEnd != new TimeSpan(17, 30, 0))
            {
                failures.Add(
                    $"合法上下班时间 08:30-17:30 保存持久化不符：handled={enterHandled}，saved={savedPrefs.WorkStart}-{savedPrefs.WorkEnd}，reloaded={reloadedPrefs.WorkStart}-{reloadedPrefs.WorkEnd}。");
            }

            var expectedOffWorkUtc = CountdownFormatting.ToUtc(new DateTime(2030, 7, 15), 17, 30, 0);
            var actualOffWorkUtc = calendar.GetWorkdayEndUtc(workdayMorningUtc, reloadedPrefs);
            var actualEarnings = calendar.GetEarnings(workdayMorningUtc, reloadedPrefs);
            if (actualOffWorkUtc != expectedOffWorkUtc || actualEarnings != 500.00m)
            {
                failures.Add(
                    $"持久化上下班时间未正确驱动工作日计算：offWork={actualOffWorkUtc:O}（期望 {expectedOffWorkUtc:O}），earnings={actualEarnings}（期望 500.00）。");
            }

            WindowValidationInput.ClosePopupForCleanup(preferencesPopup);
        }
        finally
        {
            UiAccessibility.Announced -= Observe;
            if (main is not null)
            {
                main.Close();
                WindowValidationInput.Pump(main.Dispatcher);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    private static void AssertRuleTimeLabelAndName(
        ReminderRulesEditorWindow editor,
        TextBox time,
        string expectedLabelText,
        string expectedAutomationName,
        string modeLabel,
        List<string> failures)
    {
        var labeledBy = AutomationProperties.GetLabeledBy(time) as TextBlock;
        var timePeer = FrameworkElementAutomationPeer.CreatePeerForElement(time);
        var labelPeer = labeledBy is null ? null : FrameworkElementAutomationPeer.CreatePeerForElement(labeledBy);
        var explicitName = AutomationProperties.GetName(time);
        if (labeledBy is null ||
            !labeledBy.IsVisible ||
            !editor.IsAncestorOf(labeledBy) ||
            !string.Equals(labeledBy.Text, expectedLabelText, StringComparison.Ordinal) ||
            timePeer is null ||
            labelPeer is null ||
            !ReferenceEquals(timePeer.GetLabeledBy(), labelPeer) ||
            !string.Equals(explicitName, expectedAutomationName, StringComparison.Ordinal) ||
            !string.Equals(timePeer.GetName(), expectedAutomationName, StringComparison.Ordinal))
        {
            failures.Add(
                $"{modeLabel}规则时间 LabeledBy 或自动化名不符：label=「{labeledBy?.Text ?? "<null>"}」（期望「{expectedLabelText}」），name=「{explicitName}」（期望「{expectedAutomationName}」）。");
        }
    }

    private static void VerifyReminderCustomRowGeometry(
        Border row,
        DatePicker customDate,
        TextBlock? customTimeLabel,
        TextBox customTime,
        Button customConfirm,
        string widthLabel,
        List<string> failures)
    {
        var contentPanel = VisualDescendants<WrapPanel>(row)
            .Single(panel => string.Equals(
                AutomationProperties.GetAutomationId(panel),
                "ReminderEntryActionsPrimaryRow",
                StringComparison.Ordinal))
            .Parent as Panel
            ?? throw new InvalidOperationException("找不到提醒卡片内容面板。");
        var contentWidth = contentPanel.ActualWidth;
        var controls = customTimeLabel is null
            ? new FrameworkElement[] { customDate, customTime, customConfirm }
            : new FrameworkElement[] { customDate, customTimeLabel, customTime, customConfirm };
        var rects = controls.Select(control => Bounds(control, contentPanel)).ToArray();
        for (var i = 0; i < rects.Length; i++)
        {
            if (rects[i].Width <= 0 || rects[i].Height <= 0 ||
                rects[i].Left < -0.5 || rects[i].Right > contentWidth + 0.5)
            {
                failures.Add($"宽度 {widthLabel} 下自定义稍后控件 #{i} 被裁切：rect={rects[i]}，contentWidth={contentWidth:0.##}。");
            }

            for (var j = i + 1; j < rects.Length; j++)
            {
                var intersection = Rect.Intersect(rects[i], rects[j]);
                if (!intersection.IsEmpty && intersection.Width > 0.5 && intersection.Height > 0.5)
                {
                    failures.Add($"宽度 {widthLabel} 下自定义稍后控件 #{i} 与 #{j} 重叠：{rects[i]} vs {rects[j]}。");
                }
            }
        }
    }

    private static void VerifyErrorBelowCustomSnooze(
        Border row,
        TextBox customTime,
        Button customConfirm,
        TextBlock error,
        string label,
        List<string> failures)
    {
        var rowBottom = Math.Max(Bounds(customTime, row).Bottom, Bounds(customConfirm, row).Bottom);
        var errorRect = Bounds(error, row);
        if (errorRect.Top < rowBottom - 0.5 || errorRect.Top > rowBottom + 16)
        {
            failures.Add($"自定义稍后错误（{label}）未紧贴输入行下方：rowBottom={rowBottom:0.##}，errorTop={errorRect.Top:0.##}。");
        }
    }

    private static void AssertStep(
        Window host,
        TextBox box,
        Key key,
        string expectedText,
        int expectedSelStart,
        int expectedSelLength,
        string label,
        List<string> failures)
    {
        var handled = WindowValidationInput.RaisePreviewKey(box, key);
        SettleWindow(host);
        if (!handled ||
            !string.Equals(box.Text, expectedText, StringComparison.Ordinal) ||
            box.SelectionStart != expectedSelStart ||
            box.SelectionLength != expectedSelLength)
        {
            failures.Add(
                $"{label}失败：handled={handled}，text=「{box.Text}」（期望「{expectedText}」），sel=({box.SelectionStart},{box.SelectionLength})（期望({expectedSelStart},{expectedSelLength})）。");
        }
    }

    private static void RequireTextBoxBorder(
        Window host,
        TextBox input,
        string expectedBrushKey,
        double expectedDip,
        string state,
        List<string> failures)
    {
        input.ApplyTemplate();
        if (input.Template?.FindName("Surface", input) is not Border surface)
        {
            failures.Add($"{state}：输入框缺少模板部件 Surface。");
            return;
        }

        var expectedBrush = (Brush)host.FindResource(expectedBrushKey);
        var colorMatches = ReferenceEquals(surface.BorderBrush, expectedBrush) ||
            (surface.BorderBrush is SolidColorBrush a && expectedBrush is SolidColorBrush b && a.Color == b.Color);
        if (!colorMatches)
        {
            failures.Add($"{state}边框颜色不符：expected={expectedBrushKey}({expectedBrush})，actual={surface.BorderBrush}。");
        }

        if (Math.Abs(surface.BorderThickness.Left - expectedDip) > 0.01 ||
            Math.Abs(surface.BorderThickness.Top - expectedDip) > 0.01)
        {
            failures.Add($"{state}边框厚度不符：expected={expectedDip}，actual={surface.BorderThickness}。");
        }
    }

    private static FieldGeometry ReadTextBoxGeometry(Visual root, TextBox input, FrameworkElement sibling)
    {
        input.ApplyTemplate();
        var contentHost = input.Template?.FindName("PART_ContentHost", input) as FrameworkElement
            ?? throw new InvalidOperationException("TextBox 缺少 PART_ContentHost。");
        return new FieldGeometry(
            Bounds(input, root),
            Bounds(contentHost, root),
            Bounds(sibling, root));
    }

    private static Action<Window, FrameworkElement, string>? GetEvidenceWriter()
    {
        if (!_evidenceDirectoryInitialized)
        {
            _evidenceDirectoryInitialized = true;
            var explicitDirectory = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_ISSUE329_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(explicitDirectory))
            {
                _evidenceDirectory = Path.GetFullPath(explicitDirectory);
                Directory.CreateDirectory(_evidenceDirectory);
            }
            else
            {
                var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
                if (!string.IsNullOrWhiteSpace(sourceRoot) && Directory.Exists(sourceRoot))
                {
                    _evidenceDirectory = Path.Combine(
                        Path.GetFullPath(sourceRoot),
                        "logs",
                        "ui-captures",
                        $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-time-input-spread");
                    Directory.CreateDirectory(_evidenceDirectory);
                }
            }
        }

        if (_evidenceDirectory is null)
        {
            return null;
        }

        var directory = _evidenceDirectory;
        return (window, target, label) =>
        {
            var originalOpacity = window.Opacity;
            try
            {
                if (originalOpacity == 0)
                {
                    window.Opacity = 1;
                    SettleWindow(window);
                }

                target.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(target);
                var dpiX = dpi.PixelsPerInchX > 0 ? dpi.PixelsPerInchX : 96;
                var dpiY = dpi.PixelsPerInchY > 0 ? dpi.PixelsPerInchY : 96;
                var width = Math.Max(1, (int)Math.Ceiling(target.ActualWidth * dpiX / 96.0));
                var height = Math.Max(1, (int)Math.Ceiling(target.ActualHeight * dpiY / 96.0));
                var bitmap = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
                bitmap.Render(target);
                bitmap.Freeze();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
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
                    SettleWindow(window);
                }
            }
        };
    }

    private static ReminderRulesEditorWindow CreateOffscreenRuleEditor(
        IReadOnlyList<ReminderRule> rules,
        IClock clock,
        DateTimeOffset? countdownTargetUtc = null,
        IGuidSource? guidSource = null) =>
        new(rules, clock, guidSource, countdownTargetUtc: countdownTargetUtc)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0,
            Topmost = false
        };

    private static (bool? DialogResult, IReadOnlyList<ReminderRule> Rules) RunRuleEditorModal(
        IReadOnlyList<ReminderRule> initialRules,
        IClock clock,
        IGuidSource guidSource,
        DateTimeOffset countdownTargetUtc,
        Action<ReminderRulesEditorWindow> interact)
    {
        var editor = CreateOffscreenRuleEditor(initialRules, clock, countdownTargetUtc, guidSource);
        Exception? interactionException = null;
        try
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    SettleWindow(editor);
                    interact(editor);
                }
                catch (Exception exception)
                {
                    interactionException = exception;
                    if (editor.IsVisible)
                    {
                        editor.Close();
                    }
                }
            }));
            var dialogResult = editor.ShowDialog();
            if (interactionException is not null)
            {
                throw new InvalidOperationException("规则编辑器模态交互失败。", interactionException);
            }

            return (dialogResult, editor.ResultRules);
        }
        finally
        {
            if (editor.IsVisible)
            {
                CloseWindow(editor);
            }
        }
    }

    private static void ShowMainWindow(MainWindow main)
    {
        var rendered = false;
        main.ContentRendered += (_, _) => rendered = true;
        main.Show();
        main.Activate();
        var firstFrame = WindowValidationInput.PumpRenderedFrames(main.Dispatcher, 2000, () => rendered);
        SettleWindow(main);
        if (!rendered || !firstFrame.Completed)
        {
            throw new InvalidOperationException("管理窗口没有完成首帧。");
        }
    }

    private static void ShowAndSettle(Window window)
    {
        window.Show();
        SettleWindow(window);
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

        SettleWindow(window);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetActiveWindow(IntPtr windowHandle);

    private static void SettleWindow(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static void CloseWindow(Window window)
    {
        window.Close();
        WindowValidationInput.Pump(window.Dispatcher);
    }

    private static void InvokeButton(Window window, Button button)
    {
        if (!WindowValidationInput.InvokeButton(button))
        {
            throw new InvalidOperationException(
                $"按钮「{AutomationProperties.GetAutomationId(button)}/{button.Content}」不支持真实 Invoke。");
        }

        SettleWindow(window);
    }

    private static void SelectMenuItemByHeader(Window window, Button snoozeButton, ContextMenu snoozeMenu, string header)
    {
        ActivateForKeyboard(window);
        InvokeButton(window, snoozeButton);
        var item = snoozeMenu.Items.OfType<MenuItem>()
            .Single(candidate => string.Equals(candidate.Header as string, header, StringComparison.Ordinal));
        if (UIElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.Invoke)
            is System.Windows.Automation.Provider.IInvokeProvider invokeProvider)
        {
            invokeProvider.Invoke();
        }
        else
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }

        SettleWindow(window);
    }

    private static void WaitForTask(Task task, Window window)
    {
        var deadline = Environment.TickCount64 + 3000;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            Thread.Sleep(10);
        }

        task.GetAwaiter().GetResult();
        SettleWindow(window);
    }

    private static string BuildWeekdayCalendarJson(int year)
    {
        var builder = new StringBuilder();
        builder.Append("{\"days\":[");
        var first = true;
        for (var day = new DateTime(year, 1, 1); day.Year == year; day = day.AddDays(1))
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            var isWorkday = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            builder.Append("{\"date\":\"")
                .Append(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append("\",\"is_workday\":")
                .Append(isWorkday ? "true" : "false")
                .Append('}');
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static T Required<T>(MainWindow main, string name) where T : class =>
        main.FindName(name) as T
        ?? throw new InvalidOperationException($"管理窗口缺少控件 {name}（{typeof(T).Name}）。");

    private static T[] FindAllByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject =>
        VisualDescendants<T>(root)
            .Where(element => string.Equals(
                AutomationProperties.GetAutomationId(element),
                automationId,
                StringComparison.Ordinal))
            .ToArray();

    private static T FindSingleByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject
    {
        var matches = FindAllByAutomationId<T>(root, automationId);
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"期望找到 1 个 {typeof(T).Name}({automationId})，实际找到 {matches.Length} 个。");
        }

        return matches[0];
    }

    private readonly record struct FieldGeometry(Rect Input, Rect ContentHost, Rect Sibling);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow.ToUniversalTime();
    }
}
