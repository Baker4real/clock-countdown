using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        public MotionController(FrameworkElement host)
        {
            _host = host;
        }

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
            if (control is ButtonBase)
            {
                _pressedDescriptor?.AddValueChanged(control, OnStateChanged);
            }

            if (control is ToggleButton)
            {
                _checkedDescriptor?.AddValueChanged(control, OnStateChanged);
            }

            _attached = true;
            ApplyState(
                CurrentBrushColor(surface.Background),
                CurrentBrushColor(surface.BorderBrush),
                CurrentScale(surface));
        }

        private void DetachFromControl()
        {
            _stateVersion++;
            if (_control is not null)
            {
                _control.MouseEnter -= OnMouseEnter;
                _control.MouseLeave -= OnMouseLeave;
                _pressedDescriptor?.RemoveValueChanged(_control, OnStateChanged);
                _checkedDescriptor?.RemoveValueChanged(_control, OnStateChanged);
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

        private void ApplyState(Color? currentBackground, Color? currentBorder, double currentScale)
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
            var isPressed = _control is ButtonBase { IsPressed: true };
            var colorDuration = ResolveMotionDuration(
                isPressed ? "MotionPressDuration" : "MotionHoverDuration",
                isPressed ? MotionTokens.PressDuration : MotionTokens.HoverDuration);
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
            if (control is null)
            {
                return normal ?? Brushes.Transparent;
            }

            var key = control is ButtonBase { IsPressed: true } &&
                      !string.IsNullOrWhiteSpace(pressedKey)
                ? pressedKey
                : control is ToggleButton { IsChecked: true } &&
                  !string.IsNullOrWhiteSpace(checkedKey)
                    ? checkedKey
                    : control.IsMouseOver && !string.IsNullOrWhiteSpace(hoverKey)
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
