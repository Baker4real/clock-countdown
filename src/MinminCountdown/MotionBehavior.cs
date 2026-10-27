using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MinminCountdown;

/// <summary>
/// Applies the first motion slice to template surfaces without changing layout.
/// The control templates keep their semantic state resources; this behavior only
/// interpolates the resulting brush colors and the surface scale.
/// </summary>
internal static class MotionBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(MotionBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty HoverBackgroundResourceKeyProperty =
        DependencyProperty.RegisterAttached(
            "HoverBackgroundResourceKey",
            typeof(string),
            typeof(MotionBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HoverBorderBrushResourceKeyProperty =
        DependencyProperty.RegisterAttached(
            "HoverBorderBrushResourceKey",
            typeof(string),
            typeof(MotionBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PressedBackgroundResourceKeyProperty =
        DependencyProperty.RegisterAttached(
            "PressedBackgroundResourceKey",
            typeof(string),
            typeof(MotionBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CheckedBackgroundResourceKeyProperty =
        DependencyProperty.RegisterAttached(
            "CheckedBackgroundResourceKey",
            typeof(string),
            typeof(MotionBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CheckedBorderBrushResourceKeyProperty =
        DependencyProperty.RegisterAttached(
            "CheckedBorderBrushResourceKey",
            typeof(string),
            typeof(MotionBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(MotionController),
            typeof(MotionBehavior),
            new PropertyMetadata(null));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetHoverBackgroundResourceKey(DependencyObject element, string? value) =>
        element.SetValue(HoverBackgroundResourceKeyProperty, value);

    public static string? GetHoverBackgroundResourceKey(DependencyObject element) =>
        (string?)element.GetValue(HoverBackgroundResourceKeyProperty);

    public static void SetHoverBorderBrushResourceKey(DependencyObject element, string? value) =>
        element.SetValue(HoverBorderBrushResourceKeyProperty, value);

    public static string? GetHoverBorderBrushResourceKey(DependencyObject element) =>
        (string?)element.GetValue(HoverBorderBrushResourceKeyProperty);

    public static void SetPressedBackgroundResourceKey(DependencyObject element, string? value) =>
        element.SetValue(PressedBackgroundResourceKeyProperty, value);

    public static string? GetPressedBackgroundResourceKey(DependencyObject element) =>
        (string?)element.GetValue(PressedBackgroundResourceKeyProperty);

    public static void SetCheckedBackgroundResourceKey(DependencyObject element, string? value) =>
        element.SetValue(CheckedBackgroundResourceKeyProperty, value);

    public static string? GetCheckedBackgroundResourceKey(DependencyObject element) =>
        (string?)element.GetValue(CheckedBackgroundResourceKeyProperty);

    public static void SetCheckedBorderBrushResourceKey(DependencyObject element, string? value) =>
        element.SetValue(CheckedBorderBrushResourceKeyProperty, value);

    public static string? GetCheckedBorderBrushResourceKey(DependencyObject element) =>
        (string?)element.GetValue(CheckedBorderBrushResourceKeyProperty);

    internal static void SetInputForValidation(FrameworkElement templateHost, bool isMouseOver, bool isPressed)
    {
        ApplicationTheme.EnsureMotionValidationContext();
        if (!ApplicationTheme.HasAnimationPreferenceOverrideForValidation ||
            templateHost.ReadLocalValue(ControllerProperty) is not MotionController controller)
        {
            throw new InvalidOperationException("动效输入验证需要偏好作用域和已附加的生产模板控制器。");
        }

        controller.SetInputForValidation(isMouseOver, isPressed);
    }

    internal static void ClearInputForValidation(FrameworkElement templateHost)
    {
        ApplicationTheme.EnsureMotionValidationContext();
        if (templateHost.ReadLocalValue(ControllerProperty) is MotionController controller)
        {
            controller.ClearInputForValidation();
        }
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        if (args.NewValue is true)
        {
            var controller = new MotionController(element);
            element.SetValue(ControllerProperty, controller);
            controller.Attach();
        }
        else if (element.ReadLocalValue(ControllerProperty) is MotionController controller)
        {
            controller.Detach();
            element.ClearValue(ControllerProperty);
        }
    }

    private sealed class MotionController
    {
        private readonly FrameworkElement _host;
        private Control? _control;
        private Border? _surface;
        private DependencyPropertyDescriptor? _pressedDescriptor;
        private DependencyPropertyDescriptor? _checkedDescriptor;
        private long _stateVersion;
        private bool _attached;
        private (bool IsMouseOver, bool IsPressed)? _validationInput;

        public MotionController(FrameworkElement host)
        {
            _host = host;
        }

        public void SetInputForValidation(bool isMouseOver, bool isPressed)
        {
            if (!_attached || _control is null || _surface is null)
            {
                throw new InvalidOperationException("动效验证模板尚未加载。");
            }

            _validationInput = (isMouseOver, isPressed);
            OnStateChanged(_host, EventArgs.Empty);
        }

        public void ClearInputForValidation()
        {
            _validationInput = null;
            OnStateChanged(_host, EventArgs.Empty);
        }

        private bool IsMouseOver => _validationInput?.IsMouseOver ?? _control?.IsMouseOver == true;

        private bool IsPressed => _validationInput?.IsPressed ?? _control is ButtonBase { IsPressed: true };

        public void Attach()
        {
            _host.Loaded += OnLoaded;
            _host.Unloaded += OnUnloaded;
            if (_host.IsLoaded)
            {
                ResolveAndApply();
            }
        }

        public void Detach()
        {
            _host.Loaded -= OnLoaded;
            _host.Unloaded -= OnUnloaded;
            DetachFromControl();
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => ResolveAndApply();

        private void OnUnloaded(object sender, RoutedEventArgs args) => DetachFromControl();

        private void ResolveAndApply()
        {
            if (_attached)
            {
                return;
            }

            if (_host.TemplatedParent is not Control control ||
                control.Template is not { } template)
            {
                return;
            }

            var surface = template.FindName("Surface", control) as Border;
            if (surface is null)
            {
                surface = template.FindName("Box", control) as Border;
            }

            if (surface is null)
            {
                return;
            }

            _control = control;
            _surface = surface;
            _pressedDescriptor = DependencyPropertyDescriptor.FromProperty(
                ButtonBase.IsPressedProperty,
                typeof(ButtonBase));
            _checkedDescriptor = DependencyPropertyDescriptor.FromProperty(
                ToggleButton.IsCheckedProperty,
                typeof(ToggleButton));
            control.MouseEnter += OnMouseEnter;
            control.MouseLeave += OnMouseLeave;
            control.IsEnabledChanged += OnEnabledChanged;
            if (control is ButtonBase)
            {
                _pressedDescriptor?.AddValueChanged(control, OnStateChanged);
            }

            if (control is ToggleButton)
            {
                _checkedDescriptor?.AddValueChanged(control, OnCheckedChanged);
            }

            _attached = true;
            ApplicationTheme.PaletteChanged += OnPaletteChanged;
            ApplyState(
                CurrentBrushColor(surface.Background),
                CurrentBrushColor(surface.BorderBrush),
                CurrentScale(surface),
                animateColors: false);
        }

        private void DetachFromControl()
        {
            ApplicationTheme.PaletteChanged -= OnPaletteChanged;
            _stateVersion++;
            if (_control is not null)
            {
                _control.MouseEnter -= OnMouseEnter;
                _control.MouseLeave -= OnMouseLeave;
                _control.IsEnabledChanged -= OnEnabledChanged;
                _pressedDescriptor?.RemoveValueChanged(_control, OnStateChanged);
                _checkedDescriptor?.RemoveValueChanged(_control, OnCheckedChanged);
            }

            _pressedDescriptor = null;
            _checkedDescriptor = null;
            _control = null;
            _surface = null;
            _attached = false;
        }

        private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs args) =>
            OnStateChanged(sender, EventArgs.Empty);

        private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs args) =>
            OnStateChanged(sender, EventArgs.Empty);

        private void OnCheckedChanged(object? sender, EventArgs args)
        {
            if (_control is null || _surface is null)
            {
                return;
            }

            // Selection changes the template's foreground immediately. Match
            // its surface in the same turn so white selected text never sits
            // on a fading pale fill, or dark unselected text on the old pink.
            _stateVersion++;
            ApplyState(
                CurrentBrushColor(_surface.Background),
                CurrentBrushColor(_surface.BorderBrush),
                CurrentScale(_surface),
                animateColors: false);
        }

        private void OnPaletteChanged(object? sender, EventArgs args)
        {
            _stateVersion++;
            if (_surface is not null)
                ApplyState(CurrentBrushColor(_surface.Background), CurrentBrushColor(_surface.BorderBrush),
                    CurrentScale(_surface), animateColors: false);
        }

        private void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!ApplicationTheme.HighContrastEnabled) return;
            // IsEnabledChanged is raised before template triggers finish updating
            // the control brushes. Read them at Render priority, then replace the
            // surface's local animation values without interpolating old colors.
            var version = ++_stateVersion;
            _host.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (version == _stateVersion) OnPaletteChanged(sender, EventArgs.Empty);
            }));
        }

        private void OnStateChanged(object? sender, EventArgs args)
        {
            if (_control is null || _surface is null)
            {
                return;
            }

            var version = ++_stateVersion;
            var currentBackground = CurrentBrushColor(_surface.Background);
            var currentBorder = CurrentBrushColor(_surface.BorderBrush);
            var currentScale = CurrentScale(_surface);
            _host.Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(() =>
                {
                    if (version == _stateVersion && _control is not null && _surface is not null)
                    {
                        ApplyState(currentBackground, currentBorder, currentScale);
                    }
                }));
        }

        private void ApplyState(Color? currentBackground, Color? currentBorder, double currentScale, bool animateColors = true)
        {
            if (_control is null || _surface is null)
            {
                return;
            }

            var targetBackground = ResolveTargetBrush(
                GetHoverBackgroundResourceKey(_host) ?? "CardAltBrush",
                GetPressedBackgroundResourceKey(_host),
                GetCheckedBackgroundResourceKey(_host),
                _control.Background);
            var targetBorder = ResolveTargetBrush(
                GetHoverBorderBrushResourceKey(_host) ?? "ActiveHoverBrush",
                null,
                GetCheckedBorderBrushResourceKey(_host),
                _control.BorderBrush);
            var isPressed = IsPressed;
            var colorDuration = animateColors
                ? ResolveMotionDuration(
                    isPressed ? "MotionPressDuration" : "MotionHoverDuration",
                    isPressed ? MotionTokens.PressDuration : MotionTokens.HoverDuration)
                : TimeSpan.Zero;
            var easing = ResolveMotionEasing();
            AnimateBrush(
                _surface,
                Border.BackgroundProperty,
                currentBackground,
                targetBackground,
                colorDuration,
                easing);
            AnimateBrush(
                _surface,
                Border.BorderBrushProperty,
                currentBorder,
                targetBorder,
                colorDuration,
                easing);

            var scale = EnsureScaleTransform(_surface);
            var targetScale = isPressed ? 0.97 : 1;
            AnimateScale(
                scale,
                currentScale,
                targetScale,
                ResolveMotionDuration("MotionPressDuration", MotionTokens.PressDuration),
                easing);
        }

        private TimeSpan ResolveMotionDuration(string key, TimeSpan fallback)
        {
            var configured = _control?.TryFindResource(key) is Duration duration && duration.HasTimeSpan
                ? duration.TimeSpan
                : fallback;
            return ApplicationTheme.GetEffectiveMotionDuration(configured);
        }

        private IEasingFunction ResolveMotionEasing() =>
            _control?.TryFindResource("MotionEaseOut") as IEasingFunction ??
            MotionTokens.CreateEaseOut();

        private Brush ResolveTargetBrush(
            string? hoverKey,
            string? pressedKey,
            string? checkedKey,
            Brush? normal)
        {
            var control = _control;
            if (control is null || (ApplicationTheme.HighContrastEnabled && !control.IsEnabled))
            {
                return normal ?? Brushes.Transparent;
            }

            var key = IsPressed &&
                      !string.IsNullOrWhiteSpace(pressedKey)
                ? pressedKey
                : control is ToggleButton { IsChecked: true } &&
                  !string.IsNullOrWhiteSpace(checkedKey)
                    ? checkedKey
                    : IsMouseOver && !string.IsNullOrWhiteSpace(hoverKey)
                        ? hoverKey
                        : null;
            return (key is not null ? control.TryFindResource(key) as Brush : normal) ??
                Brushes.Transparent;
        }

        private static void AnimateBrush(
            Border surface,
            DependencyProperty property,
            Color? current,
            Brush target,
            TimeSpan duration,
            IEasingFunction easing)
        {
            var targetColor = CurrentBrushColor(target) ?? Colors.Transparent;
            surface.BeginAnimation(property, null);
            surface.SetValue(property, target);
            if (duration <= TimeSpan.Zero || current is not { } from || from == targetColor)
            {
                return;
            }

            surface.BeginAnimation(
                property,
                new BrushColorAnimation
                {
                    From = from,
                    To = targetColor,
                    Duration = new Duration(duration),
                    EasingFunction = easing
                },
                HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimateScale(
            ScaleTransform scale,
            double current,
            double target,
            TimeSpan duration,
            IEasingFunction easing)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = target;
            scale.ScaleY = target;
            if (duration <= TimeSpan.Zero || Math.Abs(current - target) < 0.0001)
            {
                return;
            }

            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation
                {
                    From = current,
                    To = target,
                    Duration = new Duration(duration),
                    EasingFunction = easing
                },
                HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation
                {
                    From = current,
                    To = target,
                    Duration = new Duration(duration),
                    EasingFunction = easing
                },
                HandoffBehavior.SnapshotAndReplace);
        }

        private static ScaleTransform EnsureScaleTransform(Border surface)
        {
            if (surface.RenderTransform is ScaleTransform scale && !scale.IsFrozen)
            {
                return scale;
            }

            var clone = surface.RenderTransform is ScaleTransform existing
                ? existing.Clone()
                : new ScaleTransform(1, 1);
            surface.RenderTransform = clone;
            return clone;
        }

        private static Color? CurrentBrushColor(Brush? brush) =>
            brush is SolidColorBrush solid ? solid.Color : null;

        private static double CurrentScale(Border surface) =>
            surface.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1;
    }

    private sealed class BrushColorAnimation : AnimationTimeline
    {
        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(
                nameof(From),
                typeof(Color),
                typeof(BrushColorAnimation),
                new PropertyMetadata(Colors.Transparent));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(
                nameof(To),
                typeof(Color),
                typeof(BrushColorAnimation),
                new PropertyMetadata(Colors.Transparent));

        public static readonly DependencyProperty EasingFunctionProperty =
            DependencyProperty.Register(
                nameof(EasingFunction),
                typeof(IEasingFunction),
                typeof(BrushColorAnimation),
                new PropertyMetadata(null));

        public Color From
        {
            get => (Color)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public Color To
        {
            get => (Color)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public IEasingFunction? EasingFunction
        {
            get => (IEasingFunction?)GetValue(EasingFunctionProperty);
            set => SetValue(EasingFunctionProperty, value);
        }

        public override Type TargetPropertyType => typeof(Brush);

        protected override Freezable CreateInstanceCore() => new BrushColorAnimation();

        public override object GetCurrentValue(
            object defaultOriginValue,
            object defaultDestinationValue,
            AnimationClock animationClock)
        {
            var progress = animationClock.CurrentProgress ?? 1;
            var easedProgress = EasingFunction?.Ease(progress) ?? progress;
            static byte Lerp(byte from, byte to, double progress) =>
                (byte)Math.Round(from + ((to - from) * progress));

            return new SolidColorBrush(Color.FromArgb(
                Lerp(From.A, To.A, easedProgress),
                Lerp(From.R, To.R, easedProgress),
                Lerp(From.G, To.G, easedProgress),
                Lerp(From.B, To.B, easedProgress)));
        }
    }
}

/// <summary>
/// Animates popup surfaces without changing their placement or hit-test bounds.
/// A close request keeps the host open until the exit clock has completed, which
/// lets a rapid reopen reverse the same opacity/translation values in place.
/// </summary>
internal sealed class MotionPopup : Popup
{
    private static readonly CoerceValueCallback? BaseCoerceIsOpen =
        IsOpenProperty.GetMetadata(typeof(Popup)).CoerceValueCallback;

    static MotionPopup()
    {
        IsOpenProperty.OverrideMetadata(typeof(MotionPopup),
            new FrameworkPropertyMetadata(false, null, CoerceIsOpen));
    }

    private static object CoerceIsOpen(DependencyObject owner, object value)
    {
        // Preserve WPF's open-on-load gate before retaining an existing surface
        // for its exit. Hiding and reopening a DatePicker popup would itself
        // turn the owner's IsDropDownOpen request back on via Popup.Opened.
        var requestedOpen = (bool)(BaseCoerceIsOpen?.Invoke(owner, value) ?? value);
        return PopupMotionBehavior.CoerceIsOpen((Popup)owner, requestedOpen);
    }
}

internal static class PopupMotionBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(PopupMotionBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsToolTipEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsToolTipEnabled",
            typeof(bool),
            typeof(PopupMotionBehavior),
            new PropertyMetadata(false, OnIsToolTipEnabledChanged));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(PopupMotionController),
            typeof(PopupMotionBehavior),
            new PropertyMetadata(null));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsToolTipEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsToolTipEnabledProperty, value);

    public static bool GetIsToolTipEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsToolTipEnabledProperty);

    internal static void Attach(Popup popup) => SetIsEnabled(popup, true);

    internal static bool IsAttached(Popup? popup) =>
        popup is not null && popup.ReadLocalValue(ControllerProperty) is PopupMotionController;

    internal static bool CoerceIsOpen(Popup popup, bool requestedOpen) =>
        popup.ReadLocalValue(ControllerProperty) is PopupMotionController controller
            ? controller.CoerceIsOpen(requestedOpen)
            : requestedOpen;

    internal static void Open(Popup popup)
    {
        if (popup.ReadLocalValue(ControllerProperty) is PopupMotionController controller)
        {
            controller.RequestOpen();
        }
        else
        {
            SetPopupOpen(popup, true);
        }
    }

    internal static void Close(Popup popup)
    {
        if (popup.ReadLocalValue(ControllerProperty) is PopupMotionController controller)
        {
            controller.RequestClose();
        }
        else
        {
            SetPopupOpen(popup, false);
        }
    }

    internal static void Open(ToolTip toolTip)
    {
        if (toolTip.ReadLocalValue(ControllerProperty) is PopupMotionController controller)
        {
            controller.RequestOpen();
        }
        else
        {
            toolTip.SetCurrentValue(ToolTip.IsOpenProperty, true);
        }
    }

    internal static void Close(ToolTip toolTip)
    {
        if (toolTip.ReadLocalValue(ControllerProperty) is PopupMotionController controller)
        {
            controller.RequestClose();
        }
        else
        {
            toolTip.SetCurrentValue(ToolTip.IsOpenProperty, false);
        }
    }

    internal static void Toggle(Popup popup)
    {
        if (popup.ReadLocalValue(ControllerProperty) is PopupMotionController controller)
        {
            controller.RequestToggle();
            return;
        }

        SetPopupOpen(popup, !popup.IsOpen);
    }

    internal static bool IsClosing(Popup? popup) =>
        popup?.ReadLocalValue(ControllerProperty) is PopupMotionController controller &&
        controller.IsClosing;

    internal static string DescribeTransitionForValidation(Popup popup) =>
        popup.ReadLocalValue(ControllerProperty) is PopupMotionController controller
            ? controller.DescribeTransition()
            : "unattached";

    private static void OnIsEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Popup popup)
        {
            return;
        }

        if (args.NewValue is true)
        {
            if (popup.ReadLocalValue(ControllerProperty) is PopupMotionController)
            {
                return;
            }

            var controller = new PopupMotionController(popup);
            popup.SetValue(ControllerProperty, controller);
            controller.Attach();
        }
        else if (popup.ReadLocalValue(ControllerProperty) is PopupMotionController controller)
        {
            controller.Detach();
            popup.ClearValue(ControllerProperty);
        }
    }

    private static void OnIsToolTipEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not ToolTip toolTip)
        {
            return;
        }

        if (args.NewValue is true)
        {
            if (toolTip.ReadLocalValue(ControllerProperty) is PopupMotionController)
            {
                return;
            }

            var controller = new PopupMotionController(toolTip);
            toolTip.SetValue(ControllerProperty, controller);
            controller.Attach();
        }
        else if (toolTip.ReadLocalValue(ControllerProperty) is PopupMotionController controller)
        {
            controller.Detach();
            toolTip.ClearValue(ControllerProperty);
        }
    }

    private static void SetPopupOpen(Popup popup, bool value)
    {
        if (popup.IsOpen != value)
        {
            popup.SetCurrentValue(Popup.IsOpenProperty, value);
        }
    }

    private sealed class PopupMotionController : DependencyObject
    {
        private enum TransitionKind
        {
            None,
            Opening,
            Closing
        }

        private readonly DependencyObject _owner;
        private readonly bool _isToolTip;
        // Dependency-property bindings use weak invalidation listeners. A
        // PropertyDescriptor subscription instead roots transient popup owners.
        private static readonly DependencyProperty ObservedIsOpenProperty =
            DependencyProperty.Register("ObservedIsOpen", typeof(bool), typeof(PopupMotionController),
                new PropertyMetadata(false, (owner, _) =>
                    ((PopupMotionController)owner).OnIsOpenChanged(null, EventArgs.Empty)));
        private static readonly DependencyProperty ObservedPlacementTargetProperty =
            DependencyProperty.Register("ObservedPlacementTarget", typeof(UIElement), typeof(PopupMotionController),
                new PropertyMetadata(null, (owner, _) =>
                    ((PopupMotionController)owner).AttachPlacementTarget()));
        private static readonly DependencyProperty ObservedTargetIsOpenProperty =
            DependencyProperty.Register("ObservedTargetIsOpen", typeof(bool), typeof(PopupMotionController),
                new PropertyMetadata(false, (owner, _) =>
                    ((PopupMotionController)owner).OnPlacementTargetOpenChanged(null, EventArgs.Empty)));
        private DependencyObject? _placementTarget;
        private FrameworkElement? _visual;
        private TranslateTransform? _translation;
        private TransitionKind _transition;
        private long _transitionVersion;
        internal string DescribeTransition() =>
            $"version={_transitionVersion},kind={_transition},requested={_requestedOpen},opacity={_visual?.Opacity:R}";
        private bool _suppress;
        private bool _requestedOpen;
        private bool _hasPresented;
        private bool _attached;
        private bool _restoreDismissOnOutsideClick;

        public PopupMotionController(DependencyObject owner)
        {
            _owner = owner;
            _isToolTip = owner is ToolTip;
        }

        public bool IsClosing => _transition == TransitionKind.Closing;

        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            _attached = true;
            _requestedOpen = IsOpen;
            BindingOperations.SetBinding(this, ObservedIsOpenProperty,
                new Binding("IsOpen") { Source = _owner, Mode = BindingMode.OneWay });
            BindingOperations.SetBinding(this, ObservedPlacementTargetProperty,
                new Binding("PlacementTarget") { Source = _owner, Mode = BindingMode.OneWay });
            if (_owner is Popup popup)
            {
                popup.Opened += OnPopupOpened;
                popup.Closed += OnPopupClosed;
            }

            AttachPlacementTarget();
            if (_requestedOpen)
            {
                RequestOpen();
            }
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _transitionVersion++;
            if (_transition == TransitionKind.Closing && !_requestedOpen)
            {
                // Detaching cancels the queued callback, not the user's close
                // request. Settle it before releasing the visual/capture state.
                CompleteClose();
            }

            BindingOperations.ClearBinding(this, ObservedIsOpenProperty);
            BindingOperations.ClearBinding(this, ObservedPlacementTargetProperty);
            if (_owner is Popup popup)
            {
                popup.Opened -= OnPopupOpened;
                popup.Closed -= OnPopupClosed;
            }

            DetachPlacementTarget();
            ClearAnimations();
            _visual = null;
            _translation = null;
            if (_owner is MotionPopup popupWithRetainedExit)
            {
                popupWithRetainedExit.CoerceValue(Popup.IsOpenProperty);
            }

            RestoreCapturePolicy();
        }

        public bool CoerceIsOpen(bool requestedOpen)
        {
            if (_suppress || !_attached)
            {
                return requestedOpen;
            }

            if (requestedOpen)
            {
                if (IsOpen && _transition == TransitionKind.Closing)
                {
                    RequestOpen();
                }

                return true;
            }

            if (!IsOpen)
            {
                return false;
            }

            if (_transition == TransitionKind.Closing)
            {
                return true;
            }

            if (_requestedOpen &&
                ResolveDuration("MotionPopupExitDuration", MotionTokens.PopupExitDuration) > TimeSpan.Zero)
            {
                RequestClose();
                return true;
            }

            return false;
        }

        public void RequestOpen()
        {
            _requestedOpen = true;
            if (!IsOpen)
            {
                SetIsOpen(true);
            }

            StartTransition(TransitionKind.Opening);
            RestoreCapturePolicy();
        }

        public void RequestClose()
        {
            // Only a live/open request can own an exit transition. A binding
            // may already have closed that host; a never-opened or completed
            // host must stay closed when its trigger receives Escape.
            if (!IsOpen && !_requestedOpen)
            {
                return;
            }

            if (!_requestedOpen && _transition == TransitionKind.Closing)
            {
                return;
            }

            _requestedOpen = false;
            var duration = ResolveDuration("MotionPopupExitDuration", MotionTokens.PopupExitDuration);
            if (duration <= TimeSpan.Zero)
            {
                CompleteClose();
                SynchronizeTargetCloseRequest();
                return;
            }

            if (!IsOpen)
            {
                // The binding may already have pushed IsOpen=false. Reopen it for
                // this frame so the exit clock owns the visible surface.
                SetIsOpen(true);
            }

            StartTransition(TransitionKind.Closing);
            ReleaseCaptureForExit();
            // The exit already owns the visible surface before the native
            // picker callback can write Popup.IsOpen=false again. An outside
            // close must also make the next trigger click an open request.
            SynchronizeTargetCloseRequest();
        }

        private void ReleaseCaptureForExit()
        {
            if (_owner is Popup { StaysOpen: false } popup)
            {
                // Retaining IsOpen also retains WPF's PopupRoot capture.
                // Let WPF release it while the visible exit surface remains;
                // the next host click must be able to interrupt this exit.
                _restoreDismissOnOutsideClick = true;
                popup.SetCurrentValue(Popup.StaysOpenProperty, true);
            }
        }

        private void RestoreCapturePolicy()
        {
            if (_restoreDismissOnOutsideClick && _owner is Popup popup)
            {
                _restoreDismissOnOutsideClick = false;
                popup.SetCurrentValue(Popup.StaysOpenProperty, false);
            }
        }

        private void SynchronizeTargetCloseRequest()
        {
            if (_placementTarget is DatePicker { IsDropDownOpen: true } datePicker)
            {
                datePicker.SetCurrentValue(DatePicker.IsDropDownOpenProperty, false);
            }
            else if (_placementTarget is ComboBox { IsDropDownOpen: true } comboBox)
            {
                comboBox.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            }
        }

        public void RequestToggle()
        {
            if (_requestedOpen)
            {
                RequestClose();
            }
            else
            {
                RequestOpen();
            }
        }

        private bool IsOpen => _isToolTip
            ? ((ToolTip)_owner).IsOpen
            : ((Popup)_owner).IsOpen;

        private void SetIsOpen(bool value)
        {
            if (IsOpen == value)
            {
                return;
            }

            _suppress = true;
            try
            {
                if (_isToolTip)
                {
                    _owner.SetCurrentValue(ToolTip.IsOpenProperty, value);
                }
                else
                {
                    _owner.SetCurrentValue(Popup.IsOpenProperty, value);
                }
            }
            finally
            {
                _suppress = false;
            }
        }

        private void OnIsOpenChanged(object? sender, EventArgs args)
        {
            if (_suppress || !_attached)
            {
                return;
            }

            if (IsOpen)
            {
                AttachPlacementTarget();
                RequestOpen();
            }
            else
            {
                RequestClose();
            }
        }

        private void OnPopupOpened(object? sender, EventArgs args)
        {
            ResolveVisual();
            AttachPlacementTarget();
            if (_requestedOpen && _transition != TransitionKind.Closing)
            {
                StartTransition(TransitionKind.Opening);
            }
        }

        private void OnPopupClosed(object? sender, EventArgs args)
        {
            // Closing is driven from IsOpen above. This callback is deliberately
            // observational so an already-rendered child is never animated only
            // after Popup.Closed has torn down its native surface.
        }

        private void AttachPlacementTarget()
        {
            if (!_attached)
            {
                return;
            }
            DependencyObject? target = _isToolTip
                ? ((ToolTip)_owner).PlacementTarget
                : ((Popup)_owner).PlacementTarget;
            // Template popups can position relative to their template parent
            // without setting PlacementTarget. Observe that owner's request
            // so reopening can interrupt an exit while IsOpen is still true.
            target ??= (_owner as FrameworkElement)?.TemplatedParent;
            if (ReferenceEquals(target, _placementTarget))
            {
                return;
            }

            DetachPlacementTarget();
            _placementTarget = target;
            if (target is ComboBox or DatePicker)
            {
                BindingOperations.SetBinding(this, ObservedTargetIsOpenProperty,
                    new Binding("IsDropDownOpen") { Source = target, Mode = BindingMode.OneWay });
            }
        }

        private void DetachPlacementTarget()
        {
            _placementTarget = null;
            BindingOperations.ClearBinding(this, ObservedTargetIsOpenProperty);
        }

        private void OnPlacementTargetOpenChanged(object? sender, EventArgs args)
        {
            if (_suppress || !_attached)
            {
                return;
            }

            if (_placementTarget is ComboBox comboBox)
            {
                if (comboBox.IsDropDownOpen)
                {
                    RequestOpen();
                }
                else if (!_suppress && _requestedOpen)
                {
                    RequestClose();
                }
            }
            else if (_placementTarget is DatePicker datePicker)
            {
                if (datePicker.IsDropDownOpen)
                {
                    RequestOpen();
                }
                else if (!_suppress && _requestedOpen)
                {
                    RequestClose();
                }
            }
        }

        private void StartTransition(TransitionKind kind)
        {
            ResolveVisual();
            if (_visual is null)
            {
                // Template popups can materialize their child after Opened.
                // The event above retries; a short dispatcher retry covers a
                // binding-created child without changing popup placement.
                var pendingVersion = ++_transitionVersion;
                _transition = kind;
                _owner.Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() =>
                    {
                        if (pendingVersion == _transitionVersion)
                        {
                            StartTransition(kind);
                        }
                    }));
                return;
            }

            var version = ++_transitionVersion;
            _transition = kind;
            var translation = EnsureTranslation(_visual);
            var fromOpacity = _visual.Opacity;
            var fromY = translation.Y;
            ClearAnimations();

            if (kind == TransitionKind.Opening &&
                !_hasPresented &&
                fromOpacity >= 0.999 &&
                Math.Abs(fromY) < 0.001)
            {
                fromOpacity = 0;
                fromY = 4;
            }

            var targetOpacity = kind == TransitionKind.Opening ? 1 : 0;
            var targetY = kind == TransitionKind.Opening ? 0 : 4;
            var duration = ResolveDuration(
                kind == TransitionKind.Opening
                    ? "MotionPopupEnterDuration"
                    : "MotionPopupExitDuration",
                kind == TransitionKind.Opening
                    ? MotionTokens.PopupEnterDuration
                    : MotionTokens.PopupExitDuration);
            var easing = ResolveEasing();
            SetVisualValues(fromOpacity, fromY);
            if (duration <= TimeSpan.Zero)
            {
                CompleteTransition(version, kind, targetOpacity, targetY);
                return;
            }

            var opacityAnimation = new DoubleAnimation
            {
                From = fromOpacity,
                To = targetOpacity,
                Duration = new Duration(duration),
                EasingFunction = easing
            };
            opacityAnimation.Completed += (_, _) =>
            {
                if (version == _transitionVersion)
                {
                    CompleteTransition(version, kind, targetOpacity, targetY, deferClose: true);
                }
            };
            _visual.BeginAnimation(
                UIElement.OpacityProperty,
                opacityAnimation,
                HandoffBehavior.SnapshotAndReplace);
            translation.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation
                {
                    From = fromY,
                    To = targetY,
                    Duration = new Duration(duration),
                    EasingFunction = easing
                },
                HandoffBehavior.SnapshotAndReplace);
        }

        private void CompleteTransition(
            long version,
            TransitionKind kind,
            double targetOpacity,
            double targetY,
            bool deferClose = false)
        {
            if (version != _transitionVersion || _visual is null)
            {
                return;
            }

            SetVisualValues(targetOpacity, targetY);
            if (kind == TransitionKind.Opening)
            {
                _transition = TransitionKind.None;
                _hasPresented = true;
                return;
            }

            if (!deferClose)
            {
                CompleteClose();
                return;
            }

            // Popup's native hide synchronizes with the compositor. Let the
            // current animation/render tick submit its work before hiding the
            // HWND. Keep Closing until then so a new open request can cancel it.
            _owner.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (_attached && version == _transitionVersion &&
                    !_requestedOpen && _transition == TransitionKind.Closing)
                {
                    CompleteClose();
                }
            }));
        }

        private void CompleteClose()
        {
            _transitionVersion++;
            _transition = TransitionKind.None;
            _hasPresented = false;
            if (_visual is not null)
            {
                SetVisualValues(0, 4);
            }

            if (IsOpen)
            {
                SetIsOpen(false);
            }

            RestoreCapturePolicy();
        }

        private void ResolveVisual()
        {
            if (_isToolTip)
            {
                _visual = (ToolTip)_owner;
                _translation = null;
                return;
            }

            _visual = ((Popup)_owner).Child as FrameworkElement;
            _translation = null;
        }

        private TranslateTransform EnsureTranslation(FrameworkElement visual)
        {
            if (_translation is not null &&
                ReferenceEquals(_translation, FindTranslation(visual)))
            {
                return _translation;
            }

            var existing = FindTranslation(visual);
            if (existing is not null && !existing.IsFrozen)
            {
                _translation = existing;
                return existing;
            }

            var translation = new TranslateTransform();
            if (visual.RenderTransform is null || visual.RenderTransform == Transform.Identity)
            {
                visual.RenderTransform = translation;
            }
            else
            {
                var group = visual.RenderTransform is TransformGroup existingGroup
                    ? existingGroup.Clone()
                    : new TransformGroup
                    {
                        Children = { visual.RenderTransform.Clone() }
                    };
                group.Children.Add(translation);
                visual.RenderTransform = group;
            }

            _translation = translation;
            return translation;
        }

        private static TranslateTransform? FindTranslation(FrameworkElement visual)
        {
            if (visual.RenderTransform is TranslateTransform translation)
            {
                return translation;
            }

            if (visual.RenderTransform is TransformGroup group)
            {
                return group.Children.OfType<TranslateTransform>().LastOrDefault();
            }

            return null;
        }

        private void SetVisualValues(double opacity, double y)
        {
            if (_visual is null)
            {
                return;
            }

            var translation = EnsureTranslation(_visual);
            ClearAnimations();
            _visual.Opacity = opacity;
            translation.Y = y;
        }

        private void ClearAnimations()
        {
            _visual?.BeginAnimation(UIElement.OpacityProperty, null);
            _translation?.BeginAnimation(TranslateTransform.YProperty, null);
        }

        private TimeSpan ResolveDuration(string key, TimeSpan fallback)
        {
            var source = _visual ?? (FrameworkElement)_owner;
            var configured = source.TryFindResource(key) is Duration duration &&
                duration.HasTimeSpan
                ? duration.TimeSpan
                : fallback;
            return ApplicationTheme.GetEffectiveMotionDuration(configured);
        }

        private IEasingFunction ResolveEasing()
        {
            var source = _visual ?? (FrameworkElement)_owner;
            return source.TryFindResource("MotionEaseOut") as IEasingFunction ??
                MotionTokens.CreateEaseOut();
        }
    }
}
