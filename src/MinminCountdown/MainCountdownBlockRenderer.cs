using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MinminCountdown;

// Shared production renderer. WindowVerification uses this same algorithm when
// comparing an in-place countdown update with a freshly constructed block layout.
internal static class MainCountdownBlockRenderer
{
    private const double CountdownRemainingColumnWidth = 228;
    private const double CountdownRemainingUnitFontSize = 13;
    private const double CountdownRemainingBaseFontSize = 20;

    internal static void Render(MainWindow window, MainCountdownBlocksHost host, string remaining)
    {
        host.Surface.Children.Clear();
        var pairs = ParseMainRemainingPairs(remaining);
        host.ShapeKey = RemainingPair.ShapeKey(pairs, remaining);
        host.DpiScale = VisualTreeHelper.GetDpi(window).PixelsPerDip;
        if (pairs is null || pairs.Count == 0)
        {
            var fallback = new TextBlock
            {
                Text = remaining,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = CountdownRemainingBaseFontSize,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None
            };
            fallback.SetResourceReference(TextBlock.FontFamilyProperty, "NumeralFontFamily");
            fallback.SetResourceReference(
                TextBlock.ForegroundProperty,
                host.IsPrimary ? "PrimaryCountdownNumberBrush" : "BodyBrush");
            Typography.SetNumeralAlignment(fallback, FontNumeralAlignment.Tabular);
            host.Surface.Children.Add(fallback);
            host.Values = [];
            host.LastRenderedText = remaining;
            return;
        }

        // #148：主窗口分格与悬浮窗同一套竖长口径（约 2:3、单位在块外、间距约块宽三分之一），
        // 主窗真实 DIP：主项 38x57、次项 34x51，圆角约短边 1/4；数值优先压内边距与间距，
        // 最后才在辅助下限档之上收缩数值字号；单位字固定在下限档、不得缩小硬塞。
        const double valueFontPrimary = 20;
        const double valueFontSecondary = 16;
        const double blockWidthPrimary = 38;
        const double blockWidthSecondary = 34;
        const double blockHeightPrimary = 57;
        const double blockHeightSecondary = 51;
        const double minimumValueFontSize = 13;
        const double blockWidthReserve = 2;
        const double unitGap = 2;
        var valueFontSize = host.IsPrimary ? valueFontPrimary : valueFontSecondary;
        var blockPadding = 3;
        var pairGap = 10;

        var valueBlocks = pairs
            .Select(pair => new TextBlock
            {
                Text = pair.Value,
                MinWidth = host.IsPrimary ? 28 : 25,
                Padding = new Thickness(blockPadding, 0, blockPadding, 0),
                FontSize = valueFontSize,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None
            })
            .ToArray();
        foreach (var valueBlock in valueBlocks)
        {
            valueBlock.SetResourceReference(TextBlock.FontFamilyProperty, "NumeralFontFamily");
            Typography.SetNumeralAlignment(valueBlock, FontNumeralAlignment.Tabular);
        }

        var unitFace = new Typeface(
            (FontFamily)window.FindResource("UiFontFamily"),
            new FontStyle(),
            FontWeights.Regular,
            FontStretches.Normal);
        double ValueBlockWidth(int index, double font, double padding)
        {
            valueBlocks[index].FontSize = font;
            valueBlocks[index].Padding = new Thickness(padding, 0, padding, 0);
            valueBlocks[index].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return valueBlocks[index].DesiredSize.Width;
        }

        double RequiredWidth(double font, double padding, double gap)
        {
            var total = 0d;
            for (var index = 0; index < pairs.Count; index++)
            {
                var valueWidth = ValueBlockWidth(index, font, padding);
                var unitWidth = MeasureMainText(pairs[index].Unit, unitFace, CountdownRemainingUnitFontSize);
                var blockWidth = Math.Max(
                    host.IsPrimary ? blockWidthPrimary : blockWidthSecondary,
                    Math.Ceiling(valueWidth) + blockWidthReserve);
                total += blockWidth + unitWidth + unitGap;
                if (index < pairs.Count - 1)
                {
                    total += gap;
                }
            }
            return total;
        }

        var availableWidth = CountdownRemainingColumnWidth;
        if (RequiredWidth(valueFontSize, blockPadding, pairGap) > availableWidth)
        {
            blockPadding = 2;
        }
        if (RequiredWidth(valueFontSize, blockPadding, pairGap) > availableWidth)
        {
            pairGap = 4;
        }
        if (host.IsPrimary && RequiredWidth(minimumValueFontSize, blockPadding, pairGap) > availableWidth)
        {
            blockPadding = 0;
            pairGap = 0;
        }

        var required = RequiredWidth(valueFontSize, blockPadding, pairGap);
        while (required > availableWidth && valueFontSize > minimumValueFontSize)
        {
            var fittedFontSize = valueFontSize * (availableWidth / required);
            var nextFontSize = Math.Max(minimumValueFontSize, Math.Floor(fittedFontSize * 4) / 4);
            if (nextFontSize >= valueFontSize)
            {
                nextFontSize = Math.Max(minimumValueFontSize, valueFontSize - 0.25);
            }
            valueFontSize = nextFontSize;
            required = RequiredWidth(valueFontSize, blockPadding, pairGap);
        }

        var blockHeight = host.IsPrimary ? blockHeightPrimary : blockHeightSecondary;
        var blockBaseWidth = host.IsPrimary ? blockWidthPrimary : blockWidthSecondary;
        var blockRadius = Math.Max(4, blockBaseWidth / 4);
        var values = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        for (var index = 0; index < pairs.Count; index++)
        {
            var value = valueBlocks[index];
            value.FontSize = valueFontSize;
            value.Padding = new Thickness(blockPadding, 0, blockPadding, 0);
            value.SetResourceReference(
                TextBlock.ForegroundProperty,
                host.IsPrimary ? "PrimaryCountdownNumberBrush" : "InkBrush");
            value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var block = new Border
            {
                MinWidth = Math.Max(
                    blockBaseWidth,
                    Math.Ceiling(value.DesiredSize.Width) + blockWidthReserve),
                Height = blockHeight,
                CornerRadius = new CornerRadius(blockRadius)
            };
            block.SetResourceReference(
                Border.BackgroundProperty,
                host.IsPrimary ? "PinkBlockBrush" : "SecondaryBlockBrush");
            block.Child = value;
            var unit = new TextBlock
            {
                Text = pairs[index].Unit,
                Margin = new Thickness(unitGap, 0, 0, 0),
                FontSize = CountdownRemainingUnitFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None
            };
            unit.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var pairPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = index == 0 ? new Thickness(0) : new Thickness(pairGap, 0, 0, 0)
            };
            pairPanel.Children.Add(block);
            pairPanel.Children.Add(unit);
            values.Children.Add(pairPanel);
        }

        host.Surface.Children.Add(values);
        host.Values = valueBlocks;
        host.LastRenderedText = remaining;
    }

    private static double MeasureMainText(string text, Typeface typeface, double fontSize)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1);
        return formatted.WidthIncludingTrailingWhitespace + Math.Max(0, formatted.OverhangTrailing);
    }

    internal static IReadOnlyList<RemainingPair>? ParseMainRemainingPairs(string remaining)
    {
        if (string.Equals(remaining, "已到期", StringComparison.Ordinal))
        {
            return null;
        }
        var tokens = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length % 2 != 0)
        {
            return null;
        }
        var pairs = new List<RemainingPair>(tokens.Length / 2);
        for (var index = 0; index < tokens.Length; index += 2)
        {
            if (!long.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return null;
            }
            var unit = tokens[index + 1] switch
            {
                "天" => "天",
                "小时" => "时",
                "分钟" => "分",
                "秒" => "秒",
                _ => null
            };
            if (unit is null)
            {
                return null;
            }
            pairs.Add(new RemainingPair(tokens[index], unit));
        }
        return pairs;
    }
}
