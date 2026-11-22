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
    private const double InitialWindowWidth = 640;
    private const double MaximumInitialWindowHeight = 790;
    private const double RuleListMaxContentHeight = 288;
    private const double WorkAreaMargin = 18;
    private const double OuterChromeVerticalInset = 42;

    private readonly ReminderRulesEditorViewModel _viewModel;
    private readonly BooleanToVisibilityConverter _visibilityConverter = new();
    private readonly StackPanel _rulesHost = new();
    private readonly TextBlock _countText = new();
    private readonly ComboBox _mode = new();
    private readonly DatePicker _firstDueDate = new WpfUiDatePicker();
    private readonly TextBox _firstDueTime = new();
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
    private readonly TextBlock _rulePreview = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Border _rulePreviewSurface = new()
    {
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 6, 10, 6)
    };
    private readonly Button _saveRuleButton = new() { MinWidth = 104 };
    private readonly Button _cancelRuleEditButton = new()
    {
        Content = "取消编辑",
        MinWidth = 92
    };
    private TextBlock _firstDueLabel = null!;
    private Button _cancelDraftButton = null!;
    private Border _dragHeader = null!;
    private bool _hasManualWindowPlacement;
    private bool _placementInitialized;
    private int _nativeWindowDragRequestCount;
    private int _nativeWindowDragCompletionCount;

    internal ReminderRulesEditorWindow(
        IReadOnlyList<ReminderRule> initialRules,
        IClock clock,
        IGuidSource? guidSource = null,
        ReminderRuleEditingErrorTarget initialErrorTarget = ReminderRuleEditingErrorTarget.None,
        string initialError = "",
        DateTimeOffset? countdownTargetUtc = null,
        DiagnosticLogger? logger = null,
        IDwmWindowChromeHost? dwmChromeHost = null)
    {
        _viewModel = new ReminderRulesEditorViewModel(
            initialRules,
            clock,
            guidSource,
            initialErrorTarget,
            initialError,
            countdownTargetUtc);

        Title = "提醒规则";
        Width = InitialWindowWidth;
        MinWidth = 560;
        MinHeight = 300;
        MaxHeight = MaximumInitialWindowHeight;
        SizeToContent = SizeToContent.Height;
        ShowInTaskbar = false;
        // #322：SizeToContent.Height 下由 ReapplyPlacement 统一按多屏工作区居中于所有者并夹取，
        // 避免 WPF 延迟 CenterOwner 把副屏负坐标夹回系统主屏。
        WindowStartupLocation = WindowStartupLocation.Manual;
        // #324：改为不透明无边框窗口，向 DWM 请求系统圆角、投影与 1 DIP 暖边。
        SetResourceReference(BackgroundProperty, "CanvasBrush");
        SetResourceReference(Window.FontFamilyProperty, "UiFontFamily");
        DwmWindowChrome.ConfigureOpaqueBorderlessChrome(this);
        ResizeMode = ResizeMode.CanResizeWithGrip;
        DataContext = _viewModel;
        AutomationProperties.SetName(this, "提醒规则编辑器");

        ConfigureControls();
        var surface = BuildContent();
        DwmWindowChrome.AttachSecondaryWindow(
            this,
            surface,
            "PeachLineColor",
            "PeachLineBrush",
            DwmWindowChrome.ReminderRulesEditorEvents,
            logger,
            dwmChromeHost);
        Content = surface;
        Bind(_firstDueLabel, TextBlock.TextProperty, nameof(ReminderRulesEditorViewModel.FirstDueLabel), _viewModel);
        WireEvents();
        WindowWorkAreaGeometry.TrackChanges(this, ReapplyPlacement,
            () => _hasManualWindowPlacement = true);
        RenderRules();
    }

    internal IReadOnlyList<ReminderRule> ResultRules => _viewModel.ResultRules;

    internal bool HasManualWindowPlacement => _hasManualWindowPlacement;

    internal int NativeWindowDragRequestCount => _nativeWindowDragRequestCount;

    internal int NativeWindowDragCompletionCount => _nativeWindowDragCompletionCount;

    internal Border DragHeader => _dragHeader;

    internal void ReapplyPlacement()
    {
        var monitor = MonitorWorkAreaResolver.ResolveForWindow(
            !_placementInitialized && Owner is { } owner ? owner : this);
        var usable = Rect.Inflate(monitor.WorkAreaDip, -WorkAreaMargin, -WorkAreaMargin);
        if (usable.IsEmpty || usable.Width <= 0 || usable.Height <= 0)
        {
            return;
        }

        MaxWidth = Math.Max(MinWidth, usable.Width);
        MaxHeight = Math.Max(MinHeight, usable.Height);
        var targetWidth = double.IsNaN(Width) ? ActualWidth : Width;
        if (targetWidth > 0)
        {
            Width = Math.Clamp(targetWidth, MinWidth, MaxWidth);
        }

        if (SizeToContent == SizeToContent.Height)
        {
            InvalidateMeasure();
            UpdateLayout();
        }
        else
        {
            var targetHeight = double.IsNaN(Height) ? ActualHeight : Height;
            if (targetHeight > 0)
            {
                Height = Math.Clamp(targetHeight, MinHeight, MaxHeight);
            }
        }

        var currentWidth = Math.Clamp(
            ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? MinWidth : Width),
            MinWidth,
            MaxWidth);
        var currentHeight = Math.Clamp(
            ActualHeight > 0 ? ActualHeight : (double.IsNaN(Height) ? MinHeight : Height),
            MinHeight,
            MaxHeight);

        if (!_placementInitialized && !_hasManualWindowPlacement)
        {
            if (Owner is { } ownerWindow)
            {
                Left = ownerWindow.Left + (ownerWindow.ActualWidth - currentWidth) / 2;
                Top = ownerWindow.Top + (ownerWindow.ActualHeight - currentHeight) / 2;
            }
            else
            {
                if (double.IsNaN(Left))
                {
                    Left = usable.Left + Math.Max(0, (usable.Width - currentWidth) / 2);
                }

                if (double.IsNaN(Top))
                {
                    Top = usable.Top + Math.Max(0, (usable.Height - currentHeight) / 2);
                }
            }
        }

        Left = double.IsNaN(Left)
            ? usable.Left + Math.Max(0, (usable.Width - currentWidth) / 2)
            : Math.Clamp(Left, usable.Left, Math.Max(usable.Left, usable.Right - currentWidth));
        Top = double.IsNaN(Top)
            ? usable.Top + Math.Max(0, (usable.Height - currentHeight) / 2)
            : Math.Clamp(Top, usable.Top, Math.Max(usable.Top, usable.Bottom - currentHeight));
        _placementInitialized = true;
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
        NameControl(_firstDueTime, "首次提醒时刻");
        NameControl(_interval, "固定间隔分钟");
        NameControl(_customInterval, "自定义固定间隔分钟");
        NameControl(_relativeOffset, "倒计时提前分钟");
        NameControl(_calendarFrequency, "重复周期");
        NameControl(_calendarInterval, "每隔几个周期");
        NameControl(_calendarSelector, "具体哪几天");
        NameControl(_calendarMonth, "具体月份");
        NameControl(_calendarDay, "具体日期");
        NameControl(_calendarLastDay, "最后一天");
        NameControl(_saveRuleButton, "保存提醒规则");
        NameControl(_cancelRuleEditButton, "取消编辑提醒规则");

        AutomationProperties.SetAutomationId(_mode, "ReminderRuleMode");
        AutomationProperties.SetAutomationId(_firstDueDate, "ReminderRuleFirstDueDate");
        AutomationProperties.SetAutomationId(_firstDueTime, "ReminderRuleFirstDueTime");
        AutomationProperties.SetAutomationId(_interval, "ReminderRuleInterval");
        AutomationProperties.SetAutomationId(_customInterval, "ReminderRuleCustomInterval");
        AutomationProperties.SetAutomationId(_relativeOffset, "ReminderRuleRelativeOffsetMinutes");
        AutomationProperties.SetAutomationId(_calendarFrequency, "ReminderRuleCalendarFrequency");
        AutomationProperties.SetAutomationId(_calendarInterval, "ReminderRuleCalendarInterval");
        AutomationProperties.SetAutomationId(_calendarSelector, "ReminderRuleCalendarSelector");
        AutomationProperties.SetAutomationId(_calendarMonth, "ReminderRuleCalendarMonth");
        AutomationProperties.SetAutomationId(_calendarDay, "ReminderRuleCalendarDay");
        AutomationProperties.SetAutomationId(_calendarLastDay, "ReminderRuleCalendarLastDay");
        AutomationProperties.SetAutomationId(_rulePreviewSurface, "ReminderRulePreviewSurface");
        AutomationProperties.SetAutomationId(_rulePreview, "ReminderRulePreview");
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
        Bind(_mode, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsModeErrorVisible), _viewModel);
        Bind(_firstDueDate, DatePicker.SelectedDateProperty, nameof(ReminderRulesEditorViewModel.FirstDueDate), _viewModel, BindingMode.TwoWay);
        TimeInputBehavior.SetFormat(_firstDueTime, TimeInputFormat.HourMinute);
        TimeInputBehavior.SetIsEnabled(_firstDueTime, true);
        Bind(_firstDueTime, TimeInputBehavior.ValueProperty, nameof(ReminderRulesEditorViewModel.FirstDueTime), _viewModel, BindingMode.TwoWay);
        Bind(_firstDueTime, TimeInputBehavior.HasInvalidTextProperty, nameof(ReminderRulesEditorViewModel.HasInvalidFirstDueTimeInput), _viewModel, BindingMode.TwoWay);
        Bind(_firstDueDate, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsFirstDueDateErrorVisible), _viewModel);
        Bind(_firstDueTime, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsFirstDueTimeErrorVisible), _viewModel);
        Bind(_interval, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.IntervalIndex), _viewModel, BindingMode.TwoWay);
        NumericInputBehavior.SetMinimum(_customInterval, ReminderRulesEditorViewModel.MinimumFixedIntervalMinutes);
        NumericInputBehavior.SetMaximum(_customInterval, ReminderRulesEditorViewModel.MaximumFixedIntervalMinutes);
        NumericInputBehavior.SetStep(_customInterval, 1);
        NumericInputBehavior.SetIsEnabled(_customInterval, true);
        Bind(_customInterval, NumericInputBehavior.ValueProperty, nameof(ReminderRulesEditorViewModel.CustomIntervalMinutes), _viewModel, BindingMode.TwoWay);
        Bind(_customInterval, NumericInputBehavior.HasInvalidTextProperty, nameof(ReminderRulesEditorViewModel.HasInvalidCustomIntervalInput), _viewModel, BindingMode.TwoWay);
        Bind(_customInterval, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsIntervalErrorVisible), _viewModel);
        Bind(_interval, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsIntervalComboErrorVisible), _viewModel);
        NumericInputBehavior.SetMinimum(_relativeOffset, ReminderRulesEditorViewModel.MinimumRelativeOffsetMinutes);
        NumericInputBehavior.SetMaximum(_relativeOffset, ReminderRulesEditorViewModel.MaximumRelativeOffsetMinutes);
        NumericInputBehavior.SetStep(_relativeOffset, 1);
        NumericInputBehavior.SetIsEnabled(_relativeOffset, true);
        Bind(_relativeOffset, NumericInputBehavior.ValueProperty, nameof(ReminderRulesEditorViewModel.RelativeOffsetMinutes), _viewModel, BindingMode.TwoWay);
        Bind(_relativeOffset, NumericInputBehavior.HasInvalidTextProperty, nameof(ReminderRulesEditorViewModel.HasInvalidRelativeOffsetInput), _viewModel, BindingMode.TwoWay);
        Bind(_relativeOffset, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsRelativeOffsetErrorVisible), _viewModel);
        Bind(_calendarFrequency, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.CalendarFrequencyIndex), _viewModel, BindingMode.TwoWay);
        Bind(_calendarFrequency, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsCalendarFrequencyErrorVisible), _viewModel);
        NumericInputBehavior.SetMinimum(_calendarInterval, ReminderRulesEditorViewModel.MinimumCalendarInterval);
        NumericInputBehavior.SetMaximum(_calendarInterval, ReminderRulesEditorViewModel.MaximumCalendarInterval);
        NumericInputBehavior.SetStep(_calendarInterval, 1);
        NumericInputBehavior.SetIsEnabled(_calendarInterval, true);
        Bind(_calendarInterval, NumericInputBehavior.ValueProperty, nameof(ReminderRulesEditorViewModel.CalendarInterval), _viewModel, BindingMode.TwoWay);
        Bind(_calendarInterval, NumericInputBehavior.HasInvalidTextProperty, nameof(ReminderRulesEditorViewModel.HasInvalidCalendarIntervalInput), _viewModel, BindingMode.TwoWay);
        Bind(_calendarInterval, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsCalendarIntervalErrorVisible), _viewModel);
        Bind(_calendarMonth, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.CalendarMonthIndex), _viewModel, BindingMode.TwoWay);
        Bind(_calendarMonth, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsCalendarMonthErrorVisible), _viewModel);
        Bind(_calendarDay, Selector.SelectedIndexProperty, nameof(ReminderRulesEditorViewModel.CalendarDayIndex), _viewModel, BindingMode.TwoWay);
        Bind(_calendarDay, InputErrorState.HasErrorProperty, nameof(ReminderRulesEditorViewModel.IsCalendarDayErrorVisible), _viewModel);
        Bind(_calendarLastDay, ToggleButton.IsCheckedProperty, nameof(ReminderRulesEditorViewModel.CalendarLastDay), _viewModel, BindingMode.TwoWay);
        _rulePreview.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
        _rulePreviewSurface.Child = _rulePreview;
        _rulePreviewSurface.SetResourceReference(Border.CornerRadiusProperty, "RadiusCard");
        _rulePreviewSurface.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        _rulePreviewSurface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        Bind(_rulePreview, TextBlock.TextProperty, nameof(ReminderRulesEditorViewModel.RulePreviewText), _viewModel);
        Bind(_rulePreview, AutomationProperties.NameProperty, nameof(ReminderRulesEditorViewModel.RulePreviewText), _viewModel);
        UpdateRulePreviewForeground();
        Bind(_saveRuleButton, Button.ContentProperty, nameof(ReminderRulesEditorViewModel.SaveRuleButtonText), _viewModel);
        Bind(_cancelRuleEditButton, UIElement.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsCancelRuleEditVisible), _viewModel, converter: _visibilityConverter);
        Bind(_saveRuleButton, Button.CommandProperty, nameof(ReminderRulesEditorViewModel.SaveRuleCommand), _viewModel);
        Bind(_cancelRuleEditButton, Button.CommandProperty, nameof(ReminderRulesEditorViewModel.CancelRuleEditCommand), _viewModel);

        BindError(_modeError, nameof(ReminderRulesEditorViewModel.ModeError), nameof(ReminderRulesEditorViewModel.IsModeErrorVisible), () => _viewModel.ModeError);
        BindError(_firstDueError, nameof(ReminderRulesEditorViewModel.FirstDueError), nameof(ReminderRulesEditorViewModel.IsFirstDueErrorVisible), () => _viewModel.FirstDueError);
        BindError(_intervalError, nameof(ReminderRulesEditorViewModel.IntervalError), nameof(ReminderRulesEditorViewModel.IsIntervalErrorVisible), () => _viewModel.IntervalError);
        BindError(_relativeOffsetError, nameof(ReminderRulesEditorViewModel.RelativeOffsetError), nameof(ReminderRulesEditorViewModel.IsRelativeOffsetErrorVisible), () => _viewModel.RelativeOffsetError);
        BindError(_calendarIntervalError, nameof(ReminderRulesEditorViewModel.CalendarIntervalError), nameof(ReminderRulesEditorViewModel.IsCalendarIntervalErrorVisible), () => _viewModel.CalendarIntervalError);
        BindError(_calendarSelectorError, nameof(ReminderRulesEditorViewModel.CalendarSelectorError), nameof(ReminderRulesEditorViewModel.IsCalendarSelectorErrorVisible), () => _viewModel.CalendarSelectorError);
        BindError(_collectionError, nameof(ReminderRulesEditorViewModel.CollectionError), nameof(ReminderRulesEditorViewModel.IsCollectionErrorVisible), () => _viewModel.CollectionError);

        Bind(_firstDueDate, DatePicker.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsFirstDueVisible), _viewModel, converter: _visibilityConverter);
        Bind(_firstDueTime, TextBox.VisibilityProperty, nameof(ReminderRulesEditorViewModel.IsFirstDueVisible), _viewModel, converter: _visibilityConverter);
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
        Bind(_firstDueTime, AutomationProperties.NameProperty, nameof(ReminderRulesEditorViewModel.FirstDueTimeAutomationName), _viewModel);

        BindHelpText(_mode, nameof(ReminderRulesEditorViewModel.ModeError));
        BindHelpText(_firstDueDate, nameof(ReminderRulesEditorViewModel.FirstDueDateHelpText));
        BindHelpText(_firstDueTime, nameof(ReminderRulesEditorViewModel.FirstDueTimeHelpText));
        BindHelpText(_interval, nameof(ReminderRulesEditorViewModel.IntervalComboHelpText));
        BindHelpText(_customInterval, nameof(ReminderRulesEditorViewModel.CustomIntervalHelpText));
        BindHelpText(_relativeOffset, nameof(ReminderRulesEditorViewModel.RelativeOffsetError));
        BindHelpText(_calendarFrequency, nameof(ReminderRulesEditorViewModel.CalendarFrequencyHelpText));
        BindHelpText(_calendarInterval, nameof(ReminderRulesEditorViewModel.CalendarIntervalError));
        BindHelpText(_calendarMonth, nameof(ReminderRulesEditorViewModel.CalendarMonthHelpText));
        BindHelpText(_calendarDay, nameof(ReminderRulesEditorViewModel.CalendarDayHelpText));
        BindHelpText(_calendarLastDay, nameof(ReminderRulesEditorViewModel.CalendarSelectorError));
        BindHelpText(_saveRuleButton, nameof(ReminderRulesEditorViewModel.CollectionError));
        foreach (var checkBox in _calendarWeekdayChecks.Values)
        {
            BindHelpText(checkBox, nameof(ReminderRulesEditorViewModel.CalendarSelectorError));
        }
    }

    private Border BuildContent()
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

        var unitSpacing = SpacingTokens.Resolve(this, SpacingTokens.Unit);
        var blockGap = SpacingTokens.Resolve(this, SpacingTokens.BlockGap);
        var scroller = new ScrollViewer
        {
            Content = _rulesHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, blockGap, 0, 0)
        };
        AutomationProperties.SetName(scroller, "已配置提醒规则");
        scroller.SetResourceReference(FocusVisualStyleProperty, "MainKeyboardFocusVisual");

        var form = new Grid { Margin = new Thickness(0, blockGap, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        form.ColumnDefinitions.Add(new ColumnDefinition());
        AddFormRow(form, 0, "规则类型", _mode, _modeError);
        _firstDueLabel = AddFormRow(form, 1, "首次提醒", BuildFirstDueRow(), _firstDueError,
            nameof(ReminderRulesEditorViewModel.IsFirstDueVisible));
        AutomationProperties.SetLabeledBy(_firstDueDate, _firstDueLabel);
        AutomationProperties.SetLabeledBy(_firstDueTime, _firstDueLabel);
        var intervalLabel = AddFormRow(form, 2, "固定间隔", BuildIntervalRow(), _intervalError,
            nameof(ReminderRulesEditorViewModel.IsIntervalVisible));
        AutomationProperties.SetLabeledBy(_interval, intervalLabel);
        AutomationProperties.SetLabeledBy(_customInterval, intervalLabel);
        AddFormRow(form, 3, "提前分钟", _relativeOffset, _relativeOffsetError,
            nameof(ReminderRulesEditorViewModel.IsRelativeVisible));
        AddFormRow(form, 4, "重复周期", _calendarFrequency, ErrorText(),
            nameof(ReminderRulesEditorViewModel.IsCalendarFrequencyVisible));
        AddFormRow(form, 5, "每隔几个周期", _calendarInterval, _calendarIntervalError,
            nameof(ReminderRulesEditorViewModel.IsCalendarIntervalVisible));
        var calendarSelectorLabel = AddFormRow(form, 6, "具体哪几天", _calendarSelector, _calendarSelectorError,
            nameof(ReminderRulesEditorViewModel.IsCalendarSelectorVisible));
        Bind(calendarSelectorLabel, UIElement.VisibilityProperty,
            nameof(ReminderRulesEditorViewModel.IsCalendarSelectorControlsVisible), _viewModel,
            converter: _visibilityConverter);
        AutomationProperties.SetLabeledBy(_calendarMonth, calendarSelectorLabel);
        AutomationProperties.SetLabeledBy(_calendarDay, calendarSelectorLabel);
        AutomationProperties.SetLabeledBy(_calendarLastDay, calendarSelectorLabel);
        foreach (var checkBox in _calendarWeekdayChecks.Values)
        {
            AutomationProperties.SetLabeledBy(checkBox, calendarSelectorLabel);
        }
        var previewLabel = AddPreviewRow(form, 7, "规则预览", _rulePreviewSurface);
        AutomationProperties.SetLabeledBy(_rulePreview, previewLabel);

        // #322：窗口级「完成」为唯一实心主行动，表单提交改为次级按钮样式。
        _saveRuleButton.SetResourceReference(StyleProperty, "SecondaryButton");
        _cancelRuleEditButton.SetResourceReference(StyleProperty, "SecondaryButton");
        _cancelRuleEditButton.Margin = new Thickness(unitSpacing, 0, 0, 0);
        var ruleActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ruleActions.Children.Add(_saveRuleButton);
        ruleActions.Children.Add(_cancelRuleEditButton);
        var ruleActionsRow = new Grid { Margin = new Thickness(0, blockGap, 0, 0) };
        ruleActionsRow.ColumnDefinitions.Add(new ColumnDefinition());
        ruleActionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _collectionError.HorizontalAlignment = HorizontalAlignment.Left;
        _collectionError.VerticalAlignment = VerticalAlignment.Center;
        _collectionError.Margin = new Thickness(0, 0, blockGap, 0);
        Grid.SetColumn(_collectionError, 0);
        Grid.SetColumn(ruleActions, 1);
        ruleActionsRow.Children.Add(_collectionError);
        ruleActionsRow.Children.Add(ruleActions);

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
            Margin = new Thickness(unitSpacing, 0, 0, 0)
        };
        NameControl(cancel, "取消提醒规则草稿");
        cancel.SetResourceReference(StyleProperty, "SecondaryButton");
        AutomationProperties.SetAutomationId(cancel, "CancelReminderRulesButton");
        Bind(cancel, Button.CommandProperty, nameof(ReminderRulesEditorViewModel.CancelDraftCommand), _viewModel);
        _cancelDraftButton = cancel;
        var dialogActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        dialogActions.Children.Add(accept);
        dialogActions.Children.Add(cancel);

        var layout = new RuleEditorLayoutPanel(
            _dragHeader,
            scroller,
            form,
            ruleActionsRow,
            dialogActions,
            () => SizeToContent == SizeToContent.Height,
            RuleListMaxContentHeight + blockGap,
            MaximumInitialWindowHeight - OuterChromeVerticalInset)
        {
            Margin = new Thickness(20)
        };
        layout.SetResourceReference(TextElement.FontSizeProperty, "SecondaryWindowBodyFontSize");
        AutomationProperties.SetAutomationId(layout, "ReminderRulesEditorContentRoot");

        var surface = new Border
        {
            Child = layout
        };
        surface.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        return surface;
    }

    private UIElement BuildFirstDueRow()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        // #312/#311/#329：单框日期控件与相邻可键入时刻框之间保留 SpacingUnit(8 DIP) 间距，避免外框相撞。
        _firstDueDate.Margin = new Thickness(0, 0, SpacingTokens.Resolve(this, SpacingTokens.Unit), 0);
        var timeSlot = new Grid();
        timeSlot.Children.Add(_firstDueTime);
        timeSlot.Children.Add(CreateTimePlaceholder(_firstDueTime));
        Grid.SetColumn(timeSlot, 1);
        row.Children.Add(_firstDueDate);
        row.Children.Add(timeSlot);
        return row;
    }

    private static SilentTextBlock CreateTimePlaceholder(TextBox target)
    {
        var placeholder = new SilentTextBlock
        {
            Text = "HH:mm",
            Margin = new Thickness(12, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = string.IsNullOrEmpty(target.Text) ? Visibility.Visible : Visibility.Collapsed
        };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Typography.SetNumeralAlignment(placeholder, FontNumeralAlignment.Tabular);
        target.TextChanged += (_, _) =>
            placeholder.Visibility = string.IsNullOrEmpty(target.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        return placeholder;
    }

    private UIElement BuildIntervalRow()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        Grid.SetColumn(_customInterval, 1);
        _customInterval.Margin = new Thickness(SpacingTokens.Resolve(this, SpacingTokens.Unit), 0, 0, 0);
        row.Children.Add(_interval);
        row.Children.Add(_customInterval);
        return row;
    }

    private void BuildCalendarSelector()
    {
        _calendarDateSelector.Orientation = Orientation.Horizontal;
        _calendarMonth.Width = 90;
        _calendarDay.Width = 90;
        _calendarDay.Margin = new Thickness(SpacingTokens.Resolve(this, SpacingTokens.Unit), 0, 0, 0);
        _calendarLastDay.Margin = new Thickness(12, 0, 0, 0);
        AutomationProperties.SetAutomationId(_calendarDateSelector, "ReminderRuleCalendarDateSelector");
        _calendarDateSelector.Children.Add(_calendarMonth);
        _calendarDateSelector.Children.Add(_calendarDay);
        _calendarDateSelector.Children.Add(_calendarLastDay);
        _calendarSelector.Children.Add(_calendarWeekdays);
        _calendarSelector.Children.Add(_calendarDateSelector);
    }

    private TextBlock AddFormRow(
        Grid form,
        int rowIndex,
        string label,
        UIElement input,
        TextBlock error,
        string? visibilityProperty = null)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (visibilityProperty is not null)
        {
            Bind(row, UIElement.VisibilityProperty, visibilityProperty, _viewModel, converter: _visibilityConverter);
        }
        var fieldRowGap = SpacingTokens.Resolve(this, SpacingTokens.FieldRow);
        var labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, rowIndex == 0 ? 0 : fieldRowGap, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        labelBlock.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowBodyFontSize");
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        AutomationProperties.SetLabeledBy(input, labelBlock);
        input.SetValue(Grid.RowProperty, 0);
        input.SetValue(Grid.ColumnProperty, 1);
        input.SetValue(FrameworkElement.MarginProperty, new Thickness(0, rowIndex == 0 ? 0 : fieldRowGap, 0, 0));
        Grid.SetRow(error, 1);
        Grid.SetColumn(error, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(input);
        row.Children.Add(error);
        Grid.SetRow(row, rowIndex);
        Grid.SetColumnSpan(row, 2);
        form.Children.Add(row);
        return labelBlock;
    }

    private TextBlock AddPreviewRow(Grid form, int rowIndex, string label, UIElement previewSurface)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var fieldRowGap = SpacingTokens.Resolve(this, SpacingTokens.FieldRow);
        var labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, fieldRowGap, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        labelBlock.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowBodyFontSize");
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        AutomationProperties.SetLabeledBy(previewSurface, labelBlock);
        previewSurface.SetValue(Grid.RowProperty, 0);
        previewSurface.SetValue(Grid.ColumnProperty, 1);
        previewSurface.SetValue(FrameworkElement.MarginProperty, new Thickness(0, fieldRowGap, 0, 0));
        row.Children.Add(labelBlock);
        row.Children.Add(previewSurface);
        Grid.SetRow(row, rowIndex);
        Grid.SetColumnSpan(row, 2);
        form.Children.Add(row);
        return labelBlock;
    }

    private void WireEvents()
    {
        ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.RuleItems).CollectionChanged +=
            (_, _) => RenderRules();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.PropertyName) ||
                string.Equals(args.PropertyName, nameof(ReminderRulesEditorViewModel.IsRulePreviewValid), StringComparison.Ordinal))
            {
                UpdateRulePreviewForeground();
            }
        };
        _viewModel.DraftAccepted += (_, _) => DialogResult = true;
        _viewModel.DraftCancelled += (_, _) => DialogResult = false;
        _viewModel.ValidationErrorRaised += OnValidationErrorRaised;
        _viewModel.FormFocusRequested += OnFormFocusRequested;
        _firstDueTime.LostKeyboardFocus += (_, args) =>
        {
            if (!ReferenceEquals(args.NewFocus, _cancelRuleEditButton) &&
                !ReferenceEquals(args.NewFocus, _cancelDraftButton))
            {
                _viewModel.ValidateFirstDueTimeOnBlur();
            }
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && _viewModel.EditingRuleId is not null)
            {
                _viewModel.CancelRuleEditCommand.Execute(null);
                args.Handled = true;
            }
        };
        SizeChanged += (_, _) => ClampToCurrentWorkArea();
        Loaded += (_, _) =>
        {
            ReapplyPlacement();
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

    private void ClampToCurrentWorkArea()
    {
        if (!_placementInitialized || !IsVisible)
        {
            return;
        }

        var monitor = MonitorWorkAreaResolver.ResolveForWindow(this);
        var usable = Rect.Inflate(monitor.WorkAreaDip, -WorkAreaMargin, -WorkAreaMargin);
        if (usable.IsEmpty || usable.Width <= 0 || usable.Height <= 0)
        {
            return;
        }

        var width = Math.Clamp(ActualWidth > 0 ? ActualWidth : Width, MinWidth, MaxWidth);
        var height = Math.Clamp(ActualHeight > 0 ? ActualHeight : Height, MinHeight, MaxHeight);
        var clampedLeft = Math.Clamp(Left, usable.Left, Math.Max(usable.Left, usable.Right - width));
        var clampedTop = Math.Clamp(Top, usable.Top, Math.Max(usable.Top, usable.Bottom - height));
        if (Math.Abs(clampedLeft - Left) > 0.5)
        {
            Left = clampedLeft;
        }

        if (Math.Abs(clampedTop - Top) > 0.5)
        {
            Top = clampedTop;
        }
    }

    private void OnValidationErrorRaised(object? sender, ReminderRuleEditorErrorEventArgs args)
    {
        var control = GetErrorControl(args.Target);
        control.Focus();
        if (IsActive || IsKeyboardFocusWithin)
        {
            Keyboard.Focus(control);
        }
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
        ReminderRuleEditingErrorTarget.FirstDue =>
            _viewModel.FirstDueDate is not null &&
            (_viewModel.FirstDueTime is null || _viewModel.HasInvalidFirstDueTimeInput)
                ? _firstDueTime
                : _firstDueDate,
        ReminderRuleEditingErrorTarget.Interval => _interval.SelectedIndex == 3 ? _customInterval : _interval,
        ReminderRuleEditingErrorTarget.RelativeOffset => _relativeOffset,
        ReminderRuleEditingErrorTarget.CalendarInterval => _calendarInterval,
        ReminderRuleEditingErrorTarget.CalendarSelector => GetCalendarSelectorErrorControl(),
        _ => _saveRuleButton
    };

    private Control GetCalendarSelectorErrorControl()
    {
        if (_viewModel.IsCalendarFrequencyErrorVisible)
        {
            return _calendarFrequency;
        }

        if (_viewModel.IsCalendarWeekdaysVisible)
        {
            return _calendarWeekdayChecks.Values.First();
        }

        if (_viewModel.IsCalendarMonthErrorVisible)
        {
            return _calendarMonth;
        }

        if (_viewModel.IsCalendarDayErrorVisible)
        {
            return _calendarDay;
        }

        return _calendarLastDay;
    }

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

        var itemSpacing = SpacingTokens.Resolve(this, SpacingTokens.Unit);
        var itemCount = _viewModel.RuleItems.Count;
        for (var index = 0; index < itemCount; index++)
        {
            var item = _viewModel.RuleItems[index];
            var summary = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Regular
            };
            summary.SetResourceReference(TextBlock.FontSizeProperty, "SecondaryWindowMinorFontSize");
            summary.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            Bind(summary, TextBlock.TextProperty, nameof(ReminderRuleItemViewModel.Description), item);
            var edit = InlineActionButton.Create(
                this, InlineActionSurface.ReminderRule, InlineActionIntent.Edit, item.RuleId, $"编辑规则 {item.Description}");
            var copy = InlineActionButton.Create(
                this, InlineActionSurface.ReminderRule, InlineActionIntent.Copy, item.RuleId, $"复制规则 {item.Description}");
            var delete = InlineActionButton.Create(
                this, InlineActionSurface.ReminderRule, InlineActionIntent.Delete, item.RuleId, $"删除规则 {item.Description}");
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
                Margin = new Thickness(0, 0, 0, index == itemCount - 1 ? 0 : itemSpacing),
                BorderThickness = new Thickness(1)
            };
            surface.SetResourceReference(Border.CornerRadiusProperty, "RadiusCard");
            surface.SetResourceReference(Border.BackgroundProperty, "NoteBrush");
            surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
            _rulesHost.Children.Add(surface);
        }
    }

    // #257：行内动作的内容、可访问性、样式、危险语义、焦点和提示均由 InlineActionButton 提供。

    private void BindError(TextBlock target, string textPath, string visibilityPath, Func<string?> readText)
    {
        UiAccessibility.BindAnnouncedText(target, _viewModel, textPath, readText);
        Bind(target, TextBlock.VisibilityProperty, visibilityPath, _viewModel, converter: _visibilityConverter);
    }

    private void BindHelpText(Control target, string path) =>
        Bind(target, AutomationProperties.HelpTextProperty, path, _viewModel);

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

    private void UpdateRulePreviewForeground() =>
        _rulePreview.SetResourceReference(
            TextBlock.ForegroundProperty,
            _viewModel.IsRulePreviewValid ? "InkBrush" : "MutedBrush");

    private static void NameControl(DependencyObject target, string name) =>
        AutomationProperties.SetName(target, name);

    // #322：规则列表按内容取高，超上限或窗口受工作区约束时在列表内滚动，表单紧随其后不拉大空白，
    // 且固定区块与底部动作始终保持可见。
    private sealed class RuleEditorLayoutPanel : Panel
    {
        private readonly UIElement _header;
        private readonly UIElement _ruleList;
        private readonly UIElement _form;
        private readonly UIElement _ruleActionsRow;
        private readonly UIElement _dialogActions;
        private readonly Func<bool> _isAutoSizing;
        private readonly double _autoListSlotMaxHeight;
        private readonly double _autoContentMaxHeight;

        internal RuleEditorLayoutPanel(
            UIElement header,
            UIElement ruleList,
            UIElement form,
            UIElement ruleActionsRow,
            UIElement dialogActions,
            Func<bool> isAutoSizing,
            double autoListSlotMaxHeight,
            double autoContentMaxHeight)
        {
            _header = header;
            _ruleList = ruleList;
            _form = form;
            _ruleActionsRow = ruleActionsRow;
            _dialogActions = dialogActions;
            _isAutoSizing = isAutoSizing;
            _autoListSlotMaxHeight = autoListSlotMaxHeight;
            _autoContentMaxHeight = autoContentMaxHeight;

            Children.Add(header);
            Children.Add(ruleList);
            Children.Add(form);
            Children.Add(ruleActionsRow);
            Children.Add(dialogActions);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var infiniteHeight = new Size(availableSize.Width, double.PositiveInfinity);
            _header.Measure(infiniteHeight);
            _form.Measure(infiniteHeight);
            _ruleActionsRow.Measure(infiniteHeight);
            _dialogActions.Measure(infiniteHeight);

            var fixedHeight =
                _header.DesiredSize.Height +
                _form.DesiredSize.Height +
                _ruleActionsRow.DesiredSize.Height +
                _dialogActions.DesiredSize.Height;

            var autoSizing = _isAutoSizing();
            var effectiveHeightLimit = availableSize.Height;
            if (autoSizing)
            {
                effectiveHeightLimit = Math.Min(effectiveHeightLimit, _autoContentMaxHeight);
            }

            var listBudget = autoSizing ? _autoListSlotMaxHeight : double.PositiveInfinity;
            if (!double.IsInfinity(effectiveHeightLimit))
            {
                listBudget = Math.Min(listBudget, Math.Max(0, effectiveHeightLimit - fixedHeight));
            }

            _ruleList.Measure(new Size(availableSize.Width, listBudget));

            var desiredWidth = Math.Max(
                double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                Math.Max(
                    Math.Max(_header.DesiredSize.Width, _ruleList.DesiredSize.Width),
                    Math.Max(
                        _form.DesiredSize.Width,
                        Math.Max(_ruleActionsRow.DesiredSize.Width, _dialogActions.DesiredSize.Width))));
            var desiredHeight = fixedHeight + _ruleList.DesiredSize.Height;
            if (autoSizing)
            {
                // 把自动取高后的整窗高度向上对齐到 4 DIP 整数倍，避免 125%/150%/175% 缩放下
                // 分数 DIP 导致窗口右下角 ResizeGrip 与 RenderSize 产生亚像素浮点偏差。
                var snappedOuterHeight = Math.Ceiling((desiredHeight + OuterChromeVerticalInset) / 4.0) * 4.0;
                desiredHeight = Math.Max(desiredHeight, snappedOuterHeight - OuterChromeVerticalInset);
            }
            if (!double.IsInfinity(effectiveHeightLimit))
            {
                desiredHeight = Math.Min(effectiveHeightLimit, desiredHeight);
            }

            return new Size(
                double.IsInfinity(availableSize.Width) ? desiredWidth : availableSize.Width,
                desiredHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var width = finalSize.Width;
            var fixedHeight =
                _header.DesiredSize.Height +
                _form.DesiredSize.Height +
                _ruleActionsRow.DesiredSize.Height +
                _dialogActions.DesiredSize.Height;
            var remainingForList = Math.Max(0, finalSize.Height - fixedHeight);
            var listHeight = Math.Min(_ruleList.DesiredSize.Height, remainingForList);

            var y = 0.0;
            _header.Arrange(new Rect(0, y, width, _header.DesiredSize.Height));
            y += _header.DesiredSize.Height;

            _ruleList.Arrange(new Rect(0, y, width, listHeight));
            y += listHeight;

            _form.Arrange(new Rect(0, y, width, _form.DesiredSize.Height));
            y += _form.DesiredSize.Height;

            _ruleActionsRow.Arrange(new Rect(0, y, width, _ruleActionsRow.DesiredSize.Height));
            y += _ruleActionsRow.DesiredSize.Height;

            var dialogY = Math.Max(y, finalSize.Height - _dialogActions.DesiredSize.Height);
            _dialogActions.Arrange(new Rect(0, dialogY, width, _dialogActions.DesiredSize.Height));

            return finalSize;
        }
    }
}
