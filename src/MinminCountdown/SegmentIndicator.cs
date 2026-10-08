using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MinminCountdown;

/// <summary>
/// Moves one visual indicator between sibling segments without changing their layout.
/// The indicator has a one-DIP layout width; its visible width and position are carried
/// by a ScaleTransform and a TranslateTransform so rapid changes can continue from the
/// currently rendered geometry.
/// </summary>
internal sealed class SegmentIndicator
{
    private const double GeometryTolerance = 0.75;

    private readonly FrameworkElement _coordinateRoot;
    private readonly Canvas _layer;
    private readonly Border _indicator;
    private readonly ScaleTransform _scale;
    private readonly TranslateTransform _translate;
    private long _requestVersion;
    private FrameworkElement? _target;
    private Rect _lastTargetBounds;

    internal SegmentIndicator(
        FrameworkElement coordinateRoot,
        Canvas layer,
        Border indicator)
    {
        _coordinateRoot = coordinateRoot;
        _layer = layer;
        _indicator = indicator;
        _scale = new ScaleTransform(1, 1);
        _translate = new TranslateTransform();
        _indicator.RenderTransform = new TransformGroup
        {
            Children =
            {
                _scale,
                _translate
            }
        };
        _indicator.RenderTransformOrigin = new Point(0, 0.5);
        _indicator.Visibility = Visibility.Collapsed;
    }

    internal Rect LastTargetBounds => _lastTargetBounds;

    internal Rect CurrentVisualBounds =>
        new(
            _translate.X,
            _translate.Y,
            Math.Abs(_indicator.Width * _scale.ScaleX),
            Math.Abs(_indicator.Height * _scale.ScaleY));

    internal FrameworkElement? CurrentTarget => _target;

    internal bool IsAlignedTo(FrameworkElement target, double tolerance = GeometryTolerance)
    {
        return TryGetTargetBounds(target, out var targetBounds) &&
            GeometryMatches(CurrentVisualBounds, IndicatorBounds(targetBounds), tolerance);
    }

    internal static bool GeometryMatches(Rect actual, Rect expected, double tolerance = GeometryTolerance) =>
        Math.Abs(actual.Left - expected.Left) <= tolerance &&
        Math.Abs(actual.Top - expected.Top) <= tolerance &&
        Math.Abs(actual.Width - expected.Width) <= tolerance &&
        Math.Abs(actual.Height - expected.Height) <= tolerance;

    internal static TimeSpan EffectiveDuration(TimeSpan configured, bool clientAreaAnimationsEnabled) =>
        clientAreaAnimationsEnabled ? configured : TimeSpan.Zero;

    internal void MoveTo(FrameworkElement target)
    {
        _target = target;
        var version = ++_requestVersion;
        // Selection can change the segment's text weight and badge width in
        // this same turn. Measure only after WPF has arranged those changes.
        _coordinateRoot.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => ApplyWhenReady(version)));
    }

    private void ApplyWhenReady(long version)
    {
        if (version != _requestVersion || _target is null)
        {
            return;
        }

        if (!TryGetTargetBounds(_target, out _))
        {
            // A hidden workspace has no usable target geometry. The owner calls MoveTo
            // again when that workspace becomes visible; never enqueue a self-repeating
            // Loaded callback while the target remains collapsed.
            if (_coordinateRoot.IsVisible && _target.IsVisible)
            {
                _coordinateRoot.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() =>
                    {
                        if (version == _requestVersion &&
                            _target is not null &&
                            TryGetTargetBounds(_target, out var bounds))
                        {
                            _lastTargetBounds = bounds;
                            ApplyTarget(IndicatorBounds(bounds));
                        }
                    }));
            }

            return;
        }

        _lastTargetBounds = GetTargetBounds(_target);
        ApplyTarget(IndicatorBounds(_lastTargetBounds));
    }

    private void ApplyTarget(Rect targetBounds)
    {
        _layer.IsHitTestVisible = false;
        _indicator.Visibility = Visibility.Visible;

        var configuredDuration = _coordinateRoot.TryFindResource("MotionIndicatorDuration") is Duration duration &&
            duration.HasTimeSpan
                ? duration.TimeSpan
                : MotionTokens.IndicatorDuration;
        var effectiveDuration = EffectiveDuration(configuredDuration, ApplicationTheme.ClientAreaAnimationEnabled);
        var easing = _coordinateRoot.TryFindResource("MotionEaseOut") as IEasingFunction ??
            MotionTokens.CreateEaseOut();

        var currentScale = _scale.ScaleX;
        var currentX = _translate.X;
        var currentY = _translate.Y;
        BeginBaseValue(_scale, ScaleTransform.ScaleXProperty, targetBounds.Width);
        BeginBaseValue(_translate, TranslateTransform.XProperty, targetBounds.Left);
        BeginBaseValue(_translate, TranslateTransform.YProperty, targetBounds.Top);

        if (effectiveDuration <= TimeSpan.Zero)
        {
            return;
        }

        Animate(
            _scale,
            ScaleTransform.ScaleXProperty,
            currentScale,
            targetBounds.Width,
            effectiveDuration,
            easing);
        Animate(
            _translate,
            TranslateTransform.XProperty,
            currentX,
            targetBounds.Left,
            effectiveDuration,
            easing);
        Animate(
            _translate,
            TranslateTransform.YProperty,
            currentY,
            targetBounds.Top,
            effectiveDuration,
            easing);
    }

    private static void BeginBaseValue(
        Animatable target,
        DependencyProperty property,
        double value)
    {
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
    }

    private static void Animate(
        Animatable target,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration,
        IEasingFunction easing)
    {
        if (Math.Abs(from - to) <= 0.0001)
        {
            return;
        }

        target.BeginAnimation(
            property,
            new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(duration),
                EasingFunction = easing
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private bool TryGetTargetBounds(FrameworkElement target, out Rect bounds)
    {
        bounds = default;
        if (!target.IsLoaded ||
            !_coordinateRoot.IsLoaded ||
            !_coordinateRoot.IsVisible ||
            target.ActualWidth <= 0 ||
            target.ActualHeight <= 0 ||
            !target.IsVisible)
        {
            return false;
        }

        bounds = GetTargetBounds(target);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private Rect GetTargetBounds(FrameworkElement target)
    {
        var transform = target.TransformToAncestor(_coordinateRoot);
        return transform.TransformBounds(new Rect(new Point(0, 0), target.RenderSize));
    }

    private Rect IndicatorBounds(Rect targetBounds)
    {
        var height = _indicator.Height > 0 ? _indicator.Height : 4;
        return new Rect(
            targetBounds.Left,
            targetBounds.Bottom - height,
            targetBounds.Width,
            height);
    }
}
