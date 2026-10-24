using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class FloatingWindow
{
    private const double CountdownMascotSafeInset = 54;
    private const double PrimaryRemainingWidth = 194;
    private const double SecondaryRemainingWidth = 194;
    internal const string RemainingBlocksTag = "FloatingRemainingBlocks";
    private const string EncourageBadgeText = "超期待！";
    private const double KeyTextMinimumFontSize = 12;
    private TextBlock? _workdayCompletionIncome;

    private FrameworkElement BuildWarmPaperCountdownRow(FloatingCountdownRow row)
    {
        var secondsChanged = TrackSecondsChange(row.Key, row.Remaining);
        return BuildWarmPaperRow(
            row.IsPrimary ? BuildWarmPaperPrimaryContent(row, secondsChanged)
                : BuildWarmPaperSecondaryContent(row, secondsChanged),
            row.IsPrimary ? $"主要倒计时 {row.Name}，{row.Remaining}" : $"倒计时 {row.Name}，{row.Remaining}",
            row.IsPrimary);
    }

    private FrameworkElement BuildWorkdayCompletionRow()
    {
        var theme = ActiveTheme;
        var content = new StackPanel { Margin = new Thickness(12, 8, 68, 8) };
        var title = new TextBlock
        {
            Text = "下班啦",
            FontSize = (double)FindResource("TypeBodyFontSize"),
            FontWeight = FontWeights.SemiBold
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, theme.TextBrushKey);
        content.Children.Add(title);
        _workdayCompletionIncome = new TextBlock
        {
            FontSize = (double)FindResource("TypeCaptionFontSize"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _workdayCompletionIncome.SetResourceReference(TextBlock.ForegroundProperty, theme.MutedBrushKey);
        Typography.SetNumeralAlignment(_workdayCompletionIncome, FontNumeralAlignment.Tabular);
        content.Children.Add(_workdayCompletionIncome);
        return BuildWarmPaperRow(content, "下班啦", true);
    }

    private FrameworkElement BuildWarmPaperRow(FrameworkElement content, string accessibleName, bool isPrimary)
    {
        var theme = ActiveTheme;
        var root = new Grid
        {
            MinHeight = isPrimary ? 68 : 39,
            Margin = new Thickness(0),
            Background = Brushes.Transparent
        };
        UiAccessibility.Name(root, accessibleName);

        if (isPrimary)
        {
            var tint = new Border
            {
                Margin = new Thickness(0, 0, CountdownMascotSafeInset, 1),
                CornerRadius = new CornerRadius(8),
                Opacity = 0.8,
                IsHitTestVisible = false
            };
            tint.SetResourceReference(Border.BackgroundProperty, "PinkTintSoftBrush");
            root.Children.Add(tint);
            var accent = new Border
            {
                Width = 3,
                Height = 42,
                Margin = new Thickness(0, 0, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(1.5),
                IsHitTestVisible = false
            };
            accent.SetResourceReference(Border.BackgroundProperty, theme.PrimaryCountdownAccentBrushKey);
            root.Children.Add(accent);
        }
        else
        {
            var tint = new Border
            {
                Margin = new Thickness(0, 0, CountdownMascotSafeInset, 1),
                CornerRadius = new CornerRadius(6),
                Opacity = 0.72,
                IsHitTestVisible = false
            };
            tint.SetResourceReference(Border.BackgroundProperty, "CardAltBrush");
            root.Children.Add(tint);
        }

        var rule = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 0, CountdownMascotSafeInset, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = isPrimary ? 0.78 : 0.48,
            IsHitTestVisible = false
        };
        rule.SetResourceReference(Border.BackgroundProperty, theme.BorderBrushKey);
        root.Children.Add(rule);

        root.Children.Add(content);
        return new Border
        {
            Child = root,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
    }

    private FrameworkElement BuildWarmPaperPrimaryContent(
        FloatingCountdownRow row,
        bool fadeSeconds)
    {
        var theme = ActiveTheme;
        var layout = new Grid
        {
            Margin = new Thickness(12, 8, CountdownMascotSafeInset + 14, 8)
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock
        {
            Text = row.Name,
            MaxHeight = 40,
            Margin = new Thickness(0, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = (double)FindResource("TypeBodyFontSize"),
            FontWeight = FontWeights.SemiBold,
            LineHeight = 20,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, theme.TextBrushKey);
        Grid.SetRow(title, 0);
        Grid.SetColumn(title, 0);
        layout.Children.Add(title);
        var deadlineRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 0)
        };
        var deadline = new TextBlock
        {
            // #148：截止行数字与「月」「日」之间留空格，并使用表格数字避免刷新跳动。
            Text = DateDisplay.Deadline(row.TargetUtc),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0, 0, 3, 0),
            FontSize = (double)FindResource("TypeCaptionFontSize"),
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap
        };
        deadline.SetResourceReference(TextBlock.ForegroundProperty, theme.MutedBrushKey);
        Typography.SetNumeralAlignment(deadline, FontNumeralAlignment.Tabular);
        deadlineRow.Children.Add(deadline);

        if (row.Kind == FloatingCountdownRowKind.WorkdayEnd)
        {
            // #148：徽章只挂距离下班；文字用主文字色（旧白字对比不足），
            // #229：与截止日期同行，给四字标题留出完整宽度。
            var badge = new Border
            {
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false,
                Focusable = false
            };
            badge.SetResourceReference(Border.BackgroundProperty, "PinkBadgeBrush");
            var badgeText = new TextBlock
            {
                Text = EncourageBadgeText,
                FontSize = (double)FindResource("TypeCaptionFontSize"),
                FontWeight = FontWeights.SemiBold,
                LineHeight = 17,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap
            };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            badge.Child = badgeText;
            UiAccessibility.Name(badge, EncourageBadgeText);
            deadlineRow.Children.Add(badge);
        }

        Grid.SetRow(deadlineRow, 1);
        Grid.SetColumnSpan(deadlineRow, 2);
        var remainingBlocks = BuildRemainingBlocks(
            row.Remaining,
            isPrimary: true,
            fadeSeconds: fadeSeconds);
        Grid.SetColumn(remainingBlocks, 1);
        layout.Children.Add(deadlineRow);
        layout.Children.Add(remainingBlocks);
        return layout;
    }

    private FrameworkElement BuildWarmPaperSecondaryContent(
        FloatingCountdownRow row,
        bool fadeSeconds)
    {
        var layout = new Grid
        {
            Margin = new Thickness(7, 4, CountdownMascotSafeInset + 14, 4)
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = row.Name,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = (double)FindResource("TypeCaptionFontSize"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, ActiveTheme.TextBrushKey);
        var remainingBlocks = BuildRemainingBlocks(
            row.Remaining,
            isPrimary: false,
            fadeSeconds: fadeSeconds);
        Grid.SetColumn(remainingBlocks, 1);
        layout.Children.Add(name);
        layout.Children.Add(remainingBlocks);
        return layout;
    }

    private FrameworkElement BuildRemainingBlocks(
        string remaining,
        bool isPrimary,
        bool fadeSeconds = false)
    {
        var theme = ActiveTheme;
        var valueBrushKey = isPrimary ? "PrimaryCountdownNumberBrush" : theme.TextBrushKey;
        var pairs = ParseRemainingPairs(remaining);
        if (pairs is null || pairs.Count == 0)
        {
            var fallback = CountdownRemainingText(
                pairs is { Count: 0 } ? "少于 1 天" : remaining,
                isPrimary
                    ? (double)FindResource("SecondaryWindowTitleFontSize")
                    : (double)FindResource("SecondaryWindowSectionFontSize"),
                FontWeights.SemiBold);
            FitSingleLineText(fallback, isPrimary ? PrimaryRemainingWidth : SecondaryRemainingWidth);
            fallback.Width = isPrimary ? PrimaryRemainingWidth : SecondaryRemainingWidth;
            // #148：到期剩余文字不得用强调轨色当字；主项到期回落主项数字色，次项回落主文字。
            fallback.SetResourceReference(TextBlock.ForegroundProperty, valueBrushKey);
            return fallback;
        }

        // #198: keep the floating window on the same enlarged countdown
        // hierarchy while retaining its compact 520 x 390 geometry.
        var valueFontPrimary = (double)FindResource("SecondaryWindowTitleFontSize");
        var valueFontSecondary = (double)FindResource("SecondaryWindowSectionFontSize");
        const double blockWidthPrimary = 28;
        const double blockWidthSecondary = 20;
        const double blockHeightPrimary = 42;
        const double blockHeightSecondary = 30;
        const double rightInset = 2;
        const double availableWidth = PrimaryRemainingWidth - rightInset;
        const double blockWidthReserve = 3;

        var valueBlocks = pairs
            .Select(pair => new TextBlock
            {
                Text = pair.Value,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = isPrimary ? valueFontPrimary : valueFontSecondary,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap
            })
            .ToArray();
        foreach (var valueBlock in valueBlocks)
        {
            valueBlock.SetResourceReference(TextBlock.ForegroundProperty, valueBrushKey);
            valueBlock.SetResourceReference(TextBlock.FontFamilyProperty, "NumeralFontFamily");
            Typography.SetNumeralAlignment(valueBlock, FontNumeralAlignment.Tabular);
        }

        var unitFace = new Typeface(
            (FontFamily)FindResource("UiFontFamily"),
            new FontStyle(),
            FontWeights.Regular,
            FontStretches.Normal);
        double ValueBlockWidth(int index, double font)
        {
            valueBlocks[index].FontSize = font;
            valueBlocks[index].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return valueBlocks[index].DesiredSize.Width;
        }

        double RequiredWidth(double font, double padding, double gap)
        {
            var unitFontSize = Math.Max(
                KeyTextMinimumFontSize,
                (double)FindResource("TypeCaptionFontSize"));
            var dpi = VisualTreeHelper.GetDpi(this);
            var total = 0d;
            for (var index = 0; index < pairs.Count; index++)
            {
                var valueWidth = ValueBlockWidth(index, font);
                var unitWidth = MeasureText(pairs[index].Unit, unitFace, unitFontSize, dpi.PixelsPerDip);
                var blockWidth = Math.Max(
                    isPrimary ? blockWidthPrimary : blockWidthSecondary,
                    Math.Ceiling(valueWidth) + padding * 2 + blockWidthReserve);
                total += blockWidth + unitWidth + 2;
                if (index < pairs.Count - 1)
                {
                    total += gap;
                }
            }

            return total;
        }

        double valueFontSize = isPrimary ? valueFontPrimary : valueFontSecondary;
        double blockPadding = 4;
        double pairGap = 7;

        if (RequiredWidth(valueFontSize, blockPadding, pairGap) > availableWidth)
        {
            blockPadding = 2;
        }

        if (RequiredWidth(valueFontSize, blockPadding, pairGap) > availableWidth)
        {
            // Preserve the primary 26 DIP hierarchy in the fixed 194 DIP slot
            // before applying the long-number font fit.
            pairGap = isPrimary ? 2 : 3;
        }

        if (isPrimary && RequiredWidth(KeyTextMinimumFontSize, blockPadding, pairGap) > availableWidth)
        {
            // Very long day counts must still fit at the readable font floor.
            // Use the remaining inner spacing before reducing the numerals;
            // block sizes, unit font and the fixed column stay unchanged.
            blockPadding = 0;
            pairGap = 0;
        }

        var required = RequiredWidth(valueFontSize, blockPadding, pairGap);
        while (required > availableWidth && valueFontSize > KeyTextMinimumFontSize)
        {
            var fittedFontSize = valueFontSize * (availableWidth / required);
            var nextFontSize = Math.Max(KeyTextMinimumFontSize, Math.Floor(fittedFontSize * 4) / 4);
            if (nextFontSize >= valueFontSize)
            {
                nextFontSize = Math.Max(KeyTextMinimumFontSize, valueFontSize - 0.25);
            }

            valueFontSize = nextFontSize;
            required = RequiredWidth(valueFontSize, blockPadding, pairGap);
        }

        // The loop intentionally stops at the 12 DIP contract floor. If the
        // fixed viewport still cannot fit, expose that failure to the
        // validation seam instead of silently claiming a compliant layout.
        var keyFontFloorSatisfied =
            required <= availableWidth && valueFontSize >= KeyTextMinimumFontSize;

        var blockBrushKey = isPrimary ? "PinkBlockBrush" : "SecondaryBlockBrush";
        var blockHeight = isPrimary ? blockHeightPrimary : blockHeightSecondary;
        var blockRadius = Math.Max(4, (isPrimary ? blockWidthPrimary : blockWidthSecondary) / 4);
        var unitFontSize = (double)FindResource("TypeCaptionFontSize");

        var pairsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            // Apply the width calculation's reserved inset to layout as well;
            // pixel rounding must not push the final unit outside the column.
            Margin = new Thickness(0, 0, rightInset, 0)
        };
        for (var index = 0; index < pairs.Count; index++)
        {
            var pair = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (index > 0)
            {
                pair.Margin = new Thickness(pairGap, 0, 0, 0);
            }

            var value = valueBlocks[index];
            value.FontSize = valueFontSize;
            value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var valueWidth = value.DesiredSize.Width;
            var block = new Border
            {
                Width = Math.Max(
                    isPrimary ? blockWidthPrimary : blockWidthSecondary,
                    Math.Ceiling(valueWidth) + blockPadding * 2 + blockWidthReserve),
                Height = blockHeight,
                Padding = new Thickness(blockPadding, 0, blockPadding, 0),
                CornerRadius = new CornerRadius(blockRadius),
                Child = value
            };
            block.SetResourceReference(Border.BackgroundProperty, blockBrushKey);
            var unit = new TextBlock
            {
                Text = pairs[index].Unit,
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = unitFontSize,
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap
            };
            unit.SetResourceReference(TextBlock.ForegroundProperty, theme.MutedBrushKey);
            pair.Children.Add(block);
            pair.Children.Add(unit);
            pairsPanel.Children.Add(pair);

            if (fadeSeconds && string.Equals(pairs[index].Unit, "秒", StringComparison.Ordinal))
            {
                MotionBehavior.FadeCountdownDigit(value);
            }
        }

        var cluster = new Grid
        {
            Width = isPrimary ? PrimaryRemainingWidth : SecondaryRemainingWidth,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = RemainingBlocksTag
        };
        AutomationProperties.SetName(cluster, remaining);
        AutomationProperties.SetItemStatus(
            cluster,
            keyFontFloorSatisfied ? "KeyTextFontFloorSatisfied" : "KeyTextFontFloorExceeded");
        cluster.Children.Add(pairsPanel);
        return cluster;
    }

    // ValidationReport uses the same rendering seam for the expired fallback,
    // which is normally filtered out of the live countdown list.
    internal FrameworkElement BuildRemainingBlocksForValidation(
        string remaining,
        bool isPrimary) =>
        BuildRemainingBlocks(remaining, isPrimary);

    // #198/#212：秒位变化判定与每行的上次秒值簿记（按投影行 Key），重建路径与就地更新路径共用。
    private bool TrackSecondsChange(string rowKey, string remaining)
    {
        var currentSeconds = ParseRemainingPairs(remaining)?.FirstOrDefault(static pair =>
            string.Equals(pair.Unit, "秒", StringComparison.Ordinal))?.Value;
        var secondsChanged = currentSeconds is not null &&
            _lastCountdownSeconds.TryGetValue(rowKey, out var previousSeconds) &&
            !string.Equals(previousSeconds, currentSeconds, StringComparison.Ordinal);
        if (currentSeconds is null)
        {
            _lastCountdownSeconds.Remove(rowKey);
        }
        else
        {
            _lastCountdownSeconds[rowKey] = currentSeconds;
        }

        return secondsChanged;
    }

    private static IReadOnlyList<RemainingPair>? ParseRemainingPairs(string remaining)
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
        for (var index = 0; index + 1 < tokens.Length; index += 2)
        {
            if (!long.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
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

            if (unit != "天" || value != 0)
            {
                pairs.Add(new RemainingPair(tokens[index], unit));
            }
        }

        return pairs;
    }

    /// <summary>
    ///     测量单位字等无排版变体的文本宽度（数值宽度以真实 TextBlock 测量为准）。
    /// </summary>
    private static double MeasureText(string text, Typeface typeface, double fontSize, double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace + Math.Max(0, formatted.OverhangTrailing);
    }

    private TextBlock CountdownRemainingText(
        string text,
        double fontSize,
        FontWeight fontWeight)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap
        };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "NumeralFontFamily");
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    private static bool FitSingleLineText(TextBlock text, double availableWidth)
    {
        const double widthReserve = 2;
        const double minimumFontSize = KeyTextMinimumFontSize;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var requiredWidth = text.DesiredSize.Width;
        var usableWidth = availableWidth - widthReserve;
        if (requiredWidth > usableWidth)
        {
            var fittedFontSize = text.FontSize * (usableWidth / requiredWidth);
            text.FontSize = Math.Max(
                minimumFontSize,
                Math.Floor(fittedFontSize * 4) / 4);
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            requiredWidth = text.DesiredSize.Width;
        }

        var fits = requiredWidth <= usableWidth && text.FontSize >= minimumFontSize;
        AutomationProperties.SetItemStatus(
            text,
            fits ? "KeyTextFontFloorSatisfied" : "KeyTextFontFloorExceeded");
        return fits;
    }
}

internal sealed record RemainingPair(string Value, string Unit);
