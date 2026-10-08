using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class ReminderRulesEditorWindow : Window
{
    private readonly ReminderRulesEditorViewModel _viewModel;
    private readonly BooleanToVisibilityConverter _visibilityConverter = new();
    private readonly StackPanel _rulesHost = new();
    private readonly TextBlock _countText = new();
    private readonly ComboBox _mode = new();
    private readonly DatePicker _firstDueDate = new WpfUiDatePicker();
    private readonly ComboBox _firstDueHour = new();
    private readonly ComboBox _firstDueMinute = new();
    private readonly ComboBox _interval = new();
    private readonly TextBox _customInterval = new() { MaxLength = 4 };
    private readonly TextBox _relativeOffset = new() { MaxLength = 7 };
    private readonly ComboBox _calendarFrequency = new();
    private readonly TextBox _calendarInterval = new() { MaxLength = 3 };
    private readonly ComboBox _calendarMonth = new();
    private readonly ComboBox _calendarDay = new();
    private readonly CheckBox _calendarLastDay = new() { Content = "最后一天" };
    private readonly WrapPanel _calendarWeekdays = new();
    private readonly StackPanel _calendarSelector = new();
    private readonly StackPanel _calendarDateSelector = new();
    private readonly Dictionary<DayOfWeek, CheckBox> _calendarWeekdayChecks = new();
    private readonly TextBlock _modeError = ErrorText();
    private readonly TextBlock _firstDueError = ErrorText();
    private readonly TextBlock _intervalError = ErrorText();
    private readonly TextBlock _relativeOffsetError = ErrorText();
    private readonly TextBlock _calendarIntervalError = ErrorText();
    private readonly TextBlock _calendarSelectorError = ErrorText();
    private readonly TextBlock _collectionError = ErrorText();
    private readonly Button _saveRuleButton = new() { MinWidth = 104 };
    private readonly Button _cancelRuleEditButton = new()
    {
        MinWidth = 92,
        Margin = new Thickness(8, 0, 0, 0)
    };
    private TextBlock _firstDueLabel = null!;
    private Border _dragHeader = null!;
    private bool _hasManualWindowPlacement;
    private int _nativeWindowDragRequestCount;
    private int _nativeWindowDragCompletionCount;

    internal ReminderRulesEditorWindow(
        IReadOnlyList<ReminderRule> initialRules,
        IClock clock,
        IGuidSource? guidSource = null,
        ReminderRuleEditingErrorTarget initialErrorTarget = ReminderRuleEditingErrorTarget.None,
        string initialError = "",
        DateTimeOffset? countdownTargetUtc = null)
    {
        _viewModel = new ReminderRulesEditorViewModel(
            initialRules,
            clock,
            guidSource,
            initialErrorTarget,
            initialError,
            countdownTargetUtc);

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
        DataContext = _viewModel;
        AutomationProperties.SetName(this, "提醒规则编辑器");

        ConfigureControls();
        Content = BuildContent();
        Bind(_firstDueLabel, TextBlock.TextProperty, nameof(ReminderRulesEditorViewModel.FirstDueLabel), _viewModel);
        WireEvents();
        RenderRules();
    }

    internal IReadOnlyList<ReminderRule> ResultRules => _viewModel.ResultRules;

    internal bool HasManualWindowPlacement => _hasManualWindowPlacement;

    internal int NativeWindowDragRequestCount => _nativeWindowDragRequestCount;

    internal int NativeWindowDragCompletionCount => _nativeWindowDragCompletionCount;

    internal Border DragHeader => _dragHeader;

    internal void ReapplyPlacement()
    {
        if (_hasManualWindowPlacement)
        {
            return;
        }
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        SecondaryWindowChrome.DragFromHeader(
            this,
            eventArgs,
            _dragHeader,
            () => _hasManualWindowPlacement = true,
            ref _nativeWindowDragRequestCount,
            ref _nativeWindowDragCompletionCount);
    }

    private void ConfigureControls()
    {
        _mode.ItemsSource = _viewModel.ModeOptions;
        _firstDueHour.ItemsSource = _viewModel.Hours;
        _firstDueMinute.ItemsSource = _viewModel.Minutes;
        _interval.ItemsSource = _viewModel.IntervalOptions;
        _calendarFrequency.ItemsSource = _viewModel.CalendarFrequencyOptions;
        _calendarMonth.ItemsSource = _viewModel.Months;
        _calendarDay.ItemsSource = _viewModel.Days;

        foreach (var weekday in _viewModel.Weekdays)
        {
            var checkBox = new CheckBox
            {
                Content = weekday.Text,
                Margin = new Thickness(0, 0, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            NameControl(checkBox, $"每周星期{weekday.Text}");
            AutomationProperties.SetAutomationId(checkBox, $"ReminderRuleCalendarWeekday{weekday.Day}");
            Bind(checkBox, ToggleButton.IsCheckedProperty, nameof(ReminderRuleWeekdayViewModel.IsSelected), weekday, BindingMode.TwoWay);
            _calendarWeekdayChecks.Add(weekday.Day, checkBox);
            _calendarWeekdays.Children.Add(checkBox);
        }

        BuildCalendarSelector();
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

        Bind(_countText, TextBlock.TextProperty, nameof(ReminderRulesEditorViewModel.CountText), _viewModel);
        Bind(_mode, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.ModeIndex), _viewModel, BindingMode.TwoWay);
        Bind(_firstDueDate, DatePicker.SelectedDateProperty, nameof(ReminderRulesEditorViewModel.FirstDueDate), _viewModel, BindingMode.TwoWay);
        Bind(_firstDueHour, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.FirstDueHourIndex), _viewModel, BindingMode.TwoWay);
        Bind(_firstDueMinute, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.FirstDueMinuteIndex), _viewModel, BindingMode.TwoWay);
        Bind(_interval, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.IntervalIndex), _viewModel, BindingMode.TwoWay);
        BindText(_customInterval, nameof(ReminderRulesEditorViewModel.CustomIntervalMinutes));
        BindText(_relativeOffset, nameof(ReminderRulesEditorViewModel.RelativeOffsetMinutes));
        Bind(_calendarFrequency, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.CalendarFrequencyIndex), _viewModel, BindingMode.TwoWay);
        BindText(_calendarInterval, nameof(ReminderRulesEditorViewModel.CalendarInterval));
        Bind(_calendarMonth, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.CalendarMonthIndex), _viewModel, BindingMode.TwoWay);
        Bind(_calendarDay, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.CalendarDayIndex), _viewModel, BindingMode.TwoWay);
        Bind(_calendarLastDay, ToggleButton.IsCheckedProperty, nameof(ReminderRulesEditorViewModel.CalendarLastDay), _viewModel, BindingMode.TwoWay);
        Bind(_saveRuleButton, Button.ContentProperty, nameof(ReminderRulesEditorViewModel.SaveRuleButtonText), _viewModel);
        Bind(_cancelRuleEditButton, UIElement.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCancelRuleEditVisible), _viewModel, converter: _visibilityConverter);
        Bind(_saveRuleButton, Button.CommandProperty, nameof(ReminderRulesEditorViewModel.SaveRuleCommand), _viewModel);
        Bind(_cancelRuleEditButton, Button.CommandProperty, nameof(ReminderRulesEditorViewModel.CancelRuleEditCommand), _viewModel);

        BindError(_modeError, nameof(ReminderRulesEditorViewModel.ModeError), nameof(ReminderRulesEditorViewModel.IsModeErrorVisible));
        BindError(_firstDueError, nameof(ReminderRulesEditorViewModel.FirstDueError), nameof(ReminderRulesEditorViewModel.IsFirstDueErrorVisible));
        BindError(_intervalError, nameof(ReminderRulesEditorViewModel.IntervalError), nameof(ReminderRulesEditorViewModel.IsIntervalErrorVisible));
        BindError(_relativeOffsetError, nameof(ReminderRulesEditorViewModel.RelativeOffsetError), nameof(ReminderRulesEditorViewModel.IsRelativeOffsetErrorVisible));
        BindError(_calendarIntervalError, nameof(ReminderRulesEditorViewModel.CalendarIntervalError), nameof(ReminderRulesEditorViewModel.IsCalendarIntervalErrorVisible));
        BindError(_calendarSelectorError, nameof(ReminderRulesEditorViewModel.CalendarSelectorError), nameof(ReminderRulesEditorViewModel.IsCalendarSelectorErrorVisible));
        BindError(_collectionError, nameof(ReminderRulesEditorViewModel.CollectionError), nameof(ReminderRulesEditorViewModel.IsCollectionErrorVisible));

        Bind(_firstDueDate, DatePicker.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsFirstDueVisible), _viewModel, converter: _visibilityConverter);
        Bind(_firstDueHour, ComboBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsFirstDueVisible), _viewModel, converter: _visibilityConverter);
        Bind(_firstDueMinute, ComboBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsFirstDueVisible), _viewModel, converter: _visibilityConverter);
        Bind(_interval, ComboBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsIntervalVisible), _viewModel, converter: _visibilityConverter);
        Bind(_customInterval, TextBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCustomIntervalVisible), _viewModel, converter: _visibilityConverter);
        Bind(_relativeOffset, TextBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsRelativeVisible), _viewModel, converter: _visibilityConverter);
        Bind(_calendarFrequency, ComboBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCalendarFrequencyVisible), _viewModel, converter: _visibilityConverter);
        Bind(_calendarInterval, TextBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCalendarIntervalVisible), _viewModel, converter: _visibilityConverter);
        Bind(_calendarSelector, StackPanel.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCalendarSelectorVisible), _viewModel, converter: _visibilityConverter);
        Bind(_calendarWeekdays, WrapPanel.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCalendarWeekdaysVisible), _viewModel, converter: _visibilityConverter);
        Bind(_calendarDateSelector, StackPanel.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCalendarDateSelectorVisible), _viewModel, converter: _visibilityConverter);
        Bind(_calendarMonth, ComboBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCalendarMonthVisible), _viewModel, converter: _visibilityConverter);
        Bind(_calendarDay, ComboBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCalendarDayVisible), _viewModel, converter: _visibilityConverter);
        Bind(_firstDueDate, AutomationProperties.NameProperty, nameof(ReminderRulesEditorViewModel.FirstDueDateAutomationName), _viewModel);
        Bind(_firstDueHour, AutomationProperties.NameProperty, nameof(ReminderRulesEditorViewModel.FirstDueHourAutomationName), _viewModel);
        Bind(_firstDueMinute, AutomationProperties.NameProperty, nameof(ReminderRulesEditorViewModel.FirstDueMinuteAutomationName), _viewModel);

        BindHelpText(_mode, nameof(ReminderRulesEditorViewModel.ModeError));
        BindHelpText(_firstDueDate, nameof(ReminderRulesEditorViewModel.FirstDueError));
        BindHelpText(_firstDueHour, nameof(ReminderRulesEditorViewModel.FirstDueError));
        BindHelpText(_firstDueMinute, nameof(ReminderRulesEditorViewModel.FirstDueError));
        BindHelpText(_interval, nameof(ReminderRulesEditorViewModel.IntervalError));
        BindHelpText(_customInterval, nameof(ReminderRulesEditorViewModel.IntervalError));
        BindHelpText(_relativeOffset, nameof(ReminderRulesEditorViewModel.RelativeOffsetError));
        BindHelpText(_calendarFrequency, nameof(ReminderRulesEditorViewModel.CalendarSelectorError));
        BindHelpText(_calendarInterval, nameof(ReminderRulesEditorViewModel.CalendarIntervalError));
        BindHelpText(_calendarMonth, nameof(ReminderRulesEditorViewModel.CalendarSelectorError));
        BindHelpText(_calendarDay, nameof(ReminderRulesEditorViewModel.CalendarSelectorError));
        BindHelpText(_calendarLastDay, nameof(ReminderRulesEditorViewModel.CalendarSelectorError));
        BindHelpText(_saveRuleButton, nameof(ReminderRulesEditorViewModel.CollectionError));
        foreach (var checkBox in _calendarWeekdayChecks.Values)
        {
            BindHelpText(checkBox, nameof(ReminderRulesEditorViewModel.CalendarSelectorError));
        }
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
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(heading);
        titleRow.Children.Add(_countText);
        _dragHeader = new Border
        {
            Child = titleRow,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Padding = new Thickness(0, 4, 0, 4)
        };
        AutomationProperties.SetAutomationId(_dragHeader, "ReminderRulesEditorDragHeader");
        _dragHeader.MouseLeftButtonDown += OnHeaderMouseLeftButtonDown;

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
        _firstDueLabel = AddFormRow(form, 1, "首次提醒", BuildFirstDueRow(), _firstDueError);
        AddFormRow(form, 2, "固定间隔", BuildIntervalRow(), _intervalError);
        AddFormRow(form, 3, "提前分钟", _relativeOffset, _relativeOffsetError);
        AddFormRow(form, 4, "日历频率", _calendarFrequency, ErrorText());
        AddFormRow(form, 5, "重复间隔", _calendarInterval, _calendarIntervalError);
        AddFormRow(form, 6, "日历选择", _calendarSelector, _calendarSelectorError);

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
        Bind(accept, Button.CommandProperty, nameof(ReminderRulesEditorViewModel.AcceptDraftCommand), _viewModel);
        var cancel = new Button
        {
            Content = "取消",
            MinWidth = 88,
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0)
        };
        NameControl(cancel, "取消提醒规则草稿");
        AutomationProperties.SetAutomationId(cancel, "CancelReminderRulesButton");
        Bind(cancel, Button.CommandProperty, nameof(ReminderRulesEditorViewModel.CancelDraftCommand), _viewModel);
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
        layout.Children.Add(_dragHeader);
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
        _calendarDateSelector.Orientation = Orientation.Horizontal;
        _calendarMonth.Width = 90;
        _calendarDay.Width = 90;
        _calendarDay.Margin = new Thickness(8, 0, 0, 0);
        _calendarLastDay.Margin = new Thickness(12, 0, 0, 0);
        AutomationProperties.SetAutomationId(_calendarDateSelector, "ReminderRuleCalendarDateSelector");
        _calendarDateSelector.Children.Add(_calendarMonth);
        _calendarDateSelector.Children.Add(_calendarDay);
        _calendarDateSelector.Children.Add(_calendarLastDay);
        _calendarSelector.Children.Add(_calendarWeekdays);
        _calendarSelector.Children.Add(_calendarDateSelector);
    }

    private static TextBlock AddFormRow(
        Grid form,
        int rowIndex,
        string label,
        UIElement input,
        TextBlock error)
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
        return labelBlock;
    }

    private void WireEvents()
    {
        ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.RuleItems).CollectionChanged +=
            (_, _) => RenderRules();
        _viewModel.DraftAccepted += (_, _) => DialogResult = true;
        _viewModel.DraftCancelled += (_, _) => DialogResult = false;
        _viewModel.ValidationErrorRaised += OnValidationErrorRaised;
        _viewModel.FormFocusRequested += OnFormFocusRequested;
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && _viewModel.EditingRuleId is not null)
            {
                _viewModel.CancelRuleEditCommand.Execute(null);
                args.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            if (_viewModel.HasInitialError)
            {
                _viewModel.ShowInitialError();
            }
            else
            {
                _mode.Focus();
            }
        };
    }

    private void OnValidationErrorRaised(object? sender, ReminderRuleEditorErrorEventArgs args)
    {
        GetErrorControl(args.Target).Focus();
    }

    private void OnFormFocusRequested(object? sender, EventArgs args)
    {
        if (_viewModel.ModeIndex == 2 && _viewModel.CountdownTargetUtc is not null)
        {
            _relativeOffset.Focus();
        }
        else if (_viewModel.ModeIndex == (_viewModel.CountdownTargetUtc is null ? 2 : 3))
        {
            _calendarFrequency.Focus();
        }
        else
        {
            _firstDueDate.Focus();
        }
    }

    private Control GetErrorControl(ReminderRuleEditingErrorTarget target) => target switch
    {
        ReminderRuleEditingErrorTarget.Mode => _mode,
        ReminderRuleEditingErrorTarget.FirstDue => _firstDueDate,
        ReminderRuleEditingErrorTarget.Interval => _interval.SelectedIndex == 3 ? _customInterval : _interval,
        ReminderRuleEditingErrorTarget.RelativeOffset => _relativeOffset,
        ReminderRuleEditingErrorTarget.CalendarInterval => _calendarInterval,
        ReminderRuleEditingErrorTarget.CalendarSelector => _viewModel.CalendarFrequencyIndex == 1
            ? _calendarWeekdayChecks.Values.First()
            : _calendarLastDay,
        _ => _saveRuleButton
    };

    private void RenderRules()
    {
        _rulesHost.Children.Clear();
        if (_viewModel.IsEmpty)
        {
            var empty = new TextBlock();
            Bind(empty, TextBlock.TextProperty, nameof(ReminderRulesEditorViewModel.EmptyText), _viewModel);
            empty.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
            empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            _rulesHost.Children.Add(empty);
            return;
        }

        foreach (var item in _viewModel.RuleItems)
        {
            var summary = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Regular
            };
            summary.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
            summary.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            Bind(summary, TextBlock.TextProperty, nameof(ReminderRuleItemViewModel.Description), item);
            var edit = ActionButton("编辑", $"编辑规则 {item.Description}");
            var copy = ActionButton("复制", $"复制规则 {item.Description}");
            var delete = ActionButton("删除", $"删除规则 {item.Description}");
            edit.Tag = item.RuleId;
            copy.Tag = item.RuleId;
            delete.Tag = item.RuleId;
            delete.Style = (Style)FindResource("DangerButton");
            Bind(edit, Button.CommandProperty, nameof(ReminderRuleItemViewModel.EditCommand), item);
            Bind(copy, Button.CommandProperty, nameof(ReminderRuleItemViewModel.CopyCommand), item);
            Bind(delete, Button.CommandProperty, nameof(ReminderRuleItemViewModel.DeleteCommand), item);
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

    private void BindError(TextBlock target, string textPath, string visibilityPath)
    {
        Bind(target, TextBlock.TextProperty, textPath, _viewModel);
        Bind(target, TextBlock.VisibilityProperty, visibilityPath, _viewModel, converter: _visibilityConverter);
        Bind(target, AutomationProperties.NameProperty, textPath, _viewModel);
    }

    private void BindHelpText(Control target, string path) =>
        Bind(target, AutomationProperties.HelpTextProperty, path, _viewModel);

    private void BindText(TextBox target, string path) =>
        Bind(
            target,
            TextBox.TextProperty,
            path,
            _viewModel,
            BindingMode.TwoWay,
            updateSourceTrigger: UpdateSourceTrigger.PropertyChanged);

    private static void Bind(
        DependencyObject target,
        DependencyProperty property,
        string path,
        object source,
        BindingMode mode = BindingMode.Default,
        IValueConverter? converter = null,
        UpdateSourceTrigger updateSourceTrigger = UpdateSourceTrigger.Default) =>
        BindingOperations.SetBinding(
            target,
            property,
            new Binding(path)
            {
                Source = source,
                Mode = mode,
                Converter = converter,
                UpdateSourceTrigger = updateSourceTrigger
            });

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

    private static void NameControl(DependencyObject target, string name) =>
        AutomationProperties.SetName(target, name);
}
