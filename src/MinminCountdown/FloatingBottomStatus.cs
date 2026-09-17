using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MinminCountdown;

internal enum FloatingBottomStatusKind
{
    Earnings,
    Encouragement,
    Status
}

internal sealed record FloatingBottomStatusPresentation(
    FloatingBottomStatusKind Kind,
    string Text);

/// <summary>
///     悬浮窗两个工作区（倒计时、便签）共用的底部区域规则：
///     月薪大于 0 且日历就绪时显示今日已赚金额；月薪等于 0 且日历就绪时显示手写体鼓励文案；
///     其余情况显示日历状态文本。便签工作区直接复用本组件，仅鼓励文案可以不同。
/// </summary>
internal static class FloatingBottomStatus
{
    public const string CountdownEncouragementText = "加油！美好的事情正在靠近 ~";

    public const string NotesEncouragementText = "Have a nice day!";

    public static FloatingBottomStatusPresentation Resolve(
        decimal? earnings,
        decimal monthlySalary,
        string calendarState,
        string calendarStatusText,
        string encouragementText)
    {
        if (earnings is not null)
        {
            return new(FloatingBottomStatusKind.Earnings, $"今日已赚 ￥{earnings.Value:N2}");
        }

        if (monthlySalary <= 0 &&
            string.Equals(calendarState, "Ready", StringComparison.Ordinal))
        {
            return new(FloatingBottomStatusKind.Encouragement, encouragementText);
        }

        return new(FloatingBottomStatusKind.Status, calendarStatusText);
    }

    public static void Apply(
        TextBlock target,
        FloatingBottomStatusPresentation presentation,
        Brush statusBrush,
        Brush encouragementBrush,
        FontFamily encouragementFontFamily)
    {
        var isEncouragement = presentation.Kind == FloatingBottomStatusKind.Encouragement;
        target.Text = presentation.Text;
        target.Visibility = string.IsNullOrEmpty(presentation.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        target.Foreground = isEncouragement ? encouragementBrush : statusBrush;
        if (isEncouragement)
        {
            target.FontFamily = encouragementFontFamily;
        }
        else
        {
            target.ClearValue(TextBlock.FontFamilyProperty);
        }

        target.IsHitTestVisible = !isEncouragement;
    }
}
