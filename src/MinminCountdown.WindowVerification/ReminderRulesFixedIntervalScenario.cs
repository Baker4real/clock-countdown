using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #330：规则编辑器固定间隔输入行为（上下键/滚轮步进、1..1440 整数校验、越界当场提示与不变错误文案、
// ViewModel 可空数值属性与不变外部行为）及视觉/无障碍/资源隔离契约。
internal static class ReminderRulesFixedIntervalScenario
{
    private const string ExpectedIntervalError = "自定义提醒必须是 1 到 1440 分钟。";
    private const string IncompletePreviewText = "请完善上方设置以生成预览";
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) RunFixedIntervalNumericInput(string meaning)
    {
        var failures = new List<string>();

        CheckViewModelNullableNumericContract(failures);
        CheckSteppingImmediateFeedbackAndFiltering(failures);
        CheckModalSaveDirtyCancelPreserved(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunFixedIntervalVisualAndIsolation(string meaning)
    {
        var failures = new List<string>();
        var captureEvidence = CreateEvidenceWriter();

        CheckFocusErrorBordersContrastHighContrastAndLabels(failures, captureEvidence);
        CheckZeroWpfUiInAppResourcesAndOtherWindowsUnaffected(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    private static void CheckViewModelNullableNumericContract(List<string> failures)
    {
        var prop = typeof(ReminderRulesEditorViewModel).GetProperty(
            nameof(ReminderRulesEditorViewModel.CustomIntervalMinutes));
        if (prop is null || prop.PropertyType != typeof(int?))
        {
            failures.Add(
                $"ReminderRulesEditorViewModel.CustomIntervalMinutes 属性类型期望 Nullable<Int32>，实际 {prop?.PropertyType.FullName ?? "null"}");
            return;
        }

        var clock = new FixedClock(FixtureUtc);
        var vm = new ReminderRulesEditorViewModel(Array.Empty<ReminderRule>(), clock);
        vm.ModeIndex = 1;
        vm.IntervalIndex = 3;

        if (vm.CustomIntervalMinutes is not null)
        {
            failures.Add($"初始自定义固定间隔期望 null，实际 {vm.CustomIntervalMinutes}");
        }

        if (vm.IsIntervalErrorVisible)
        {
            failures.Add("刚切到自定义且尚未输入时不应立即显示固定间隔错误");
        }

        // 越界值 0 与 1441 当场提示且预览回退
        vm.CustomIntervalMinutes = 0;
        if (!vm.IsIntervalErrorVisible ||
            !string.Equals(vm.IntervalError, ExpectedIntervalError, StringComparison.Ordinal) ||
            vm.IsRulePreviewValid ||
            !string.Equals(vm.RulePreviewText, IncompletePreviewText, StringComparison.Ordinal) ||
            !vm.IsFormDirty)
        {
            failures.Add(
                $"ViewModel 设 0 未当场报错或未置脏（visible={vm.IsIntervalErrorVisible}, error=「{vm.IntervalError}」, previewValid={vm.IsRulePreviewValid}, dirty={vm.IsFormDirty}）");
        }

        vm.CustomIntervalMinutes = 1441;
        if (!vm.IsIntervalErrorVisible ||
            !string.Equals(vm.IntervalError, ExpectedIntervalError, StringComparison.Ordinal) ||
            vm.IsRulePreviewValid)
        {
            failures.Add(
                $"ViewModel 设 1441 未当场报错（visible={vm.IsIntervalErrorVisible}, error=「{vm.IntervalError}」）");
        }

        // 合法值 45 立即清错并恢复一句话预览
        vm.CustomIntervalMinutes = 45;
        if (vm.IsIntervalErrorVisible ||
            !string.IsNullOrEmpty(vm.IntervalError) ||
            !vm.IsRulePreviewValid ||
            !vm.RulePreviewText.Contains("每 45 分钟", StringComparison.Ordinal))
        {
            failures.Add(
                $"ViewModel 设 45 未立即清错或未更新预览（visible={vm.IsIntervalErrorVisible}, preview=「{vm.RulePreviewText}」）");
        }

        // 边界值 1 与 1440 合法
        foreach (var boundary in new[] { 1, 1440 })
        {
            vm.CustomIntervalMinutes = boundary;
            if (vm.IsIntervalErrorVisible || !vm.IsRulePreviewValid ||
                !vm.RulePreviewText.Contains($"每 {boundary} 分钟", StringComparison.Ordinal))
            {
                failures.Add($"ViewModel 边界值 {boundary} 被误判非法（error=「{vm.IntervalError}」, preview=「{vm.RulePreviewText}」）");
            }
        }

        // 切回预设档位清错，再编辑已有自定义规则回填 int? 值
        vm.CustomIntervalMinutes = 0;
        vm.IntervalIndex = 1;
        if (vm.IsIntervalErrorVisible)
        {
            failures.Add("切回预设 30 分钟档位后未清除自定义固定间隔错误");
        }

        var existingCustom = new FixedIntervalReminderRule("vm-330-custom", FixtureUtc.AddHours(2), new ReminderInterval(75));
        var existingPreset = new FixedIntervalReminderRule("vm-330-preset", FixtureUtc.AddHours(3), new ReminderInterval(30));
        var editVm = new ReminderRulesEditorViewModel([existingCustom, existingPreset], clock);
        editVm.RuleItems[0].EditCommand.Execute(null);
        if (editVm.IntervalIndex != 3 || editVm.CustomIntervalMinutes != 75 || editVm.IsFormDirty || editVm.IsIntervalErrorVisible)
        {
            failures.Add(
                $"编辑 75 分钟自定义规则未正确回填（index={editVm.IntervalIndex}, val={editVm.CustomIntervalMinutes}, dirty={editVm.IsFormDirty}）");
        }

        editVm.RuleItems[1].EditCommand.Execute(null);
        if (editVm.IntervalIndex != 1 || editVm.CustomIntervalMinutes is not null || editVm.IsFormDirty)
        {
            failures.Add(
                $"编辑 30 分钟预设规则未正确回填（index={editVm.IntervalIndex}, val={editVm.CustomIntervalMinutes}, dirty={editVm.IsFormDirty}）");
        }
    }

    private static void CheckSteppingImmediateFeedbackAndFiltering(List<string> failures)
    {
        var announcements = new List<(string Id, string Text, AutomationLiveSetting Live)>();
        void Observe(TextBlock block) => announcements.Add((
            AutomationProperties.GetAutomationId(block) ?? string.Empty,
            AutomationProperties.GetName(block) ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));

        UiAccessibility.Announced += Observe;
        var editor = CreateOffscreenEditor(Array.Empty<ReminderRule>(), new FixedClock(FixtureUtc));
        try
        {
            ShowAndSettle(editor);
            editor.Activate();

            var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
            var intervalChoice = FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval");
            var customInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval");
            var intervalError = FindByAutomationId<TextBlock>(editor, "ReminderRuleIntervalError");
            var preview = FindByAutomationId<TextBlock>(editor, "ReminderRulePreview");
            var vm = (ReminderRulesEditorViewModel)editor.DataContext;

            mode.SelectedIndex = 1;
            intervalChoice.SelectedIndex = 3;
            SettleWindow(editor);
            Keyboard.Focus(customInterval);
            SettleWindow(editor);

            // 1. 空白态按 Up 从最小值 1 起步；在 1 按 Down 钳位在 1；按 Up 到 2
            var upFromEmptyHandled = RaisePreviewKeyDown(customInterval, Key.Up);
            SettleWindow(editor);
            if (!upFromEmptyHandled || customInterval.Text != "1" || vm.CustomIntervalMinutes != 1)
            {
                failures.Add($"空白按 Up 未落到 1（handled={upFromEmptyHandled}, text=「{customInterval.Text}」, vm={vm.CustomIntervalMinutes}）");
            }

            var downAtMinHandled = RaisePreviewKeyDown(customInterval, Key.Down);
            SettleWindow(editor);
            if (!downAtMinHandled || customInterval.Text != "1" || intervalError.IsVisible)
            {
                failures.Add($"最小值 1 按 Down 未钳位在 1（handled={downAtMinHandled}, text=「{customInterval.Text}」, errorVisible={intervalError.IsVisible}）");
            }

            _ = RaisePreviewKeyDown(customInterval, Key.Up);
            SettleWindow(editor);
            if (customInterval.Text != "2" || vm.CustomIntervalMinutes != 2)
            {
                failures.Add($"1 按 Up 未增到 2（text=「{customInterval.Text}」）");
            }

            // 2. 最大值 1440 按 Up 钳位在 1440，按 Down 降到 1439
            customInterval.Text = "1440";
            SettleWindow(editor);
            var upAtMaxHandled = RaisePreviewKeyDown(customInterval, Key.Up);
            SettleWindow(editor);
            if (!upAtMaxHandled || customInterval.Text != "1440" || intervalError.IsVisible)
            {
                failures.Add($"最大值 1440 按 Up 未钳位在 1440（text=「{customInterval.Text}」, errorVisible={intervalError.IsVisible}）");
            }

            _ = RaisePreviewKeyDown(customInterval, Key.Down);
            SettleWindow(editor);
            if (customInterval.Text != "1439" || vm.CustomIntervalMinutes != 1439)
            {
                failures.Add($"1440 按 Down 未降到 1439（text=「{customInterval.Text}」）");
            }

            // 3. 滚轮增减（含 PreviewMouseWheel、直接 MouseWheel 与多格滚轮、上下边界钳位）
            customInterval.Text = "30";
            SettleWindow(editor);
            var wheelUpHandled = RaiseMouseWheel(customInterval, 120, preview: true);
            SettleWindow(editor);
            if (!wheelUpHandled || customInterval.Text != "31" || vm.CustomIntervalMinutes != 31)
            {
                failures.Add($"滚轮上滑(+120)未增到 31（handled={wheelUpHandled}, text=「{customInterval.Text}」）");
            }

            var wheelDownHandled = RaiseMouseWheel(customInterval, -120, preview: true);
            SettleWindow(editor);
            if (!wheelDownHandled || customInterval.Text != "30")
            {
                failures.Add($"滚轮下滑(-120)未降到 30（handled={wheelDownHandled}, text=「{customInterval.Text}」）");
            }

            var wheelDoubleUpHandled = RaiseMouseWheel(customInterval, 240, preview: false);
            SettleWindow(editor);
            if (!wheelDoubleUpHandled || customInterval.Text != "32")
            {
                failures.Add($"两格滚轮(+240)未增到 32（handled={wheelDoubleUpHandled}, text=「{customInterval.Text}」）");
            }

            customInterval.Text = "1";
            SettleWindow(editor);
            _ = RaiseMouseWheel(customInterval, -120, preview: true);
            SettleWindow(editor);
            if (customInterval.Text != "1")
            {
                failures.Add($"最小值 1 滚轮下滑未钳位在 1（text=「{customInterval.Text}」）");
            }

            customInterval.Text = "1440";
            SettleWindow(editor);
            _ = RaiseMouseWheel(customInterval, 120, preview: true);
            SettleWindow(editor);
            if (customInterval.Text != "1440")
            {
                failures.Add($"最大值 1440 滚轮上滑未钳位在 1440（text=「{customInterval.Text}」）");
            }

            // 4. 越界输入当场提示（无需点击保存），错误文案不变、直播区播报、HelpText 与 InputErrorState 同步
            announcements.Clear();
            customInterval.Text = "0";
            SettleWindow(editor);
            if (!intervalError.IsVisible ||
                !string.Equals(intervalError.Text, ExpectedIntervalError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(customInterval) ||
                !string.Equals(AutomationProperties.GetHelpText(customInterval), ExpectedIntervalError, StringComparison.Ordinal) ||
                customInterval.Text != "0" ||
                !string.Equals(preview.Text, IncompletePreviewText, StringComparison.Ordinal) ||
                !announcements.Any(a =>
                    a.Id == "ReminderRuleIntervalError" &&
                    a.Text == ExpectedIntervalError &&
                    a.Live == AutomationLiveSetting.Polite))
            {
                failures.Add(
                    $"输入 0 未当场报错或状态不同步（visible={intervalError.IsVisible}, text=「{intervalError.Text}」, hasError={InputErrorState.GetHasError(customInterval)}, help=「{AutomationProperties.GetHelpText(customInterval)}」, input=「{customInterval.Text}」, preview=「{preview.Text}」）");
            }

            // 从越界 0 按 Up 立即恢复为 1 并当场清错
            _ = RaisePreviewKeyDown(customInterval, Key.Up);
            SettleWindow(editor);
            if (customInterval.Text != "1" || intervalError.IsVisible || InputErrorState.GetHasError(customInterval) ||
                !string.IsNullOrEmpty(AutomationProperties.GetHelpText(customInterval)))
            {
                failures.Add($"从越界 0 按 Up 未立即恢复并清错（text=「{customInterval.Text}」, errorVisible={intervalError.IsVisible}）");
            }

            // 超过上限 1441 当场报错，滚轮下滑恢复 1440 并清错
            customInterval.Text = "1441";
            SettleWindow(editor);
            if (!intervalError.IsVisible ||
                !string.Equals(intervalError.Text, ExpectedIntervalError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(customInterval) ||
                customInterval.Text != "1441")
            {
                failures.Add($"输入 1441 未当场报错（visible={intervalError.IsVisible}, text=「{intervalError.Text}」, input=「{customInterval.Text}」）");
            }

            _ = RaiseMouseWheel(customInterval, -120, preview: true);
            SettleWindow(editor);
            if (customInterval.Text != "1440" || intervalError.IsVisible || InputErrorState.GetHasError(customInterval))
            {
                failures.Add($"从越界 1441 滚轮下滑未恢复到 1440 并清错（text=「{customInterval.Text}」, errorVisible={intervalError.IsVisible}）");
            }

            // 5. 非整数字符交互拦截 + 异常文本（如程序/IME 写入 "abc"）当场提示且不静默覆盖
            foreach (var rejected in new[] { "a", ".", "-", " " })
            {
                if (!RaisePreviewTextInput(customInterval, rejected))
                {
                    failures.Add($"交互输入非数字字符「{rejected}」未被拦截");
                }
            }

            if (RaisePreviewTextInput(customInterval, "5"))
            {
                failures.Add("交互输入合法数字「5」被错误拦截");
            }

            if (!RaisePreviewKeyDown(customInterval, Key.Space))
            {
                failures.Add("空格键未被整数输入拦截");
            }

            customInterval.Text = "abc";
            SettleWindow(editor);
            if (!intervalError.IsVisible ||
                !string.Equals(intervalError.Text, ExpectedIntervalError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(customInterval) ||
                customInterval.Text != "abc" ||
                vm.CustomIntervalMinutes is not null ||
                !string.Equals(preview.Text, IncompletePreviewText, StringComparison.Ordinal))
            {
                failures.Add(
                    $"非整数文本 abc 未当场提示或未置空数值（visible={intervalError.IsVisible}, text=「{customInterval.Text}」, vm={vm.CustomIntervalMinutes}）");
            }

            customInterval.Text = "45";
            SettleWindow(editor);
            if (intervalError.IsVisible || InputErrorState.GetHasError(customInterval) ||
                vm.CustomIntervalMinutes != 45 ||
                !preview.Text.Contains("每 45 分钟", StringComparison.Ordinal))
            {
                failures.Add($"从 abc 修正为 45 后未恢复正常态（errorVisible={intervalError.IsVisible}, preview=「{preview.Text}」）");
            }

            // 6. 禁用态与只读态不响应上下键与滚轮
            customInterval.IsReadOnly = true;
            _ = RaisePreviewKeyDown(customInterval, Key.Up);
            _ = RaiseMouseWheel(customInterval, 120, preview: true);
            SettleWindow(editor);
            if (customInterval.Text != "45")
            {
                failures.Add($"只读态下固定间隔被意外步进为「{customInterval.Text}」");
            }

            customInterval.IsReadOnly = false;
            customInterval.IsEnabled = false;
            _ = RaisePreviewKeyDown(customInterval, Key.Up);
            _ = RaiseMouseWheel(customInterval, 120, preview: true);
            SettleWindow(editor);
            if (customInterval.Text != "45")
            {
                failures.Add($"禁用态下固定间隔被意外步进为「{customInterval.Text}」");
            }

            customInterval.IsEnabled = true;
        }
        finally
        {
            UiAccessibility.Announced -= Observe;
            CloseWindow(editor);
        }
    }

    private static void CheckModalSaveDirtyCancelPreserved(List<string> failures)
    {
        var clock = new FixedClock(FixtureUtc);
        var futureBeijing = ChinaStandardTime.ToBeijingTime(FixtureUtc).AddDays(1);
        var due = new DateTimeOffset(futureBeijing.Year, futureBeijing.Month, futureBeijing.Day, 10, 30, 0, futureBeijing.Offset);

        // 滚轮/上下键设置 32 分钟后点完成 -> 自动提交 32 分钟规则
        var stepCommit = RunModal(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("33000001-0000-0000-0000-000000000001")),
            editor =>
            {
                FindByAutomationId<ComboBox>(editor, "ReminderRuleMode").SelectedIndex = 1;
                FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate").SelectedDate = due.Date;
                FindByAutomationId<TextBox>(editor, "ReminderRuleFirstDueTime").Text =
                    due.ToString("HH:mm", CultureInfo.InvariantCulture);
                FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval").SelectedIndex = 3;
                var customInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval");
                customInterval.Text = "30";
                SettleWindow(editor);
                _ = RaisePreviewKeyDown(customInterval, Key.Up);
                _ = RaiseMouseWheel(customInterval, 120, preview: true);
                SettleWindow(editor);
                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            });
        if (stepCommit.DialogResult != true ||
            stepCommit.Rules.Count != 1 ||
            stepCommit.Rules[0] is not FixedIntervalReminderRule { Interval.Minutes: 32 })
        {
            failures.Add($"上下键/滚轮修改后点完成未提交 32 分钟规则（dialog={stepCommit.DialogResult}, count={stepCommit.Rules.Count}）");
        }

        // 越界 1441 点完成被拦住并保留草稿，按 Down 恢复 1440 后点完成成功提交
        var blockedAt1441 = false;
        var recoverCommit = RunModal(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("33000002-0000-0000-0000-000000000002")),
            editor =>
            {
                FindByAutomationId<ComboBox>(editor, "ReminderRuleMode").SelectedIndex = 1;
                FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate").SelectedDate = due.Date;
                FindByAutomationId<TextBox>(editor, "ReminderRuleFirstDueTime").Text =
                    due.ToString("HH:mm", CultureInfo.InvariantCulture);
                FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval").SelectedIndex = 3;
                var customInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval");
                customInterval.Text = "1441";
                SettleWindow(editor);
                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
                var intervalError = FindByAutomationId<TextBlock>(editor, "ReminderRuleIntervalError");
                blockedAt1441 = editor.IsVisible &&
                    intervalError.IsVisible &&
                    string.Equals(intervalError.Text, ExpectedIntervalError, StringComparison.Ordinal) &&
                    customInterval.Text == "1441";
                _ = RaisePreviewKeyDown(customInterval, Key.Down);
                SettleWindow(editor);
                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            });
        if (!blockedAt1441 ||
            recoverCommit.DialogResult != true ||
            recoverCommit.Rules.Count != 1 ||
            recoverCommit.Rules[0] is not FixedIntervalReminderRule { Interval.Minutes: 1440 })
        {
            failures.Add(
                $"1441 拦住并按 Down 恢复 1440 后提交失败（blocked={blockedAt1441}, dialog={recoverCommit.DialogResult}, count={recoverCommit.Rules.Count}）");
        }
    }

    private static void CheckFocusErrorBordersContrastHighContrastAndLabels(
        List<string> failures,
        Action<Window, string>? captureEvidence)
    {
        var editor = CreateOffscreenEditor(Array.Empty<ReminderRule>(), new FixedClock(FixtureUtc));
        try
        {
            ShowAndSettle(editor);
            editor.Activate();

            var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
            var intervalCombo = FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval");
            var customInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval");
            var intervalError = FindByAutomationId<TextBlock>(editor, "ReminderRuleIntervalError");
            var saveButton = FindByAutomationId<Button>(editor, "SaveReminderRuleButton");

            mode.SelectedIndex = 1;
            intervalCombo.SelectedIndex = 3;
            customInterval.Text = "45";
            Keyboard.Focus(saveButton);
            SettleWindow(editor);

            // 1. 单层焦点描边 + 共享圆角 token
            customInterval.ApplyTemplate();
            if (customInterval.FocusVisualStyle is not null)
            {
                failures.Add("固定间隔输入未关闭外层 FocusVisualStyle（存在双层焦点框风险）");
            }

            var surface = customInterval.Template?.FindName("Surface", customInterval) as Border;
            var contentHost = customInterval.Template?.FindName("PART_ContentHost", customInterval) as FrameworkElement;
            if (surface is null || contentHost is null)
            {
                failures.Add("固定间隔输入缺少共享暖粉 TextBox 模板部件 Surface / PART_ContentHost");
                return;
            }

            var expectedRadius = (CornerRadius)Application.Current.FindResource("RadiusControl");
            if (surface.CornerRadius != expectedRadius)
            {
                failures.Add($"固定间隔输入圆角期望 {expectedRadius}，实际 {surface.CornerRadius}");
            }

            // 2. 可见标签与输入的 LabeledBy / AutomationPeer 标签关系
            AssertLabeledBy(editor, customInterval, "固定间隔", "固定间隔自定义输入", failures);
            AssertLabeledBy(editor, intervalCombo, "固定间隔", "固定间隔下拉框", failures);
            if (!string.Equals(AutomationProperties.GetName(customInterval), "自定义固定间隔分钟", StringComparison.Ordinal))
            {
                failures.Add($"自定义固定间隔可访问名变更：{AutomationProperties.GetName(customInterval)}");
            }

            var customPeer = UIElementAutomationPeer.CreatePeerForElement(customInterval);
            var peerLabel = customPeer?.GetLabeledBy();
            if (peerLabel is null || !string.Equals(peerLabel.GetName(), "固定间隔", StringComparison.Ordinal))
            {
                failures.Add($"AutomationPeer.GetLabeledBy 未解析到「固定间隔」（actual={peerLabel?.GetName() ?? "null"}）");
            }

            // 3. 对比度基线（正文/错误字 >= 4.5:1，常态/焦点/错误边框 >= 3.0:1）
            var canvasBrush = (Brush)Application.Current.FindResource("CanvasBrush");
            var inkBrush = (Brush)Application.Current.FindResource("InkBrush");
            var controlBorderBrush = (Brush)Application.Current.FindResource("ControlBorderBrush");
            var focusBrush = (Brush)Application.Current.FindResource("MainActionFocusBrush");
            var dangerBrush = (Brush)Application.Current.FindResource("DangerBrush");
            if (!ApplicationTheme.MeetsContrast(canvasBrush, inkBrush, 4.5) ||
                !ApplicationTheme.MeetsContrast(canvasBrush, dangerBrush, 4.5) ||
                !ApplicationTheme.MeetsContrast(canvasBrush, controlBorderBrush, 3.0) ||
                !ApplicationTheme.MeetsContrast(canvasBrush, focusBrush, 3.0) ||
                !ApplicationTheme.MeetsContrast(canvasBrush, dangerBrush, 3.0))
            {
                failures.Add("固定间隔输入在日间主题下未满足对比度基线（文字 >= 4.5:1，边框 >= 3.0:1）");
            }

            // 4. 焦点、悬停与错误边框规则（1 / 2 DIP + 暖金 vs 砖红 + 零布局抖动）
            var root = (Visual)editor.Content;
            var restingGeometry = ReadGeometry(root, customInterval, contentHost, intervalCombo, saveButton);
            AssertBorderState(surface, "ControlBorderBrush", 1, "日间常态无焦点", failures);
            captureEvidence?.Invoke(editor, "fixed-interval-day-normal");

            // 悬停态：1 DIP ActiveHoverBrush
            WithPointerHover(editor, customInterval, () =>
            {
                if (!customInterval.IsMouseOver)
                {
                    failures.Add("固定间隔悬停探针未命中输入框");
                    return;
                }

                AssertBorderState(surface, "ActiveHoverBrush", 1, "日间悬停无焦点", failures);
            });

            // 有效获焦态：2 DIP MainActionFocusBrush 暖金，不与 ActiveBrush/DangerBrush 混用，零布局抖动
            Keyboard.Focus(customInterval);
            SettleWindow(editor);
            if (!customInterval.IsKeyboardFocused)
            {
                failures.Add("固定间隔输入未能取得键盘焦点");
            }

            AssertBorderState(surface, "MainActionFocusBrush", 2, "日间有效有焦点", failures);
            if (BrushMatches(surface.BorderBrush, (Brush)Application.Current.FindResource("ActiveBrush")) ||
                BrushMatches(surface.BorderBrush, dangerBrush))
            {
                failures.Add("固定间隔有效焦点仍与 ActiveBrush / DangerBrush 混用");
            }

            var focusedGeometry = ReadGeometry(root, customInterval, contentHost, intervalCombo, saveButton);
            if (!restingGeometry.Equals(focusedGeometry))
            {
                failures.Add($"有效焦点切换挤动了布局：resting={restingGeometry}，focused={focusedGeometry}");
            }

            captureEvidence?.Invoke(editor, "fixed-interval-day-focused");

            // 越界出错有焦点：2 DIP DangerBrush，不挤布局，相邻下拉框不变红
            customInterval.Text = "1441";
            SettleWindow(editor);
            if (!intervalError.IsVisible || InputErrorState.GetHasError(intervalCombo))
            {
                failures.Add(
                    $"越界 1441 错误状态异常（errorVisible={intervalError.IsVisible}, comboHasError={InputErrorState.GetHasError(intervalCombo)}）");
            }

            AssertBorderState(surface, "DangerBrush", 2, "日间越界出错有焦点", failures);
            var errorFocusedGeometry = ReadGeometry(root, customInterval, contentHost, intervalCombo, saveButton);
            if (errorFocusedGeometry.Input != restingGeometry.Input ||
                errorFocusedGeometry.ContentHost != restingGeometry.ContentHost ||
                errorFocusedGeometry.Combo != restingGeometry.Combo)
            {
                failures.Add($"出错有焦点挤动了输入槽几何：resting={restingGeometry}，errorFocused={errorFocusedGeometry}");
            }

            captureEvidence?.Invoke(editor, "fixed-interval-day-error-focused");

            // 越界出错无焦点：1 DIP DangerBrush，零输入槽抖动
            Keyboard.Focus(saveButton);
            SettleWindow(editor);
            AssertBorderState(surface, "DangerBrush", 1, "日间越界出错无焦点", failures);
            var errorUnfocusedGeometry = ReadGeometry(root, customInterval, contentHost, intervalCombo, saveButton);
            if (errorUnfocusedGeometry.Input != restingGeometry.Input ||
                errorUnfocusedGeometry.ContentHost != restingGeometry.ContentHost ||
                errorUnfocusedGeometry.Combo != restingGeometry.Combo)
            {
                failures.Add($"出错无焦点挤动了输入槽几何：resting={restingGeometry}，errorUnfocused={errorUnfocusedGeometry}");
            }

            captureEvidence?.Invoke(editor, "fixed-interval-day-error-unfocused");

            // 5. Windows 系统对比度主题（运行中切换配色 + 有效焦点 Highlight 2 DIP vs 错误 WindowText 1/2 DIP + 恢复日间）
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
                SettleWindow(editor);

                AssertBorderState(surface, "DangerBrush", 1, "系统对比度出错无焦点", failures);
                if (!BrushMatches(surface.BorderBrush, new SolidColorBrush(Colors.White)) || !intervalError.IsVisible)
                {
                    failures.Add("系统对比度下固定间隔错误未映射 WindowText 或缺少错误文字");
                }

                Keyboard.Focus(customInterval);
                SettleWindow(editor);
                AssertBorderState(surface, "DangerBrush", 2, "系统对比度出错有焦点", failures);
                captureEvidence?.Invoke(editor, "fixed-interval-high-contrast-error");

                customInterval.Text = "45";
                SettleWindow(editor);
                AssertBorderState(surface, "MainActionFocusBrush", 2, "系统对比度有效有焦点", failures);
                if (!BrushMatches(surface.BorderBrush, new SolidColorBrush(Colors.Yellow)))
                {
                    failures.Add("系统对比度下固定间隔有效焦点未映射 Highlight 黄色");
                }

                captureEvidence?.Invoke(editor, "fixed-interval-high-contrast-focused");
            }

            SettleWindow(editor);
            AssertBorderState(surface, "MainActionFocusBrush", 2, "退出系统对比度后恢复暖金焦点", failures);
            if (!BrushMatches(surface.BorderBrush, new SolidColorBrush(ApplicationTheme.DayColorFor("MainActionFocusBrush"))))
            {
                failures.Add("退出系统对比度后固定间隔焦点色未恢复暖粉日间暖金");
            }
        }
        finally
        {
            CloseWindow(editor);
        }
    }

    private static void CheckZeroWpfUiInAppResourcesAndOtherWindowsUnaffected(List<string> failures)
    {
        var forbiddenKeys = new object[]
        {
            typeof(Wpf.Ui.Controls.NumberBox),
            typeof(Wpf.Ui.Controls.TextBox),
            typeof(Wpf.Ui.Controls.MessageBox),
            "DefaultUiNumberBoxStyle",
            "TextControlBackground",
            "TextControlFocusedBorderBrush",
            "ControlFillColorDefaultBrush"
        };

        var queue = new Queue<ResourceDictionary>();
        queue.Enqueue(Application.Current.Resources);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is Wpf.Ui.Markup.ThemesDictionary or Wpf.Ui.Markup.ControlsDictionary ||
                current.Source?.OriginalString.Contains("Wpf.Ui", StringComparison.OrdinalIgnoreCase) == true)
            {
                failures.Add($"WPF-UI 资源字典进入了应用级资源：{current.GetType().FullName}");
            }

            foreach (var key in forbiddenKeys)
            {
                if (current.Contains(key))
                {
                    failures.Add($"WPF-UI 资源键进入了应用级资源：{key}");
                }
            }

            foreach (var merged in current.MergedDictionaries)
            {
                queue.Enqueue(merged);
            }
        }

        // 打开规则编辑器并操作固定间隔后，确认规则编辑器自身及独立窗口里的普通 TextBox 仍消费应用暖粉模板而非 WPF-UI 样式
        var editor = CreateOffscreenEditor(Array.Empty<ReminderRule>(), new FixedClock(FixtureUtc));
        Window? peerWindow = null;
        try
        {
            ShowAndSettle(editor);
            var editorDictionaries = new Queue<ResourceDictionary>();
            editorDictionaries.Enqueue(editor.Resources);
            while (editorDictionaries.Count > 0)
            {
                var current = editorDictionaries.Dequeue();
                if (current is Wpf.Ui.Markup.ThemesDictionary or Wpf.Ui.Markup.ControlsDictionary)
                {
                    failures.Add($"退路实现不应向规则编辑器合并 WPF-UI 字典：{current.GetType().FullName}");
                }

                foreach (var merged in current.MergedDictionaries)
                {
                    editorDictionaries.Enqueue(merged);
                }
            }

            var peerTextBox = new TextBox { Text = "其他窗口输入" };
            peerWindow = new Window
            {
                Width = 300,
                Height = 140,
                Content = peerTextBox,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            peerWindow.Show();
            SettleWindow(peerWindow);
            peerTextBox.ApplyTemplate();

            var appTextBoxStyle = Application.Current.FindResource(typeof(TextBox)) as Style;
            var peerSurface = peerTextBox.Template?.FindName("Surface", peerTextBox) as Border;
            if (!ReferenceEquals(peerTextBox.Style, appTextBoxStyle) ||
                peerTextBox.FocusVisualStyle is not null ||
                peerSurface is null ||
                !BrushMatches(peerSurface.BorderBrush, (Brush)Application.Current.FindResource("ControlBorderBrush")))
            {
                failures.Add("其他窗口中的 TextBox 未保持原有暖粉样式（疑似受到 WPF-UI 资源污染）");
            }
        }
        finally
        {
            if (peerWindow is not null)
            {
                CloseWindow(peerWindow);
            }

            CloseWindow(editor);
        }
    }

    private static void AssertLabeledBy(
        Window editor,
        FrameworkElement element,
        string expectedLabelText,
        string label,
        List<string> failures)
    {
        var labeledBy = AutomationProperties.GetLabeledBy(element);
        if (labeledBy is not TextBlock textBlock ||
            !textBlock.IsVisible ||
            !string.Equals(textBlock.Text, expectedLabelText, StringComparison.Ordinal) ||
            !editor.IsAncestorOf(textBlock))
        {
            failures.Add(
                $"{label} 的 LabeledBy 未指向可见标签「{expectedLabelText}」（actual={labeledBy?.GetType().Name ?? "null"}/{(labeledBy as TextBlock)?.Text ?? ""}）");
        }
    }

    private static void AssertBorderState(
        Border surface,
        string expectedBrushKey,
        double expectedDip,
        string state,
        List<string> failures)
    {
        var expectedBrush = (Brush)Application.Current.FindResource(expectedBrushKey);
        if (!BrushMatches(surface.BorderBrush, expectedBrush))
        {
            failures.Add($"{state} 边框颜色不符：期望 {expectedBrushKey}（{expectedBrush}），实际 {surface.BorderBrush}");
        }

        if (Math.Abs(surface.BorderThickness.Left - expectedDip) > 0.01 ||
            Math.Abs(surface.BorderThickness.Top - expectedDip) > 0.01 ||
            Math.Abs(surface.BorderThickness.Right - expectedDip) > 0.01 ||
            Math.Abs(surface.BorderThickness.Bottom - expectedDip) > 0.01)
        {
            failures.Add($"{state} 边框粗细期望四边 {expectedDip} DIP，实际 {surface.BorderThickness}");
        }
    }

    private static bool RaisePreviewKeyDown(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)
            ?? throw new InvalidOperationException("控件缺少 PresentationSource。");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        target.RaiseEvent(args);
        return args.Handled;
    }

    private static bool RaiseMouseWheel(UIElement target, int delta, bool preview)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta)
        {
            RoutedEvent = preview ? UIElement.PreviewMouseWheelEvent : UIElement.MouseWheelEvent
        };
        target.RaiseEvent(args);
        return args.Handled;
    }

    private static bool RaisePreviewTextInput(UIElement target, string text)
    {
        var composition = new TextComposition(InputManager.Current, target, text);
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent
        };
        target.RaiseEvent(args);
        return args.Handled;
    }

    private static void WithPointerHover(Window window, Control target, Action verify)
    {
        SettleWindow(window);
        if (PresentationSource.FromVisual(target) is not HwndSource source ||
            source.RootVisual is not Visual root)
        {
            throw new InvalidOperationException("固定间隔悬停探针缺少 HWND。");
        }

        var point = target.TransformToAncestor(root).Transform(
            new Point(Math.Min(target.RenderSize.Width * 0.4, 24), target.RenderSize.Height / 2));
        point = source.CompositionTarget.TransformToDevice.Transform(point);
        var coordinates = new IntPtr(((int)point.Y << 16) | ((int)point.X & 0xffff));
        window.Activate();
        SetCapture(source.Handle);
        try
        {
            if (GetCapture() != source.Handle)
            {
                throw new InvalidOperationException("隔离验证窗口无法捕获鼠标移动消息。");
            }

            SendMessage(source.Handle, 0x0200, IntPtr.Zero, coordinates);
            SettleWindow(window);
            verify();
        }
        finally
        {
            if (GetCapture() == source.Handle)
            {
                ReleaseCapture();
            }

            SendMessage(source.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
            SettleWindow(window);
        }
    }

    private static FieldGeometry ReadGeometry(
        Visual root,
        TextBox input,
        FrameworkElement contentHost,
        ComboBox combo,
        Button save) =>
        new(
            Bounds(input, root),
            Bounds(contentHost, root),
            Bounds(combo, root),
            Bounds(save, root));

    private static bool BrushMatches(Brush? actual, Brush expected) =>
        ReferenceEquals(actual, expected) ||
        (actual is SolidColorBrush a && expected is SolidColorBrush b && a.Color == b.Color);

    private static ModalEditorResult RunModal(
        IReadOnlyList<ReminderRule> initialRules,
        IClock clock,
        IGuidSource? guidSource,
        Action<ReminderRulesEditorWindow> interact)
    {
        var editor = CreateOffscreenEditor(initialRules, clock, guidSource);
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
                }
                finally
                {
                    if (editor.IsVisible)
                    {
                        editor.Close();
                    }
                }
            }));
            var dialogResult = editor.ShowDialog();
            if (interactionException is not null)
            {
                throw new InvalidOperationException("规则编辑器模态交互抛出异常。", interactionException);
            }

            return new ModalEditorResult(dialogResult, editor.ResultRules);
        }
        finally
        {
            if (editor.IsVisible)
            {
                CloseWindow(editor);
            }
        }
    }

    private static ReminderRulesEditorWindow CreateOffscreenEditor(
        IReadOnlyList<ReminderRule> rules,
        IClock clock,
        IGuidSource? guidSource = null)
    {
        return new ReminderRulesEditorWindow(rules, clock, guidSource)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0,
            Topmost = false
        };
    }

    private static void ShowAndSettle(ReminderRulesEditorWindow editor)
    {
        editor.Show();
        SettleWindow(editor);
    }

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

    private static T FindByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject
    {
        var matches = VisualDescendants<T>(root)
            .Where(element => string.Equals(
                AutomationProperties.GetAutomationId(element),
                automationId,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"期望找到 1 个 {typeof(T).Name}({automationId})，实际找到 {matches.Length} 个。");
        }

        return matches[0];
    }

    private static readonly Lazy<string?> SharedEvidenceDirectory = new(() =>
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
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-rules-editor-330");
        Directory.CreateDirectory(directory);
        return directory;
    });

    private static Action<Window, string>? CreateEvidenceWriter()
    {
        var directory = SharedEvidenceDirectory.Value;
        if (directory is null)
        {
            return null;
        }

        return (window, label) =>
        {
            if (window.Content is not FrameworkElement root)
            {
                throw new InvalidOperationException("规则编辑器实拍缺少根视觉。");
            }

            var originalOpacity = window.Opacity;
            try
            {
                TextOptions.SetTextRenderingMode(root, TextRenderingMode.Aliased);
                TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
                if (window.Opacity == 0)
                {
                    window.Opacity = 0.01;
                    SettleWindow(window);
                }

                var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
                var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                bitmap.Freeze();

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new FileStream(
                    Path.Combine(directory, label + ".png"),
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                encoder.Save(stream);
            }
            finally
            {
                window.Opacity = originalOpacity;
            }
        };
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

    private readonly record struct FieldGeometry(Rect Input, Rect ContentHost, Rect Combo, Rect Save);

    private sealed record ModalEditorResult(bool? DialogResult, IReadOnlyList<ReminderRule> Rules);

    private sealed class ValidationGuidSource(params Guid[] values) : IGuidSource
    {
        private readonly Queue<Guid> _values = new(values);

        public Guid NewGuid() => _values.Count > 0 ? _values.Dequeue() : Guid.NewGuid();
    }
}
