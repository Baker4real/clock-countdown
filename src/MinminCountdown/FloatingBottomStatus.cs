using System.Windows;
using System.Windows.Automation;
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
///     日历未就绪时统一显示 <see cref="CalendarUnavailableText" />（#218），其余情况显示日历状态文本。
///     便签工作区直接复用本组件，仅鼓励文案可以不同。
/// </summary>
internal static class FloatingBottomStatus
{
    public const string CountdownEncouragementText = "加油！美好的事情正在靠近 ~";

    public const string NotesEncouragementText = "Have a nice day!";
    public const string HiddenEarningsText = "今日已赚 ¥••••";

    // #218 日历未就绪时统一说明影响范围与恢复方式，两个工作区同文案。
    public const string CalendarUnavailableText = "节假日安排未就绪：距离下班和今日已赚暂不可用，联网后自动恢复";

    public static FloatingBottomStatusPresentation Resolve(
        decimal? earnings,
        decimal monthlySalary,
        string calendarState,
        string calendarStatusText,
        string encouragementText,
        bool hideEarnings = false)
    {
        if (earnings is not null)
        {
            return new(FloatingBottomStatusKind.Earnings,
                hideEarnings ? HiddenEarningsText : $"今日已赚 ￥{earnings.Value:N2}");
        }

        if (monthlySalary <= 0 &&
            string.Equals(calendarState, "Ready", StringComparison.Ordinal))
        {
            return new(FloatingBottomStatusKind.Encouragement, encouragementText);
        }

        if (string.Equals(calendarState, "Unavailable", StringComparison.Ordinal))
        {
            return new(FloatingBottomStatusKind.Status, CalendarUnavailableText);
        }

        return new(FloatingBottomStatusKind.Status, calendarStatusText);
    }

    public static void Apply(
        AutomationOptionalTextBlock target,
        FloatingBottomStatusPresentation presentation,
        string statusBrushKey,
        string encouragementBrushKey,
        FontFamily encouragementFontFamily)
    {
        var isEncouragement = presentation.Kind == FloatingBottomStatusKind.Encouragement;
        target.IsInvokable = presentation.Kind == FloatingBottomStatusKind.Earnings;
        target.Text = presentation.Text;
        target.SetExcludedFromAutomationTree(isEncouragement);
        if (isEncouragement)
        {
            AutomationProperties.SetName(target, string.Empty);
        }
        else
        {
            AutomationProperties.SetName(target, presentation.Text);
        }
        target.Visibility = string.IsNullOrEmpty(presentation.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        target.SetResourceReference(
            TextBlock.ForegroundProperty,
            isEncouragement ? encouragementBrushKey : statusBrushKey);
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
