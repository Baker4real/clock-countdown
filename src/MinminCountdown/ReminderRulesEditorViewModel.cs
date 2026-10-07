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
internal sealed class ReminderRulesEditorViewModel : ObservableObject
{
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
    private bool _formDirty;
    private IReadOnlyList<ReminderRule> _rules;
    private IReadOnlyList<ReminderRule> _resultRules;
    private string? _editingRuleId;
    private int _modeIndex;
    private DateTime? _firstDueDate;
    private int _firstDueHourIndex = -1;
    private int _firstDueMinuteIndex = -1;
    private int _intervalIndex = 1;
    private string _customIntervalMinutes = string.Empty;
    private string _relativeOffsetMinutes = "0";
    private int _calendarFrequencyIndex;
    private string _calendarInterval = "1";
    private int _calendarMonthIndex;
    private int _calendarDayIndex;
    private bool _calendarLastDay;
    private string _modeError = string.Empty;
    private string _firstDueError = string.Empty;
    private string _intervalError = string.Empty;
    private string _relativeOffsetError = string.Empty;
    private string _calendarIntervalError = string.Empty;
    private string _calendarSelectorError = string.Empty;
    private string _collectionError = string.Empty;
    private string _saveRuleButtonText = "添加规则";
    private bool _isCancelRuleEditVisible;

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
            ? new[] { "绝对一次", "固定间隔", "日历重复" }
            : new[] { "绝对一次", "固定间隔", "倒计时相对", "日历重复" };
        IntervalOptions = new[] { "15 分钟", "30 分钟", "60 分钟", "自定义" };
        CalendarFrequencyOptions = new[] { "每天", "每周", "每月", "每年" };
        Hours = Enumerable.Range(0, 24)
            .Select(static hour => hour.ToString("D2", CultureInfo.InvariantCulture))
            .ToArray();
        Minutes = Enumerable.Range(0, 60)
            .Select(static minute => minute.ToString("D2", CultureInfo.InvariantCulture))
            .ToArray();
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
        SaveRuleCommand = new RelayCommand(SaveDraftRule);
        CancelRuleEditCommand = new RelayCommand(BeginAddRule);
        AcceptDraftCommand = new RelayCommand(AcceptDraft);
        CancelDraftCommand = new RelayCommand(CancelDraft);

        ReplaceRules(_rules);
        BeginAddRule();
    }

    public IReadOnlyList<string> ModeOptions { get; }

    public IReadOnlyList<string> IntervalOptions { get; }

    public IReadOnlyList<string> CalendarFrequencyOptions { get; }

    public IReadOnlyList<string> Hours { get; }

    public IReadOnlyList<string> Minutes { get; }

    public IReadOnlyList<string> Months { get; }

    public IReadOnlyList<string> Days { get; }

    public ReadOnlyObservableCollection<ReminderRuleWeekdayViewModel> Weekdays { get; }

    public ReadOnlyObservableCollection<ReminderRuleItemViewModel> RuleItems => _readOnlyRuleItems;

    public int RuleCount => _rules.Count;

    public string CountText => $"{RuleCount} / 8 条";

    public bool IsEmpty => RuleCount == 0;

    public string EmptyText => "当前不提醒";

    public string? EditingRuleId => _editingRuleId;

    public bool IsFormDirty => _formDirty;

    public IReadOnlyList<ReminderRule> ResultRules => _resultRules;

    public ReminderRuleEditingErrorTarget InitialErrorTarget { get; }

    public string InitialError { get; }

    public bool HasInitialError =>
        InitialErrorTarget != ReminderRuleEditingErrorTarget.None &&
        !string.IsNullOrWhiteSpace(InitialError);

    public DateTimeOffset? CountdownTargetUtc => _countdownTargetUtc;

    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            if (!SetFormProperty(ref _modeIndex, value))
            {
                return;
            }

            EnsureWeeklySelection();
            UpdateVisibility();
        }
    }

    public DateTime? FirstDueDate
    {
        get => _firstDueDate;
        set => SetFormProperty(ref _firstDueDate, value);
    }

    public int FirstDueHourIndex
    {
        get => _firstDueHourIndex;
        set => SetFormProperty(ref _firstDueHourIndex, value);
    }

    public int FirstDueMinuteIndex
    {
        get => _firstDueMinuteIndex;
        set => SetFormProperty(ref _firstDueMinuteIndex, value);
    }

    public int IntervalIndex
    {
        get => _intervalIndex;
        set
        {
            if (!SetFormProperty(ref _intervalIndex, value))
            {
                return;
            }

            UpdateVisibility();
        }
    }

    public string CustomIntervalMinutes
    {
        get => _customIntervalMinutes;
        set => SetFormProperty(ref _customIntervalMinutes, value ?? string.Empty);
    }

    public string RelativeOffsetMinutes
    {
        get => _relativeOffsetMinutes;
        set => SetFormProperty(ref _relativeOffsetMinutes, value ?? string.Empty);
    }

    public int CalendarFrequencyIndex
    {
        get => _calendarFrequencyIndex;
        set
        {
            if (!SetFormProperty(ref _calendarFrequencyIndex, value))
            {
                return;
            }

            EnsureWeeklySelection();
            UpdateVisibility();
        }
    }

    public string CalendarInterval
    {
        get => _calendarInterval;
        set => SetFormProperty(ref _calendarInterval, value ?? string.Empty);
    }

    public int CalendarMonthIndex
    {
        get => _calendarMonthIndex;
        set => SetFormProperty(ref _calendarMonthIndex, value);
    }

    public int CalendarDayIndex
    {
        get => _calendarDayIndex;
        set => SetFormProperty(ref _calendarDayIndex, value);
    }

    public bool CalendarLastDay
    {
        get => _calendarLastDay;
        set
        {
            if (!SetFormProperty(ref _calendarLastDay, value))
            {
                return;
            }

            UpdateVisibility();
        }
    }

    public string FirstDueLabel => IsCalendarMode ? "开始日期" : "首次提醒";

    public string FirstDueDateAutomationName => IsCalendarMode ? "开始日期" : "首次提醒日期";

    public string FirstDueHourAutomationName => IsCalendarMode ? "本地时刻小时" : "首次提醒小时";

    public string FirstDueMinuteAutomationName => IsCalendarMode ? "本地时刻分钟" : "首次提醒分钟";

    public bool IsFirstDueVisible => !IsRelativeMode;

    public bool IsIntervalVisible => IsRepeatingMode;

    public bool IsRelativeVisible => IsRelativeMode;

    public bool IsCalendarFrequencyVisible => IsCalendarMode;

    public bool IsCalendarIntervalVisible => IsCalendarMode;

    public bool IsCalendarSelectorVisible => IsCalendarMode;

    public bool IsCalendarWeekdaysVisible => IsCalendarMode && SelectedCalendarFrequency == CalendarFrequency.Weekly;

    public bool IsCalendarDateSelectorVisible => IsCalendarMode &&
        SelectedCalendarFrequency is CalendarFrequency.Monthly or CalendarFrequency.Yearly;

    public bool IsCalendarMonthVisible => IsCalendarMode && SelectedCalendarFrequency == CalendarFrequency.Yearly;

    public bool IsCalendarDayVisible => !CalendarLastDay;

    public bool IsCustomIntervalVisible => IsRepeatingMode && IntervalIndex == 3;

    public string SaveRuleButtonText
    {
        get => _saveRuleButtonText;
        private set => SetProperty(ref _saveRuleButtonText, value);
    }

    public bool IsCancelRuleEditVisible
    {
        get => _isCancelRuleEditVisible;
        private set => SetProperty(ref _isCancelRuleEditVisible, value);
    }

    public string ModeError
    {
        get => _modeError;
        private set
        {
            if (SetProperty(ref _modeError, value))
            {
                OnPropertyChanged(nameof(IsModeErrorVisible));
            }
        }
    }

    public string FirstDueError
    {
        get => _firstDueError;
        private set
        {
            if (SetProperty(ref _firstDueError, value))
            {
                OnPropertyChanged(nameof(IsFirstDueErrorVisible));
            }
        }
    }

    public string IntervalError
    {
        get => _intervalError;
        private set
        {
            if (SetProperty(ref _intervalError, value))
            {
                OnPropertyChanged(nameof(IsIntervalErrorVisible));
            }
        }
    }

    public string RelativeOffsetError
    {
        get => _relativeOffsetError;
        private set
        {
            if (SetProperty(ref _relativeOffsetError, value))
            {
                OnPropertyChanged(nameof(IsRelativeOffsetErrorVisible));
            }
        }
    }

    public string CalendarIntervalError
    {
        get => _calendarIntervalError;
        private set
        {
            if (SetProperty(ref _calendarIntervalError, value))
            {
                OnPropertyChanged(nameof(IsCalendarIntervalErrorVisible));
            }
        }
    }

    public string CalendarSelectorError
    {
        get => _calendarSelectorError;
        private set
        {
            if (SetProperty(ref _calendarSelectorError, value))
            {
                OnPropertyChanged(nameof(IsCalendarSelectorErrorVisible));
            }
        }
    }

    public string CollectionError
    {
        get => _collectionError;
        private set
        {
            if (SetProperty(ref _collectionError, value))
            {
                OnPropertyChanged(nameof(IsCollectionErrorVisible));
            }
        }
    }

    public bool IsModeErrorVisible => !string.IsNullOrEmpty(ModeError);

    public bool IsFirstDueErrorVisible => !string.IsNullOrEmpty(FirstDueError);

    public bool IsIntervalErrorVisible => !string.IsNullOrEmpty(IntervalError);

    public bool IsRelativeOffsetErrorVisible => !string.IsNullOrEmpty(RelativeOffsetError);

    public bool IsCalendarIntervalErrorVisible => !string.IsNullOrEmpty(CalendarIntervalError);

    public bool IsCalendarSelectorErrorVisible => !string.IsNullOrEmpty(CalendarSelectorError);

    public bool IsCollectionErrorVisible => !string.IsNullOrEmpty(CollectionError);

    public RelayCommand SaveRuleCommand { get; }

    public RelayCommand CancelRuleEditCommand { get; }

    public RelayCommand AcceptDraftCommand { get; }

    public RelayCommand CancelDraftCommand { get; }

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

    private void BeginAddRule()
    {
        _updatingForm = true;
        try
        {
            _editingRuleId = null;
            OnPropertyChanged(nameof(EditingRuleId));
            ModeIndex = 0;
            IntervalIndex = 1;
            CustomIntervalMinutes = string.Empty;
            RelativeOffsetMinutes = "0";
            CalendarFrequencyIndex = 0;
            CalendarInterval = "1";
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
            SetFormDirty(false);
        }

        UpdateVisibility();
    }

    private void BeginEditRule(ReminderRule rule, bool copy)
    {
        _updatingForm = true;
        try
        {
            _editingRuleId = copy ? null : rule.Id;
            OnPropertyChanged(nameof(EditingRuleId));
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
                    ? string.Empty
                    : minutes.ToString(CultureInfo.InvariantCulture);
            }

            if (rule is CountdownRelativeReminderRule relative)
            {
                RelativeOffsetMinutes = relative.OffsetMinutesBefore.ToString(CultureInfo.InvariantCulture);
            }

            if (rule is CalendarReminderRule calendar)
            {
                CalendarFrequencyIndex = (int)calendar.Frequency;
                CalendarInterval = calendar.Interval.ToString(CultureInfo.InvariantCulture);
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
            SetFormDirty(false);
        }

        UpdateVisibility();
        RequestFormFocus();
    }

    private void SaveDraftRule()
    {
        ClearErrors();
        var result = _editor.SaveRule(_rules, new ReminderRuleEditingRequest(
            _editingRuleId,
            ModeIndex switch
            {
                0 => ReminderEditingMode.Once,
                1 => ReminderEditingMode.Repeating,
                2 when _countdownTargetUtc is not null => ReminderEditingMode.CountdownRelative,
                var index when index == CalendarModeIndex => ReminderEditingMode.Calendar,
                _ => (ReminderEditingMode)(-1)
            },
            FirstDueDate,
            FirstDueHourIndex,
            FirstDueMinuteIndex,
            SelectedInterval,
            CustomIntervalMinutes,
            _clock.UtcNow,
            _countdownTargetUtc,
            RelativeOffsetMinutes,
            SelectedCalendarFrequency,
            CalendarInterval,
            Weekdays.Where(static weekday => weekday.IsSelected)
                .Select(static weekday => weekday.Day)
                .ToArray(),
            CalendarDayIndex >= 0
                ? (CalendarDayIndex + 1).ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            CalendarMonthIndex >= 0
                ? (CalendarMonthIndex + 1).ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            CalendarLastDay));
        if (!result.Succeeded)
        {
            ShowError(result.ErrorTarget, result.Error);
            return;
        }

        _rules = Freeze(result.Rules);
        ReplaceRules(_rules);
        BeginAddRule();
    }

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
        if (string.Equals(_editingRuleId, ruleId, StringComparison.OrdinalIgnoreCase))
        {
            BeginAddRule();
        }
    }

    private void AcceptDraft()
    {
        if (_formDirty)
        {
            SaveDraftRule();
            if (_formDirty)
            {
                return;
            }
        }

        _resultRules = Freeze(_rules);
        DraftAccepted?.Invoke(this, EventArgs.Empty);
    }

    private void CancelDraft()
    {
        _resultRules = _initialRules;
        DraftCancelled?.Invoke(this, EventArgs.Empty);
    }

    private void SetSchedule(DateTimeOffset? beijing)
    {
        FirstDueDate = beijing?.Date;
        FirstDueHourIndex = beijing?.Hour ?? -1;
        FirstDueMinuteIndex = beijing?.Minute ?? -1;
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
        OnPropertyChanged(nameof(FirstDueHourAutomationName));
        OnPropertyChanged(nameof(FirstDueMinuteAutomationName));
        OnPropertyChanged(nameof(IsFirstDueVisible));
        OnPropertyChanged(nameof(IsIntervalVisible));
        OnPropertyChanged(nameof(IsRelativeVisible));
        OnPropertyChanged(nameof(IsCalendarFrequencyVisible));
        OnPropertyChanged(nameof(IsCalendarIntervalVisible));
        OnPropertyChanged(nameof(IsCalendarSelectorVisible));
        OnPropertyChanged(nameof(IsCalendarWeekdaysVisible));
        OnPropertyChanged(nameof(IsCalendarDateSelectorVisible));
        OnPropertyChanged(nameof(IsCalendarMonthVisible));
        OnPropertyChanged(nameof(IsCalendarDayVisible));
        OnPropertyChanged(nameof(IsCustomIntervalVisible));
    }

    private bool SetFormProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        if (!_updatingForm)
        {
            MarkFormDirty();
        }

        return true;
    }

    private void MarkFormDirty()
    {
        if (!_updatingForm)
        {
            SetFormDirty(true);
        }

        ClearErrors();
    }

    private void SetFormDirty(bool value)
    {
        if (_formDirty == value)
        {
            return;
        }

        _formDirty = value;
        OnPropertyChanged(nameof(IsFormDirty));
    }

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

    private void ClearErrors()
    {
        ModeError = string.Empty;
        FirstDueError = string.Empty;
        IntervalError = string.Empty;
        RelativeOffsetError = string.Empty;
        CalendarIntervalError = string.Empty;
        CalendarSelectorError = string.Empty;
        CollectionError = string.Empty;
        OnPropertyChanged(nameof(IsModeErrorVisible));
        OnPropertyChanged(nameof(IsFirstDueErrorVisible));
        OnPropertyChanged(nameof(IsIntervalErrorVisible));
        OnPropertyChanged(nameof(IsRelativeOffsetErrorVisible));
        OnPropertyChanged(nameof(IsCalendarIntervalErrorVisible));
        OnPropertyChanged(nameof(IsCalendarSelectorErrorVisible));
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
            $"一次 · {ChinaStandardTime.ToBeijingTime(once.ScheduledUtc):yyyy-MM-dd HH:mm}",
        FixedIntervalReminderRule repeating =>
            $"每 {repeating.Interval.Minutes} 分钟 · {ChinaStandardTime.ToBeijingTime(repeating.FirstDueUtc):yyyy-MM-dd HH:mm} 起",
        CountdownRelativeReminderRule relative => relative.OffsetMinutesBefore == 0
            ? "倒计时到点"
            : $"倒计时提前 {relative.OffsetMinutesBefore} 分钟",
        CalendarReminderRule calendar => DescribeCalendar(calendar),
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

internal sealed class ReminderRuleItemViewModel : ObservableObject
{
    public ReminderRuleItemViewModel(ReminderRule rule)
    {
        Rule = rule;
        EditCommand = new RelayCommand(() => EditRequested?.Invoke(this, EventArgs.Empty));
        CopyCommand = new RelayCommand(() => CopyRequested?.Invoke(this, EventArgs.Empty));
        DeleteCommand = new RelayCommand(() => DeleteRequested?.Invoke(this, EventArgs.Empty));
    }

    public ReminderRule Rule { get; }

    public string RuleId => Rule.Id;

    public string Description => ReminderRulesEditorViewModel.Describe(Rule);

    public RelayCommand EditCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand DeleteCommand { get; }

    internal event EventHandler? EditRequested;

    internal event EventHandler? CopyRequested;

    internal event EventHandler? DeleteRequested;
}

internal sealed class ReminderRuleWeekdayViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected;

    public ReminderRuleWeekdayViewModel(DayOfWeek day, string text, Action changed)
    {
        Day = day;
        Text = text;
        _changed = changed;
    }

    public DayOfWeek Day { get; }

    public string Text { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _changed();
            }
        }
    }
}

internal sealed record ReminderRuleEditorErrorEventArgs(
    ReminderRuleEditingErrorTarget Target,
    string Error);
