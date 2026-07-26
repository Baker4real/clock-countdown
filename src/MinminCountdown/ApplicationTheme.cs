using System.Windows;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

internal enum ApplicationThemeMode
{
    Day,
    Night
}

internal sealed class ApplicationTheme
{
    private static readonly ThemeToken[] Tokens =
    [
        Token("CanvasColor", "CanvasBrush", "#FFFDFC", "#2D2630"),
        Token("BackgroundColor", "BackgroundBrush", "#FFF6EF", "#251F27"),
        Token("CardColor", "CardBrush", "#FFF0E4", "#40333E"),
        Token("NoteColor", "NoteBrush", "#FFF6D9", "#4C4039"),
        Token("NoteLineColor", "NoteLineBrush", "#E7C781", "#B89B62"),
        Token("InkColor", "InkBrush", "#4A2D24", "#FFF7FA"),
        Token("BodyColor", "BodyBrush", "#765247", "#E7CFD7"),
        Token("MutedColor", "MutedBrush", "#87665A", "#D8BBC8"),
        Token("ActiveColor", "ActiveBrush", "#B8533D", "#B55770"),
        Token("ActiveHoverColor", "ActiveHoverBrush", "#A84432", "#9B4660"),
        Token("PeachLineColor", "PeachLineBrush", "#EFC9A7", "#8A6A7F"),
        Token("DangerColor", "DangerBrush", "#A24438", "#FFB6AD"),
        Token("SuccessColor", "SuccessBrush", "#8F5A2D", "#E2C38A"),
        Token("TagColor", "TagBrush", "#FCE8DE", "#3A2F39"),
        Token("ExpiredColor", "ExpiredBrush", "#F9DFDB", "#4E3940"),
        Token("SegmentSelectedForegroundColor", "SegmentSelectedForegroundBrush", "#4A2D24", "#E7CFD7")
    ];

    private readonly ResourceDictionary _resources;
    private ApplicationThemeMode? _activeMode;

    public ApplicationTheme(ResourceDictionary resources)
    {
        _resources = resources;
    }

    public ApplicationThemeMode ActiveMode => _activeMode ??
        throw new InvalidOperationException("应用主题尚未初始化。");

    public ApplicationThemeMode Apply(DateTimeOffset nowUtc)
    {
        var nextMode = Resolve(nowUtc);
        if (_activeMode == nextMode)
        {
            return nextMode;
        }

        foreach (var token in Tokens)
        {
            var color = nextMode == ApplicationThemeMode.Day ? token.DayColor : token.NightColor;
            _resources[token.ColorKey] = color;
            _resources[token.BrushKey] = new SolidColorBrush(color);
        }

        _activeMode = nextMode;
        return nextMode;
    }

    public static ApplicationThemeMode Resolve(DateTimeOffset nowUtc)
    {
        var beijing = ChinaStandardTime.ToBeijingTime(nowUtc);
        return beijing.Hour is >= 7 and < 19
            ? ApplicationThemeMode.Day
            : ApplicationThemeMode.Night;
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

    private static ThemeToken Token(
        string colorKey,
        string brushKey,
        string dayColor,
        string nightColor) =>
        new(colorKey, brushKey, ParseColor(dayColor), ParseColor(nightColor));

    private static Color ParseColor(string value) =>
        ColorConverter.ConvertFromString(value) is Color color
            ? color
            : throw new InvalidOperationException($"无法解析主题颜色：{value}");

    private sealed record ThemeToken(
        string ColorKey,
        string BrushKey,
        Color DayColor,
        Color NightColor);
}
