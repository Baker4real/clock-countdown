using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class ReminderRulesEditorWindow : Window
{
    private readonly ReminderRuleEditing _editor;
    private readonly ReminderEditing _scheduleEditor = new();
    private readonly IClock _clock;
    private readonly IReadOnlyList<ReminderRule> _initialRules;
    private readonly ReminderRuleEditingErrorTarget _initialErrorTarget;
    private readonly string _initialError;
    private readonly DateTimeOffset? _countdownTargetUtc;
    private IReadOnlyList<ReminderRule> _rules;
    private string? _editingRuleId;
    private bool _updatingForm;
    private bool _formDirty;

    private readonly StackPanel _rulesHost = new();
    private readonly TextBlock _countText = new();
    private readonly ComboBox _mode = new();
    private readonly DatePicker _firstDueDate = new();
    private readonly ComboBox _firstDueHour = new();
    private readonly ComboBox _firstDueMinute = new();
    private readonly ComboBox _interval = new();
    private readonly TextBox _customInterval = new() { MaxLength = 4 };
    private readonly TextBox _relativeOffset = new() { MaxLength = 7 };
    private readonly ComboBox _calendarFrequency = new();
    private readonly TextBox _calendarInterval = new() { MaxLength = 3, Text = "1" };
    private readonly ComboBox _calendarMonth = new();
    private readonly ComboBox _calendarDay = new();
    private readonly CheckBox _calendarLastDay = new() { Content = "最后一天" };
    private readonly WrapPanel _calendarWeekdays = new();
    private readonly StackPanel _calendarSelector = new();
    private readonly Dictionary<DayOfWeek, CheckBox> _calendarWeekdayChecks = new();
    private readonly TextBlock _modeError = ErrorText();
    private readonly TextBlock _firstDueError = ErrorText();
    private readonly TextBlock _intervalError = ErrorText();
    private readonly TextBlock _relativeOffsetError = ErrorText();
    private readonly TextBlock _calendarIntervalError = ErrorText();
    private readonly TextBlock _calendarSelectorError = ErrorText();
    private readonly TextBlock _collectionError = ErrorText();
    private readonly List<UIElement> _firstDueElements = new();
    private readonly List<UIElement> _intervalElements = new();
    private readonly List<UIElement> _relativeElements = new();
    private readonly List<UIElement> _calendarFrequencyElements = new();
    private readonly List<UIElement> _calendarIntervalElements = new();
    private readonly List<UIElement> _calendarSelectorElements = new();
    private readonly Button _saveRuleButton = new() { Content = "添加规则", MinWidth = 104 };
    private readonly Button _cancelRuleEditButton = new()
    {
        Content = "取消编辑",
        MinWidth = 92,
        Margin = new Thickness(8, 0, 0, 0),
        Visibility = Visibility.Collapsed
    };

    internal ReminderRulesEditorWindow(
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
        _clock = clock;
        _initialErrorTarget = initialErrorTarget;
        _initialError = initialError;
        _countdownTargetUtc = countdownTargetUtc?.ToUniversalTime();
        _editor = new ReminderRuleEditing(guidSource);
        ResultRules = _initialRules;

        Title = "提醒规则";
        Width = 640;
        Height = 790;
        MinWidth = 560;
        MinHeight = 560;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        AutomationProperties.SetName(this, "提醒规则编辑器");

        ConfigureControls();
        Content = BuildContent();
        WireEvents();
        BeginAddRule();
        RenderRules();
    }

    internal IReadOnlyList<ReminderRule> ResultRules { get; private set; }

    private void ConfigureControls()
    {
        _mode.Items.Add("绝对一次");
        _mode.Items.Add("固定间隔");
        if (_countdownTargetUtc is not null)
        {
            _mode.Items.Add("倒计时相对");
        }
        _mode.Items.Add("日历重复");
        for (var hour = 0; hour < 24; hour++)
        {
            _firstDueHour.Items.Add(hour.ToString("D2", CultureInfo.InvariantCulture));
        }

        for (var minute = 0; minute < 60; minute++)
        {
            _firstDueMinute.Items.Add(minute.ToString("D2", CultureInfo.InvariantCulture));
        }

        _interval.Items.Add("15 分钟");
        _interval.Items.Add("30 分钟");
        _interval.Items.Add("60 分钟");
        _interval.Items.Add("自定义");
        _interval.SelectedIndex = 1;
        foreach (var frequency in new[] { "每天", "每周", "每月", "每年" })
        {
            _calendarFrequency.Items.Add(frequency);
        }
        _calendarFrequency.SelectedIndex = 0;
        for (var month = 1; month <= 12; month++)
        {
            _calendarMonth.Items.Add(month.ToString(CultureInfo.InvariantCulture));
        }
        _calendarMonth.SelectedIndex = 0;
        for (var day = 1; day <= 31; day++)
        {
            _calendarDay.Items.Add(day.ToString(CultureInfo.InvariantCulture));
        }
        _calendarDay.SelectedIndex = 0;
        foreach (var (weekday, text) in new[]
                 {
                     (DayOfWeek.Monday, "一"),
                     (DayOfWeek.Tuesday, "二"),
                     (DayOfWeek.Wednesday, "三"),
                     (DayOfWeek.Thursday, "四"),
                     (DayOfWeek.Friday, "五"),
                     (DayOfWeek.Saturday, "六"),
                     (DayOfWeek.Sunday, "日")
                 })
        {
            var checkBox = new CheckBox
            {
                Content = text,
                Margin = new Thickness(0, 0, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            NameControl(checkBox, $"每周星期{text}");
            AutomationProperties.SetAutomationId(checkBox, $"ReminderRuleCalendarWeekday{weekday}");
            _calendarWeekdayChecks.Add(weekday, checkBox);
            _calendarWeekdays.Children.Add(checkBox);
        }
        BuildCalendarSelector();
        ThemedDatePickerBehavior.Attach(_firstDueDate);

        NameControl(_mode, "提醒规则类型");
        NameControl(_firstDueDate, "首次提醒日期");
        NameControl(_firstDueHour, "首次提醒小时");
        NameControl(_firstDueMinute, "首次提醒分钟");
        NameControl(_interval, "固定间隔分钟");
        NameControl(_customInterval, "自定义固定间隔分钟");
        NameControl(_relativeOffset, "倒计时提前分钟");
        NameControl(_calendarFrequency, "日历重复频率");
        NameControl(_calendarInterval, "日历重复间隔");
        NameControl(_calendarMonth, "日历重复月份");
        NameControl(_calendarDay, "日历重复日期");
        NameControl(_calendarLastDay, "显式选择最后一天");
        NameControl(_saveRuleButton, "保存提醒规则");
        NameControl(_cancelRuleEditButton, "取消编辑提醒规则");
        AutomationProperties.SetAutomationId(_mode, "ReminderRuleMode");
        AutomationProperties.SetAutomationId(_firstDueDate, "ReminderRuleFirstDueDate");
        AutomationProperties.SetAutomationId(_firstDueHour, "ReminderRuleFirstDueHour");
        AutomationProperties.SetAutomationId(_firstDueMinute, "ReminderRuleFirstDueMinute");
        AutomationProperties.SetAutomationId(_interval, "ReminderRuleInterval");
        AutomationProperties.SetAutomationId(_customInterval, "ReminderRuleCustomInterval");
        AutomationProperties.SetAutomationId(_relativeOffset, "ReminderRuleRelativeOffsetMinutes");
        AutomationProperties.SetAutomationId(_calendarFrequency, "ReminderRuleCalendarFrequency");
        AutomationProperties.SetAutomationId(_calendarInterval, "ReminderRuleCalendarInterval");
        AutomationProperties.SetAutomationId(_calendarMonth, "ReminderRuleCalendarMonth");
        AutomationProperties.SetAutomationId(_calendarDay, "ReminderRuleCalendarDay");
        AutomationProperties.SetAutomationId(_calendarLastDay, "ReminderRuleCalendarLastDay");
        AutomationProperties.SetAutomationId(_saveRuleButton, "SaveReminderRuleButton");
        AutomationProperties.SetAutomationId(_cancelRuleEditButton, "CancelReminderRuleEditButton");
        AutomationProperties.SetAutomationId(_countText, "ReminderRulesCount");
        AutomationProperties.SetAutomationId(_modeError, "ReminderRuleModeError");
        AutomationProperties.SetAutomationId(_firstDueError, "ReminderRuleFirstDueError");
        AutomationProperties.SetAutomationId(_intervalError, "ReminderRuleIntervalError");
        AutomationProperties.SetAutomationId(_relativeOffsetError, "ReminderRuleRelativeOffsetError");
        AutomationProperties.SetAutomationId(_calendarIntervalError, "ReminderRuleCalendarIntervalError");
        AutomationProperties.SetAutomationId(_calendarSelectorError, "ReminderRuleCalendarSelectorError");
        AutomationProperties.SetAutomationId(_collectionError, "ReminderRuleCollectionError");
        UiAccessibility.MarkLive(_modeError);
        UiAccessibility.MarkLive(_firstDueError);
        UiAccessibility.MarkLive(_intervalError);
        UiAccessibility.MarkLive(_relativeOffsetError);
        UiAccessibility.MarkLive(_calendarIntervalError);
        UiAccessibility.MarkLive(_calendarSelectorError);
        UiAccessibility.MarkLive(_collectionError);
    }

    private UIElement BuildContent()
    {
        var heading = new TextBlock
        {
            Text = "提醒规则",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowTitleFontSize");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _countText.Margin = new Thickness(12, 0, 0, 0);
        _countText.VerticalAlignment = VerticalAlignment.Center;
        _countText.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
        _countText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(heading);
        header.Children.Add(_countText);

        var scroller = new ScrollViewer
        {
            Content = _rulesHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 14, 0, 0)
        };
        AutomationProperties.SetName(scroller, "已配置提醒规则");

        var form = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        form.ColumnDefinitions.Add(new ColumnDefinition());
        AddFormRow(form, 0, "规则类型", _mode, _modeError);
        AddFormRow(form, 1, "首次提醒", BuildFirstDueRow(), _firstDueError, _firstDueElements);
        AddFormRow(form, 2, "固定间隔", BuildIntervalRow(), _intervalError, _intervalElements);
        AddFormRow(form, 3, "提前分钟", _relativeOffset, _relativeOffsetError, _relativeElements);
        AddFormRow(form, 4, "日历频率", _calendarFrequency, ErrorText(), _calendarFrequencyElements);
        AddFormRow(form, 5, "重复间隔", _calendarInterval, _calendarIntervalError, _calendarIntervalElements);
        AddFormRow(form, 6, "日历选择", _calendarSelector, _calendarSelectorError, _calendarSelectorElements);

        _saveRuleButton.SetResourceReference(StyleProperty, "PrimaryButton");
        var ruleActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        ruleActions.Children.Add(_saveRuleButton);
        ruleActions.Children.Add(_cancelRuleEditButton);

        var accept = new Button { Content = "完成", MinWidth = 96, IsDefault = true };
        accept.SetResourceReference(StyleProperty, "PrimaryButton");
        NameControl(accept, "接受提醒规则草稿");
        AutomationProperties.SetAutomationId(accept, "AcceptReminderRulesButton");
        var cancel = new Button
        {
            Content = "取消",
            MinWidth = 88,
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0)
        };
        NameControl(cancel, "取消提醒规则草稿");
        AutomationProperties.SetAutomationId(cancel, "CancelReminderRulesButton");
        accept.Click += (_, _) => AcceptDraft();
        cancel.Click += (_, _) => CancelDraft();
        var dialogActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        dialogActions.Children.Add(accept);
        dialogActions.Children.Add(cancel);

        var layout = new Grid { Margin = new Thickness(20) };
        layout.SetResourceReference(TextElement.FontSizeProperty, "SecondaryWindowBodyFontSize");
        AutomationProperties.SetAutomationId(layout, "ReminderRulesEditorContentRoot");
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scroller, 1);
        Grid.SetRow(form, 2);
        Grid.SetRow(ruleActions, 3);
        Grid.SetRow(dialogActions, 4);
        layout.Children.Add(header);
        layout.Children.Add(scroller);
        layout.Children.Add(form);
        layout.Children.Add(ruleActions);
        layout.Children.Add(_collectionError);
        Grid.SetRow(_collectionError, 3);
        _collectionError.HorizontalAlignment = HorizontalAlignment.Left;
        _collectionError.VerticalAlignment = VerticalAlignment.Center;
        layout.Children.Add(dialogActions);

        var surface = new Border
        {
            Child = layout,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        surface.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        return surface;
    }

    private UIElement BuildFirstDueRow()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        Grid.SetColumn(_firstDueHour, 1);
        var separator = new TextBlock
        {
            Text = ":",
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        separator.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowSectionFontSize");
        separator.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        Grid.SetColumn(separator, 2);
        Grid.SetColumn(_firstDueMinute, 3);
        row.Children.Add(_firstDueDate);
        row.Children.Add(_firstDueHour);
        row.Children.Add(separator);
        row.Children.Add(_firstDueMinute);
        return row;
    }

    private UIElement BuildIntervalRow()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        Grid.SetColumn(_customInterval, 1);
        _customInterval.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(_interval);
        row.Children.Add(_customInterval);
        return row;
    }

    private void BuildCalendarSelector()
    {
        var dateRow = new StackPanel { Orientation = Orientation.Horizontal };
        _calendarMonth.Width = 90;
        _calendarDay.Width = 90;
        _calendarDay.Margin = new Thickness(8, 0, 0, 0);
        _calendarLastDay.Margin = new Thickness(12, 0, 0, 0);
        dateRow.Children.Add(_calendarMonth);
        dateRow.Children.Add(_calendarDay);
        dateRow.Children.Add(_calendarLastDay);
        AutomationProperties.SetAutomationId(dateRow, "ReminderRuleCalendarDateSelector");
        _calendarSelector.Children.Add(_calendarWeekdays);
        _calendarSelector.Children.Add(dateRow);
    }

    private static void AddFormRow(
        Grid form,
        int rowIndex,
        string label,
        UIElement input,
        TextBlock error,
        ICollection<UIElement>? elements = null)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, rowIndex == 0 ? 0 : 10, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        labelBlock.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowBodyFontSize");
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        input.SetValue(Grid.RowProperty, rowIndex * 2);
        input.SetValue(Grid.ColumnProperty, 1);
        input.SetValue(FrameworkElement.MarginProperty, new Thickness(0, rowIndex == 0 ? 0 : 10, 0, 0));
        Grid.SetRow(labelBlock, rowIndex * 2);
        Grid.SetRow(error, rowIndex * 2 + 1);
        Grid.SetColumn(error, 1);
        form.Children.Add(labelBlock);
        form.Children.Add(input);
        form.Children.Add(error);
        elements?.Add(labelBlock);
        elements?.Add(input);
        elements?.Add(error);
    }

    private void WireEvents()
    {
        _mode.SelectionChanged += (_, _) =>
        {
            UpdateModeVisibility();
            MarkFormDirty();
        };
        _interval.SelectionChanged += (_, _) =>
        {
            UpdateModeVisibility();
            MarkFormDirty();
        };
        _firstDueDate.SelectedDateChanged += (_, _) => MarkFormDirty();
        _firstDueHour.SelectionChanged += (_, _) => MarkFormDirty();
        _firstDueMinute.SelectionChanged += (_, _) => MarkFormDirty();
        _customInterval.TextChanged += (_, _) => MarkFormDirty();
        _relativeOffset.TextChanged += (_, _) => MarkFormDirty();
        _calendarFrequency.SelectionChanged += (_, _) =>
        {
            EnsureWeeklySelection();
            UpdateModeVisibility();
            MarkFormDirty();
        };
        _calendarInterval.TextChanged += (_, _) => MarkFormDirty();
        _calendarMonth.SelectionChanged += (_, _) => MarkFormDirty();
        _calendarDay.SelectionChanged += (_, _) => MarkFormDirty();
        _calendarLastDay.Checked += (_, _) =>
        {
            UpdateModeVisibility();
            MarkFormDirty();
        };
        _calendarLastDay.Unchecked += (_, _) =>
        {
            UpdateModeVisibility();
            MarkFormDirty();
        };
        foreach (var checkBox in _calendarWeekdayChecks.Values)
        {
            checkBox.Checked += (_, _) => MarkFormDirty();
            checkBox.Unchecked += (_, _) => MarkFormDirty();
        }
        _saveRuleButton.Click += (_, _) => SaveDraftRule();
        _cancelRuleEditButton.Click += (_, _) => BeginAddRule();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && _editingRuleId is not null)
            {
                BeginAddRule();
                args.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            if (_initialErrorTarget != ReminderRuleEditingErrorTarget.None &&
                !string.IsNullOrWhiteSpace(_initialError))
            {
                ShowError(_initialErrorTarget, _initialError);
            }
            else
            {
                _mode.Focus();
            }
        };
    }

    private void RenderRules()
    {
        _rulesHost.Children.Clear();
        _countText.Text = $"{_rules.Count} / 8 条";
        if (_rules.Count == 0)
        {
            var empty = new TextBlock { Text = "当前不提醒" };
            empty.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
            empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            _rulesHost.Children.Add(empty);
            return;
        }

        foreach (var rule in _rules)
        {
            var summary = new TextBlock
            {
                Text = Describe(rule),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Regular
            };
            summary.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
            summary.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            var edit = ActionButton("编辑", $"编辑规则 {Describe(rule)}");
            var copy = ActionButton("复制", $"复制规则 {Describe(rule)}");
            var delete = ActionButton("删除", $"删除规则 {Describe(rule)}");
            edit.Tag = rule.Id;
            copy.Tag = rule.Id;
            delete.Tag = rule.Id;
            edit.Click += (_, _) => BeginEditRule(rule, copy: false);
            copy.Click += (_, _) => BeginEditRule(rule, copy: true);
            delete.Click += (_, _) => DeleteRule(rule.Id);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            actions.Children.Add(edit);
            actions.Children.Add(copy);
            actions.Children.Add(delete);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(actions, 1);
            row.Children.Add(summary);
            row.Children.Add(actions);
            var surface = new Border
            {
                Child = row,
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
            surface.SetResourceReference(Border.BackgroundProperty, "NoteBrush");
            surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
            _rulesHost.Children.Add(surface);
        }
    }

    private static Button ActionButton(string content, string accessibleName)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 58,
            Margin = new Thickness(6, 0, 0, 0)
        };
        NameControl(button, accessibleName);
        return button;
    }

    private void BeginAddRule()
    {
        _updatingForm = true;
        try
        {
            _editingRuleId = null;
            _mode.SelectedIndex = 0;
            _interval.SelectedIndex = 1;
            _customInterval.Clear();
            _relativeOffset.Text = "0";
            _calendarFrequency.SelectedIndex = 0;
            _calendarInterval.Text = "1";
            _calendarMonth.SelectedIndex = 0;
            _calendarDay.SelectedIndex = 0;
            _calendarLastDay.IsChecked = false;
            foreach (var checkBox in _calendarWeekdayChecks.Values)
            {
                checkBox.IsChecked = false;
            }
            var defaultDue = _scheduleEditor.GetDefaultFirstDueBeijing(
                ReminderEditingMode.Once,
                ReminderIntervalSelection.ThirtyMinutes,
                string.Empty,
                _clock.UtcNow).FirstDueBeijing;
            SetSchedule(defaultDue);
            UpdateModeVisibility();
            _saveRuleButton.Content = "添加规则";
            _cancelRuleEditButton.Visibility = Visibility.Collapsed;
            ClearErrors();
        }
        finally
        {
            _updatingForm = false;
            _formDirty = false;
        }
    }

    private void BeginEditRule(ReminderRule rule, bool copy)
    {
        _updatingForm = true;
        try
        {
            _editingRuleId = copy ? null : rule.Id;
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
            _mode.SelectedIndex = rule switch
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
                _interval.SelectedIndex = minutes switch
                {
                    15 => 0,
                    30 => 1,
                    60 => 2,
                    _ => 3
                };
                _customInterval.Text = minutes is 15 or 30 or 60
                    ? string.Empty
                    : minutes.ToString(CultureInfo.InvariantCulture);
            }

            if (rule is CountdownRelativeReminderRule relative)
            {
                _relativeOffset.Text = relative.OffsetMinutesBefore.ToString(CultureInfo.InvariantCulture);
            }

            if (rule is CalendarReminderRule calendar)
            {
                _calendarFrequency.SelectedIndex = (int)calendar.Frequency;
                _calendarInterval.Text = calendar.Interval.ToString(CultureInfo.InvariantCulture);
                foreach (var pair in _calendarWeekdayChecks)
                {
                    pair.Value.IsChecked = calendar.Selector is WeeklyCalendarSelector weekly &&
                        weekly.Weekdays.Contains(pair.Key);
                }
                _calendarMonth.SelectedIndex = calendar.Selector is YearlyCalendarSelector yearSelector
                    ? yearSelector.Month - 1
                    : 0;
                var day = calendar.Selector switch
                {
                    MonthlyCalendarSelector monthly => monthly.Day,
                    YearlyCalendarSelector yearDaySelector => yearDaySelector.Day,
                    _ => null
                };
                _calendarDay.SelectedIndex = (day ?? 1) - 1;
                _calendarLastDay.IsChecked = calendar.Selector is MonthlyCalendarSelector { LastDay: true } or
                    YearlyCalendarSelector { LastDay: true };
            }

            SetSchedule(dueUtc is null ? null : ChinaStandardTime.ToBeijingTime(dueUtc.Value));
            UpdateModeVisibility();
            _saveRuleButton.Content = copy ? "保存副本" : "保存修改";
            _cancelRuleEditButton.Visibility = Visibility.Visible;
            ClearErrors();
        }
        finally
        {
            _updatingForm = false;
            _formDirty = false;
        }

        if (_mode.SelectedIndex == 2 && _countdownTargetUtc is not null)
        {
            _relativeOffset.Focus();
        }
        else if (_mode.SelectedIndex == CalendarModeIndex)
        {
            _calendarFrequency.Focus();
        }
        else
        {
            _firstDueDate.Focus();
        }
    }

    private bool SaveDraftRule()
    {
        ClearErrors();
        var result = _editor.SaveRule(_rules, new ReminderRuleEditingRequest(
            _editingRuleId,
            SelectedMode(),
            _firstDueDate.SelectedDate,
            _firstDueHour.SelectedIndex,
            _firstDueMinute.SelectedIndex,
            SelectedInterval(),
            _customInterval.Text,
            _clock.UtcNow,
            _countdownTargetUtc,
            _relativeOffset.Text,
            SelectedCalendarFrequency(),
            _calendarInterval.Text,
            SelectedCalendarWeekdays(),
            _calendarDay.SelectedIndex >= 0
                ? (_calendarDay.SelectedIndex + 1).ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            _calendarMonth.SelectedIndex >= 0
                ? (_calendarMonth.SelectedIndex + 1).ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            _calendarLastDay.IsChecked == true));
        if (!result.Succeeded)
        {
            ShowError(result.ErrorTarget, result.Error);
            return false;
        }

        _rules = Freeze(result.Rules);
        RenderRules();
        BeginAddRule();
        return true;
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
        if (string.Equals(_editingRuleId, ruleId, StringComparison.OrdinalIgnoreCase))
        {
            BeginAddRule();
        }

        RenderRules();
    }

    private void AcceptDraft()
    {
        if (_formDirty && !SaveDraftRule())
        {
            return;
        }

        ResultRules = Freeze(_rules);
        DialogResult = true;
    }

    private void CancelDraft()
    {
        ResultRules = _initialRules;
        DialogResult = false;
    }

    private void SetSchedule(DateTimeOffset? beijing)
    {
        _firstDueDate.SelectedDate = beijing?.Date;
        _firstDueHour.SelectedIndex = beijing?.Hour ?? -1;
        _firstDueMinute.SelectedIndex = beijing?.Minute ?? -1;
    }

    private ReminderEditingMode SelectedMode() => _mode.SelectedIndex switch
    {
        0 => ReminderEditingMode.Once,
        1 => ReminderEditingMode.Repeating,
        2 when _countdownTargetUtc is not null => ReminderEditingMode.CountdownRelative,
        var index when index == CalendarModeIndex => ReminderEditingMode.Calendar,
        _ => (ReminderEditingMode)(-1)
    };

    private int CalendarModeIndex => _countdownTargetUtc is null ? 2 : 3;

    private CalendarFrequency SelectedCalendarFrequency() => _calendarFrequency.SelectedIndex switch
    {
        0 => CalendarFrequency.Daily,
        1 => CalendarFrequency.Weekly,
        2 => CalendarFrequency.Monthly,
        3 => CalendarFrequency.Yearly,
        _ => (CalendarFrequency)(-1)
    };

    private IReadOnlyList<DayOfWeek> SelectedCalendarWeekdays() => _calendarWeekdayChecks
        .Where(static pair => pair.Value.IsChecked == true)
        .Select(static pair => pair.Key)
        .ToArray();

    private ReminderIntervalSelection SelectedInterval() => _interval.SelectedIndex switch
    {
        0 => ReminderIntervalSelection.FifteenMinutes,
        1 => ReminderIntervalSelection.ThirtyMinutes,
        2 => ReminderIntervalSelection.SixtyMinutes,
        _ => ReminderIntervalSelection.Custom
    };

    private void ShowError(ReminderRuleEditingErrorTarget target, string error)
    {
        var (message, control) = target switch
        {
            ReminderRuleEditingErrorTarget.Mode => (_modeError, (Control)_mode),
            ReminderRuleEditingErrorTarget.FirstDue => (_firstDueError, _firstDueDate),
            ReminderRuleEditingErrorTarget.Interval => (_intervalError,
                _interval.SelectedIndex == 3 ? _customInterval : _interval),
            ReminderRuleEditingErrorTarget.RelativeOffset => (_relativeOffsetError, _relativeOffset),
            ReminderRuleEditingErrorTarget.CalendarInterval => (_calendarIntervalError, _calendarInterval),
            ReminderRuleEditingErrorTarget.CalendarSelector => (_calendarSelectorError,
                SelectedCalendarFrequency() == CalendarFrequency.Weekly
                    ? _calendarWeekdayChecks.Values.First()
                    : _calendarLastDay),
            _ => (_collectionError, (Control)_saveRuleButton)
        };
        message.Text = error;
        message.Visibility = Visibility.Visible;
        AutomationProperties.SetName(message, error);
        AutomationProperties.SetHelpText(control, error);
        control.Focus();
    }

    private void ClearErrors()
    {
        foreach (var message in new[]
                 {
                     _modeError,
                     _firstDueError,
                     _intervalError,
                     _relativeOffsetError,
                     _calendarIntervalError,
                     _calendarSelectorError,
                     _collectionError
                 })
        {
            message.Text = string.Empty;
            message.Visibility = Visibility.Collapsed;
        }

        foreach (var control in new Control[]
                 {
                     _mode,
                     _firstDueDate,
                     _firstDueHour,
                     _firstDueMinute,
                     _interval,
                     _customInterval,
                     _relativeOffset,
                     _calendarFrequency,
                     _calendarInterval,
                     _calendarMonth,
                     _calendarDay,
                     _calendarLastDay,
                     _saveRuleButton
                 })
        {
            AutomationProperties.SetHelpText(control, string.Empty);
        }
    }

    private void MarkFormDirty()
    {
        if (!_updatingForm)
        {
            _formDirty = true;
        }

        ClearErrors();
    }

    private void UpdateModeVisibility()
    {
        var relative = _mode.SelectedIndex == 2 && _countdownTargetUtc is not null;
        var calendar = _mode.SelectedIndex == CalendarModeIndex;
        SetVisibility(_firstDueElements, relative ? Visibility.Collapsed : Visibility.Visible);
        SetVisibility(
            _intervalElements,
            !relative && _mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed);
        SetVisibility(_relativeElements, relative ? Visibility.Visible : Visibility.Collapsed);
        SetVisibility(_calendarFrequencyElements, calendar ? Visibility.Visible : Visibility.Collapsed);
        SetVisibility(_calendarIntervalElements, calendar ? Visibility.Visible : Visibility.Collapsed);
        SetVisibility(_calendarSelectorElements, calendar ? Visibility.Visible : Visibility.Collapsed);
        _calendarWeekdays.Visibility = calendar && SelectedCalendarFrequency() == CalendarFrequency.Weekly
            ? Visibility.Visible
            : Visibility.Collapsed;
        var dateSelector = _calendarSelector.Children[1];
        dateSelector.Visibility = calendar && SelectedCalendarFrequency() is CalendarFrequency.Monthly or CalendarFrequency.Yearly
            ? Visibility.Visible
            : Visibility.Collapsed;
        _calendarMonth.Visibility = calendar && SelectedCalendarFrequency() == CalendarFrequency.Yearly
            ? Visibility.Visible
            : Visibility.Collapsed;
        _calendarDay.Visibility = _calendarLastDay.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        _customInterval.Visibility = !relative && _mode.SelectedIndex == 1 && _interval.SelectedIndex == 3
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void EnsureWeeklySelection()
    {
        if (_updatingForm || SelectedCalendarFrequency() != CalendarFrequency.Weekly ||
            _calendarWeekdayChecks.Values.Any(static checkBox => checkBox.IsChecked == true))
        {
            return;
        }

        var weekday = _firstDueDate.SelectedDate?.DayOfWeek ?? DayOfWeek.Monday;
        _calendarWeekdayChecks[weekday].IsChecked = true;
    }

    private static void SetVisibility(IEnumerable<UIElement> elements, Visibility visibility)
    {
        foreach (var element in elements)
        {
            element.Visibility = visibility;
        }
    }

    private static TextBlock ErrorText()
    {
        var text = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        return text;
    }

    private static string Describe(ReminderRule rule) => rule switch
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

    private static void NameControl(DependencyObject target, string name) =>
        AutomationProperties.SetName(target, name);
}
