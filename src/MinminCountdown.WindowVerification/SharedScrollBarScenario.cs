using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #306：共享滚动条静止 4 DIP、悬停命中轨或拖动展开到 10 DIP、命中轨 10 DIP、颜色对比度 >= 3:1、
// 仅用变换消费 MotionHoverDuration 并尊重系统减弱动画、四类滚动交互正常、悬浮窗 4 DIP 滚动条不变。
internal static class SharedScrollBarScenario
{
    private const double ExpectedHitRailDip = 10.0;
    private const double ExpectedRestingDip = 4.0;
    private const double ExpectedExpandedDip = 10.0;
    private const double GeometryToleranceDip = 0.15;
    private const double MinimumGraphicContrast = 3.0;
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) RunRestingThinAndExpandBehavior(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
        {
            theme.Apply();
            try
            {
                VerifyMainWindowSurfaces(theme, failures);
                VerifyReminderCenterSurface(failures);
                VerifyReminderRulesEditorSurface(failures);
                VerifyFloatingScrollBarUnchanged(failures);
            }
            catch (Exception exception)
            {
                failures.Add($"共享滚动条形态与对比度验收异常：{exception.Message}");
            }
        }

        theme.Apply();
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunMotionAndInteractions(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        try
        {
            VerifyMotionAndReducedMotion(theme, failures);
            VerifyAllScrollInteractions(theme, failures);
        }
        catch (Exception exception)
        {
            failures.Add($"共享滚动条动效与交互验收异常：{exception.Message}");
        }
        finally
        {
            theme.Apply();
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    private static void VerifyMainWindowSurfaces(ApplicationTheme theme, ICollection<string> failures)
    {
        WithPopulatedMainWindow(
            userSize: null,
            tagCount: 60,
            countdownCount: 12,
            noteCount: 14,
            (main, workspace) =>
            {
                _ = workspace;
                var probe = new MainWindowEmptyStateProbe(main);
                var canvasColor = RequiredBrushColor("CanvasBrush");
                var backgroundColor = RequiredBrushColor("BackgroundBrush");
                var cardColor = RequiredBrushColor("CardBrush");

                // 1) 管理窗口倒计时列表
                probe.ShowWorkspace(WorkspaceKind.Countdowns);
                SettleLayout(main);
                var countdownList = RequiredElement<ScrollViewer>(main, "CountdownListRegion");
                VerifySharedScrollBar("MainWindow.CountdownListRegion", countdownList, Orientation.Vertical, [canvasColor, backgroundColor, cardColor], failures);

                // 2) 管理窗口便签列表
                probe.ShowWorkspace(WorkspaceKind.Notes);
                SettleLayout(main);
                var notesList = RequiredElement<ScrollViewer>(main, "NotesListRegion");
                VerifySharedScrollBar(
                    "MainWindow.NotesListRegion",
                    notesList,
                    Orientation.Vertical,
                    [cardColor, RequiredBrushColor("NoteBrush"), RequiredBrushColor("NoteWhiteBrush")],
                    failures);

                // 3) 弹层内滚动：筛选弹层
                var filterButton = RequiredElement<Button>(main, "FilterButton");
                var filterPopup = RequiredElement<Popup>(main, "FilterPopup");
                VerifyPopupScrollViewer(
                    main,
                    "MainWindow.FilterPopup",
                    filterPopup,
                    () => InvokeButtonOrFail(filterButton, "FilterButton"),
                    canvasColor,
                    failures);

                // 4) 弹层内滚动：标签管理弹层
                var tagManagerButton = RequiredElement<Button>(main, "TagManagerButton");
                var tagManagerPopup = RequiredElement<Popup>(main, "TagManagerPopup");
                VerifyPopupScrollViewer(
                    main,
                    "MainWindow.TagManagerPopup",
                    tagManagerPopup,
                    () => InvokeButtonOrFail(tagManagerButton, "TagManagerButton"),
                    canvasColor,
                    failures);

                // 5) 弹层内滚动：标签选择器弹层
                var noteTags = RequiredElement<TagSelector>(main, "NoteTags");
                VerifyPopupScrollViewer(
                    main,
                    "MainWindow.NoteTagsPopup",
                    noteTags.Popup,
                    () => InvokeButtonOrFail(noteTags.PopupTrigger, "NoteTags.PopupTrigger"),
                    canvasColor,
                    failures);

                // 6) 系统对比度主题映射复核
                var bar = RequireScrollBar(notesList, Orientation.Vertical, "MainWindow.NotesListRegion.HighContrast", failures);
                if (bar is not null)
                {
                    using (ApplicationTheme.OverrideHighContrastForValidation(true))
                    {
                        SettleLayout(main);
                        var thumbSurface = FindThumbSurface(bar);
                        var expectedContrastBrush = Application.Current.FindResource("ControlBorderBrush") as SolidColorBrush;
                        if (thumbSurface?.Background is not SolidColorBrush actualBrush ||
                            expectedContrastBrush is null ||
                            actualBrush.Color != expectedContrastBrush.Color)
                        {
                            failures.Add("系统高对比度下共享滚动条未随 ControlBorderBrush 同步映射系统色");
                        }
                    }

                    theme.Apply();
                    SettleLayout(main);
                }
            });

        // 7) 窗口级纵向与横向滚动（720×560 缩小窗口）
        WithPopulatedMainWindow(
            userSize: new Size(720, 560),
            tagCount: 0,
            countdownCount: 12,
            noteCount: 14,
            (main, workspace) =>
            {
                _ = workspace;
                var probe = new MainWindowEmptyStateProbe(main);
                probe.ShowWorkspace(WorkspaceKind.Notes);
                SettleLayout(main);
                var host = RequiredElement<ScrollViewer>(main, "MainWindowScrollHost");
                var surfaceColors = new[] { RequiredBrushColor("BackgroundBrush"), RequiredBrushColor("CanvasBrush") };
                VerifySharedScrollBar("MainWindow.MainWindowScrollHost(V)", host, Orientation.Vertical, surfaceColors, failures);
                VerifySharedScrollBar("MainWindow.MainWindowScrollHost(H)", host, Orientation.Horizontal, surfaceColors, failures);
            });
    }

    private static void VerifyReminderCenterSurface(ICollection<string> failures)
    {
        var center = CreateOverflowedReminderCenter();
        try
        {
            PrepareOffscreen(center);
            center.Show();
            SettleWindow(center);
            var scroller = VisualDescendants<ScrollViewer>(center)
                .Single(candidate => AutomationProperties.GetName(candidate) == "提醒条目列表");
            VerifySharedScrollBar(
                "ReminderCenterWindow",
                scroller,
                Orientation.Vertical,
                [RequiredBrushColor("ReminderPageBrush"), RequiredBrushColor("ReminderCardBrush")],
                failures);
        }
        finally
        {
            CloseWindow(center);
        }
    }

    private static void VerifyReminderRulesEditorSurface(ICollection<string> failures)
    {
        var editor = CreateOverflowedRulesEditor();
        try
        {
            PrepareOffscreen(editor);
            editor.Show();
            SettleWindow(editor);
            var scroller = VisualDescendants<ScrollViewer>(editor)
                .Single(candidate => AutomationProperties.GetName(candidate) == "已配置提醒规则");
            VerifySharedScrollBar(
                "ReminderRulesEditorWindow",
                scroller,
                Orientation.Vertical,
                [RequiredBrushColor("CanvasBrush"), RequiredBrushColor("CardBrush")],
                failures);
        }
        finally
        {
            CloseWindow(editor);
        }
    }

    private static void VerifyFloatingScrollBarUnchanged(ICollection<string> failures)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-scrollbar-floating-{Guid.NewGuid():N}.settings.json");
        FloatingWindow? floating = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
            for (var index = 1; index <= 8; index++)
            {
                var result = workspace.Apply(new SaveNoteChange(
                    null,
                    $"悬浮窗长便签滚动条回归验证条目 {index:00}：确认悬浮窗局部 4 DIP 滚动条未被共享样式改变。",
                    workspace.Snapshot.BeijingNow.Date,
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"写入悬浮窗便签失败：{result.Error}");
                }
            }

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            floating = new FloatingWindow(
                workspace,
                calendar,
                new NoopManagerHost(),
                AppContext.BaseDirectory);
            PrepareOffscreen(floating);
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            SettleWindow(floating);

            var bodyScroller = floating.FindName("FloatingBodyScroller") as ScrollViewer;
            if (bodyScroller is null)
            {
                failures.Add("悬浮窗缺少 FloatingBodyScroller");
                return;
            }

            if (bodyScroller.ScrollableHeight <= 0.5 ||
                bodyScroller.ComputedVerticalScrollBarVisibility != Visibility.Visible)
            {
                failures.Add($"悬浮窗便签夹具未产生真实滚动（scrollable={bodyScroller.ScrollableHeight:0.##}）");
                return;
            }

            bodyScroller.ApplyTemplate();
            var scrollBar = bodyScroller.Template?.FindName("PART_VerticalScrollBar", bodyScroller) as ScrollBar;
            scrollBar?.ApplyTemplate();
            var rail = scrollBar?.Template?.FindName("FloatingScrollBarHitRail", scrollBar) as FrameworkElement;
            var track = scrollBar?.Template?.FindName("PART_Track", scrollBar) as Track;
            var thumb = track?.Thumb as FloatingScrollBarThumb;
            thumb?.ApplyTemplate();
            var visual = thumb?.Template?.FindName("FloatingScrollBarThumbVisual", thumb) as Border;
            if (scrollBar is null || rail is null || thumb is null || visual is null)
            {
                failures.Add("悬浮窗滚动条不再使用 FloatingScrollBarHitRail / FloatingScrollBarThumb 局部模板");
                return;
            }

            if (Math.Abs(rail.ActualWidth - 10.0) > GeometryToleranceDip)
            {
                failures.Add($"悬浮窗滚动条命中轨宽异常：{rail.ActualWidth:0.##}（期望 10）");
            }

            // 分别验证 normal / hover / drag 三态下浮窗仍保持 4 DIP 且透明度为 0.52 / 0.78 / 0.96，不展开到 10 DIP。
            var expectedStates = new (FloatingScrollBarThumbState State, double Opacity)[]
            {
                (FloatingScrollBarThumbState.Normal, 0.52),
                (FloatingScrollBarThumbState.Hover, 0.78),
                (FloatingScrollBarThumbState.Drag, 0.96)
            };
            foreach (var (state, expectedOpacity) in expectedStates)
            {
                thumb.ApplyVisualStateForValidation(state);
                if (state == FloatingScrollBarThumbState.Hover)
                {
                    RaiseHoverEnter(scrollBar);
                }
                else if (state == FloatingScrollBarThumbState.Drag)
                {
                    RaiseDragStarted(thumb);
                }

                SettleWindow(floating);
                var renderedWidth = Bounds(visual, scrollBar).Width;
                if (Math.Abs(renderedWidth - 4.0) > GeometryToleranceDip)
                {
                    failures.Add($"悬浮窗滚动条 {state} 可见宽被改变：{renderedWidth:0.##}（期望 4）");
                }

                if (Math.Abs(thumb.ThumbOpacity - expectedOpacity) > 0.01)
                {
                    failures.Add($"悬浮窗滚动条 {state} 透明度被改变：{thumb.ThumbOpacity:0.##}（期望 {expectedOpacity:0.##}）");
                }

                if (state == FloatingScrollBarThumbState.Hover)
                {
                    RaiseHoverLeave(scrollBar);
                }
                else if (state == FloatingScrollBarThumbState.Drag)
                {
                    RaiseDragCompleted(thumb);
                }
            }

            thumb.ApplyVisualStateForValidation(FloatingScrollBarThumbState.Normal);
            SettleWindow(floating);

            // 快速新增命令区滚动条也须保持局部 FloatingScrollBarThumb 模板。
            var commandScroller = floating.FindName("FloatingNotesCommandScroller") as ScrollViewer;
            commandScroller?.ApplyTemplate();
            var commandScrollBar = commandScroller?.Template?.FindName("PART_VerticalScrollBar", commandScroller) as ScrollBar;
            commandScrollBar?.ApplyTemplate();
            var commandTrack = commandScrollBar?.Template?.FindName("PART_Track", commandScrollBar) as Track;
            if (commandTrack?.Thumb is not FloatingScrollBarThumb)
            {
                failures.Add("悬浮窗 FloatingNotesCommandScroller 不再使用 FloatingScrollBarThumb 局部样式");
            }
        }
        finally
        {
            if (floating is not null)
            {
                CloseWindow(floating);
            }

            DeleteSettingsFiles(settingsPath);
        }
    }

    private static void VerifyMotionAndReducedMotion(ApplicationTheme theme, ICollection<string> failures)
    {
        WithPopulatedMainWindow(
            userSize: null,
            tagCount: 0,
            countdownCount: 12,
            noteCount: 0,
            (main, workspace) =>
            {
                _ = workspace;
                var probe = new MainWindowEmptyStateProbe(main);
                probe.ShowWorkspace(WorkspaceKind.Countdowns);
                SettleLayout(main);
                var list = RequiredElement<ScrollViewer>(main, "CountdownListRegion");
                var bar = RequireScrollBar(list, Orientation.Vertical, "Motion.CountdownListRegion", failures);
                if (bar is null)
                {
                    return;
                }

                var hitRail = FindHitRail(bar);
                var thumbSurface = FindThumbSurface(bar);
                var track = FindTrack(bar);
                if (hitRail is null || thumbSurface is null || track?.Thumb is null)
                {
                    failures.Add("动效验证缺少 ScrollBar SharedScrollBarHitRail / ThumbSurface");
                    return;
                }

                var initialBarWidth = bar.ActualWidth;

                // 1) 开启动效：消费 MotionHoverDuration (120ms)，展开与收起均有中间帧且不改布局宽，支持快速反向与运行中切关动画。
                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(true))
                {
                    theme.Apply();
                    var hoverDuration = Application.Current.TryFindResource("MotionHoverDuration") is Duration d && d.HasTimeSpan
                        ? d.TimeSpan.TotalMilliseconds
                        : -1d;
                    if (Math.Abs(hoverDuration - 120.0) > 0.1)
                    {
                        failures.Add($"开启动效时 MotionHoverDuration 应为 120ms，实际 {hoverDuration:0.##}ms");
                    }

                    var restingWidth = VisibleThickness(bar, thumbSurface);
                    if (Math.Abs(restingWidth - ExpectedRestingDip) > GeometryToleranceDip)
                    {
                        failures.Add($"动效前静止宽应为 4 DIP，实际 {restingWidth:0.##}");
                    }

                    // 展开动画
                    var expandSamples = new List<double>();
                    var layoutStableDuringExpand = true;
                    RaiseHoverEnter(bar);
                    var expandSettled = WindowValidationInput.PumpRenderedFrames(
                        main.Dispatcher,
                        2000,
                        () =>
                        {
                            var width = VisibleThickness(bar, thumbSurface);
                            expandSamples.Add(width);
                            if (Math.Abs(hitRail.ActualWidth - ExpectedHitRailDip) > GeometryToleranceDip ||
                                Math.Abs(bar.ActualWidth - initialBarWidth) > GeometryToleranceDip ||
                                Math.Abs(thumbSurface.ActualWidth - ExpectedExpandedDip) > GeometryToleranceDip)
                            {
                                layoutStableDuringExpand = false;
                            }

                            return Math.Abs(width - ExpectedExpandedDip) <= GeometryToleranceDip;
                        });
                    var expandHadIntermediate = expandSamples.Any(w => w > ExpectedRestingDip + 0.15 && w < ExpectedExpandedDip - 0.15);
                    if (!expandSettled.Completed || !expandHadIntermediate || !layoutStableDuringExpand)
                    {
                        failures.Add(string.Create(
                            CultureInfo.InvariantCulture,
                            $"展开动效异常：settled={expandSettled.Completed}({expandSettled.ElapsedMilliseconds:0.#}ms)，" +
                            $"intermediate={expandHadIntermediate}，layoutStable={layoutStableDuringExpand}，" +
                            $"samples=[{string.Join(",", expandSamples.Take(8).Select(w => w.ToString("0.00", CultureInfo.InvariantCulture)))}]"));
                    }

                    // 收起动画
                    var collapseSamples = new List<double>();
                    var layoutStableDuringCollapse = true;
                    RaiseHoverLeave(bar);
                    var collapseSettled = WindowValidationInput.PumpRenderedFrames(
                        main.Dispatcher,
                        2000,
                        () =>
                        {
                            var width = VisibleThickness(bar, thumbSurface);
                            collapseSamples.Add(width);
                            if (Math.Abs(hitRail.ActualWidth - ExpectedHitRailDip) > GeometryToleranceDip ||
                                Math.Abs(bar.ActualWidth - initialBarWidth) > GeometryToleranceDip ||
                                Math.Abs(thumbSurface.ActualWidth - ExpectedExpandedDip) > GeometryToleranceDip)
                            {
                                layoutStableDuringCollapse = false;
                            }

                            return Math.Abs(width - ExpectedRestingDip) <= GeometryToleranceDip;
                        });
                    var collapseHadIntermediate = collapseSamples.Any(w => w > ExpectedRestingDip + 0.15 && w < ExpectedExpandedDip - 0.15);
                    if (!collapseSettled.Completed || !collapseHadIntermediate || !layoutStableDuringCollapse)
                    {
                        failures.Add(string.Create(
                            CultureInfo.InvariantCulture,
                            $"收起动效异常：settled={collapseSettled.Completed}({collapseSettled.ElapsedMilliseconds:0.#}ms)，" +
                            $"intermediate={collapseHadIntermediate}，layoutStable={layoutStableDuringCollapse}，" +
                            $"samples=[{string.Join(",", collapseSamples.Take(8).Select(w => w.ToString("0.00", CultureInfo.InvariantCulture)))}]"));
                    }

                    // 中途快速反向：展开到中间帧后立刻移出，不得跳回 10 再回退，须平滑回到 4 DIP。
                    var peakBeforeReverse = 0d;
                    RaiseHoverEnter(bar);
                    var reachedMidExpand = WindowValidationInput.PumpRenderedFrames(
                        main.Dispatcher,
                        1000,
                        () =>
                        {
                            var width = VisibleThickness(bar, thumbSurface);
                            if (width > ExpectedRestingDip + 0.3 && width < ExpectedExpandedDip - 0.3)
                            {
                                peakBeforeReverse = width;
                                return true;
                            }

                            return false;
                        });
                    if (reachedMidExpand.Completed)
                    {
                        var firstReverseSample = double.NaN;
                        RaiseHoverLeave(bar);
                        var reversedSettled = WindowValidationInput.PumpRenderedFrames(
                            main.Dispatcher,
                            2000,
                            () =>
                            {
                                var width = VisibleThickness(bar, thumbSurface);
                                if (double.IsNaN(firstReverseSample))
                                {
                                    firstReverseSample = width;
                                }

                                return Math.Abs(width - ExpectedRestingDip) <= GeometryToleranceDip;
                            });
                        if (!reversedSettled.Completed || firstReverseSample > peakBeforeReverse + 0.5)
                        {
                            failures.Add(string.Create(
                                CultureInfo.InvariantCulture,
                                $"快速反向续接异常：peak={peakBeforeReverse:0.00}，firstReverse={firstReverseSample:0.00}，settled={reversedSettled.Completed}"));
                        }
                    }
                    else
                    {
                        RaiseHoverLeave(bar);
                        WaitForWidth(main, bar, thumbSurface, ExpectedRestingDip);
                        failures.Add("快速反向未能捕获展开中间帧");
                    }
                }

                // 2) 运行中关闭系统动画：正在展开中途关闭，立刻直达终值 10 DIP。
                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(true))
                {
                    theme.Apply();
                    RaiseHoverEnter(bar);
                    _ = WindowValidationInput.PumpRenderedFrames(
                        main.Dispatcher,
                        1000,
                        () =>
                        {
                            var width = VisibleThickness(bar, thumbSurface);
                            return width > ExpectedRestingDip + 0.2 && width < ExpectedExpandedDip - 0.2;
                        });
                }

                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
                {
                    theme.Apply();
                    SettleLayout(main);
                    var snappedMidFlight = VisibleThickness(bar, thumbSurface);
                    if (Math.Abs(snappedMidFlight - ExpectedExpandedDip) > GeometryToleranceDip)
                    {
                        failures.Add($"展开中途关闭系统动画未立即直达 10 DIP（实际 {snappedMidFlight:0.##}）");
                    }

                    // 3) 系统关闭动画：MotionHoverDuration == 0ms，收起与展开均单拍直接到位。
                    var reducedDuration = Application.Current.TryFindResource("MotionHoverDuration") is Duration d && d.HasTimeSpan
                        ? d.TimeSpan.TotalMilliseconds
                        : -1d;
                    if (Math.Abs(reducedDuration) > 0.01)
                    {
                        failures.Add($"关闭动画时 MotionHoverDuration 应为 0ms，实际 {reducedDuration:0.##}ms");
                    }

                    RaiseHoverLeave(bar);
                    WindowValidationInput.Pump(main.Dispatcher);
                    var snappedCollapsed = VisibleThickness(bar, thumbSurface);
                    if (Math.Abs(snappedCollapsed - ExpectedRestingDip) > GeometryToleranceDip)
                    {
                        failures.Add($"关闭动画时收起未直接到位 4 DIP（实际 {snappedCollapsed:0.##}）");
                    }

                    RaiseHoverEnter(bar);
                    WindowValidationInput.Pump(main.Dispatcher);
                    var snappedExpanded = VisibleThickness(bar, thumbSurface);
                    if (Math.Abs(snappedExpanded - ExpectedExpandedDip) > GeometryToleranceDip)
                    {
                        failures.Add($"关闭动画时悬停展开未直接到位 10 DIP（实际 {snappedExpanded:0.##}）");
                    }

                    RaiseHoverLeave(bar);
                    WindowValidationInput.Pump(main.Dispatcher);

                    // 拖动在关闭动画时同样直接到位与回退。
                    RaiseDragStarted(track.Thumb);
                    WindowValidationInput.Pump(main.Dispatcher);
                    var dragSnapped = VisibleThickness(bar, thumbSurface);
                    if (Math.Abs(dragSnapped - ExpectedExpandedDip) > GeometryToleranceDip)
                    {
                        failures.Add($"关闭动画时拖动展开未直接到位 10 DIP（实际 {dragSnapped:0.##}）");
                    }

                    RaiseDragCompleted(track.Thumb);
                    WindowValidationInput.Pump(main.Dispatcher);
                    var dragEndSnapped = VisibleThickness(bar, thumbSurface);
                    if (Math.Abs(dragEndSnapped - ExpectedRestingDip) > GeometryToleranceDip)
                    {
                        failures.Add($"关闭动画时拖动结束未直接回到 4 DIP（实际 {dragEndSnapped:0.##}）");
                    }
                }

                theme.Apply();
            });
    }

    private static void VerifyAllScrollInteractions(ApplicationTheme theme, ICollection<string> failures)
    {
        using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
        {
            theme.Apply();

            // 1) 主窗口倒计时列表 + 720×560 窗口级横向轨道点击
            WithPopulatedMainWindow(
                userSize: new Size(720, 560),
                tagCount: 0,
                countdownCount: 12,
                noteCount: 14,
                (main, _) =>
                {
                    var probe = new MainWindowEmptyStateProbe(main);
                    probe.ShowWorkspace(WorkspaceKind.Countdowns);
                    SettleLayout(main);
                    var countdownList = RequiredElement<ScrollViewer>(main, "CountdownListRegion");
                    VerifyVerticalScrollInteractions(main, "MainWindow.CountdownListRegion", countdownList, failures);

                    probe.ShowWorkspace(WorkspaceKind.Notes);
                    SettleLayout(main);
                    var host = RequiredElement<ScrollViewer>(main, "MainWindowScrollHost");
                    VerifyHorizontalTrackClickInteraction(main, "MainWindow.MainWindowScrollHost(H)", host, failures);
                });

            // 2) 提醒中心列表
            var center = CreateOverflowedReminderCenter();
            try
            {
                PrepareOffscreen(center);
                center.Show();
                SettleWindow(center);
                var scroller = VisualDescendants<ScrollViewer>(center)
                    .Single(candidate => AutomationProperties.GetName(candidate) == "提醒条目列表");
                VerifyVerticalScrollInteractions(center, "ReminderCenterWindow", scroller, failures);
            }
            finally
            {
                CloseWindow(center);
            }

            // 3) 规则编辑器列表
            var editor = CreateOverflowedRulesEditor();
            try
            {
                PrepareOffscreen(editor);
                editor.Show();
                SettleWindow(editor);
                var scroller = VisualDescendants<ScrollViewer>(editor)
                    .Single(candidate => AutomationProperties.GetName(candidate) == "已配置提醒规则");
                VerifyVerticalScrollInteractions(editor, "ReminderRulesEditorWindow", scroller, failures);
            }
            finally
            {
                CloseWindow(editor);
            }
        }

        theme.Apply();
    }

    private static void VerifyVerticalScrollInteractions(
        Window owner,
        string label,
        ScrollViewer scroller,
        ICollection<string> failures)
    {
        var bar = RequireScrollBar(scroller, Orientation.Vertical, label, failures);
        var track = bar is null ? null : FindTrack(bar);
        if (bar is null || track?.Thumb is null || track.IncreaseRepeatButton is null || track.DecreaseRepeatButton is null)
        {
            failures.Add($"{label}：滚动条轨道部件不完整");
            return;
        }

        // a) 鼠标滚轮
        scroller.ScrollToTop();
        SettleWindow(owner);
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = scroller
        };
        scroller.RaiseEvent(wheel);
        SettleWindow(owner);
        if (!wheel.Handled || scroller.VerticalOffset <= 0.5)
        {
            failures.Add($"{label}：鼠标滚轮未滚动（handled={wheel.Handled}，offset={scroller.VerticalOffset:0.##}）");
        }

        // b) 拖动滑块
        scroller.ScrollToTop();
        SettleWindow(owner);
        var dragDelta = Math.Max(24, track.ActualHeight / 3);
        RaiseDragStarted(track.Thumb);
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, dragDelta)
        {
            RoutedEvent = Thumb.DragDeltaEvent,
            Source = track.Thumb
        });
        SettleWindow(owner);
        var draggedOffset = scroller.VerticalOffset;
        RaiseDragCompleted(track.Thumb);
        SettleWindow(owner);
        if (draggedOffset <= 0.5)
        {
            failures.Add($"{label}：拖动滑块未滚动（offset={draggedOffset:0.##}）");
        }

        // c) 键盘翻页（PageDown / PageUp）
        scroller.ScrollToTop();
        SettleWindow(owner);
        owner.Activate();
        scroller.Focusable = true;
        Keyboard.Focus(scroller);
        SettleWindow(owner);
        WindowValidationInput.RaiseKey(scroller, Key.PageDown);
        SettleWindow(owner);
        var afterPageDown = scroller.VerticalOffset;
        WindowValidationInput.RaiseKey(scroller, Key.PageUp);
        SettleWindow(owner);
        var afterPageUp = scroller.VerticalOffset;
        if (afterPageDown <= 0.5 || afterPageUp >= afterPageDown - 0.5)
        {
            failures.Add($"{label}：键盘翻页异常（PageDown={afterPageDown:0.##}，PageUp={afterPageUp:0.##}）");
        }

        // d) 点击轨道翻页（IncreaseRepeatButton / DecreaseRepeatButton）
        scroller.ScrollToTop();
        SettleWindow(owner);
        InvokeRepeatButton(track.IncreaseRepeatButton);
        SettleWindow(owner);
        var afterTrackDown = scroller.VerticalOffset;
        InvokeRepeatButton(track.DecreaseRepeatButton);
        SettleWindow(owner);
        var afterTrackUp = scroller.VerticalOffset;
        if (afterTrackDown <= 0.5 || afterTrackUp >= afterTrackDown - 0.5)
        {
            failures.Add($"{label}：点击轨道翻页异常（向下={afterTrackDown:0.##}，向上={afterTrackUp:0.##}）");
        }
    }

    private static void VerifyHorizontalTrackClickInteraction(
        Window owner,
        string label,
        ScrollViewer scroller,
        ICollection<string> failures)
    {
        var bar = RequireScrollBar(scroller, Orientation.Horizontal, label, failures);
        var track = bar is null ? null : FindTrack(bar);
        if (bar is null || track?.IncreaseRepeatButton is null || track.DecreaseRepeatButton is null)
        {
            failures.Add($"{label}：横向滚动条轨道部件不完整");
            return;
        }

        scroller.ScrollToLeftEnd();
        SettleWindow(owner);
        InvokeRepeatButton(track.IncreaseRepeatButton);
        SettleWindow(owner);
        var afterRight = scroller.HorizontalOffset;
        InvokeRepeatButton(track.DecreaseRepeatButton);
        SettleWindow(owner);
        var afterLeft = scroller.HorizontalOffset;
        if (afterRight <= 0.5 || afterLeft >= afterRight - 0.5)
        {
            failures.Add($"{label}：横向点击轨道翻页异常（向右={afterRight:0.##}，向左={afterLeft:0.##}）");
        }
    }

    private static void VerifyPopupScrollViewer(
        MainWindow main,
        string label,
        Popup popup,
        Action openPopup,
        Color adjacentSurface,
        ICollection<string> failures)
    {
        try
        {
            openPopup();
            SettleLayout(main);
            if (!popup.IsOpen || popup.Child is not FrameworkElement child)
            {
                failures.Add($"{label}：弹层未打开");
                return;
            }

            child.UpdateLayout();
            SettleLayout(main);
            var scroller = VisualDescendants<ScrollViewer>(child)
                .FirstOrDefault(candidate => candidate.ScrollableHeight > 0.5 &&
                    candidate.ComputedVerticalScrollBarVisibility == Visibility.Visible);
            if (scroller is null)
            {
                failures.Add($"{label}：弹层内未出现溢出滚动容器");
                return;
            }

            VerifySharedScrollBar(label, scroller, Orientation.Vertical, [adjacentSurface], failures);
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(popup);
            SettleLayout(main);
        }
    }

    private static void VerifySharedScrollBar(
        string label,
        ScrollViewer scroller,
        Orientation orientation,
        IReadOnlyList<Color> adjacentSurfaces,
        ICollection<string> failures)
    {
        var bar = RequireScrollBar(scroller, orientation, label, failures);
        if (bar is null)
        {
            return;
        }

        var hitRailElement = FindHitRail(bar);
        var track = FindTrack(bar);
        var thumb = track?.Thumb;
        var thumbSurface = FindThumbSurface(bar);
        if (hitRailElement is null || track is null || thumb is null || thumbSurface is null)
        {
            failures.Add($"{label}：缺少 SharedScrollBarHitRail / PART_Track / Thumb / ThumbSurface");
            return;
        }

        var hitRail = CrossAxis(hitRailElement.ActualWidth, hitRailElement.ActualHeight, orientation);
        var trackCross = CrossAxis(track.ActualWidth, track.ActualHeight, orientation);
        var thumbHitCross = CrossAxis(thumb.ActualWidth, thumb.ActualHeight, orientation);
        var surfaceLayoutCross = CrossAxis(thumbSurface.ActualWidth, thumbSurface.ActualHeight, orientation);
        if (Math.Abs(hitRail - ExpectedHitRailDip) > GeometryToleranceDip ||
            Math.Abs(trackCross - ExpectedHitRailDip) > GeometryToleranceDip ||
            Math.Abs(thumbHitCross - ExpectedHitRailDip) > GeometryToleranceDip)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{label}：命中轨宽应保持 10 DIP（rail={hitRail:0.##}，track={trackCross:0.##}，thumb={thumbHitCross:0.##}）"));
        }

        // 命中测试覆盖 10 DIP 命中轨两端（即使静止可见条只有 4 DIP）。
        var nearProbe = orientation == Orientation.Vertical
            ? new Point(1.5, Math.Max(4, hitRailElement.ActualHeight / 2))
            : new Point(Math.Max(4, hitRailElement.ActualWidth / 2), 1.5);
        var farProbe = orientation == Orientation.Vertical
            ? new Point(8.5, Math.Max(4, hitRailElement.ActualHeight / 2))
            : new Point(Math.Max(4, hitRailElement.ActualWidth / 2), 8.5);
        if (VisualTreeHelper.HitTest(hitRailElement, nearProbe) is null ||
            VisualTreeHelper.HitTest(hitRailElement, farProbe) is null)
        {
            failures.Add($"{label}：10 DIP 命中轨两端存在不可命中空洞");
        }

        // 静止状态：4 DIP、ControlBorderBrush、不透明、对邻近表面 >= 3:1。
        var expectedIdleColor = RequiredBrushColor("ControlBorderBrush");
        var expectedDragColor = RequiredBrushColor("ActiveBrush");
        var restingVisible = VisibleThickness(bar, thumbSurface);
        if (Math.Abs(restingVisible - ExpectedRestingDip) > GeometryToleranceDip)
        {
            failures.Add($"{label}：静止可见宽应为 4 DIP，实际 {restingVisible:0.##}");
        }

        VerifyBrushAndContrast($"{label}.Resting", thumbSurface, expectedIdleColor, adjacentSurfaces, failures);

        // 悬停进入命中轨：展开到 10 DIP、布局宽不变、颜色角色与 >= 3:1 对比不变。
        RaiseHoverEnter(bar);
        WindowValidationInput.Pump(bar.Dispatcher);
        var hoverVisible = VisibleThickness(bar, thumbSurface);
        var hoverLayoutCross = CrossAxis(thumbSurface.ActualWidth, thumbSurface.ActualHeight, orientation);
        if (Math.Abs(hoverVisible - ExpectedExpandedDip) > GeometryToleranceDip)
        {
            failures.Add($"{label}：悬停命中轨可见宽应为 10 DIP，实际 {hoverVisible:0.##}");
        }

        if (Math.Abs(hoverLayoutCross - surfaceLayoutCross) > GeometryToleranceDip)
        {
            failures.Add($"{label}：展开改变了布局宽度（{surfaceLayoutCross:0.##} -> {hoverLayoutCross:0.##}）");
        }

        VerifyBrushAndContrast($"{label}.Hover", thumbSurface, expectedIdleColor, adjacentSurfaces, failures);

        // 拖动状态：即使指针移出命中轨仍保持 10 DIP，且 ActiveBrush 对邻近表面 >= 3:1。
        RaiseDragStarted(thumb);
        RaiseHoverLeave(bar);
        WindowValidationInput.Pump(bar.Dispatcher);
        var dragVisible = VisibleThickness(bar, thumbSurface);
        if (Math.Abs(dragVisible - ExpectedExpandedDip) > GeometryToleranceDip)
        {
            failures.Add($"{label}：拖动期间（指针离轨后）可见宽应保持 10 DIP，实际 {dragVisible:0.##}");
        }

        foreach (var surfaceColor in adjacentSurfaces)
        {
            var dragRatio = ContrastRatio(expectedDragColor, surfaceColor, opacity: 1.0);
            if (dragRatio < MinimumGraphicContrast)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}.Drag：ActiveBrush 对邻面对比度 {dragRatio:0.00} < 3.00"));
            }
        }

        // 结束拖动（指针已在轨外）：收回到 4 DIP。
        RaiseDragCompleted(thumb);
        WindowValidationInput.Pump(bar.Dispatcher);
        var afterDragVisible = VisibleThickness(bar, thumbSurface);
        if (Math.Abs(afterDragVisible - ExpectedRestingDip) > GeometryToleranceDip)
        {
            failures.Add($"{label}：拖动结束后未收回 4 DIP，实际 {afterDragVisible:0.##}");
        }
    }

    private static void VerifyBrushAndContrast(
        string label,
        Border thumbSurface,
        Color expectedColor,
        IReadOnlyList<Color> adjacentSurfaces,
        ICollection<string> failures)
    {
        if (thumbSurface.Background is not SolidColorBrush brush || brush.Color != expectedColor)
        {
            var actualText = thumbSurface.Background is SolidColorBrush actual ? actual.Color.ToString() : thumbSurface.Background?.GetType().Name ?? "null";
            failures.Add($"{label}：颜色角色变化（actual={actualText}，expected={expectedColor}）");
            return;
        }

        var opacity = thumbSurface.Opacity;
        foreach (var surfaceColor in adjacentSurfaces)
        {
            var ratio = ContrastRatio(brush.Color, surfaceColor, opacity);
            if (ratio < MinimumGraphicContrast)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label}：可见对比度 {ratio:0.00}（opacity={opacity:0.##}）低于 3.00"));
            }
        }
    }

    private static ScrollBar? RequireScrollBar(
        ScrollViewer scroller,
        Orientation orientation,
        string label,
        ICollection<string> failures)
    {
        scroller.ApplyTemplate();
        var scrollable = orientation == Orientation.Vertical ? scroller.ScrollableHeight : scroller.ScrollableWidth;
        var visibility = orientation == Orientation.Vertical
            ? scroller.ComputedVerticalScrollBarVisibility
            : scroller.ComputedHorizontalScrollBarVisibility;
        var partName = orientation == Orientation.Vertical ? "PART_VerticalScrollBar" : "PART_HorizontalScrollBar";
        var bar = scroller.Template?.FindName(partName, scroller) as ScrollBar;
        if (scrollable <= 0.5 || visibility != Visibility.Visible || bar is null || !bar.IsVisible)
        {
            failures.Add($"{label}：未产生可见的 {orientation} 滚动条（scrollable={scrollable:0.##}，visibility={visibility}）");
            return null;
        }

        bar.ApplyTemplate();
        return bar;
    }

    private static FrameworkElement? FindHitRail(ScrollBar bar)
    {
        bar.ApplyTemplate();
        return bar.Template?.FindName("SharedScrollBarHitRail", bar) as FrameworkElement;
    }

    private static Track? FindTrack(ScrollBar bar)
    {
        bar.ApplyTemplate();
        return bar.Template?.FindName("PART_Track", bar) as Track;
    }

    private static Border? FindThumbSurface(ScrollBar bar)
    {
        var thumb = FindTrack(bar)?.Thumb;
        thumb?.ApplyTemplate();
        return thumb?.Template?.FindName("ThumbSurface", thumb) as Border;
    }

    private static double VisibleThickness(ScrollBar bar, Border thumbSurface)
    {
        var bounds = thumbSurface.TransformToAncestor(bar)
            .TransformBounds(new Rect(new Point(0, 0), thumbSurface.RenderSize));
        return CrossAxis(bounds.Width, bounds.Height, bar.Orientation);
    }

    private static double CrossAxis(double width, double height, Orientation orientation) =>
        orientation == Orientation.Vertical ? width : height;

    private static void WaitForWidth(MainWindow main, ScrollBar bar, Border thumbSurface, double target)
    {
        _ = WindowValidationInput.PumpRenderedFrames(
            main.Dispatcher,
            2000,
            () => Math.Abs(VisibleThickness(bar, thumbSurface) - target) <= GeometryToleranceDip);
    }

    private static void RaiseHoverEnter(ScrollBar bar) =>
        bar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = Mouse.MouseEnterEvent,
            Source = bar
        });

    private static void RaiseHoverLeave(ScrollBar bar) =>
        bar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = Mouse.MouseLeaveEvent,
            Source = bar
        });

    private static void RaiseDragStarted(Thumb thumb) =>
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0)
        {
            RoutedEvent = Thumb.DragStartedEvent,
            Source = thumb
        });

    private static void RaiseDragCompleted(Thumb thumb) =>
        thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, canceled: false)
        {
            RoutedEvent = Thumb.DragCompletedEvent,
            Source = thumb
        });

    private static void InvokeRepeatButton(RepeatButton button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button)
            ?? new RepeatButtonAutomationPeer(button);
        if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider provider)
        {
            throw new InvalidOperationException("轨道 RepeatButton 缺少 IInvokeProvider。");
        }

        provider.Invoke();
    }

    private static void InvokeButtonOrFail(Button button, string name)
    {
        if (!WindowValidationInput.InvokeButton(button))
        {
            throw new InvalidOperationException($"无法调用按钮 {name}。");
        }
    }

    private static Color RequiredBrushColor(string key) =>
        Application.Current.FindResource(key) is SolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"资源 {key} 不是 SolidColorBrush。");

    private static double ContrastRatio(Color foreground, Color background, double opacity)
    {
        var effectiveAlpha = Math.Clamp((foreground.A / 255.0) * opacity, 0.0, 1.0);
        var r = foreground.R * effectiveAlpha + background.R * (1.0 - effectiveAlpha);
        var g = foreground.G * effectiveAlpha + background.G * (1.0 - effectiveAlpha);
        var b = foreground.B * effectiveAlpha + background.B * (1.0 - effectiveAlpha);
        var l1 = RelativeLuminance(r, g, b);
        var l2 = RelativeLuminance(background.R, background.G, background.B);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static double RelativeLuminance(double r, double g, double b)
    {
        static double Linearize(double channel)
        {
            var normalized = channel / 255.0;
            return normalized <= 0.04045
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    private static void WithPopulatedMainWindow(
        Size? userSize,
        int tagCount,
        int countdownCount,
        int noteCount,
        Action<MainWindow, DesktopWorkspace> probe)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-scrollbar-main-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var clock = new FixedClock(FixtureUtc);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
            var main = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                AppContext.BaseDirectory);
            PrepareOffscreen(main);
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();

                for (var index = 1; index <= tagCount; index++)
                {
                    Apply(
                        workspace,
                        new CreateTagChange(
                            string.Create(CultureInfo.InvariantCulture, $"滚动条弹层测试专用标签名称超长条目编号-{index:00}"),
                            null),
                        main.Dispatcher);
                }

                for (var index = 1; index <= countdownCount; index++)
                {
                    SaveCountdown(
                        workspace,
                        string.Create(CultureInfo.InvariantCulture, $"滚动条验证倒计时 {index:00}"),
                        index,
                        main.Dispatcher);
                }

                for (var index = 1; index <= noteCount; index++)
                {
                    SaveNote(
                        workspace,
                        string.Create(CultureInfo.InvariantCulture, $"滚动条验证便签 {index:00}"),
                        main.Dispatcher);
                }

                if (userSize is { } targetSize)
                {
                    var handle = new WindowInteropHelper(main).Handle;
                    NativeWindowMessage.Send(handle, NativeWindowMessage.WmEnterSizeMove, IntPtr.Zero, IntPtr.Zero);
                    main.Width = targetSize.Width;
                    main.Height = targetSize.Height;
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    NativeWindowMessage.Send(handle, NativeWindowMessage.WmExitSizeMove, IntPtr.Zero, IntPtr.Zero);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                }

                probe(main, workspace);
            }
            finally
            {
                CloseWindow(main);
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            if (File.Exists(diagnosticPath))
            {
                File.Delete(diagnosticPath);
            }
        }
    }

    private static ReminderCenterWindow CreateOverflowedReminderCenter()
    {
        var center = new ReminderCenterWindow();
        for (var index = 1; index <= 6; index++)
        {
            var key = new ActiveReminderOccurrenceKey($"scrollbar-rule-{index:00}", FixtureUtc.AddMinutes(-index));
            center.AddEntry(new ReminderCenterItem(
                key,
                new NoteRecord(
                    $"scrollbar-note-{index:00}",
                    $"提醒中心滚动条验证条目 {index:00}：核对共享滚动条在提醒中心列表中的静止与展开形态。",
                    "2038-07-08",
                    false,
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()),
                index - 1,
                0));
            center.SetDeliveryReady(key);
        }

        center.UpdateTime(FixtureUtc);
        return center;
    }

    private static ReminderRulesEditorWindow CreateOverflowedRulesEditor()
    {
        var rules = Enumerable.Range(1, 7)
            .Select(index => (ReminderRule)new AbsoluteOnceReminderRule(
                $"scrollbar-editor-rule-{index:00}",
                FixtureUtc.AddDays(index)))
            .ToArray();
        return new ReminderRulesEditorWindow(rules, new FixedClock(FixtureUtc));
    }

    private static T RequiredElement<T>(MainWindow main, string name) where T : DependencyObject =>
        main.FindName(name) as T ??
        throw new InvalidOperationException($"管理窗口缺少滚动条验证元素 {name}（{typeof(T).Name}）。");

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

    private static void SettleWindow(Window window)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static void CloseWindow(Window window)
    {
        window.Close();
        WindowValidationInput.Pump(window.Dispatcher);
    }

    private sealed class NoopManagerHost : IManagerWorkspaceHost
    {
        public void Reveal(WorkspaceKind workspace)
        {
        }

        public void RevealPreferences(WorkspaceKind workspace)
        {
        }
    }
}
