using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown;

public sealed partial class FloatingWindow
{
    // Current row origins are x=60 in the single day theme; card width 319 keeps x <= 396.
    // #143：复选框槽位与“…”触发器命中区按 36px 下限收紧（DESIGN §10 小尺寸命中区合同）。
    // #335（方案 B）：移除右侧 48 DIP 印记列，改为正文列上缘 18 DIP 单行日期条（与正文间隔 4 DIP），正文可用宽 237 DIP。
    private const double CalendarIndexNoteCardWidth = 319;
    private const double CalendarIndexDateStripHeight = 18;
    private const double CalendarIndexDateStripBodyGap = 4;
    private const double CalendarIndexCardVerticalInset = 9;
    private const double CalendarIndexBodyRightMargin = 8;
    private const double CalendarIndexRuleSpacing = 18;
    private const double CalendarIndexOverflowMenuWidth = 36;
    private const double CalendarIndexCheckboxSlotWidth = 36;

    internal static readonly DependencyProperty IsNoteCardDateStampProperty =
        DependencyProperty.RegisterAttached(
            "IsNoteCardDateStamp",
            typeof(bool),
            typeof(FloatingWindow),
            new PropertyMetadata(false));

    internal static bool IsNoteCardDateStamp(Border border) =>
        (bool)border.GetValue(IsNoteCardDateStampProperty);

    // DESIGN §2 日期印记 pastel 轮换：粉 → 蓝 → 绿，按列表顺序循环。
    private static readonly string[][] CalendarIndexStampPalette =
    [
        ["StampPinkBrush", "StampPinkTextBrush"],
        ["StampBlueBrush", "StampBlueTextBrush"],
        ["StampGreenBrush", "StampGreenTextBrush"]
    ];

    // #149：当前渲染批次内每张便签卡的日期印记粘性位移入口（RenderRows 重建时清空重填）。
    private readonly List<FloatingDateStampStickyEntry> _noteDateStampStickyEntries = new();

    // #319：单一懒创建并跨卡复用的原生 ContextMenu，避免每张便签卡预创建弹层与按钮。
    private MotionContextMenu? _noteOverflowMenu;
    private MenuItem? _noteOverflowEditItem;
    private MenuItem? _noteOverflowDeleteItem;
    private NoteRecord? _activeOverflowNote;
    private Button? _activeOverflowTrigger;
    private bool _overflowOpenedByPointer;
    private bool _overflowEscapeClosePending;
    private bool _overflowFocusRestoredForCurrentOpen;
    private bool _reopeningNoteOverflowMenu;

    private bool IsNoteOverflowMenuOpen =>
        _noteOverflowMenu is { IsOpen: true };

    private bool IsNoteOverflowMenuOpenFor(Button trigger) =>
        _noteOverflowMenu is { IsOpen: true } &&
        ReferenceEquals(_activeOverflowTrigger, trigger);

    private bool IsNoteOverflowMenuActivelyOpenFor(Button trigger) =>
        IsNoteOverflowMenuOpenFor(trigger) &&
        !PopupMotionBehavior.IsContextMenuClosing(_noteOverflowMenu);

    internal sealed record FloatingDateStampStickyEntry(
        Border Card,
        Border StampBorder,
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
            Margin = new Thickness(
                0,
                CalendarIndexCardVerticalInset + CalendarIndexDateStripHeight + CalendarIndexDateStripBodyGap,
                CalendarIndexBodyRightMargin,
                CalendarIndexCardVerticalInset)
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

        var nextReminder = NextReminderUtc(
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
            Width = new GridLength(CalendarIndexOverflowMenuWidth)
        });

        var overflowMenu = BuildNoteOverflowMenu(note);
        var dateStamp = CreateCalendarIndexDateStamp(note, rowIndex, out var stampTranslate);
        Grid.SetColumn(body, 1);
        Grid.SetColumn(dateStamp, 1);
        Panel.SetZIndex(dateStamp, 1);
        Grid.SetColumn(overflowMenu, 2);
        layout.Children.Add(completion);
        layout.Children.Add(body);
        layout.Children.Add(overflowMenu);
        layout.Children.Add(dateStamp);

        // DESIGN §10：便签卡底按条目在暖黄与白纸之间交替，形成条带节奏；卡间距消费 SpacingUnit。
        var card = new Border
        {
            Child = layout,
            Width = CalendarIndexNoteCardWidth,
            MinHeight = 72,
            Margin = new Thickness(0, 0, 0, SpacingTokens.Resolve(this, SpacingTokens.Unit)),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        // #250：悬浮便签卡与主窗条目卡同用 §13.3 RadiusCard。
        card.SetResourceReference(Border.CornerRadiusProperty, "RadiusCard");
        NoteCompletionExit.SetCompletionIdentity(card, NoteCompletionExit.NoteIdentity(note.Id));
        card.SetResourceReference(Border.BorderBrushProperty, theme.BorderBrushKey);
        card.PreviewKeyDown += (_, args) =>
        {
            if (args.Handled || args.Key != Key.Delete ||
                MainShortcutInput.ResolveModifiers(args) != ModifierKeys.None ||
                !IsEnabled || FloatingTags.IsPopupOpen || FloatingNoteDate.IsDropDownOpen ||
                IsNoteOverflowMenuOpen) return;
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
                stampTranslate));

        return card;
    }

    // #240：可见便签「下次提醒」按便签缓存，渲染签名与便签卡共用同一份结果。缓存在以下最先发生者作废：
    // 整分钟翻转、到达该便签缓存的提醒时刻、任一工作区状态变化、节假日日历状态变化。
    private readonly Dictionary<string, CachedNextReminder> _nextReminderCache = new(StringComparer.Ordinal);
    private long _nextReminderCacheMinute = long.MinValue;

    private readonly record struct CachedNextReminder(DateTimeOffset? DueUtc, DateTimeOffset ComputedAtUtc);

    private DateTimeOffset? NextReminderUtc(CountdownSettings settings, NoteRecord note, DateTimeOffset nowUtc)
    {
        var minute = nowUtc.UtcTicks / TimeSpan.TicksPerMinute;
        if (minute != _nextReminderCacheMinute)
        {
            _nextReminderCache.Clear();
            _nextReminderCacheMinute = minute;
        }

        // 计算时已过去的提醒时刻（未处理的一次性或日历提醒）不再「到达」，只随状态变化或整分钟翻转更新。
        if (_nextReminderCache.TryGetValue(note.Id, out var cached) &&
            cached.ComputedAtUtc <= nowUtc &&
            (cached.DueUtc is not { } dueUtc || dueUtc <= cached.ComputedAtUtc || nowUtc < dueUtc))
        {
            return cached.DueUtc;
        }

        FloatingNextReminderComputationCountForValidation++;
        var nextReminder = ReminderSchedule.GetNextDueUtc(settings, note, nowUtc);
        _nextReminderCache[note.Id] = new CachedNextReminder(nextReminder, nowUtc);
        return nextReminder;
    }

    private void RefreshAfterStateChange()
    {
        _nextReminderCache.Clear();
        Refresh();
    }

    /// <summary>
    ///     #149/#335（方案 B）：长便签顶部滚出视口时，上缘单行日期条在卡片可见条带内粘性置顶，
    ///     始终保持完整可读且不越出卡片底边、不覆盖搜索槽或底部命令栏；未滚出顶部的卡片位移归零并保持 4 DIP 正文间距。
    ///     日期条继续不可命中、不可聚焦。
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
                entry.Card.ActualHeight <= 0 ||
                VisualTreeHelper.GetParent(entry.Card) is not Visual cardParent)
            {
                continue;
            }

            // #243：按卡片布局位置计算，不含完成补位的过渡 RenderTransform，补位结束时印记即在终态位置。
            var cardTop = cardParent
                .TransformToVisual(FloatingBodyScroller)
                .Transform((Point)VisualTreeHelper.GetOffset(entry.Card))
                .Y;
            var visibleTop = Math.Max(0, -cardTop);
            var visibleBottom = Math.Min(
                entry.Card.ActualHeight,
                FloatingBodyScroller.ViewportHeight - cardTop);
            var stampHeight = entry.StampBorder.ActualHeight;
            var stampLayoutTop = VisualTreeHelper.GetOffset(entry.StampBorder).Y;
            var layoutOffsetTop = entry.Card.Child is Visual layoutVisual
                ? VisualTreeHelper.GetOffset(layoutVisual).Y
                : entry.Card.BorderThickness.Top;
            var naturalTop = layoutOffsetTop + stampLayoutTop;
            var maxTop = Math.Max(
                naturalTop,
                entry.Card.ActualHeight - entry.Card.BorderThickness.Bottom - CalendarIndexCardVerticalInset - stampHeight);
            double stickyTop;
            if (visibleBottom <= visibleTop || visibleTop <= naturalTop)
            {
                // 卡片完全在视口外或自然顶边仍在视口内：位移归零，保持精确 4 DIP 静止间距。
                stickyTop = naturalTop;
            }
            else
            {
                var desiredTop = Math.Min(
                    visibleTop,
                    Math.Max(naturalTop, visibleBottom - stampHeight));
                stickyTop = Math.Clamp(desiredTop, naturalTop, maxTop);
            }

            entry.Translate.Y = stickyTop - naturalTop;
        }
    }

    internal void ClearNoteDateStampStickyEntries() => _noteDateStampStickyEntries.Clear();

    /// <summary>
    ///     #319：每条便签右侧的“…”按钮不再预创建弹层与菜单按钮，仅在首次打开时懒创建并复用
    ///     共享暖粉原生 ContextMenu / MenuItem（#309）。
    /// </summary>
    private Button BuildNoteOverflowMenu(NoteRecord note)
    {
        var overflowIcon = new WpfPath
        {
            Width = 16,
            Height = 16,
            Data = IconGeometries.Resolve(this, IconGeometries.OverflowDots),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        overflowIcon.SetBinding(
            System.Windows.Shapes.Shape.StrokeProperty,
            new System.Windows.Data.Binding(nameof(Control.Foreground))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(
                    System.Windows.Data.RelativeSourceMode.FindAncestor,
                    typeof(Button),
                    1)
            });

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
                Content = overflowIcon
            },
            $"更多操作：编辑或删除便签 {note.Text}");
        trigger.SetResourceReference(Control.ForegroundProperty, ActiveTheme.MutedBrushKey);
        trigger.SetValue(NoteControlRoleProperty, NoteControlRoleOverflow);

        var pointerOpening = false;
        var suppressNextClickReopen = false;

        trigger.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (IsNoteOverflowMenuActivelyOpenFor(trigger))
            {
                suppressNextClickReopen = true;
                pointerOpening = false;
            }
            else
            {
                pointerOpening = true;
                suppressNextClickReopen = false;
            }
        };
        trigger.PreviewKeyDown += (_, args) =>
        {
            pointerOpening = false;
            suppressNextClickReopen = false;
            if (args.Key == Key.Escape && IsNoteOverflowMenuOpenFor(trigger))
            {
                CloseNoteOverflowMenu(immediate: true);
                RestoreNoteOverflowTriggerFocus(trigger);
                args.Handled = true;
            }
        };
        trigger.LostKeyboardFocus += (_, _) => pointerOpening = false;
        trigger.Unloaded += (_, _) => pointerOpening = false;
        trigger.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Down &&
                MainShortcutInput.ResolveModifiers(args) == ModifierKeys.None &&
                !IsNoteOverflowMenuActivelyOpenFor(trigger))
            {
                pointerOpening = false;
                suppressNextClickReopen = false;
                args.Handled = true;
                OpenNoteOverflowMenu(note, trigger, immediate: true, openedByPointer: false);
            }
        };
        trigger.Click += (_, _) =>
        {
            if (suppressNextClickReopen)
            {
                suppressNextClickReopen = false;
                CloseNoteOverflowMenu(immediate: false);
                return;
            }

            var openedByPointer = pointerOpening;
            pointerOpening = false;
            if (IsNoteOverflowMenuActivelyOpenFor(trigger))
            {
                CloseNoteOverflowMenu(immediate: !openedByPointer);
                return;
            }

            OpenNoteOverflowMenu(note, trigger, immediate: !openedByPointer, openedByPointer: openedByPointer);
        };
        trigger.ContextMenuOpening += (_, args) =>
        {
            args.Handled = true;
            OpenNoteOverflowMenu(note, trigger, immediate: false, openedByPointer: true);
        };
        return trigger;
    }

    private MotionContextMenu EnsureNoteOverflowMenu()
    {
        if (_noteOverflowMenu is not null)
        {
            return _noteOverflowMenu;
        }

        var menu = new MotionContextMenu
        {
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4
        };
        SharedMenuStyles.Apply(menu);
        // 交由 PopupMotionBehavior 统一驱动进入/退出动效，避免叠加 WPF 自带菜单动画。
        menu.Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;
        PopupMotionBehavior.AttachContextMenu(menu);
        AutomationProperties.SetAutomationId(menu, "FloatingNoteOverflowMenu");

        var editItem = new MenuItem { Header = "编辑" };
        AutomationProperties.SetAutomationId(editItem, "FloatingNoteOverflowMenuEdit");
        var deleteItem = new MenuItem { Header = "删除" };
        AutomationProperties.SetAutomationId(deleteItem, "FloatingNoteOverflowMenuDelete");
        SharedMenuStyles.ApplyDangerItemStyle(deleteItem, menu);

        WireOverflowMenuItemSpaceActivation(editItem);
        WireOverflowMenuItemSpaceActivation(deleteItem);

        editItem.Click += (_, _) =>
        {
            var note = _activeOverflowNote;
            CloseNoteOverflowMenu(immediate: true);
            if (note is not null)
            {
                RequestNoteEdit(note);
            }
        };
        deleteItem.Click += (_, _) =>
        {
            var note = _activeOverflowNote;
            CloseNoteOverflowMenu(immediate: true);
            if (note is not null)
            {
                RequestNoteDelete(note);
            }
        };

        menu.Items.Add(editItem);
        menu.Items.Add(deleteItem);

        menu.Opened += (_, _) =>
        {
            if (menu.Parent is Popup parentPopup)
            {
                parentPopup.PopupAnimation = PopupAnimation.None;
            }

            _overflowEscapeClosePending = false;
            _overflowFocusRestoredForCurrentOpen = false;
            if (!_overflowOpenedByPointer)
            {
                Keyboard.Focus(editItem);
                menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (menu.IsOpen &&
                        !PopupMotionBehavior.IsContextMenuClosing(menu) &&
                        !_overflowOpenedByPointer &&
                        !menu.IsKeyboardFocusWithin)
                    {
                        Keyboard.Focus(editItem);
                    }
                }));
            }
            else
            {
                Keyboard.Focus(menu);
            }
        };
        menu.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                _overflowEscapeClosePending = true;
                args.Handled = true;
                var triggerToRestore = _activeOverflowTrigger;
                CloseNoteOverflowMenu(immediate: true);
                RestoreNoteOverflowTriggerFocus(triggerToRestore);
            }
        };
        menu.AddHandler(
            Keyboard.KeyDownEvent,
            new KeyEventHandler((_, args) =>
            {
                if (args.Key != Key.Escape)
                {
                    return;
                }

                var triggerToRestore = _activeOverflowTrigger;
                if (menu.IsOpen)
                {
                    _overflowEscapeClosePending = true;
                    args.Handled = true;
                    CloseNoteOverflowMenu(immediate: true);
                }

                RestoreNoteOverflowTriggerFocus(triggerToRestore);
            }),
            handledEventsToo: true);
        menu.AddHandler(
            Mouse.PreviewMouseDownOutsideCapturedElementEvent,
            new MouseButtonEventHandler((_, _) =>
            {
                if (menu.IsOpen && !PopupMotionBehavior.IsContextMenuClosing(menu))
                {
                    CloseNoteOverflowMenu(immediate: false);
                }
            }),
            handledEventsToo: true);
        menu.Closed += (_, _) =>
        {
            if (_reopeningNoteOverflowMenu || menu.IsOpen)
            {
                return;
            }

            var triggerToRestore = _activeOverflowTrigger;
            _activeOverflowNote = null;
            _activeOverflowTrigger = null;
            menu.ClearValue(ContextMenu.PlacementTargetProperty);
            if (_overflowEscapeClosePending)
            {
                RestoreNoteOverflowTriggerFocus(triggerToRestore);
            }
        };

        _noteOverflowMenu = menu;
        _noteOverflowEditItem = editItem;
        _noteOverflowDeleteItem = deleteItem;
        return menu;
    }

    private static void WireOverflowMenuItemSpaceActivation(MenuItem item)
    {
        // 原生 WPF MenuItem 默认仅处理 Enter 激活；按 #319 合同在 KeyDown 补齐空格键激活（与 Enter 同阶段）。
        item.KeyDown += (_, args) =>
        {
            if (args.Handled || args.Key != Key.Space || args.IsRepeat ||
                MainShortcutInput.ResolveModifiers(args) != ModifierKeys.None)
            {
                return;
            }

            args.Handled = true;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
        };
    }

    private void OpenNoteOverflowMenu(NoteRecord note, Button trigger, bool immediate, bool openedByPointer)
    {
        var menu = EnsureNoteOverflowMenu();
        if (menu.IsOpen && !ReferenceEquals(_activeOverflowTrigger, trigger))
        {
            CloseNoteOverflowMenu(immediate: true);
        }

        _activeOverflowNote = note;
        _activeOverflowTrigger = trigger;
        _overflowOpenedByPointer = openedByPointer;
        _overflowEscapeClosePending = false;
        _overflowFocusRestoredForCurrentOpen = false;

        AutomationProperties.SetName(menu, $"更多操作：编辑或删除便签 {note.Text}");
        AutomationProperties.SetName(_noteOverflowEditItem!, $"编辑便签 {note.Text}");
        AutomationProperties.SetName(_noteOverflowDeleteItem!, $"删除便签 {note.Text}");
        _noteOverflowEditItem!.Tag = note.Id;
        _noteOverflowDeleteItem!.Tag = note.Id;

        menu.PlacementTarget = trigger;
        menu.Placement = PlacementMode.Bottom;
        menu.VerticalOffset = 4;
        if (!IsActive)
        {
            Activate();
        }

        _reopeningNoteOverflowMenu = true;
        try
        {
            PopupMotionBehavior.OpenContextMenu(menu, immediate: immediate);
        }
        finally
        {
            _reopeningNoteOverflowMenu = false;
        }
    }

    private void CloseNoteOverflowMenu(bool immediate)
    {
        if (_noteOverflowMenu is not { } menu)
        {
            return;
        }

        if (menu.IsOpen)
        {
            PopupMotionBehavior.CloseContextMenu(menu, immediate: immediate);
        }

        if (!menu.IsOpen)
        {
            _activeOverflowNote = null;
            _activeOverflowTrigger = null;
            menu.ClearValue(ContextMenu.PlacementTargetProperty);
        }
    }

    private void RestoreNoteOverflowTriggerFocus(Button? trigger)
    {
        if (_overflowFocusRestoredForCurrentOpen ||
            trigger is null ||
            !trigger.IsEnabled ||
            !trigger.IsVisible ||
            !IsVisible)
        {
            return;
        }

        _overflowFocusRestoredForCurrentOpen = true;
        _overflowEscapeClosePending = false;
        Activate();
        FocusManager.SetFocusedElement(this, trigger);
        trigger.Focus();
        Keyboard.Focus(trigger);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_noteOverflowMenu is not { IsOpen: true } &&
                trigger.IsEnabled &&
                trigger.IsVisible &&
                IsVisible &&
                !ReferenceEquals(Keyboard.FocusedElement, trigger))
            {
                Activate();
                FocusManager.SetFocusedElement(this, trigger);
                trigger.Focus();
                Keyboard.Focus(trigger);
            }
        }));
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
        stickyTranslate = new TranslateTransform();

        var label = new TextBlock
        {
            Text = date is null
                ? "无日期"
                : $"{date.Value.Month} 月 {date.Value.Day} 日 · {culture.DateTimeFormat.GetAbbreviatedDayName(date.Value.DayOfWeek)}",
            FontFamily = (FontFamily)FindResource("UiFontFamily"),
            FontSize = (double)FindResource("TypeCaptionFontSize"),
            FontWeight = FontWeights.Regular,
            LineHeight = CalendarIndexDateStripHeight - 2,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        label.SetResourceReference(TextBlock.ForegroundProperty, palette[1]);

        var dateStamp = new Border
        {
            Child = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Height = CalendarIndexDateStripHeight,
            Margin = new Thickness(0, CalendarIndexCardVerticalInset, CalendarIndexBodyRightMargin, 0),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 0, 7, 0),
            IsHitTestVisible = false,
            Focusable = false,
            RenderTransform = stickyTranslate
        };
        dateStamp.SetValue(IsNoteCardDateStampProperty, true);
        dateStamp.SetResourceReference(Border.CornerRadiusProperty, "RadiusChip");
        dateStamp.SetResourceReference(Border.BackgroundProperty, palette[0]);
        var dayBorderBrush = new SolidColorBrush(ApplicationTheme.DayColorFor(palette[0]));
        dayBorderBrush.Freeze();
        ApplicationTheme.ApplyContrastAwareBrush(
            dateStamp,
            Border.BorderBrushProperty,
            dayBorderBrush,
            "ControlBorderBrush");
        return dateStamp;
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
