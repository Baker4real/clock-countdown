using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinminCountdown.Core;

namespace MinminCountdown;

/// <summary>
/// The state and commands exposed by the reminder center.  The window owns the
/// visual tree and render probe, while this object owns every mutable value
/// shown by that tree.
/// </summary>
internal sealed class ReminderCenterViewModel : ObservableObject
{
    private readonly ObservableCollection<ReminderCenterEntryViewModel> _entries = new();
    private bool _canAcknowledgeAll;

    public ReminderCenterViewModel()
    {
        Entries = new ReadOnlyObservableCollection<ReminderCenterEntryViewModel>(_entries);
        AcknowledgeAllCommand = new RelayCommand(
            () => AcknowledgeAllRequested?.Invoke(this, EventArgs.Empty),
            () => CanAcknowledgeAll);
        _entries.CollectionChanged += (_, _) => RefreshSummary();
    }

    public ReadOnlyObservableCollection<ReminderCenterEntryViewModel> Entries { get; }

    public int EntryCount => _entries.Count;

    public string CountText => $"{EntryCount} 条";

    public string AccessibleCountText => $"共 {EntryCount} 条提醒";

    public bool CanAcknowledgeAll
    {
        get => _canAcknowledgeAll;
        private set
        {
            if (SetProperty(ref _canAcknowledgeAll, value))
            {
                AcknowledgeAllCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RelayCommand AcknowledgeAllCommand { get; }

    internal event EventHandler<EventArgs>? AcknowledgeAllRequested;

    internal ReminderCenterEntryViewModel Add(ReminderCenterItem item) =>
        Add(new ReminderCenterEntryViewModel(item));

    internal ReminderCenterEntryViewModel Add(ReminderCenterEntryViewModel entry)
    {
        var existing = _entries.FirstOrDefault(candidate => candidate.Item.Key == entry.Item.Key);
        if (existing is not null)
        {
            return existing;
        }

        entry.PropertyChanged += OnEntryPropertyChanged;
        _entries.Add(entry);
        RefreshSummary();
        return entry;
    }

    internal bool Remove(ActiveReminderOccurrenceKey key)
    {
        var entry = _entries.FirstOrDefault(candidate => candidate.Item.Key == key);
        if (entry is null)
        {
            return false;
        }

        entry.PropertyChanged -= OnEntryPropertyChanged;
        _entries.Remove(entry);
        RefreshSummary();
        return true;
    }

    internal ReminderCenterEntryViewModel? Find(ActiveReminderOccurrenceKey key) =>
        _entries.FirstOrDefault(entry => entry.Item.Key == key);

    internal void UpdateTime(DateTimeOffset nowUtc)
    {
        foreach (var entry in _entries)
        {
            entry.UpdateTime(nowUtc);
        }
    }

    private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ReminderCenterEntryViewModel.IsAcknowledgeEnabled) or
            nameof(ReminderCenterEntryViewModel.IsFailureVisible))
        {
            RefreshSummary();
        }
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(AccessibleCountText));
        CanAcknowledgeAll = _entries.Any(entry => entry.IsAcknowledgeEnabled);
    }
}

internal sealed class ReminderCenterEntryViewModel : ObservableObject
{
    private const string IncompleteCustomSnoozeError = "请选择完整的自定义稍后日期和时间。";

    private readonly bool _canComplete;
    private bool _isAcknowledgeEnabled;
    private bool _isSnoozeEnabled;
    private bool _isCompleteEnabled;
    private bool _isCustomSnoozeVisible;
    private bool _isFailureVisible;
    private bool _isCustomErrorVisible;
    private bool _isRendered;
    private bool _isTitleOverflowing;
    private bool _isTitleExpanded;
    private string _errorMessage = string.Empty;
    private string _customErrorMessage = string.Empty;
    private DateTime? _customDate;
    private string _relativeTimeText = "刚刚";
    private TimeSpan? _customTime;
    private bool _hasInvalidCustomTimeInput;
    private bool _customTimeTouched;

    public ReminderCenterEntryViewModel(ReminderCenterItem item)
    {
        Item = item;
        _canComplete = item.CanComplete;
        AcknowledgeCommand = new RelayCommand(
            () => AcknowledgeRequested?.Invoke(this, new ReminderCenterEntryEventArgs(Item.Key)),
            () => IsAcknowledgeEnabled);
        SelectSnoozePresetCommand = new RelayCommand<ReminderSnoozeOption>(
            SelectSnoozePreset,
            option => IsSnoozeEnabled && option != ReminderSnoozeOption.Custom);
        OpenCustomSnoozeCommand = new RelayCommand(BeginCustomSnooze, () => IsSnoozeEnabled);
        ConfirmCustomSnoozeCommand = new RelayCommand(ConfirmCustomSnooze, () => IsSnoozeEnabled);
        CompleteCommand = new RelayCommand(
            () => CompleteRequested?.Invoke(this, new ReminderCenterEntryEventArgs(Item.Key)),
            () => IsCompleteEnabled);
        OpenSourceCommand = new RelayCommand(
            () => OpenSourceRequested?.Invoke(this, new ReminderCenterEntryEventArgs(Item.Key)));
        ToggleTitleExpandedCommand = new RelayCommand(ToggleTitleExpanded);
    }

    public ReminderCenterItem Item { get; }

    public string RelativeTimeText
    {
        get => _relativeTimeText;
        private set => SetProperty(ref _relativeTimeText, value);
    }

    internal void UpdateTime(DateTimeOffset nowUtc)
    {
        var elapsed = nowUtc.ToUniversalTime() - Item.Key.ScheduledUtc.ToUniversalTime();
        RelativeTimeText = elapsed.TotalSeconds < 60
            ? "刚刚"
            : elapsed.TotalHours < 1
                ? $"已过 {Math.Floor(elapsed.TotalMinutes):0} 分钟"
                : $"已过 {Math.Floor(elapsed.TotalHours):0} 小时";
    }

    public bool CanComplete => _canComplete;

    public bool IsAcknowledgeEnabled
    {
        get => _isAcknowledgeEnabled;
        private set
        {
            if (SetProperty(ref _isAcknowledgeEnabled, value))
            {
                AcknowledgeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsSnoozeEnabled
    {
        get => _isSnoozeEnabled;
        private set
        {
            if (SetProperty(ref _isSnoozeEnabled, value))
            {
                SelectSnoozePresetCommand.NotifyCanExecuteChanged();
                OpenCustomSnoozeCommand.NotifyCanExecuteChanged();
                ConfirmCustomSnoozeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsCompleteEnabled
    {
        get => _isCompleteEnabled;
        private set
        {
            if (SetProperty(ref _isCompleteEnabled, value))
            {
                CompleteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsCustomSnoozeVisible
    {
        get => _isCustomSnoozeVisible;
        private set => SetProperty(ref _isCustomSnoozeVisible, value);
    }

    public bool IsFailureVisible
    {
        get => _isFailureVisible;
        private set => SetProperty(ref _isFailureVisible, value);
    }

    public bool IsCustomErrorVisible
    {
        get => _isCustomErrorVisible;
        private set
        {
            if (SetProperty(ref _isCustomErrorVisible, value))
            {
                NotifyCustomErrorBordersChanged();
            }
        }
    }

    public bool IsCustomDateErrorVisible =>
        IsCustomErrorVisible &&
        (CustomDate is null || (CustomTime is not null && !HasInvalidCustomTimeInput));

    public bool IsCustomTimeErrorVisible =>
        IsCustomErrorVisible &&
        (CustomTime is null || HasInvalidCustomTimeInput || CustomDate is not null);

    public string CustomDateHelpText => IsCustomDateErrorVisible ? CustomErrorMessage : string.Empty;

    public string CustomTimeHelpText => IsCustomTimeErrorVisible ? CustomErrorMessage : string.Empty;

    public bool IsRendered
    {
        get => _isRendered;
        private set => SetProperty(ref _isRendered, value);
    }

    public bool IsTitleOverflowing
    {
        get => _isTitleOverflowing;
        private set
        {
            if (!SetProperty(ref _isTitleOverflowing, value))
            {
                return;
            }

            if (!value && _isTitleExpanded)
            {
                _isTitleExpanded = false;
                OnPropertyChanged(nameof(IsTitleExpanded));
                NotifyTitleDisclosureChanged();
            }
        }
    }

    public bool IsTitleExpanded
    {
        get => _isTitleExpanded;
        private set
        {
            if (SetProperty(ref _isTitleExpanded, value))
            {
                NotifyTitleDisclosureChanged();
            }
        }
    }

    public string TitleDisclosureText => IsTitleExpanded ? "收起" : "展开全文";

    public string TitleDisclosureStateText => IsTitleExpanded ? "当前展开" : "当前收起";

    public string TitleDisclosureAccessibleName => IsTitleExpanded
        ? $"收起标题（当前展开）：{Item.Source.DisplayText}"
        : $"展开标题（当前收起）：{Item.Source.DisplayText}";

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string CustomErrorMessage
    {
        get => _customErrorMessage;
        private set
        {
            if (SetProperty(ref _customErrorMessage, value))
            {
                OnPropertyChanged(nameof(CustomDateHelpText));
                OnPropertyChanged(nameof(CustomTimeHelpText));
            }
        }
    }

    public DateTime? CustomDate
    {
        get => _customDate;
        set
        {
            if (!SetProperty(ref _customDate, value))
            {
                return;
            }

            if (value is not null && IsCustomErrorVisible)
            {
                ClearCustomError();
            }
            else
            {
                NotifyCustomErrorBordersChanged();
            }
        }
    }

    public TimeSpan? CustomTime
    {
        get => _customTime;
        set
        {
            if (!SetProperty(ref _customTime, value))
            {
                return;
            }

            _customTimeTouched = true;
            if (value is not null && !_hasInvalidCustomTimeInput && IsCustomErrorVisible)
            {
                ClearCustomError();
            }
            else
            {
                NotifyCustomErrorBordersChanged();
            }
        }
    }

    public bool HasInvalidCustomTimeInput
    {
        get => _hasInvalidCustomTimeInput;
        set
        {
            if (!SetProperty(ref _hasInvalidCustomTimeInput, value))
            {
                return;
            }

            if (value)
            {
                _customTimeTouched = true;
            }
            else if (_customTime is not null && IsCustomErrorVisible)
            {
                ClearCustomError();
                return;
            }

            NotifyCustomErrorBordersChanged();
        }
    }

    public RelayCommand AcknowledgeCommand { get; }

    public RelayCommand<ReminderSnoozeOption> SelectSnoozePresetCommand { get; }

    public RelayCommand OpenCustomSnoozeCommand { get; }

    public RelayCommand ConfirmCustomSnoozeCommand { get; }

    public RelayCommand CompleteCommand { get; }

    public RelayCommand OpenSourceCommand { get; }

    public RelayCommand ToggleTitleExpandedCommand { get; }

    internal event EventHandler<ReminderCenterEntryEventArgs>? AcknowledgeRequested;

    internal event EventHandler<ReminderCenterSnoozeEventArgs>? SnoozeRequested;

    internal event EventHandler<ReminderCenterEntryEventArgs>? CompleteRequested;

    internal event EventHandler<ReminderCenterEntryEventArgs>? OpenSourceRequested;

    internal event EventHandler? CustomSnoozeValidationErrorFocusRequested;

    internal void SetDeliveryReady()
    {
        ClearFailure();
        IsAcknowledgeEnabled = true;
        IsSnoozeEnabled = true;
        IsCompleteEnabled = CanComplete;
    }

    internal void ShowFailure(string message)
    {
        ErrorMessage = message;
        IsFailureVisible = true;
        IsAcknowledgeEnabled = false;
        IsSnoozeEnabled = false;
        IsCompleteEnabled = false;
    }

    internal void ShowCustomSnoozeError(string message, bool focusFirstInvalid = false)
    {
        ClearFailure();
        // 先确保自定义输入区域已按需创建并可见，再写错误文案以便错误贴在输入旁并正确播报。
        IsCustomSnoozeVisible = true;
        CustomErrorMessage = message;
        IsCustomErrorVisible = true;
        NotifyCustomErrorBordersChanged();
        IsAcknowledgeEnabled = true;
        IsSnoozeEnabled = true;
        IsCompleteEnabled = CanComplete;
        if (focusFirstInvalid)
        {
            CustomSnoozeValidationErrorFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void MarkRendered() => IsRendered = true;

    internal void SetTitleOverflowing(bool overflowing) => IsTitleOverflowing = overflowing;

    internal void BeginCustomSnooze()
    {
        if (!IsSnoozeEnabled)
        {
            return;
        }

        IsCustomSnoozeVisible = true;
    }

    internal void SelectSnoozePreset(ReminderSnoozeOption option)
    {
        if (!IsSnoozeEnabled || option == ReminderSnoozeOption.Custom)
        {
            return;
        }

        IsCustomSnoozeVisible = false;
        ClearCustomError();
        SnoozeRequested?.Invoke(
            this,
            new ReminderCenterSnoozeEventArgs(Item.Key, option, null));
    }

    private void ToggleTitleExpanded()
    {
        if (!IsTitleOverflowing)
        {
            return;
        }

        IsTitleExpanded = !IsTitleExpanded;
    }

    private void NotifyTitleDisclosureChanged()
    {
        OnPropertyChanged(nameof(TitleDisclosureText));
        OnPropertyChanged(nameof(TitleDisclosureStateText));
        OnPropertyChanged(nameof(TitleDisclosureAccessibleName));
    }

    internal void ValidateCustomTimeOnBlur()
    {
        if (!IsCustomSnoozeVisible || !IsSnoozeEnabled)
        {
            return;
        }

        if (HasInvalidCustomTimeInput || (_customTimeTouched && CustomTime is null))
        {
            ShowCustomSnoozeError(IncompleteCustomSnoozeError);
        }
        else if (CustomDate is not null &&
                 string.Equals(CustomErrorMessage, IncompleteCustomSnoozeError, StringComparison.Ordinal))
        {
            ClearCustomError();
        }
    }

    private void ConfirmCustomSnooze()
    {
        if (!IsSnoozeEnabled)
        {
            return;
        }

        IsCustomSnoozeVisible = true;
        if (CustomDate is not { } date || HasInvalidCustomTimeInput || CustomTime is not { } time)
        {
            ShowCustomSnoozeError(IncompleteCustomSnoozeError, focusFirstInvalid: true);
            return;
        }

        var customBeijing = ChinaStandardTime.ToBeijingTime(
            CountdownFormatting.ToUtc(date, time.Hours, time.Minutes, 0));
        ClearCustomError();
        SnoozeRequested?.Invoke(
            this,
            new ReminderCenterSnoozeEventArgs(Item.Key, ReminderSnoozeOption.Custom, customBeijing));
    }

    private void NotifyCustomErrorBordersChanged()
    {
        OnPropertyChanged(nameof(IsCustomDateErrorVisible));
        OnPropertyChanged(nameof(IsCustomTimeErrorVisible));
        OnPropertyChanged(nameof(CustomDateHelpText));
        OnPropertyChanged(nameof(CustomTimeHelpText));
    }

    private void ClearFailure()
    {
        ErrorMessage = string.Empty;
        IsFailureVisible = false;
    }

    private void ClearCustomError()
    {
        CustomErrorMessage = string.Empty;
        IsCustomErrorVisible = false;
    }
}
