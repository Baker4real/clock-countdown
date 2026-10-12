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
    private readonly bool _canComplete;
    private bool _isAcknowledgeEnabled;
    private bool _isSnoozeEnabled;
    private bool _isCompleteEnabled;
    private bool _isCustomSnoozeVisible;
    private bool _isFailureVisible;
    private bool _isCustomErrorVisible;
    private bool _isRendered;
    private string _errorMessage = string.Empty;
    private string _customErrorMessage = string.Empty;
    private ReminderSnoozeOption _selectedSnoozeOption = ReminderSnoozeOption.TenMinutes;
    private DateTime? _customDate;
    private string _relativeTimeText = "刚刚";
    private int _customHourIndex = -1;
    private int _customMinuteIndex = -1;

    public ReminderCenterEntryViewModel(ReminderCenterItem item)
    {
        Item = item;
        _canComplete = item.CanComplete;
        AcknowledgeCommand = new RelayCommand(
            () => AcknowledgeRequested?.Invoke(this, new ReminderCenterEntryEventArgs(Item.Key)),
            () => IsAcknowledgeEnabled);
        SnoozeCommand = new RelayCommand(RequestSnooze, () => IsSnoozeEnabled);
        CompleteCommand = new RelayCommand(
            () => CompleteRequested?.Invoke(this, new ReminderCenterEntryEventArgs(Item.Key)),
            () => IsCompleteEnabled);
        OpenSourceCommand = new RelayCommand(
            () => OpenSourceRequested?.Invoke(this, new ReminderCenterEntryEventArgs(Item.Key)));
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
                SnoozeCommand.NotifyCanExecuteChanged();
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
        private set => SetProperty(ref _isCustomErrorVisible, value);
    }

    public bool IsRendered
    {
        get => _isRendered;
        private set => SetProperty(ref _isRendered, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string CustomErrorMessage
    {
        get => _customErrorMessage;
        private set => SetProperty(ref _customErrorMessage, value);
    }

    public ReminderSnoozeOption SelectedSnoozeOption
    {
        get => _selectedSnoozeOption;
        set
        {
            if (!SetProperty(ref _selectedSnoozeOption, value))
            {
                return;
            }

            IsCustomSnoozeVisible = value == ReminderSnoozeOption.Custom;
            if (!IsCustomSnoozeVisible)
            {
                ClearCustomError();
            }
        }
    }

    public DateTime? CustomDate
    {
        get => _customDate;
        set => SetProperty(ref _customDate, value);
    }

    public int CustomHourIndex
    {
        get => _customHourIndex;
        set => SetProperty(ref _customHourIndex, value);
    }

    public int CustomMinuteIndex
    {
        get => _customMinuteIndex;
        set => SetProperty(ref _customMinuteIndex, value);
    }

    public RelayCommand AcknowledgeCommand { get; }

    public RelayCommand SnoozeCommand { get; }

    public RelayCommand CompleteCommand { get; }

    public RelayCommand OpenSourceCommand { get; }

    internal event EventHandler<ReminderCenterEntryEventArgs>? AcknowledgeRequested;

    internal event EventHandler<ReminderCenterSnoozeEventArgs>? SnoozeRequested;

    internal event EventHandler<ReminderCenterEntryEventArgs>? CompleteRequested;

    internal event EventHandler<ReminderCenterEntryEventArgs>? OpenSourceRequested;

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

    internal void ShowCustomSnoozeError(string message)
    {
        ClearFailure();
        CustomErrorMessage = message;
        IsCustomErrorVisible = true;
        IsAcknowledgeEnabled = true;
        IsSnoozeEnabled = true;
        IsCompleteEnabled = CanComplete;
    }

    internal void MarkRendered() => IsRendered = true;

    private void RequestSnooze()
    {
        if (!IsSnoozeEnabled)
        {
            return;
        }

        DateTimeOffset? customBeijing = null;
        if (SelectedSnoozeOption == ReminderSnoozeOption.Custom)
        {
            if (CustomDate is not { } date || CustomHourIndex < 0 || CustomMinuteIndex < 0)
            {
                ShowCustomSnoozeError("请选择完整的自定义稍后日期和时间。");
                return;
            }

            customBeijing = ChinaStandardTime.ToBeijingTime(
                CountdownFormatting.ToUtc(date, CustomHourIndex, CustomMinuteIndex, 0));
        }

        ClearCustomError();
        SnoozeRequested?.Invoke(
            this,
            new ReminderCenterSnoozeEventArgs(Item.Key, SelectedSnoozeOption, customBeijing));
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
