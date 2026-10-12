using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MinminCountdown;

// #233: keep only a viewport-sized rendering of the old card after the domain
// transaction removes it. The snapshot cannot receive input or affect layout.
internal sealed class NoteCompletionExit : Adorner, IDisposable
{
    private readonly FrameworkElement _viewport;
    private readonly AdornerLayer _layer;
    private readonly Window? _window;
    private BitmapSource? _snapshot;
    private bool _started;
    private bool _disposed;

    private NoteCompletionExit(FrameworkElement viewport, AdornerLayer layer, BitmapSource snapshot) : base(viewport)
    {
        _viewport = viewport;
        _layer = layer;
        _window = Window.GetWindow(viewport);
        _snapshot = snapshot;
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        AutomationProperties.SetAutomationId(this, "NoteCompletionExit");
    }

    public static NoteCompletionExit? Capture(FrameworkElement? card, FrameworkElement viewport)
    {
        if (!ApplicationTheme.ClientAreaAnimationEnabled || card is null || !card.IsVisible ||
            !viewport.IsVisible || viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0 ||
            AdornerLayer.GetAdornerLayer(viewport) is not { } layer)
            return null;

        var bounds = card.TransformToAncestor(viewport).TransformBounds(new Rect(card.RenderSize));
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(card) { Stretch = Stretch.Fill }, null, bounds);
        }
        var dpi = VisualTreeHelper.GetDpi(viewport);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(viewport.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(viewport.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return new NoteCompletionExit(viewport, layer, bitmap);
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
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;
        var animation = new DoubleAnimation(1, 0, MotionTokens.NoteCompletionExitDuration)
        {
            EasingFunction = MotionTokens.CreateEaseOut(),
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) => Dispose();
        BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_snapshot is not null) drawingContext.DrawImage(_snapshot, new Rect(_viewport.RenderSize));
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        BeginAnimation(OpacityProperty, null);
        _viewport.IsVisibleChanged -= OnVisibilityChanged;
        _viewport.Unloaded -= OnUnloaded;
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        if (_started) _layer.Remove(this);
        _snapshot = null;
    }
}
