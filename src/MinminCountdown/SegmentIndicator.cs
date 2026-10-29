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

    // 指示条顶边与选中控件外缘（常驻焦点环）之间的空隙，选中与焦点不叠在同一条槽里。
    private const double FocusRingClearance = 1;

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
        if (immediate)
        {
            // #242：键盘路径的下一帧就必须停在目标上。先把本轮选中变化排版完，
            // 再按真实几何直接写入终值；下面的 Loaded 复核仍会再对齐一次。
            _coordinateRoot.UpdateLayout();
            if (TryGetTargetBounds(target, out var bounds))
            {
                _lastTargetBounds = bounds;
                ApplyTarget(IndicatorBounds(target, bounds), immediate: true);
            }
        }

        // Selection can change the segment's text weight and badge width in
        // this same turn. Measure only after WPF has arranged those changes.
        _coordinateRoot.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => ApplyWhenReady(version, immediate)));
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
                            ApplyTarget(IndicatorBounds(_target, bounds), immediate);
                        }
                    }));
            }

            return;
        }

        _lastTargetBounds = GetTargetBounds(_target);
        ApplyTarget(IndicatorBounds(_target, _lastTargetBounds), immediate);
    }

    private void ApplyTarget(Rect targetBounds, bool immediate)
    {
        _layer.IsHitTestVisible = false;
        _indicator.Visibility = Visibility.Visible;

        var configuredDuration = _coordinateRoot.TryFindResource("MotionIndicatorDuration") is Duration duration &&
            duration.HasTimeSpan
                ? duration.TimeSpan
                : MotionTokens.IndicatorDuration;
        var effectiveDuration = immediate
            ? TimeSpan.Zero
            : EffectiveDuration(configuredDuration, ApplicationTheme.ClientAreaAnimationEnabled);
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

    // 可见宽度只取选中胶囊（模板 Surface）的平直底边：Surface 外边距加底部圆角之内，按布局值
    // 计算，不受按压缩放影响；整条落在控件外缘之下 FocusRingClearance 处。
    private Rect IndicatorBounds(FrameworkElement target, Rect targetBounds)
    {
        var height = _indicator.Height > 0 ? _indicator.Height : 4;
        var (leftInset, rightInset) = PillSurface(target) is { } surface
            ? (surface.Margin.Left + surface.CornerRadius.BottomLeft,
                surface.Margin.Right + surface.CornerRadius.BottomRight)
            : (0d, 0d);
        return new Rect(
            targetBounds.Left + leftInset,
            targetBounds.Bottom + FocusRingClearance,
            Math.Max(0, targetBounds.Width - leftInset - rightInset),
            height);
    }

    private static Border? PillSurface(FrameworkElement target) =>
        target is Control control ? control.Template?.FindName("Surface", control) as Border : null;
}
