using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MinminCountdown;

/// <summary>
/// 应用固定为单一日间主题：只应用日间画刷 token，不再解析 Auto/Day/Night，也不随 tick 变化。
/// Tokens 是 Primitive 原色层（App.xaml *Color 资源）的运行时权威副本：
/// 键与顺序与 App.xaml 的第 1 层保持一一对应，新增或修改颜色必须两处同步。
/// 分层与可读性契约见 DESIGN.md §13。
/// </summary>
internal sealed class ApplicationTheme
{
    private static readonly ThemeToken[] Tokens =
    [
        // 画布与表面
        Token("CanvasColor", "CanvasBrush", "#FFFDF3EB"),
        Token("CanvasDeepColor", "CanvasDeepBrush", "#FFFDE8DC"),
        Token("BackgroundColor", "BackgroundBrush", "#FFFDF0E4"),
        Token("CardColor", "CardBrush", "#FFFEFCFB"),
        Token("PanelColor", "PanelBrush", "#FFFFFDFC"),
        Token("CardAltColor", "CardAltBrush", "#FFFEF9F5"),
        Token("StatusBarColor", "StatusBarBrush", "#FFFDFAF8"),
        Token("NoteColor", "NoteBrush", "#FFFEF5DC"),
        Token("NoteWhiteColor", "NoteWhiteBrush", "#FFFEFDFD"),
        Token("NoteLineColor", "NoteLineBrush", "#FFF6D9B2"),
        Token("NoteActiveRailColor", "NoteActiveRailBrush", "#FFCD623E"),
        Token("NoteCompletedRailColor", "NoteCompletedRailBrush", "#FF8A7069"),
        Token("NoteTimelineColor", "NoteTimelineBrush", "#FFF6DFD3"),

        // 文字与结构线
        Token("InkColor", "InkBrush", "#FF3B190D"),
        Token("BodyColor", "BodyBrush", "#FF6B4A3C"),
        Token("MutedColor", "MutedBrush", "#FF82665F"),
        Token("ActiveColor", "ActiveBrush", "#FFE05A4A"),
        Token("ActiveHoverColor", "ActiveHoverBrush", "#FFB84A36"),
        // #151：文本选区高亮独立语义色（不得复用 1px 结构线色）。
        Token("SelectionColor", "SelectionBrush", "#FFD4526A"),
        // #199：主窗口与悬浮窗统一使用深玫粉主行动；Danger 保留砖红。
        Token("MainActionColor", "MainActionBrush", "#FFC23E68"),
        Token("MainActionHoverColor", "MainActionHoverBrush", "#FFB0355D"),
        Token("MainActionPressedColor", "MainActionPressedBrush", "#FF9B2D50"),
        Token("MainActionFocusColor", "MainActionFocusBrush", "#FFFFD49A"),
        Token("MainActionTextColor", "MainActionTextBrush", "#FFFDFC"),
        // #151：主窗口次级实心按钮（「保存设置」）珊瑚语义色，文字用主文字色。
        Token("CoralActionColor", "CoralActionBrush", "#FFD68481"),
        Token("PeachLineColor", "PeachLineBrush", "#FFF6DFD3"),
        Token("DangerColor", "DangerBrush", "#FFB94D3A"),
        Token("SuccessColor", "SuccessBrush", "#FF266B5E"),
        Token("TagColor", "TagBrush", "#FFF7E8"),
        Token("TagChipTextColor", "TagChipTextBrush", "#FF3B190D"),
        Token("ExpiredColor", "ExpiredBrush", "#FFF9DFDB"),
        Token("SegmentSelectedColor", "SegmentSelectedBrush", "#FFFEEFE1"),
        Token("SegmentSelectedForegroundColor", "SegmentSelectedForegroundBrush", "#FF3B190D"),
        Token("WorkspacePanelColor", "WorkspacePanelBrush", "#00FFFDFC"),
        Token("WorkspacePanelLineColor", "WorkspacePanelLineBrush", "#00F6DFD3"),

        // 强调与控件
        Token("PrimaryCountdownNumberColor", "PrimaryCountdownNumberBrush", "#FFC0102F"),
        Token("PinkTintColor", "PinkTintBrush", "#FFFEDDDE"),
        Token("PinkAccentColor", "PinkAccentBrush", "#FFDE5863"),
        Token("PinkAccentStrongColor", "PinkAccentStrongBrush", "#FFC23E68"),
        Token("PinkBadgeColor", "PinkBadgeBrush", "#FFFD97A1"),
        Token("PinkBadgeTextColor", "PinkBadgeTextBrush", "#FFFDFC"),
        Token("PinkBlockColor", "PinkBlockBrush", "#FEE0E4"),
        Token("PinkTintSoftColor", "PinkTintSoftBrush", "#FEF0F1"),
        Token("SecondaryBlockColor", "SecondaryBlockBrush", "#FAF0EA"),
        Token("PeachTintColor", "PeachTintBrush", "#FEEFE1"),
        Token("PeachRailColor", "PeachRailBrush", "#CD623E"),
        Token("PeachBadgeColor", "PeachBadgeBrush", "#FBAE90"),
        Token("PeachBadgeMutedColor", "PeachBadgeMutedBrush", "#F3DED8"),
        Token("IconAccentColor", "IconAccentBrush", "#F28995"),

        // 便签与提醒
        Token("WarningAccentColor", "WarningAccentBrush", "#FFAD4A10"),
        Token("WarningTintColor", "WarningTintBrush", "#FDEDD8"),
        Token("ReminderCardColor", "ReminderCardBrush", "#FEF9E8"),
        Token("ReminderPageColor", "ReminderPageBrush", "#FEFEFE"),
        Token("SourceNoteIconColor", "SourceNoteIconBrush", "#FED67F"),
        Token("DecoTextColor", "DecoTextBrush", "#D9B7B1"),
        Token("DecoTextSoftColor", "DecoTextSoftBrush", "#E5AE9C"),

        // 日期印记 pastel 轮换
        Token("StampPinkColor", "StampPinkBrush", "#FEEFF1"),
        Token("StampPinkTextColor", "StampPinkTextBrush", "#D41F57"),
        Token("StampBlueColor", "StampBlueBrush", "#EEF5FE"),
        Token("StampBlueTextColor", "StampBlueTextBrush", "#FF1D6BB0"),
        Token("StampGreenColor", "StampGreenBrush", "#EDF6E8"),
        Token("StampGreenTextColor", "StampGreenTextBrush", "#FF26732D"),

        // 角色面板（三丽鸥玩偶泊位）
        Token("CharacterPanelColor", "CharacterPanelBrush", "#FFF7E8"),
        Token("CharacterLineColor", "CharacterLineBrush", "#EFC9A7"),
        Token("CharacterMyMelodyDockColor", "CharacterMyMelodyDockBrush", "#FFE3C7"),
        Token("CharacterCinnamorollDockColor", "CharacterCinnamorollDockBrush", "#FFF0D2"),
        Token("CharacterKuromiDockColor", "CharacterKuromiDockBrush", "#F5DEC5"),

        // 悬浮窗组件原色
        Token("FloatingDayPrimaryCountdownAccentColor", "FloatingDayPrimaryCountdownAccentBrush", "#EE2B46"),
        Token("FloatingDayPlushGlowColor", "FloatingDayPlushGlowBrush", "#B9794E")
    ];

    private readonly ResourceDictionary _resources;
    private bool _applied;

    public ApplicationTheme(ResourceDictionary resources)
    {
        _resources = resources;
    }

    public void Apply()
    {
        if (!_applied)
        {
            foreach (var token in Tokens)
            {
                _resources[token.ColorKey] = token.Color;
                _resources[token.BrushKey] = new SolidColorBrush(token.Color);
            }

            _applied = true;
        }

        ApplyMotionTokens();
    }

    private void ApplyMotionTokens()
    {
        var enabled = ClientAreaAnimationEnabled;
        _resources["MotionHoverDuration"] = new Duration(
            enabled ? MotionTokens.HoverDuration : TimeSpan.Zero);
        _resources["MotionPressDuration"] = new Duration(
            enabled ? MotionTokens.PressDuration : TimeSpan.Zero);
        _resources["MotionPopupEnterDuration"] = new Duration(
            enabled ? MotionTokens.PopupEnterDuration : TimeSpan.Zero);
        _resources["MotionPopupExitDuration"] = new Duration(
            enabled ? MotionTokens.PopupExitDuration : TimeSpan.Zero);
        _resources["MotionIndicatorDuration"] = new Duration(
            enabled ? MotionTokens.IndicatorDuration : TimeSpan.Zero);
        _resources["MotionCountdownDuration"] = new Duration(
            enabled ? MotionTokens.CountdownDuration : TimeSpan.Zero);
        _resources["MotionEaseOut"] = MotionTokens.CreateEaseOut();
        _resources["MotionCubicEaseOut"] = new CubicEase
        {
            EasingMode = EasingMode.EaseOut
        };
        _resources["MotionQuarticEaseOut"] = MotionTokens.CreateEaseOut();
    }

    internal static bool MeetsContrast(Brush background, Brush foreground, double minimum)
    {
        return background is SolidColorBrush backgroundBrush &&
            foreground is SolidColorBrush foregroundBrush &&
            MeetsContrast(backgroundBrush.Color, foregroundBrush.Color, minimum);
    }

    internal static bool MeetsContrast(Color first, Color second, double minimum)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255d;
                return normalized <= 0.04045
                    ? normalized / 12.92
                    : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(color.R) +
                0.7152 * Channel(color.G) +
                0.0722 * Channel(color.B);
        }

        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        var ratio = (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
        return ratio >= minimum;
    }

    // #194：所有动效的即时等价入口——读取系统「客户区动画」开关
    //（SystemParameters.ClientAreaAnimation）。系统关闭时每个动效 token 都变为零时长。
    internal static bool ClientAreaAnimationEnabled => SystemParameters.ClientAreaAnimation;

    internal static TimeSpan GetEffectiveMotionDuration(TimeSpan enabledDuration) =>
        ClientAreaAnimationEnabled ? enabledDuration : TimeSpan.Zero;

    private static ThemeToken Token(
        string colorKey,
        string brushKey,
        string dayColor) =>
        new(colorKey, brushKey, ParseColor(dayColor));

    private static Color ParseColor(string value) =>
        ColorConverter.ConvertFromString(value) is Color color
            ? color
            : throw new InvalidOperationException($"无法解析主题颜色：{value}");

    private sealed record ThemeToken(
        string ColorKey,
        string BrushKey,
        Color Color);
}

internal static class MotionTokens
{
    internal static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(120);
    internal static readonly TimeSpan PressDuration = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan PopupEnterDuration = TimeSpan.FromMilliseconds(140);
    internal static readonly TimeSpan PopupExitDuration = TimeSpan.FromMilliseconds(90);
    internal static readonly TimeSpan IndicatorDuration = TimeSpan.FromMilliseconds(160);
    internal static readonly TimeSpan CountdownDuration = TimeSpan.FromMilliseconds(90);

    internal static PowerEase CreateEaseOut() => new()
    {
        EasingMode = EasingMode.EaseOut,
        Power = 4
    };
}
