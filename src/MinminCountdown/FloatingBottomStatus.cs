using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
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
        InteractiveStatusTextBlock statusTarget,
        SilentTextBlock encouragementTarget,
        FloatingBottomStatusPresentation presentation,
        string statusBrushKey,
        string encouragementBrushKey,
        FontFamily encouragementFontFamily)
    {
        var isEncouragement = presentation.Kind == FloatingBottomStatusKind.Encouragement;
        var hasText = !string.IsNullOrEmpty(presentation.Text);
        var previousVisibility = statusTarget.Visibility;
        var previousInvokable = statusTarget.IsInvokable;
        var previousText = statusTarget.Text;
        var previousName = AutomationProperties.GetName(statusTarget);

        if (isEncouragement)
        {
            statusTarget.IsInvokable = false;
            statusTarget.Text = string.Empty;
            AutomationProperties.SetName(statusTarget, string.Empty);
            statusTarget.Visibility = Visibility.Collapsed;
            statusTarget.IsHitTestVisible = false;

            encouragementTarget.Text = presentation.Text;
            encouragementTarget.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            encouragementTarget.SetResourceReference(TextBlock.ForegroundProperty, encouragementBrushKey);
            encouragementTarget.FontFamily = encouragementFontFamily;
            encouragementTarget.IsHitTestVisible = false;
        }
        else
        {
            encouragementTarget.Text = string.Empty;
            encouragementTarget.Visibility = Visibility.Collapsed;
            encouragementTarget.IsHitTestVisible = false;

            statusTarget.IsInvokable = presentation.Kind == FloatingBottomStatusKind.Earnings;
            statusTarget.Text = presentation.Text;
            AutomationProperties.SetName(statusTarget, presentation.Text);
            statusTarget.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            statusTarget.SetResourceReference(TextBlock.ForegroundProperty, statusBrushKey);
            statusTarget.ClearValue(TextBlock.FontFamilyProperty);
            statusTarget.IsHitTestVisible = true;
        }

        if (statusTarget.Visibility != previousVisibility ||
            statusTarget.IsInvokable != previousInvokable ||
            !string.Equals(statusTarget.Text, previousText, StringComparison.Ordinal) ||
            !string.Equals(AutomationProperties.GetName(statusTarget), previousName, StringComparison.Ordinal))
        {
            UIElementAutomationPeer.FromElement(statusTarget)?.InvalidatePeer();
        }
    }
}
