using System.Globalization;
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
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;
using WpfPath = System.Windows.Shapes.Path;

namespace MinminCountdown.WindowVerification;

// #304：管理窗口五项对齐与空态细项的独立窗口验证。
// 1) 命令栏三枚命令按钮等高且垂直居中，数量文字共用同一中线，命令栏右缘对齐内容区右缘；页签容器、指示条与命中区不退化。
// 2) 切换倒计时/便签页签前后主标题水平位置不变，两页装饰图形（心形/小屋）均保留在标题之后。
// 3) 便签新增态与编辑态行尾留白等于行首留白，正文列吸收剩余宽度，「取消」内边距与其他次级按钮一致。
// 4) 首次空态填写引导常态可辨认为可点（暖粉底色、≥3:1 边框、≥4.5:1 文字与方向图标、悬停/焦点/系统对比度），点击只聚焦表单不新增不清草稿。
// 5) 系统对比度开启时装饰带布局空间收起不留空白（含运行中切换与对比度下新开窗口），关闭后恢复原样。
internal static class MainWindowAlignmentAndPolishScenario
{
    private const double GeometryTolerance = 0.5;
    private const double MinimumCompactHitTarget = 24.0;
    private const double MinimumSecondaryHitHeight = 28.0;
    private const double ExpectedBlockGap = 12.0;
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) RunCommandBarHeaderAndNoteRows(string meaning)
    {
        var failures = new List<string>();
        WithMainWindow(seedData: true, (main, workspace) =>
        {
            VerifyCommandBarAlignment(main, failures);
            VerifyTitleInvariantAcrossTabs(main, failures);
            VerifyNoteEditorAddAndEditRows(main, workspace, failures);
        });
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunWorkspaceSlidingPill(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        try
        {
            WithMainWindow(seedData: true, (main, _) =>
            {
                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
                {
                    theme.Apply();
                    VerifyWorkspacePillGeometryAndStates(main, theme, failures);
                }

                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(true))
                {
                    theme.Apply();
                    VerifyWorkspacePillFirstFrames(main, failures);
                }
            });
        }
        catch (Exception exception)
        {
            failures.Add($"工作区滑动胶囊验收异常：{exception.Message}");
        }

        theme.Apply();
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunNotesSlidingPillGeometry(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        try
        {
            WithMainWindow(seedData: true, (main, _) =>
            {
                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
                {
                    theme.Apply();
                    new MainWindowEmptyStateProbe(main).ShowWorkspace(WorkspaceKind.Notes);
                    SettleLayout(main);
                    VerifyNotesPillGeometryAndStates(main, theme, failures);
                }
            });
        }
        catch (Exception exception)
        {
            failures.Add($"便签状态滑动胶囊验收异常：{exception.Message}");
        }

        theme.Apply();
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunNotesSlidingPillDynamicCounts(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        try
        {
            WithMainWindow(
                seedData: false,
                (main, workspace) =>
                {
                    using (ApplicationTheme.OverrideAnimationPreferenceForValidation(true))
                    {
                        theme.Apply();
                        VerifyNotesDynamicCountGeometry(main, workspace, failures);
                    }
                },
                prepare: SeedDynamicCountNotes);
        }
        catch (Exception exception)
        {
            failures.Add($"便签动态计数胶囊验收异常：{exception.Message}");
        }

        theme.Apply();
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunEmptyStateAndHighContrastRibbon(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
        {
            theme.Apply();
            try
            {
                WithMainWindow(seedData: false, (main, workspace) =>
                {
                    VerifyEmptyStateClickableAffordance(main, workspace, failures);
                    VerifyHighContrastDecorativeRibbonCollapse(main, theme, failures);
                });
            }
            catch (Exception exception)
            {
                failures.Add($"空态可点性与系统对比度装饰带收起验收异常：{exception.Message}");
            }
        }

        theme.Apply();
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    private static void VerifyWorkspacePillGeometryAndStates(
        MainWindow main,
        ApplicationTheme theme,
        ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var layout = Required<Canvas>(main, "MainLayoutSurface");
        var commandBar = Required<Grid>(main, "MainCommandBarZone");
        var host = Required<Grid>(main, "MainWorkspaceTabHost");
        var container = host.Parent as Border
            ?? throw new InvalidOperationException("工作区页签缺少外层槽 Border。");
        var layer = Required<Canvas>(main, "MainWorkspaceTabIndicatorLayer");
        var indicator = Required<Border>(main, "MainWorkspaceTabIndicator");
        var countdownTab = Required<ToggleButton>(main, "CountdownTab");
        var notesTab = Required<ToggleButton>(main, "NotesTab");
        var buttons = host.Children.OfType<StackPanel>().Single();
        var contentTops = new Dictionary<WorkspaceKind, double>();

        foreach (var (kind, tab, contentName) in new[]
                 {
                     (WorkspaceKind.Countdowns, countdownTab, "CountdownEditorSurface"),
                     (WorkspaceKind.Notes, notesTab, "NoteEditorSurface")
                 })
        {
            probe.ShowWorkspace(kind);
            SettleLayout(main);
            _ = WindowValidationInput.WaitUntil(
                main.Dispatcher,
                () => main.VisibleSegmentIndicatorsSettledForCapture,
                1000);
            tab.ApplyTemplate();
            var surface = RequiredTemplate<Border>(tab, "Surface");
            var ring = RequiredTemplate<Border>(tab, "FocusRing");
            var surfaceBounds = Bounds(surface, host);
            var pillBounds = Bounds(indicator, host);
            var surfaceInContainer = Bounds(surface, container);
            var commandBounds = Bounds(commandBar, layout);
            var containerBounds = Bounds(container, layout);

            if (Math.Abs(commandBounds.Height - 51) > GeometryTolerance ||
                Math.Abs(containerBounds.Width - 190) > GeometryTolerance ||
                Math.Abs(containerBounds.Height - 50) > GeometryTolerance ||
                Math.Abs(CenterY(containerBounds) - CenterY(commandBounds)) > GeometryTolerance ||
                container.Padding != new Thickness(3, 2, 2, 2) ||
                container.BorderThickness != new Thickness(1) ||
                container.CornerRadius != (CornerRadius)main.FindResource("RadiusControl") ||
                !BrushMatches(container.Background, RequiredBrush(container, "CardAltBrush")) ||
                !BrushMatches(container.BorderBrush, RequiredBrush(container, "PeachLineBrush")))
            {
                failures.Add($"{kind}：工作区槽几何/画刷不符（bar={FormatRect(commandBounds)}, container={FormatRect(containerBounds)}, padding={container.Padding}, border={container.BorderThickness}, radius={container.CornerRadius}）。");
            }

            if (!RectClose(surfaceBounds, pillBounds) ||
                Math.Abs(pillBounds.Height - 38) > GeometryTolerance ||
                indicator.CornerRadius != new CornerRadius(19))
            {
                failures.Add($"{kind}：滑动胶囊未与 Surface 全矩形重合或不是 38/19 全圆角（surface={FormatRect(surfaceBounds)}, pill={FormatRect(pillBounds)}）。");
            }

            if (Math.Abs(surfaceInContainer.Top -
                    (container.ActualHeight - surfaceInContainer.Bottom)) > GeometryTolerance ||
                surface.Margin != new Thickness(3) || ring.BorderThickness != new Thickness(2))
            {
                failures.Add($"{kind}：胶囊上下净空不对称，或 2 DIP 环 + 1 DIP 间隙发生变化。");
            }

            if (Panel.GetZIndex(layer) >= Panel.GetZIndex(buttons) ||
                layer.IsHitTestVisible || indicator.IsHitTestVisible || indicator.Focusable ||
                UIElementAutomationPeer.CreatePeerForElement(indicator) is not null)
            {
                failures.Add($"{kind}：滑动胶囊未位于按钮底层、仍可命中/聚焦或进入了自动化树。");
            }

            var hitPoint = surface.TransformToAncestor(host).Transform(
                new Point(surface.RenderSize.Width / 2, surface.RenderSize.Height / 2));
            var hit = host.InputHitTest(hitPoint) as DependencyObject;
            while (hit is not null && hit != tab)
            {
                hit = VisualTreeHelper.GetParent(hit);
            }
            if (hit != tab)
            {
                failures.Add($"{kind}：胶囊中心的真实命中未到达激活页签。");
            }

            contentTops[kind] = Bounds(
                Required<FrameworkElement>(main, contentName), layout).Top;
            var decorationTop = Bounds(
                Required<FrameworkElement>(main, "MainDecorationBandSpacer"), layout).Top;
            if (Math.Abs(decorationTop - commandBounds.Bottom) > GeometryTolerance)
            {
                failures.Add($"{kind}：命令栏/装饰带纵向漂移（bar={FormatRect(commandBounds)}, decorationTop={decorationTop:0.##}）。");
            }
        }

        // 未选中 hover 使用暖底/暖边；选中 hover 的自身 Surface 必须保持透明。
        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        var selectedSurface = RequiredTemplate<Border>(countdownTab, "Surface");
        var unselectedSurface = RequiredTemplate<Border>(notesTab, "Surface");
        if (!WindowValidationInput.WaitUntil(main.Dispatcher, () =>
                IsTransparentBrush(selectedSurface.Background) &&
                IsTransparentBrush(selectedSurface.BorderBrush), 1000))
        {
            failures.Add("选中页签自身 Surface 未保持透明背景与边框。");
        }
        WithPointerHover(main, notesTab, () =>
        {
            if (!BrushMatches(unselectedSurface.Background, RequiredBrush(notesTab, "CardAltBrush")) ||
                !BrushMatches(unselectedSurface.BorderBrush, RequiredBrush(notesTab, "ActiveHoverBrush")))
            {
                failures.Add("未选中页签 hover 未呈现 CardAlt 暖底与 ActiveHover 暖边。");
            }
        });
        WithPointerHover(main, countdownTab, () =>
        {
            if (!IsTransparentBrush(selectedSurface.Background) ||
                !IsTransparentBrush(selectedSurface.BorderBrush))
            {
                failures.Add("选中页签 hover 回落到第二层底色或边框。");
            }
        });

        // 真实 WPF Tab 遍历：单层 2 DIP Active 环对槽底达到非文本对比度。
        main.Activate();
        Keyboard.Focus(countdownTab);
        FocusVisualState.SetIsKeyboardFocusVisible(countdownTab, true);
        SettleLayout(main);
        var tabMoved = countdownTab.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        SettleLayout(main);
        var notesRing = RequiredTemplate<Border>(notesTab, "FocusRing");
        if (!tabMoved || !notesTab.IsKeyboardFocused ||
            !FocusVisualState.GetIsKeyboardFocusVisible(notesTab) ||
            notesTab.FocusVisualStyle is not null ||
            notesRing.BorderThickness != new Thickness(2) ||
            !BrushMatches(notesRing.BorderBrush, RequiredBrush(notesTab, "ActiveBrush")) ||
            !ApplicationTheme.MeetsContrast(
                notesRing.BorderBrush,
                container.Background,
                3.0))
        {
            failures.Add("Tab 聚焦未保持单层 2 DIP Active 焦点环或环对槽底低于 3:1。");
        }
        var pointerFocused = WindowValidationInput.ClickForFocusValidation(notesTab);
        SettleLayout(main);
        if (!pointerFocused || FocusVisualState.GetIsKeyboardFocusVisible(notesTab) ||
            !IsTransparentBrush(notesRing.BorderBrush))
        {
            failures.Add("鼠标点击后仍残留键盘焦点环。");
        }

        // 系统对比度只给底层胶囊增加 2 DIP Highlight 轮廓；关闭后完整恢复日间。
        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        VerifyDayPill(indicator, "初始日间", failures);
        using (ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            SettleLayout(main);
            if (indicator.BorderThickness != new Thickness(2) ||
                !BrushMatchesColor(indicator.Background, SystemColors.WindowColor) ||
                !BrushMatchesColor(indicator.BorderBrush, SystemColors.HighlightColor))
            {
                failures.Add("系统对比度下胶囊不是 Window 底 + 2 DIP Highlight 轮廓。");
            }
        }
        theme.Apply();
        SettleLayout(main);
        VerifyDayPill(indicator, "对比度往返后", failures);

        foreach (var (kind, contentName) in new[]
                 {
                     (WorkspaceKind.Countdowns, "CountdownEditorSurface"),
                     (WorkspaceKind.Notes, "NoteEditorSurface")
                 })
        {
            probe.ShowWorkspace(kind);
            SettleLayout(main);
            var currentTop = Bounds(Required<FrameworkElement>(main, contentName), layout).Top;
            if (Math.Abs(currentTop - contentTops[kind]) > GeometryTolerance)
            {
                failures.Add($"{kind}：交互与对比度往返后内容区纵向位置漂移。");
            }
        }
    }

    private static void SeedDynamicCountNotes(DesktopWorkspace workspace)
    {
        for (var index = 0; index < 122; index++)
        {
            var result = workspace.Apply(new SaveNoteChange(
                null,
                $"动态计数-{index:000}",
                workspace.Snapshot.BeijingNow.Date,
                isCompleted: index >= 9,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"动态计数夹具写入失败：{result.Error}");
            }
        }
    }

    private static void VerifyNotesDynamicCountGeometry(
        MainWindow main,
        DesktopWorkspace workspace,
        ICollection<string> failures)
    {
        new MainWindowEmptyStateProbe(main).ShowWorkspace(WorkspaceKind.Notes);
        var active = Required<ToggleButton>(main, "ActiveNotesSegment");
        var completed = Required<ToggleButton>(main, "CompletedNotesSegment");
        var indicator = Required<Border>(main, "NotesSegmentIndicator");
        SettleLayout(main);

        completed.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var pointerAnimated = false;
        var pointerFrame = WindowValidationInput.PumpRenderedFrames(main.Dispatcher, 1000, () =>
        {
            pointerAnimated = WorkspacePillAnimating(indicator);
            return pointerAnimated;
        });
        if (!pointerFrame.Completed || !pointerAnimated)
        {
            var duration = main.FindResource("MotionIndicatorDuration") is Duration configured
                ? configured.TimeSpan.TotalMilliseconds
                : -1;
            failures.Add($"指针切换未完成→已完成时没有启动统一 SlidingPill 的 160ms 动画（enabled={ApplicationTheme.ClientAreaAnimationEnabled}, duration={duration:0.##}ms）。");
        }
        SettleLayout(main);
        _ = WindowValidationInput.WaitUntil(
            main.Dispatcher,
            () => main.VisibleSegmentIndicatorsSettledForCapture,
            1000);
        var beforeAdd = VerifyNotesCountImmediate(
            main, completed, 9, 113, "初始已完成 113", failures, requireImmediate: false);
        var beforeActiveWidth = active.ActualWidth;

        _ = SaveNote(workspace, "动态计数-新增第十条", main.Dispatcher);
        var afterAdd = VerifyNotesCountImmediate(main, completed, 10, 113, "新增使非激活兄弟 9→10", failures);
        if (active.ActualWidth <= beforeActiveWidth + GeometryTolerance ||
            afterAdd.Left <= beforeAdd.Left + GeometryTolerance)
        {
            failures.Add($"非激活兄弟变宽没有推动激活已完成段 X（activeWidth={beforeActiveWidth:0.##}→{active.ActualWidth:0.##}, pillX={beforeAdd.Left:0.##}→{afterAdd.Left:0.##}）。");
        }

        var noteId = workspace.Snapshot.Settings.Notes.First(note => !note.IsCompleted).Id;
        var completedResult = workspace.ApplyWithUndo(
            new SetNoteCompletionChange(noteId, true),
            WorkspaceUndoSurface.Main);
        if (!completedResult.Succeeded)
        {
            failures.Add($"完成便签失败：{completedResult.Error}");
            return;
        }
        WindowValidationInput.Pump(main.Dispatcher);
        _ = VerifyNotesCountImmediate(main, completed, 9, 114, "已完成段选中时完成/兄弟变窄", failures);

        var undoResult = workspace.Undo(WorkspaceUndoSurface.Main);
        if (!undoResult.Succeeded)
        {
            failures.Add($"撤销完成失败：{undoResult.Error}");
            return;
        }
        WindowValidationInput.Pump(main.Dispatcher);
        _ = VerifyNotesCountImmediate(main, completed, 10, 113, "已完成段选中时撤销", failures);

        active.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
        _ = WindowValidationInput.WaitUntil(
            main.Dispatcher,
            () => main.VisibleSegmentIndicatorsSettledForCapture,
            1000);
        var beforeActiveCompletion = VerifyNotesCountImmediate(
            main, active, 10, 113, "激活段完成前 10", failures, requireImmediate: false);
        completedResult = workspace.ApplyWithUndo(
            new SetNoteCompletionChange(noteId, true),
            WorkspaceUndoSurface.Main);
        if (!completedResult.Succeeded)
        {
            failures.Add($"第二次完成便签失败：{completedResult.Error}");
            return;
        }
        WindowValidationInput.Pump(main.Dispatcher);
        var afterActiveCompletion = VerifyNotesCountImmediate(main, active, 9, 114, "激活段自身 10→9", failures);
        if (afterActiveCompletion.Width >= beforeActiveCompletion.Width - GeometryTolerance)
        {
            failures.Add($"激活段自身计数位数减少没有缩小胶囊 Width（{beforeActiveCompletion.Width:0.##}→{afterActiveCompletion.Width:0.##}）。");
        }

        undoResult = workspace.Undo(WorkspaceUndoSurface.Main);
        if (!undoResult.Succeeded)
        {
            failures.Add($"第二次撤销完成失败：{undoResult.Error}");
            return;
        }
        WindowValidationInput.Pump(main.Dispatcher);
        _ = VerifyNotesCountImmediate(main, active, 10, 113, "激活段自身撤销 9→10", failures);

        var originalFilter = workspace.Snapshot.Filter;
        workspace.ApplyFilter(originalFilter with { NoteDate = "2001-02-03" });
        WindowValidationInput.Pump(main.Dispatcher);
        _ = VerifyNotesCountImmediate(main, active, 0, 0, "筛选使激活段归零", failures);

        workspace.ApplyFilter(originalFilter);
        WindowValidationInput.Pump(main.Dispatcher);
        _ = VerifyNotesCountImmediate(main, active, 10, 113, "清除筛选恢复计数", failures);
    }

    private static Rect VerifyNotesCountImmediate(
        MainWindow main,
        ToggleButton selected,
        int expectedActive,
        int expectedCompleted,
        string label,
        ICollection<string> failures,
        bool requireImmediate = true)
    {
        var host = Required<Grid>(main, "NotesSegmentHost");
        var indicator = Required<Border>(main, "NotesSegmentIndicator");
        var activeCount = Required<TextBlock>(main, "ActiveNotesCount");
        var completedCount = Required<TextBlock>(main, "CompletedNotesCount");
        var badge = Required<Border>(
            main,
            selected == CompletedSegment(main) ? "CompletedNotesBadge" : "ActiveNotesBadge");
        selected.ApplyTemplate();
        var surface = RequiredTemplate<Border>(selected, "Surface");
        var pill = default(Rect);
        var expected = default(Rect);
        var badgeBounds = default(Rect);
        var animated = false;
        var countsMatch = false;
        var frame = WindowValidationInput.PumpRenderedFrames(main.Dispatcher, 1000, () =>
        {
            pill = Bounds(indicator, host);
            expected = Bounds(surface, host);
            badgeBounds = Bounds(badge, host);
            animated = WorkspacePillAnimating(indicator);
            countsMatch = activeCount.Text == expectedActive.ToString(CultureInfo.InvariantCulture) &&
                completedCount.Text == expectedCompleted.ToString(CultureInfo.InvariantCulture);
            return true;
        });
        var badgeInside = badgeBounds.Left >= pill.Left - GeometryTolerance &&
            badgeBounds.Top >= pill.Top - GeometryTolerance &&
            badgeBounds.Right <= pill.Right + GeometryTolerance &&
            badgeBounds.Bottom <= pill.Bottom + GeometryTolerance;
        if (!frame.Completed || (requireImmediate && animated) || !countsMatch ||
            !RectClose(pill, expected) || !badgeInside)
        {
            failures.Add($"{label}：计数几何未在首帧即时清钟贴合（pill={FormatRect(pill)}, surface={FormatRect(expected)}, badge={FormatRect(badgeBounds)}, animated={animated}, counts={activeCount.Text}/{completedCount.Text}, expected={expectedActive}/{expectedCompleted}, badgeInside={badgeInside}）。");
        }
        return pill;
    }

    private static ToggleButton CompletedSegment(MainWindow main) =>
        Required<ToggleButton>(main, "CompletedNotesSegment");

    private static void VerifyNotesPillGeometryAndStates(
        MainWindow main,
        ApplicationTheme theme,
        ICollection<string> failures)
    {
        var toolbar = Required<Grid>(main, "NotesListToolbar");
        var listCard = Required<Grid>(main, "NotesListCard");
        var notesList = Required<ScrollViewer>(main, "NotesListRegion");
        var host = Required<Grid>(main, "NotesSegmentHost");
        var container = host.Parent as Border
            ?? throw new InvalidOperationException("便签状态分段缺少外层槽 Border。");
        var layer = Required<Canvas>(main, "NotesSegmentIndicatorLayer");
        var indicator = Required<Border>(main, "NotesSegmentIndicator");
        var active = Required<ToggleButton>(main, "ActiveNotesSegment");
        var completed = Required<ToggleButton>(main, "CompletedNotesSegment");
        var search = Required<TextBox>(main, "NoteSearch");
        var buttons = host.Children.OfType<StackPanel>().Single();
        var toolbarBounds = Bounds(toolbar, listCard);
        var containerBounds = Bounds(container, toolbar);
        var searchBounds = Bounds(search, toolbar);

        if (Math.Abs(toolbarBounds.Height - 42) > GeometryTolerance ||
            Math.Abs(CenterY(containerBounds) - CenterY(searchBounds)) > GeometryTolerance ||
            container.Padding != new Thickness(2) ||
            container.BorderThickness != new Thickness(1) ||
            container.CornerRadius != (CornerRadius)main.FindResource("RadiusControl") ||
            !BrushMatches(container.Background, RequiredBrush(container, "CardAltBrush")) ||
            !BrushMatches(container.BorderBrush, RequiredBrush(container, "PeachLineBrush")))
        {
            failures.Add($"便签工具条/槽几何或画刷不符（toolbar={FormatRect(toolbarBounds)}, container={FormatRect(containerBounds)}, search={FormatRect(searchBounds)}, padding={container.Padding}, border={container.BorderThickness}）。");
        }

        var initialListHeight = notesList.ActualHeight;
        if (initialListHeight < 345 - GeometryTolerance)
        {
            failures.Add($"便签列表可读视口低于现役 345 DIP 下限（{initialListHeight:0.##}）。");
        }

        foreach (var showCompleted in new[] { false, true })
        {
            var selected = showCompleted ? completed : active;
            var unselected = showCompleted ? active : completed;
            selected.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            _ = WindowValidationInput.WaitUntil(
                main.Dispatcher,
                () => main.VisibleSegmentIndicatorsSettledForCapture,
                1000);
            selected.ApplyTemplate();
            unselected.ApplyTemplate();
            var selectedSurface = RequiredTemplate<Border>(selected, "Surface");
            var unselectedSurface = RequiredTemplate<Border>(unselected, "Surface");
            var surfaceBounds = Bounds(selectedSurface, host);
            var pillBounds = Bounds(indicator, host);
            var selectedRail = Required<Border>(
                main,
                showCompleted ? "CompletedNotesSegmentRail" : "ActiveNotesSegmentRail");
            var unselectedRail = Required<Border>(
                main,
                showCompleted ? "ActiveNotesSegmentRail" : "CompletedNotesSegmentRail");
            var badge = Required<Border>(
                main,
                showCompleted ? "CompletedNotesBadge" : "ActiveNotesBadge");
            var badgeBounds = Bounds(badge, host);

            if (selected.IsChecked != true || unselected.IsChecked == true ||
                !RectClose(surfaceBounds, pillBounds) ||
                Math.Abs(pillBounds.Height - 32) > GeometryTolerance ||
                indicator.CornerRadius != new CornerRadius(16))
            {
                failures.Add($"{selected.Name}：滑动胶囊未与 Surface 全矩形重合或不是 32/16 全圆角（surface={FormatRect(surfaceBounds)}, pill={FormatRect(pillBounds)}）。");
            }

            if (Panel.GetZIndex(layer) >= Panel.GetZIndex(buttons) ||
                layer.IsHitTestVisible || indicator.IsHitTestVisible || indicator.Focusable ||
                UIElementAutomationPeer.CreatePeerForElement(indicator) is not null)
            {
                failures.Add($"{selected.Name}：滑动胶囊未位于按钮底层、仍可命中/聚焦或进入了自动化树。");
            }

            var hitPoint = selectedSurface.TransformToAncestor(host).Transform(
                new Point(selectedSurface.RenderSize.Width / 2, selectedSurface.RenderSize.Height / 2));
            var hit = host.InputHitTest(hitPoint) as DependencyObject;
            while (hit is not null && hit != selected)
            {
                hit = VisualTreeHelper.GetParent(hit);
            }
            if (hit != selected)
            {
                failures.Add($"{selected.Name}：胶囊中心的真实命中未到达激活段。");
            }

            if (!IsTransparentBrush(selectedSurface.Background) ||
                !IsTransparentBrush(selectedSurface.BorderBrush) ||
                !IsTransparentBrush(unselectedSurface.Background) ||
                !IsTransparentBrush(unselectedSurface.BorderBrush))
            {
                failures.Add($"{selected.Name}：常态或激活态 Surface 不是透明无底无边，仍可能泄漏 #707070 冷灰边。");
            }

            if (selectedRail.Visibility != Visibility.Visible ||
                unselectedRail.Visibility != Visibility.Collapsed ||
                Math.Abs(selectedRail.ActualWidth - 3) > GeometryTolerance ||
                Math.Abs(selectedRail.ActualHeight - 19) > GeometryTolerance ||
                selectedRail.CornerRadius != new CornerRadius(1.5) ||
                !BrushMatches(selectedRail.Background, RequiredBrush(selectedRail, "PeachRailBrush")) ||
                !ApplicationTheme.MeetsContrast(
                    RequiredBrush(selectedRail, "PeachRailBrush"),
                    RequiredBrush(indicator, "PeachTintBrush"),
                    3.0))
            {
                failures.Add($"{selected.Name}：3×19/1.5 暖橙竖轨的显隐、尺寸或对蜜桃底对比度不符。");
            }

            if (badgeBounds.Left < pillBounds.Left - GeometryTolerance ||
                badgeBounds.Top < pillBounds.Top - GeometryTolerance ||
                badgeBounds.Right > pillBounds.Right + GeometryTolerance ||
                badgeBounds.Bottom > pillBounds.Bottom + GeometryTolerance)
            {
                failures.Add($"{selected.Name}：数量徽章越出滑动胶囊（badge={FormatRect(badgeBounds)}, pill={FormatRect(pillBounds)}）。");
            }

            var expectedActiveName = "查看未完成便签，共 1 条";
            var expectedCompletedName = "查看已完成便签，共 0 条";
            if (AutomationProperties.GetName(active) != expectedActiveName ||
                AutomationProperties.GetName(completed) != expectedCompletedName ||
                AutomationProperties.GetAutomationId(active) != "ActiveNotesSegment" ||
                AutomationProperties.GetAutomationId(completed) != "CompletedNotesSegment")
            {
                failures.Add("便签状态分段的可访问名或 AutomationId 发生变化。");
            }
        }

        active.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
        var activeSurface = RequiredTemplate<Border>(active, "Surface");
        var completedSurface = RequiredTemplate<Border>(completed, "Surface");
        WithPointerHover(main, completed, () =>
        {
            if (!BrushMatches(completedSurface.Background, RequiredBrush(completed, "CardAltBrush")) ||
                !BrushMatches(completedSurface.BorderBrush, RequiredBrush(completed, "ActiveHoverBrush")))
            {
                failures.Add("未选中便签状态段 hover 未呈现 CardAlt 暖底与 ActiveHover 暖边。");
            }
        });
        WithPointerHover(main, active, () =>
        {
            if (!IsTransparentBrush(activeSurface.Background) ||
                !IsTransparentBrush(activeSurface.BorderBrush))
            {
                failures.Add("选中便签状态段 hover 叠出了第二层底色或边框。");
            }
        });

        main.Activate();
        Keyboard.Focus(active);
        FocusVisualState.SetIsKeyboardFocusVisible(active, true);
        SettleLayout(main);
        var tabMoved = active.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        SettleLayout(main);
        var completedRing = RequiredTemplate<Border>(completed, "FocusRing");
        if (!tabMoved || !completed.IsKeyboardFocused ||
            !FocusVisualState.GetIsKeyboardFocusVisible(completed) ||
            completed.FocusVisualStyle is not null ||
            completedRing.BorderThickness != new Thickness(2) ||
            !BrushMatches(completedRing.BorderBrush, RequiredBrush(completed, "ActiveBrush")) ||
            !ApplicationTheme.MeetsContrast(completedRing.BorderBrush, container.Background, 3.0))
        {
            failures.Add("便签状态分段 Tab 顺序或单层 2 DIP Active 焦点环发生变化。");
        }

        VerifyNotesDayPill(indicator, "初始日间", failures);
        using (ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            SettleLayout(main);
            if (indicator.BorderThickness != new Thickness(2) ||
                !BrushMatchesColor(indicator.Background, SystemColors.WindowColor) ||
                !BrushMatchesColor(indicator.BorderBrush, SystemColors.HighlightColor))
            {
                failures.Add("系统对比度下便签状态胶囊不是 Window 底 + 2 DIP Highlight 轮廓。");
            }
        }
        theme.Apply();
        SettleLayout(main);
        VerifyNotesDayPill(indicator, "对比度往返后", failures);
        if (Math.Abs(notesList.ActualHeight - initialListHeight) > GeometryTolerance ||
            Math.Abs(toolbar.ActualHeight - 42) > GeometryTolerance)
        {
            failures.Add("交互与系统对比度往返改变了 42 DIP 工具条或便签列表视口高度。");
        }
    }

    private static void VerifyNotesDayPill(
        Border indicator,
        string label,
        ICollection<string> failures)
    {
        if (indicator.BorderThickness != new Thickness(0) ||
            !IsTransparentBrush(indicator.BorderBrush) ||
            !BrushMatches(indicator.Background, RequiredBrush(indicator, "PeachTintBrush")))
        {
            failures.Add($"{label}：便签状态胶囊不是零边框透明边 + PeachTint 填充。");
        }
    }

    private static void VerifyWorkspacePillFirstFrames(MainWindow main, ICollection<string> failures)
    {
        var host = Required<Grid>(main, "MainWorkspaceTabHost");
        var indicator = Required<Border>(main, "MainWorkspaceTabIndicator");
        var countdownTab = Required<ToggleButton>(main, "CountdownTab");
        var notesTab = Required<ToggleButton>(main, "NotesTab");

        void Verify(string label, WorkspaceKind start, WorkspaceKind expected, Key key)
        {
            main.ShowWorkspace(start, immediateIndicator: true);
            SettleLayout(main);
            var observed = false;
            var handled = WindowValidationInput.RaisePreviewKey(countdownTab, key, ModifierKeys.Control);
            var firstFrame = WindowValidationInput.PumpRenderedFrames(main.Dispatcher, 1000, () =>
            {
                var tab = expected == WorkspaceKind.Countdowns ? countdownTab : notesTab;
                tab.ApplyTemplate();
                var surface = RequiredTemplate<Border>(tab, "Surface");
                observed = main.CurrentWorkspace == expected &&
                    RectClose(Bounds(indicator, host), Bounds(surface, host)) &&
                    !WorkspacePillAnimating(indicator);
                return true;
            });
            if (!handled || !firstFrame.Completed || !observed)
            {
                failures.Add($"{label}：Ctrl 首帧未对齐目标胶囊，或 Width/Translate 仍有动画时钟。");
            }
        }

        Verify("Ctrl+2", WorkspaceKind.Countdowns, WorkspaceKind.Notes, Key.D2);
        Verify("Ctrl+1", WorkspaceKind.Notes, WorkspaceKind.Countdowns, Key.D1);

        main.ShowWorkspace(WorkspaceKind.Countdowns, immediateIndicator: true);
        SettleLayout(main);
        var visibilityChanges = new List<bool>();
        main.IsVisibleChanged += (_, args) => visibilityChanges.Add((bool)args.NewValue);
        main.Hide();
        new MainWindowEmptyStateProbe(main).ShowWorkspace(WorkspaceKind.Notes);
        WindowValidationInput.Pump(main.Dispatcher);
        main.Show();
        var revealObserved = false;
        var revealActual = default(Rect);
        var revealExpected = default(Rect);
        var revealAnimated = false;
        var revealFrame = WindowValidationInput.PumpRenderedFrames(main.Dispatcher, 1000, () =>
        {
            notesTab.ApplyTemplate();
            var surface = RequiredTemplate<Border>(notesTab, "Surface");
            revealActual = Bounds(indicator, host);
            revealExpected = Bounds(surface, host);
            revealAnimated = WorkspacePillAnimating(indicator);
            revealObserved = RectClose(revealActual, revealExpected) && !revealAnimated;
            return true;
        });
        if (!revealFrame.Completed || !revealObserved)
        {
            var laterAligned = WindowValidationInput.WaitUntil(main.Dispatcher, () =>
                main.VisibleSegmentIndicatorsSettledForCapture, 1000);
            failures.Add($"隐藏后首次揭示未即时对齐（actual={FormatRect(revealActual)}, expected={FormatRect(revealExpected)}, animated={revealAnimated}, visible={main.IsVisible}, hostLoaded={host.IsLoaded}, hostVisible={host.IsVisible}, notesLoaded={notesTab.IsLoaded}, notesVisible={notesTab.IsVisible}, visibility={string.Join(',', visibilityChanges)}, laterAligned={laterAligned}）。");
        }
    }

    private static void VerifyDayPill(Border indicator, string label, ICollection<string> failures)
    {
        if (indicator.BorderThickness != new Thickness(0) ||
            !IsTransparentBrush(indicator.BorderBrush) ||
            !BrushMatches(indicator.Background, RequiredBrush(indicator, "PinkTintBrush")))
        {
            failures.Add($"{label}：胶囊不是零边框透明边 + PinkTint 填充。");
        }
    }

    private static bool WorkspacePillAnimating(Border indicator) =>
        DependencyPropertyHelper.GetValueSource(indicator, FrameworkElement.WidthProperty).IsAnimated ||
        indicator.RenderTransform is Transform { HasAnimatedProperties: true };

    private static T RequiredTemplate<T>(Control control, string name) where T : class
    {
        control.ApplyTemplate();
        return control.Template?.FindName(name, control) as T
            ?? throw new InvalidOperationException($"{control.Name} 缺少模板部件 {name}。");
    }

    private static bool IsTransparentBrush(Brush? brush) =>
        brush is SolidColorBrush { Color.A: 0 };

    private static bool BrushMatches(Brush? actual, Brush? expected) =>
        actual is SolidColorBrush left && expected is SolidColorBrush right && left.Color == right.Color;

    private static bool BrushMatchesColor(Brush? actual, Color expected) =>
        actual is SolidColorBrush brush && brush.Color == expected;

    private static void VerifyCommandBarAlignment(MainWindow main, ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var surface = Required<Canvas>(main, "MainLayoutSurface");
        var commandBar = Required<Grid>(main, "MainCommandBarZone");
        var tabHost = Required<Grid>(main, "MainWorkspaceTabHost");
        var tabContainer = tabHost.Parent as Border
            ?? throw new InvalidOperationException("MainWorkspaceTabHost 缺少外层 Border 页签容器。");
        var countdownTab = Required<ToggleButton>(main, "CountdownTab");
        var notesTab = Required<ToggleButton>(main, "NotesTab");
        var gear = Required<Button>(main, "PreferencesButton");
        var filter = Required<Button>(main, "FilterButton");
        var tags = Required<Button>(main, "TagManagerButton");
        var count = Required<TextBlock>(main, "RecordCountText");

        Rect? initialTabContainerBounds = null;
        foreach (var (workspaceKind, contentSurfaceName, secondaryContentSurfaceName, label) in new[]
                 {
                     (WorkspaceKind.Countdowns, "CountdownEditorSurface", (string?)null, "倒计时页"),
                     (WorkspaceKind.Notes, "NoteEditorSurface", "NotesListSurface", "便签页")
                 })
        {
            probe.ShowWorkspace(workspaceKind);
            SettleLayout(main);
            _ = WindowValidationInput.PumpRenderedFrames(
                main.Dispatcher,
                500,
                () => main.VisibleSegmentIndicatorsSettledForCapture);
            SettleLayout(main);

            var commandBounds = Bounds(commandBar, surface);
            var tabContainerBounds = Bounds(tabContainer, surface);
            var gearBounds = Bounds(gear, surface);
            var filterBounds = Bounds(filter, surface);
            var tagsBounds = Bounds(tags, surface);
            var countBounds = Bounds(count, surface);
            var contentBounds = Bounds(Required<FrameworkElement>(main, contentSurfaceName), surface);

            if (initialTabContainerBounds is null)
            {
                initialTabContainerBounds = tabContainerBounds;
            }
            else if (!RectClose(initialTabContainerBounds.Value, tabContainerBounds))
            {
                failures.Add($"{label}：切页后页签容器几何变化（原 {FormatRect(initialTabContainerBounds.Value)}，现 {FormatRect(tabContainerBounds)}）");
            }

            if (Math.Abs(tabContainerBounds.Left - 30) > GeometryTolerance ||
                Math.Abs(tabContainerBounds.Width - 190) > GeometryTolerance ||
                Math.Abs(tabContainerBounds.Height - 50) > GeometryTolerance ||
                tabContainer.Padding != new Thickness(3, 2, 2, 2) ||
                Math.Abs(countdownTab.ActualWidth - 99) > GeometryTolerance ||
                Math.Abs(countdownTab.ActualHeight - 44) > GeometryTolerance ||
                Math.Abs(notesTab.ActualWidth - 82) > GeometryTolerance ||
                Math.Abs(notesTab.ActualHeight - 44) > GeometryTolerance)
            {
                failures.Add($"{label}：页签容器或页签尺寸偏离现役几何（container={FormatRect(tabContainerBounds)}, padding={tabContainer.Padding}）");
            }

            if (!main.VisibleSegmentIndicatorsSettledForCapture ||
                (workspaceKind == WorkspaceKind.Notes && !main.SegmentIndicatorGeometryMatchesForValidation))
            {
                failures.Add($"{label}：选中指示条未对齐当前激活页签/分段。");
            }

            if (Math.Abs(gearBounds.Height - filterBounds.Height) > GeometryTolerance ||
                Math.Abs(tagsBounds.Height - filterBounds.Height) > GeometryTolerance)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：三个命令按钮不等高（gear={gearBounds.Height:0.##}, filter={filterBounds.Height:0.##}, tags={tagsBounds.Height:0.##}）"));
            }

            if (gearBounds.Height < MinimumSecondaryHitHeight ||
                gearBounds.Width < 41 - GeometryTolerance ||
                filterBounds.Height < MinimumSecondaryHitHeight ||
                filterBounds.Width < 99 - GeometryTolerance ||
                tagsBounds.Height < MinimumSecondaryHitHeight ||
                tagsBounds.Width < 98 - GeometryTolerance ||
                gearBounds.Height < MinimumCompactHitTarget)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：命令按钮命中区低于下限（gear={gearBounds.Width:0.##}x{gearBounds.Height:0.##}, filter={filterBounds.Width:0.##}x{filterBounds.Height:0.##}, tags={tagsBounds.Width:0.##}x{tagsBounds.Height:0.##}）"));
            }

            var barCenterY = CenterY(commandBounds);
            var gearCenterY = CenterY(gearBounds);
            var filterCenterY = CenterY(filterBounds);
            var tagsCenterY = CenterY(tagsBounds);
            var countCenterY = CenterY(countBounds);
            if (Math.Abs(gearCenterY - barCenterY) > GeometryTolerance ||
                Math.Abs(filterCenterY - barCenterY) > GeometryTolerance ||
                Math.Abs(tagsCenterY - barCenterY) > GeometryTolerance)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：三个命令按钮未垂直居中或中线不一致（bar={barCenterY:0.##}, gear={gearCenterY:0.##}, filter={filterCenterY:0.##}, tags={tagsCenterY:0.##}）"));
            }

            if (Math.Abs(countCenterY - filterCenterY) > GeometryTolerance ||
                count.Padding != new Thickness(0))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：数量文字未与命令按钮共用垂直中线（count={countCenterY:0.##}, filter={filterCenterY:0.##}, padding={count.Padding}）"));
            }

            if (Math.Abs(commandBounds.Right - contentBounds.Right) > GeometryTolerance ||
                Math.Abs(countBounds.Right - contentBounds.Right) > GeometryTolerance)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：命令栏右缘未对齐内容区右缘（barRight={commandBounds.Right:0.##}, countRight={countBounds.Right:0.##}, contentRight={contentBounds.Right:0.##}）"));
            }

            if (secondaryContentSurfaceName is not null)
            {
                var secondaryBounds = Bounds(Required<FrameworkElement>(main, secondaryContentSurfaceName), surface);
                if (Math.Abs(commandBounds.Right - secondaryBounds.Right) > GeometryTolerance)
                {
                    failures.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{label}：命令栏右缘未对齐列表区右缘（barRight={commandBounds.Right:0.##}, listRight={secondaryBounds.Right:0.##}）"));
                }
            }
        }
    }

    private static void VerifyTitleInvariantAcrossTabs(MainWindow main, ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var surface = Required<Canvas>(main, "MainLayoutSurface");
        var titleZone = Required<StackPanel>(main, "MainTitleZone");
        var titleText = VisualDescendants<TextBlock>(titleZone)
            .Single(text => string.Equals(text.Text, "敏敏子的时光小屋", StringComparison.Ordinal));
        var subtitleText = VisualDescendants<TextBlock>(titleZone)
            .Single(text => string.Equals(text.Text, "重要时刻和今日努力，都在这里慢慢长大", StringComparison.Ordinal));
        var motifs = VisualDescendants<WpfPath>(titleZone).ToArray();
        if (motifs.Length != 2)
        {
            failures.Add($"主标题区应保留两枚页级装饰图形（心形与小屋），实际 {motifs.Length} 枚。");
            return;
        }

        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        var countdownTitle = Bounds(titleText, surface);
        var countdownSubtitle = Bounds(subtitleText, surface);
        var visibleOnCountdowns = motifs.Where(path => path.IsVisible).ToArray();
        if (visibleOnCountdowns.Length != 1)
        {
            failures.Add($"倒计时页标题区应只显示一枚装饰图形，实际 {visibleOnCountdowns.Length} 枚。");
        }

        var countdownMotif = visibleOnCountdowns.FirstOrDefault();
        if (countdownMotif is not null)
        {
            var motifBounds = Bounds(countdownMotif, surface);
            if (countdownMotif.IsHitTestVisible || motifBounds.Left < countdownTitle.Right - GeometryTolerance)
            {
                failures.Add($"倒计时页标题装饰应位于标题之后且不可命中（motif={FormatRect(motifBounds)}, title={FormatRect(countdownTitle)}）。");
            }
        }

        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);
        var notesTitle = Bounds(titleText, surface);
        var notesSubtitle = Bounds(subtitleText, surface);
        var visibleOnNotes = motifs.Where(path => path.IsVisible).ToArray();
        if (visibleOnNotes.Length != 1)
        {
            failures.Add($"便签页标题区应只显示一枚装饰图形，实际 {visibleOnNotes.Length} 枚。");
        }

        var notesMotif = visibleOnNotes.FirstOrDefault();
        if (notesMotif is not null)
        {
            var motifBounds = Bounds(notesMotif, surface);
            if (notesMotif.IsHitTestVisible || motifBounds.Left < notesTitle.Right - GeometryTolerance)
            {
                failures.Add($"便签页标题装饰应位于标题之后且不可命中（motif={FormatRect(motifBounds)}, title={FormatRect(notesTitle)}）。");
            }
        }

        if (countdownMotif is not null && notesMotif is not null && ReferenceEquals(countdownMotif, notesMotif))
        {
            failures.Add("倒计时页与便签页未保留各自独立的标题装饰图形。");
        }

        if (Math.Abs(countdownTitle.Left - notesTitle.Left) > GeometryTolerance ||
            Math.Abs(countdownTitle.Top - notesTitle.Top) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"切页前后主标题位置变化（countdown={FormatRect(countdownTitle)}, notes={FormatRect(notesTitle)}）"));
        }

        if (Math.Abs(countdownTitle.Left - countdownSubtitle.Left) > GeometryTolerance ||
            Math.Abs(notesTitle.Left - notesSubtitle.Left) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"主标题与副标题左缘未对齐（countdownTitle={countdownTitle.Left:0.##}, subtitle={countdownSubtitle.Left:0.##}, notesTitle={notesTitle.Left:0.##}）"));
        }

        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        var restoredTitle = Bounds(titleText, surface);
        if (Math.Abs(restoredTitle.Left - countdownTitle.Left) > GeometryTolerance)
        {
            failures.Add("切回倒计时页后主标题水平位置未保持不变。");
        }
    }

    private static void VerifyNoteEditorAddAndEditRows(
        MainWindow main,
        DesktopWorkspace workspace,
        ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var surface = Required<Canvas>(main, "MainLayoutSurface");
        var editorSurface = Required<Border>(main, "NoteEditorSurface");
        var noteText = Required<TextBox>(main, "NoteText");
        var noteDate = Required<DatePicker>(main, "NoteDate");
        var noteDateLabel = Required<TextBlock>(main, "NoteDateLabel");
        var saveButton = Required<Button>(main, "SaveNoteButton");
        var cancelButton = Required<Button>(main, "CancelNoteEdit");
        var secondaryReference = Required<Button>(main, "ManageNoteReminderRulesButton");

        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);

        if (cancelButton.Visibility != Visibility.Collapsed)
        {
            failures.Add("便签新增态不应显示取消编辑按钮。");
        }

        var cardAdd = Bounds(editorSurface, surface);
        var textAdd = Bounds(noteText, surface);
        var dateAdd = Bounds(noteDate, surface);
        var dateLabelAdd = Bounds(noteDateLabel, surface);
        var saveAdd = Bounds(saveButton, surface);
        var leadingAdd = textAdd.Left - cardAdd.Left;
        var trailingAdd = cardAdd.Right - saveAdd.Right;
        if (Math.Abs(trailingAdd - leadingAdd) > GeometryTolerance || leadingAdd < 16)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签新增态行尾留白不等于行首留白（leading={leadingAdd:0.##}, trailing={trailingAdd:0.##}）"));
        }

        if (Math.Abs(dateLabelAdd.Left - dateAdd.Left) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签新增态日期标签未与日期框左对齐（label={dateLabelAdd.Left:0.##}, date={dateAdd.Left:0.##}）"));
        }

        var targetNote = workspace.Snapshot.Settings.Notes.First();
        main.LoadNoteIntoEditor(targetNote);
        SettleLayout(main);

        if (!cancelButton.IsVisible)
        {
            failures.Add("便签编辑态未显示取消按钮。");
            return;
        }

        var cardEdit = Bounds(editorSurface, surface);
        var textEdit = Bounds(noteText, surface);
        var dateEdit = Bounds(noteDate, surface);
        var dateLabelEdit = Bounds(noteDateLabel, surface);
        var saveEdit = Bounds(saveButton, surface);
        var cancelEdit = Bounds(cancelButton, surface);
        var leadingEdit = textEdit.Left - cardEdit.Left;
        var trailingEdit = cardEdit.Right - cancelEdit.Right;
        if (Math.Abs(trailingEdit - leadingEdit) > GeometryTolerance ||
            Math.Abs(leadingEdit - leadingAdd) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签编辑态行尾留白不等于行首留白（leading={leadingEdit:0.##}, trailing={trailingEdit:0.##}, addLeading={leadingAdd:0.##}）"));
        }

        if (Math.Abs(dateLabelEdit.Left - dateEdit.Left) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签编辑态日期标签未与日期框左对齐（label={dateLabelEdit.Left:0.##}, date={dateEdit.Left:0.##}）"));
        }

        var expectedShrink = cancelEdit.Width + cancelButton.Margin.Left;
        var actualShrink = textAdd.Width - textEdit.Width;
        if (actualShrink <= 20 || Math.Abs(actualShrink - expectedShrink) > 1.0)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"便签编辑态正文列未吸收剩余宽度（addBody={textAdd.Width:0.##}, editBody={textEdit.Width:0.##}, expectedShrink={expectedShrink:0.##}）"));
        }

        if (cancelButton.Padding != secondaryReference.Padding)
        {
            failures.Add($"便签取消按钮内边距（{cancelButton.Padding}）与次级按钮（{secondaryReference.Padding}）不一致。");
        }

        if (cancelEdit.Left < saveEdit.Right + 4 - GeometryTolerance ||
            Math.Abs(cancelEdit.Height - saveEdit.Height) > GeometryTolerance ||
            Math.Abs(dateEdit.Width - 242) > GeometryTolerance)
        {
            failures.Add($"便签编辑态控件几何异常（dateW={dateEdit.Width:0.##}, save={FormatRect(saveEdit)}, cancel={FormatRect(cancelEdit)}）。");
        }

        cancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        SettleLayout(main);
        var textAfterCancel = Bounds(noteText, surface);
        var saveAfterCancel = Bounds(saveButton, surface);
        if (cancelButton.Visibility != Visibility.Collapsed ||
            Math.Abs(textAfterCancel.Width - textAdd.Width) > GeometryTolerance ||
            Math.Abs((cardAdd.Right - saveAfterCancel.Right) - leadingAdd) > GeometryTolerance)
        {
            failures.Add("取消便签编辑后未恢复新增态正文宽度与行尾留白。");
        }
    }

    private static void VerifyEmptyStateClickableAffordance(
        MainWindow main,
        DesktopWorkspace workspace,
        ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var primaryBrush = RequiredBrush(main, "MainActionBrush");
        var activeBrush = RequiredBrush(main, "ActiveBrush");
        var hoverBorderBrush = RequiredBrush(main, "ActiveHoverBrush");
        var hoverBgBrush = RequiredBrush(main, "PinkTintBrush");

        foreach (var (workspaceKind, guideName, editorName, outerBgKey, draft, countGetter, label) in new (
                     WorkspaceKind, string, string, string, string, Func<int>, string)[]
                 {
                     (WorkspaceKind.Countdowns, "AddEmptyCountdownButton", "CountdownName", "BackgroundBrush",
                         "空态点击不丢草稿-倒计时", () => workspace.Snapshot.Settings.Countdowns.Count, "倒计时首次空态引导"),
                     (WorkspaceKind.Notes, "AddEmptyNoteButton", "NoteText", "CardBrush",
                         "空态点击不丢草稿-便签", () => workspace.Snapshot.Settings.Notes.Count, "便签首次空态引导")
                 })
        {
            probe.ShowWorkspace(workspaceKind);
            main.Activate();
            Keyboard.Focus(Required<ToggleButton>(main, "CountdownTab"));
            SettleLayout(main);

            var guide = Required<Button>(main, guideName);
            var editor = Required<TextBox>(main, editorName);
            var outerBg = RequiredBrush(main, outerBgKey);
            guide.ApplyTemplate();
            var surface = guide.Template?.FindName("Surface", guide) as Border;
            var focusRing = guide.Template?.FindName("FocusRing", guide) as Border;
            if (!guide.IsVisible || surface is null || focusRing is null)
            {
                failures.Add($"{label}：按钮不可见或缺少模板部件 Surface/FocusRing。");
                continue;
            }

            if (guide.Cursor != Cursors.Hand || guide.ActualHeight < MinimumSecondaryHitHeight)
            {
                failures.Add($"{label}：常态手型指针或命中高度不足（cursor={guide.Cursor}, height={guide.ActualHeight:0.##}）。");
            }

            var restBg = surface.Background as SolidColorBrush;
            var restBorder = surface.BorderBrush as SolidColorBrush;
            var restFg = guide.Foreground as SolidColorBrush;
            if (restBg is null || restBg.Color.A != byte.MaxValue ||
                restBorder is null || restBorder.Color.A != byte.MaxValue ||
                surface.BorderThickness.Left < 1 || surface.BorderThickness.Top < 1 ||
                restFg is null || restFg.Color.A != byte.MaxValue)
            {
                failures.Add($"{label}：常态未呈现可辨认的不透明表面与边框（bg={surface.Background}, border={surface.BorderBrush}, thickness={surface.BorderThickness}）。");
            }
            else
            {
                if (restBg.Color == primaryBrush.Color)
                {
                    failures.Add($"{label}：空态引导不应冒充页面唯一实心主行动底色。");
                }

                if (!ApplicationTheme.MeetsContrast(outerBg.Color, restBorder.Color, 3.0) ||
                    !ApplicationTheme.MeetsContrast(restBg.Color, restBorder.Color, 3.0))
                {
                    failures.Add($"{label}：常态边框对比度不足 3:1（outer={outerBg.Color}, bg={restBg.Color}, border={restBorder.Color}）。");
                }

                if (!ApplicationTheme.MeetsContrast(restBg.Color, restFg.Color, 4.5))
                {
                    failures.Add($"{label}：常态文字对比度不足 4.5:1（bg={restBg.Color}, fg={restFg.Color}）。");
                }
            }

            var cuePath = VisualDescendants<WpfPath>(surface).FirstOrDefault(path => path.IsVisible);
            if (cuePath is null || cuePath.IsHitTestVisible)
            {
                failures.Add($"{label}：常态缺少可见且不可命中的矢量方向提示图标。");
            }

            // 悬停态反馈
            var restingBounds = Bounds(guide, main);
            WithPointerHover(main, guide, () =>
            {
                var hoverBg = surface.Background as SolidColorBrush;
                var hoverBorder = surface.BorderBrush as SolidColorBrush;
                if (!guide.IsMouseOver || hoverBg is null || hoverBorder is null ||
                    hoverBg.Color != hoverBgBrush.Color ||
                    hoverBorder.Color != hoverBorderBrush.Color ||
                    (restFg is not null && !ApplicationTheme.MeetsContrast(hoverBg.Color, restFg.Color, 4.5)) ||
                    !ApplicationTheme.MeetsContrast(outerBg.Color, hoverBorder.Color, 3.0))
                {
                    failures.Add($"{label}：悬停反馈色或对比度不符合合同（over={guide.IsMouseOver}, bg={surface.Background}, border={surface.BorderBrush}）。");
                }
            });

            // 键盘焦点环单层且不挤布局
            Keyboard.Focus(guide);
            FocusVisualState.SetIsKeyboardFocusVisible(guide, true);
            SettleLayout(main);
            var focusedBounds = Bounds(guide, main);
            var ringBrush = focusRing.BorderBrush as SolidColorBrush;
            if (!guide.IsKeyboardFocused ||
                ringBrush is null ||
                ringBrush.Color != activeBrush.Color ||
                !ApplicationTheme.MeetsContrast(outerBg.Color, ringBrush.Color, 3.0) ||
                !KeyboardFocusPolicy.KeyboardFocusDrawnOnce(guide) ||
                !RectClose(restingBounds, focusedBounds))
            {
                failures.Add($"{label}：键盘焦点环颜色、单层描边或布局稳定性异常（focused={guide.IsKeyboardFocused}, ring={focusRing.BorderBrush}）。");
            }

            // 点击只把焦点交给表单输入，保留草稿且不新增记录
            editor.Text = draft;
            Keyboard.Focus(guide);
            SettleLayout(main);
            guide.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            if (!ReferenceEquals(Keyboard.FocusedElement, editor) ||
                !string.Equals(editor.Text, draft, StringComparison.Ordinal) ||
                countGetter() != 0)
            {
                failures.Add($"{label}：点击后未保持「只聚焦输入、保留草稿、不新增记录」（focused={Keyboard.FocusedElement}, text={editor.Text}, count={countGetter()}）。");
            }

            editor.Text = string.Empty;
            SettleLayout(main);
        }
    }

    private static void VerifyHighContrastDecorativeRibbonCollapse(
        MainWindow main,
        ApplicationTheme theme,
        ICollection<string> failures)
    {
        var probe = new MainWindowEmptyStateProbe(main);
        var surface = Required<Canvas>(main, "MainLayoutSurface");
        var commandBar = Required<Grid>(main, "MainCommandBarZone");
        var statusBar = Required<Border>(main, "MainStatusBarSurface");
        var countdownEditor = Required<Border>(main, "CountdownEditorSurface");
        var countdownList = Required<ScrollViewer>(main, "CountdownListRegion");
        var noteEditor = Required<Border>(main, "NoteEditorSurface");
        var notesListSurface = Required<Border>(main, "NotesListSurface");
        var notesList = Required<ScrollViewer>(main, "NotesListRegion");
        var decorationLayer = Required<Canvas>(main, "MainReferenceDecorationLayer");

        // 1) 记录日间基线布局
        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        var dayCommandBottom = Bounds(commandBar, surface).Bottom;
        var dayCountdownEditorTop = Bounds(countdownEditor, surface).Top;
        var dayCountdownListHeight = countdownList.ActualHeight;
        var dayCountdownGap = dayCountdownEditorTop - dayCommandBottom;

        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);
        var dayNotesEditorTop = Bounds(noteEditor, surface).Top;
        var dayNotesListHeight = notesList.ActualHeight;
        var dayNotesGap = dayNotesEditorTop - dayCommandBottom;

        if (dayCountdownGap < 100 || dayNotesGap < 110)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"日间装饰带高度异常（countdownGap={dayCountdownGap:0.##}, notesGap={dayNotesGap:0.##}）"));
        }

        // 2) 开启系统对比度：装饰带与布局空间全部收起，不留空白，且空态引导在系统对比度下保持可读可辨
        using (ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            SettleLayout(main);

            if (decorationLayer.Visibility != Visibility.Collapsed)
            {
                failures.Add("系统对比度开启时主装饰图层未收起。");
            }

            foreach (var spacerName in new[]
                     {
                         "MainDecorationBandSpacer",
                         "CountdownDecorationSpacer",
                         "CountdownDecorationBottomSpacer",
                         "NotesDecorationSpacer"
                     })
            {
                var spacer = main.FindName(spacerName) as FrameworkElement;
                if (spacer is null || spacer.Visibility != Visibility.Collapsed || spacer.ActualHeight > 0.01)
                {
                    failures.Add($"系统对比度开启时装饰占位 {spacerName} 未 Collapsed（vis={spacer?.Visibility}, h={spacer?.ActualHeight:0.##}）。");
                }
            }

            probe.ShowWorkspace(WorkspaceKind.Countdowns);
            SettleLayout(main);
            var hcCommandBottom = Bounds(commandBar, surface).Bottom;
            var hcCountdownEditorTop = Bounds(countdownEditor, surface).Top;
            var hcCountdownGap = hcCountdownEditorTop - hcCommandBottom;
            if (hcCountdownGap < ExpectedBlockGap - GeometryTolerance ||
                hcCountdownGap > ExpectedBlockGap + GeometryTolerance ||
                countdownList.ActualHeight <= dayCountdownListHeight + 90)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"系统对比度下倒计时页装饰带仍留空白（gap={hcCountdownGap:0.##}, dayListH={dayCountdownListHeight:0.##}, hcListH={countdownList.ActualHeight:0.##}）"));
            }

            VerifyEmptyGuideHighContrast(main, "AddEmptyCountdownButton", "倒计时空态引导（高对比度）", failures);

            probe.ShowWorkspace(WorkspaceKind.Notes);
            SettleLayout(main);
            var hcNotesEditorTop = Bounds(noteEditor, surface).Top;
            var hcNotesGap = hcNotesEditorTop - hcCommandBottom;
            var hcNotesToStatus = Bounds(statusBar, surface).Top - Bounds(notesListSurface, surface).Bottom;
            if (hcNotesGap < ExpectedBlockGap - GeometryTolerance ||
                hcNotesGap > ExpectedBlockGap + GeometryTolerance ||
                Math.Abs(hcNotesToStatus - ExpectedBlockGap) > 1.0 ||
                notesList.ActualHeight <= dayNotesListHeight + 90)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"系统对比度下便签页装饰带仍留空白（gap={hcNotesGap:0.##}, toStatus={hcNotesToStatus:0.##}, dayListH={dayNotesListHeight:0.##}, hcListH={notesList.ActualHeight:0.##}）"));
            }

            VerifyEmptyGuideHighContrast(main, "AddEmptyNoteButton", "便签空态引导（高对比度）", failures);

            // 对比度已开启时新打开的管理窗口也必须直接收起装饰带空间
            WithMainWindow(seedData: false, (freshMain, _) =>
            {
                var freshProbe = new MainWindowEmptyStateProbe(freshMain);
                var freshSurface = Required<Canvas>(freshMain, "MainLayoutSurface");
                var freshBarBottom = Bounds(Required<Grid>(freshMain, "MainCommandBarZone"), freshSurface).Bottom;
                freshProbe.ShowWorkspace(WorkspaceKind.Countdowns);
                SettleLayout(freshMain);
                var freshCountdownGap = Bounds(Required<Border>(freshMain, "CountdownEditorSurface"), freshSurface).Top - freshBarBottom;
                freshProbe.ShowWorkspace(WorkspaceKind.Notes);
                SettleLayout(freshMain);
                var freshNotesGap = Bounds(Required<Border>(freshMain, "NoteEditorSurface"), freshSurface).Top - freshBarBottom;
                if (Math.Abs(freshCountdownGap - ExpectedBlockGap) > GeometryTolerance ||
                    Math.Abs(freshNotesGap - ExpectedBlockGap) > GeometryTolerance)
                {
                    failures.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"系统对比度已开启时新开主窗未直接收起装饰带（countdownGap={freshCountdownGap:0.##}, notesGap={freshNotesGap:0.##}）"));
                }
            });
        }

        // 3) 关闭系统对比度：完整恢复日间布局与装饰带
        theme.Apply();
        SettleLayout(main);
        if (decorationLayer.Visibility != Visibility.Visible)
        {
            failures.Add("关闭系统对比度后主装饰图层未恢复显示。");
        }

        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        SettleLayout(main);
        var restoredCountdownEditorTop = Bounds(countdownEditor, surface).Top;
        if (Math.Abs(restoredCountdownEditorTop - dayCountdownEditorTop) > GeometryTolerance ||
            Math.Abs(countdownList.ActualHeight - dayCountdownListHeight) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"关闭系统对比度后倒计时页未恢复原样（top={restoredCountdownEditorTop:0.##} vs {dayCountdownEditorTop:0.##}, listH={countdownList.ActualHeight:0.##} vs {dayCountdownListHeight:0.##}）"));
        }

        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);
        var restoredNotesEditorTop = Bounds(noteEditor, surface).Top;
        if (Math.Abs(restoredNotesEditorTop - dayNotesEditorTop) > GeometryTolerance ||
            Math.Abs(notesList.ActualHeight - dayNotesListHeight) > GeometryTolerance)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"关闭系统对比度后便签页未恢复原样（top={restoredNotesEditorTop:0.##} vs {dayNotesEditorTop:0.##}, listH={notesList.ActualHeight:0.##} vs {dayNotesListHeight:0.##}）"));
        }
    }

    private static void VerifyEmptyGuideHighContrast(
        MainWindow main,
        string guideName,
        string label,
        ICollection<string> failures)
    {
        var guide = Required<Button>(main, guideName);
        guide.ApplyTemplate();
        var surface = guide.Template?.FindName("Surface", guide) as Border;
        var focusRing = guide.Template?.FindName("FocusRing", guide) as Border;
        if (surface is null || focusRing is null)
        {
            failures.Add($"{label}：缺少模板部件 Surface/FocusRing。");
            return;
        }

        if (surface.Background is not SolidColorBrush bg || bg.Color != SystemColors.WindowColor ||
            surface.BorderBrush is not SolidColorBrush border || border.Color != SystemColors.WindowTextColor ||
            guide.Foreground is not SolidColorBrush fg || fg.Color != SystemColors.WindowTextColor)
        {
            failures.Add($"{label}：系统对比度常态映射不正确（bg={surface.Background}, border={surface.BorderBrush}, fg={guide.Foreground}）。");
        }

        main.Activate();
        Keyboard.Focus(guide);
        FocusVisualState.SetIsKeyboardFocusVisible(guide, true);
        SettleLayout(main);
        if (focusRing.BorderBrush is not SolidColorBrush ring || ring.Color != SystemColors.HighlightColor)
        {
            failures.Add($"{label}：系统对比度焦点环未映射系统高亮色（ring={focusRing.BorderBrush}）。");
        }
    }

    private static void WithMainWindow(
        bool seedData,
        Action<MainWindow, DesktopWorkspace> verify,
        Action<DesktopWorkspace>? prepare = null)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-align-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            if (seedData)
            {
                SaveCountdown(workspace, "命令栏对齐倒计时", 5, Application.Current.Dispatcher);
                SaveNote(workspace, "便签编辑行对齐验证正文", Application.Current.Dispatcher);
            }
            prepare?.Invoke(workspace);

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
            main = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            var contentRendered = false;
            main.ContentRendered += (_, _) => contentRendered = true;
            main.Show();
            _ = WindowValidationInput.PumpRenderedFrames(main.Dispatcher, 2000, () => contentRendered);
            SettleLayout(main);
            verify(main, workspace);
        }
        finally
        {
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

    private static void WithPointerHover(MainWindow main, Control target, Action verify)
    {
        target.BringIntoView();
        SettleLayout(main);
        if (PresentationSource.FromVisual(target) is not HwndSource source ||
            source.RootVisual is not Visual root)
        {
            throw new InvalidOperationException($"{target.Name} 缺少用于悬停探针的 HWND。");
        }

        var point = target.TransformToAncestor(root).Transform(
            new Point(target.RenderSize.Width / 2, target.RenderSize.Height / 2));
        point = source.CompositionTarget.TransformToDevice.Transform(point);
        var coordinates = new IntPtr(((int)point.Y << 16) | ((int)point.X & 0xffff));
        main.Activate();
        SetCapture(source.Handle);
        try
        {
            if (GetCapture() != source.Handle)
            {
                throw new InvalidOperationException("隔离验证窗口无法捕获鼠标悬停消息。");
            }

            SendMessage(source.Handle, 0x0200, IntPtr.Zero, coordinates);
            SettleLayout(main);
            verify();
        }
        finally
        {
            if (GetCapture() == source.Handle)
            {
                ReleaseCapture();
            }

            SendMessage(source.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
            SettleLayout(main);
        }
    }

    private static double CenterY(Rect rect) => rect.Top + (rect.Height / 2);

    private static bool RectClose(Rect a, Rect b) =>
        Math.Abs(a.Left - b.Left) <= GeometryTolerance &&
        Math.Abs(a.Top - b.Top) <= GeometryTolerance &&
        Math.Abs(a.Width - b.Width) <= GeometryTolerance &&
        Math.Abs(a.Height - b.Height) <= GeometryTolerance;

    private static string FormatRect(Rect r) =>
        string.Create(CultureInfo.InvariantCulture, $"({r.Left:0.##},{r.Top:0.##},{r.Width:0.##}x{r.Height:0.##})");

    private static SolidColorBrush RequiredBrush(FrameworkElement element, string key) =>
        element.FindResource(key) as SolidColorBrush
        ?? throw new InvalidOperationException($"缺少画刷资源 {key}。");

    private static T Required<T>(MainWindow main, string name) where T : class =>
        main.FindName(name) as T ?? throw new InvalidOperationException($"管理窗口缺少控件 {name}（{typeof(T).Name}）。");

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
}
