using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #319：悬浮窗便签「…」菜单改用懒创建并复用的共享暖粉原生 ContextMenu / MenuItem（#309），
// 验证零预创建弹层与按钮、原生 Menu/MenuItem 自动化角色与名称、高对比、方向键/Enter/Space/Esc/点外部、
// 确认与 10 秒撤销、卡片 Delete 行为不变，以及指针动效、键盘即时与减弱动画即时。
internal static class FloatingNoteOverflowMenuScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 10, 1, 0, 0, TimeSpan.Zero);

    private const string FirstNoteText = "整理周报并发送";
    private const string SecondNoteText = "预约下周牙医";
    private const string ThirdNoteText = "确认发版清单";

    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;

    private static string? _captureDirectory;
    private static bool _captureDirectoryInitialized;

    internal static (bool Passed, string Explanation) RunNativeAndLazy(ApplicationTheme theme, string meaning)
    {
        var failures = new List<string>();
        var captureDirectory = GetCaptureDirectory();
        RunWithDualWindows("native-lazy", (workspace, main, floating) =>
        {
            var note1Id = SaveNote(workspace, FirstNoteText, floating.Dispatcher);
            var note2Id = SaveNote(workspace, SecondNoteText, floating.Dispatcher);
            _ = SaveNote(workspace, ThirdNoteText, floating.Dispatcher);

            floating.ShowWorkspace(WorkspaceKind.Notes);
            Settle(floating);
            ActivateForKeyboard(floating);

            var rows = RequiredByName<StackPanel>(floating, "FloatingRows");
            var triggers = FindOverflowTriggers(rows);
            Require(triggers.Count == 3, $"期望 3 张便签卡各有 1 个「…」触发器，实际找到 {triggers.Count} 个。");

            // 1. 静态零预创建：便签卡视觉树、逻辑树与本地属性均不得预创建 Popup、ContextMenu、MenuItem 或编辑/删除按钮。
            Check(failures, "便签列表视觉树静态不含 Popup", !VisualDescendants<Popup>(rows).Any());
            Check(failures, "便签列表视觉树静态不含编辑或删除按钮",
                !VisualDescendants<Button>(rows).Any(button =>
                    string.Equals(button.Content as string, "编辑", StringComparison.Ordinal) ||
                    string.Equals(button.Content as string, "删除", StringComparison.Ordinal)));
            Check(failures, "静态打开的 ContextMenu 为零", FindOpenContextMenu() is null);

            foreach (var trigger in triggers)
            {
                Check(failures, $"触发器「{AutomationProperties.GetName(trigger)}」静态未预挂 ContextMenu",
                    trigger.ContextMenu is null);
                Check(failures, $"触发器「{AutomationProperties.GetName(trigger)}」本地属性不含预创建 Popup/ContextMenu/MenuItem",
                    !HasAttachedPopupOrMenuValue(trigger));
            }

            foreach (var card in rows.Children.OfType<FrameworkElement>())
            {
                Check(failures, "便签卡本地属性及子树不含预创建 Popup/ContextMenu/MenuItem",
                    !CardSubtreeHasPrecreatedMenuOrPopup(card));
            }

            var trigger1 = FindTriggerForNote(rows, note1Id, FirstNoteText);
            var trigger2 = FindTriggerForNote(rows, note2Id, SecondNoteText);

            // 2. 首次打开懒创建原生 ContextMenu，套用 SharedMenuStyles，具备原生 Menu/MenuItem 角色与动作+对象名称。
            Require(Keyboard.Focus(trigger1) == trigger1, "无法聚焦第一张便签卡的「…」触发器。");
            Settle(floating);
            WindowValidationInput.RaiseKey(trigger1, Key.Enter);
            Settle(floating);

            var menu1 = FindOpenContextMenu();
            Require(menu1 is { IsOpen: true }, "按 Enter 打开「…」按钮后未出现原生 ContextMenu。");
            ContextMenu menu = menu1!;
            IDisposable? menuInput = null;
            try
            {
                var items = menu.Items.OfType<MenuItem>().ToArray();
                Require(items.Length == 2 &&
                        string.Equals(items[0].Header as string, "编辑", StringComparison.Ordinal) &&
                        string.Equals(items[1].Header as string, "删除", StringComparison.Ordinal),
                    $"原生菜单项应依次为「编辑」「删除」，实际=[{string.Join(", ", items.Select(static i => i.Header))}]。");

                var editItem = items[0];
                var deleteItem = items[1];

                // 自动化角色与名称（包含动作与便签对象）。
                var menuPeer = UIElementAutomationPeer.CreatePeerForElement(menu);
                var editPeer = UIElementAutomationPeer.CreatePeerForElement(editItem);
                var deletePeer = UIElementAutomationPeer.CreatePeerForElement(deleteItem);
                Check(failures, "菜单自动化角色为 Menu",
                    menuPeer is not null && menuPeer.GetAutomationControlType() == AutomationControlType.Menu);
                Check(failures, "菜单自动化名称包含动作与便签对象",
                    menuPeer is not null &&
                    menuPeer.GetName().Contains("编辑", StringComparison.Ordinal) &&
                    menuPeer.GetName().Contains("删除", StringComparison.Ordinal) &&
                    menuPeer.GetName().Contains(FirstNoteText, StringComparison.Ordinal));
                Check(failures, "「编辑」菜单项自动化角色为 MenuItem 且名称为「编辑便签 …」",
                    editPeer is not null &&
                    editPeer.GetAutomationControlType() == AutomationControlType.MenuItem &&
                    string.Equals(editPeer.GetName(), $"编辑便签 {FirstNoteText}", StringComparison.Ordinal));
                Check(failures, "「删除」菜单项自动化角色为 MenuItem 且名称为「删除便签 …」",
                    deletePeer is not null &&
                    deletePeer.GetAutomationControlType() == AutomationControlType.MenuItem &&
                    string.Equals(deletePeer.GetName(), $"删除便签 {FirstNoteText}", StringComparison.Ordinal));

                // 共享暖粉样式 token。
                menu.ApplyTemplate();
                editItem.ApplyTemplate();
                deleteItem.ApplyTemplate();
                var surfaceBorder = VisualDescendants<Border>(menu).FirstOrDefault();
                var expectedOverlayRadius = (CornerRadius)Application.Current.FindResource("RadiusOverlay");
                var expectedBodySize = (double)Application.Current.FindResource("TypeBodyFontSize");
                var expectedFontSource = ((FontFamily)Application.Current.FindResource("UiFontFamily")).Source;
                Check(failures, "菜单使用 SharedMenuStyles 表面与边界画刷",
                    BrushColorEquals(menu.Background, "CanvasBrush") &&
                    BrushColorEquals(menu.BorderBrush, "ControlBorderBrush"));
                Check(failures, "菜单表面圆角消费 RadiusOverlay",
                    surfaceBorder is not null && surfaceBorder.CornerRadius == expectedOverlayRadius);
                Check(failures, "菜单项消费共享字体、字号且不挂系统虚线 FocusVisualStyle",
                    items.All(item =>
                        Math.Abs(item.FontSize - expectedBodySize) < 0.01 &&
                        string.Equals(item.FontFamily.Source, expectedFontSource, StringComparison.Ordinal) &&
                        item.FocusVisualStyle is null));

                menuInput = WindowValidationInput.AcquireContextMenuInputForValidation(menu);
                if (!editItem.IsKeyboardFocused)
                {
                    Keyboard.Focus(menu);
                    Settle(floating);
                    WindowValidationInput.RaiseKey(menu, Key.Down);
                    Settle(floating);
                }

                Check(failures, "首项「编辑」获焦高亮使用 MainActionBrush / MainActionTextBrush",
                    editItem.IsKeyboardFocused &&
                    editItem.IsHighlighted &&
                    BrushColorEquals(editItem.Background, "MainActionBrush") &&
                    BrushColorEquals(editItem.Foreground, "MainActionTextBrush"));
                Check(failures, "次项「删除」未高亮时使用 DangerBrush 语义色且底色为 CanvasBrush",
                    !deleteItem.IsHighlighted &&
                    BrushColorEquals(deleteItem.Background, "CanvasBrush") &&
                    BrushColorEquals(deleteItem.Foreground, "DangerBrush"));

                CaptureMenuVisual(menu, captureDirectory, "floating-note-overflow-menu-day-open", isHighContrast: false);

                // 高对比度打开期间实时切换并拍图。
                var dayBackgroundColor = ((SolidColorBrush)menu.Background).Color;
                using (ApplicationTheme.OverrideHighContrastForValidation(true))
                {
                    Settle(floating);
                    Check(failures, "打开期间跟随系统高对比度切换表面与高亮系统色",
                        menu.Background is SolidColorBrush hcBg && hcBg.Color == SystemColors.WindowColor &&
                        editItem.Background is SolidColorBrush hcHighlight && hcHighlight.Color == SystemColors.HighlightColor &&
                        editItem.Foreground is SolidColorBrush hcHighlightText && hcHighlightText.Color == SystemColors.HighlightTextColor &&
                        deleteItem.Foreground is SolidColorBrush hcText && hcText.Color == SystemColors.WindowTextColor);
                    CaptureMenuVisual(menu, captureDirectory, "floating-note-overflow-menu-high-contrast-open", isHighContrast: true);
                }

                theme.Apply();
                Settle(floating);
                Check(failures, "关闭系统高对比度后恢复暖粉日间配色",
                    menu.Background is SolidColorBrush restoredBg && restoredBg.Color == dayBackgroundColor);

                WindowValidationInput.RaiseKey(editItem, Key.Escape);
                Settle(floating);
                Require(!menu.IsOpen, "关闭第一张卡的菜单失败。");
            }
            finally
            {
                if (menu.IsOpen)
                {
                    menu.IsOpen = false;
                    Settle(floating);
                }
                menuInput?.Dispose();
            }

            // 3. 第二张便签卡打开时复用同一个 ContextMenu 与 MenuItem 实例，并更新对象名称。
            Require(Keyboard.Focus(trigger2) == trigger2, "无法聚焦第二张便签卡的「…」触发器。");
            Settle(floating);
            WindowValidationInput.RaiseKey(trigger2, Key.Enter);
            Settle(floating);

            var menu2 = FindOpenContextMenu();
            Require(menu2 is { IsOpen: true }, "第二张便签卡未打开原生 ContextMenu。");
            try
            {
                var items2 = menu2!.Items.OfType<MenuItem>().ToArray();
                Check(failures, "跨卡复用同一个 ContextMenu 实例", ReferenceEquals(menu, menu2));
                Check(failures, "跨卡复用同一对 MenuItem 实例",
                    items2.Length == 2 &&
                    ReferenceEquals(menu.Items[0], items2[0]) &&
                    ReferenceEquals(menu.Items[1], items2[1]));
                Check(failures, "复用打开第二张卡时自动化名称更新为第二张便签对象",
                    string.Equals(AutomationProperties.GetName(items2[0]), $"编辑便签 {SecondNoteText}", StringComparison.Ordinal) &&
                    string.Equals(AutomationProperties.GetName(items2[1]), $"删除便签 {SecondNoteText}", StringComparison.Ordinal) &&
                    AutomationProperties.GetName(menu2).Contains(SecondNoteText, StringComparison.Ordinal));

                // 切换工作区时关闭菜单并解除 PlacementTarget 引用，不泄漏旧触发器。
                floating.ShowWorkspace(WorkspaceKind.Countdowns);
                Settle(floating);
                Check(failures, "切换工作区时关闭菜单并清空 PlacementTarget",
                    !menu2.IsOpen && menu2.PlacementTarget is null);
            }
            finally
            {
                if (menu2!.IsOpen)
                {
                    menu2.IsOpen = false;
                    Settle(floating);
                }
            }
        });

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join("；", failures)}");
    }

    internal static (bool Passed, string Explanation) RunInteractionsAndMotion(ApplicationTheme theme, string meaning)
    {
        var failures = new List<string>();
        RunWithDualWindows("interactions-motion", (workspace, main, floating) =>
        {
            var note1Id = SaveNote(workspace, FirstNoteText, floating.Dispatcher);
            var note2Id = SaveNote(workspace, SecondNoteText, floating.Dispatcher);

            floating.ShowWorkspace(WorkspaceKind.Notes);
            Settle(floating);
            ActivateForKeyboard(floating);

            var rows = RequiredByName<StackPanel>(floating, "FloatingRows");

            // A. 开启动效作用域：验证键盘打开即时到位、方向键、Esc 回焦、点外部关闭、指针进入与退出动效。
            using (ApplicationTheme.OverrideAnimationPreferenceForValidation(true))
            {
                theme.Apply();
                var enterMs = Application.Current.TryFindResource("MotionPopupEnterDuration") is Duration dEnter && dEnter.HasTimeSpan
                    ? dEnter.TimeSpan.TotalMilliseconds
                    : -1d;
                var exitMs = Application.Current.TryFindResource("MotionPopupExitDuration") is Duration dExit && dExit.HasTimeSpan
                    ? dExit.TimeSpan.TotalMilliseconds
                    : -1d;
                Check(failures, "开启动效时弹层进入/退出 token 为 140ms/90ms",
                    Math.Abs(enterMs - 140.0) < 0.1 && Math.Abs(exitMs - 90.0) < 0.1);

                var trigger1 = FindTriggerForNote(rows, note1Id, FirstNoteText);

                // A1. 键盘 Enter 打开：立即到位（Opacity=1、Y=0、无动画时钟），聚焦首项「编辑」，方向键往返，Esc 立即关闭并还焦给「…」按钮。
                Require(Keyboard.Focus(trigger1) == trigger1, "无法聚焦 trigger1。");
                Settle(floating);
                WindowValidationInput.RaiseKey(trigger1, Key.Enter);
                WindowValidationInput.Pump(floating.Dispatcher);

                var menu = FindOpenContextMenu();
                Require(menu is { IsOpen: true }, "键盘 Enter 未打开原生菜单。");
                Check(failures, "键盘 Enter 打开时立即到位（不播进入过渡）",
                    Math.Abs(menu!.Opacity - 1.0) < 0.001 &&
                    Math.Abs(ReadTranslateY(menu)) < 0.001 &&
                    !IsOpacityAnimated(menu));

                var items = menu.Items.OfType<MenuItem>().ToArray();
                Require(items.Length == 2, "原生菜单项数量不是 2。");
                using (WindowValidationInput.AcquireContextMenuInputForValidation(menu))
                {
                    if (!items[0].IsKeyboardFocused)
                    {
                        Keyboard.Focus(menu);
                        Settle(floating);
                        WindowValidationInput.RaiseKey(menu, Key.Down);
                        Settle(floating);
                    }

                    Check(failures, "键盘打开后焦点位于首项「编辑」",
                        items[0].IsKeyboardFocused && items[0].IsHighlighted);

                    WindowValidationInput.RaiseKey(items[0], Key.Down);
                    Settle(floating);
                    Check(failures, "方向键 Down 移动到「删除」并高亮",
                        items[1].IsKeyboardFocused && items[1].IsHighlighted &&
                        BrushColorEquals(items[1].Background, "MainActionBrush") &&
                        BrushColorEquals(items[1].Foreground, "MainActionTextBrush"));

                    WindowValidationInput.RaiseKey(items[1], Key.Up);
                    Settle(floating);
                    Check(failures, "方向键 Up 移回「编辑」",
                        items[0].IsKeyboardFocused && items[0].IsHighlighted);

                    // 菜单打开期间卡片按 Delete 不误触发删除确认。
                    var card1 = FindCardForNote(rows, note1Id);
                    var dialogOpenedWhileMenuOpen = false;
                    using (QueueConfirmationDialogGuard(floating, dialog =>
                           {
                               dialogOpenedWhileMenuOpen = true;
                               return WindowValidationInput.InvokeButton(dialog.CancelButton);
                           }))
                    {
                        WindowValidationInput.RaisePreviewKey(card1, Key.Delete);
                        WindowValidationInput.Pump(floating.Dispatcher);
                    }
                    Check(failures, "菜单打开期间卡片 Delete 不触发删除确认", !dialogOpenedWhileMenuOpen);

                    // Esc 立即关闭并把焦点还给「…」按钮。
                    WindowValidationInput.RaiseKey(items[0], Key.Escape);
                    WindowValidationInput.Pump(floating.Dispatcher);
                    Check(failures, "Esc 立即关闭原生菜单（不保留退出阶段）", !menu.IsOpen);
                }

                Settle(floating);
                Check(failures, "Esc 关闭后焦点回到原「…」按钮且保持单层焦点环",
                    ReferenceEquals(Keyboard.FocusedElement, trigger1) &&
                    KeyboardFocusPolicy.KeyboardFocusDrawnOnce(trigger1));

                // A2. 键盘 Down 打开触发器同样立即到位并可 Esc 关闭。
                WindowValidationInput.RaiseKey(trigger1, Key.Down);
                WindowValidationInput.Pump(floating.Dispatcher);
                Check(failures, "焦点在「…」按钮时按 Down 立即打开菜单",
                    menu.IsOpen &&
                    Math.Abs(menu.Opacity - 1.0) < 0.001 &&
                    Math.Abs(ReadTranslateY(menu)) < 0.001 &&
                    !IsOpacityAnimated(menu));
                WindowValidationInput.RaiseKey(menu, Key.Escape);
                Settle(floating);
                Check(failures, "Down 打开后按 Esc 关闭并回到触发器",
                    !menu.IsOpen && ReferenceEquals(Keyboard.FocusedElement, trigger1));

                // A3. 指针点击打开消费进入动效（140ms），点击外部消费退出动效（90ms）并关闭。
                var enterSamples = new List<(double Opacity, double Y, bool Animated)>();
                PointerClickButton(trigger1);
                var enterSettled = WindowValidationInput.PumpRenderedFrames(
                    floating.Dispatcher,
                    500,
                    () =>
                    {
                        if (menu.IsOpen)
                        {
                            enterSamples.Add((menu.Opacity, ReadTranslateY(menu), IsOpacityAnimated(menu)));
                        }

                        return menu.IsOpen &&
                               Math.Abs(menu.Opacity - 1.0) < 0.001 &&
                               Math.Abs(ReadTranslateY(menu)) < 0.001 &&
                               !IsOpacityAnimated(menu);
                    });
                Check(failures, "指针打开消费弹层进入动效（观察到透明/位移中间态并落到 1/0）",
                    enterSettled.Completed &&
                    enterSamples.Any(sample =>
                        (sample.Opacity > 0.01 && sample.Opacity < 0.99) ||
                        Math.Abs(sample.Y) > 0.05 ||
                        sample.Animated));

                // 点击外部关闭，并观察退出动效（90ms）后关闭。
                var exitSamples = new List<(bool Open, double Opacity, double Y, bool Animated)>();
                using (WindowValidationInput.AcquireContextMenuInputForValidation(menu))
                {
                    SendOutsideLeftMouseDown(menu);
                }

                var exitObservedOpenWhileClosing = menu.IsOpen;
                var exitSettled = WindowValidationInput.PumpRenderedFrames(
                    floating.Dispatcher,
                    450,
                    () =>
                    {
                        exitSamples.Add((menu.IsOpen, menu.Opacity, ReadTranslateY(menu), IsOpacityAnimated(menu)));
                        return !menu.IsOpen;
                    });
                Check(failures, "点击外部触发退出动效并关闭菜单（退出期间保持可见并完成淡出收回）",
                    exitObservedOpenWhileClosing &&
                    exitSettled.Completed &&
                    !menu.IsOpen &&
                    exitSamples.Any(sample =>
                        sample.Open &&
                        ((sample.Opacity > 0.01 && sample.Opacity < 0.99) ||
                         Math.Abs(sample.Y) > 0.05 ||
                         sample.Animated)));
                Check(failures, "点击外部关闭不误触发编辑或删除",
                    !main.IsVisible &&
                    workspace.Snapshot.Settings.Notes.Count == 2);
            }

            theme.Apply();

            // B. 系统关闭动画（减弱动画）：指针打开与关闭均立即到位。
            using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
            {
                theme.Apply();
                var enterMs = Application.Current.TryFindResource("MotionPopupEnterDuration") is Duration dEnter && dEnter.HasTimeSpan
                    ? dEnter.TimeSpan.TotalMilliseconds
                    : -1d;
                var exitMs = Application.Current.TryFindResource("MotionPopupExitDuration") is Duration dExit && dExit.HasTimeSpan
                    ? dExit.TimeSpan.TotalMilliseconds
                    : -1d;
                Check(failures, "系统关闭动画时弹层动效 token 归零",
                    Math.Abs(enterMs) < 0.01 && Math.Abs(exitMs) < 0.01);

                var trigger1 = FindTriggerForNote(rows, note1Id, FirstNoteText);
                PointerClickButton(trigger1);
                WindowValidationInput.Pump(floating.Dispatcher);

                var menu = FindOpenContextMenu();
                Require(menu is { IsOpen: true }, "关闭动画时指针点击未打开原生菜单。");
                Check(failures, "系统关闭动画时指针打开立即到位（Opacity=1、Y=0、无动画时钟）",
                    Math.Abs(menu!.Opacity - 1.0) < 0.001 &&
                    Math.Abs(ReadTranslateY(menu)) < 0.001 &&
                    !IsOpacityAnimated(menu));

                using (WindowValidationInput.AcquireContextMenuInputForValidation(menu))
                {
                    SendOutsideLeftMouseDown(menu);
                }

                WindowValidationInput.Pump(floating.Dispatcher);
                Check(failures, "系统关闭动画时点击外部立即关闭（无退出滞留）", !menu.IsOpen);
            }

            theme.Apply();
            Settle(floating);

            // C. Enter 与 Space 激活「编辑」：均打开主窗编辑对应便签并关闭菜单。
            VerifyEditActivation(floating, main, rows, note1Id, FirstNoteText, Key.Enter, failures);
            VerifyEditActivation(floating, main, rows, note2Id, SecondNoteText, Key.Space, failures);

            // D. Space / Enter 激活「删除」：仍走取消为安全默认的确认框 + 10 秒撤销。
            VerifyDeleteWithConfirmationAndUndo(workspace, floating, rows, note2Id, SecondNoteText, Key.Space, cancelOnly: true, failures);
            VerifyDeleteWithConfirmationAndUndo(workspace, floating, rows, note2Id, SecondNoteText, Key.Enter, cancelOnly: false, failures);

            // E. 卡片上按 Delete 行为不变（取消保留、确认删除且可 10 秒撤销）。
            VerifyCardDeleteKeyUnchanged(workspace, floating, rows, note1Id, FirstNoteText, failures);
        });

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败：{string.Join("；", failures)}");
    }

    private static void VerifyEditActivation(
        FloatingWindow floating,
        MainWindow main,
        StackPanel rows,
        string noteId,
        string noteText,
        Key activationKey,
        ICollection<string> failures)
    {
        ActivateForKeyboard(floating);
        var trigger = FindTriggerForNote(rows, noteId, noteText);
        Require(Keyboard.Focus(trigger) == trigger, $"无法聚焦 {noteText} 的触发器。");
        Settle(floating);
        WindowValidationInput.RaiseKey(trigger, Key.Enter);
        Settle(floating);

        var menu = FindOpenContextMenu();
        Require(menu is { IsOpen: true }, $"打开 {noteText} 菜单以验证 {activationKey} 编辑失败。");
        var editItem = menu!.Items.OfType<MenuItem>().First();
        using (WindowValidationInput.AcquireContextMenuInputForValidation(menu))
        {
            if (!editItem.IsKeyboardFocused)
            {
                Keyboard.Focus(menu);
                Settle(floating);
                WindowValidationInput.RaiseKey(menu, Key.Down);
                Settle(floating);
            }

            WindowValidationInput.RaiseKey(editItem, activationKey);
            Settle(floating);
        }

        Settle(main);
        var editorText = RequiredByName<TextBox>(main, "NoteText");
        var cancelEdit = RequiredByName<Button>(main, "CancelNoteEdit");
        Check(failures, $"{activationKey} 激活「编辑」关闭菜单并在主窗载入对应便签「{noteText}」",
            !menu.IsOpen &&
            main.IsVisible &&
            string.Equals(editorText.Text, noteText, StringComparison.Ordinal) &&
            cancelEdit.Visibility == Visibility.Visible);

        cancelEdit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Settle(main);
        main.Hide();
        Settle(floating);
    }

    private static void VerifyDeleteWithConfirmationAndUndo(
        DesktopWorkspace workspace,
        FloatingWindow floating,
        StackPanel rows,
        string noteId,
        string noteText,
        Key activationKey,
        bool cancelOnly,
        ICollection<string> failures)
    {
        ActivateForKeyboard(floating);
        var trigger = FindTriggerForNote(rows, noteId, noteText);
        Require(Keyboard.Focus(trigger) == trigger, $"无法聚焦 {noteText} 的触发器。");
        Settle(floating);
        WindowValidationInput.RaiseKey(trigger, Key.Enter);
        Settle(floating);

        var menu = FindOpenContextMenu();
        Require(menu is { IsOpen: true }, $"打开 {noteText} 菜单以验证 {activationKey} 删除失败。");
        var items = menu!.Items.OfType<MenuItem>().ToArray();
        var editItem = items[0];
        var deleteItem = items[1];

        bool? cancelIsSafeDefault = null;
        bool? mentionsTenSecondUndo = null;
        var dialogSeen = false;
        using (QueueConfirmationDialogGuard(floating, dialog =>
               {
                   dialogSeen = true;
                   cancelIsSafeDefault = dialog.CancelButton.IsDefault && dialog.CancelButton.IsCancel &&
                                         !dialog.ConfirmButton.IsDefault;
                   mentionsTenSecondUndo = dialog.BodyText.Text.Contains("10 秒", StringComparison.Ordinal);
                   return WindowValidationInput.InvokeButton(cancelOnly ? dialog.CancelButton : dialog.ConfirmButton);
               }))
        using (WindowValidationInput.AcquireContextMenuInputForValidation(menu))
        {
            if (!editItem.IsKeyboardFocused)
            {
                Keyboard.Focus(menu);
                Settle(floating);
                WindowValidationInput.RaiseKey(menu, Key.Down);
                Settle(floating);
            }

            WindowValidationInput.RaiseKey(editItem, Key.Down);
            Settle(floating);
            Require(deleteItem.IsKeyboardFocused, "方向键未能停在「删除」菜单项。");
            WindowValidationInput.RaiseKey(deleteItem, activationKey);
            Settle(floating);
        }

        Settle(floating);
        Check(failures, $"{activationKey} 激活「删除」关闭菜单并弹出取消为安全默认的确认框",
            !menu.IsOpen && dialogSeen && cancelIsSafeDefault == true && mentionsTenSecondUndo == true);

        if (cancelOnly)
        {
            Check(failures, "删除确认框取消后便签保持不变",
                workspace.Snapshot.Settings.Notes.Any(note => string.Equals(note.Id, noteId, StringComparison.Ordinal)));
            return;
        }

        Check(failures, "删除确认框确认后删除便签并在悬浮窗展示 10 秒撤销入口",
            !workspace.Snapshot.Settings.Notes.Any(note => string.Equals(note.Id, noteId, StringComparison.Ordinal)) &&
            DesktopWorkspace.UndoWindow == TimeSpan.FromSeconds(10) &&
            workspace.UndoOffer is { Surface: WorkspaceUndoSurface.Floating, Payload: var payload } &&
            string.Equals(payload.AccessibleName, $"撤销删除便签「{noteText}」", StringComparison.Ordinal) &&
            RequiredByName<Grid>(floating, "FloatingDeleteUndoLine").Visibility == Visibility.Visible);

        var undoButton = RequiredByName<Button>(floating, "FloatingDeleteUndoButton");
        Require(WindowValidationInput.InvokeButton(undoButton), "无法点击悬浮窗删除撤销按钮。");
        Settle(floating);
        Check(failures, "点击 10 秒撤销恢复被删便签",
            workspace.Snapshot.Settings.Notes.Any(note => string.Equals(note.Id, noteId, StringComparison.Ordinal)));
    }

    private static void VerifyCardDeleteKeyUnchanged(
        DesktopWorkspace workspace,
        FloatingWindow floating,
        StackPanel rows,
        string noteId,
        string noteText,
        ICollection<string> failures)
    {
        ActivateForKeyboard(floating);
        var card = FindCardForNote(rows, noteId);
        var completion = VisualDescendants<CheckBox>(card).Single(box =>
            string.Equals(box.Tag as string, noteId, StringComparison.Ordinal));
        Require(Keyboard.Focus(completion) == completion, $"无法聚焦便签「{noteText}」的完成复选框。");
        Settle(floating);

        // 取消保留。
        var cancelSeen = false;
        using (QueueConfirmationDialogGuard(floating, dialog =>
               {
                   cancelSeen = dialog.CancelButton.IsDefault && dialog.CancelButton.IsCancel;
                   return WindowValidationInput.InvokeButton(dialog.CancelButton);
               }))
        {
            WindowValidationInput.RaisePreviewKey(completion, Key.Delete);
            Settle(floating);
        }
        Check(failures, "卡片上按 Delete 弹出取消为安全默认的确认框且取消后保留便签",
            cancelSeen &&
            workspace.Snapshot.Settings.Notes.Any(note => string.Equals(note.Id, noteId, StringComparison.Ordinal)));

        // 确认删除 + 撤销恢复。
        var confirmSeen = false;
        using (QueueConfirmationDialogGuard(floating, dialog =>
               {
                   confirmSeen = true;
                   return WindowValidationInput.InvokeButton(dialog.ConfirmButton);
               }))
        {
            WindowValidationInput.RaisePreviewKey(completion, Key.Delete);
            Settle(floating);
        }
        Check(failures, "卡片上按 Delete 确认后删除并提供 10 秒撤销",
            confirmSeen &&
            !workspace.Snapshot.Settings.Notes.Any(note => string.Equals(note.Id, noteId, StringComparison.Ordinal)) &&
            workspace.UndoOffer is { Surface: WorkspaceUndoSurface.Floating });

        var undoButton = RequiredByName<Button>(floating, "FloatingDeleteUndoButton");
        Require(WindowValidationInput.InvokeButton(undoButton), "卡片 Delete 删除后无法点击撤销按钮。");
        Settle(floating);
        Check(failures, "卡片 Delete 删除后撤销恢复便签",
            workspace.Snapshot.Settings.Notes.Any(note => string.Equals(note.Id, noteId, StringComparison.Ordinal)));
    }

    private static void RunWithDualWindows(
        string tag,
        Action<DesktopWorkspace, MainWindow, FloatingWindow> body)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-floating-overflow-{tag}-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        FloatingWindow? floating = null;
        try
        {
            var clock = new FixedClock(FixtureUtc);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
            {
                ShowInTaskbar = false
            };
            PrepareOffscreen(main);
            floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory)
            {
                ShowInTaskbar = false
            };
            PrepareOffscreen(floating);
            // 悬浮窗位于主工作区左上安全位置（仍在隔离桌面），避免屏幕底边干扰并允许正常渲染实拍。
            floating.Left = 40;
            floating.Top = 40;
            floating.Opacity = 1;
            floating.Show();
            Settle(floating);
            body(workspace, main, floating);
        }
        finally
        {
            var openMenu = FindOpenContextMenu();
            if (openMenu is { IsOpen: true })
            {
                openMenu.IsOpen = false;
                WindowValidationInput.Pump(openMenu.Dispatcher);
            }

            if (floating is not null)
            {
                floating.Close();
                WindowValidationInput.Pump(floating.Dispatcher);
            }

            if (main is not null)
            {
                main.Close();
                WindowValidationInput.Pump(main.Dispatcher);
            }

            DeleteSettingsFiles(settingsPath);
            if (File.Exists(diagnosticPath))
            {
                File.Delete(diagnosticPath);
            }
        }
    }

    private static List<Button> FindOverflowTriggers(DependencyObject rows) =>
        VisualDescendants<Button>(rows)
            .Where(button => AutomationProperties.GetName(button)
                .StartsWith("更多操作：编辑或删除便签 ", StringComparison.Ordinal))
            .ToList();

    private static Button FindTriggerForNote(DependencyObject rows, string noteId, string noteText) =>
        VisualDescendants<Button>(rows)
            .Single(button =>
                string.Equals(button.Tag as string, noteId, StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(button),
                    $"更多操作：编辑或删除便签 {noteText}",
                    StringComparison.Ordinal));

    private static Border FindCardForNote(StackPanel rows, string noteId) =>
        rows.Children.OfType<Border>()
            .Single(card => VisualDescendants<CheckBox>(card)
                .Any(box => string.Equals(box.Tag as string, noteId, StringComparison.Ordinal)));

    private static ContextMenu? FindOpenContextMenu()
    {
        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (source.RootVisual is not DependencyObject root)
            {
                continue;
            }

            if (root is ContextMenu direct && direct.IsOpen)
            {
                return direct;
            }

            var nested = VisualDescendants<ContextMenu>(root).FirstOrDefault(candidate => candidate.IsOpen);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool CardSubtreeHasPrecreatedMenuOrPopup(DependencyObject root)
    {
        if (root is Popup or ContextMenu or MenuItem || HasAttachedPopupOrMenuValue(root))
        {
            return true;
        }

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(root))
        {
            if (logicalChild is Popup or ContextMenu or MenuItem)
            {
                return true;
            }
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (CardSubtreeHasPrecreatedMenuOrPopup(VisualTreeHelper.GetChild(root, index)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttachedPopupOrMenuValue(DependencyObject element)
    {
        var enumerator = element.GetLocalValueEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.Value is Popup or ContextMenu or MenuItem)
            {
                return true;
            }
        }

        return false;
    }

    private static void PointerClickButton(Button button)
    {
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            Source = button
        };
        button.RaiseEvent(down);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static void SendOutsideLeftMouseDown(ContextMenu menu)
    {
        if (PresentationSource.FromVisual(menu) is not HwndSource source)
        {
            throw new InvalidOperationException("原生菜单没有可用的 HwndSource 以发送外部点击。");
        }

        // (-20, -20) 位于菜单客户区之外，在菜单持捕期间同步线程左键状态后由 WPF 原生路径触发外部点击关闭。
        var savedKeys = new byte[256];
        var hasSavedKeys = GetKeyboardState(savedKeys);
        try
        {
            var keys = hasSavedKeys ? (byte[])savedKeys.Clone() : new byte[256];
            var lParam = MakeLParam(-20, -20);
            SendMessageW(source.Handle, WmMouseMove, IntPtr.Zero, lParam);
            keys[1] = 0x80;
            SetKeyboardState(keys);
            SendMessageW(source.Handle, WmLButtonDown, new IntPtr(1), lParam);
            keys[1] = 0;
            SetKeyboardState(keys);
            SendMessageW(source.Handle, WmLButtonUp, IntPtr.Zero, lParam);
        }
        finally
        {
            if (hasSavedKeys)
            {
                SetKeyboardState(savedKeys);
            }
        }
    }

    private static IntPtr MakeLParam(int x, int y) =>
        new((unchecked((short)y) << 16) | (unchecked((ushort)(short)x)));

    private static double ReadTranslateY(UIElement element) =>
        element.RenderTransform switch
        {
            TranslateTransform translate => translate.Y,
            TransformGroup group => group.Children.OfType<TranslateTransform>().LastOrDefault()?.Y ?? 0,
            _ => 0
        };

    private static bool IsOpacityAnimated(DependencyObject element) =>
        DependencyPropertyHelper.GetValueSource(element, UIElement.OpacityProperty).IsAnimated;

    private static bool BrushColorEquals(Brush? actual, string resourceKey) =>
        actual is SolidColorBrush actualSolid &&
        Application.Current.TryFindResource(resourceKey) is SolidColorBrush expectedSolid &&
        actualSolid.Color == expectedSolid.Color;

    private static T RequiredByName<T>(FrameworkElement host, string name) where T : FrameworkElement =>
        host.FindName(name) as T ?? throw new InvalidOperationException($"找不到控件 {name}。");

    private static void ActivateForKeyboard(Window window)
    {
        window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetActiveWindow(handle);
        }

        Settle(window);
    }

    private static void PrepareOffscreen(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20_000;
        window.Top = -20_000;
        window.Opacity = 0;
        window.Topmost = false;
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static string? GetCaptureDirectory()
    {
        if (!_captureDirectoryInitialized)
        {
            _captureDirectory = CreateCaptureDirectory();
            _captureDirectoryInitialized = true;
        }

        return _captureDirectory;
    }

    private static string? CreateCaptureDirectory()
    {
        var explicitDirectory = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_EVIDENCE_319_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            var resolved = Path.GetFullPath(explicitDirectory);
            Directory.CreateDirectory(resolved);
            return resolved;
        }

        var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return null;
        }

        var directory = Path.Combine(
            Path.GetFullPath(sourceRoot),
            "logs",
            "ui-captures",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-floating-note-overflow-menu");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CaptureMenuVisual(ContextMenu menu, string? directory, string fileStem, bool isHighContrast)
    {
        if (directory is null)
        {
            return;
        }

        TextOptions.SetTextRenderingMode(menu, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(menu, TextFormattingMode.Ideal);
        WindowValidationInput.Pump(menu.Dispatcher);
        menu.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(menu);
        var dpiX = dpi.PixelsPerInchX > 0 ? dpi.PixelsPerInchX : 96;
        var dpiY = dpi.PixelsPerInchY > 0 ? dpi.PixelsPerInchY : 96;
        const int pad = 12;
        var contentWidth = Math.Max(1, (int)Math.Ceiling(menu.ActualWidth * dpiX / 96.0));
        var contentHeight = Math.Max(1, (int)Math.Ceiling(menu.ActualHeight * dpiY / 96.0));
        var width = contentWidth + (pad * 2);
        var height = contentHeight + (pad * 2);
        var backdropColor = isHighContrast
            ? SystemColors.WindowColor
            : Application.Current.TryFindResource("BackgroundBrush") is SolidColorBrush bg
                ? bg.Color
                : Color.FromRgb(0xFD, 0xF3, 0xEB);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(backdropColor), null, new Rect(0, 0, width, height));
            var brush = new VisualBrush(menu) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            context.DrawRectangle(brush, null, new Rect(pad, pad, contentWidth, contentHeight));
        }

        var bitmap = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var outputPath = Path.Combine(directory, $"{fileStem}.png");
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static IDisposable QueueConfirmationDialogGuard(Window owner, Func<ConfirmationDialog, bool> invoke)
    {
        var attempts = 0;
        ConfirmationDialog? dispatchedDialog = null;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, owner.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        timer.Tick += (_, _) =>
        {
            attempts++;
            if (dispatchedDialog is { IsVisible: false })
            {
                timer.Stop();
                return;
            }

            var dialog = owner.OwnedWindows
                .OfType<ConfirmationDialog>()
                .FirstOrDefault(window => window.IsVisible);
            if (dialog is not null && dispatchedDialog is null)
            {
                dispatchedDialog = dialog;
                if (!invoke(dialog))
                {
                    timer.Stop();
                    dialog.Close();
                    throw new InvalidOperationException("确认框验证未能调用按钮命令。");
                }
            }

            if (attempts >= 100)
            {
                timer.Stop();
                dispatchedDialog?.Close();
                throw new InvalidOperationException("确认框验证在 3 秒内未完成关闭。");
            }
        };
        timer.Start();
        return new ActionDisposable(() => timer.Stop());
    }

    private sealed class ActionDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    private static void Check(ICollection<string> failures, string label, bool condition)
    {
        if (!condition)
        {
            failures.Add(label);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetActiveWindow(IntPtr windowHandle);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] lpKeyState);
}
