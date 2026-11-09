using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MinminCountdown;

// #251：悬浮列表的滚动上下文提示。只在列表真实可滚动、且视口上方 / 下方仍有内容（行、便签卡、恢复提示或底部
// 状态）时，把列表内容呈现器对应边缘 18 DIP 渐隐到 40% 不透明度；到达边界、只剩首尾留白或没有可滚动内容时不挂遮罩。
// 遮罩只挂在 PART_ScrollContentPresenter 上：滚动条在模板的另一列，命令区、玩偶层与弹层都不在它的子树里；
// 只改不透明度，不改布局、命中测试、滚动与焦点行为，也不加动画。
public sealed partial class FloatingWindow
{
    private const double ScrollContextFadeLength = 18;
    private const double ScrollContextEdgeOpacity = 0.4;

    private ScrollContentPresenter? _scrollContextPresenter;
    private (bool Above, bool Below, double Height) _scrollContextHint;

    private void UpdateScrollContextHint()
    {
        if (FloatingBodyScroller.Template?.FindName("PART_ScrollContentPresenter", FloatingBodyScroller)
            is not ScrollContentPresenter presenter)
        {
            return;
        }

        var height = presenter.ActualHeight;
        var above = false;
        var below = false;
        if (FloatingBodyScroller.ScrollableHeight > 0.5 && height > 0 &&
            ScrollContextContentSpan(presenter) is { } span)
        {
            above = span.Top < -0.5;
            below = span.Bottom > height + 0.5;
        }

        var hint = (above, below, height);
        if (ReferenceEquals(presenter, _scrollContextPresenter) && hint == _scrollContextHint)
        {
            return;
        }

        _scrollContextPresenter = presenter;
        _scrollContextHint = hint;
        presenter.OpacityMask = above || below ? CreateScrollContextMask(height, above, below) : null;
    }

    // 内容的纵向范围（呈现器坐标）：第一项有意义内容的上沿到最后一项的下沿。列表首尾的固定留白（行区上边距、
    // 便签末尾的 FrameworkElement 内边距、底部状态下方的外边距）不算内容。按布局位置计算，不含完成补位的过渡变换。
    private (double Top, double Bottom)? ScrollContextContentSpan(Visual presenter)
    {
        FrameworkElement? first = null;
        FrameworkElement? last = null;
        void Consider(FrameworkElement element)
        {
            if (!element.IsVisible || element.ActualHeight <= 0)
            {
                return;
            }

            first ??= element;
            last = element;
        }

        Consider(SettingsRecoveryCompletedStatus);
        foreach (var row in FloatingRows.Children.OfType<FrameworkElement>())
        {
            if (row.GetType() != typeof(FrameworkElement))
            {
                Consider(row);
            }
        }

        if (!string.IsNullOrEmpty(FloatingIncome.Text))
        {
            Consider(FloatingIncome);
        }

        if (first is null || last is null)
        {
            return null;
        }

        return (LayoutTop(first, presenter), LayoutTop(last, presenter) + last.ActualHeight);
    }

    private static double LayoutTop(FrameworkElement element, Visual presenter) =>
        VisualTreeHelper.GetParent(element) is Visual parent
            ? parent.TransformToVisual(presenter).Transform((Point)VisualTreeHelper.GetOffset(element)).Y
            : 0;

    // 纵向绝对坐标渐变：有提示的一侧从边缘 40% 渐到 18 DIP 处不透明，其余全不透明；只用它的 alpha。
    private static LinearGradientBrush CreateScrollContextMask(double height, bool above, bool below)
    {
        var fade = Math.Min(ScrollContextFadeLength, height / 2) / height;
        var edge = Color.FromArgb((byte)Math.Round(byte.MaxValue * ScrollContextEdgeOpacity), 0, 0, 0);
        var opaque = Colors.Black;
        var mask = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(above ? edge : opaque, 0),
                new(opaque, above ? fade : 0),
                new(opaque, below ? 1 - fade : 1),
                new(below ? edge : opaque, 1)
            },
            new Point(0, 0),
            new Point(0, height))
        {
            MappingMode = BrushMappingMode.Absolute
        };
        mask.Freeze();
        return mask;
    }
}
