using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MinminCountdown;

// #233/#243：便签完成过渡的唯一负责者。领域事务提交前截取完成卡片本身的快照与位置，并按稳定身份
// 记录列表可见条目相对视口的纵向位置；列表在同一调用里重建后先完成布局，再在首帧渲染前让保留条目
// 带上「旧位置减新位置」的纵向位移。快照淡出与位移归零是同一个 Storyboard 的子时间线。
// 快照不参与布局、命中或焦点；任何打断都让快照和位移立即落到终态。
internal sealed class NoteCompletionExit : Adorner, IDisposable
{
    private static readonly DependencyProperty CompletionIdentityProperty = DependencyProperty.RegisterAttached(
        "CompletionIdentity", typeof(string), typeof(NoteCompletionExit));

    private readonly FrameworkElement _viewport;
    private readonly AdornerLayer _layer;
    private readonly Window? _window;
    private readonly Rect _snapshotBounds;
    private readonly Dictionary<string, double> _previousTops;
    private readonly List<Displacement> _displacements = new();
    private BitmapSource? _snapshot;
    private Storyboard? _storyboard;
    private bool _started;
    private bool _disposed;

    internal int SnapshotPixelWidthForValidation => _snapshot?.PixelWidth ?? 0;
    internal int SnapshotPixelHeightForValidation => _snapshot?.PixelHeight ?? 0;
    internal int CapturedPositionCountForValidation => _previousTops.Count;
    internal int ActiveFlipCountForValidation => _displacements.Count;

    private NoteCompletionExit(
        FrameworkElement viewport,
        AdornerLayer layer,
        BitmapSource snapshot,
        Rect snapshotBounds,
        Dictionary<string, double> previousTops) : base(viewport)
    {
        _viewport = viewport;
        _layer = layer;
        _window = Window.GetWindow(viewport);
        _snapshot = snapshot;
        _snapshotBounds = snapshotBounds;
        _previousTops = previousTops;
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        AutomationProperties.SetAutomationId(this, "NoteCompletionExit");
    }

    internal static string NoteIdentity(string noteId) => $"note:{noteId}";
    internal static string DateIdentity(string? date) => $"date:{date ?? "<undated>"}";

    internal static void SetCompletionIdentity(FrameworkElement element, string identity) =>
        element.SetValue(CompletionIdentityProperty, identity);

    public static NoteCompletionExit? Capture(FrameworkElement? card, FrameworkElement viewport)
    {
        if (!ApplicationTheme.ClientAreaAnimationEnabled || card is null || !card.IsVisible ||
            card.RenderSize.Width <= 0 || card.RenderSize.Height <= 0 ||
            !viewport.IsVisible || viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0 ||
            AdornerLayer.GetAdornerLayer(viewport) is not { } layer)
            return null;

        var previousTops = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ReadListItems(viewport))
        {
            previousTops.TryAdd(item.Identity, item.Bounds.Top);
        }

        var size = card.RenderSize;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(card) { Stretch = Stretch.Fill }, null, new Rect(size));
        }
        var dpi = VisualTreeHelper.GetDpi(viewport);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return new NoteCompletionExit(
            viewport,
            layer,
            bitmap,
            card.TransformToAncestor(viewport).TransformBounds(new Rect(size)),
            previousTops);
    }

    public void Start()
    {
        if (_disposed || !ApplicationTheme.ClientAreaAnimationEnabled || !_viewport.IsVisible)
        {
            Dispose();
            return;
        }

        _layer.Add(this);
        _started = true;
        _viewport.IsVisibleChanged += OnVisibilityChanged;
        _viewport.Unloaded += OnUnloaded;
        ApplicationTheme.PaletteChanged += OnPreferenceChanged;
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;

        // 领域事务已在同一调用里同步重建列表：先完成布局，再在首帧渲染前施加反向位移，
        // 避免先渲染一帧「新布局压在快照下」再跳回旧位置。
        _viewport.UpdateLayout();
        if (_disposed) return;
        ApplyDisplacements();

        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateAnimation(this, new PropertyPath(OpacityProperty), 1));
        foreach (var displacement in _displacements)
        {
            // 时钟会冻结复制时间线，以 Freezable 为 Storyboard.Target 时动画的是变换副本；
            // 以元素为目标、经 RenderTransform 路径才落到卡片当前的这个 TranslateTransform 上。
            storyboard.Children.Add(CreateAnimation(
                displacement.Element,
                new PropertyPath("(0).(1)", RenderTransformProperty, TranslateTransform.YProperty),
                displacement.Transform.Y));
        }
        storyboard.Completed += (_, _) => Dispose();
        _storyboard = storyboard;
        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    // 前后都在列表中、且旧位置或新位置与视口相交的条目从旧位置补位；没有旧位置的新条目、
    // 旧新位置都在视口外的条目和零位移条目不动。祖先已在补位的条目随祖先移动，不叠加位移。
    private void ApplyDisplacements()
    {
        var viewportHeight = _viewport.RenderSize.Height;
        var moving = new HashSet<FrameworkElement>();
        foreach (var item in ReadListItems(_viewport).OrderBy(static item => item.Depth))
        {
            if (!_previousTops.TryGetValue(item.Identity, out var previousTop)) continue;
            var offset = previousTop - item.Bounds.Top;
            if (Math.Abs(offset) < 0.01 ||
                Math.Max(previousTop, item.Bounds.Top) + item.Bounds.Height <= 0 ||
                Math.Min(previousTop, item.Bounds.Top) >= viewportHeight ||
                HasMovingAncestor(item.Element, moving))
            {
                continue;
            }

            var transform = new TranslateTransform(0, offset);
            _displacements.Add(new Displacement(
                item.Element,
                transform,
                item.Element.ReadLocalValue(RenderTransformProperty)));
            item.Element.RenderTransform = transform;
            moving.Add(item.Element);
        }
    }

    private static DoubleAnimation CreateAnimation(DependencyObject target, PropertyPath path, double from)
    {
        var animation = new DoubleAnimation(from, 0, MotionTokens.NoteCompletionExitDuration)
        {
            EasingFunction = MotionTokens.CreateEaseOut(),
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, path);
        return animation;
    }

    private bool HasMovingAncestor(DependencyObject element, HashSet<FrameworkElement> moving)
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(element);
             current is not null && !ReferenceEquals(current, _viewport);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement ancestor && moving.Contains(ancestor)) return true;
        }
        return false;
    }

    // 带稳定身份、当前可见（不在折叠分组或隐藏分段里）的列表条目及其相对视口的边界。
    private static List<ListItem> ReadListItems(FrameworkElement viewport)
    {
        var items = new List<ListItem>();
        Collect(viewport, 0);
        return items;

        void Collect(DependencyObject parent, int depth)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is UIElement { IsVisible: false }) continue;
                if (child is FrameworkElement element &&
                    element.GetValue(CompletionIdentityProperty) is string identity)
                {
                    items.Add(new ListItem(
                        element,
                        identity,
                        element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize)),
                        depth));
                }
                Collect(child, depth + 1);
            }
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_snapshot is not null) drawingContext.DrawImage(_snapshot, _snapshotBounds);
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!_viewport.IsVisible) Dispose();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();

    private void OnWindowStateChanged(object? sender, EventArgs args)
    {
        if (_window?.WindowState == WindowState.Minimized) Dispose();
    }

    private void OnPreferenceChanged(object? sender, EventArgs args)
    {
        if (!ApplicationTheme.ClientAreaAnimationEnabled) Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _storyboard?.Remove(this);
        _storyboard = null;
        BeginAnimation(OpacityProperty, null);
        foreach (var displacement in _displacements)
        {
            displacement.Transform.BeginAnimation(TranslateTransform.YProperty, null);
            displacement.Transform.Y = 0;
            if (!ReferenceEquals(displacement.Element.RenderTransform, displacement.Transform)) continue;
            if (displacement.PreviousLocalValue is Transform previous)
                displacement.Element.RenderTransform = previous;
            else
                displacement.Element.ClearValue(RenderTransformProperty);
        }
        _displacements.Clear();
        _viewport.IsVisibleChanged -= OnVisibilityChanged;
        _viewport.Unloaded -= OnUnloaded;
        ApplicationTheme.PaletteChanged -= OnPreferenceChanged;
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        if (_started) _layer.Remove(this);
        _snapshot = null;
    }

    private sealed record ListItem(FrameworkElement Element, string Identity, Rect Bounds, int Depth);

    private sealed record Displacement(
        FrameworkElement Element,
        TranslateTransform Transform,
        object PreviousLocalValue);
}
