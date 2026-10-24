using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MinminCountdown.Core;

namespace MinminCountdown;

public sealed partial class FloatingWindow
{
    // Current row origins are x=60 in the single day theme; card width 319 keeps x <= 396.
    // #143：复选框槽位与“…”触发器命中区按 36px 下限收紧（DESIGN §10 小尺寸命中区合同）。
    private const double CalendarIndexNoteCardWidth = 319;
    private const double CalendarIndexDateStampWidth = 48;
    private const double CalendarIndexRuleSpacing = 18;
    private const double CalendarIndexOverflowMenuWidth = 36;
    private const double CalendarIndexCheckboxSlotWidth = 36;

    // DESIGN §2 日期印记 pastel 轮换：粉 → 蓝 → 绿，按列表顺序循环。
    private static readonly string[][] CalendarIndexStampPalette =
    [
        ["StampPinkBrush", "StampPinkTextBrush"],
        ["StampBlueBrush", "StampBlueTextBrush"],
        ["StampGreenBrush", "StampGreenTextBrush"]
    ];

    // #149：当前渲染批次内每张便签卡的日期印记粘性位移入口（RenderRows 重建时清空重填）。
    private readonly List<FloatingDateStampStickyEntry> _noteDateStampStickyEntries = new();

    internal sealed record FloatingDateStampStickyEntry(
        Border Card,
        Border StampBorder,
        StackPanel StampContent,
        TranslateTransform Translate);

    private FrameworkElement BuildCalendarIndexNoteCard(NoteRecord note, DateTimeOffset nowUtc, int rowIndex)
    {
        var theme = ActiveTheme;
        var noteError = new TextBlock
        {
            FontSize = (double)FindResource("TypeCaptionFontSize"),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        noteError.SetResourceReference(TextBlock.ForegroundProperty, theme.ErrorBrushKey);
        UiAccessibility.MarkLive(noteError);

        var completion = UiAccessibility.Name(
            new CheckBox
            {
                Tag = note.Id,
                Style = (Style)FindResource("CompletionCheckBox"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, 1, 0)
            },
            $"完成便签 {note.Text}");
        completion.SetResourceReference(Control.ForegroundProperty, theme.TextBrushKey);
        completion.SetValue(NoteControlRoleProperty, NoteControlRoleCompletion);
        if (_noteCardErrors.TryGetValue(note.Id, out var savedError))
        {
            SetNoteCardError(noteError, completion, savedError);
        }

        completion.Checked += (_, _) =>
        {
            var card = _noteDateStampStickyEntries.Select(entry => entry.Card)
                .FirstOrDefault(candidate => candidate.Child is Grid grid && grid.Children.Contains(completion));
            var exit = NoteCompletionExit.Capture(card, FloatingBodyScroller);
            var result = _desktopWorkspace.ApplyWithUndo(
                new SetNoteCompletionChange(note.Id, true),
                WorkspaceUndoSurface.Floating);
            if (!result.Succeeded)
            {
                exit?.Dispose();
                completion.IsChecked = false;
                _noteCardErrors[note.Id] = result.Error;
                SetNoteCardError(noteError, completion, result.Error);
                SetNoteMessage(result.Error);
                completion.Focus();
            }
            else
            {
                _noteCompletionExit = exit;
                exit?.Start();
                var previousError = noteError.Text;
                _noteCardErrors.Remove(note.Id);
                SetNoteCardError(noteError, completion, string.Empty);
                if (string.Equals(FloatingNoteMessage.Text, previousError, StringComparison.Ordinal))
                {
                    SetNoteMessage(string.Empty);
                }
            }
        };

        var body = new StackPanel
        {
            Margin = new Thickness(0, 9, 8, 9)
        };
        var noteText = new TextBlock
        {
            Text = note.Text,
            FontSize = (double)FindResource("TypeCaptionFontSize"),
            LineHeight = 18,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap
        };
        noteText.SetResourceReference(TextBlock.ForegroundProperty, theme.TextBrushKey);
        body.Children.Add(noteText);

        var nextReminder = ReminderSchedule.GetNextDueUtc(
            _desktopWorkspace.Snapshot.Settings,
            note,
            nowUtc);
        if (nextReminder is not null)
        {
            var reminderText = new TextBlock
            {
                Text = $"下次提醒：{DateDisplay.Deadline(nextReminder.Value)}",
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = (double)FindResource("TypeCaptionFontSize"),
                LineHeight = 17,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextWrapping = TextWrapping.Wrap
            };
            reminderText.SetResourceReference(TextBlock.ForegroundProperty, theme.MutedBrushKey);
            body.Children.Add(reminderText);
        }

        body.Children.Add(noteError);

        var layout = new Grid
        {
            Background = CreateCalendarIndexRuleBrush(ThemeBrush(theme.BorderBrushKey)),
            ClipToBounds = true
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CalendarIndexCheckboxSlotWidth) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(CalendarIndexDateStampWidth)
        });
        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(CalendarIndexOverflowMenuWidth)
        });

        var overflowMenu = BuildNoteOverflowMenu(note);
        var dateStamp = CreateCalendarIndexDateStamp(note, rowIndex, out var stampTranslate);
        Grid.SetColumn(body, 1);
        Grid.SetColumn(dateStamp, 2);
        Grid.SetColumn(overflowMenu, 3);
        layout.Children.Add(completion);
        layout.Children.Add(body);
        layout.Children.Add(overflowMenu);
        layout.Children.Add(dateStamp);

        // DESIGN §10：便签卡底按条目在暖黄与白纸之间交替，形成条带节奏。
        var card = new Border
        {
            Child = layout,
            Width = CalendarIndexNoteCardWidth,
            MinHeight = 72,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        card.SetResourceReference(Border.BorderBrushProperty, theme.BorderBrushKey);
        card.PreviewKeyDown += (_, args) =>
        {
            if (args.Handled || args.Key != Key.Delete ||
                MainShortcutInput.ResolveModifiers(args) != ModifierKeys.None ||
                !IsEnabled || FloatingTags.IsPopupOpen || FloatingNoteDate.IsDropDownOpen ||
                NoteRowOverflowMenuBehavior.GetPopup(overflowMenu)?.IsOpen == true) return;
            args.Handled = true;
            RequestNoteDelete(note);
        };
        card.SetResourceReference(
            Border.BackgroundProperty,
            rowIndex % 2 == 0 ? theme.NoteCardBrushKey : "NoteWhiteBrush");

        _noteDateStampStickyEntries.Add(
            new FloatingDateStampStickyEntry(
                card,
                dateStamp,
                (StackPanel)dateStamp.Child,
                stampTranslate));

        return card;
    }

    /// <summary>
    ///     #149：长便签被视口裁切时，日期印记三行文字在「卡片可见区域」内居中（粘性），
    ///     「月 / 日 / 周X」始终整体可见，不再只剩「日 / 周」；完整可见的卡片保持自然居中（位移 0）。
    ///     印记 pastel 背景列仍铺满卡片高度，仅文字位移；印记继续不可命中、不可聚焦。
    /// </summary>
    internal void UpdateNoteDateStampStickyOffsets()
    {
        if (_workspace != WorkspaceKind.Notes || FloatingBodyScroller.ViewportHeight <= 0)
        {
            return;
        }

        foreach (var entry in _noteDateStampStickyEntries)
        {
            if (entry.StampBorder.ActualHeight <= 0 ||
                entry.StampContent.ActualHeight <= 0 ||
                entry.Card.ActualHeight <= 0)
            {
                continue;
            }

            var cardTop = entry.Card
                .TransformToVisual(FloatingBodyScroller)
                .Transform(new Point(0, 0))
                .Y;
            var visibleTop = Math.Max(0, -cardTop);
            var visibleBottom = Math.Min(
                entry.Card.ActualHeight,
                FloatingBodyScroller.ViewportHeight - cardTop);
            var stampHeight = entry.StampContent.ActualHeight;
            var columnHeight = entry.StampBorder.ActualHeight;
            var centeredTop = (columnHeight - stampHeight) / 2;
            double stickyTop;
            if (visibleBottom <= visibleTop)
            {
                // 卡片完全在视口外：回到自然居中，滚动后由 ScrollChanged 再次修正。
                stickyTop = centeredTop;
            }
            else
            {
                var desiredTop = visibleTop +
                    Math.Max(0, (visibleBottom - visibleTop - stampHeight) / 2);
                stickyTop = Math.Clamp(desiredTop, 0, Math.Max(0, columnHeight - stampHeight));
            }

            entry.Translate.Y = stickyTop - centeredTop;
        }
    }

    internal void ClearNoteDateStampStickyEntries() => _noteDateStampStickyEntries.Clear();

    /// <summary>
    ///     每条便签右侧的“…”菜单：提供编辑/删除，编辑复用主窗口编辑流程（悬浮窗首次获得编辑能力）。
    /// </summary>
    private Button BuildNoteOverflowMenu(NoteRecord note)
    {
        var trigger = UiAccessibility.Name(
            new Button
            {
                Tag = note.Id,
                Width = CalendarIndexOverflowMenuWidth,
                Height = CalendarIndexOverflowMenuWidth,
                MinWidth = CalendarIndexOverflowMenuWidth,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FocusVisualStyle = null,
                Style = (Style)FindResource("FloatingHeaderIconButton"),
                Content = new TextBlock
                {
                    Text = "…",
                    FontSize = (double)FindResource("TypeCaptionFontSize"),
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            },
            $"更多操作：编辑或删除便签 {note.Text}");
        trigger.SetResourceReference(Control.ForegroundProperty, ActiveTheme.MutedBrushKey);
        trigger.SetValue(NoteControlRoleProperty, NoteControlRoleOverflow);

        var menuEdit = UiAccessibility.Name(
            new Button
            {
                Content = "编辑",
                Tag = note.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2),
                Style = (Style)FindResource("SecondaryButton")
            },
            $"编辑便签 {note.Text}");
        var menuDelete = UiAccessibility.Name(
            new Button
            {
                Content = "删除",
                Tag = note.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2),
                Style = (Style)FindResource("DangerButton")
            },
            $"删除便签 {note.Text}");

        var items = new StackPanel { Margin = new Thickness(4) };
        items.Children.Add(menuEdit);
        items.Children.Add(menuDelete);
        var surface = new Border
        {
            MinWidth = 132,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = items
        };
        surface.SetResourceReference(Border.BackgroundProperty, "CanvasBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "PeachLineBrush");
        var popup = new Popup
        {
            PlacementTarget = trigger,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.None,
            Child = surface
        };
        NoteRowOverflowMenuBehavior.SetPopup(trigger, popup);
        // #149：接入与筛选/标签选择器同一套弹层焦点生命周期——打开聚焦首项（编辑）、
        // Esc 关闭并归还触发器、层内 Tab 循环，键盘可走完「编辑 / 删除」。
        PopupFocusLifecycle.Attach(popup, trigger, () => menuEdit);
        trigger.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        menuEdit.Click += (_, _) => popup.IsOpen = false;
        menuEdit.Click += (_, _) => RequestNoteEdit(note);
        menuDelete.Click += (_, _) => popup.IsOpen = false;
        menuDelete.Click += (_, _) => RequestNoteDelete(note);
        return trigger;
    }

    private void RequestNoteEdit(NoteRecord note)
    {
        ManagerWindowLifecycle.RevealNoteEditor(_manager, _desktopWorkspace, note.Id);
    }

    private void RequestNoteDelete(NoteRecord note)
    {
        if (new ConfirmationDialog(
                this,
                "删除便签",
                DeleteConfirmationCopy.For(note.Text)).ShowDialog() != true)
        {
            return;
        }

        // #214：撤销留在发起删除的悬浮窗，不转交管理窗口。
        var result = _desktopWorkspace.ApplyWithUndo(new DeleteNoteChange(note.Id), WorkspaceUndoSurface.Floating);
        if (!result.Succeeded)
        {
            SetNoteMessage($"删除失败：{result.Error}");
        }
    }

    private static void SetNoteCardError(TextBlock error, Control source, string message)
    {
        UiAccessibility.Announce(error, message, block =>
        {
            block.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
            AutomationProperties.SetHelpText(block, message);
            AutomationProperties.SetHelpText(source, message);
        });
    }

    private Border CreateCalendarIndexDateStamp(
        NoteRecord note,
        int rowIndex,
        out TranslateTransform stickyTranslate)
    {
        var date = CalendarIndexDate(note);
        var culture = CultureInfo.GetCultureInfo("zh-CN");
        var palette = CalendarIndexStampPalette[rowIndex % CalendarIndexStampPalette.Length];
        var stampText = palette[1];
        stickyTranslate = new TranslateTransform();
        var stamp = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = stickyTranslate
        };
        stamp.Children.Add(CalendarIndexStampText(
            date is null ? "无日期" : $"{date.Value.Month} 月",
            (double)FindResource("TypeCaptionFontSize"),
            FontWeights.Regular,
            stampText));
        stamp.Children.Add(CalendarIndexStampText(
            date is null ? "--" : date.Value.Day.ToString("D2", CultureInfo.InvariantCulture),
            (double)FindResource("TypeH2FontSize"),
            FontWeights.SemiBold,
            stampText,
            (FontFamily)FindResource("NumeralFontFamily"),
            new Thickness(0, 2, 0, 2)));
        stamp.Children.Add(CalendarIndexStampText(
            date is null
                ? " "
                : culture.DateTimeFormat.GetAbbreviatedDayName(date.Value.DayOfWeek),
            (double)FindResource("TypeCaptionFontSize"),
            FontWeights.Regular,
            stampText));

        var dateStamp = new Border
        {
            Child = stamp,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(4, 8, 4, 8),
            IsHitTestVisible = false,
            Focusable = false
        };
        dateStamp.SetResourceReference(Border.BackgroundProperty, palette[0]);
        ApplicationTheme.ApplyContrastAwareBrush(dateStamp, Border.BorderBrushProperty,
            CreateCalendarIndexStampEdgeBrush(ApplicationTheme.DayColorFor(palette[1])), "ControlBorderBrush");
        return dateStamp;
    }

    private static Brush CreateCalendarIndexStampEdgeBrush(Color dayText)
    {
        var edge = new SolidColorBrush(Color.FromArgb(96, dayText.R, dayText.G, dayText.B));
        edge.Freeze();
        return edge;
    }

    private static TextBlock CalendarIndexStampText(
        string text,
        double fontSize,
        FontWeight fontWeight,
        string foregroundKey,
        FontFamily? fontFamily = null,
        Thickness? margin = null)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        if (margin is { } value)
        {
            label.Margin = value;
        }

        if (fontFamily is not null)
        {
            label.FontFamily = fontFamily;
            Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        }

        return label;
    }

    private static DateTime? CalendarIndexDate(NoteRecord note)
    {
        if (note.Date is not null &&
            DateTime.TryParseExact(
                note.Date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed.Date;
        }

        return null;
    }

    // 横线是由主题线色合成的 DrawingBrush，不能引用资源键；配色替换后为现有卡片重建，卡片实例保留。
    private void RefreshCalendarIndexRuleBrushes()
    {
        var lineBrush = ThemeBrush(ActiveTheme.BorderBrushKey);
        foreach (var entry in _noteDateStampStickyEntries)
        {
            if (entry.Card.Child is Grid layout)
            {
                layout.Background = CreateCalendarIndexRuleBrush(lineBrush);
            }
        }
    }

    private static DrawingBrush CreateCalendarIndexRuleBrush(Brush lineBrush)
    {
        var tile = new Rect(
            0,
            0,
            CalendarIndexNoteCardWidth,
            CalendarIndexRuleSpacing);
        var rule = new GeometryDrawing(
            null,
            new Pen(lineBrush, 1),
            new LineGeometry(
                new Point(0, CalendarIndexRuleSpacing - 0.5),
                new Point(CalendarIndexNoteCardWidth, CalendarIndexRuleSpacing - 0.5)));
        return new DrawingBrush(rule)
        {
            Viewbox = tile,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = tile,
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            Opacity = 0.24
        };
    }
}
