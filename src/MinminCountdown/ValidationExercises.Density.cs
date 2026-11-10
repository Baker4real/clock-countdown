using System.Globalization;
using System.Windows;
using MinminCountdown.Core;

namespace MinminCountdown;

// #251：管理窗口密度回收与状态/操作上下文的隔离窗口编排。窗口档复用 #248 的首选、工作区缩小、最小尺寸与真实
// WM_DPICHANGED 五档；测量在窗口的场景 partial（WindowValidationScenarios.Density）里，判定在测量记录上。
internal static partial class ValidationExercises
{
    public static MainWorkbenchDensityProbe RunMainWorkbenchDensityBehavior(
        Action<MainWindow, string>? captureEvidence = null)
    {
        var failures = new List<string>();
        var details = new List<string>();
        var measured = 0;
        foreach (var windowCase in EmptyStateWindowCases)
        {
            WithEmptyMainWindow(windowCase, (main, injection) =>
            {
                var label = windowCase.Label;
                if (windowCase.RaiseDpi)
                {
                    if (injection is not { Reached: true })
                    {
                        failures.Add($"{label}：WM_DPICHANGED 注入未到达目标（{injection?.ToString() ?? "未注入"}）");
                    }

                    label = string.Create(CultureInfo.InvariantCulture, $"dpi-{injection?.Target ?? 0}");
                }

                // 实拍首选、最小与 DPI 变化三档；两档工作区缩小只量不拍。
                Action<string>? revealed = captureEvidence is not null && (windowCase.Capture || windowCase.RaiseDpi)
                    ? state => captureEvidence(main, state)
                    : null;
                var measurement = main.MeasureWorkbenchDensityForValidation(label, revealed);
                measured++;
                details.Add($"{label}：{measurement.Describe()}");
                failures.AddRange(measurement.Failures(
                    windowCase.ExpectedWindow,
                    windowCase.ExpectedWindow == new Size(864, 1000)));
            });
        }

        return new MainWorkbenchDensityProbe(measured, EmptyStateWindowCases.Length, failures, details);
    }

    // GeometryPreflight 入口：失败时把实测写进异常消息。
    public static bool RunMainWorkbenchDensityPreflight()
    {
        var probe = RunMainWorkbenchDensityBehavior();
        Must(probe.Passed, probe.Explain("#251：管理窗口密度回收与状态/操作上下文"));
        return true;
    }

    // #251：悬浮窗滚动上下文提示。空便签列表（不可滚动）、七张长便签的顶部/首部空白/中段/尾部空白/底部，滚轮、
    // thumb 与键盘 PageDown/PageUp 改变偏移后的提示，搜索展开、快速新增展开与「…」菜单打开时的提示，以及十二条倒计时
    // 溢出的顶部与底部（到底时底部状态完整可见）。captureEvidence 在每个状态摆好后调用（实拍用，菜单态带弹层）。
    public static FloatingScrollContextProbe RunFloatingScrollContextHintBehavior(
        Action<FloatingWindow, System.Windows.Controls.Primitives.Popup?, string>? captureEvidence = null)
    {
        using var workArea = MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(new Rect(0, 0, 2560, 2000), 1, 1));
        var failures = new List<string>();
        var details = new List<string>();
        var checkedStates = 0;
        return DualWindowValidationFixture.Run("scroll-context", fixture =>
        {
            var workspace = fixture.Workspace;
            fixture.ShowWindows();
            var today = workspace.Snapshot.BeijingNow.Date;
            var window = fixture.Floating;
            var scroller = window.BodyScrollerForValidation;

            void Expect(string state, bool above, bool below, System.Windows.Controls.Primitives.Popup? popup = null)
            {
                var reading = window.ScrollContextStateForValidation();
                checkedStates++;
                details.Add($"{state}：{reading.Describe()}");
                var problems = reading.Problems(above, below);
                if (problems.Count > 0)
                {
                    failures.Add($"{state}：{string.Join("、", problems)}（{reading.Describe()}）");
                }

                captureEvidence?.Invoke(window, popup, state);
            }

            // 没有可滚动内容：空便签列表只有空态说明与底部状态，不挂提示。悬浮便签卡带日期印记，单张卡加底部状态
            // 已接近便签视口高度，所以不可滚动的状态用空列表，不用「一张短便签」。
            window.ShowWorkspace(WorkspaceKind.Notes);
            window.SettleScrollContextForValidation();
            Must(scroller.ScrollableHeight <= 0.5,
                $"#251：空便签列表不应产生滚动；可滚 {scroller.ScrollableHeight:0.##}。");
            Expect("notes-empty", false, false);

            var longText = string.Concat(Enumerable.Repeat("滚动提示长便签正文，确认上下边缘提示与日期印记都完整。", 2));
            for (var index = 1; index <= 7; index++)
            {
                MustApply(workspace, new SaveNoteChange(null, $"{longText}{index}", today.AddDays(index), false,
                    Array.Empty<string>(), Array.Empty<ReminderRule>()));
            }

            window.ShowWorkspace(WorkspaceKind.Notes);
            window.ScrollBodyForValidation(0);
            Must(scroller.ScrollableHeight > 120, $"#251：七张长便签夹具应产生足够滚动；可滚 {scroller.ScrollableHeight:0.##}。");
            Expect("notes-long-top", false, true);
            // 首部只有列表上边距被截住、尾部只有末尾留白被截住时，那一侧没有内容，不提示。
            window.ScrollBodyForValidation(5);
            Expect("notes-long-leading-blank", false, true);
            window.ScrollBodyForValidation(scroller.ScrollableHeight / 2);
            Expect("notes-long-middle", true, true);
            window.ScrollBodyForValidation(scroller.ScrollableHeight - 5);
            Expect("notes-long-trailing-blank", true, false);
            window.ScrollBodyForValidation(scroller.ScrollableHeight);
            Expect("notes-long-bottom", true, false);

            // 输入路径：滚轮、thumb 与键盘仍由原生滚动处理，提示跟着偏移变。
            window.ScrollBodyForValidation(0);
            var wheel = new System.Windows.Input.MouseWheelEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = scroller
            };
            scroller.RaiseEvent(wheel);
            window.SettleScrollContextForValidation();
            Must(wheel.Handled && scroller.VerticalOffset > 0.5, $"#251：滚轮未滚动悬浮列表；偏移 {scroller.VerticalOffset:0.##}。");
            Expect("notes-long-after-wheel", true, true);
            window.ScrollBodyForValidation(0);
            var thumb = window.BodyScrollThumbForValidation;
            Must(thumb is not null, "#251：悬浮列表滚动条缺少 thumb。");
            thumb!.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
                Source = thumb
            });
            thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, 40)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent,
                Source = thumb
            });
            thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 40, false)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
                Source = thumb
            });
            window.SettleScrollContextForValidation();
            Must(scroller.VerticalOffset > 0.5, $"#251：拖动 thumb 未滚动悬浮列表；偏移 {scroller.VerticalOffset:0.##}。");
            Expect("notes-long-after-thumb", true, true);
            // 键盘：ScrollViewer 原生的纵向翻页键（不带 Ctrl 的 Home / End 在 WPF 里是横向滚动）。PageDown 一路到底、
            // PageUp 一路回顶，提示跟着到边界消失。
            scroller.Focus();
            for (var presses = 0; presses < 40 && scroller.VerticalOffset < scroller.ScrollableHeight - 0.5; presses++)
            {
                WindowValidationInput.RaiseKey(scroller, System.Windows.Input.Key.PageDown);
                window.SettleScrollContextForValidation();
            }

            Must(Math.Abs(scroller.VerticalOffset - scroller.ScrollableHeight) <= 0.5,
                $"#251：PageDown 未滚到悬浮列表底部；偏移 {scroller.VerticalOffset:0.##}/{scroller.ScrollableHeight:0.##}。");
            Expect("notes-long-after-page-down", true, false);
            for (var presses = 0; presses < 40 && scroller.VerticalOffset > 0.5; presses++)
            {
                WindowValidationInput.RaiseKey(scroller, System.Windows.Input.Key.PageUp);
                window.SettleScrollContextForValidation();
            }

            Must(scroller.VerticalOffset <= 0.5, $"#251：PageUp 未回到悬浮列表顶部；偏移 {scroller.VerticalOffset:0.##}。");
            Expect("notes-long-after-page-up", false, true);

            // 搜索展开：列表视口变矮，提示跟随新视口。
            window.SetNoteSearchExpandedForValidation(true);
            window.ScrollBodyForValidation(0);
            Expect("notes-search-expanded-top", false, true);
            window.ScrollBodyForValidation(scroller.ScrollableHeight);
            Expect("notes-search-expanded-bottom", true, false);
            window.SetNoteSearchExpandedForValidation(false);

            // 快速新增展开：命令区变高、列表视口变矮，命令区本身不挂提示。
            window.SetQuickAddExpanded(true);
            window.ScrollBodyForValidation(0);
            Expect("notes-quick-add-top", false, true);
            window.ScrollBodyForValidation(scroller.ScrollableHeight / 2);
            Expect("notes-quick-add-middle", true, true);
            window.CollapseQuickAddForValidation();

            // 「…」菜单：在中段打开露在视口里的卡片菜单，提示不变，弹层不挂遮罩。
            window.ScrollBodyForValidation(scroller.ScrollableHeight / 2);
            var menu = window.OpenVisibleNoteOverflowMenuForValidation();
            Must(menu is { IsOpen: true }, "#251：没能打开视口里便签卡的「…」菜单。");
            Must(menu!.Child is UIElement { OpacityMask: null }, "#251：「…」菜单弹层不应挂遮罩。");
            Expect("notes-menu-open", true, true, menu);
            WindowValidationInput.ClosePopupForCleanup(menu);
            window.SettleScrollContextForValidation();
            Expect("notes-menu-closed", true, true);

            // 倒计时溢出：到底时底部状态（今日已赚 / 下班状态）完整落在视口里，下侧不提示。
            for (var index = 1; index <= 12; index++)
            {
                MustApply(workspace, new SaveCountdownChange(null, $"滚动提示倒计时 {index:00}",
                    workspace.Snapshot.UtcNow.AddDays(index), Array.Empty<string>(), Array.Empty<ReminderRule>()));
            }

            window.ShowWorkspace(WorkspaceKind.Countdowns);
            window.ScrollBodyForValidation(0);
            Must(scroller.ScrollableHeight > 60, $"#251：十二条倒计时应溢出悬浮列表；可滚 {scroller.ScrollableHeight:0.##}。");
            Expect("countdowns-overflow-top", false, true);
            window.ScrollBodyForValidation(scroller.ScrollableHeight);
            Expect("countdowns-overflow-bottom", true, false);
            var income = window.IncomeForValidation;
            if (income.IsVisible && income.ActualHeight > 0)
            {
                var incomeBounds = income.TransformToAncestor(scroller)
                    .TransformBounds(new Rect(new Point(0, 0), income.RenderSize));
                Must(incomeBounds.Top >= -0.5 && incomeBounds.Bottom <= scroller.ViewportHeight + 0.5,
                    $"#251：倒计时滚到底时底部状态应完整可见；{incomeBounds}，视口 {scroller.ViewportHeight:0.##}。");
            }

            return new FloatingScrollContextProbe(checkedStates, 18, failures, details);
        }, store: new SettingsStore(clock: new AdvancingValidationClock(CountdownRowFixtureUtc)));
    }

    public static bool RunFloatingScrollContextHintPreflight()
    {
        var probe = RunFloatingScrollContextHintBehavior();
        Must(probe.Passed, probe.Explain("#251：悬浮窗滚动上下文提示"));
        return true;
    }
}

// #251：悬浮窗滚动提示汇总。全部状态都量到且没有问题才算通过；失败时列出前 6 项。
internal sealed record FloatingScrollContextProbe(
    int Checked,
    int Expected,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Details)
{
    public bool Passed => Checked == Expected && Failures.Count == 0;

    public string Explain(string meaning)
    {
        if (Passed)
        {
            return meaning;
        }

        var shown = string.Join("；", Failures.Take(6));
        var remaining = Failures.Count > 6 ? $"；另有 {Failures.Count - 6} 项" : string.Empty;
        return $"{meaning}；检查 {Checked}/{Expected} 个状态，未通过 {Failures.Count} 项：{shown}{remaining}";
    }
}

// #251：密度汇总。五个窗口档都量到且没有失败项才算通过；失败时列出前 6 项实测。
internal sealed record MainWorkbenchDensityProbe(
    int Measured,
    int Expected,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Details)
{
    public bool Passed => Measured == Expected && Failures.Count == 0;

    public string Explain(string meaning)
    {
        if (Passed)
        {
            return meaning;
        }

        var shown = string.Join("；", Failures.Take(6));
        var remaining = Failures.Count > 6 ? $"；另有 {Failures.Count - 6} 项" : string.Empty;
        return $"{meaning}；测量 {Measured}/{Expected} 个窗口，未通过 {Failures.Count} 项：{shown}{remaining}";
    }
}
