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
        Token("CanvasColor", "CanvasBrush", "#FFFDFC", "#33291E"),
        Token("BackgroundColor", "BackgroundBrush", "#FFF6EF", "#2B2118"),
        Token("CardColor", "CardBrush", "#FFF0E4", "#423125"),
        Token("NoteColor", "NoteBrush", "#FFF6D9", "#4E3827"),
        Token("NoteLineColor", "NoteLineBrush", "#E7C781", "#A77B4B"),
        Token("InkColor", "InkBrush", "#4A2D24", "#FFF4D8"),
        Token("BodyColor", "BodyBrush", "#765247", "#E7C99B"),
        Token("MutedColor", "MutedBrush", "#87665A", "#D7B88C"),
        Token("ActiveColor", "ActiveBrush", "#B8533D", "#9A523B"),
        Token("ActiveHoverColor", "ActiveHoverBrush", "#B34B36", "#86442F"),
        Token("PeachLineColor", "PeachLineBrush", "#F1D5C5", "#8B6845"),
        Token("DangerColor", "DangerBrush", "#A24438", "#FFC0AD"),
        Token("SuccessColor", "SuccessBrush", "#92522E", "#E2C38A"),
        Token("TagColor", "TagBrush", "#FFF7E8", "#4B3522"),
        Token("TagChipTextColor", "TagChipTextBrush", "#4A2D24", "#4A2D24"),
        Token("ExpiredColor", "ExpiredBrush", "#F9DFDB", "#59392E"),
        Token("SegmentSelectedForegroundColor", "SegmentSelectedForegroundBrush", "#725326", "#FFF0CC"),
        Token("CharacterPanelColor", "CharacterPanelBrush", "#FFF7E8", "#3B2B1D"),
        Token("CharacterLineColor", "CharacterLineBrush", "#EFC9A7", "#987644"),
        Token("CharacterMyMelodyDockColor", "CharacterMyMelodyDockBrush", "#FFE3C7", "#5A3D2B"),
        Token("CharacterCinnamorollDockColor", "CharacterCinnamorollDockBrush", "#FFF0D2", "#55452A"),
        Token("CharacterKuromiDockColor", "CharacterKuromiDockBrush", "#F5DEC5", "#51362C")
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
        return Apply(Resolve(nowUtc));
    }

    public ApplicationThemeMode Apply(DateTimeOffset nowUtc, ThemePreferenceMode preference)
    {
        return Apply(Resolve(nowUtc, preference));
    }

    internal ApplicationThemeMode Apply(ApplicationThemeMode nextMode)
    {
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
        return Resolve(nowUtc, ThemePreferenceMode.Auto);
    }

    public static ApplicationThemeMode Resolve(DateTimeOffset nowUtc, ThemePreferenceMode preference)
    {
        return ThemePreferenceResolver.Resolve(nowUtc, preference) == EffectiveThemeMode.Day
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
