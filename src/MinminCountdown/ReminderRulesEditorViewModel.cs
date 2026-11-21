using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinminCountdown.Core;

namespace MinminCountdown;

/// <summary>
/// Owns the isolated draft used by <see cref="ReminderRulesEditorWindow"/>.
/// The window builds the WPF surface and binds it to this object; validation,
/// draft commands and all mutable form state stay here.
/// </summary>
internal sealed partial class ReminderRulesEditorViewModel : ObservableObject
{
    internal const string IncompleteRulePreviewText = "请完善上方设置以生成预览";
    internal const int MinimumFixedIntervalMinutes = 1;
    internal const int MaximumFixedIntervalMinutes = 1440;
    internal const string InvalidFixedIntervalError = "自定义提醒必须是 1 到 1440 分钟。";
    internal const int MinimumRelativeOffsetMinutes = 0;
    internal const int MaximumRelativeOffsetMinutes = 5_256_000;
    internal const string InvalidRelativeOffsetError = "倒计时相对提醒必须提前 0 到 5256000 分钟，且派生时间晚于当前北京时间。";
    internal const int MinimumCalendarInterval = 1;
    internal const int MaximumCalendarInterval = 999;
    internal const string InvalidCalendarIntervalError = "每隔几个周期必须是 1 到 999。";
    private const string IncompleteFirstDueError = "请选择完整的首次提醒日期和时间。";
    private const string IncompleteCalendarScheduleError = "请选择完整的日历起始日期和本地时间。";
    private const string IncompleteYearlyCalendarSelectorError = "按年重复必须选择月份及 1 到 31 日或显式最后一天。";

    private const int CalendarModeIndexWithoutCountdown = 2;
    private const int CalendarModeIndexWithCountdown = 3;

    private readonly ReminderRuleEditing _editor;
    private readonly ReminderEditing _scheduleEditor = new();
    private readonly IClock _clock;
    private readonly DateTimeOffset? _countdownTargetUtc;
    private readonly IReadOnlyList<ReminderRule> _initialRules;
    private readonly ObservableCollection<ReminderRuleItemViewModel> _ruleItems = new();
    private readonly ReadOnlyObservableCollection<ReminderRuleItemViewModel> _readOnlyRuleItems;
    private bool _updatingForm;
    private IReadOnlyList<ReminderRule> _rules;
    private IReadOnlyList<ReminderRule> _resultRules;

    public ReminderRulesEditorViewModel(
        IReadOnlyList<ReminderRule> initialRules,
        IClock clock,
        IGuidSource? guidSource = null,
        ReminderRuleEditingErrorTarget initialErrorTarget = ReminderRuleEditingErrorTarget.None,
        string initialError = "",
        DateTimeOffset? countdownTargetUtc = null)
    {
        ArgumentNullException.ThrowIfNull(initialRules);
        ArgumentNullException.ThrowIfNull(clock);

        _initialRules = Freeze(initialRules);
        _rules = Freeze(initialRules);
        _resultRules = _initialRules;
        _clock = clock;
        _countdownTargetUtc = countdownTargetUtc?.ToUniversalTime();
        _editor = new ReminderRuleEditing(guidSource);
        InitialErrorTarget = initialErrorTarget;
        InitialError = initialError;
        _readOnlyRuleItems = new ReadOnlyObservableCollection<ReminderRuleItemViewModel>(_ruleItems);

        ModeOptions = _countdownTargetUtc is null
            ? new[] { "提醒一次", "每隔一段时间", "按日历重复" }
            : new[] { "提醒一次", "每隔一段时间", "截止前提醒", "按日历重复" };
        IntervalOptions = new[] { "15 分钟", "30 分钟", "60 分钟", "自定义" };
        CalendarFrequencyOptions = new[] { "每天", "每周", "每月", "每年" };
        Months = Enumerable.Range(1, 12)
            .Select(static month => month.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        Days = Enumerable.Range(1, 31)
            .Select(static day => day.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        Weekdays = new ReadOnlyObservableCollection<ReminderRuleWeekdayViewModel>(
            new ObservableCollection<ReminderRuleWeekdayViewModel>(
                new[]
                {
                    (DayOfWeek.Monday, "一"),
                    (DayOfWeek.Tuesday, "二"),
                    (DayOfWeek.Wednesday, "三"),
                    (DayOfWeek.Thursday, "四"),
                    (DayOfWeek.Friday, "五"),
                    (DayOfWeek.Saturday, "六"),
                    (DayOfWeek.Sunday, "日")
                }.Select(pair => new ReminderRuleWeekdayViewModel(
                    pair.Item1,
                    pair.Item2,
                    MarkFormDirty))));

        SaveRuleButtonText = "添加规则";
        ModeError = string.Empty;
        FirstDueError = string.Empty;
        IntervalError = string.Empty;
        RelativeOffsetError = string.Empty;
        CalendarIntervalError = string.Empty;
        CalendarSelectorError = string.Empty;
        CollectionError = string.Empty;

        ReplaceRules(_rules);
        BeginAddRule();
    }

    public IReadOnlyList<string> ModeOptions { get; }

    public IReadOnlyList<string> IntervalOptions { get; }

    public IReadOnlyList<string> CalendarFrequencyOptions { get; }

    public IReadOnlyList<string> Months { get; }

    public IReadOnlyList<string> Days { get; }

    public ReadOnlyObservableCollection<ReminderRuleWeekdayViewModel> Weekdays { get; }

    public ReadOnlyObservableCollection<ReminderRuleItemViewModel> RuleItems => _readOnlyRuleItems;

    public int RuleCount => _rules.Count;

    public string CountText => $"{RuleCount} / 8 条";

    public bool IsEmpty => RuleCount == 0;

    public string EmptyText => "当前不提醒";

    [ObservableProperty]
    public partial string? EditingRuleId { get; private set; }

    [ObservableProperty]
    public partial bool IsFormDirty { get; private set; }

    public IReadOnlyList<ReminderRule> ResultRules => _resultRules;

    public ReminderRuleEditingErrorTarget InitialErrorTarget { get; }

    public string InitialError { get; }

    public bool HasInitialError =>
        InitialErrorTarget != ReminderRuleEditingErrorTarget.None &&
        !string.IsNullOrWhiteSpace(InitialError);

    public DateTimeOffset? CountdownTargetUtc => _countdownTargetUtc;

    [ObservableProperty]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstDueDateErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsFirstDueTimeErrorVisible))]
    [NotifyPropertyChangedFor(nameof(FirstDueDateHelpText))]
    [NotifyPropertyChangedFor(nameof(FirstDueTimeHelpText))]
    public partial DateTime? FirstDueDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstDueDateErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsFirstDueTimeErrorVisible))]
    [NotifyPropertyChangedFor(nameof(FirstDueDateHelpText))]
    [NotifyPropertyChangedFor(nameof(FirstDueTimeHelpText))]
    public partial TimeSpan? FirstDueTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstDueDateErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsFirstDueTimeErrorVisible))]
    [NotifyPropertyChangedFor(nameof(FirstDueDateHelpText))]
    [NotifyPropertyChangedFor(nameof(FirstDueTimeHelpText))]
    public partial bool HasInvalidFirstDueTimeInput { get; set; }

    [ObservableProperty]
    public partial int IntervalIndex { get; set; }

    [ObservableProperty]
    public partial int? CustomIntervalMinutes { get; set; }

    [ObservableProperty]
    public partial bool HasInvalidCustomIntervalInput { get; set; }

    [ObservableProperty]
    public partial int? RelativeOffsetMinutes { get; set; }

    [ObservableProperty]
    public partial bool HasInvalidRelativeOffsetInput { get; set; }

    [ObservableProperty]
    public partial int CalendarFrequencyIndex { get; set; }

    [ObservableProperty]
    public partial int? CalendarInterval { get; set; }

    [ObservableProperty]
    public partial bool HasInvalidCalendarIntervalInput { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCalendarFrequencyErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCalendarMonthErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCalendarDayErrorVisible))]
    [NotifyPropertyChangedFor(nameof(CalendarFrequencyHelpText))]
    [NotifyPropertyChangedFor(nameof(CalendarMonthHelpText))]
    [NotifyPropertyChangedFor(nameof(CalendarDayHelpText))]
    public partial int CalendarMonthIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCalendarFrequencyErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCalendarMonthErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCalendarDayErrorVisible))]
    [NotifyPropertyChangedFor(nameof(CalendarFrequencyHelpText))]
    [NotifyPropertyChangedFor(nameof(CalendarMonthHelpText))]
    [NotifyPropertyChangedFor(nameof(CalendarDayHelpText))]
    public partial int CalendarDayIndex { get; set; }

    [ObservableProperty]
    public partial bool CalendarLastDay { get; set; }

    public string FirstDueLabel => IsCalendarMode ? "开始日期" : "首次提醒";

    public string FirstDueDateAutomationName => IsCalendarMode ? "开始日期" : "首次提醒日期";

    public string FirstDueTimeAutomationName => IsCalendarMode ? "本地时刻" : "首次提醒时刻";

    public bool IsFirstDueVisible => !IsRelativeMode;

    public bool IsIntervalVisible => IsRepeatingMode;

    public bool IsRelativeVisible => IsRelativeMode;

    public bool IsCalendarFrequencyVisible => IsCalendarMode;

    public bool IsCalendarIntervalVisible => IsCalendarMode;

    public bool IsCalendarSelectorControlsVisible =>
        IsCalendarWeekdaysVisible || IsCalendarDateSelectorVisible;

    public bool IsCalendarSelectorVisible => IsCalendarMode &&
        (IsCalendarSelectorControlsVisible || IsCalendarSelectorErrorVisible);

    public bool IsCalendarWeekdaysVisible => IsCalendarMode && SelectedCalendarFrequency == CalendarFrequency.Weekly;

    public bool IsCalendarDateSelectorVisible => IsCalendarMode &&
        SelectedCalendarFrequency is CalendarFrequency.Monthly or CalendarFrequency.Yearly;

    public bool IsCalendarMonthVisible => IsCalendarMode && SelectedCalendarFrequency == CalendarFrequency.Yearly;

    public bool IsCalendarDayVisible => !CalendarLastDay;

    public bool IsCustomIntervalVisible => IsRepeatingMode && IntervalIndex == 3;

    public bool IsRulePreviewValid => TryBuildPreviewRule(out _);

    public string RulePreviewText => TryBuildPreviewRule(out var previewRule)
        ? Describe(previewRule!)
        : IncompleteRulePreviewText;

    [ObservableProperty]
    public partial string SaveRuleButtonText { get; private set; }

    [ObservableProperty]
    public partial bool IsCancelRuleEditVisible { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModeErrorVisible))]
    public partial string ModeError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstDueErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsFirstDueDateErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsFirstDueTimeErrorVisible))]
    [NotifyPropertyChangedFor(nameof(FirstDueDateHelpText))]
    [NotifyPropertyChangedFor(nameof(FirstDueTimeHelpText))]
    public partial string FirstDueError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIntervalErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsIntervalComboErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IntervalComboHelpText))]
    [NotifyPropertyChangedFor(nameof(CustomIntervalHelpText))]
    public partial string IntervalError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRelativeOffsetErrorVisible))]
    public partial string RelativeOffsetError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCalendarIntervalErrorVisible))]
    public partial string CalendarIntervalError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCalendarSelectorErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCalendarSelectorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCalendarFrequencyErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCalendarMonthErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCalendarDayErrorVisible))]
    [NotifyPropertyChangedFor(nameof(CalendarFrequencyHelpText))]
    [NotifyPropertyChangedFor(nameof(CalendarMonthHelpText))]
    [NotifyPropertyChangedFor(nameof(CalendarDayHelpText))]
    public partial string CalendarSelectorError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollectionErrorVisible))]
    public partial string CollectionError { get; private set; }

    public bool IsModeErrorVisible => !string.IsNullOrEmpty(ModeError);

    public bool IsFirstDueErrorVisible => !string.IsNullOrEmpty(FirstDueError);

    public bool IsFirstDueDateErrorVisible =>
        IsFirstDueErrorVisible &&
        (FirstDueDate is null || (FirstDueTime is not null && !HasInvalidFirstDueTimeInput));

    public bool IsFirstDueTimeErrorVisible =>
        IsFirstDueErrorVisible &&
        (FirstDueTime is null || HasInvalidFirstDueTimeInput || FirstDueDate is not null);

    public string FirstDueDateHelpText => IsFirstDueDateErrorVisible ? FirstDueError : string.Empty;

    public string FirstDueTimeHelpText => IsFirstDueTimeErrorVisible ? FirstDueError : string.Empty;

    public bool IsIntervalErrorVisible => !string.IsNullOrEmpty(IntervalError);

    public bool IsIntervalComboErrorVisible => IsIntervalErrorVisible && !IsCustomIntervalVisible;

    public string IntervalComboHelpText => IsIntervalComboErrorVisible ? IntervalError : string.Empty;

    public string CustomIntervalHelpText => IsIntervalErrorVisible ? IntervalError : string.Empty;

    public bool IsRelativeOffsetErrorVisible => !string.IsNullOrEmpty(RelativeOffsetError);

    public bool IsCalendarIntervalErrorVisible => !string.IsNullOrEmpty(CalendarIntervalError);

    public bool IsCalendarSelectorErrorVisible => !string.IsNullOrEmpty(CalendarSelectorError);

    public bool IsCalendarFrequencyErrorVisible =>
        IsCalendarSelectorErrorVisible &&
        (SelectedCalendarFrequency is not (CalendarFrequency.Daily or CalendarFrequency.Weekly or CalendarFrequency.Monthly or CalendarFrequency.Yearly) ||
         SelectedCalendarFrequency == CalendarFrequency.Daily);

    public bool IsCalendarMonthErrorVisible =>
        IsCalendarSelectorErrorVisible &&
        SelectedCalendarFrequency == CalendarFrequency.Yearly &&
        (CalendarMonthIndex < 0 || CalendarLastDay || CalendarDayIndex >= 0);

    public bool IsCalendarDayErrorVisible =>
        IsCalendarSelectorErrorVisible &&
        !CalendarLastDay &&
        (SelectedCalendarFrequency == CalendarFrequency.Monthly ||
         (SelectedCalendarFrequency == CalendarFrequency.Yearly &&
          (CalendarDayIndex < 0 || CalendarMonthIndex >= 0)));

    public string CalendarFrequencyHelpText => IsCalendarFrequencyErrorVisible ? CalendarSelectorError : string.Empty;

    public string CalendarMonthHelpText => IsCalendarMonthErrorVisible ? CalendarSelectorError : string.Empty;

    public string CalendarDayHelpText => IsCalendarDayErrorVisible ? CalendarSelectorError : string.Empty;

    public bool IsCollectionErrorVisible => !string.IsNullOrEmpty(CollectionError);

    internal event EventHandler? DraftAccepted;

    internal event EventHandler? DraftCancelled;

    internal event EventHandler<ReminderRuleEditorErrorEventArgs>? ValidationErrorRaised;

    internal event EventHandler? FormFocusRequested;

    internal void ShowInitialError()
    {
        if (HasInitialError)
        {
            ShowError(InitialErrorTarget, InitialError);
        }
    }

    internal void ClearValidationErrors() => ClearErrors();

    internal void ValidateFirstDueTimeOnBlur()
    {
        if (_updatingForm || !IsFirstDueVisible)
        {
            return;
        }

        if (HasInvalidFirstDueTimeInput || FirstDueTime is null)
        {
            FirstDueError = IsCalendarMode
                ? IncompleteCalendarScheduleError
                : IncompleteFirstDueError;
        }
        else if (FirstDueDate is not null &&
                 (string.Equals(FirstDueError, IncompleteFirstDueError, StringComparison.Ordinal) ||
                  string.Equals(FirstDueError, IncompleteCalendarScheduleError, StringComparison.Ordinal)))
        {
            FirstDueError = string.Empty;
        }
    }

    private bool IsRelativeMode =>
        _countdownTargetUtc is not null && ModeIndex == 2;

    private bool IsRepeatingMode => ModeIndex == 1;

    private bool IsCalendarMode => ModeIndex == CalendarModeIndex;

    private int CalendarModeIndex => _countdownTargetUtc is null
        ? CalendarModeIndexWithoutCountdown
        : CalendarModeIndexWithCountdown;

    private CalendarFrequency SelectedCalendarFrequency => CalendarFrequencyIndex switch
    {
        0 => CalendarFrequency.Daily,
        1 => CalendarFrequency.Weekly,
        2 => CalendarFrequency.Monthly,
        3 => CalendarFrequency.Yearly,
        _ => (CalendarFrequency)(-1)
    };

    private ReminderIntervalSelection SelectedInterval => IntervalIndex switch
    {
        0 => ReminderIntervalSelection.FifteenMinutes,
        1 => ReminderIntervalSelection.ThirtyMinutes,
        2 => ReminderIntervalSelection.SixtyMinutes,
        _ => ReminderIntervalSelection.Custom
    };

    partial void OnModeIndexChanged(int value)
    {
        OnFormFieldChanged(clearPairedIncompleteErrors: true);
        EnsureWeeklySelection();
        UpdateVisibility();
    }

    partial void OnFirstDueDateChanged(DateTime? value) => OnFormFieldChanged();

    partial void OnFirstDueTimeChanged(TimeSpan? value) => OnFormFieldChanged();

    partial void OnHasInvalidFirstDueTimeInputChanged(bool value) => OnFormFieldChanged();

    partial void OnIntervalIndexChanged(int value)
    {
        OnFormFieldChanged();
        UpdateVisibility();
    }

    partial void OnCustomIntervalMinutesChanged(int? value) => OnFormFieldChanged();

    partial void OnHasInvalidCustomIntervalInputChanged(bool value) => OnFormFieldChanged();

    partial void OnRelativeOffsetMinutesChanged(int? value) => OnFormFieldChanged();

    partial void OnHasInvalidRelativeOffsetInputChanged(bool value) => OnFormFieldChanged();

    partial void OnCalendarFrequencyIndexChanged(int value)
    {
        OnFormFieldChanged();
        EnsureWeeklySelection();
        UpdateVisibility();
    }

    partial void OnCalendarIntervalChanged(int? value) => OnFormFieldChanged();

    partial void OnHasInvalidCalendarIntervalInputChanged(bool value) => OnFormFieldChanged();

    partial void OnCalendarMonthIndexChanged(int value) => OnFormFieldChanged();

    partial void OnCalendarDayIndexChanged(int value) => OnFormFieldChanged();

    partial void OnCalendarLastDayChanged(bool value)
    {
        OnFormFieldChanged();
        UpdateVisibility();
    }

    [RelayCommand]
    private void CancelRuleEdit() => BeginAddRule();

    private void BeginAddRule()
    {
        _updatingForm = true;
        try
        {
            EditingRuleId = null;
            ModeIndex = 0;
            IntervalIndex = 1;
            CustomIntervalMinutes = null;
            HasInvalidCustomIntervalInput = false;
            RelativeOffsetMinutes = 0;
            HasInvalidRelativeOffsetInput = false;
            CalendarFrequencyIndex = 0;
            CalendarInterval = 1;
            HasInvalidCalendarIntervalInput = false;
            CalendarMonthIndex = 0;
            CalendarDayIndex = 0;
            CalendarLastDay = false;
            foreach (var weekday in Weekdays)
            {
                weekday.IsSelected = false;
            }

            var defaultDue = _scheduleEditor.GetDefaultFirstDueBeijing(
                ReminderEditingMode.Once,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                _clock.UtcNow).FirstDueBeijing;
            SetSchedule(defaultDue);
            SaveRuleButtonText = "添加规则";
            IsCancelRuleEditVisible = false;
            ClearErrors();
        }
        finally
        {
            _updatingForm = false;
            IsFormDirty = false;
        }

        UpdateVisibility();
    }

    private void BeginEditRule(ReminderRule rule, bool copy)
    {
        _updatingForm = true;
        try
        {
            EditingRuleId = copy ? null : rule.Id;
            var dueUtc = rule switch
            {
                AbsoluteOnceReminderRule once => once.ScheduledUtc,
                FixedIntervalReminderRule repeating => repeating.FirstDueUtc,
                CountdownRelativeReminderRule => (DateTimeOffset?)null,
                CalendarReminderRule calendarSchedule => new DateTimeOffset(
                    calendarSchedule.StartDate.ToDateTime(calendarSchedule.LocalTime, DateTimeKind.Unspecified),
                    TimeSpan.FromHours(8)),
                _ => throw new ArgumentOutOfRangeException(nameof(rule))
            };
            ModeIndex = rule switch
            {
                FixedIntervalReminderRule => 1,
                CountdownRelativeReminderRule when _countdownTargetUtc is not null => 2,
                CountdownRelativeReminderRule => throw new InvalidOperationException(
                    "便签提醒编辑器不能编辑倒计时相对规则。"),
                CalendarReminderRule => CalendarModeIndex,
                _ => 0
            };
            HasInvalidCustomIntervalInput = false;
            RelativeOffsetMinutes = 0;
            HasInvalidRelativeOffsetInput = false;
            CalendarInterval = 1;
            HasInvalidCalendarIntervalInput = false;
            if (rule is FixedIntervalReminderRule fixedInterval)
            {
                var minutes = fixedInterval.Interval.Minutes;
                IntervalIndex = minutes switch
                {
                    15 => 0,
                    30 => 1,
                    60 => 2,
                    _ => 3
                };
                CustomIntervalMinutes = minutes is 15 or 30 or 60
                    ? null
                    : minutes;
                HasInvalidCustomIntervalInput = false;
            }

            if (rule is CountdownRelativeReminderRule relative)
            {
                RelativeOffsetMinutes = relative.OffsetMinutesBefore;
                HasInvalidRelativeOffsetInput = false;
            }

            if (rule is CalendarReminderRule calendar)
            {
                CalendarFrequencyIndex = (int)calendar.Frequency;
                CalendarInterval = calendar.Interval;
                HasInvalidCalendarIntervalInput = false;
                foreach (var weekday in Weekdays)
                {
                    weekday.IsSelected = calendar.Selector is WeeklyCalendarSelector weekly &&
                        weekly.Weekdays.Contains(weekday.Day);
                }

                CalendarMonthIndex = calendar.Selector is YearlyCalendarSelector yearSelector
                    ? yearSelector.Month - 1
                    : 0;
                var day = calendar.Selector switch
                {
                    MonthlyCalendarSelector monthly => monthly.Day,
                    YearlyCalendarSelector yearDaySelector => yearDaySelector.Day,
                    _ => null
                };
                CalendarDayIndex = (day ?? 1) - 1;
                CalendarLastDay = calendar.Selector is MonthlyCalendarSelector { LastDay: true } or
                    YearlyCalendarSelector { LastDay: true };
            }

            SetSchedule(dueUtc is null ? null : ChinaStandardTime.ToBeijingTime(dueUtc.Value));
            SaveRuleButtonText = copy ? "保存副本" : "保存修改";
            IsCancelRuleEditVisible = true;
            ClearErrors();
        }
        finally
        {
            _updatingForm = false;
            IsFormDirty = false;
        }

        UpdateVisibility();
        RequestFormFocus();
    }

    [RelayCommand]
    private void SaveRule()
    {
        ClearErrors();
        var result = _editor.SaveRule(_rules, BuildEditingRequest());
        if (!result.Succeeded)
        {
            ShowError(result.ErrorTarget, result.Error);
            return;
        }

        _rules = Freeze(result.Rules);
        ReplaceRules(_rules);
        BeginAddRule();
    }

    private ReminderRuleEditingRequest BuildEditingRequest()
    {
        var hasValidTime = !HasInvalidFirstDueTimeInput && FirstDueTime is not null;
        return new ReminderRuleEditingRequest(
            EditingRuleId,
            ModeIndex switch
            {
                0 => ReminderEditingMode.Once,
                1 => ReminderEditingMode.Repeating,
                2 when _countdownTargetUtc is not null => ReminderEditingMode.CountdownRelative,
                var index when index == CalendarModeIndex => ReminderEditingMode.Calendar,
                _ => (ReminderEditingMode)(-1)
            },
            FirstDueDate,
            hasValidTime ? FirstDueTime!.Value.Hours : -1,
            hasValidTime ? FirstDueTime!.Value.Minutes : -1,
            SelectedInterval,
            CustomIntervalMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            _clock.UtcNow,
            _countdownTargetUtc,
            RelativeOffsetMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            SelectedCalendarFrequency,
            CalendarInterval?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Weekdays.Where(static weekday => weekday.IsSelected)
                .Select(static weekday => weekday.Day)
                .ToArray(),
            CalendarDayIndex >= 0
                ? (CalendarDayIndex + 1).ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            CalendarMonthIndex >= 0
                ? (CalendarMonthIndex + 1).ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            CalendarLastDay);
    }

    private bool TryBuildPreviewRule(out ReminderRule? rule) =>
        ReminderRuleEditing.TryBuildPreviewRule(BuildEditingRequest(), out rule);

    private void DeleteRule(string ruleId)
    {
        var result = _editor.DeleteRule(_rules, ruleId);
        if (!result.Succeeded)
        {
            ShowError(result.ErrorTarget, result.Error);
            return;
        }

        _rules = Freeze(result.Rules);
        ReplaceRules(_rules);
        if (string.Equals(EditingRuleId, ruleId, StringComparison.OrdinalIgnoreCase))
        {
            BeginAddRule();
        }
    }

    [RelayCommand]
    private void AcceptDraft()
    {
        if (IsFormDirty)
        {
            SaveRule();
            if (IsFormDirty)
            {
                return;
            }
        }

        _resultRules = Freeze(_rules);
        DraftAccepted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelDraft()
    {
        _resultRules = _initialRules;
        DraftCancelled?.Invoke(this, EventArgs.Empty);
    }

    private void SetSchedule(DateTimeOffset? beijing)
    {
        FirstDueDate = beijing?.Date;
        FirstDueTime = beijing is { } value
            ? new TimeSpan(value.Hour, value.Minute, 0)
            : null;
        HasInvalidFirstDueTimeInput = false;
    }

    private void RequestFormFocus()
    {
        FormFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureWeeklySelection()
    {
        if (_updatingForm || SelectedCalendarFrequency != CalendarFrequency.Weekly ||
            Weekdays.Any(static weekday => weekday.IsSelected))
        {
            return;
        }

        var weekday = FirstDueDate?.DayOfWeek ?? DayOfWeek.Monday;
        var target = Weekdays.First(candidate => candidate.Day == weekday);
        target.IsSelected = true;
    }

    private void UpdateVisibility()
    {
        OnPropertyChanged(nameof(FirstDueLabel));
        OnPropertyChanged(nameof(FirstDueDateAutomationName));
        OnPropertyChanged(nameof(FirstDueTimeAutomationName));
        OnPropertyChanged(nameof(IsFirstDueVisible));
        OnPropertyChanged(nameof(IsIntervalVisible));
        OnPropertyChanged(nameof(IsRelativeVisible));
        OnPropertyChanged(nameof(IsCalendarFrequencyVisible));
        OnPropertyChanged(nameof(IsCalendarIntervalVisible));
        OnPropertyChanged(nameof(IsCalendarSelectorControlsVisible));
        OnPropertyChanged(nameof(IsCalendarSelectorVisible));
        OnPropertyChanged(nameof(IsCalendarWeekdaysVisible));
        OnPropertyChanged(nameof(IsCalendarDateSelectorVisible));
        OnPropertyChanged(nameof(IsCalendarMonthVisible));
        OnPropertyChanged(nameof(IsCalendarDayVisible));
        OnPropertyChanged(nameof(IsCustomIntervalVisible));
        NotifyIntervalErrorStateChanged();
        NotifyCalendarSelectorErrorStateChanged();
        NotifyPreviewChanged();
    }

    private void NotifyFirstDueErrorStateChanged()
    {
        OnPropertyChanged(nameof(IsFirstDueDateErrorVisible));
        OnPropertyChanged(nameof(IsFirstDueTimeErrorVisible));
        OnPropertyChanged(nameof(FirstDueDateHelpText));
        OnPropertyChanged(nameof(FirstDueTimeHelpText));
    }

    private void NotifyIntervalErrorStateChanged()
    {
        OnPropertyChanged(nameof(IsIntervalComboErrorVisible));
        OnPropertyChanged(nameof(IntervalComboHelpText));
        OnPropertyChanged(nameof(CustomIntervalHelpText));
    }

    private void NotifyCalendarSelectorErrorStateChanged()
    {
        OnPropertyChanged(nameof(IsCalendarFrequencyErrorVisible));
        OnPropertyChanged(nameof(IsCalendarMonthErrorVisible));
        OnPropertyChanged(nameof(IsCalendarDayErrorVisible));
        OnPropertyChanged(nameof(CalendarFrequencyHelpText));
        OnPropertyChanged(nameof(CalendarMonthHelpText));
        OnPropertyChanged(nameof(CalendarDayHelpText));
    }

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(RulePreviewText));
        OnPropertyChanged(nameof(IsRulePreviewValid));
    }

    private void OnFormFieldChanged(bool clearPairedIncompleteErrors = false)
    {
        if (!_updatingForm)
        {
            MarkFormDirty(clearPairedIncompleteErrors);
        }
    }

    private void MarkFormDirty() => MarkFormDirty(clearPairedIncompleteErrors: false);

    private void MarkFormDirty(bool clearPairedIncompleteErrors)
    {
        if (!_updatingForm)
        {
            IsFormDirty = true;
            NotifyPreviewChanged();
        }

        var preserveFirstDueError = !_updatingForm && !clearPairedIncompleteErrors && HasIncompleteFirstDueError();
        var preserveCalendarSelectorError = !_updatingForm && !clearPairedIncompleteErrors && HasIncompleteYearlyCalendarSelectorError();
        var immediateIntervalError = !_updatingForm && HasImmediateCustomIntervalError();
        var immediateRelativeOffsetError = !_updatingForm && HasImmediateRelativeOffsetError();
        var immediateCalendarIntervalError = !_updatingForm && HasImmediateCalendarIntervalError();
        ClearErrors(
            preserveFirstDueError: preserveFirstDueError,
            preserveImmediateIntervalError: immediateIntervalError,
            preserveImmediateRelativeOffsetError: immediateRelativeOffsetError,
            preserveImmediateCalendarIntervalError: immediateCalendarIntervalError,
            preserveCalendarSelectorError: preserveCalendarSelectorError);
        if (immediateIntervalError)
        {
            IntervalError = InvalidFixedIntervalError;
        }

        if (immediateRelativeOffsetError)
        {
            RelativeOffsetError = InvalidRelativeOffsetError;
        }

        if (immediateCalendarIntervalError)
        {
            CalendarIntervalError = InvalidCalendarIntervalError;
        }
    }

    private bool HasIncompleteFirstDueError() =>
        IsFirstDueVisible &&
        (string.Equals(FirstDueError, IncompleteFirstDueError, StringComparison.Ordinal) ||
         string.Equals(FirstDueError, IncompleteCalendarScheduleError, StringComparison.Ordinal)) &&
        (FirstDueDate is null || FirstDueTime is null || HasInvalidFirstDueTimeInput);

    private bool HasIncompleteYearlyCalendarSelectorError() =>
        IsCalendarMode &&
        SelectedCalendarFrequency == CalendarFrequency.Yearly &&
        string.Equals(CalendarSelectorError, IncompleteYearlyCalendarSelectorError, StringComparison.Ordinal) &&
        (CalendarMonthIndex < 0 || (!CalendarLastDay && CalendarDayIndex < 0));

    private bool HasImmediateCustomIntervalError() =>
        IsCustomIntervalVisible &&
        (HasInvalidCustomIntervalInput ||
         (CustomIntervalMinutes is int minutes &&
          minutes is < MinimumFixedIntervalMinutes or > MaximumFixedIntervalMinutes));

    private bool HasImmediateRelativeOffsetError() =>
        IsRelativeVisible &&
        (HasInvalidRelativeOffsetInput ||
         (RelativeOffsetMinutes is int minutes &&
          minutes is < MinimumRelativeOffsetMinutes or > MaximumRelativeOffsetMinutes));

    private bool HasImmediateCalendarIntervalError() =>
        IsCalendarIntervalVisible &&
        (HasInvalidCalendarIntervalInput ||
         (CalendarInterval is int interval &&
          interval is < MinimumCalendarInterval or > MaximumCalendarInterval));

    private void ReplaceRules(IEnumerable<ReminderRule> rules)
    {
        foreach (var item in _ruleItems)
        {
            item.EditRequested -= OnEditRequested;
            item.CopyRequested -= OnCopyRequested;
            item.DeleteRequested -= OnDeleteRequested;
        }

        _ruleItems.Clear();
        foreach (var rule in rules)
        {
            var item = new ReminderRuleItemViewModel(rule);
            item.EditRequested += OnEditRequested;
            item.CopyRequested += OnCopyRequested;
            item.DeleteRequested += OnDeleteRequested;
            _ruleItems.Add(item);
        }

        OnPropertyChanged(nameof(RuleCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void OnEditRequested(object? sender, EventArgs args)
    {
        if (sender is ReminderRuleItemViewModel item)
        {
            BeginEditRule(item.Rule, copy: false);
        }
    }

    private void OnCopyRequested(object? sender, EventArgs args)
    {
        if (sender is ReminderRuleItemViewModel item)
        {
            BeginEditRule(item.Rule, copy: true);
        }
    }

    private void OnDeleteRequested(object? sender, EventArgs args)
    {
        if (sender is ReminderRuleItemViewModel item)
        {
            DeleteRule(item.RuleId);
        }
    }

    private void ShowError(ReminderRuleEditingErrorTarget target, string error)
    {
        switch (target)
        {
            case ReminderRuleEditingErrorTarget.Mode:
                ModeError = error;
                break;
            case ReminderRuleEditingErrorTarget.FirstDue:
                FirstDueError = error;
                break;
            case ReminderRuleEditingErrorTarget.Interval:
                IntervalError = error;
                break;
            case ReminderRuleEditingErrorTarget.RelativeOffset:
                RelativeOffsetError = error;
                break;
            case ReminderRuleEditingErrorTarget.CalendarInterval:
                CalendarIntervalError = error;
                break;
            case ReminderRuleEditingErrorTarget.CalendarSelector:
                CalendarSelectorError = error;
                break;
            default:
                CollectionError = error;
                break;
        }

        ValidationErrorRaised?.Invoke(this, new ReminderRuleEditorErrorEventArgs(target, error));
    }

    private void ClearErrors(
        bool preserveFirstDueError = false,
        bool preserveImmediateIntervalError = false,
        bool preserveImmediateRelativeOffsetError = false,
        bool preserveImmediateCalendarIntervalError = false,
        bool preserveCalendarSelectorError = false)
    {
        ModeError = string.Empty;
        if (!preserveFirstDueError)
        {
            FirstDueError = string.Empty;
        }

        if (!preserveImmediateIntervalError)
        {
            IntervalError = string.Empty;
        }

        if (!preserveImmediateRelativeOffsetError)
        {
            RelativeOffsetError = string.Empty;
        }

        if (!preserveImmediateCalendarIntervalError)
        {
            CalendarIntervalError = string.Empty;
        }

        if (!preserveCalendarSelectorError)
        {
            CalendarSelectorError = string.Empty;
        }

        CollectionError = string.Empty;
        OnPropertyChanged(nameof(IsModeErrorVisible));
        NotifyFirstDueErrorStateChanged();
        NotifyIntervalErrorStateChanged();
        OnPropertyChanged(nameof(IsRelativeOffsetErrorVisible));
        OnPropertyChanged(nameof(IsCalendarIntervalErrorVisible));
        NotifyCalendarSelectorErrorStateChanged();
        OnPropertyChanged(nameof(IsCalendarSelectorVisible));
        OnPropertyChanged(nameof(IsCollectionErrorVisible));
    }

    private static IReadOnlyList<ReminderRule> Freeze(IEnumerable<ReminderRule> rules) =>
        Array.AsReadOnly(rules.Select(rule => (ReminderRule)(rule switch
        {
            AbsoluteOnceReminderRule once => once with { },
            FixedIntervalReminderRule repeating => repeating with { },
            CountdownRelativeReminderRule relative => relative with { },
            CalendarReminderRule calendar => calendar with { Selector = calendar.Selector switch
            {
                DailyCalendarSelector => new DailyCalendarSelector(),
                WeeklyCalendarSelector weekly => new WeeklyCalendarSelector(weekly.Weekdays.ToArray()),
                MonthlyCalendarSelector monthly => monthly with { },
                YearlyCalendarSelector yearly => yearly with { },
                _ => throw new ArgumentOutOfRangeException(nameof(rule))
            } },
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        })).ToArray());

    internal static string Describe(ReminderRule rule) => rule switch
    {
        AbsoluteOnceReminderRule once =>
            $"提醒一次 · {DateDisplay.FullMoment(once.ScheduledUtc)}",
        FixedIntervalReminderRule repeating =>
            $"每隔一段时间 · 每 {repeating.Interval.Minutes} 分钟 · {DateDisplay.FullMoment(repeating.FirstDueUtc)} 起",
        CountdownRelativeReminderRule relative => relative.OffsetMinutesBefore == 0
            ? "截止前提醒 · 截止时提醒"
            : $"截止前提醒 · 提前 {relative.OffsetMinutesBefore} 分钟",
        CalendarReminderRule calendar => $"按日历重复 · {DescribeCalendar(calendar)}",
        _ => throw new ArgumentOutOfRangeException(nameof(rule))
    };

    private static string DescribeCalendar(CalendarReminderRule calendar)
    {
        var frequency = calendar.Frequency switch
        {
            CalendarFrequency.Daily => "日",
            CalendarFrequency.Weekly => "周",
            CalendarFrequency.Monthly => "月",
            CalendarFrequency.Yearly => "年",
            _ => throw new ArgumentOutOfRangeException(nameof(calendar))
        };
        var selector = calendar.Selector switch
        {
            DailyCalendarSelector => string.Empty,
            WeeklyCalendarSelector weekly => $" · 周{string.Join("、周", weekly.Weekdays.Select(ChineseWeekday))}",
            MonthlyCalendarSelector { LastDay: true } => " · 最后一天",
            MonthlyCalendarSelector monthly => $" · {monthly.Day} 日",
            YearlyCalendarSelector { LastDay: true } yearly => $" · {yearly.Month} 月最后一天",
            YearlyCalendarSelector yearly => $" · {yearly.Month} 月 {yearly.Day} 日",
            _ => throw new ArgumentOutOfRangeException(nameof(calendar))
        };
        return $"每 {calendar.Interval} {frequency}{selector} · {calendar.LocalTime:HH\\:mm}";
    }

    private static string ChineseWeekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        DayOfWeek.Sunday => "日",
        _ => throw new ArgumentOutOfRangeException(nameof(day))
    };
}

internal sealed partial class ReminderRuleItemViewModel : ObservableObject
{
    public ReminderRuleItemViewModel(ReminderRule rule)
    {
        Rule = rule;
    }

    public ReminderRule Rule { get; }

    public string RuleId => Rule.Id;

    public string Description => ReminderRulesEditorViewModel.Describe(Rule);

    internal event EventHandler? EditRequested;

    internal event EventHandler? CopyRequested;

    internal event EventHandler? DeleteRequested;

    [RelayCommand]
    private void Edit() => EditRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Copy() => CopyRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke(this, EventArgs.Empty);
}

internal sealed partial class ReminderRuleWeekdayViewModel : ObservableObject
{
    private readonly Action _changed;

    public ReminderRuleWeekdayViewModel(DayOfWeek day, string text, Action changed)
    {
        Day = day;
        Text = text;
        _changed = changed;
    }

    public DayOfWeek Day { get; }

    public string Text { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => _changed();
}

internal sealed record ReminderRuleEditorErrorEventArgs(
    ReminderRuleEditingErrorTarget Target,
    string Error);
