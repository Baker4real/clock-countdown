using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MinminCountdown;

/// <summary>
/// Moves one visual sliding pill between sibling segments without changing their layout.
/// Translate X/Y and Border Width share the same animation so both ends keep their
/// semicircular radius.
/// </summary>
internal sealed class SegmentIndicator
{
    private const double GeometryTolerance = 0.75;

    private readonly FrameworkElement _coordinateRoot;
    private readonly Canvas _layer;
    private readonly Border _indicator;
    private readonly TranslateTransform _translate;
    private long _requestVersion;
    private FrameworkElement? _target;
    private Rect _lastTargetBounds;
    private bool _hasPresentedPill;
    private bool _animatedMovePending;
    private FrameworkElement? _animatedTarget;
    private Rect? _animatedTargetBounds;

    internal SegmentIndicator(
        FrameworkElement coordinateRoot,
        Canvas layer,
        Border indicator)
    {
        _coordinateRoot = coordinateRoot;
        _layer = layer;
        _indicator = indicator;
        _translate = new TranslateTransform();
        _indicator.RenderTransform = _translate;
        _indicator.RenderTransformOrigin = new Point(0, 0.5);
        _indicator.Visibility = Visibility.Collapsed;
    }

    internal Rect LastTargetBounds => _lastTargetBounds;

    internal Rect CurrentVisualBounds =>
        new(
            _translate.X,
            _translate.Y,
            Math.Abs(_indicator.Width),
            Math.Abs(_indicator.Height));

    internal FrameworkElement? CurrentTarget => _target;

    internal bool IsAlignedTo(FrameworkElement target, double tolerance = GeometryTolerance)
    {
        return TryGetTargetBounds(target, out var targetBounds) &&
            GeometryMatches(CurrentVisualBounds, IndicatorBounds(target, targetBounds), tolerance);
    }

    internal static bool GeometryMatches(Rect actual, Rect expected, double tolerance = GeometryTolerance) =>
        Math.Abs(actual.Left - expected.Left) <= tolerance &&
        Math.Abs(actual.Top - expected.Top) <= tolerance &&
        Math.Abs(actual.Width - expected.Width) <= tolerance &&
        Math.Abs(actual.Height - expected.Height) <= tolerance;

    internal static TimeSpan EffectiveDuration(TimeSpan configured, bool clientAreaAnimationsEnabled) =>
        clientAreaAnimationsEnabled ? configured : TimeSpan.Zero;

    internal void MoveTo(FrameworkElement target) => MoveTo(target, immediate: false);

    internal void MoveTo(FrameworkElement target, bool immediate)
    {
        _target = target;
        var version = ++_requestVersion;
        var immediateGeometry = immediate || !_hasPresentedPill;
        _animatedMovePending = !immediateGeometry;
        if (immediateGeometry)
        {
            _animatedTarget = null;
            _animatedTargetBounds = null;
            // 键盘路径、计数几何变化与第一次可用几何都必须在下一真实帧前直接写入终值。
            // 先把本轮选中变化排版完；下面的延迟复核仍会再对齐一次。
            _coordinateRoot.UpdateLayout();
            if (TryGetTargetBounds(target, out var bounds))
            {
                _lastTargetBounds = bounds;
                ApplyTarget(IndicatorBounds(target, bounds), immediate: true);
            }
        }

        // Selection can change the segment's text weight and badge width in this same turn.
        // If an immediate request cannot measure during IsVisibleChanged, its deferred retry
        // must run before Rendering; ordinary pointer transitions retain the Loaded commit.
        _coordinateRoot.Dispatcher.BeginInvoke(
            immediateGeometry ? DispatcherPriority.Send : DispatcherPriority.Loaded,
            new Action(() => ApplyWhenReady(version, immediateGeometry)));
    }

    internal void SynchronizeWithCurrentTarget()
    {
        if (_target is null || _animatedMovePending)
        {
            // A selection transition owns this layout pass. SizeChanged must not snap its
            // newly weighted segments to the final pill geometry before the 160ms commit.
            return;
        }

        if (ReferenceEquals(_animatedTarget, _target) &&
            _animatedTargetBounds is { } animatedBounds &&
            TryGetTargetBounds(_target, out var currentBounds) &&
            GeometryMatches(currentBounds, animatedBounds, tolerance: 0.01))
        {
            // This SizeChanged belongs to the selection's own font/rail layout. Its
            // geometry already matches the animated destination; do not cancel the pill.
            return;
        }

        var target = _target;
        var version = _requestVersion;
        _coordinateRoot.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                if (version == _requestVersion &&
                    ReferenceEquals(target, _target) &&
                    !_animatedMovePending)
                {
                    MoveTo(target, immediate: true);
                }
            }));
    }

    private void ApplyWhenReady(long version, bool immediate)
    {
        if (version != _requestVersion || _target is null)
        {
            return;
        }

        if (!TryGetTargetBounds(_target, out _))
        {
            // A hidden workspace has no usable target geometry. The owner calls MoveTo
            // again when that workspace becomes visible; never enqueue a self-repeating
            // deferred callback while the target remains collapsed.
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
                            _animatedTarget = immediate ? null : _target;
                            _animatedTargetBounds = immediate ? null : bounds;
                            ApplyTarget(IndicatorBounds(_target, bounds), immediate);
                            _animatedMovePending = false;
                        }
                    }));
            }

            return;
        }

        _lastTargetBounds = GetTargetBounds(_target);
        _animatedTarget = immediate ? null : _target;
        _animatedTargetBounds = immediate ? null : _lastTargetBounds;
        ApplyTarget(IndicatorBounds(_target, _lastTargetBounds), immediate);
        _animatedMovePending = false;
    }

    private void ApplyTarget(Rect targetBounds, bool immediate)
    {
        _layer.IsHitTestVisible = false;
        _indicator.IsHitTestVisible = false;
        _indicator.Focusable = false;
        var firstPillPresentation = !_hasPresentedPill;
        _indicator.Visibility = Visibility.Visible;

        var configuredDuration = _coordinateRoot.TryFindResource("MotionIndicatorDuration") is Duration duration &&
            duration.HasTimeSpan
                ? duration.TimeSpan
                : MotionTokens.IndicatorDuration;
        var effectiveDuration = immediate || firstPillPresentation
            ? TimeSpan.Zero
            : EffectiveDuration(configuredDuration, ApplicationTheme.ClientAreaAnimationEnabled);
        var easing = _coordinateRoot.TryFindResource("MotionEaseOut") as IEasingFunction ??
            MotionTokens.CreateEaseOut();

        var currentWidth = _indicator.Width;
        var currentX = _translate.X;
        var currentY = _translate.Y;
        BeginBaseValue(_indicator, FrameworkElement.WidthProperty, targetBounds.Width);
        BeginBaseValue(_translate, TranslateTransform.XProperty, targetBounds.Left);
        BeginBaseValue(_translate, TranslateTransform.YProperty, targetBounds.Top);
        _hasPresentedPill = true;

        if (effectiveDuration <= TimeSpan.Zero)
        {
            return;
        }

        Animate(
            _indicator,
            FrameworkElement.WidthProperty,
            currentWidth,
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
        DependencyObject target,
        DependencyProperty property,
        double value)
    {
        switch (target)
        {
            case UIElement element:
                element.BeginAnimation(property, null);
                break;
            case Animatable animatable:
                animatable.BeginAnimation(property, null);
                break;
            default:
                throw new ArgumentException("Indicator animation target must support BeginAnimation.", nameof(target));
        }
        target.SetValue(property, value);
    }

    private static void Animate(
        DependencyObject target,
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

        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(duration),
            EasingFunction = easing
        };
        switch (target)
        {
            case UIElement element:
                element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
            case Animatable animatable:
                animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
            default:
                throw new ArgumentException("Indicator animation target must support BeginAnimation.", nameof(target));
        }
    }

    private bool TryGetTargetBounds(FrameworkElement target, out Rect bounds)
    {
        bounds = default;
        if (!target.IsLoaded ||
            !_coordinateRoot.IsLoaded ||
            !_coordinateRoot.IsVisible ||
            target.ActualWidth <= 0 ||
            target.ActualHeight <= 0 ||
            !target.IsVisible ||
            PillSurface(target) is null)
        {
            if (!_coordinateRoot.IsVisible || !target.IsVisible)
            {
                // A hidden window/workspace reveals directly at its current target instead of
                // resuming an invisible transition or entering from the previous geometry.
                _hasPresentedPill = false;
            }
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

    private static Rect IndicatorBounds(FrameworkElement target, Rect targetBounds)
    {
        // Use the Surface layout rectangle, not TransformToAncestor on Surface itself:
        // its 0.97 press RenderTransform must never move or resize the underlying pill.
        var margin = PillSurface(target)?.Margin ?? new Thickness(0);
        return new Rect(
            targetBounds.Left + margin.Left,
            targetBounds.Top + margin.Top,
            Math.Max(0, targetBounds.Width - margin.Left - margin.Right),
            Math.Max(0, targetBounds.Height - margin.Top - margin.Bottom));
    }

    private static Border? PillSurface(FrameworkElement target) =>
        target is Control control ? control.Template?.FindName("Surface", control) as Border : null;
}
