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
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #331：将 #330 的可复用数值步进与校验模式铺到规则编辑器重复间隔（1..999）、
// 倒计时提前分钟（0..5256000）以及主窗口显示设置月薪（两位小数、千位分隔、0..上限），
// 验收步进、范围校验、不变错误文案与位置、零月薪下游鼓励语、单层暖金焦点与 1/2 DIP 错误边、
// 对比度、系统对比度主题、LabeledBy 标签关系与零 WPF-UI 资源污染。
internal static class ExtendedNumericInputsScenario
{
    private const string ExpectedCalendarIntervalError = "每隔几个周期必须是 1 到 999。";
    private const string ExpectedRelativeOffsetError = "倒计时相对提醒必须提前 0 到 5256000 分钟，且派生时间晚于当前北京时间。";
    private const string ExpectedSalaryFormatError = "月薪格式无效。";
    private const string ExpectedSalaryNegativeError = "月薪不能小于 0。";
    private const string IncompletePreviewText = "请完善上方设置以生成预览";

    private const decimal FixtureMonthlySalary = 4910.04m; // 2030-07 有 23 个工作日，日薪 213.48 元
    private const string ExpectedQuarterDayEarningsText = "今日已赚 ￥53.37";

    private static readonly DateTimeOffset RuleEditorFixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SalaryFixtureUtc =
        new DateTimeOffset(2030, 7, 15, 11, 15, 0, TimeSpan.FromHours(8)).ToUniversalTime();

    internal static (bool Passed, string Explanation) RunRuleEditorRepeatAndLeadInputs(string meaning)
    {
        var failures = new List<string>();

        CheckCalendarIntervalViewModelContract(failures);
        CheckCalendarIntervalSteppingFilteringAndModal(failures);
        CheckRelativeOffsetViewModelContract(failures);
        CheckRelativeOffsetSteppingFilteringAndModal(failures);
        CheckRuleEditorVisualContrastHighContrastAndLabels(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunMonthlySalaryDecimalInputAndDownstream(string meaning)
    {
        var failures = new List<string>();

        CheckMonthlySalaryInputValidationVisualAndDownstream(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    private static void CheckCalendarIntervalViewModelContract(List<string> failures)
    {
        var prop = typeof(ReminderRulesEditorViewModel).GetProperty(
            nameof(ReminderRulesEditorViewModel.CalendarInterval));
        if (prop is null || prop.PropertyType != typeof(int?))
        {
            failures.Add(
                $"ReminderRulesEditorViewModel.CalendarInterval 属性类型期望 Nullable<Int32>，实际 {prop?.PropertyType.FullName ?? "null"}");
            return;
        }

        var clock = new FixedClock(RuleEditorFixtureUtc);
        var vm = new ReminderRulesEditorViewModel(Array.Empty<ReminderRule>(), clock);
        vm.ModeIndex = 2; // 便签模式下 2 = 按日历重复

        if (vm.CalendarInterval != 1 || vm.IsCalendarIntervalErrorVisible)
        {
            failures.Add(
                $"初始每隔几个周期期望 1 且无错误（value={vm.CalendarInterval}, errorVisible={vm.IsCalendarIntervalErrorVisible}）");
        }

        // 越界 0 与 1000 当场报错且预览回退
        foreach (var invalid in new[] { 0, 1000 })
        {
            vm.CalendarInterval = invalid;
            if (!vm.IsCalendarIntervalErrorVisible ||
                !string.Equals(vm.CalendarIntervalError, ExpectedCalendarIntervalError, StringComparison.Ordinal) ||
                vm.IsRulePreviewValid ||
                !string.Equals(vm.RulePreviewText, IncompletePreviewText, StringComparison.Ordinal) ||
                !vm.IsFormDirty)
            {
                failures.Add(
                    $"ViewModel CalendarInterval={invalid} 未当场报错或预览未回退（visible={vm.IsCalendarIntervalErrorVisible}, error=「{vm.CalendarIntervalError}」, previewValid={vm.IsRulePreviewValid}, dirty={vm.IsFormDirty}）");
            }
        }

        // 边界 1 与 999 合法并清错
        foreach (var boundary in new[] { 1, 999 })
        {
            vm.CalendarInterval = boundary;
            if (vm.IsCalendarIntervalErrorVisible ||
                !string.IsNullOrEmpty(vm.CalendarIntervalError) ||
                !vm.IsRulePreviewValid ||
                !vm.RulePreviewText.Contains($"每 {boundary} 日", StringComparison.Ordinal))
            {
                failures.Add(
                    $"ViewModel CalendarInterval 边界值 {boundary} 被误判非法（error=「{vm.CalendarIntervalError}」, preview=「{vm.RulePreviewText}」）");
            }
        }

        // 切出日历模式清错，编辑已有规则回填 int? 值且不置脏
        vm.CalendarInterval = 0;
        vm.ModeIndex = 0;
        if (vm.IsCalendarIntervalErrorVisible)
        {
            failures.Add("切离按日历重复模式后未清除每隔几个周期错误");
        }

        var existingCalendar = new CalendarReminderRule(
            "vm-331-cal",
            CalendarFrequency.Daily,
            4,
            new DateOnly(2038, 7, 9),
            new TimeOnly(9, 15),
            new DailyCalendarSelector());
        var editVm = new ReminderRulesEditorViewModel([existingCalendar], clock);
        editVm.RuleItems[0].EditCommand.Execute(null);
        if (editVm.CalendarInterval != 4 || editVm.IsFormDirty || editVm.IsCalendarIntervalErrorVisible)
        {
            failures.Add(
                $"编辑 4 日周期规则未正确回填（val={editVm.CalendarInterval}, dirty={editVm.IsFormDirty}, errorVisible={editVm.IsCalendarIntervalErrorVisible}）");
        }
    }

    private static void CheckCalendarIntervalSteppingFilteringAndModal(List<string> failures)
    {
        var announcements = new List<(string Id, string Text, AutomationLiveSetting Live)>();
        void Observe(TextBlock block) => announcements.Add((
            AutomationProperties.GetAutomationId(block) ?? string.Empty,
            AutomationProperties.GetName(block) ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));

        UiAccessibility.Announced += Observe;
        var editor = CreateOffscreenEditor(Array.Empty<ReminderRule>(), new FixedClock(RuleEditorFixtureUtc));
        try
        {
            ShowAndSettle(editor);
            editor.Activate();

            var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
            var calendarInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCalendarInterval");
            var intervalError = FindByAutomationId<TextBlock>(editor, "ReminderRuleCalendarIntervalError");
            var preview = FindByAutomationId<TextBlock>(editor, "ReminderRulePreview");

            mode.SelectedIndex = 2; // 便签模式：按日历重复
            SettleWindow(editor);
            Keyboard.Focus(calendarInterval);
            SettleWindow(editor);

            // 1. 空白按 Up 起步 1；最小值 1 按 Down 钳位；按 Up 到 2；最大值 999 按 Up 钳位，按 Down 降到 998
            calendarInterval.Text = string.Empty;
            SettleWindow(editor);
            var upFromEmpty = RaisePreviewKeyDown(calendarInterval, Key.Up);
            SettleWindow(editor);
            if (!upFromEmpty || calendarInterval.Text != "1")
            {
                failures.Add($"每隔几个周期空白按 Up 未落到 1（handled={upFromEmpty}, text=「{calendarInterval.Text}」）");
            }

            var downAtMin = RaisePreviewKeyDown(calendarInterval, Key.Down);
            SettleWindow(editor);
            if (!downAtMin || calendarInterval.Text != "1" || intervalError.IsVisible)
            {
                failures.Add($"每隔几个周期 1 按 Down 未钳位在 1（handled={downAtMin}, text=「{calendarInterval.Text}」）");
            }

            _ = RaisePreviewKeyDown(calendarInterval, Key.Up);
            SettleWindow(editor);
            if (calendarInterval.Text != "2")
            {
                failures.Add($"每隔几个周期 1 按 Up 未增到 2（text=「{calendarInterval.Text}」）");
            }

            calendarInterval.Text = "999";
            SettleWindow(editor);
            var upAtMax = RaisePreviewKeyDown(calendarInterval, Key.Up);
            SettleWindow(editor);
            if (!upAtMax || calendarInterval.Text != "999" || intervalError.IsVisible)
            {
                failures.Add($"每隔几个周期 999 按 Up 未钳位在 999（handled={upAtMax}, text=「{calendarInterval.Text}」）");
            }

            _ = RaisePreviewKeyDown(calendarInterval, Key.Down);
            SettleWindow(editor);
            if (calendarInterval.Text != "998")
            {
                failures.Add($"每隔几个周期 999 按 Down 未降到 998（text=「{calendarInterval.Text}」）");
            }

            // 2. 滚轮增减与边界钳位
            calendarInterval.Text = "10";
            SettleWindow(editor);
            var wheelUp = RaiseMouseWheel(calendarInterval, 120, preview: true);
            SettleWindow(editor);
            var wheelDown = RaiseMouseWheel(calendarInterval, -120, preview: true);
            SettleWindow(editor);
            var wheelDoubleUp = RaiseMouseWheel(calendarInterval, 240, preview: false);
            SettleWindow(editor);
            if (!wheelUp || !wheelDown || !wheelDoubleUp || calendarInterval.Text != "12")
            {
                failures.Add($"每隔几个周期滚轮步进异常（up={wheelUp}, down={wheelDown}, double={wheelDoubleUp}, text=「{calendarInterval.Text}」）");
            }

            // 3. 越界 0 当场报错 + 直播区播报 + HelpText + InputErrorState；按 Up 恢复 1 清错
            announcements.Clear();
            calendarInterval.Text = "0";
            SettleWindow(editor);
            if (!intervalError.IsVisible ||
                !string.Equals(intervalError.Text, ExpectedCalendarIntervalError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(calendarInterval) ||
                !string.Equals(AutomationProperties.GetHelpText(calendarInterval), ExpectedCalendarIntervalError, StringComparison.Ordinal) ||
                calendarInterval.Text != "0" ||
                !string.Equals(preview.Text, IncompletePreviewText, StringComparison.Ordinal) ||
                !announcements.Any(a =>
                    a.Id == "ReminderRuleCalendarIntervalError" &&
                    a.Text == ExpectedCalendarIntervalError &&
                    a.Live == AutomationLiveSetting.Polite))
            {
                failures.Add(
                    $"每隔几个周期填 0 未当场报错或状态不同步（visible={intervalError.IsVisible}, text=「{intervalError.Text}」, hasError={InputErrorState.GetHasError(calendarInterval)}, help=「{AutomationProperties.GetHelpText(calendarInterval)}」, preview=「{preview.Text}」）");
            }

            _ = RaisePreviewKeyDown(calendarInterval, Key.Up);
            SettleWindow(editor);
            if (calendarInterval.Text != "1" || intervalError.IsVisible || InputErrorState.GetHasError(calendarInterval) ||
                !string.IsNullOrEmpty(AutomationProperties.GetHelpText(calendarInterval)))
            {
                failures.Add($"每隔几个周期从 0 按 Up 未立即恢复并清错（text=「{calendarInterval.Text}」, visible={intervalError.IsVisible}）");
            }

            // 越界 1000 当场报错，滚轮下滑恢复 999 清错
            calendarInterval.Text = "1000";
            SettleWindow(editor);
            if (!intervalError.IsVisible || !InputErrorState.GetHasError(calendarInterval) ||
                !string.Equals(intervalError.Text, ExpectedCalendarIntervalError, StringComparison.Ordinal))
            {
                failures.Add($"每隔几个周期填 1000 未当场报错（visible={intervalError.IsVisible}, text=「{intervalError.Text}」）");
            }

            _ = RaiseMouseWheel(calendarInterval, -120, preview: true);
            SettleWindow(editor);
            if (calendarInterval.Text != "999" || intervalError.IsVisible || InputErrorState.GetHasError(calendarInterval))
            {
                failures.Add($"每隔几个周期从 1000 滚轮下滑未恢复 999 并清错（text=「{calendarInterval.Text}」）");
            }

            // 4. 非数字字符与空格拦截 + 异常文本 "abc" 当场报错
            foreach (var rejected in new[] { "a", ".", "-", " " })
            {
                if (!RaisePreviewTextInput(calendarInterval, rejected))
                {
                    failures.Add($"每隔几个周期未拦截非数字输入「{rejected}」");
                }
            }

            if (RaisePreviewTextInput(calendarInterval, "3"))
            {
                failures.Add("每隔几个周期错误拦截了数字「3」");
            }

            if (!RaisePreviewKeyDown(calendarInterval, Key.Space))
            {
                failures.Add("每隔几个周期未拦截空格键");
            }

            calendarInterval.Text = "abc";
            SettleWindow(editor);
            if (!intervalError.IsVisible || !InputErrorState.GetHasError(calendarInterval) ||
                calendarInterval.Text != "abc" ||
                !string.Equals(preview.Text, IncompletePreviewText, StringComparison.Ordinal))
            {
                failures.Add($"每隔几个周期非整数文本 abc 未当场报错（visible={intervalError.IsVisible}, text=「{calendarInterval.Text}」）");
            }

            calendarInterval.Text = "3";
            SettleWindow(editor);
            if (intervalError.IsVisible || InputErrorState.GetHasError(calendarInterval) ||
                !preview.Text.Contains("每 3 日", StringComparison.Ordinal))
            {
                failures.Add($"每隔几个周期从 abc 修正为 3 未恢复正常（visible={intervalError.IsVisible}, preview=「{preview.Text}」）");
            }

            // 5. 只读与禁用不响应步进
            calendarInterval.IsReadOnly = true;
            _ = RaisePreviewKeyDown(calendarInterval, Key.Up);
            _ = RaiseMouseWheel(calendarInterval, 120, preview: true);
            SettleWindow(editor);
            if (calendarInterval.Text != "3")
            {
                failures.Add($"每隔几个周期只读态被步进为「{calendarInterval.Text}」");
            }

            calendarInterval.IsReadOnly = false;
            calendarInterval.IsEnabled = false;
            _ = RaisePreviewKeyDown(calendarInterval, Key.Up);
            _ = RaiseMouseWheel(calendarInterval, 120, preview: true);
            SettleWindow(editor);
            if (calendarInterval.Text != "3")
            {
                failures.Add($"每隔几个周期禁用态被步进为「{calendarInterval.Text}」");
            }

            calendarInterval.IsEnabled = true;
        }
        finally
        {
            UiAccessibility.Announced -= Observe;
            CloseWindow(editor);
        }

        // 6. 模态保存与脏关闭拦错后按 Up 恢复提交
        var clock = new FixedClock(RuleEditorFixtureUtc);
        var startDate = ChinaStandardTime.ToBeijingTime(RuleEditorFixtureUtc).AddDays(1).Date;
        var blockedAtZero = false;
        var modal = RunModal(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("33100001-0000-0000-0000-000000000001")),
            countdownTargetUtc: null,
            editorWindow =>
            {
                FindByAutomationId<ComboBox>(editorWindow, "ReminderRuleMode").SelectedIndex = 2;
                FindByAutomationId<ComboBox>(editorWindow, "ReminderRuleCalendarFrequency").SelectedIndex = 0;
                FindByAutomationId<DatePicker>(editorWindow, "ReminderRuleFirstDueDate").SelectedDate = startDate;
                FindByAutomationId<TextBox>(editorWindow, "ReminderRuleFirstDueTime").Text = "09:15";
                var intervalBox = FindByAutomationId<TextBox>(editorWindow, "ReminderRuleCalendarInterval");
                intervalBox.Text = "0";
                SettleWindow(editorWindow);
                InvokeButton(editorWindow, FindByAutomationId<Button>(editorWindow, "AcceptReminderRulesButton"));
                var err = FindByAutomationId<TextBlock>(editorWindow, "ReminderRuleCalendarIntervalError");
                blockedAtZero = editorWindow.IsVisible && err.IsVisible &&
                    string.Equals(err.Text, ExpectedCalendarIntervalError, StringComparison.Ordinal) &&
                    intervalBox.Text == "0";
                _ = RaisePreviewKeyDown(intervalBox, Key.Up); // -> 1
                _ = RaisePreviewKeyDown(intervalBox, Key.Up); // -> 2
                _ = RaiseMouseWheel(intervalBox, 240, preview: true); // -> 4
                SettleWindow(editorWindow);
                InvokeButton(editorWindow, FindByAutomationId<Button>(editorWindow, "AcceptReminderRulesButton"));
            });
        if (!blockedAtZero || modal.DialogResult != true || modal.Rules.Count != 1 ||
            modal.Rules[0] is not CalendarReminderRule { Interval: 4 })
        {
            failures.Add(
                $"每隔几个周期模态拦错与步进提交失败（blocked={blockedAtZero}, dialog={modal.DialogResult}, count={modal.Rules.Count}）");
        }
    }

    private static void CheckRelativeOffsetViewModelContract(List<string> failures)
    {
        var prop = typeof(ReminderRulesEditorViewModel).GetProperty(
            nameof(ReminderRulesEditorViewModel.RelativeOffsetMinutes));
        if (prop is null || prop.PropertyType != typeof(int?))
        {
            failures.Add(
                $"ReminderRulesEditorViewModel.RelativeOffsetMinutes 属性类型期望 Nullable<Int32>，实际 {prop?.PropertyType.FullName ?? "null"}");
            return;
        }

        var clock = new FixedClock(RuleEditorFixtureUtc);
        var targetUtc = RuleEditorFixtureUtc.AddYears(11);
        var vm = new ReminderRulesEditorViewModel(Array.Empty<ReminderRule>(), clock, countdownTargetUtc: targetUtc);
        vm.ModeIndex = 2; // 倒计时模式：截止前提醒

        if (vm.RelativeOffsetMinutes != 0 || vm.IsRelativeOffsetErrorVisible)
        {
            failures.Add(
                $"初始提前分钟期望 0 且无错误（value={vm.RelativeOffsetMinutes}, errorVisible={vm.IsRelativeOffsetErrorVisible}）");
        }

        // 越界 -1 与 5256001 当场报错且预览回退
        foreach (var invalid in new[] { -1, 5_256_001 })
        {
            vm.RelativeOffsetMinutes = invalid;
            if (!vm.IsRelativeOffsetErrorVisible ||
                !string.Equals(vm.RelativeOffsetError, ExpectedRelativeOffsetError, StringComparison.Ordinal) ||
                vm.IsRulePreviewValid ||
                !string.Equals(vm.RulePreviewText, IncompletePreviewText, StringComparison.Ordinal) ||
                !vm.IsFormDirty)
            {
                failures.Add(
                    $"ViewModel RelativeOffsetMinutes={invalid} 未当场报错或预览未回退（visible={vm.IsRelativeOffsetErrorVisible}, error=「{vm.RelativeOffsetError}」, previewValid={vm.IsRulePreviewValid}, dirty={vm.IsFormDirty}）");
            }
        }

        // 边界 0 与 5256000 合法
        vm.RelativeOffsetMinutes = 0;
        if (vm.IsRelativeOffsetErrorVisible || !vm.IsRulePreviewValid ||
            !string.Equals(vm.RulePreviewText, "截止前提醒 · 截止时提醒", StringComparison.Ordinal))
        {
            failures.Add($"ViewModel RelativeOffsetMinutes=0 被误判非法（error=「{vm.RelativeOffsetError}」, preview=「{vm.RulePreviewText}」）");
        }

        vm.RelativeOffsetMinutes = 5_256_000;
        if (vm.IsRelativeOffsetErrorVisible || !vm.IsRulePreviewValid ||
            !string.Equals(vm.RulePreviewText, "截止前提醒 · 提前 5256000 分钟", StringComparison.Ordinal))
        {
            failures.Add($"ViewModel RelativeOffsetMinutes=5256000 被误判非法（error=「{vm.RelativeOffsetError}」, preview=「{vm.RulePreviewText}」）");
        }

        // 切离模式清错，编辑已有规则回填 int? 且不置脏
        vm.RelativeOffsetMinutes = -1;
        vm.ModeIndex = 0;
        if (vm.IsRelativeOffsetErrorVisible)
        {
            failures.Add("切离截止前提醒模式后未清除提前分钟错误");
        }

        var existingRelative = new CountdownRelativeReminderRule("vm-331-rel", 45);
        var editVm = new ReminderRulesEditorViewModel([existingRelative], clock, countdownTargetUtc: targetUtc);
        editVm.RuleItems[0].EditCommand.Execute(null);
        if (editVm.RelativeOffsetMinutes != 45 || editVm.IsFormDirty || editVm.IsRelativeOffsetErrorVisible)
        {
            failures.Add(
                $"编辑提前 45 分钟规则未正确回填（val={editVm.RelativeOffsetMinutes}, dirty={editVm.IsFormDirty}）");
        }
    }

    private static void CheckRelativeOffsetSteppingFilteringAndModal(List<string> failures)
    {
        var announcements = new List<(string Id, string Text, AutomationLiveSetting Live)>();
        void Observe(TextBlock block) => announcements.Add((
            AutomationProperties.GetAutomationId(block) ?? string.Empty,
            AutomationProperties.GetName(block) ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));

        var farTargetUtc = RuleEditorFixtureUtc.AddYears(11);
        UiAccessibility.Announced += Observe;
        var editor = CreateOffscreenEditor(
            Array.Empty<ReminderRule>(),
            new FixedClock(RuleEditorFixtureUtc),
            countdownTargetUtc: farTargetUtc);
        try
        {
            ShowAndSettle(editor);
            editor.Activate();

            var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
            var relativeOffset = FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes");
            var relativeError = FindByAutomationId<TextBlock>(editor, "ReminderRuleRelativeOffsetError");
            var preview = FindByAutomationId<TextBlock>(editor, "ReminderRulePreview");

            mode.SelectedIndex = 2; // 截止前提醒
            SettleWindow(editor);
            Keyboard.Focus(relativeOffset);
            SettleWindow(editor);

            // 1. 空白按 Up 落到 0；0 按 Down 钳位；按 Up 到 1；5256000 按 Up 钳位，按 Down 降到 5255999
            relativeOffset.Text = string.Empty;
            SettleWindow(editor);
            var upFromEmpty = RaisePreviewKeyDown(relativeOffset, Key.Up);
            SettleWindow(editor);
            if (!upFromEmpty || relativeOffset.Text != "0")
            {
                failures.Add($"提前分钟空白按 Up 未落到 0（handled={upFromEmpty}, text=「{relativeOffset.Text}」）");
            }

            var downAtMin = RaisePreviewKeyDown(relativeOffset, Key.Down);
            SettleWindow(editor);
            if (!downAtMin || relativeOffset.Text != "0" || relativeError.IsVisible)
            {
                failures.Add($"提前分钟 0 按 Down 未钳位在 0（handled={downAtMin}, text=「{relativeOffset.Text}」）");
            }

            _ = RaisePreviewKeyDown(relativeOffset, Key.Up);
            SettleWindow(editor);
            if (relativeOffset.Text != "1")
            {
                failures.Add($"提前分钟 0 按 Up 未增到 1（text=「{relativeOffset.Text}」）");
            }

            relativeOffset.Text = "5256000";
            SettleWindow(editor);
            var upAtMax = RaisePreviewKeyDown(relativeOffset, Key.Up);
            SettleWindow(editor);
            if (!upAtMax || relativeOffset.Text != "5256000" || relativeError.IsVisible)
            {
                failures.Add($"提前分钟 5256000 按 Up 未钳位在 5256000（handled={upAtMax}, text=「{relativeOffset.Text}」）");
            }

            _ = RaisePreviewKeyDown(relativeOffset, Key.Down);
            SettleWindow(editor);
            if (relativeOffset.Text != "5255999")
            {
                failures.Add($"提前分钟 5256000 按 Down 未降到 5255999（text=「{relativeOffset.Text}」）");
            }

            // 2. 滚轮步进
            relativeOffset.Text = "15";
            SettleWindow(editor);
            var wheelUp = RaiseMouseWheel(relativeOffset, 120, preview: true);
            SettleWindow(editor);
            var wheelDown = RaiseMouseWheel(relativeOffset, -120, preview: true);
            SettleWindow(editor);
            var wheelDoubleUp = RaiseMouseWheel(relativeOffset, 240, preview: false);
            SettleWindow(editor);
            if (!wheelUp || !wheelDown || !wheelDoubleUp || relativeOffset.Text != "17")
            {
                failures.Add($"提前分钟滚轮步进异常（up={wheelUp}, down={wheelDown}, double={wheelDoubleUp}, text=「{relativeOffset.Text}」）");
            }

            // 3. 越界 -1 当场报错、直播区播报、HelpText 与 InputErrorState 同步；Up 恢复 0 清错
            announcements.Clear();
            relativeOffset.Text = "-1";
            SettleWindow(editor);
            if (!relativeError.IsVisible ||
                !string.Equals(relativeError.Text, ExpectedRelativeOffsetError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(relativeOffset) ||
                !string.Equals(AutomationProperties.GetHelpText(relativeOffset), ExpectedRelativeOffsetError, StringComparison.Ordinal) ||
                relativeOffset.Text != "-1" ||
                !string.Equals(preview.Text, IncompletePreviewText, StringComparison.Ordinal) ||
                !announcements.Any(a =>
                    a.Id == "ReminderRuleRelativeOffsetError" &&
                    a.Text == ExpectedRelativeOffsetError &&
                    a.Live == AutomationLiveSetting.Polite))
            {
                failures.Add(
                    $"提前分钟填 -1 未当场报错或状态不同步（visible={relativeError.IsVisible}, text=「{relativeError.Text}」, hasError={InputErrorState.GetHasError(relativeOffset)}, help=「{AutomationProperties.GetHelpText(relativeOffset)}」）");
            }

            _ = RaisePreviewKeyDown(relativeOffset, Key.Up);
            SettleWindow(editor);
            if (relativeOffset.Text != "0" || relativeError.IsVisible || InputErrorState.GetHasError(relativeOffset) ||
                !string.IsNullOrEmpty(AutomationProperties.GetHelpText(relativeOffset)))
            {
                failures.Add($"提前分钟从 -1 按 Up 未立即恢复 0 并清错（text=「{relativeOffset.Text}」, visible={relativeError.IsVisible}）");
            }

            // 越界 5256001 当场报错，滚轮下滑恢复 5256000 清错
            relativeOffset.Text = "5256001";
            SettleWindow(editor);
            if (!relativeError.IsVisible || !InputErrorState.GetHasError(relativeOffset) ||
                !string.Equals(relativeError.Text, ExpectedRelativeOffsetError, StringComparison.Ordinal))
            {
                failures.Add($"提前分钟填 5256001 未当场报错（visible={relativeError.IsVisible}, text=「{relativeError.Text}」）");
            }

            _ = RaiseMouseWheel(relativeOffset, -120, preview: true);
            SettleWindow(editor);
            if (relativeOffset.Text != "5256000" || relativeError.IsVisible || InputErrorState.GetHasError(relativeOffset))
            {
                failures.Add($"提前分钟从 5256001 滚轮下滑未恢复 5256000 并清错（text=「{relativeOffset.Text}」）");
            }

            // 4. 非数字字符与空格拦截 + 异常文本 "abc" 当场报错
            foreach (var rejected in new[] { "a", ".", "-", " " })
            {
                if (!RaisePreviewTextInput(relativeOffset, rejected))
                {
                    failures.Add($"提前分钟未拦截非数字输入「{rejected}」");
                }
            }

            if (RaisePreviewTextInput(relativeOffset, "8"))
            {
                failures.Add("提前分钟错误拦截了数字「8」");
            }

            if (!RaisePreviewKeyDown(relativeOffset, Key.Space))
            {
                failures.Add("提前分钟未拦截空格键");
            }

            relativeOffset.Text = "abc";
            SettleWindow(editor);
            if (!relativeError.IsVisible || !InputErrorState.GetHasError(relativeOffset) ||
                relativeOffset.Text != "abc" ||
                !string.Equals(preview.Text, IncompletePreviewText, StringComparison.Ordinal))
            {
                failures.Add($"提前分钟非整数文本 abc 未当场报错（visible={relativeError.IsVisible}, text=「{relativeOffset.Text}」）");
            }

            relativeOffset.Text = "15";
            SettleWindow(editor);
            if (relativeError.IsVisible || InputErrorState.GetHasError(relativeOffset) ||
                !string.Equals(preview.Text, "截止前提醒 · 提前 15 分钟", StringComparison.Ordinal))
            {
                failures.Add($"提前分钟从 abc 修正为 15 未恢复正常（visible={relativeError.IsVisible}, preview=「{preview.Text}」）");
            }

            // 5. 只读与禁用不响应步进
            relativeOffset.IsReadOnly = true;
            _ = RaisePreviewKeyDown(relativeOffset, Key.Up);
            _ = RaiseMouseWheel(relativeOffset, 120, preview: true);
            SettleWindow(editor);
            if (relativeOffset.Text != "15")
            {
                failures.Add($"提前分钟只读态被步进为「{relativeOffset.Text}」");
            }

            relativeOffset.IsReadOnly = false;
            relativeOffset.IsEnabled = false;
            _ = RaisePreviewKeyDown(relativeOffset, Key.Up);
            _ = RaiseMouseWheel(relativeOffset, 120, preview: true);
            SettleWindow(editor);
            if (relativeOffset.Text != "15")
            {
                failures.Add($"提前分钟禁用态被步进为「{relativeOffset.Text}」");
            }

            relativeOffset.IsEnabled = true;
        }
        finally
        {
            UiAccessibility.Announced -= Observe;
            CloseWindow(editor);
        }

        // 6. 派生时间早于当前北京时间在保存时仍以原错误文案拦截并挂错误边框
        var shortTargetEditor = CreateOffscreenEditor(
            Array.Empty<ReminderRule>(),
            new FixedClock(RuleEditorFixtureUtc),
            countdownTargetUtc: RuleEditorFixtureUtc.AddMinutes(30));
        try
        {
            ShowAndSettle(shortTargetEditor);
            shortTargetEditor.Activate();
            FindByAutomationId<ComboBox>(shortTargetEditor, "ReminderRuleMode").SelectedIndex = 2;
            var offsetBox = FindByAutomationId<TextBox>(shortTargetEditor, "ReminderRuleRelativeOffsetMinutes");
            var err = FindByAutomationId<TextBlock>(shortTargetEditor, "ReminderRuleRelativeOffsetError");
            offsetBox.Text = "60";
            SettleWindow(shortTargetEditor);
            InvokeButton(shortTargetEditor, FindByAutomationId<Button>(shortTargetEditor, "SaveReminderRuleButton"));
            if (!err.IsVisible ||
                !string.Equals(err.Text, ExpectedRelativeOffsetError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(offsetBox) ||
                !offsetBox.IsKeyboardFocusWithin)
            {
                failures.Add(
                    $"提前分钟派生时刻过去保存拦错状态异常（visible={err.IsVisible}, text=「{err.Text}」, hasError={InputErrorState.GetHasError(offsetBox)}, focused={offsetBox.IsKeyboardFocusWithin}）");
            }
        }
        finally
        {
            CloseWindow(shortTargetEditor);
        }

        // 7. 模态保存与越界拦错后恢复提交
        var clock = new FixedClock(RuleEditorFixtureUtc);
        var blockedAtOverflow = false;
        var modal = RunModal(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("33100002-0000-0000-0000-000000000002")),
            countdownTargetUtc: farTargetUtc,
            editorWindow =>
            {
                FindByAutomationId<ComboBox>(editorWindow, "ReminderRuleMode").SelectedIndex = 2;
                var offsetBox = FindByAutomationId<TextBox>(editorWindow, "ReminderRuleRelativeOffsetMinutes");
                offsetBox.Text = "5256001";
                SettleWindow(editorWindow);
                InvokeButton(editorWindow, FindByAutomationId<Button>(editorWindow, "AcceptReminderRulesButton"));
                var err = FindByAutomationId<TextBlock>(editorWindow, "ReminderRuleRelativeOffsetError");
                blockedAtOverflow = editorWindow.IsVisible && err.IsVisible &&
                    string.Equals(err.Text, ExpectedRelativeOffsetError, StringComparison.Ordinal) &&
                    offsetBox.Text == "5256001";
                offsetBox.Text = "15";
                SettleWindow(editorWindow);
                _ = RaisePreviewKeyDown(offsetBox, Key.Up); // -> 16
                _ = RaiseMouseWheel(offsetBox, 120, preview: true); // -> 17
                SettleWindow(editorWindow);
                InvokeButton(editorWindow, FindByAutomationId<Button>(editorWindow, "AcceptReminderRulesButton"));
            });
        if (!blockedAtOverflow || modal.DialogResult != true || modal.Rules.Count != 1 ||
            modal.Rules[0] is not CountdownRelativeReminderRule { OffsetMinutesBefore: 17 })
        {
            failures.Add(
                $"提前分钟模态拦错与步进提交失败（blocked={blockedAtOverflow}, dialog={modal.DialogResult}, count={modal.Rules.Count}）");
        }
    }

    private static void CheckRuleEditorVisualContrastHighContrastAndLabels(List<string> failures)
    {
        var editor = CreateOffscreenEditor(
            Array.Empty<ReminderRule>(),
            new FixedClock(RuleEditorFixtureUtc),
            countdownTargetUtc: RuleEditorFixtureUtc.AddYears(11));
        try
        {
            ShowAndSettle(editor);
            editor.Activate();

            var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
            var calendarInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCalendarInterval");
            var calendarError = FindByAutomationId<TextBlock>(editor, "ReminderRuleCalendarIntervalError");
            var relativeOffset = FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes");
            var relativeError = FindByAutomationId<TextBlock>(editor, "ReminderRuleRelativeOffsetError");
            var saveButton = FindByAutomationId<Button>(editor, "SaveReminderRuleButton");

            // 检查重复间隔（每隔几个周期）
            mode.SelectedIndex = 3;
            calendarInterval.Text = "2";
            Keyboard.Focus(saveButton);
            SettleWindow(editor);
            CheckTextBoxVisualAndAccessibilityContract(
                editor,
                (Visual)editor.Content,
                calendarInterval,
                calendarError,
                saveButton,
                expectedLabelText: "每隔几个周期",
                expectedAutomationName: "每隔几个周期",
                validText: "2",
                invalidText: "0",
                surfaceBrushKey: "CanvasBrush",
                fieldLabel: "规则编辑器每隔几个周期",
                failures);

            // 检查提前分钟
            mode.SelectedIndex = 2;
            relativeOffset.Text = "15";
            Keyboard.Focus(saveButton);
            SettleWindow(editor);
            CheckTextBoxVisualAndAccessibilityContract(
                editor,
                (Visual)editor.Content,
                relativeOffset,
                relativeError,
                saveButton,
                expectedLabelText: "提前分钟",
                expectedAutomationName: "倒计时提前分钟",
                validText: "15",
                invalidText: "5256001",
                surfaceBrushKey: "CanvasBrush",
                fieldLabel: "规则编辑器提前分钟",
                failures);

            CheckZeroWpfUiPollution(editor, "规则编辑器", failures);
        }
        finally
        {
            CloseWindow(editor);
        }
    }

    private static void CheckMonthlySalaryInputValidationVisualAndDownstream(List<string> failures)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-salary-331-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var diagnosticPath = Path.Combine(tempRoot, "diagnostic.jsonl");
        var announcements = new List<(string Id, string Name, string Text, AutomationLiveSetting Live)>();
        void Observe(TextBlock block) => announcements.Add((
            AutomationProperties.GetAutomationId(block) ?? string.Empty,
            block.Name ?? string.Empty,
            AutomationProperties.GetName(block) ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));

        MainWindow? main = null;
        FloatingWindow? floating = null;
        UiAccessibility.Announced += Observe;
        try
        {
            var clock = new FixedClock(SalaryFixtureUtc);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var initialPrefs = workspace.Snapshot.Settings.Preferences;
            var seedResult = workspace.Apply(new SavePreferencesChange(
                initialPrefs.DisplayPrecision,
                FixtureMonthlySalary,
                new TimeSpan(9, 0, 0),
                new TimeSpan(18, 0, 0)));
            if (!seedResult.Succeeded)
            {
                failures.Add($"初始化月薪夹具失败：{seedResult.Error}");
                return;
            }

            var year2030Json = BuildWeekdayCalendarJson(2030);
            using var calendar = new HolidayCalendar(
                tempRoot,
                (year, _) => year == 2030
                    ? Task.FromResult(year2030Json)
                    : Task.FromException<string>(new HttpRequestException($"no fixture for {year}")));

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
                Opacity = 0,
                Topmost = false
            };

            ShowRenderedWindow(main, "管理窗口");
            ShowRenderedWindow(floating, "悬浮窗");
            WaitForTask(calendar.RefreshAsync(SalaryFixtureUtc, CancellationToken.None), floating);
            floating.UpdateTime(SalaryFixtureUtc);
            SettleWindow(floating);

            var preferencesButton = Required<Button>(main, "PreferencesButton");
            var preferencesPopup = Required<Popup>(main, "PreferencesPopup");
            var preferencesSurface = Required<Border>(main, "CountdownPreferencesSurface");
            var monthlySalary = Required<TextBox>(main, "MonthlySalary");
            var monthlySalaryError = Required<TextBlock>(main, "MonthlySalaryError");
            var workStart = Required<TextBox>(main, "WorkStart");
            var savePreferences = Required<Button>(main, "SavePreferencesButton");
            var statusText = Required<TextBlock>(main, "StatusText");

            var floatingIncome = Required<TextBlock>(floating, "FloatingIncome");
            var floatingEncouragement = Required<SilentTextBlock>(floating, "FloatingEncouragement");

            // 确认初始正值月薪在悬浮窗显示今日已赚
            if (floatingIncome.Visibility != Visibility.Visible ||
                !string.Equals(floatingIncome.Text, ExpectedQuarterDayEarningsText, StringComparison.Ordinal))
            {
                failures.Add(
                    $"初始正值月薪下悬浮窗未显示预期今日已赚（visible={floatingIncome.Visibility}, text=「{floatingIncome.Text}」）");
            }

            // 打开显示设置弹层
            OpenPreferencesPopup(main, preferencesButton, preferencesPopup, failures);
            if (!preferencesPopup.IsOpen)
            {
                return;
            }

            // 1. 初始 N2 格式（千位分隔 + 两位小数）+ IME 关闭 + 表格数字
            var expectedInitialText = FixtureMonthlySalary.ToString("N2", CultureInfo.CurrentCulture);
            if (!string.Equals(monthlySalary.Text, expectedInitialText, StringComparison.Ordinal))
            {
                failures.Add($"月薪初始显示未按本地 N2 格式化（expected=「{expectedInitialText}」, actual=「{monthlySalary.Text}」）");
            }

            if (InputMethod.GetIsInputMethodEnabled(monthlySalary))
            {
                failures.Add("月薪输入框未关闭 IME");
            }

            if (Typography.GetNumeralAlignment(monthlySalary) != FontNumeralAlignment.Tabular)
            {
                failures.Add($"月薪输入框未使用表格数字对齐（actual={Typography.GetNumeralAlignment(monthlySalary)}）");
            }

            // 2. 未获焦时滚轮不得误改月薪
            Keyboard.Focus(savePreferences);
            SettleWindow(main);
            var unfocusedWheelHandled = RaiseMouseWheel(monthlySalary, 120, preview: true);
            SettleWindow(main);
            if (unfocusedWheelHandled || !string.Equals(monthlySalary.Text, expectedInitialText, StringComparison.Ordinal))
            {
                failures.Add($"月薪未获焦时滚轮误触发步进（handled={unfocusedWheelHandled}, text=「{monthlySalary.Text}」）");
            }

            // 3. 获焦后上下键与滚轮步进（保持两位小数与千位分隔）
            Keyboard.Focus(monthlySalary);
            SettleWindow(main);

            var upHandled = RaisePreviewKeyDown(monthlySalary, Key.Up);
            SettleWindow(main);
            var expectedPlusOne = (FixtureMonthlySalary + 1m).ToString("N2", CultureInfo.CurrentCulture);
            if (!upHandled || !string.Equals(monthlySalary.Text, expectedPlusOne, StringComparison.Ordinal))
            {
                failures.Add($"月薪按 Up 未按 N2 步进（handled={upHandled}, expected=「{expectedPlusOne}」, actual=「{monthlySalary.Text}」）");
            }

            var downHandled = RaisePreviewKeyDown(monthlySalary, Key.Down);
            SettleWindow(main);
            if (!downHandled || !string.Equals(monthlySalary.Text, expectedInitialText, StringComparison.Ordinal))
            {
                failures.Add($"月薪按 Down 未回退原 N2 值（handled={downHandled}, actual=「{monthlySalary.Text}」）");
            }

            var wheelUpHandled = RaiseMouseWheel(monthlySalary, 120, preview: true);
            SettleWindow(main);
            var wheelDoubleDownHandled = RaiseMouseWheel(monthlySalary, -240, preview: true);
            SettleWindow(main);
            var expectedMinusOne = (FixtureMonthlySalary - 1m).ToString("N2", CultureInfo.CurrentCulture);
            if (!wheelUpHandled || !wheelDoubleDownHandled ||
                !string.Equals(monthlySalary.Text, expectedMinusOne, StringComparison.Ordinal))
            {
                failures.Add($"月薪获焦滚轮步进异常（up={wheelUpHandled}, doubleDown={wheelDoubleDownHandled}, expected=「{expectedMinusOne}」, actual=「{monthlySalary.Text}」）");
            }

            // 跨千位边界步进 + 失焦按 N2 格式化
            monthlySalary.Text = "999.50";
            SettleWindow(main);
            _ = RaisePreviewKeyDown(monthlySalary, Key.Up);
            SettleWindow(main);
            var expectedThousand = 1000.50m.ToString("N2", CultureInfo.CurrentCulture);
            if (!string.Equals(monthlySalary.Text, expectedThousand, StringComparison.Ordinal))
            {
                failures.Add($"月薪跨千位步进未带千位分隔（expected=「{expectedThousand}」, actual=「{monthlySalary.Text}」）");
            }

            monthlySalary.Text = "12345.6";
            SettleWindow(main);
            Keyboard.Focus(workStart);
            SettleWindow(main);
            var expectedBlurred = 12345.60m.ToString("N2", CultureInfo.CurrentCulture);
            if (!string.Equals(monthlySalary.Text, expectedBlurred, StringComparison.Ordinal))
            {
                failures.Add($"月薪失焦未按本地 N2 格式化（expected=「{expectedBlurred}」, actual=「{monthlySalary.Text}」）");
            }

            // 下限 0 钳位 + 空白起步 0.00
            Keyboard.Focus(monthlySalary);
            SettleWindow(main);
            monthlySalary.Text = "0.50";
            SettleWindow(main);
            _ = RaisePreviewKeyDown(monthlySalary, Key.Down);
            SettleWindow(main);
            var expectedZero = 0m.ToString("N2", CultureInfo.CurrentCulture);
            if (!string.Equals(monthlySalary.Text, expectedZero, StringComparison.Ordinal) || monthlySalaryError.IsVisible)
            {
                failures.Add($"月薪 0.50 按 Down 未钳位到 0.00（actual=「{monthlySalary.Text}」, errorVisible={monthlySalaryError.IsVisible}）");
            }

            _ = RaisePreviewKeyDown(monthlySalary, Key.Down);
            SettleWindow(main);
            if (!string.Equals(monthlySalary.Text, expectedZero, StringComparison.Ordinal))
            {
                failures.Add($"月薪 0.00 按 Down 未保持 0.00（actual=「{monthlySalary.Text}」）");
            }

            monthlySalary.Text = string.Empty;
            SettleWindow(main);
            _ = RaisePreviewKeyDown(monthlySalary, Key.Up);
            SettleWindow(main);
            if (!string.Equals(monthlySalary.Text, expectedZero, StringComparison.Ordinal))
            {
                failures.Add($"月薪空白按 Up 未落到 0.00（actual=「{monthlySalary.Text}」）");
            }

            // 4. 交互过滤：拦截字母、负号、空格、第 3 位小数、第 2 个小数点；允许合法小数与千位分隔
            monthlySalary.Text = "12.34";
            monthlySalary.CaretIndex = monthlySalary.Text.Length;
            SettleWindow(main);
            foreach (var rejected in new[] { "a", "-", " ", "5", "." })
            {
                if (!RaisePreviewTextInput(monthlySalary, rejected))
                {
                    failures.Add($"月薪未拦截非法交互输入「{rejected}」（当前文本「{monthlySalary.Text}」）");
                }
            }

            if (!RaisePreviewKeyDown(monthlySalary, Key.Space))
            {
                failures.Add("月薪未拦截空格键");
            }

            monthlySalary.Text = "12.3";
            monthlySalary.CaretIndex = monthlySalary.Text.Length;
            SettleWindow(main);
            if (RaisePreviewTextInput(monthlySalary, "4"))
            {
                failures.Add("月薪错误拦截了合法第 2 位小数「4」");
            }

            monthlySalary.Text = "1234";
            monthlySalary.CaretIndex = monthlySalary.Text.Length;
            SettleWindow(main);
            if (RaisePreviewTextInput(monthlySalary, "."))
            {
                failures.Add("月薪错误拦截了小数点「.」");
            }

            monthlySalary.Text = string.Empty;
            SettleWindow(main);
            foreach (var rejectedPaste in new[] { "abc", "-500", "12.345" })
            {
                if (!RaisePasting(monthlySalary, rejectedPaste))
                {
                    failures.Add($"月薪未拦截非法粘贴文本「{rejectedPaste}」");
                }
            }

            if (RaisePasting(monthlySalary, "12,345.67"))
            {
                failures.Add("月薪错误拦截了带千位分隔的合法粘贴文本「12,345.67」");
            }

            // 5. 只读与禁用不响应步进
            monthlySalary.Text = expectedInitialText;
            SettleWindow(main);
            monthlySalary.IsReadOnly = true;
            _ = RaisePreviewKeyDown(monthlySalary, Key.Up);
            _ = RaiseMouseWheel(monthlySalary, 120, preview: true);
            SettleWindow(main);
            if (!string.Equals(monthlySalary.Text, expectedInitialText, StringComparison.Ordinal))
            {
                failures.Add($"月薪只读态被步进为「{monthlySalary.Text}」");
            }

            monthlySalary.IsReadOnly = false;
            monthlySalary.IsEnabled = false;
            _ = RaisePreviewKeyDown(monthlySalary, Key.Up);
            _ = RaiseMouseWheel(monthlySalary, 120, preview: true);
            SettleWindow(main);
            if (!string.Equals(monthlySalary.Text, expectedInitialText, StringComparison.Ordinal))
            {
                failures.Add($"月薪禁用态被步进为「{monthlySalary.Text}」");
            }

            monthlySalary.IsEnabled = true;

            // 6. 越界负数 "-500" 当场报错（精确文案、位置、InputErrorState、HelpText、播报、失焦不覆写）+ 保存拦截 + Up 恢复清错
            announcements.Clear();
            Keyboard.Focus(monthlySalary);
            monthlySalary.Text = "-500";
            SettleWindow(main);
            var errorBelowField =
                monthlySalaryError.TranslatePoint(new Point(), preferencesSurface).Y >=
                monthlySalary.TranslatePoint(new Point(0, monthlySalary.ActualHeight), preferencesSurface).Y - 0.5;
            if (!monthlySalaryError.IsVisible ||
                !string.Equals(monthlySalaryError.Text, ExpectedSalaryNegativeError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(monthlySalary) ||
                !string.Equals(AutomationProperties.GetHelpText(monthlySalary), ExpectedSalaryNegativeError, StringComparison.Ordinal) ||
                !errorBelowField ||
                monthlySalary.Text != "-500" ||
                !announcements.Any(a =>
                    (a.Id == "MonthlySalaryError" || a.Name == "MonthlySalaryError") &&
                    a.Text == ExpectedSalaryNegativeError &&
                    a.Live == AutomationLiveSetting.Polite))
            {
                failures.Add(
                    $"月薪输入 -500 未当场报错或状态不同步（visible={monthlySalaryError.IsVisible}, text=「{monthlySalaryError.Text}」, hasError={InputErrorState.GetHasError(monthlySalary)}, help=「{AutomationProperties.GetHelpText(monthlySalary)}」, below={errorBelowField}, input=「{monthlySalary.Text}」）");
            }

            // 失焦不得覆盖越界草稿
            Keyboard.Focus(workStart);
            SettleWindow(main);
            if (monthlySalary.Text != "-500" || !monthlySalaryError.IsVisible || !InputErrorState.GetHasError(monthlySalary))
            {
                failures.Add($"月薪越界草稿 -500 在失焦后被覆写或清错（input=「{monthlySalary.Text}」）");
            }

            // 点击保存拦截并镜像底栏且聚焦月薪
            savePreferences.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleWindow(main);
            if (workspace.Snapshot.Settings.Preferences.MonthlySalary != FixtureMonthlySalary ||
                !string.Equals(statusText.Text, ExpectedSalaryNegativeError, StringComparison.Ordinal) ||
                !monthlySalary.IsKeyboardFocusWithin ||
                !monthlySalaryError.IsVisible ||
                !InputErrorState.GetHasError(monthlySalary) ||
                monthlySalary.Text != "-500")
            {
                failures.Add(
                    $"月薪 -500 保存未按原契约拦截（saved={workspace.Snapshot.Settings.Preferences.MonthlySalary}, status=「{statusText.Text}」, focused={monthlySalary.IsKeyboardFocusWithin}, input=「{monthlySalary.Text}」）");
            }

            // 按 Up 从 -500 恢复为 0.00 并当场清错
            _ = RaisePreviewKeyDown(monthlySalary, Key.Up);
            SettleWindow(main);
            if (!string.Equals(monthlySalary.Text, expectedZero, StringComparison.Ordinal) ||
                monthlySalaryError.IsVisible ||
                InputErrorState.GetHasError(monthlySalary) ||
                !string.IsNullOrEmpty(AutomationProperties.GetHelpText(monthlySalary)))
            {
                failures.Add($"月薪从 -500 按 Up 未恢复 0.00 并清错（text=「{monthlySalary.Text}」, errorVisible={monthlySalaryError.IsVisible}, hasError={InputErrorState.GetHasError(monthlySalary)}）");
            }

            // 7. 非法文本 "not-a-number" 当场报错 + 保存拦截 + 获焦滚轮恢复清错
            monthlySalary.Text = "not-a-number";
            SettleWindow(main);
            if (!monthlySalaryError.IsVisible ||
                !string.Equals(monthlySalaryError.Text, ExpectedSalaryFormatError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(monthlySalary) ||
                monthlySalary.Text != "not-a-number")
            {
                failures.Add($"月薪输入 not-a-number 未当场报错（visible={monthlySalaryError.IsVisible}, text=「{monthlySalaryError.Text}」）");
            }

            savePreferences.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleWindow(main);
            if (!string.Equals(statusText.Text, ExpectedSalaryFormatError, StringComparison.Ordinal) ||
                !monthlySalary.IsKeyboardFocusWithin ||
                !InputErrorState.GetHasError(monthlySalary))
            {
                failures.Add($"月薪 not-a-number 保存未按原契约拦截（status=「{statusText.Text}」）");
            }

            _ = RaiseMouseWheel(monthlySalary, 120, preview: true);
            SettleWindow(main);
            if (!string.Equals(monthlySalary.Text, expectedZero, StringComparison.Ordinal) ||
                monthlySalaryError.IsVisible ||
                InputErrorState.GetHasError(monthlySalary))
            {
                failures.Add($"月薪从 not-a-number 获焦滚轮上滑未恢复 0.00 并清错（text=「{monthlySalary.Text}」）");
            }

            // 空白文本允许清空重输，点保存时仍以「月薪格式无效。」拦截并挂错误边框
            monthlySalary.Text = string.Empty;
            SettleWindow(main);
            if (monthlySalaryError.IsVisible || InputErrorState.GetHasError(monthlySalary))
            {
                failures.Add("月薪清空重输时不应立即闪现错误");
            }

            savePreferences.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleWindow(main);
            if (!monthlySalaryError.IsVisible ||
                !string.Equals(monthlySalaryError.Text, ExpectedSalaryFormatError, StringComparison.Ordinal) ||
                !string.Equals(statusText.Text, ExpectedSalaryFormatError, StringComparison.Ordinal) ||
                !InputErrorState.GetHasError(monthlySalary))
            {
                failures.Add($"月薪留空点保存未拦截或未挂错误边框（visible={monthlySalaryError.IsVisible}, text=「{monthlySalaryError.Text}」, hasError={InputErrorState.GetHasError(monthlySalary)}）");
            }

            // 8. 视觉焦点、错误边框、零布局抖动、对比度、系统对比度与 LabeledBy 标签关系
            monthlySalary.Text = expectedInitialText;
            Keyboard.Focus(savePreferences);
            SettleWindow(main);
            CheckTextBoxVisualAndAccessibilityContract(
                main,
                preferencesSurface,
                monthlySalary,
                monthlySalaryError,
                savePreferences,
                expectedLabelText: "月薪（元）",
                expectedAutomationName: null,
                validText: expectedInitialText,
                invalidText: "-500",
                surfaceBrushKey: "PanelBrush",
                fieldLabel: "主窗口显示设置月薪",
                failures,
                hoverViaPopup: true);

            // 9. 保存 0 月薪 -> 下游悬浮窗两工作区均显示鼓励语（不显示今日已赚），再 Enter 保存正值恢复今日已赚
            if (!preferencesPopup.IsOpen)
            {
                OpenPreferencesPopup(main, preferencesButton, preferencesPopup, failures);
            }

            monthlySalary.Text = expectedZero;
            SettleWindow(main);
            savePreferences.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleWindow(main);
            floating.UpdateTime(SalaryFixtureUtc);
            SettleWindow(floating);
            if (workspace.Snapshot.Settings.Preferences.MonthlySalary != 0m ||
                !string.Equals(monthlySalary.Text, expectedZero, StringComparison.Ordinal) ||
                !string.Equals(statusText.Text, "设置已保存", StringComparison.Ordinal))
            {
                failures.Add(
                    $"保存 0 月薪失败（saved={workspace.Snapshot.Settings.Preferences.MonthlySalary}, text=「{monthlySalary.Text}」, status=「{statusText.Text}」）");
            }

            if (floatingIncome.Visibility == Visibility.Visible ||
                floatingEncouragement.Visibility != Visibility.Visible ||
                !string.Equals(floatingEncouragement.Text, FloatingBottomStatus.CountdownEncouragementText, StringComparison.Ordinal))
            {
                failures.Add(
                    $"月薪保存为 0 后悬浮窗倒计时工作区未显示鼓励语（incomeVisible={floatingIncome.Visibility}, encVisible={floatingEncouragement.Visibility}, encText=「{floatingEncouragement.Text}」）");
            }

            floating.ShowWorkspace(WorkspaceKind.Notes);
            floating.UpdateTime(SalaryFixtureUtc);
            SettleWindow(floating);
            if (floatingIncome.Visibility == Visibility.Visible ||
                floatingEncouragement.Visibility != Visibility.Visible ||
                !string.Equals(floatingEncouragement.Text, FloatingBottomStatus.NotesEncouragementText, StringComparison.Ordinal))
            {
                failures.Add(
                    $"月薪保存为 0 后悬浮窗便签工作区未显示鼓励语（incomeVisible={floatingIncome.Visibility}, encVisible={floatingEncouragement.Visibility}, encText=「{floatingEncouragement.Text}」）");
            }

            // 通过月薪框 Enter 局部提交恢复正值月薪，确认今日已赚恢复
            floating.ShowWorkspace(WorkspaceKind.Countdowns);
            Keyboard.Focus(monthlySalary);
            monthlySalary.Text = expectedInitialText;
            SettleWindow(main);
            var enterHandled = RaisePreviewKeyDown(monthlySalary, Key.Enter);
            SettleWindow(main);
            floating.UpdateTime(SalaryFixtureUtc);
            SettleWindow(floating);
            if (!enterHandled ||
                workspace.Snapshot.Settings.Preferences.MonthlySalary != FixtureMonthlySalary ||
                !string.Equals(monthlySalary.Text, expectedInitialText, StringComparison.Ordinal) ||
                floatingIncome.Visibility != Visibility.Visible ||
                !string.Equals(floatingIncome.Text, ExpectedQuarterDayEarningsText, StringComparison.Ordinal))
            {
                failures.Add(
                    $"月薪框按 Enter 提交正值未恢复今日已赚（enter={enterHandled}, saved={workspace.Snapshot.Settings.Preferences.MonthlySalary}, income=「{floatingIncome.Text}」）");
            }

            CheckZeroWpfUiPollution(main, "管理窗口", failures);
        }
        finally
        {
            UiAccessibility.Announced -= Observe;
            if (floating is not null)
            {
                CloseWindow(floating);
            }

            if (main is not null)
            {
                CloseWindow(main);
            }

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理由系统回收
            }
        }
    }

    private static void CheckTextBoxVisualAndAccessibilityContract(
        Window hostWindow,
        Visual geometryRoot,
        TextBox input,
        TextBlock errorText,
        Button fallbackFocusTarget,
        string expectedLabelText,
        string? expectedAutomationName,
        string validText,
        string invalidText,
        string surfaceBrushKey,
        string fieldLabel,
        List<string> failures,
        bool hoverViaPopup = false)
    {
        if (InputMethod.GetIsInputMethodEnabled(input))
        {
            failures.Add($"{fieldLabel}未关闭 IME");
        }

        if (Typography.GetNumeralAlignment(input) != FontNumeralAlignment.Tabular)
        {
            failures.Add($"{fieldLabel}未使用表格数字对齐（actual={Typography.GetNumeralAlignment(input)}）");
        }

        input.ApplyTemplate();
        if (input.FocusVisualStyle is not null)
        {
            failures.Add($"{fieldLabel}未关闭外层 FocusVisualStyle（存在双层焦点框风险）");
        }

        var surface = input.Template?.FindName("Surface", input) as Border;
        var contentHost = input.Template?.FindName("PART_ContentHost", input) as FrameworkElement;
        if (surface is null || contentHost is null)
        {
            failures.Add($"{fieldLabel}缺少共享暖粉 TextBox 模板部件 Surface / PART_ContentHost");
            return;
        }

        var expectedRadius = (CornerRadius)Application.Current.FindResource("RadiusControl");
        if (surface.CornerRadius != expectedRadius)
        {
            failures.Add($"{fieldLabel}圆角期望 {expectedRadius}，实际 {surface.CornerRadius}");
        }

        // LabeledBy 与 AutomationPeer 标签关系
        var labeledBy = AutomationProperties.GetLabeledBy(input);
        if (labeledBy is not TextBlock labelBlock ||
            !labelBlock.IsVisible ||
            !string.Equals(labelBlock.Text, expectedLabelText, StringComparison.Ordinal))
        {
            failures.Add(
                $"{fieldLabel} 的 LabeledBy 未指向可见标签「{expectedLabelText}」（actual={labeledBy?.GetType().Name ?? "null"}/{(labeledBy as TextBlock)?.Text ?? ""}）");
        }

        if (expectedAutomationName is not null &&
            !string.Equals(AutomationProperties.GetName(input), expectedAutomationName, StringComparison.Ordinal))
        {
            failures.Add($"{fieldLabel}可访问名变更：{AutomationProperties.GetName(input)}");
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(input);
        var peerLabel = peer?.GetLabeledBy();
        if (peerLabel is null || !string.Equals(peerLabel.GetName(), expectedLabelText, StringComparison.Ordinal))
        {
            failures.Add($"{fieldLabel} 的 AutomationPeer.GetLabeledBy 未解析到「{expectedLabelText}」（actual={peerLabel?.GetName() ?? "null"}）");
        }

        // 对比度基线（文字 >= 4.5:1，边框 >= 3.0:1）
        var surfaceBrush = (Brush)Application.Current.FindResource(surfaceBrushKey);
        var inkBrush = (Brush)Application.Current.FindResource("InkBrush");
        var controlBorderBrush = (Brush)Application.Current.FindResource("ControlBorderBrush");
        var focusBrush = (Brush)Application.Current.FindResource("MainActionFocusBrush");
        var dangerBrush = (Brush)Application.Current.FindResource("DangerBrush");
        if (!ApplicationTheme.MeetsContrast(surfaceBrush, inkBrush, 4.5) ||
            !ApplicationTheme.MeetsContrast(surfaceBrush, dangerBrush, 4.5) ||
            !ApplicationTheme.MeetsContrast(surfaceBrush, controlBorderBrush, 3.0) ||
            !ApplicationTheme.MeetsContrast(surfaceBrush, focusBrush, 3.0) ||
            !ApplicationTheme.MeetsContrast(surfaceBrush, dangerBrush, 3.0))
        {
            failures.Add($"{fieldLabel}在日间主题下未满足对比度基线（surface={surfaceBrushKey}）");
        }

        // 常态、悬停、有效焦点、出错有焦点、出错无焦点与零布局抖动
        var restingInputBounds = Bounds(input, geometryRoot);
        var restingHostBounds = Bounds(contentHost, geometryRoot);
        var restingButtonBounds = Bounds(fallbackFocusTarget, geometryRoot);
        AssertBorderState(surface, "ControlBorderBrush", 1, $"{fieldLabel}日间常态无焦点", failures);

        WithPointerHover(hostWindow, input, hoverViaPopup, () =>
        {
            if (!input.IsMouseOver)
            {
                failures.Add($"{fieldLabel}悬停探针未命中输入框");
                return;
            }

            AssertBorderState(surface, "ActiveHoverBrush", 1, $"{fieldLabel}日间悬停无焦点", failures);
        });

        Keyboard.Focus(input);
        SettleWindow(hostWindow);
        if (!input.IsKeyboardFocused)
        {
            failures.Add($"{fieldLabel}未能取得键盘焦点");
        }

        AssertBorderState(surface, "MainActionFocusBrush", 2, $"{fieldLabel}日间有效有焦点", failures);
        if (BrushMatches(surface.BorderBrush, (Brush)Application.Current.FindResource("ActiveBrush")) ||
            BrushMatches(surface.BorderBrush, dangerBrush))
        {
            failures.Add($"{fieldLabel}有效焦点仍与 ActiveBrush / DangerBrush 混用");
        }

        if (Bounds(input, geometryRoot) != restingInputBounds ||
            Bounds(contentHost, geometryRoot) != restingHostBounds ||
            Bounds(fallbackFocusTarget, geometryRoot) != restingButtonBounds)
        {
            failures.Add($"{fieldLabel}有效焦点切换挤动了布局");
        }

        // 出错有焦点：2 DIP DangerBrush，不挤动输入槽
        input.Text = invalidText;
        SettleWindow(hostWindow);
        if (!errorText.IsVisible || !InputErrorState.GetHasError(input))
        {
            failures.Add($"{fieldLabel}输入越界值「{invalidText}」未显示错误或未挂错误边框");
        }

        AssertBorderState(surface, "DangerBrush", 2, $"{fieldLabel}日间出错有焦点", failures);
        if (Bounds(input, geometryRoot) != restingInputBounds ||
            Bounds(contentHost, geometryRoot) != restingHostBounds)
        {
            failures.Add($"{fieldLabel}出错有焦点挤动了输入槽几何");
        }

        // 出错无焦点：1 DIP DangerBrush，不挤动输入槽
        Keyboard.Focus(fallbackFocusTarget);
        SettleWindow(hostWindow);
        AssertBorderState(surface, "DangerBrush", 1, $"{fieldLabel}日间出错无焦点", failures);
        if (Bounds(input, geometryRoot) != restingInputBounds ||
            Bounds(contentHost, geometryRoot) != restingHostBounds)
        {
            failures.Add($"{fieldLabel}出错无焦点挤动了输入槽几何");
        }

        // 系统对比度主题
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
            SettleWindow(hostWindow);

            AssertBorderState(surface, "DangerBrush", 1, $"{fieldLabel}系统对比度出错无焦点", failures);
            if (!BrushMatches(surface.BorderBrush, new SolidColorBrush(Colors.White)) || !errorText.IsVisible)
            {
                failures.Add($"{fieldLabel}系统对比度下错误未映射 WindowText 或缺少错误文字");
            }

            Keyboard.Focus(input);
            SettleWindow(hostWindow);
            AssertBorderState(surface, "DangerBrush", 2, $"{fieldLabel}系统对比度出错有焦点", failures);

            input.Text = validText;
            SettleWindow(hostWindow);
            AssertBorderState(surface, "MainActionFocusBrush", 2, $"{fieldLabel}系统对比度有效有焦点", failures);
            if (!BrushMatches(surface.BorderBrush, new SolidColorBrush(Colors.Yellow)))
            {
                failures.Add($"{fieldLabel}系统对比度下有效焦点未映射 Highlight 黄色");
            }
        }

        SettleWindow(hostWindow);
        AssertBorderState(surface, "MainActionFocusBrush", 2, $"{fieldLabel}退出系统对比度后恢复暖金焦点", failures);
        if (!BrushMatches(surface.BorderBrush, new SolidColorBrush(ApplicationTheme.DayColorFor("MainActionFocusBrush"))))
        {
            failures.Add($"{fieldLabel}退出系统对比度后焦点色未恢复暖粉日间暖金");
        }
    }

    private static void CheckZeroWpfUiPollution(Window window, string label, List<string> failures)
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

        void ScanDictionary(ResourceDictionary rootDictionary, string scopeLabel)
        {
            var queue = new Queue<ResourceDictionary>();
            queue.Enqueue(rootDictionary);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current is Wpf.Ui.Markup.ThemesDictionary or Wpf.Ui.Markup.ControlsDictionary ||
                    current.Source?.OriginalString.Contains("Wpf.Ui", StringComparison.OrdinalIgnoreCase) == true)
                {
                    failures.Add($"WPF-UI 资源字典进入了{scopeLabel}：{current.GetType().FullName}");
                }

                foreach (var key in forbiddenKeys)
                {
                    if (current.Contains(key))
                    {
                        failures.Add($"WPF-UI 资源键进入了{scopeLabel}：{key}");
                    }
                }

                foreach (var merged in current.MergedDictionaries)
                {
                    queue.Enqueue(merged);
                }
            }
        }

        ScanDictionary(Application.Current.Resources, "应用级资源");
        ScanDictionary(window.Resources, $"{label}资源");
    }

    private static void OpenPreferencesPopup(
        MainWindow main,
        Button preferencesButton,
        Popup preferencesPopup,
        List<string> failures)
    {
        WindowValidationInput.ClosePopupForCleanup(preferencesPopup);
        main.Activate();
        Keyboard.Focus(preferencesButton);
        preferencesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleWindow(main);
        if (!preferencesPopup.IsOpen || PopupMotionBehavior.IsClosing(preferencesPopup))
        {
            failures.Add("无法打开主窗口显示设置弹层");
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

    private static bool RaisePasting(UIElement target, string text)
    {
        var dataObject = new DataObject(DataFormats.UnicodeText, text);
        var args = new DataObjectPastingEventArgs(dataObject, isDragDrop: false, DataFormats.UnicodeText);
        target.RaiseEvent(args);
        return args.CommandCancelled;
    }

    private static void WithPointerHover(Window window, Control target, bool hoverViaPopup, Action verify)
    {
        SettleWindow(window);
        if (PresentationSource.FromVisual(target) is not HwndSource source ||
            source.RootVisual is not Visual root)
        {
            throw new InvalidOperationException("悬停探针缺少 HWND。");
        }

        var point = target.TransformToAncestor(root).Transform(
            new Point(Math.Min(target.RenderSize.Width * 0.4, 24), target.RenderSize.Height / 2));
        point = source.CompositionTarget.TransformToDevice.Transform(point);
        var coordinates = new IntPtr(((int)point.Y << 16) | ((int)point.X & 0xffff));
        var previousCapture = GetCapture();
        var acquiredCapture = false;
        if (!hoverViaPopup)
        {
            window.Activate();
            SetCapture(source.Handle);
            acquiredCapture = true;
            if (GetCapture() != source.Handle)
            {
                throw new InvalidOperationException("隔离验证窗口无法捕获鼠标移动消息。");
            }
        }
        else if (previousCapture != source.Handle)
        {
            SetCapture(source.Handle);
            acquiredCapture = true;
        }

        try
        {
            SendMessage(source.Handle, 0x0200, IntPtr.Zero, coordinates);
            SettleWindow(window);
            verify();
        }
        finally
        {
            if (hoverViaPopup)
            {
                SendMessage(source.Handle, 0x0200, IntPtr.Zero, new IntPtr((1 << 16) | 1));
            }

            if (acquiredCapture && GetCapture() == source.Handle)
            {
                if (hoverViaPopup && previousCapture != IntPtr.Zero)
                {
                    SetCapture(previousCapture);
                }
                else
                {
                    ReleaseCapture();
                }
            }

            SendMessage(source.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
            SettleWindow(window);
        }
    }

    private static bool BrushMatches(Brush? actual, Brush expected) =>
        ReferenceEquals(actual, expected) ||
        (actual is SolidColorBrush a && expected is SolidColorBrush b && a.Color == b.Color);

    private static ModalEditorResult RunModal(
        IReadOnlyList<ReminderRule> initialRules,
        IClock clock,
        IGuidSource? guidSource,
        DateTimeOffset? countdownTargetUtc,
        Action<ReminderRulesEditorWindow> interact)
    {
        var editor = CreateOffscreenEditor(initialRules, clock, guidSource, countdownTargetUtc);
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
        IGuidSource? guidSource = null,
        DateTimeOffset? countdownTargetUtc = null)
    {
        return new ReminderRulesEditorWindow(rules, clock, guidSource, countdownTargetUtc: countdownTargetUtc)
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

    private static void ShowAndSettle(Window window)
    {
        window.Show();
        SettleWindow(window);
    }

    private static void ShowRenderedWindow(Window window, string label)
    {
        var rendered = false;
        EventHandler handler = (_, _) => rendered = true;
        window.ContentRendered += handler;
        try
        {
            window.Show();
            var frame = WindowValidationInput.PumpRenderedFrames(window.Dispatcher, 2000, () => rendered);
            SettleWindow(window);
            if (!rendered || !frame.Completed)
            {
                throw new InvalidOperationException($"{label}未完成首帧渲染。");
            }
        }
        finally
        {
            window.ContentRendered -= handler;
        }
    }

    private static void WaitForTask(Task task, Window window)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            Thread.Sleep(10);
        }

        task.GetAwaiter().GetResult();
        SettleWindow(window);
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

    private static T Required<T>(Window window, string name) where T : FrameworkElement =>
        window.FindName(name) as T
        ?? throw new InvalidOperationException($"窗口缺少命名控件 {name} ({typeof(T).Name})。");

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

    private static string BuildWeekdayCalendarJson(int year)
    {
        var builder = new StringBuilder(16 * 1024);
        builder.Append("{\"days\":[");
        var date = new DateOnly(year, 1, 1);
        var first = true;
        while (date.Year == year)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            var isWorkday = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            builder.Append("{\"date\":\"")
                .Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append("\",\"is_workday\":")
                .Append(isWorkday ? "true" : "false")
                .Append('}');
            date = date.AddDays(1);
        }

        builder.Append("]}");
        return builder.ToString();
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

    private sealed record ModalEditorResult(bool? DialogResult, IReadOnlyList<ReminderRule> Rules);

    private sealed class ValidationGuidSource(params Guid[] values) : IGuidSource
    {
        private readonly Queue<Guid> _values = new(values);

        public Guid NewGuid() => _values.Count > 0 ? _values.Dequeue() : Guid.NewGuid();
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
