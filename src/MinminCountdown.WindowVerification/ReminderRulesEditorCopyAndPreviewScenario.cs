using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #323：规则编辑器字段日常化词表、可访问名与 LabeledBy 一致、一句话实时预览与保存摘要一致，
// 以及四类规则名称、类型化领域记录、规则 ID 与持久化 JSON 不变。
internal static class ReminderRulesEditorCopyAndPreviewScenario
{
    private const string IncompletePreviewText = "请完善上方设置以生成预览";
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    private static readonly string[] BannedJargonTerms =
    [
        "日历频率",
        "重复间隔",
        "日历选择",
        "日历重复频率",
        "日历重复间隔",
        "日历重复月份",
        "日历重复日期",
        "显式选择最后一天"
    ];

    internal static (bool Passed, string Explanation) RunPlainLanguageLabelsAndAccessibility(string meaning)
    {
        var failures = new List<string>();

        CheckLabelsAccessibilityAndNoJargon(failures);
        CheckDomainTypesRuleIdsAndCanonicalJsonUnchanged(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunLivePreviewMatchesSavedSummary(string meaning)
    {
        var failures = new List<string>();
        var captureEvidence = CreateEvidenceWriter();

        CheckLivePreviewAcrossModesAndSavedRows(failures, captureEvidence);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    private static void CheckLabelsAccessibilityAndNoJargon(List<string> failures)
    {
        var clock = new FixedClock(FixtureUtc);
        var editor = CreateOffscreenEditor(
            Array.Empty<ReminderRule>(),
            clock,
            countdownTargetUtc: FixtureUtc.AddDays(10));
        try
        {
            ShowAndSettle(editor);

            var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
            var expectedCountdownModes = new[] { "提醒一次", "每隔一段时间", "截止前提醒", "按日历重复" };
            if (!mode.Items.Cast<string>().SequenceEqual(expectedCountdownModes))
            {
                failures.Add($"倒计时规则类型列表变更：{string.Join(",", mode.Items.Cast<string>())}");
            }

            // 1. 逐模式核对：无实现用语，原有开始日期/首次提醒可访问名不变。
            mode.SelectedIndex = 0;
            SettleWindow(editor);
            AssertNoBannedJargon(editor, "提醒一次", failures);
            AssertLabeledByText(editor, mode, "规则类型", "提醒一次-规则类型", failures);
            AssertAutomationName(
                FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate"),
                "首次提醒日期",
                "提醒一次-日期名",
                failures);

            mode.SelectedIndex = 1;
            SettleWindow(editor);
            AssertNoBannedJargon(editor, "每隔一段时间", failures);

            mode.SelectedIndex = 2;
            SettleWindow(editor);
            AssertNoBannedJargon(editor, "截止前提醒", failures);
            AssertLabeledByText(
                editor,
                FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes"),
                "提前分钟",
                "截止前提醒-提前分钟",
                failures);

            // 2. 按日历重复：每日（不留空白“具体哪几天”标签）、每周、每月、每年
            mode.SelectedIndex = 3;
            var frequency = FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarFrequency");
            var interval = FindByAutomationId<TextBox>(editor, "ReminderRuleCalendarInterval");
            var selector = FindByAutomationId<StackPanel>(editor, "ReminderRuleCalendarSelector");
            var month = FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarMonth");
            var day = FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarDay");
            var lastDay = FindByAutomationId<CheckBox>(editor, "ReminderRuleCalendarLastDay");
            var preview = FindByAutomationId<TextBlock>(editor, "ReminderRulePreview");

            // 每日
            frequency.SelectedIndex = 0;
            SettleWindow(editor);
            AssertNoBannedJargon(editor, "日历-每天", failures);
            AssertVisibleFormLabels(editor, "日历-每天", ["规则类型", "开始日期", "重复周期", "每隔几个周期", "规则预览"], failures);
            AssertSingleLineFormLabels(editor, "日历-每天", failures);
            AssertAutomationName(
                FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate"),
                "开始日期",
                "日历-开始日期名",
                failures);
            var firstDueTimeCheck = FindByAutomationId<TextBox>(editor, "ReminderRuleFirstDueTime");
            AssertAutomationName(
                firstDueTimeCheck,
                "本地时刻",
                "日历-本地时刻名",
                failures);
            AssertLabeledByText(editor, firstDueTimeCheck, "开始日期", "日历-本地时刻标签关系", failures);
            AssertFieldLabelAndName(editor, frequency, "重复周期", "日历-重复周期", failures);
            AssertFieldLabelAndName(editor, interval, "每隔几个周期", "日历-每隔几个周期", failures);
            AssertLabeledByText(editor, preview, "规则预览", "日历-规则预览", failures);
            var previewPeerName = UIElementAutomationPeer.CreatePeerForElement(preview)?.GetName() ?? string.Empty;
            if (!string.Equals(previewPeerName, preview.Text, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(preview.Text))
            {
                failures.Add($"规则预览自动化名称与实时文案不一致（peer=「{previewPeerName}」, text=「{preview.Text}」）");
            }

            // 触发周期数错误，确认校验报错也不含旧实现用语
            interval.Text = "0";
            SettleWindow(editor);
            InvokeButton(editor, FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));
            AssertNoBannedJargon(editor, "日历-周期数错误", failures);
            var intervalError = FindByAutomationId<TextBlock>(editor, "ReminderRuleCalendarIntervalError");
            if (!intervalError.IsVisible ||
                !string.Equals(intervalError.Text, "每隔几个周期必须是 1 到 999。", StringComparison.Ordinal))
            {
                failures.Add($"每隔几个周期错误文案不符（visible={intervalError.IsVisible}, text=「{intervalError.Text}」）");
            }

            interval.Text = "1";
            SettleWindow(editor);

            // 每周
            frequency.SelectedIndex = 1;
            SettleWindow(editor);
            AssertNoBannedJargon(editor, "日历-每周", failures);
            AssertVisibleFormLabels(editor, "日历-每周", ["规则类型", "开始日期", "重复周期", "每隔几个周期", "具体哪几天", "规则预览"], failures);
            AssertSingleLineFormLabels(editor, "日历-每周", failures);
            AssertFieldLabelAndName(editor, selector, "具体哪几天", "日历-每周选择器容器", failures);
            var monday = FindByAutomationId<CheckBox>(editor, "ReminderRuleCalendarWeekdayMonday");
            AssertLabeledByText(editor, monday, "具体哪几天", "日历-每周星期一标签关系", failures);
            AssertAutomationName(monday, "每周星期一", "日历-每周星期一名称", failures);

            // 每月
            frequency.SelectedIndex = 2;
            SettleWindow(editor);
            AssertNoBannedJargon(editor, "日历-每月", failures);
            AssertVisibleFormLabels(editor, "日历-每月", ["规则类型", "开始日期", "重复周期", "每隔几个周期", "具体哪几天", "规则预览"], failures);
            AssertAutomationName(day, "具体日期", "日历-每月日期名称", failures);
            AssertLabeledByText(editor, day, "具体哪几天", "日历-每月日期标签关系", failures);
            AssertAutomationName(lastDay, "最后一天", "日历-每月最后一天名称", failures);
            AssertLabeledByText(editor, lastDay, "具体哪几天", "日历-每月最后一天标签关系", failures);

            // 每年
            frequency.SelectedIndex = 3;
            SettleWindow(editor);
            AssertNoBannedJargon(editor, "日历-每年", failures);
            AssertVisibleFormLabels(editor, "日历-每年", ["规则类型", "开始日期", "重复周期", "每隔几个周期", "具体哪几天", "规则预览"], failures);
            AssertAutomationName(month, "具体月份", "日历-每年月份名称", failures);
            AssertLabeledByText(editor, month, "具体哪几天", "日历-每年月份标签关系", failures);
            AssertAutomationName(day, "具体日期", "日历-每年日期名称", failures);
            AssertAutomationName(lastDay, "最后一天", "日历-每年最后一天名称", failures);
        }
        finally
        {
            CloseWindow(editor);
        }

        // 便签模式只暴露三种规则类型
        var noteEditor = CreateOffscreenEditor(Array.Empty<ReminderRule>(), clock);
        try
        {
            ShowAndSettle(noteEditor);
            var noteModes = FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleMode").Items.Cast<string>().ToArray();
            if (!noteModes.SequenceEqual(["提醒一次", "每隔一段时间", "按日历重复"]))
            {
                failures.Add($"便签规则类型列表变更：{string.Join(",", noteModes)}");
            }
        }
        finally
        {
            CloseWindow(noteEditor);
        }
    }

    private static void CheckDomainTypesRuleIdsAndCanonicalJsonUnchanged(List<string> failures)
    {
        var clock = new FixedClock(FixtureUtc);
        var targetUtc = FixtureUtc.AddDays(30);
        var startDate = new DateOnly(2038, 7, 9);
        ReminderRule[] initialRules =
        [
            new AbsoluteOnceReminderRule("rt-323-once", FixtureUtc.AddHours(2)),
            new FixedIntervalReminderRule("rt-323-interval", FixtureUtc.AddHours(3), new ReminderInterval(30)),
            new CountdownRelativeReminderRule("rt-323-relative", 15),
            new CalendarReminderRule("rt-323-daily", CalendarFrequency.Daily, 1, startDate, new TimeOnly(9, 15), new DailyCalendarSelector()),
            new CalendarReminderRule("rt-323-weekly", CalendarFrequency.Weekly, 2, startDate, new TimeOnly(9, 0), new WeeklyCalendarSelector([DayOfWeek.Monday, DayOfWeek.Thursday])),
            new CalendarReminderRule("rt-323-monthly", CalendarFrequency.Monthly, 1, startDate, new TimeOnly(18, 0), new MonthlyCalendarSelector(null, true)),
            new CalendarReminderRule("rt-323-yearly", CalendarFrequency.Yearly, 1, startDate, new TimeOnly(9, 30), new YearlyCalendarSelector(8, 15, false))
        ];

        var modalResult = RunModal(
            initialRules,
            clock,
            null,
            targetUtc,
            editor =>
            {
                foreach (var rule in initialRules)
                {
                    var editButton = VisualDescendants<Button>(editor).Single(button =>
                        string.Equals(button.Content as string, "编辑", StringComparison.Ordinal) &&
                        string.Equals(button.Tag as string, rule.Id, StringComparison.Ordinal));
                    InvokeButton(editor, editButton);
                    InvokeButton(editor, FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));
                }

                InvokeButton(editor, FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            });

        if (modalResult.DialogResult != true)
        {
            failures.Add("四类规则往返编辑保存未接受草稿");
            return;
        }

        if (!modalResult.Rules.Select(rule => rule.GetType()).SequenceEqual(initialRules.Select(rule => rule.GetType())))
        {
            failures.Add("四类规则往返保存改变了类型化领域记录");
        }

        if (!modalResult.Rules.Select(rule => rule.Id).SequenceEqual(initialRules.Select(rule => rule.Id)))
        {
            failures.Add("四类规则往返保存改变了规则 ID");
        }

        string CanonicalJson(IReadOnlyList<ReminderRule> rules) =>
            SettingsContract.SerializeCanonical(CountdownSettings.Empty with
            {
                Countdowns = [new CountdownRecord("cd-323", "词表不变契约", targetUtc, [], rules)]
            });

        if (!string.Equals(CanonicalJson(modalResult.Rules), CanonicalJson(initialRules), StringComparison.Ordinal))
        {
            failures.Add("四类规则往返保存改变了规范持久化 JSON");
        }
    }

    private static void CheckLivePreviewAcrossModesAndSavedRows(
        List<string> failures,
        Action<Window, string>? captureEvidence)
    {
        var clock = new FixedClock(FixtureUtc);
        var targetUtc = FixtureUtc.AddDays(30);
        var guidIds = Enumerable.Range(1, 8)
            .Select(index => Guid.Parse($"{index:X8}-0323-0000-0000-000000000000"))
            .ToArray();
        var editor = CreateOffscreenEditor(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(guidIds),
            countdownTargetUtc: targetUtc);
        try
        {
            ShowAndSettle(editor);

            var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
            var firstDueDate = FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate");
            var firstDueTime = FindByAutomationId<TextBox>(editor, "ReminderRuleFirstDueTime");
            var intervalChoice = FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval");
            var customInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval");
            var relativeOffset = FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes");
            var frequency = FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarFrequency");
            var calendarInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCalendarInterval");
            var calendarMonth = FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarMonth");
            var calendarDay = FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarDay");
            var calendarLastDay = FindByAutomationId<CheckBox>(editor, "ReminderRuleCalendarLastDay");
            var preview = FindByAutomationId<TextBlock>(editor, "ReminderRulePreview");
            var saveButton = FindByAutomationId<Button>(editor, "SaveReminderRuleButton");
            var inkBrush = (Brush)Application.Current.FindResource("InkBrush");
            var mutedBrush = (Brush)Application.Current.FindResource("MutedBrush");

            // 1. 提醒一次：实时更新 + 保存后与规则行摘要一致
            mode.SelectedIndex = 0;
            firstDueDate.SelectedDate = new DateTime(2038, 7, 9);
            firstDueTime.Text = "10:20";
            SettleWindow(editor);
            AssertPreview(preview, "提醒一次 · 2038 年 7 月 9 日 10:20", inkBrush, "一次初值", failures);
            firstDueTime.Text = "10:45";
            SettleWindow(editor);
            AssertPreview(preview, "提醒一次 · 2038 年 7 月 9 日 10:45", inkBrush, "一次改分钟", failures);
            var oncePreviewBeforeSave = preview.Text;
            InvokeButton(editor, saveButton);
            AssertSavedRowContainsExact(editor, oncePreviewBeforeSave, "一次保存匹配", failures);

            // 2. 每隔一段时间：预设、自定义、无效值回退、恢复并保存匹配
            mode.SelectedIndex = 1;
            firstDueDate.SelectedDate = new DateTime(2038, 7, 9);
            firstDueTime.Text = "11:00";
            intervalChoice.SelectedIndex = 1; // 30 分钟
            SettleWindow(editor);
            AssertPreview(preview, "每隔一段时间 · 每 30 分钟 · 2038 年 7 月 9 日 11:00 起", inkBrush, "固定间隔预设", failures);
            intervalChoice.SelectedIndex = 3; // 自定义
            customInterval.Text = "0";
            SettleWindow(editor);
            AssertPreview(preview, IncompletePreviewText, mutedBrush, "固定间隔无效自定义", failures);
            customInterval.Text = "45";
            SettleWindow(editor);
            AssertPreview(preview, "每隔一段时间 · 每 45 分钟 · 2038 年 7 月 9 日 11:00 起", inkBrush, "固定间隔有效自定义", failures);
            var intervalPreviewBeforeSave = preview.Text;
            InvokeButton(editor, saveButton);
            AssertSavedRowContainsExact(editor, intervalPreviewBeforeSave, "固定间隔保存匹配", failures);

            // 3. 截止前提醒：0 分钟、正数、非法回退并保存匹配
            mode.SelectedIndex = 2;
            relativeOffset.Text = "0";
            SettleWindow(editor);
            AssertPreview(preview, "截止前提醒 · 截止时提醒", inkBrush, "截止时提醒", failures);
            relativeOffset.Text = "";
            SettleWindow(editor);
            AssertPreview(preview, IncompletePreviewText, mutedBrush, "提前分钟为空", failures);
            relativeOffset.Text = "15";
            SettleWindow(editor);
            AssertPreview(preview, "截止前提醒 · 提前 15 分钟", inkBrush, "提前 15 分钟", failures);
            var relativePreviewBeforeSave = preview.Text;
            InvokeButton(editor, saveButton);
            AssertSavedRowContainsExact(editor, relativePreviewBeforeSave, "截止前提醒保存匹配", failures);

            // 4. 按日历重复 - 每天
            mode.SelectedIndex = 3;
            frequency.SelectedIndex = 0;
            calendarInterval.Text = "1";
            firstDueDate.SelectedDate = new DateTime(2038, 7, 9);
            firstDueTime.Text = "09:15";
            SettleWindow(editor);
            AssertPreview(preview, "按日历重复 · 每 1 日 · 09:15", inkBrush, "日历每天", failures);
            captureEvidence?.Invoke(editor, "rules-editor-323-calendar-daily");
            calendarInterval.Text = "2";
            firstDueTime.Text = "08:30";
            SettleWindow(editor);
            AssertPreview(preview, "按日历重复 · 每 2 日 · 08:30", inkBrush, "日历每2日", failures);
            var dailyPreviewBeforeSave = preview.Text;
            InvokeButton(editor, saveButton);
            AssertSavedRowContainsExact(editor, dailyPreviewBeforeSave, "日历每天保存匹配", failures);

            // 5. 按日历重复 - 每周（先清空全不选验证回退，再乱序勾选周四、周一验证顺序一致）
            mode.SelectedIndex = 3;
            frequency.SelectedIndex = 1;
            calendarInterval.Text = "1";
            firstDueDate.SelectedDate = new DateTime(2038, 7, 9);
            firstDueTime.Text = "09:00";
            foreach (var weekday in Enum.GetValues<DayOfWeek>())
            {
                FindByAutomationId<CheckBox>(editor, $"ReminderRuleCalendarWeekday{weekday}").IsChecked = false;
            }
            SettleWindow(editor);
            AssertPreview(preview, IncompletePreviewText, mutedBrush, "日历每周未选星期", failures);

            FindByAutomationId<CheckBox>(editor, "ReminderRuleCalendarWeekdayThursday").IsChecked = true;
            FindByAutomationId<CheckBox>(editor, "ReminderRuleCalendarWeekdayMonday").IsChecked = true;
            SettleWindow(editor);
            AssertPreview(preview, "按日历重复 · 每 1 周 · 周一、周四 · 09:00", inkBrush, "日历每周周一、周四", failures);
            captureEvidence?.Invoke(editor, "rules-editor-323-calendar-weekly");

            // 系统高对比度适配实拍与校验
            using (ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                SettleWindow(editor);
                var previewSurface = FindByAutomationId<Border>(editor, "ReminderRulePreviewSurface");
                if (!BrushColorEquals(preview.Foreground, new SolidColorBrush(SystemColors.WindowTextColor)) ||
                    !BrushColorEquals(previewSurface.BorderBrush, new SolidColorBrush(SystemColors.WindowTextColor)))
                {
                    failures.Add("系统高对比度下规则预览未切换到系统文字/边框色");
                }

                captureEvidence?.Invoke(editor, "rules-editor-323-high-contrast");
            }
            SettleWindow(editor);

            var weeklyPreviewBeforeSave = preview.Text;
            InvokeButton(editor, saveButton);
            AssertSavedRowContainsExact(editor, weeklyPreviewBeforeSave, "日历每周保存匹配", failures);

            // 6. 按日历重复 - 每月（具体日期 -> 最后一天）
            mode.SelectedIndex = 3;
            frequency.SelectedIndex = 2;
            calendarInterval.Text = "1";
            firstDueDate.SelectedDate = new DateTime(2038, 7, 9);
            firstDueTime.Text = "18:00";
            calendarLastDay.IsChecked = false;
            calendarDay.SelectedIndex = 14; // 15 日
            SettleWindow(editor);
            AssertPreview(preview, "按日历重复 · 每 1 月 · 15 日 · 18:00", inkBrush, "日历每月15日", failures);
            calendarLastDay.IsChecked = true;
            SettleWindow(editor);
            AssertPreview(preview, "按日历重复 · 每 1 月 · 最后一天 · 18:00", inkBrush, "日历每月最后一天", failures);
            var monthlyPreviewBeforeSave = preview.Text;
            InvokeButton(editor, saveButton);
            AssertSavedRowContainsExact(editor, monthlyPreviewBeforeSave, "日历每月保存匹配", failures);

            // 7. 按日历重复 - 每年（具体月日 -> 某月最后一天）+ 非法周期数实拍
            mode.SelectedIndex = 3;
            frequency.SelectedIndex = 3;
            calendarInterval.Text = "1";
            firstDueDate.SelectedDate = new DateTime(2038, 7, 9);
            firstDueTime.Text = "09:30";
            calendarLastDay.IsChecked = false;
            calendarMonth.SelectedIndex = 7; // 8 月
            calendarDay.SelectedIndex = 14; // 15 日
            SettleWindow(editor);
            AssertPreview(preview, "按日历重复 · 每 1 年 · 8 月 15 日 · 09:30", inkBrush, "日历每年8月15日", failures);
            captureEvidence?.Invoke(editor, "rules-editor-323-calendar-yearly");

            calendarLastDay.IsChecked = true;
            SettleWindow(editor);
            AssertPreview(preview, "按日历重复 · 每 1 年 · 8 月最后一天 · 09:30", inkBrush, "日历每年8月最后一天", failures);
            var yearlyPreviewBeforeSave = preview.Text;
            InvokeButton(editor, saveButton);
            AssertSavedRowContainsExact(editor, yearlyPreviewBeforeSave, "日历每年保存匹配", failures);

            // 8. 点击编辑/复制已有规则时预览立即等于该行摘要，取消编辑后回到默认新增态预览
            var weeklyEditButton = VisualDescendants<Button>(editor).Single(button =>
                string.Equals(button.Content as string, "编辑", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetName(button), $"编辑规则 {weeklyPreviewBeforeSave}", StringComparison.Ordinal));
            InvokeButton(editor, weeklyEditButton);
            AssertPreview(preview, weeklyPreviewBeforeSave, inkBrush, "编辑已有每周规则预填预览", failures);

            calendarInterval.Text = "0";
            SettleWindow(editor);
            AssertPreview(preview, IncompletePreviewText, mutedBrush, "编辑态周期数填0回退提示", failures);
            InvokeButton(editor, saveButton);
            captureEvidence?.Invoke(editor, "rules-editor-323-calendar-invalid-preview");

            InvokeButton(editor, FindByAutomationId<Button>(editor, "CancelReminderRuleEditButton"));
            if (!preview.Text.StartsWith("提醒一次 · ", StringComparison.Ordinal))
            {
                failures.Add($"取消编辑后规则预览未重置为新增态默认预览（actual=「{preview.Text}」）");
            }

            var monthlyCopyButton = VisualDescendants<Button>(editor).Single(button =>
                string.Equals(button.Content as string, "复制", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetName(button), $"复制规则 {monthlyPreviewBeforeSave}", StringComparison.Ordinal));
            InvokeButton(editor, monthlyCopyButton);
            AssertPreview(preview, monthlyPreviewBeforeSave, inkBrush, "复制已有每月规则预填预览", failures);

            Console.Error.WriteLine(
                $"RULE_EDITOR_323_PREVIEW_PROBE: weekly={weeklyPreviewBeforeSave}; monthly={monthlyPreviewBeforeSave}; yearly={yearlyPreviewBeforeSave}");
        }
        finally
        {
            CloseWindow(editor);
        }
    }

    private static void AssertPreview(
        TextBlock preview,
        string expectedText,
        Brush expectedForeground,
        string scenarioLabel,
        List<string> failures)
    {
        if (!preview.IsVisible)
        {
            failures.Add($"{scenarioLabel}：规则预览不可见");
            return;
        }

        if (!string.Equals(preview.Text, expectedText, StringComparison.Ordinal))
        {
            failures.Add($"{scenarioLabel}：规则预览期望「{expectedText}」，实际「{preview.Text}」");
        }

        if (!BrushColorEquals(preview.Foreground, expectedForeground))
        {
            failures.Add($"{scenarioLabel}：规则预览前景色未符合状态语义");
        }
    }

    private static void AssertSavedRowContainsExact(
        ReminderRulesEditorWindow editor,
        string expectedSummary,
        string scenarioLabel,
        List<string> failures)
    {
        var scroller = VisualDescendants<ScrollViewer>(editor).Single(scroll =>
            string.Equals(AutomationProperties.GetName(scroll), "已配置提醒规则", StringComparison.Ordinal));
        var savedSummaries = VisualDescendants<TextBlock>(scroller)
            .Select(text => text.Text)
            .ToArray();
        if (!savedSummaries.Contains(expectedSummary, StringComparer.Ordinal))
        {
            failures.Add($"{scenarioLabel}：保存后规则列表未找到与预览完全一致的摘要「{expectedSummary}」，现有=[{string.Join(" | ", savedSummaries)}]");
        }
    }

    private static void AssertNoBannedJargon(
        ReminderRulesEditorWindow editor,
        string scenarioLabel,
        List<string> failures)
    {
        foreach (var text in VisualDescendants<TextBlock>(editor).Where(block => block.IsVisible))
        {
            foreach (var banned in BannedJargonTerms)
            {
                if (text.Text.Contains(banned, StringComparison.Ordinal))
                {
                    failures.Add($"{scenarioLabel}：可见文案仍包含实现用语「{banned}」（text=「{text.Text}」）");
                }
            }
        }

        foreach (var element in VisualDescendants<FrameworkElement>(editor))
        {
            var explicitName = AutomationProperties.GetName(element) ?? string.Empty;
            var helpText = AutomationProperties.GetHelpText(element) ?? string.Empty;
            var peerName = UIElementAutomationPeer.CreatePeerForElement(element)?.GetName() ?? string.Empty;
            foreach (var banned in BannedJargonTerms)
            {
                if (explicitName.Contains(banned, StringComparison.Ordinal) ||
                    helpText.Contains(banned, StringComparison.Ordinal) ||
                    peerName.Contains(banned, StringComparison.Ordinal))
                {
                    var id = AutomationProperties.GetAutomationId(element);
                    failures.Add(
                        $"{scenarioLabel}：元素 {element.GetType().Name}({id}) 的可访问属性仍包含实现用语「{banned}」" +
                        $"（name=「{explicitName}」, peer=「{peerName}」, help=「{helpText}」）");
                }
            }
        }
    }

    private static void AssertVisibleFormLabels(
        ReminderRulesEditorWindow editor,
        string scenarioLabel,
        string[] expectedLabels,
        List<string> failures)
    {
        var formGrid = FindFormGrid(editor);
        var actualLabels = formGrid.Children
            .OfType<Grid>()
            .Where(row => row.IsVisible)
            .SelectMany(row => row.Children.OfType<TextBlock>().Where(block => Grid.GetColumn(block) == 0 && block.IsVisible))
            .Select(block => block.Text)
            .ToArray();
        if (!actualLabels.SequenceEqual(expectedLabels))
        {
            failures.Add(
                $"{scenarioLabel}：表单可见标签期望 [{string.Join(", ", expectedLabels)}]，实际 [{string.Join(", ", actualLabels)}]");
        }
    }

    private static void AssertSingleLineFormLabels(
        ReminderRulesEditorWindow editor,
        string scenarioLabel,
        List<string> failures)
    {
        var formGrid = FindFormGrid(editor);
        foreach (var label in formGrid.Children
                     .OfType<Grid>()
                     .Where(row => row.IsVisible)
                     .SelectMany(row => row.Children.OfType<TextBlock>().Where(block => Grid.GetColumn(block) == 0 && block.IsVisible)))
        {
            if (label.ActualWidth <= 0 || label.ActualWidth > 118.5 || label.ActualHeight >= 30)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{scenarioLabel}：标签「{label.Text}」尺寸异常或换行（{label.ActualWidth:0.##}×{label.ActualHeight:0.##}）"));
            }
        }
    }

    private static void AssertFieldLabelAndName(
        ReminderRulesEditorWindow editor,
        FrameworkElement element,
        string expectedLabel,
        string scenarioLabel,
        List<string> failures)
    {
        AssertAutomationName(element, expectedLabel, scenarioLabel, failures);
        AssertLabeledByText(editor, element, expectedLabel, scenarioLabel, failures);
    }

    private static void AssertAutomationName(
        FrameworkElement element,
        string expectedName,
        string scenarioLabel,
        List<string> failures)
    {
        var explicitName = AutomationProperties.GetName(element);
        if (!string.Equals(explicitName, expectedName, StringComparison.Ordinal))
        {
            failures.Add($"{scenarioLabel}：AutomationProperties.Name 期望「{expectedName}」，实际「{explicitName}」");
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer is not null)
        {
            var peerName = peer.GetName();
            if (!string.Equals(peerName, expectedName, StringComparison.Ordinal))
            {
                failures.Add($"{scenarioLabel}：AutomationPeer.GetName 期望「{expectedName}」，实际「{peerName}」");
            }
        }
    }

    private static void AssertLabeledByText(
        ReminderRulesEditorWindow editor,
        FrameworkElement element,
        string expectedLabelText,
        string scenarioLabel,
        List<string> failures)
    {
        var labeledBy = AutomationProperties.GetLabeledBy(element);
        if (labeledBy is not TextBlock labelBlock ||
            !labelBlock.IsVisible ||
            !string.Equals(labelBlock.Text, expectedLabelText, StringComparison.Ordinal) ||
            !editor.IsAncestorOf(labelBlock))
        {
            failures.Add(
                $"{scenarioLabel}：LabeledBy 未指向可见标签「{expectedLabelText}」（actual={labeledBy?.GetType().Name ?? "null"}/{(labeledBy as TextBlock)?.Text ?? ""}）");
        }
    }

    private static Grid FindFormGrid(ReminderRulesEditorWindow editor)
    {
        var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
        var formRow = mode.Parent as Grid
            ?? throw new InvalidOperationException("规则类型未位于表单行 Grid。");
        return formRow.Parent as Grid
            ?? throw new InvalidOperationException("表单行未位于表单 Grid。");
    }

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
        return new ReminderRulesEditorWindow(
            rules,
            clock,
            guidSource,
            countdownTargetUtc: countdownTargetUtc)
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

    private static bool BrushColorEquals(Brush? actual, Brush? expected) =>
        actual is SolidColorBrush a &&
        expected is SolidColorBrush e &&
        a.Color == e.Color;

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
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-rules-editor-323");
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

    private sealed record ModalEditorResult(bool? DialogResult, IReadOnlyList<ReminderRule> Rules);

    private sealed class ValidationGuidSource(params Guid[] values) : IGuidSource
    {
        private readonly Queue<Guid> _values = new(values);

        public Guid NewGuid() => _values.Count > 0 ? _values.Dequeue() : Guid.NewGuid();
    }
}
