using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #332：验证提醒中心与规则编辑器 ViewModel 已迁至 CommunityToolkit.Mvvm 源生成器，
// 保持对外属性名、访问级别、PropertyChanged 通知时机、命令 CanExecute 转换不变，且不引入消息总线。
internal static class ViewModelSourceGeneratorScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 10, 1, 0, 0, TimeSpan.Zero);

    private const string ObservablePropertyGeneratorName =
        "CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator";
    private const string RelayCommandGeneratorName =
        "CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator";

    internal static (bool Passed, string Explanation) Run(string meaning)
    {
        var failures = new List<string>();

        CheckSourceGeneratorContractAndNoMessenger(failures);
        CheckReminderCenterViewModelBehavior(failures);
        CheckReminderRulesEditorViewModelBehavior(failures);

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join(" | ", failures)}");
    }

    private static void CheckSourceGeneratorContractAndNoMessenger(List<string> failures)
    {
        Type[] viewModelTypes =
        [
            typeof(ReminderCenterViewModel),
            typeof(ReminderCenterEntryViewModel),
            typeof(ReminderRulesEditorViewModel),
            typeof(ReminderRuleItemViewModel),
            typeof(ReminderRuleWeekdayViewModel)
        ];

        foreach (var type in viewModelTypes)
        {
            if (!typeof(ObservableObject).IsAssignableFrom(type))
            {
                failures.Add($"{type.Name} 未继承 ObservableObject");
            }

            if (type.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRecipient<>)))
            {
                failures.Add($"{type.Name} 不得实现 IRecipient<> 消息总线接口");
            }

            const BindingFlags AllMembers =
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in type.GetFields(AllMembers))
            {
                if (IsMessengerType(field.FieldType))
                {
                    failures.Add($"{type.Name} 字段 {field.Name} 引入了消息总线类型 {field.FieldType.Name}");
                }
            }

            foreach (var prop in type.GetProperties(AllMembers))
            {
                if (IsMessengerType(prop.PropertyType))
                {
                    failures.Add($"{type.Name} 属性 {prop.Name} 引入了消息总线类型 {prop.PropertyType.Name}");
                }
            }

            foreach (var ctor in type.GetConstructors(AllMembers))
            {
                if (ctor.GetParameters().Any(p => IsMessengerType(p.ParameterType)))
                {
                    failures.Add($"{type.Name} 构造函数引入了消息总线参数");
                }
            }
        }

        // ReminderCenterViewModel
        AssertGeneratedCommand(typeof(ReminderCenterViewModel), nameof(ReminderCenterViewModel.AcknowledgeAllCommand), typeof(IRelayCommand), failures);
        AssertGeneratedObservableProperty(typeof(ReminderCenterViewModel), nameof(ReminderCenterViewModel.CanAcknowledgeAll), typeof(bool), publicSetter: false, failures);

        // ReminderCenterEntryViewModel
        AssertGeneratedCommand(typeof(ReminderCenterEntryViewModel), nameof(ReminderCenterEntryViewModel.AcknowledgeCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderCenterEntryViewModel), nameof(ReminderCenterEntryViewModel.SelectSnoozePresetCommand), typeof(IRelayCommand<ReminderSnoozeOption>), failures);
        AssertGeneratedCommand(typeof(ReminderCenterEntryViewModel), nameof(ReminderCenterEntryViewModel.OpenCustomSnoozeCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderCenterEntryViewModel), nameof(ReminderCenterEntryViewModel.ConfirmCustomSnoozeCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderCenterEntryViewModel), nameof(ReminderCenterEntryViewModel.CompleteCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderCenterEntryViewModel), nameof(ReminderCenterEntryViewModel.OpenSourceCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderCenterEntryViewModel), nameof(ReminderCenterEntryViewModel.ToggleTitleExpandedCommand), typeof(IRelayCommand), failures);

        foreach (var (propName, propType, publicSetter) in new (string, Type, bool)[]
                 {
                     (nameof(ReminderCenterEntryViewModel.RelativeTimeText), typeof(string), false),
                     (nameof(ReminderCenterEntryViewModel.IsAcknowledgeEnabled), typeof(bool), false),
                     (nameof(ReminderCenterEntryViewModel.IsSnoozeEnabled), typeof(bool), false),
                     (nameof(ReminderCenterEntryViewModel.IsCompleteEnabled), typeof(bool), false),
                     (nameof(ReminderCenterEntryViewModel.IsCustomSnoozeVisible), typeof(bool), false),
                     (nameof(ReminderCenterEntryViewModel.IsFailureVisible), typeof(bool), false),
                     (nameof(ReminderCenterEntryViewModel.IsCustomErrorVisible), typeof(bool), false),
                     (nameof(ReminderCenterEntryViewModel.IsRendered), typeof(bool), false),
                     (nameof(ReminderCenterEntryViewModel.IsTitleOverflowing), typeof(bool), false),
                     (nameof(ReminderCenterEntryViewModel.IsTitleExpanded), typeof(bool), false),
                     (nameof(ReminderCenterEntryViewModel.ErrorMessage), typeof(string), false),
                     (nameof(ReminderCenterEntryViewModel.CustomErrorMessage), typeof(string), false),
                     (nameof(ReminderCenterEntryViewModel.CustomDate), typeof(DateTime?), true),
                     (nameof(ReminderCenterEntryViewModel.CustomTime), typeof(TimeSpan?), true),
                     (nameof(ReminderCenterEntryViewModel.HasInvalidCustomTimeInput), typeof(bool), true)
                 })
        {
            AssertGeneratedObservableProperty(typeof(ReminderCenterEntryViewModel), propName, propType, publicSetter, failures);
        }

        // ReminderRulesEditorViewModel
        AssertGeneratedCommand(typeof(ReminderRulesEditorViewModel), nameof(ReminderRulesEditorViewModel.SaveRuleCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderRulesEditorViewModel), nameof(ReminderRulesEditorViewModel.CancelRuleEditCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderRulesEditorViewModel), nameof(ReminderRulesEditorViewModel.AcceptDraftCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderRulesEditorViewModel), nameof(ReminderRulesEditorViewModel.CancelDraftCommand), typeof(IRelayCommand), failures);

        foreach (var (propName, propType, publicSetter) in new (string, Type, bool)[]
                 {
                     (nameof(ReminderRulesEditorViewModel.EditingRuleId), typeof(string), false),
                     (nameof(ReminderRulesEditorViewModel.IsFormDirty), typeof(bool), false),
                     (nameof(ReminderRulesEditorViewModel.ModeIndex), typeof(int), true),
                     (nameof(ReminderRulesEditorViewModel.FirstDueDate), typeof(DateTime?), true),
                     (nameof(ReminderRulesEditorViewModel.FirstDueTime), typeof(TimeSpan?), true),
                     (nameof(ReminderRulesEditorViewModel.HasInvalidFirstDueTimeInput), typeof(bool), true),
                     (nameof(ReminderRulesEditorViewModel.IntervalIndex), typeof(int), true),
                     (nameof(ReminderRulesEditorViewModel.CustomIntervalMinutes), typeof(int?), true),
                     (nameof(ReminderRulesEditorViewModel.HasInvalidCustomIntervalInput), typeof(bool), true),
                     (nameof(ReminderRulesEditorViewModel.RelativeOffsetMinutes), typeof(int?), true),
                     (nameof(ReminderRulesEditorViewModel.HasInvalidRelativeOffsetInput), typeof(bool), true),
                     (nameof(ReminderRulesEditorViewModel.CalendarFrequencyIndex), typeof(int), true),
                     (nameof(ReminderRulesEditorViewModel.CalendarInterval), typeof(int?), true),
                     (nameof(ReminderRulesEditorViewModel.HasInvalidCalendarIntervalInput), typeof(bool), true),
                     (nameof(ReminderRulesEditorViewModel.CalendarMonthIndex), typeof(int), true),
                     (nameof(ReminderRulesEditorViewModel.CalendarDayIndex), typeof(int), true),
                     (nameof(ReminderRulesEditorViewModel.CalendarLastDay), typeof(bool), true),
                     (nameof(ReminderRulesEditorViewModel.SaveRuleButtonText), typeof(string), false),
                     (nameof(ReminderRulesEditorViewModel.IsCancelRuleEditVisible), typeof(bool), false),
                     (nameof(ReminderRulesEditorViewModel.ModeError), typeof(string), false),
                     (nameof(ReminderRulesEditorViewModel.FirstDueError), typeof(string), false),
                     (nameof(ReminderRulesEditorViewModel.IntervalError), typeof(string), false),
                     (nameof(ReminderRulesEditorViewModel.RelativeOffsetError), typeof(string), false),
                     (nameof(ReminderRulesEditorViewModel.CalendarIntervalError), typeof(string), false),
                     (nameof(ReminderRulesEditorViewModel.CalendarSelectorError), typeof(string), false),
                     (nameof(ReminderRulesEditorViewModel.CollectionError), typeof(string), false)
                 })
        {
            AssertGeneratedObservableProperty(typeof(ReminderRulesEditorViewModel), propName, propType, publicSetter, failures);
        }

        // ReminderRuleItemViewModel & ReminderRuleWeekdayViewModel
        AssertGeneratedCommand(typeof(ReminderRuleItemViewModel), nameof(ReminderRuleItemViewModel.EditCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderRuleItemViewModel), nameof(ReminderRuleItemViewModel.CopyCommand), typeof(IRelayCommand), failures);
        AssertGeneratedCommand(typeof(ReminderRuleItemViewModel), nameof(ReminderRuleItemViewModel.DeleteCommand), typeof(IRelayCommand), failures);
        AssertGeneratedObservableProperty(typeof(ReminderRuleWeekdayViewModel), nameof(ReminderRuleWeekdayViewModel.IsSelected), typeof(bool), publicSetter: true, failures);
    }

    private static void CheckReminderCenterViewModelBehavior(List<string> failures)
    {
        var center = new ReminderCenterViewModel();
        var centerChanges = new List<string?>();
        center.PropertyChanged += (_, args) => centerChanges.Add(args.PropertyName);

        var ackAllCanExecuteChanges = 0;
        var ackAllInvocations = 0;
        center.AcknowledgeAllCommand.CanExecuteChanged += (_, _) => ackAllCanExecuteChanges++;
        center.AcknowledgeAllRequested += (_, _) => ackAllInvocations++;

        Require(center.EntryCount == 0 &&
                center.CountText == "0 条" &&
                center.AccessibleCountText == "共 0 条提醒" &&
                !center.CanAcknowledgeAll &&
                !center.AcknowledgeAllCommand.CanExecute(null),
            "ReminderCenterViewModel 初始状态异常", failures);

        var noteKey = new ActiveReminderOccurrenceKey("sg-note-rule", FixtureUtc);
        var noteItem = new ReminderCenterItem(
            noteKey,
            new NoteRecord("sg-note", "源生成器行为验证便签标题", "2038-07-10", false, Array.Empty<string>(), Array.Empty<ReminderRule>()),
            0,
            0);
        var countdownKey = new ActiveReminderOccurrenceKey("sg-countdown-rule", FixtureUtc.AddMinutes(1));
        var countdownItem = new ReminderCenterItem(
            countdownKey,
            new CountdownReminderSource(new CountdownRecord("sg-countdown", "倒计时提醒", FixtureUtc.AddDays(1), Array.Empty<string>())),
            1,
            0);

        var entry = center.Add(noteItem);
        Require(center.EntryCount == 1 &&
                center.CountText == "1 条" &&
                center.AccessibleCountText == "共 1 条提醒" &&
                centerChanges.Contains(nameof(ReminderCenterViewModel.EntryCount)) &&
                centerChanges.Contains(nameof(ReminderCenterViewModel.CountText)) &&
                centerChanges.Contains(nameof(ReminderCenterViewModel.AccessibleCountText)) &&
                !center.CanAcknowledgeAll &&
                !center.AcknowledgeAllCommand.CanExecute(null),
            "添加未送达条目后计数通知或 CanAcknowledgeAll 异常", failures);

        var entryChanges = new List<string?>();
        entry.PropertyChanged += (_, args) => entryChanges.Add(args.PropertyName);

        var ackCanExec = 0;
        var presetCanExec = 0;
        var openCustomCanExec = 0;
        var confirmCustomCanExec = 0;
        var completeCanExec = 0;
        entry.AcknowledgeCommand.CanExecuteChanged += (_, _) => ackCanExec++;
        entry.SelectSnoozePresetCommand.CanExecuteChanged += (_, _) => presetCanExec++;
        entry.OpenCustomSnoozeCommand.CanExecuteChanged += (_, _) => openCustomCanExec++;
        entry.ConfirmCustomSnoozeCommand.CanExecuteChanged += (_, _) => confirmCustomCanExec++;
        entry.CompleteCommand.CanExecuteChanged += (_, _) => completeCanExec++;

        Require(entry.RelativeTimeText == "刚刚" &&
                entry.ErrorMessage == string.Empty &&
                entry.CustomErrorMessage == string.Empty &&
                !entry.AcknowledgeCommand.CanExecute(null) &&
                !entry.SelectSnoozePresetCommand.CanExecute(ReminderSnoozeOption.TenMinutes) &&
                !entry.SelectSnoozePresetCommand.CanExecute(ReminderSnoozeOption.Custom) &&
                !entry.OpenCustomSnoozeCommand.CanExecute(null) &&
                !entry.ConfirmCustomSnoozeCommand.CanExecute(null) &&
                !entry.CompleteCommand.CanExecute(null) &&
                entry.OpenSourceCommand.CanExecute(null) &&
                entry.ToggleTitleExpandedCommand.CanExecute(null),
            "条目送达前初始命令 CanExecute 条件异常", failures);

        // 送达就绪：命令 CanExecute 与中心 CanAcknowledgeAll 同步翻转
        centerChanges.Clear();
        entryChanges.Clear();
        entry.SetDeliveryReady();
        Require(entry.IsAcknowledgeEnabled &&
                entry.IsSnoozeEnabled &&
                entry.IsCompleteEnabled &&
                entry.AcknowledgeCommand.CanExecute(null) &&
                entry.SelectSnoozePresetCommand.CanExecute(ReminderSnoozeOption.TenMinutes) &&
                !entry.SelectSnoozePresetCommand.CanExecute(ReminderSnoozeOption.Custom) &&
                entry.OpenCustomSnoozeCommand.CanExecute(null) &&
                entry.ConfirmCustomSnoozeCommand.CanExecute(null) &&
                entry.CompleteCommand.CanExecute(null) &&
                ackCanExec == 1 &&
                presetCanExec == 1 &&
                openCustomCanExec == 1 &&
                confirmCustomCanExec == 1 &&
                completeCanExec == 1 &&
                center.CanAcknowledgeAll &&
                center.AcknowledgeAllCommand.CanExecute(null) &&
                ackAllCanExecuteChanges == 1 &&
                centerChanges.Contains(nameof(ReminderCenterViewModel.CanAcknowledgeAll)),
            "SetDeliveryReady 后命令 CanExecuteChanged 或 CanAcknowledgeAll 通知异常", failures);

        center.AcknowledgeAllCommand.Execute(null);
        Require(ackAllInvocations == 1, "AcknowledgeAllCommand.Execute 未触发 AcknowledgeAllRequested", failures);

        // 倒计时条目不能完成便签
        var countdownEntry = center.Add(countdownItem);
        countdownEntry.SetDeliveryReady();
        Require(!countdownEntry.CanComplete &&
                !countdownEntry.IsCompleteEnabled &&
                !countdownEntry.CompleteCommand.CanExecute(null) &&
                countdownEntry.AcknowledgeCommand.CanExecute(null),
            "倒计时条目的 CompleteCommand CanExecute 必须保持 false", failures);

        // 失败态禁用处置，保留打开来源
        entry.ShowFailure("写盘失败");
        Require(entry.IsFailureVisible &&
                entry.ErrorMessage == "写盘失败" &&
                !entry.AcknowledgeCommand.CanExecute(null) &&
                !entry.SelectSnoozePresetCommand.CanExecute(ReminderSnoozeOption.TenMinutes) &&
                !entry.OpenCustomSnoozeCommand.CanExecute(null) &&
                !entry.ConfirmCustomSnoozeCommand.CanExecute(null) &&
                !entry.CompleteCommand.CanExecute(null) &&
                entry.OpenSourceCommand.CanExecute(null) &&
                ackCanExec == 2 &&
                presetCanExec == 2 &&
                openCustomCanExec == 2 &&
                confirmCustomCanExec == 2 &&
                completeCanExec == 2,
            "ShowFailure 后命令 CanExecute 或失败状态异常", failures);

        // 倒计时也失败后中心 CanAcknowledgeAll 回到 false
        countdownEntry.ShowFailure("倒计时失败");
        Require(!center.CanAcknowledgeAll &&
                !center.AcknowledgeAllCommand.CanExecute(null) &&
                ackAllCanExecuteChanges == 2,
            "全部条目失败后 CanAcknowledgeAll 未回到 false 或未通知 CanExecuteChanged", failures);

        // 恢复送达，测 #321 长标题展开/收起及退出溢出自动收起
        entry.SetDeliveryReady();
        entryChanges.Clear();
        entry.SetTitleOverflowing(true);
        entry.ToggleTitleExpandedCommand.Execute(null);
        Require(entry.IsTitleExpanded &&
                entry.TitleDisclosureText == "收起" &&
                entry.TitleDisclosureStateText == "当前展开" &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.IsTitleExpanded)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.TitleDisclosureText)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.TitleDisclosureStateText)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.TitleDisclosureAccessibleName)),
            "#321 展开长标题未发出完整通知", failures);

        entryChanges.Clear();
        entry.SetTitleOverflowing(false);
        Require(!entry.IsTitleOverflowing &&
                !entry.IsTitleExpanded &&
                entry.TitleDisclosureText == "展开全文" &&
                entry.TitleDisclosureStateText == "当前收起" &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.IsTitleOverflowing)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.IsTitleExpanded)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.TitleDisclosureText)),
            "#321 退出溢出时未自动收起标题并发出通知", failures);

        // 相对时间原位更新
        entryChanges.Clear();
        center.UpdateTime(FixtureUtc.AddMinutes(5));
        Require(entry.RelativeTimeText == "已过 5 分钟" &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.RelativeTimeText)),
            "UpdateTime 未更新 RelativeTimeText 或未发通知", failures);

        // #320/#329/#313 自定义稍后、校验错误边框与 HelpText 通知、有效提交与预设提交
        var focusRequests = 0;
        var snoozeEvents = new List<ReminderCenterSnoozeEventArgs>();
        entry.CustomSnoozeValidationErrorFocusRequested += (_, _) => focusRequests++;
        entry.SnoozeRequested += (_, args) => snoozeEvents.Add(args);

        entryChanges.Clear();
        entry.OpenCustomSnoozeCommand.Execute(null);
        Require(entry.IsCustomSnoozeVisible &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.IsCustomSnoozeVisible)),
            "OpenCustomSnoozeCommand 未展开自定义稍后", failures);

        entryChanges.Clear();
        entry.ConfirmCustomSnoozeCommand.Execute(null);
        Require(entry.IsCustomErrorVisible &&
                entry.IsCustomDateErrorVisible &&
                entry.IsCustomTimeErrorVisible &&
                !string.IsNullOrEmpty(entry.CustomDateHelpText) &&
                !string.IsNullOrEmpty(entry.CustomTimeHelpText) &&
                focusRequests == 1 &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.CustomErrorMessage)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.IsCustomErrorVisible)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.IsCustomDateErrorVisible)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.IsCustomTimeErrorVisible)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.CustomDateHelpText)) &&
                entryChanges.Contains(nameof(ReminderCenterEntryViewModel.CustomTimeHelpText)),
            "不完整自定义稍后未发出错误边框与 HelpText 通知或未请求聚焦", failures);

        // 修正日期即清错，时间非法失焦报错，再修正恢复并提交
        entry.CustomDate = new DateTime(2038, 7, 10);
        Require(!entry.IsCustomErrorVisible &&
                string.IsNullOrEmpty(entry.CustomDateHelpText),
            "填入有效 CustomDate 后未清除自定义稍后错误", failures);

        entry.HasInvalidCustomTimeInput = true;
        entry.ValidateCustomTimeOnBlur();
        Require(entry.IsCustomErrorVisible &&
                entry.IsCustomTimeErrorVisible &&
                !entry.IsCustomDateErrorVisible,
            "无效时间失焦未定位到时间错误", failures);

        entry.CustomTime = new TimeSpan(10, 30, 0);
        entry.HasInvalidCustomTimeInput = false;
        Require(!entry.IsCustomErrorVisible &&
                string.IsNullOrEmpty(entry.CustomTimeHelpText),
            "恢复有效时间后未清除自定义稍后错误", failures);

        entry.ConfirmCustomSnoozeCommand.Execute(null);
        Require(snoozeEvents.Count == 1 &&
                snoozeEvents[0].Option == ReminderSnoozeOption.Custom &&
                snoozeEvents[0].CustomBeijing is { Year: 2038, Month: 7, Day: 10, Hour: 10, Minute: 30 },
            "有效自定义稍后未触发带精确北京时间的 SnoozeRequested", failures);

        entry.SelectSnoozePresetCommand.Execute(ReminderSnoozeOption.ThirtyMinutes);
        Require(!entry.IsCustomSnoozeVisible &&
                snoozeEvents.Count == 2 &&
                snoozeEvents[1].Option == ReminderSnoozeOption.ThirtyMinutes,
            "SelectSnoozePresetCommand 未收起自定义稍后或未触发预设事件", failures);

        // 知道了、完成、打开来源命令事件
        var ackCount = 0;
        var completeCount = 0;
        var openSourceCount = 0;
        entry.AcknowledgeRequested += (_, _) => ackCount++;
        entry.CompleteRequested += (_, _) => completeCount++;
        entry.OpenSourceRequested += (_, _) => openSourceCount++;
        entry.AcknowledgeCommand.Execute(null);
        entry.CompleteCommand.Execute(null);
        entry.OpenSourceCommand.Execute(null);
        Require(ackCount == 1 && completeCount == 1 && openSourceCount == 1,
            "条目处置/打开来源命令未触发对应请求事件", failures);

        Require(center.Remove(noteKey) && center.Remove(countdownKey) && center.EntryCount == 0,
            "移除条目后 EntryCount 异常", failures);
    }

    private static void CheckReminderRulesEditorViewModelBehavior(List<string> failures)
    {
        var clock = new FixedClock(FixtureUtc);
        var targetUtc = FixtureUtc.AddDays(10);
        var vm = new ReminderRulesEditorViewModel(Array.Empty<ReminderRule>(), clock, countdownTargetUtc: targetUtc);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        Require(!vm.IsFormDirty &&
                vm.RuleCount == 0 &&
                vm.CountText == "0 / 8 条" &&
                vm.IsEmpty &&
                vm.EmptyText == "当前不提醒" &&
                vm.SaveRuleButtonText == "添加规则" &&
                !vm.IsCancelRuleEditVisible,
            "ReminderRulesEditorViewModel 初始状态异常", failures);

        // #323 实时预览与 #322 脏态通知
        changes.Clear();
        vm.FirstDueDate = new DateTime(2038, 7, 11);
        Require(vm.IsFormDirty &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.FirstDueDate)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.IsFormDirty)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.RulePreviewText)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.IsRulePreviewValid)),
            "修改 FirstDueDate 未同步发出脏态与实时预览通知", failures);

        // #329/#313 失焦时间校验与 HelpText 通知
        changes.Clear();
        vm.FirstDueTime = null;
        vm.ValidateFirstDueTimeOnBlur();
        Require(vm.IsFirstDueErrorVisible &&
                vm.IsFirstDueTimeErrorVisible &&
                !vm.IsFirstDueDateErrorVisible &&
                !string.IsNullOrEmpty(vm.FirstDueTimeHelpText) &&
                string.IsNullOrEmpty(vm.FirstDueDateHelpText) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.FirstDueError)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.IsFirstDueTimeErrorVisible)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.FirstDueTimeHelpText)),
            "FirstDueTime 失焦校验未精确通知错误与 HelpText", failures);

        vm.FirstDueTime = new TimeSpan(10, 15, 0);
        Require(!vm.IsFirstDueErrorVisible && string.IsNullOrEmpty(vm.FirstDueTimeHelpText),
            "恢复有效 FirstDueTime 后未清错", failures);

        // #330/#331 数值即刻校验与清错
        vm.ModeIndex = 1;
        vm.IntervalIndex = 3;
        changes.Clear();
        vm.CustomIntervalMinutes = 0;
        Require(vm.IsIntervalErrorVisible &&
                !vm.IsIntervalComboErrorVisible &&
                !string.IsNullOrEmpty(vm.CustomIntervalHelpText) &&
                !vm.IsRulePreviewValid &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.IntervalError)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.CustomIntervalHelpText)),
            "CustomIntervalMinutes 越界未当场报错与通知 CustomIntervalHelpText", failures);
        vm.CustomIntervalMinutes = 30;
        Require(!vm.IsIntervalErrorVisible && vm.IsRulePreviewValid, "CustomIntervalMinutes 合法值未清错", failures);

        vm.ModeIndex = 2;
        vm.RelativeOffsetMinutes = -1;
        Require(vm.IsRelativeOffsetErrorVisible && !vm.IsRulePreviewValid, "RelativeOffsetMinutes 越界未当场报错", failures);
        vm.RelativeOffsetMinutes = 15;
        Require(!vm.IsRelativeOffsetErrorVisible && vm.IsRulePreviewValid, "RelativeOffsetMinutes 合法值未清错", failures);

        // 日历模式：每周自动勾选星期、Weekday IsSelected 同步预览与脏态、周期数越界校验
        vm.ModeIndex = 3;
        vm.CalendarFrequencyIndex = 1; // 每周
        Require(vm.IsCalendarWeekdaysVisible &&
                vm.Weekdays.Any(static w => w.IsSelected) &&
                vm.IsRulePreviewValid,
            "切到每周周期未自动勾选默认星期或预览无效", failures);

        changes.Clear();
        var monday = vm.Weekdays.Single(static w => w.Day == DayOfWeek.Monday);
        monday.IsSelected = !monday.IsSelected;
        Require(changes.Contains(nameof(ReminderRulesEditorViewModel.RulePreviewText)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.IsRulePreviewValid)),
            "切换星期复选未刷新实时预览通知", failures);

        vm.CalendarInterval = 0;
        Require(vm.IsCalendarIntervalErrorVisible && !vm.IsRulePreviewValid, "CalendarInterval=0 未当场报错", failures);
        vm.CalendarInterval = 1;
        Require(!vm.IsCalendarIntervalErrorVisible && vm.IsRulePreviewValid, "CalendarInterval=1 未清错", failures);

        // 每年选择器属性通知 + 每周空选触发 CalendarSelectorError 通知
        vm.CalendarFrequencyIndex = 3; // 每年
        changes.Clear();
        vm.CalendarMonthIndex = 7;
        vm.CalendarDayIndex = 14;
        vm.CalendarLastDay = true;
        Require(changes.Contains(nameof(ReminderRulesEditorViewModel.CalendarMonthIndex)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.CalendarDayIndex)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.CalendarLastDay)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.IsCalendarDayVisible)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.RulePreviewText)),
            "按年日历选择器属性变更未触发预期通知", failures);
        vm.CalendarLastDay = false;

        vm.CalendarFrequencyIndex = 1; // 回到每周并清空所有星期，触发 CalendarSelectorError
        foreach (var w in vm.Weekdays)
        {
            w.IsSelected = false;
        }
        changes.Clear();
        vm.SaveRuleCommand.Execute(null);
        Require(vm.IsCalendarSelectorErrorVisible &&
                vm.IsCalendarSelectorVisible &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.CalendarSelectorError)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.IsCalendarSelectorErrorVisible)) &&
                changes.Contains(nameof(ReminderRulesEditorViewModel.IsCalendarSelectorVisible)),
            "每周未选星期保存失败未触发 CalendarSelectorError 及其依赖通知", failures);

        monday.IsSelected = true;
        Require(!vm.IsCalendarSelectorErrorVisible && vm.IsRulePreviewValid,
            "重新勾选星期后未清除 CalendarSelectorError", failures);

        // 保存规则 + ItemViewModel Edit / Copy / Delete + CancelRuleEdit
        var focusEvents = 0;
        vm.FormFocusRequested += (_, _) => focusEvents++;
        vm.SaveRuleCommand.Execute(null);
        Require(vm.RuleCount == 1 &&
                vm.CountText == "1 / 8 条" &&
                !vm.IsEmpty &&
                !vm.IsFormDirty &&
                vm.EditingRuleId is null,
            "SaveRuleCommand 保存后集合状态或脏态异常", failures);

        if (vm.RuleItems.Count == 1)
        {
            var item = vm.RuleItems[0];
            item.EditCommand.Execute(null);
            Require(vm.EditingRuleId == item.RuleId &&
                    vm.SaveRuleButtonText == "保存修改" &&
                    vm.IsCancelRuleEditVisible &&
                    !vm.IsFormDirty &&
                    focusEvents == 1,
                "ReminderRuleItemViewModel.EditCommand 回填状态异常", failures);

            vm.CancelRuleEditCommand.Execute(null);
            Require(vm.EditingRuleId is null &&
                    vm.SaveRuleButtonText == "添加规则" &&
                    !vm.IsCancelRuleEditVisible &&
                    !vm.IsFormDirty,
                "CancelRuleEditCommand 重置状态异常", failures);

            item.CopyCommand.Execute(null);
            Require(vm.EditingRuleId is null &&
                    vm.SaveRuleButtonText == "保存副本" &&
                    vm.IsCancelRuleEditVisible &&
                    !vm.IsFormDirty &&
                    focusEvents == 2,
                "ReminderRuleItemViewModel.CopyCommand 回填状态异常", failures);

            item.DeleteCommand.Execute(null);
            Require(vm.RuleCount == 0 && vm.IsEmpty, "ReminderRuleItemViewModel.DeleteCommand 删除规则失败", failures);
        }

        // #322 脏态完成拦截无效输入、自动提交有效输入，以及取消恢复初始草稿
        var accepted = 0;
        var cancelled = 0;
        var validationErrors = new List<ReminderRuleEditorErrorEventArgs>();
        vm.DraftAccepted += (_, _) => accepted++;
        vm.DraftCancelled += (_, _) => cancelled++;
        vm.ValidationErrorRaised += (_, args) => validationErrors.Add(args);

        vm.ModeIndex = 1;
        vm.IntervalIndex = 3;
        vm.CustomIntervalMinutes = 0;
        vm.AcceptDraftCommand.Execute(null);
        Require(accepted == 0 &&
                vm.IsFormDirty &&
                validationErrors.Count == 1 &&
                validationErrors[0].Target == ReminderRuleEditingErrorTarget.Interval,
            "脏态无效输入下 AcceptDraftCommand 未拦错", failures);

        vm.CustomIntervalMinutes = 45;
        vm.AcceptDraftCommand.Execute(null);
        Require(accepted == 1 &&
                !vm.IsFormDirty &&
                vm.ResultRules.Count == 1 &&
                vm.ResultRules[0] is FixedIntervalReminderRule { Interval.Minutes: 45 },
            "脏态有效输入下 AcceptDraftCommand 未自动保存并接受", failures);

        vm.CancelDraftCommand.Execute(null);
        Require(cancelled == 1 && vm.ResultRules.Count == 0,
            "CancelDraftCommand 未恢复初始规则并触发取消事件", failures);
    }

    private static void AssertGeneratedCommand(
        Type ownerType,
        string propertyName,
        Type expectedType,
        List<string> failures)
    {
        var prop = ownerType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop is null)
        {
            failures.Add($"{ownerType.Name} 缺少公开命令属性 {propertyName}");
            return;
        }

        if (prop.PropertyType != expectedType)
        {
            failures.Add($"{ownerType.Name}.{propertyName} 类型期望 {expectedType.Name}，实际 {prop.PropertyType.Name}");
        }

        if (prop.SetMethod is not null)
        {
            failures.Add($"{ownerType.Name}.{propertyName} 不应包含 setter");
        }

        var generated = prop.GetCustomAttribute<GeneratedCodeAttribute>();
        if (generated is null || !string.Equals(generated.Tool, RelayCommandGeneratorName, StringComparison.Ordinal))
        {
            failures.Add(
                $"{ownerType.Name}.{propertyName} 未由 {RelayCommandGeneratorName} 生成（actual={generated?.Tool ?? "<null>"}）");
        }
    }

    private static void AssertGeneratedObservableProperty(
        Type ownerType,
        string propertyName,
        Type expectedType,
        bool publicSetter,
        List<string> failures)
    {
        var prop = ownerType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop is null)
        {
            failures.Add($"{ownerType.Name} 缺少公开属性 {propertyName}");
            return;
        }

        if (prop.PropertyType != expectedType)
        {
            failures.Add($"{ownerType.Name}.{propertyName} 类型期望 {expectedType.Name}，实际 {prop.PropertyType.Name}");
        }

        var setMethod = prop.GetSetMethod(nonPublic: true);
        if (setMethod is null)
        {
            failures.Add($"{ownerType.Name}.{propertyName} 缺少 setter");
        }
        else if (publicSetter && !setMethod.IsPublic)
        {
            failures.Add($"{ownerType.Name}.{propertyName} 期望公开 setter，实际非公开");
        }
        else if (!publicSetter && setMethod.IsPublic)
        {
            failures.Add($"{ownerType.Name}.{propertyName} 期望非公开 setter，实际被放宽为 public");
        }

        var generated = prop.GetCustomAttribute<GeneratedCodeAttribute>();
        if (generated is null || !string.Equals(generated.Tool, ObservablePropertyGeneratorName, StringComparison.Ordinal))
        {
            failures.Add(
                $"{ownerType.Name}.{propertyName} 未由 {ObservablePropertyGeneratorName} 生成（actual={generated?.Tool ?? "<null>"}）");
        }
    }

    private static bool IsMessengerType(Type type) =>
        type == typeof(IMessenger) ||
        type == typeof(WeakReferenceMessenger) ||
        type == typeof(StrongReferenceMessenger) ||
        typeof(IMessenger).IsAssignableFrom(type);

    private static void Require(bool condition, string message, List<string> failures)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}
