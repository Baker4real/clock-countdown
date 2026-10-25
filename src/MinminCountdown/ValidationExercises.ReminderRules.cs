using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    public static bool RunReminderEditingInterfaceBehavior()
    {
        var editor = new ReminderEditing();
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 2, 3, TimeSpan.Zero);
        var futureBeijing = new DateTimeOffset(2034, 5, 7, 10, 20, 0, TimeSpan.FromHours(8));
        var noReminder = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.None,
            null,
            -1,
            -1,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc,
            null,
            false));
        Must(noReminder.Succeeded && noReminder.Reminder is NoReminder && noReminder.Error.Length == 0,
            "不提醒模式未通过 ReminderEditing interface。");

        foreach (var mode in new[] { ReminderEditingMode.Once, ReminderEditingMode.Repeating })
        {
            var incomplete = editor.Build(new ReminderEditingRequest(
                mode,
                null,
                -1,
                -1,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                null,
                true));
            Must(!incomplete.Succeeded &&
                incomplete.Error == "请选择完整的首次提醒日期和时间。",
                $"{mode} 未拒绝不完整的首次提醒时间。");

            var notFuture = editor.Build(new ReminderEditingRequest(
                mode,
                new DateTime(2034, 5, 6),
                9,
                2,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                null,
                true));
            Must(!notFuture.Succeeded &&
                notFuture.Error == "首次提醒时间必须晚于当前北京时间。",
                $"{mode} 未拒绝不在未来的首次提醒时间。");
        }

        var intervalCases = new[]
        {
            (ReminderIntervalSelection.FifteenMinutes, string.Empty, 15),
            (ReminderIntervalSelection.ThirtyMinutes, string.Empty, 30),
            (ReminderIntervalSelection.SixtyMinutes, string.Empty, 60),
            (ReminderIntervalSelection.Custom, "1", 1),
            (ReminderIntervalSelection.Custom, "1440", 1440)
        };
        foreach (var (selection, custom, expectedMinutes) in intervalCases)
        {
            var result = editor.Build(new ReminderEditingRequest(
                ReminderEditingMode.Repeating,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                selection,
                custom,
                nowUtc,
                null,
                true));
            Must(result is { Succeeded: true, Reminder: RepeatingReminder repeating } &&
                repeating.FirstDueUtc == futureBeijing.ToUniversalTime() &&
                repeating.Interval.Minutes == expectedMinutes,
                $"重复提醒间隔 {expectedMinutes} 分钟未通过 ReminderEditing interface。");
        }

        foreach (var invalidCustom in new[] { "0", "1441", "不是数字" })
        {
            var result = editor.Build(new ReminderEditingRequest(
                ReminderEditingMode.Repeating,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                ReminderIntervalSelection.Custom,
                invalidCustom,
                nowUtc,
                null,
                true));
            Must(!result.Succeeded && result.Error == "自定义提醒必须是 1 到 1440 分钟。",
                $"无效自定义间隔“{invalidCustom}”未保留现有中文错误。");
        }

        var existingOnce = new OnceReminder(nowUtc.AddDays(-1), OnceDeliveryState.Delivered);
        var preservedOnce = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.Once,
            null,
            -1,
            -1,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc,
            existingOnce,
            false));
        Must(preservedOnce.Succeeded && Equals(existingOnce, preservedOnce.Reminder),
            "未改动 schedule 时没有完整保留既有 Once reminder。");

        var existingRepeating = new RepeatingReminder(
            nowUtc.AddDays(-3),
            new ReminderInterval(30));
        var preservedRepeating = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.Repeating,
            futureBeijing.Date,
            futureBeijing.Hour,
            futureBeijing.Minute,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc,
            existingRepeating,
            false));
        Must(preservedRepeating.Succeeded && Equals(existingRepeating, preservedRepeating.Reminder),
            "interval 与 schedule 未变时没有完整保留既有 Repeating reminder。");

        var changedInterval = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.Repeating,
            futureBeijing.Date,
            futureBeijing.Hour,
            futureBeijing.Minute,
            ReminderIntervalSelection.SixtyMinutes,
            string.Empty,
            nowUtc,
            existingRepeating,
            false));
        Must(changedInterval is { Succeeded: true, Reminder: RepeatingReminder changedIntervalReminder } &&
            !Equals(existingRepeating, changedIntervalReminder) &&
            changedIntervalReminder.Interval.Minutes == 60 &&
            changedIntervalReminder.FirstDueUtc == futureBeijing.ToUniversalTime(),
            "interval 改变时没有生成新的 Repeating reminder。");

        var changedSchedule = editor.Build(new ReminderEditingRequest(
            ReminderEditingMode.Repeating,
            futureBeijing.Date,
            futureBeijing.Hour,
            futureBeijing.Minute,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc,
            existingRepeating,
            true));
        Must(changedSchedule is { Succeeded: true, Reminder: RepeatingReminder changedScheduleReminder } &&
            !Equals(existingRepeating, changedScheduleReminder) &&
            changedScheduleReminder.Interval.Minutes == 30 &&
            changedScheduleReminder.FirstDueUtc == futureBeijing.ToUniversalTime(),
            "首次时间改变时没有生成新的 Repeating reminder。");

        var onceDefault = editor.GetDefaultFirstDueBeijing(
            ReminderEditingMode.Once,
            ReminderIntervalSelection.SixtyMinutes,
            string.Empty,
            nowUtc);
        var repeatingDefault = editor.GetDefaultFirstDueBeijing(
            ReminderEditingMode.Repeating,
            ReminderIntervalSelection.ThirtyMinutes,
            string.Empty,
            nowUtc);
        var customDefault = editor.GetDefaultFirstDueBeijing(
            ReminderEditingMode.Repeating,
            ReminderIntervalSelection.Custom,
            "1",
            nowUtc);
        Must(onceDefault.Succeeded &&
            onceDefault.FirstDueBeijing == new DateTimeOffset(2034, 5, 6, 9, 17, 0, TimeSpan.FromHours(8)),
            "Once 默认首次时间未按北京时间加 15 分钟。");
        Must(repeatingDefault.Succeeded &&
            repeatingDefault.FirstDueBeijing == new DateTimeOffset(2034, 5, 6, 9, 32, 0, TimeSpan.FromHours(8)),
            "Repeating 默认首次时间未使用预设 interval。");
        Must(customDefault.Succeeded &&
            customDefault.FirstDueBeijing == new DateTimeOffset(2034, 5, 6, 9, 3, 0, TimeSpan.FromHours(8)),
            "Repeating 默认首次时间未使用自定义 interval。");

        var ruleEditor = new ReminderRuleEditing(new ValidationGuidSource(
            Guid.Parse("11111111-1111-1111-1111-111111111111")));
        var addedRule = ruleEditor.SaveRule(
            Array.Empty<ReminderRule>(),
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(addedRule is { Succeeded: true, Rules.Count: 1 } &&
            addedRule.Rules[0] is AbsoluteOnceReminderRule
            {
                Id: "11111111111111111111111111111111"
            } addedOnce &&
            addedOnce.ScheduledUtc == futureBeijing.ToUniversalTime(),
            "空规则集合未能通过共享编辑器新增绝对一次规则。");

        var editedBeijing = futureBeijing.AddHours(2);
        var editedRule = ruleEditor.SaveRule(
            addedRule.Rules,
            new ReminderRuleEditingRequest(
                addedRule.Rules[0].Id,
                ReminderEditingMode.Once,
                editedBeijing.Date,
                editedBeijing.Hour,
                editedBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(editedRule is { Succeeded: true, Rules.Count: 1 } &&
            editedRule.Rules[0] is AbsoluteOnceReminderRule editedOnce &&
            editedOnce.Id == addedRule.Rules[0].Id &&
            editedOnce.ScheduledUtc == editedBeijing.ToUniversalTime(),
            "原位编辑提醒规则没有保留 rule ID。");

        var copyEditor = new ReminderRuleEditing(new ValidationGuidSource(
            Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var copiedDue = editedBeijing.AddHours(1);
        var copiedRule = copyEditor.SaveRule(
            editedRule.Rules,
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                copiedDue.Date,
                copiedDue.Hour,
                copiedDue.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(copiedRule is { Succeeded: true, Rules.Count: 2 } &&
            copiedRule.Rules[1].Id == "22222222222222222222222222222222" &&
            copiedRule.Rules[1].Id != copiedRule.Rules[0].Id,
            "复制规则保存为唯一日程后没有生成新 rule ID。");

        var deletedRule = copyEditor.DeleteRule(copiedRule.Rules, copiedRule.Rules[0].Id);
        Must(deletedRule is { Succeeded: true, Rules.Count: 1 } &&
            deletedRule.Rules[0].Id == copiedRule.Rules[1].Id,
            "删除规则误删或改写了同便签的其他规则。");

        var boundaryGuidValues = Enumerable.Range(1, 9)
            .Select(index => Guid.Parse($"{index:X8}-0000-0000-0000-000000000000"))
            .ToArray();
        var boundaryEditor = new ReminderRuleEditing(new ValidationGuidSource(boundaryGuidValues));
        IReadOnlyList<ReminderRule> eightRules = Array.Empty<ReminderRule>();
        for (var index = 0; index < 8; index++)
        {
            var due = futureBeijing.AddMinutes(index);
            var result = boundaryEditor.SaveRule(
                eightRules,
                new ReminderRuleEditingRequest(
                    null,
                    ReminderEditingMode.Once,
                    due.Date,
                    due.Hour,
                    due.Minute,
                    ReminderIntervalSelection.ThirtyMinutes,
                    string.Empty,
                    nowUtc));
            Must(result.Succeeded, $"第 {index + 1} 条提醒规则未能保存。");
            eightRules = result.Rules;
        }

        Must(eightRules.Count == 8, "共享规则编辑器未接受 8 条规则边界。");
        var ninthDue = futureBeijing.AddMinutes(9);
        var ninthRule = boundaryEditor.SaveRule(
            eightRules,
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                ninthDue.Date,
                ninthDue.Hour,
                ninthDue.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(!ninthRule.Succeeded &&
            ninthRule.Rules.SequenceEqual(eightRules) &&
            ninthRule.ErrorTarget == ReminderRuleEditingErrorTarget.Collection,
            "共享规则编辑器未拒绝第 9 条规则并把错误归位到规则集合。");

        var firstRuleOnly = Array.AsReadOnly(new[] { eightRules[0] });
        var duplicateRule = boundaryEditor.SaveRule(
            firstRuleOnly,
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(!duplicateRule.Succeeded &&
            duplicateRule.Rules.SequenceEqual(firstRuleOnly) &&
            duplicateRule.ErrorTarget == ReminderRuleEditingErrorTarget.FirstDue,
            "共享规则编辑器未拒绝重复的规范日程并把错误归位到时间输入。");

        var invalidInterval = boundaryEditor.SaveRule(
            Array.Empty<ReminderRule>(),
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Repeating,
                futureBeijing.Date,
                futureBeijing.Hour,
                futureBeijing.Minute,
                ReminderIntervalSelection.Custom,
                "0",
                nowUtc));
        Must(!invalidInterval.Succeeded &&
            invalidInterval.ErrorTarget == ReminderRuleEditingErrorTarget.Interval,
            "共享规则编辑器未把非法间隔错误归位到间隔输入。");

        var beijingNow = ChinaStandardTime.ToBeijingTime(nowUtc);
        var pastAbsolute = boundaryEditor.SaveRule(
            Array.Empty<ReminderRule>(),
            new ReminderRuleEditingRequest(
                null,
                ReminderEditingMode.Once,
                beijingNow.Date,
                beijingNow.Hour,
                beijingNow.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc));
        Must(!pastAbsolute.Succeeded &&
            pastAbsolute.ErrorTarget == ReminderRuleEditingErrorTarget.FirstDue,
            "共享规则编辑器未拒绝过去的绝对时间并把错误归位到时间输入。");

        Must(RunMainReminderRuleDeletionBehavior(),
            "主窗口未能把共享编辑器中的 1→0 规则删除原子保存到磁盘、state 和提醒中心。");
        Must(RunDelayedReminderRuleCommitBehavior(),
            "主窗口延迟最终提交未拒绝已经过期的新规则并原子保留原状态。");
        Must(RunHistoricalReminderRuleCommitBehavior(),
            "历史规则的正文编辑、改期拒绝或删除事务不符合最终校验边界。");
        Must(RunMainDelayedReminderRuleCommitBehavior(),
            "主窗口最终提交失败后未重开规则编辑器并归位首次提醒错误。");
        Must(RunReminderRuleTypeTransitionBehavior(nowUtc),
            "共享规则编辑 module 未能在相对、绝对一次与固定间隔之间安全切换并保留 rule ID。");
        Must(RunReminderRulesEditorWindowBehavior(nowUtc),
            "共享提醒规则窗口未通过真实 WPF 事件完成实时校验、dirty 保存或取消恢复。");

        return true;
    }

    private static bool RunReminderRuleTypeTransitionBehavior(DateTimeOffset nowUtc)
    {
        const string ruleId = "reminder-type-transition";
        var editing = new ReminderRuleEditing();
        var relative = new CountdownRelativeReminderRule(ruleId, 30);
        var dueBeijing = ChinaStandardTime.ToBeijingTime(nowUtc.AddHours(2));
        var targetUtc = nowUtc.AddHours(4);

        var toAbsolute = editing.SaveRule(
            new ReminderRule[] { relative },
            new ReminderRuleEditingRequest(
                ruleId,
                ReminderEditingMode.Once,
                dueBeijing.Date,
                dueBeijing.Hour,
                dueBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                targetUtc));
        var toRepeating = editing.SaveRule(
            new ReminderRule[] { relative },
            new ReminderRuleEditingRequest(
                ruleId,
                ReminderEditingMode.Repeating,
                dueBeijing.Date,
                dueBeijing.Hour,
                dueBeijing.Minute,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                targetUtc));
        var absolute = new AbsoluteOnceReminderRule(ruleId, dueBeijing.ToUniversalTime());
        var toRelative = editing.SaveRule(
            new ReminderRule[] { absolute },
            new ReminderRuleEditingRequest(
                ruleId,
                ReminderEditingMode.CountdownRelative,
                null,
                -1,
                -1,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                nowUtc,
                targetUtc,
                "30"));

        return toAbsolute is
                   { Succeeded: true, Rules: [AbsoluteOnceReminderRule absoluteResult] } &&
               absoluteResult.Id == ruleId &&
               toRepeating is
                   { Succeeded: true, Rules: [FixedIntervalReminderRule repeatingResult] } &&
               repeatingResult.Id == ruleId &&
               toRelative is
                   { Succeeded: true, Rules: [CountdownRelativeReminderRule relativeResult] } &&
               relativeResult.Id == ruleId;
    }

    private static void InvokeRuleEditorButton(Button button)
    {
        // #203: editor actions now bind commands; Click alone skips them.
        Must(WindowValidationInput.InvokeButton(button), "提醒规则编辑器按钮不支持真实 Invoke。");
        WindowValidationInput.Pump(button.Dispatcher);
    }

    internal static bool ExerciseReminderRulesEntry(Window owner, Button entry)
    {
        var opened = false;
        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                var editors = Application.Current.Windows
                    .OfType<ReminderRulesEditorWindow>()
                    .Where(window => ReferenceEquals(window.Owner, owner))
                    .ToArray();
                opened = editors.Length == 1 &&
                    editors[0].IsVisible &&
                    string.Equals(
                        AutomationProperties.GetName(editors[0]),
                        "提醒规则编辑器",
                        StringComparison.Ordinal);
                if (editors.Length == 1)
                {
                    InvokeRuleEditorButton(FindByAutomationId<Button>(editors[0], "CancelReminderRulesButton"));
                }
            }));
        entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WindowValidationInput.Pump(owner.Dispatcher);
        return opened &&
            !Application.Current.Windows
                .OfType<ReminderRulesEditorWindow>()
                .Any(window => ReferenceEquals(window.Owner, owner));
    }

    private static bool RunReminderRulesEditorWindowBehavior(DateTimeOffset nowUtc)
    {
        ProbePlainLanguageRuleLabelsAndPersistence();
        var clock = new AdvancingValidationClock(nowUtc);
        var ids = new[]
        {
            Guid.Parse("11111111-0000-0000-0000-000000000000"),
            Guid.Parse("22222222-0000-0000-0000-000000000000"),
            Guid.Parse("33333333-0000-0000-0000-000000000000")
        };
        var actionsWorked = false;
        var formModesWorked = false;
        var editCancelWorked = false;
        var copyCancelWorked = false;
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(ids));
        try
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                formModesWorked = ProbeReminderRuleFormModes(editor, supportsRelative: false);
                var firstDue = ChinaStandardTime.ToBeijingTime(nowUtc).AddDays(1);
                firstDue = new DateTimeOffset(
                    firstDue.Year,
                    firstDue.Month,
                    firstDue.Day,
                    10,
                    20,
                    0,
                    firstDue.Offset);
                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue, 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));

                var originalEdit = VisualDescendants<Button>(editor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                var originalId = originalEdit.Tag as string;
                InvokeRuleEditorButton(originalEdit);
                editCancelWorked = CancelRuleEditHasVisibleLabelAndResetsForm(editor);
                InvokeRuleEditorButton(originalEdit);
                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue.AddHours(1), 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));

                InvokeRuleEditorButton(VisualDescendants<Button>(editor).Single(button =>
                        string.Equals(button.Content?.ToString(), "复制", StringComparison.Ordinal) &&
                        string.Equals(button.Tag as string, originalId, StringComparison.Ordinal)));
                copyCancelWorked = CancelRuleEditHasVisibleLabelAndResetsForm(editor);
                InvokeRuleEditorButton(VisualDescendants<Button>(editor).Single(button =>
                        string.Equals(button.Content?.ToString(), "复制", StringComparison.Ordinal) &&
                        string.Equals(button.Tag as string, originalId, StringComparison.Ordinal)));
                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue.AddHours(2), 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));

                var copiedId = VisualDescendants<Button>(editor)
                    .Where(button => string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal))
                    .Select(button => button.Tag as string)
                    .Single(id => !string.Equals(id, originalId, StringComparison.Ordinal));
                InvokeRuleEditorButton(VisualDescendants<Button>(editor).Single(button =>
                        string.Equals(button.Content?.ToString(), "删除", StringComparison.Ordinal) &&
                        string.Equals(button.Tag as string, originalId, StringComparison.Ordinal)));

                SetEditorSchedule(editor, ReminderEditingMode.Once, firstDue.AddHours(3), 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
                actionsWorked = editor.ResultRules.Count == 2 &&
                    editor.ResultRules.Any(rule => string.Equals(rule.Id, copiedId, StringComparison.Ordinal)) &&
                    editor.ResultRules.All(rule => !string.Equals(rule.Id, originalId, StringComparison.Ordinal));
            }));
            var accepted = editor.ShowDialog() == true;
            Must(accepted, "共享提醒规则编辑器未接受有效的新增、编辑、复制与删除草稿。");
            Must(actionsWorked, "共享提醒规则编辑器未保留复制项、删除原项或提交 dirty 草稿。");
            Must(formModesWorked && editCancelWorked && copyCancelWorked,
                $"提醒规则可见表单回归：modeRows={formModesWorked}, editCancel={editCancelWorked}, copyCancel={copyCancelWorked}。");
        }
        finally
        {
            if (editor.IsVisible)
            {
                editor.Close();
            }
        }

        var delayedClock = new AdvancingValidationClock(nowUtc);
        var delayedEditor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            delayedClock,
            new ValidationGuidSource(Guid.Parse("44444444-0000-0000-0000-000000000000")));
        var rejectedAtLiveTime = false;
        try
        {
            delayedEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                var firstDue = ChinaStandardTime.ToBeijingTime(nowUtc.AddMinutes(3));
                SetEditorSchedule(delayedEditor, ReminderEditingMode.Once, firstDue, 30);
                delayedClock.UtcNow = nowUtc.AddMinutes(4);
                InvokeRuleEditorButton(FindByAutomationId<Button>(delayedEditor, "AcceptReminderRulesButton"));
                var error = FindByAutomationId<TextBlock>(delayedEditor, "ReminderRuleFirstDueError");
                rejectedAtLiveTime = delayedEditor.IsVisible &&
                    delayedEditor.ResultRules.Count == 0 &&
                    error.Visibility == Visibility.Visible &&
                    string.Equals(
                        error.Text,
                        "首次提醒时间必须晚于当前北京时间。",
                        StringComparison.Ordinal);

                var correctedDue = ChinaStandardTime.ToBeijingTime(delayedClock.UtcNow.AddMinutes(10));
                SetEditorSchedule(delayedEditor, ReminderEditingMode.Once, correctedDue, 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(delayedEditor, "AcceptReminderRulesButton"));
            }));
            Must(delayedEditor.ShowDialog() == true,
                "共享提醒规则编辑器未在修正实时过期输入后接受草稿。");
            Must(rejectedAtLiveTime,
                "共享提醒规则编辑器未按最终提交时钟拒绝已经过期的首次提醒。");
            Must(delayedEditor.ResultRules.Count == 1,
                "共享提醒规则编辑器修正实时过期输入后未只保存一条规则。");
        }
        finally
        {
            if (delayedEditor.IsVisible)
            {
                delayedEditor.Close();
            }
        }

        var initialRule = new AbsoluteOnceReminderRule("cancel-rule", nowUtc.AddDays(1));
        var cancelledEditor = new ReminderRulesEditorWindow(
            new ReminderRule[] { initialRule },
            clock);
        try
        {
            cancelledEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                InvokeRuleEditorButton(VisualDescendants<Button>(cancelledEditor).Single(button =>
                        string.Equals(button.Content?.ToString(), "删除", StringComparison.Ordinal)));
                InvokeRuleEditorButton(FindByAutomationId<Button>(cancelledEditor, "CancelReminderRulesButton"));
            }));
            Must(cancelledEditor.ShowDialog() == false,
                "共享提醒规则编辑器取消删除后未以取消结果关闭。");
            Must(cancelledEditor.ResultRules.Count == 1 &&
                 cancelledEditor.ResultRules[0].Id == initialRule.Id,
                "共享提醒规则编辑器取消删除后未恢复原规则。");
            Must(RunCountdownRelativeReminderRulesEditorWindowBehavior(nowUtc),
                "倒计时相对提醒规则编辑器未通过真实 WPF 行为。");
            Must(RunCalendarReminderRulesEditorWindowBehavior(nowUtc),
                "日历提醒规则编辑器未通过真实 WPF 行为。");
            return true;
        }
        finally
        {
            if (cancelledEditor.IsVisible)
            {
                cancelledEditor.Close();
            }
        }
    }

    private static void ProbePlainLanguageRuleLabelsAndPersistence()
    {
        var now = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ReminderRule[] rules =
        [
            new AbsoluteOnceReminderRule("plain-once", now.AddHours(1)),
            new FixedIntervalReminderRule("plain-interval", now.AddHours(2), new ReminderInterval(30)),
            new CountdownRelativeReminderRule("plain-relative", 15),
            new CalendarReminderRule("plain-calendar", CalendarFrequency.Daily, 1,
                new DateOnly(2031, 1, 2), new TimeOnly(9, 30), new DailyCalendarSelector())
        ];
        var labels = new[] { "提醒一次", "每隔一段时间", "截止前提醒", "按日历重复" };
        var descriptions = new[]
        {
            "提醒一次 · 2031 年 1 月 1 日 09:00",
            "每隔一段时间 · 每 30 分钟 · 2031 年 1 月 1 日 10:00 起",
            "截止前提醒 · 提前 15 分钟",
            "按日历重复 · 每 1 日 · 09:30"
        };
        var editor = new ReminderRulesEditorWindow(rules, new AdvancingValidationClock(now),
            countdownTargetUtc: now.AddDays(3));
        PrepareOffscreenValidationWindow(editor);
        try
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                var modes = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
                Must(modes.Items.Cast<string>().SequenceEqual(labels), "#228 规则类型未使用平实文案。");
                Must(descriptions.All(expected => VisualDescendants<TextBlock>(editor).Any(text => text.Text == expected)),
                    "#225/#228 四类规则摘要未保留平实文案和带空格的完整时间信息。");
                foreach (var (rule, index) in rules.Select((rule, index) => (rule, index)))
                {
                    var edit = VisualDescendants<Button>(editor).Single(button =>
                        Equals(button.Content, "编辑") && Equals(button.Tag, rule.Id));
                    InvokeRuleEditorButton(edit);
                    modes.SelectedItem = labels[index];
                    InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));
                }
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            }));
            Must(editor.ShowDialog() == true, "#228 新类型文案无法提交规则。");
            Must(editor.ResultRules.Select(rule => rule.GetType()).SequenceEqual(rules.Select(rule => rule.GetType())) &&
                editor.ResultRules.Select(rule => rule.Id).SequenceEqual(rules.Select(rule => rule.Id)),
                "#228 可见文案改变了领域类型或稳定 rule ID。");
            string Canonical(IReadOnlyList<ReminderRule> values) => SettingsContract.SerializeCanonical(
                CountdownSettings.Empty with { Countdowns =
                    [new CountdownRecord("plain-countdown", "文案契约", now.AddDays(3), [], values)] });
            Must(Canonical(editor.ResultRules) == Canonical(rules), "#228 文案变更改变了规范持久化 JSON。");
        }
        finally { if (editor.IsVisible) editor.Close(); }
        var noteEditor = new ReminderRulesEditorWindow([], new AdvancingValidationClock(now));
        PrepareOffscreenValidationWindow(noteEditor);
        try
        {
            noteEditor.Show();
            WindowValidationInput.Pump(noteEditor.Dispatcher);
            Must(FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleMode").Items.Cast<string>()
                .SequenceEqual(new[] { "提醒一次", "每隔一段时间", "按日历重复" }),
                "#228 便签类型文案或相对规则隐藏合同不符。");
        }
        finally { noteEditor.Close(); }
    }

    private static bool RunCountdownRelativeReminderRulesEditorWindowBehavior(
        DateTimeOffset nowUtc)
    {
        var clock = new AdvancingValidationClock(nowUtc);
        var relativeRuleId = Guid.Parse("55555555-0000-0000-0000-000000000000");
        var targetUtc = nowUtc.AddMinutes(5_256_010);
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(relativeRuleId),
            countdownTargetUtc: targetUtc);
        var editedExistingRule = false;
        var formModesWorked = false;
        try
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                formModesWorked = ProbeReminderRuleFormModes(editor, supportsRelative: true);
                var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
                mode.SelectedIndex = 2;
                FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes").Text = "0";
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));

                var edits = VisualDescendants<Button>(editor).Where(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal)).ToArray();
                if (edits.Length != 1)
                {
                    InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "CancelReminderRulesButton"));
                    return;
                }

                var edit = edits[0];
                var createdId = edit.Tag as string;
                InvokeRuleEditorButton(edit);
                FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes").Text =
                    "5256000";
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
                editedExistingRule =
                    mode.Items.Count == 4 &&
                    string.Equals(createdId, relativeRuleId.ToString("N"), StringComparison.Ordinal) &&
                    editor.ResultRules.SingleOrDefault() is CountdownRelativeReminderRule relative &&
                    string.Equals(relative.Id, createdId, StringComparison.Ordinal) &&
                    relative.OffsetMinutesBefore == 5_256_000;
            }));

            Must(editor.ShowDialog() == true,
                "倒计时相对规则编辑器未接受有效的创建和编辑草稿。");
            Must(editedExistingRule,
                "倒计时相对规则编辑器未保留 rule ID、最大 offset 或完整类型列表。");
            Must(formModesWorked, "倒计时提醒规则模式切换后存在孤立标签、时刻分隔符或未折叠空行。");
        }
        finally
        {
            if (editor.IsVisible)
            {
                editor.Close();
            }
        }

        const string transitionRuleId = "countdown-relative-type-transition";
        var transitionEditor = new ReminderRulesEditorWindow(
            new ReminderRule[] { new CountdownRelativeReminderRule(transitionRuleId, 30) },
            clock,
            countdownTargetUtc: nowUtc.AddHours(4));
        var typeTransitionsWorked = false;
        try
        {
            transitionEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dueBeijing = ChinaStandardTime.ToBeijingTime(nowUtc.AddHours(2));
                var edit = VisualDescendants<Button>(transitionEditor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                InvokeRuleEditorButton(edit);
                SetEditorSchedule(transitionEditor, ReminderEditingMode.Once, dueBeijing, 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(transitionEditor, "SaveReminderRuleButton"));

                edit = VisualDescendants<Button>(transitionEditor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                InvokeRuleEditorButton(edit);
                FindByAutomationId<ComboBox>(transitionEditor, "ReminderRuleMode").SelectedIndex = 2;
                FindByAutomationId<TextBox>(transitionEditor, "ReminderRuleRelativeOffsetMinutes").Text = "30";
                InvokeRuleEditorButton(FindByAutomationId<Button>(transitionEditor, "SaveReminderRuleButton"));

                edit = VisualDescendants<Button>(transitionEditor).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal));
                InvokeRuleEditorButton(edit);
                SetEditorSchedule(transitionEditor, ReminderEditingMode.Repeating, dueBeijing, 30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(transitionEditor, "SaveReminderRuleButton"));
                InvokeRuleEditorButton(FindByAutomationId<Button>(transitionEditor, "AcceptReminderRulesButton"));
                typeTransitionsWorked =
                    transitionEditor.ResultRules.SingleOrDefault() is FixedIntervalReminderRule finalRule &&
                    string.Equals(finalRule.Id, transitionRuleId, StringComparison.Ordinal);
            }));

            Must(transitionEditor.ShowDialog() == true,
                "倒计时相对规则类型切换后未接受有效草稿。");
            Must(typeTransitionsWorked,
                "倒计时相对、绝对一次和固定间隔切换未保留 rule ID。");
        }
        finally
        {
            if (transitionEditor.IsVisible)
            {
                transitionEditor.Close();
            }
        }

        var rejectedEditor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("66666666-0000-0000-0000-000000000000")),
            countdownTargetUtc: nowUtc.AddMinutes(30));
        var pastRejectedAtOffset = false;
        try
        {
            rejectedEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                FindByAutomationId<ComboBox>(rejectedEditor, "ReminderRuleMode").SelectedIndex = 2;
                FindByAutomationId<TextBox>(rejectedEditor, "ReminderRuleRelativeOffsetMinutes").Text = "60";
                InvokeRuleEditorButton(FindByAutomationId<Button>(rejectedEditor, "SaveReminderRuleButton"));
                var error = FindByAutomationId<TextBlock>(
                    rejectedEditor,
                    "ReminderRuleRelativeOffsetError");
                pastRejectedAtOffset =
                    rejectedEditor.ResultRules.Count == 0 &&
                    error.Visibility == Visibility.Visible &&
                    error.Text.Contains("派生时间晚于当前北京时间", StringComparison.Ordinal);
                InvokeRuleEditorButton(FindByAutomationId<Button>(rejectedEditor, "CancelReminderRulesButton"));
            }));
            Must(rejectedEditor.ShowDialog() == false,
                "倒计时相对规则无效 offset 探针未以取消结果关闭。");
            Must(pastRejectedAtOffset,
                "倒计时相对规则未拒绝过去的派生时刻或未把错误归位到 offset。");
        }
        finally
        {
            if (rejectedEditor.IsVisible)
            {
                rejectedEditor.Close();
            }
        }

        var noteEditor = new ReminderRulesEditorWindow(Array.Empty<ReminderRule>(), clock)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            noteEditor.Show();
            WindowValidationInput.Pump(noteEditor.Dispatcher);
            Must(FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleMode").Items.Count == 3,
                "便签规则编辑器未只暴露绝对一次、固定间隔和日历重复类型。");
            Must(FindByAutomationId<TextBox>(noteEditor, "ReminderRuleRelativeOffsetMinutes")
                    .Visibility == Visibility.Collapsed,
                "便签规则编辑器错误暴露了倒计时相对 offset。");
            return true;
        }
        finally
        {
            noteEditor.Close();
        }
    }

    private static bool RunCalendarReminderRulesEditorWindowBehavior(DateTimeOffset nowUtc)
    {
        var clock = new AdvancingValidationClock(nowUtc);
        var noteEditor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("77777777-0000-0000-0000-000000000000")));
        var noteErrorLocalized = false;
        var noteSelectorErrorAssociated = false;
        var noteSelectorErrorCasesLocalized = false;
        var noteThemeWorked = false;
        var noteKeyboardWorked = false;
        try
        {
            noteEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                var mode = FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleMode");
                mode.SelectedIndex = 2;
                var frequency = FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleCalendarFrequency");
                var interval = FindByAutomationId<TextBox>(noteEditor, "ReminderRuleCalendarInterval");
                frequency.SelectedIndex = 1;
                interval.Text = "2";
                FindByAutomationId<DatePicker>(noteEditor, "ReminderRuleFirstDueDate").SelectedDate =
                    new DateTime(2032, 1, 5);
                FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleFirstDueHour").SelectedIndex = 9;
                FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleFirstDueMinute").SelectedIndex = 30;
                foreach (var day in Enum.GetValues<DayOfWeek>())
                {
                    FindByAutomationId<CheckBox>(noteEditor, $"ReminderRuleCalendarWeekday{day}")
                        .IsChecked = false;
                }

                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton"));
                var error = FindByAutomationId<TextBlock>(noteEditor, "ReminderRuleCalendarSelectorError");
                var form = error.Parent as Grid ??
                           throw new InvalidOperationException("日历 selector 错误不在规则表单 Grid 中。");
                var selectorInput = form.Children.Cast<UIElement>().Single(element =>
                    Grid.GetColumn(element) == 1 &&
                    Grid.GetRow(element) == Grid.GetRow(error) - 1);
                var monday = FindByAutomationId<CheckBox>(
                    noteEditor,
                    "ReminderRuleCalendarWeekdayMonday");
                noteErrorLocalized = error.Visibility == Visibility.Visible &&
                    error.Text.Contains("至少选择一个星期", StringComparison.Ordinal) &&
                    string.Equals(
                        AutomationProperties.GetHelpText(monday),
                        error.Text,
                        StringComparison.Ordinal) &&
                    monday.IsKeyboardFocusWithin;
                noteSelectorErrorAssociated =
                    string.Equals(
                        AutomationProperties.GetAutomationId(error),
                        "ReminderRuleCalendarSelectorError",
                        StringComparison.Ordinal) &&
                    selectorInput is StackPanel selectorPanel &&
                    selectorPanel.IsAncestorOf(monday) &&
                    Grid.GetRow(error) == Grid.GetRow(selectorPanel) + 1 &&
                    Grid.GetColumn(error) == Grid.GetColumn(selectorPanel);

                frequency.SelectedIndex = 2;
                var daySelector = FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleCalendarDay");
                var lastDay = FindByAutomationId<CheckBox>(noteEditor, "ReminderRuleCalendarLastDay");
                daySelector.SelectedIndex = -1;
                lastDay.IsChecked = false;
                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton"));
                var monthlyLocalized = error.Visibility == Visibility.Visible &&
                    error.Text.Contains("按月重复", StringComparison.Ordinal) &&
                    string.Equals(
                        AutomationProperties.GetHelpText(lastDay),
                        error.Text,
                        StringComparison.Ordinal) &&
                    lastDay.IsKeyboardFocusWithin;

                frequency.SelectedIndex = 3;
                FindByAutomationId<ComboBox>(noteEditor, "ReminderRuleCalendarMonth").SelectedIndex = -1;
                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton"));
                var yearlyLocalized = error.Visibility == Visibility.Visible &&
                    error.Text.Contains("按年重复", StringComparison.Ordinal) &&
                    string.Equals(
                        AutomationProperties.GetHelpText(lastDay),
                        error.Text,
                        StringComparison.Ordinal) &&
                    lastDay.IsKeyboardFocusWithin;
                noteSelectorErrorCasesLocalized = monthlyLocalized && yearlyLocalized;

                frequency.SelectedIndex = 1;
                monday.IsChecked = true;
                FindByAutomationId<CheckBox>(noteEditor, "ReminderRuleCalendarWeekdayFriday")
                    .IsChecked = true;
                noteThemeWorked = ReferenceEquals(
                    frequency.Background,
                    Application.Current.FindResource("CanvasBrush"));
                frequency.Focus();
                frequency.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                noteKeyboardWorked = interval.IsKeyboardFocusWithin;
                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "SaveReminderRuleButton"));
                InvokeRuleEditorButton(FindByAutomationId<Button>(noteEditor, "AcceptReminderRulesButton"));
            }));

            Must(noteEditor.ShowDialog() == true, "便签日历规则编辑器未接受有效草稿。");
            Must(noteErrorLocalized, "便签日历规则 selector 错误未归位。 ");
            Must(noteSelectorErrorAssociated,
                "日历 selector 错误没有与 selector 控件关联并显示在其下一行。");
            Must(noteSelectorErrorCasesLocalized,
                "monthly/yearly selector 错误没有在 selector 控件就近显示、聚焦和提供 HelpText。");
            Must(noteThemeWorked, "便签日历规则控件未使用当前主题资源。");
            Must(noteKeyboardWorked, "便签日历规则控件未进入连续 Tab 顺序。");
            Must(noteEditor.ResultRules.SingleOrDefault() is CalendarReminderRule
            {
                Frequency: CalendarFrequency.Weekly,
                Interval: 2,
                Selector: WeeklyCalendarSelector weekly
            } && weekly.Weekdays.SequenceEqual([DayOfWeek.Monday, DayOfWeek.Friday]),
                "便签日历规则未保存类型化周 selector。");
        }
        finally
        {
            if (noteEditor.IsVisible)
            {
                noteEditor.Close();
            }
        }

        var countdownEditor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            new ValidationGuidSource(Guid.Parse("88888888-0000-0000-0000-000000000000")),
            countdownTargetUtc: nowUtc.AddYears(10));
        try
        {
            countdownEditor.Dispatcher.BeginInvoke(new Action(() =>
            {
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleMode").SelectedIndex = 3;
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleCalendarFrequency").SelectedIndex = 2;
                FindByAutomationId<TextBox>(countdownEditor, "ReminderRuleCalendarInterval").Text = "1";
                FindByAutomationId<DatePicker>(countdownEditor, "ReminderRuleFirstDueDate").SelectedDate =
                    new DateTime(2031, 1, 1);
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleFirstDueHour").SelectedIndex = 10;
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleFirstDueMinute").SelectedIndex = 0;
                FindByAutomationId<ComboBox>(countdownEditor, "ReminderRuleCalendarDay").SelectedIndex = 30;
                InvokeRuleEditorButton(FindByAutomationId<Button>(countdownEditor, "SaveReminderRuleButton"));
                InvokeRuleEditorButton(FindByAutomationId<Button>(countdownEditor, "AcceptReminderRulesButton"));
            }));
            Must(countdownEditor.ShowDialog() == true, "倒计时日历规则编辑器未接受有效草稿。");
            Must(countdownEditor.ResultRules.SingleOrDefault() is CalendarReminderRule
                {
                    Frequency: CalendarFrequency.Monthly,
                    Selector: MonthlyCalendarSelector { Day: 31, LastDay: false }
                }, "倒计时日历规则未保存类型化月 selector。");
            return true;
        }
        finally
        {
            if (countdownEditor.IsVisible)
            {
                countdownEditor.Close();
            }
        }
    }

    private static bool CancelRuleEditHasVisibleLabelAndResetsForm(ReminderRulesEditorWindow editor)
    {
        editor.UpdateLayout();
        var cancel = FindByAutomationId<Button>(editor, "CancelReminderRuleEditButton");
        var labeled = cancel.IsVisible && cancel.ActualWidth > 0 && cancel.ActualHeight > 0 &&
            string.Equals(cancel.Content as string, "取消编辑", StringComparison.Ordinal);
        var originalCount = VisualDescendants<Button>(editor).Count(button =>
            string.Equals(button.Content as string, "编辑", StringComparison.Ordinal));
        InvokeRuleEditorButton(cancel);
        return labeled && !cancel.IsVisible &&
            string.Equals(FindByAutomationId<Button>(editor, "SaveReminderRuleButton").Content as string,
                "添加规则", StringComparison.Ordinal) &&
            VisualDescendants<Button>(editor).Count(button =>
                string.Equals(button.Content as string, "编辑", StringComparison.Ordinal)) == originalCount;
    }

    private static bool ProbeReminderRuleFormModes(ReminderRulesEditorWindow editor, bool supportsRelative)
    {
        var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
        var knownLabels = new[] { "规则类型", "首次提醒", "开始日期", "固定间隔", "提前分钟", "日历频率", "重复间隔", "日历选择" };
        var passed = true;
        void Check(string scenario, params string[] expected)
        {
            WindowValidationInput.Pump(editor.Dispatcher);
            editor.UpdateLayout();
            var labels = VisualDescendants<TextBlock>(editor)
                .Where(label => label.Parent is Grid &&
                    knownLabels.Contains(label.Text, StringComparer.Ordinal)).ToArray();
            var actual = labels.Where(label => label.IsVisible).Select(label => label.Text).ToArray();
            var firstDueVisible = expected.Contains("首次提醒") || expected.Contains("开始日期");
            var visibleFields = new[]
            {
                ("ReminderRuleFirstDueDate", firstDueVisible),
                ("ReminderRuleFirstDueHour", firstDueVisible),
                ("ReminderRuleFirstDueMinute", firstDueVisible),
                ("ReminderRuleInterval", expected.Contains("固定间隔")),
                ("ReminderRuleRelativeOffsetMinutes", expected.Contains("提前分钟")),
                ("ReminderRuleCalendarFrequency", expected.Contains("日历频率")),
                ("ReminderRuleCalendarInterval", expected.Contains("重复间隔"))
            }.All(field => FindByAutomationId<FrameworkElement>(editor, field.Item1).IsVisible == field.Item2);
            var timeSeparatorMatches = VisualDescendants<TextBlock>(editor)
                .Any(text => text.Text == ":" && text.IsVisible) == firstDueVisible;
            var emptyRowsCollapse = VisualDescendants<Grid>(editor)
                .Where(grid => grid.IsVisible &&
                    (grid.IsAncestorOf(mode) || labels.Any(label => ReferenceEquals(label.Parent, grid))))
                .All(grid => grid.RowDefinitions.Select((row, index) => (row, index)).All(pair =>
                    grid.Children.Cast<UIElement>().Any(child => Grid.GetRow(child) == pair.index && child.IsVisible) ||
                    pair.row.ActualHeight <= 0.01));
            var result = actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)) &&
                visibleFields && timeSeparatorMatches && emptyRowsCollapse;
            passed &= result;
            if (!result)
            {
                Console.Error.WriteLine("REMINDER_FORM_DIAGNOSTIC " + JsonSerializer.Serialize(new
                { scenario, supportsRelative, expected, actual, visibleFields, timeSeparatorMatches, emptyRowsCollapse }));
            }
        }

        mode.SelectedIndex = 0;
        Check("once", "规则类型", "首次提醒");
        mode.SelectedIndex = 1;
        Check("interval", "规则类型", "首次提醒", "固定间隔");
        FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval").SelectedIndex = 3;
        FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval").Text = "0";
        InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "SaveReminderRuleButton"));
        passed &= FindByAutomationId<TextBlock>(editor, "ReminderRuleIntervalError").IsVisible;
        mode.SelectedIndex = 0;
        Check("once-after-error", "规则类型", "首次提醒");
        passed &= !FindByAutomationId<TextBlock>(editor, "ReminderRuleIntervalError").IsVisible;
        if (supportsRelative)
        {
            mode.SelectedIndex = 2;
            Check("relative", "规则类型", "提前分钟");
        }
        mode.SelectedIndex = supportsRelative ? 3 : 2;
        FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarFrequency").SelectedIndex = 1;
        Check("calendar-weekly", "规则类型", "开始日期", "日历频率", "重复间隔", "日历选择");
        FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarFrequency").SelectedIndex = 3;
        Check("calendar-yearly", "规则类型", "开始日期", "日历频率", "重复间隔", "日历选择");
        mode.SelectedIndex = 0;
        Check("once-after-calendar", "规则类型", "首次提醒");
        return passed;
    }

    private static void SetEditorSchedule(
        ReminderRulesEditorWindow editor,
        ReminderEditingMode mode,
        DateTimeOffset due,
        int intervalMinutes)
    {
        FindByAutomationId<ComboBox>(editor, "ReminderRuleMode").SelectedIndex =
            mode == ReminderEditingMode.Once ? 0 : 1;
        FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate").SelectedDate = due.Date;
        FindByAutomationId<ComboBox>(editor, "ReminderRuleFirstDueHour").SelectedIndex = due.Hour;
        FindByAutomationId<ComboBox>(editor, "ReminderRuleFirstDueMinute").SelectedIndex = due.Minute;
        var interval = FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval");
        interval.SelectedIndex = intervalMinutes switch
        {
            15 => 0,
            30 => 1,
            60 => 2,
            _ => 3
        };
        FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval").Text =
            intervalMinutes is 15 or 30 or 60
                ? string.Empty
                : intervalMinutes.ToString(CultureInfo.InvariantCulture);
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
                $"Expected one {typeof(T).Name} with automation ID '{automationId}', found {matches.Length}.");
        }

        return matches[0];
    }

    private static bool RunMainReminderRuleDeletionBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-rule-delete-ui-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 2, 0, TimeSpan.Zero);
        var dueUtc = nowUtc.AddMinutes(1);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var workspace = DesktopWorkspace.Load(settingsPath, store);
        var rule = new AbsoluteOnceReminderRule("ui-delete-rule", dueUtc);
        var saved = workspace.Apply(new SaveNoteChange(
            null,
            "真实 WPF 删除唯一规则",
            ChinaStandardTime.ToBeijingTime(nowUtc).Date,
            false,
            Array.Empty<string>(),
            new ReminderRule[] { rule }));
        if (!saved.Succeeded)
        {
            return false;
        }

        var noteId = workspace.Snapshot.Settings.Notes.Single().Id;
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        using var coordinator = new ReminderDeliveryCoordinator(workspace, logger);
        try
        {
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            clock.UtcNow = dueUtc;
            coordinator.Tick(dueUtc);
            WindowValidationInput.Pump(main.Dispatcher);
            var before = workspace.Snapshot.Settings;
            if (coordinator.ActiveEntryCount != 1 ||
                !before.ReminderStates.ContainsKey(rule.Id))
            {
                return false;
            }

            var edit = VisualDescendants<Button>(main).Single(button =>
                string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                string.Equals(button.Tag as string, noteId, StringComparison.Ordinal));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            main.Dispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows
                    .OfType<ReminderRulesEditorWindow>()
                    .Single(window => ReferenceEquals(window.Owner, main));
                var delete = VisualDescendants<Button>(editor).Single(button =>
                    AutomationProperties.GetName(button).StartsWith(
                        "删除规则 ",
                        StringComparison.Ordinal));
                InvokeRuleEditorButton(delete);
                var complete = VisualDescendants<Button>(editor).Single(button =>
                    string.Equals(
                        AutomationProperties.GetName(button),
                        "接受提醒规则草稿",
                        StringComparison.Ordinal));
                InvokeRuleEditorButton(complete);
            }));
            ((Button)main.FindName("ManageNoteReminderRulesButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)main.FindName("SaveNoteButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);

            var memory = workspace.Snapshot.Settings;
            var disk = store.Load(settingsPath).Settings;
            return memory.Notes.Single(note => note.Id == noteId).ReminderRules.Count == 0 &&
                disk.Notes.Single(note => note.Id == noteId).ReminderRules.Count == 0 &&
                !memory.ReminderStates.ContainsKey(rule.Id) &&
                !disk.ReminderStates.ContainsKey(rule.Id) &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0;
        }
        finally
        {
            if (main.IsVisible)
            {
                main.Close();
            }

            DeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
            File.Delete(diagnosticPath);
        }
    }

    private static bool RunDelayedReminderRuleCommitBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-rule-delay-{Guid.NewGuid():N}.settings.json");
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 2, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var workspace = DesktopWorkspace.Load(settingsPath, store);
        try
        {
            var initialSave = workspace.Apply(new SaveNoteChange(
                null,
                "延迟提交规则",
                ChinaStandardTime.ToBeijingTime(nowUtc).Date,
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            if (!initialSave.Succeeded)
            {
                return false;
            }

            var note = workspace.Snapshot.Settings.Notes.Single();
            var candidateRules = new ReminderRule[]
            {
                new AbsoluteOnceReminderRule("delayed-rule", nowUtc.AddMinutes(1))
            };
            var beforeMemory = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var beforeDisk = File.ReadAllText(settingsPath);
            clock.UtcNow = nowUtc.AddMinutes(2);
            var result = workspace.Apply(new SaveNoteChange(
                note.Id,
                note.Text,
                DateTime.ParseExact(note.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                note.IsCompleted,
                note.TagIds,
                candidateRules));
            return !result.Succeeded &&
                result.ReminderRuleErrorTarget == ReminderRuleEditingErrorTarget.FirstDue &&
                string.Equals(
                    beforeMemory,
                    SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                    StringComparison.Ordinal) &&
                string.Equals(beforeDisk, File.ReadAllText(settingsPath), StringComparison.Ordinal) &&
                candidateRules.Length == 1 &&
                candidateRules[0].Id == "delayed-rule";
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }
    }

    private static bool RunHistoricalReminderRuleCommitBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-rule-history-{Guid.NewGuid():N}.settings.json");
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 20, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var historicalRule = new AbsoluteOnceReminderRule(
            "historical-rule",
            nowUtc.AddMinutes(-10));
        var historicalNote = new NoteRecord(
            "historical-note",
            "历史规则",
            "2034-05-06",
            false,
            Array.Empty<string>(),
            new ReminderRule[] { historicalRule });
        var occurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
            historicalRule,
            historicalRule.ScheduledUtc).ToString();
        try
        {
            store.Save(settingsPath, CountdownSettings.Empty with
            {
                Notes = new[] { historicalNote },
                ReminderStates = new Dictionary<string, ReminderRuleState>(StringComparer.OrdinalIgnoreCase)
                {
                    [historicalRule.Id] = new ReminderRuleState(
                        historicalRule.Id,
                        historicalRule.ScheduledUtc,
                        occurrenceKey)
                }
            });
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            var bodyEdit = workspace.Apply(new SaveNoteChange(
                historicalNote.Id,
                "历史规则正文已编辑",
                new DateTime(2034, 5, 7),
                false,
                Array.Empty<string>(),
                historicalNote.ReminderRules));
            var afterBodyEdit = workspace.Snapshot.Settings;
            var beforeRejectedChange = SettingsContract.SerializeCanonical(afterBodyEdit);
            var diskBeforeRejectedChange = File.ReadAllText(settingsPath);
            var changedPastRule = new AbsoluteOnceReminderRule(
                historicalRule.Id,
                nowUtc.AddMinutes(-5));
            var changedSchedule = workspace.Apply(new SaveNoteChange(
                historicalNote.Id,
                "不应提交的历史改期",
                new DateTime(2034, 5, 8),
                false,
                Array.Empty<string>(),
                new ReminderRule[] { changedPastRule }));
            var afterRejectedChange = workspace.Snapshot.Settings;
            var diskAfterRejectedChange = File.ReadAllText(settingsPath);
            var deletion = workspace.Apply(new SaveNoteChange(
                historicalNote.Id,
                "历史规则已停用",
                new DateTime(2034, 5, 9),
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var afterDeletion = workspace.Snapshot.Settings;
            return bodyEdit.Succeeded &&
                afterBodyEdit.Notes.Single().Text == "历史规则正文已编辑" &&
                afterBodyEdit.ReminderStates.ContainsKey(historicalRule.Id) &&
                !changedSchedule.Succeeded &&
                changedSchedule.ReminderRuleErrorTarget == ReminderRuleEditingErrorTarget.FirstDue &&
                string.Equals(
                    beforeRejectedChange,
                    SettingsContract.SerializeCanonical(afterRejectedChange),
                    StringComparison.Ordinal) &&
                string.Equals(
                    diskBeforeRejectedChange,
                    diskAfterRejectedChange,
                    StringComparison.Ordinal) &&
                deletion.Succeeded &&
                afterDeletion.Notes.Single().ReminderRules.Count == 0 &&
                !afterDeletion.ReminderStates.ContainsKey(historicalRule.Id);
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }
    }

    private static bool RunMainDelayedReminderRuleCommitBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-rule-delay-main-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2034, 5, 6, 1, 2, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var store = new SettingsStore(clock: clock);
        var workspace = DesktopWorkspace.Load(settingsPath, store);
        var initialSave = workspace.Apply(new SaveNoteChange(
            null,
            "主窗口延迟提交归位",
            ChinaStandardTime.ToBeijingTime(nowUtc).Date,
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        if (!initialSave.Succeeded)
        {
            return false;
        }

        var noteId = workspace.Snapshot.Settings.Notes.Single().Id;
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(
            workspace,
            calendar,
            new DiagnosticLogger(diagnosticPath),
            AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        var reopenedWithFirstDueError = false;
        try
        {
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            VisualDescendants<Button>(main).Single(button =>
                    string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                    string.Equals(button.Tag as string, noteId, StringComparison.Ordinal))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            main.Dispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows
                    .OfType<ReminderRulesEditorWindow>()
                    .Single(window => ReferenceEquals(window.Owner, main));
                SetEditorSchedule(
                    editor,
                    ReminderEditingMode.Once,
                    ChinaStandardTime.ToBeijingTime(nowUtc.AddMinutes(3)),
                    30);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "AcceptReminderRulesButton"));
            }));
            ((Button)main.FindName("ManageNoteReminderRulesButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            clock.UtcNow = nowUtc.AddMinutes(4);
            main.Dispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows
                    .OfType<ReminderRulesEditorWindow>()
                    .Single(window => ReferenceEquals(window.Owner, main));
                var error = FindByAutomationId<TextBlock>(editor, "ReminderRuleFirstDueError");
                reopenedWithFirstDueError = editor.IsVisible &&
                    error.Visibility == Visibility.Visible &&
                    string.Equals(
                        error.Text,
                        "首次提醒时间必须晚于当前北京时间。",
                        StringComparison.Ordinal);
                InvokeRuleEditorButton(FindByAutomationId<Button>(editor, "CancelReminderRulesButton"));
            }));
            ((Button)main.FindName("SaveNoteButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);

            var memory = workspace.Snapshot.Settings;
            var disk = store.Load(settingsPath).Settings;
            return reopenedWithFirstDueError &&
                memory.Notes.Single().ReminderRules.Count == 0 &&
                disk.Notes.Single().ReminderRules.Count == 0;
        }
        finally
        {
            if (main.IsVisible)
            {
                main.Close();
            }

            DeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
            File.Delete(diagnosticPath);
        }
    }
}
