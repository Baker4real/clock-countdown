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
internal sealed partial class ReminderCenterViewModel : ObservableObject
{
    private readonly ObservableCollection<ReminderCenterEntryViewModel> _entries = new();

    public ReminderCenterViewModel()
    {
        Entries = new ReadOnlyObservableCollection<ReminderCenterEntryViewModel>(_entries);
        _entries.CollectionChanged += (_, _) => RefreshSummary();
    }

    public ReadOnlyObservableCollection<ReminderCenterEntryViewModel> Entries { get; }

    public int EntryCount => _entries.Count;

    public string CountText => $"{EntryCount} 条";

    public string AccessibleCountText => $"共 {EntryCount} 条提醒";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcknowledgeAllCommand))]
    public partial bool CanAcknowledgeAll { get; private set; }

    internal event EventHandler<EventArgs>? AcknowledgeAllRequested;

    [RelayCommand(CanExecute = nameof(CanAcknowledgeAll))]
    private void AcknowledgeAll() => AcknowledgeAllRequested?.Invoke(this, EventArgs.Empty);

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

internal sealed partial class ReminderCenterEntryViewModel : ObservableObject
{
    private const string IncompleteCustomSnoozeError = "请选择完整的自定义稍后日期和时间。";

    private readonly bool _canComplete;
    private bool _customTimeTouched;

    public ReminderCenterEntryViewModel(ReminderCenterItem item)
    {
        Item = item;
        _canComplete = item.CanComplete;
        RelativeTimeText = "刚刚";
        ErrorMessage = string.Empty;
        CustomErrorMessage = string.Empty;
    }

    public ReminderCenterItem Item { get; }

    [ObservableProperty]
    public partial string RelativeTimeText { get; private set; }

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcknowledgeCommand))]
    public partial bool IsAcknowledgeEnabled { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectSnoozePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenCustomSnoozeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCustomSnoozeCommand))]
    public partial bool IsSnoozeEnabled { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteCommand))]
    public partial bool IsCompleteEnabled { get; private set; }

    [ObservableProperty]
    public partial bool IsCustomSnoozeVisible { get; private set; }

    [ObservableProperty]
    public partial bool IsFailureVisible { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomDateErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCustomTimeErrorVisible))]
    [NotifyPropertyChangedFor(nameof(CustomDateHelpText))]
    [NotifyPropertyChangedFor(nameof(CustomTimeHelpText))]
    public partial bool IsCustomErrorVisible { get; private set; }

    public bool IsCustomDateErrorVisible =>
        IsCustomErrorVisible &&
        (CustomDate is null || (CustomTime is not null && !HasInvalidCustomTimeInput));

    public bool IsCustomTimeErrorVisible =>
        IsCustomErrorVisible &&
        (CustomTime is null || HasInvalidCustomTimeInput || CustomDate is not null);

    public string CustomDateHelpText => IsCustomDateErrorVisible ? CustomErrorMessage : string.Empty;

    public string CustomTimeHelpText => IsCustomTimeErrorVisible ? CustomErrorMessage : string.Empty;

    [ObservableProperty]
    public partial bool IsRendered { get; private set; }

    [ObservableProperty]
    public partial bool IsTitleOverflowing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleDisclosureText))]
    [NotifyPropertyChangedFor(nameof(TitleDisclosureStateText))]
    [NotifyPropertyChangedFor(nameof(TitleDisclosureAccessibleName))]
    public partial bool IsTitleExpanded { get; private set; }

    public string TitleDisclosureText => IsTitleExpanded ? "收起" : "展开全文";

    public string TitleDisclosureStateText => IsTitleExpanded ? "当前展开" : "当前收起";

    public string TitleDisclosureAccessibleName => IsTitleExpanded
        ? $"收起标题（当前展开）：{Item.Source.DisplayText}"
        : $"展开标题（当前收起）：{Item.Source.DisplayText}";

    [ObservableProperty]
    public partial string ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomDateHelpText))]
    [NotifyPropertyChangedFor(nameof(CustomTimeHelpText))]
    public partial string CustomErrorMessage { get; private set; }

    [ObservableProperty]
    public partial DateTime? CustomDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan? CustomTime { get; set; }

    [ObservableProperty]
    public partial bool HasInvalidCustomTimeInput { get; set; }

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

    private bool CanSelectSnoozePreset(ReminderSnoozeOption option) =>
        IsSnoozeEnabled && option != ReminderSnoozeOption.Custom;

    [RelayCommand(CanExecute = nameof(CanSelectSnoozePreset))]
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

    [RelayCommand(CanExecute = nameof(IsAcknowledgeEnabled))]
    private void Acknowledge() =>
        AcknowledgeRequested?.Invoke(this, new ReminderCenterEntryEventArgs(Item.Key));

    [RelayCommand(CanExecute = nameof(IsSnoozeEnabled))]
    private void OpenCustomSnooze() => BeginCustomSnooze();

    [RelayCommand(CanExecute = nameof(IsCompleteEnabled))]
    private void Complete() =>
        CompleteRequested?.Invoke(this, new ReminderCenterEntryEventArgs(Item.Key));

    [RelayCommand]
    private void OpenSource() =>
        OpenSourceRequested?.Invoke(this, new ReminderCenterEntryEventArgs(Item.Key));

    [RelayCommand]
    private void ToggleTitleExpanded()
    {
        if (!IsTitleOverflowing)
        {
            return;
        }

        IsTitleExpanded = !IsTitleExpanded;
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

    [RelayCommand(CanExecute = nameof(IsSnoozeEnabled))]
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

    partial void OnIsTitleOverflowingChanged(bool value)
    {
        if (!value && IsTitleExpanded)
        {
            IsTitleExpanded = false;
        }
    }

    partial void OnCustomDateChanged(DateTime? value)
    {
        if (value is not null && IsCustomErrorVisible)
        {
            ClearCustomError();
        }
        else
        {
            NotifyCustomErrorBordersChanged();
        }
    }

    partial void OnCustomTimeChanged(TimeSpan? value)
    {
        _customTimeTouched = true;
        if (value is not null && !HasInvalidCustomTimeInput && IsCustomErrorVisible)
        {
            ClearCustomError();
        }
        else
        {
            NotifyCustomErrorBordersChanged();
        }
    }

    partial void OnHasInvalidCustomTimeInputChanged(bool value)
    {
        if (value)
        {
            _customTimeTouched = true;
        }
        else if (CustomTime is not null && IsCustomErrorVisible)
        {
            ClearCustomError();
            return;
        }

        NotifyCustomErrorBordersChanged();
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
