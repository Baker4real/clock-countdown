using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    // #155：为真实确认框路径排队自动应答。确认框在 Click 处理器内以 ShowDialog 模态打开并阻塞，
    // 排队的 ApplicationIdle 计时器在模态泵内轮询 owner 的 OwnedWindows，找到弹层后按需点击
    // 确认/取消；inspect 允许探针在应答前核对弹层内容（安全默认、文案与消息正文）。
    internal static void QueueConfirmationDialogResponse(
        Window owner,
        bool confirm,
        Action<ConfirmationDialog>? inspect = null)
    {
        QueueConfirmationDialogAction(owner, dialog =>
        {
            inspect?.Invoke(dialog);
            return WindowValidationInput.InvokeButton(confirm ? dialog.ConfirmButton : dialog.CancelButton);
        });
    }

    internal static void QueueConfirmationDialogAction(
        Window owner,
        Func<ConfirmationDialog, bool> invoke)
    {
        var attempts = 0;
        ConfirmationDialog? dispatchedDialog = null;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, owner.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(40)
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
                    throw new InvalidOperationException("确认框验证未能调用按钮命令；窗口仅为失败收口而关闭。");
                }
            }

            if (attempts >= 75)
            {
                timer.Stop();
                dispatchedDialog?.Close();
                // Fail the validation process after bounded modal cleanup.
                // Closing the dialog is never counted as a successful action.
                throw new InvalidOperationException("确认框验证在 3 秒内未完成按钮命令或窗口关闭。");
            }
        };
        timer.Start();
    }

    // #155：「完成便签」确认框行为——先取消（安全默认，便签/条目/可完成状态原样保留），
    // 再确认（原子完成便签、移除中心条目、写入处置 key）。「全部知道了」不经本入口。
    public static bool RunReminderCenterCompleteRequiresConfirmationBehavior()
    {
        var dueUtc = new DateTimeOffset(2036, 5, 12, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-complete-confirm-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new AdvancingValidationClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var rule = new AbsoluteOnceReminderRule("complete-confirm-rule", dueUtc);
            Must(
                workspace.Apply(new SaveNoteChange(
                    null,
                    "完成确认探针便签",
                    new DateTime(2036, 5, 12),
                    false,
                    Array.Empty<string>(),
                    new ReminderRule[] { rule })).Succeeded,
                "无法建立完成确认探针便签。");
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = rule.ScheduledUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(clock.UtcNow);
            var center = coordinator.ActiveWindow ??
                         throw new InvalidOperationException("提醒中心未打开。");
            center.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                center.IsCompleteEnabled(key) &&
                coordinator.ActiveEntryCount == 1,
                "完成确认探针条目未进入可完成状态。");

            QueueConfirmationDialogResponse(center, confirm: false);
            Must(center.RequestComplete(key), "无法点击真实完成便签控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                !workspace.Snapshot.Settings.Notes.Single().IsCompleted &&
                coordinator.ActiveEntryCount == 1 &&
                center.IsCompleteEnabled(key) &&
                !center.IsFailureVisible(key),
                "完成便签确认框取消后必须原样保留便签、中心条目与可完成状态。");

            QueueConfirmationDialogResponse(
                center,
                confirm: true,
                inspect: dialog => Must(
                    string.Equals(
                        dialog.ConfirmButton.Content?.ToString(),
                        "确认完成",
                        StringComparison.Ordinal) &&
                    dialog.CancelButton.IsDefault &&
                    dialog.CancelButton.IsCancel &&
                    !dialog.ConfirmButton.IsDefault,
                    "完成便签确认框没有以取消为安全默认或确认文案不是「确认完成」。"));
            Must(center.RequestComplete(key), "无法再次点击真实完成便签控件。");
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            Must(
                workspace.Snapshot.Settings.Notes.Single().IsCompleted &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                workspace.Snapshot.Settings.ReminderStates[rule.Id].LastDispositionOccurrenceKey ==
                    ReminderOccurrenceKey.Create(rule, rule.ScheduledUtc).ToString(),
                "完成便签确认后没有原子完成便签并移除全部中心条目。");
            return true;
        }
        finally
        {
            coordinator?.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(settingsPath + ".tmp");
            DeleteDirectoryIfEmpty(settingsPath + ".tmp");
        }
    }

    // #155：最近一次删除的短时撤销——确认框取消为安全默认；删除成功后主窗口状态条提供
    // 「撤销」入口；撤销在同一事务恢复记录与规则提醒状态；连续删除只保留最近一次；入口短时过期。
    public static bool RunNoteCompletionExitTransition()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-completion-exit-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        MustApply(workspace, new SaveNoteChange(null, "完成淡出探针", null, false, [], Array.Empty<ReminderRule>()));
        var note = workspace.Snapshot.Settings.Notes.Single();
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main); PrepareOffscreenValidationWindow(floating);
            main.Show(); floating.Show(); main.ShowWorkspace(WorkspaceKind.Notes); floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            foreach (var onFloating in new[] { true, false })
            {
                var source = onFloating ? (Window)floating : main;
                var viewport = onFloating ? floating.FloatingBodyScroller : main.NotesListRegion;
                var undo = onFloating ? floating.FloatingDeleteUndoButton : main.MainStatusUndoButton;
                Adorner? ExitVisual() => AdornerLayer.GetAdornerLayer(viewport)?.GetAdorners(viewport)?
                    .SingleOrDefault(visual => AutomationProperties.GetAutomationId(visual) == "NoteCompletionExit");
                void Complete()
                {
                    var check = VisualDescendants<CheckBox>(source).Single(candidate => candidate.Tag as string == note.Id);
                    check.BringIntoView(); WindowValidationInput.Pump(source.Dispatcher);
                    var peer = new CheckBoxAutomationPeer(check);
                    ((System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
                }
                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(true))
                {
                    Complete();
                    var visual = ExitVisual();
                    Must(visual is not null && visual.HasAnimatedProperties && !visual.IsHitTestVisible &&
                        new SettingsStore().Load(path).Settings.Notes.Single().IsCompleted,
                        "#233：成功提交后必须立即保留不可交互的完成卡片离场视觉，不得延迟完成写盘。");
                    Must(WindowValidationInput.WaitUntil(source.Dispatcher, () => visual!.Opacity is > 0 and < 1),
                        "#233：离场视觉没有经过真实透明度中间态。");
                    Must(!DependencyPropertyHelper.GetValueSource(visual!, FrameworkElement.WidthProperty).IsAnimated &&
                        !DependencyPropertyHelper.GetValueSource(visual!, FrameworkElement.HeightProperty).IsAnimated &&
                        !DependencyPropertyHelper.GetValueSource(visual!, FrameworkElement.MarginProperty).IsAnimated &&
                        ((Duration)Application.Current.FindResource("MotionNoteCompletionExitDuration")).TimeSpan <= TimeSpan.FromMilliseconds(200),
                        "#233：离场过渡不得动画布局属性，且必须不超过200ms。");
                    Must(WindowValidationInput.WaitUntil(source.Dispatcher, () => ExitVisual() is null),
                        "#233：完成离场没有释放视觉与动画。");
                    undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WindowValidationInput.Pump(source.Dispatcher);
                    Complete();
                    Must(ExitVisual() is not null, "#233：第二次完成没有离场视觉。");
                    undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WindowValidationInput.Pump(source.Dispatcher);
                    Must(ExitVisual() is null && !new SettingsStore().Load(path).Settings.Notes.Single().IsCompleted,
                        "#233：离场中撤销必须立即停止旧动画并落到重新打开状态。");
                    Complete(); source.Hide(); WindowValidationInput.Pump(source.Dispatcher);
                    Must(ExitVisual() is null, "#233：窗口隐藏后不应保留离场视觉。");
                    source.Show(); undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WindowValidationInput.Pump(source.Dispatcher);
                    Complete();
                    if (onFloating) floating.ShowWorkspace(WorkspaceKind.Countdowns);
                    else main.ShowWorkspace(WorkspaceKind.Countdowns);
                    WindowValidationInput.Pump(source.Dispatcher);
                    Must(ExitVisual() is null, "#233：切换工作区后不应保留旧便签离场视觉。");
                    if (onFloating) floating.ShowWorkspace(WorkspaceKind.Notes);
                    else main.ShowWorkspace(WorkspaceKind.Notes);
                    undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WindowValidationInput.Pump(source.Dispatcher);
                }
                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
                {
                    Complete();
                    Must(ExitVisual() is null && new SettingsStore().Load(path).Settings.Notes.Single().IsCompleted,
                        "#233：关闭客户区动画时必须同步到达完成终态。");
                    undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WindowValidationInput.Pump(source.Dispatcher);
                }
            }
            return true;
        }
        finally
        {
            floating.Close(); main.Close(); DeleteSettingsFiles(path); DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunNoteCompletionFlipTransition()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-completion-flip-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        void Add(string text, DateTime date) => MustApply(
            workspace,
            new SaveNoteChange(null, text, date, false, [], Array.Empty<ReminderRule>()));
        Add("零位移便签", new DateTime(2038, 7, 14));
        Add("悬浮下一张", new DateTime(2038, 7, 13));
        Add("悬浮完成", new DateTime(2038, 7, 13));
        Add("管理下一张", new DateTime(2038, 7, 12));
        Add("管理完成", new DateTime(2038, 7, 12));
        Add("日期末条", new DateTime(2038, 7, 11));
        Add("日期下方", new DateTime(2038, 7, 10));
        for (var index = 0; index < 8; index++)
        {
            Add($"视口外{index}", new DateTime(2038, 7, 8 - index));
        }

        var notes = workspace.Snapshot.Settings.Notes;
        var stationary = notes.Single(note => note.Text == "零位移便签");
        var floatingTarget = notes.Single(note => note.Text == "悬浮完成");
        var floatingNext = notes.Single(note => note.Text == "悬浮下一张");
        var mainTarget = notes.Single(note => note.Text == "管理完成");
        var mainNext = notes.Single(note => note.Text == "管理下一张");
        var lastInDate = notes.Single(note => note.Text == "日期末条");
        var belowDate = notes.Single(note => note.Text == "日期下方");
        // 日期最早的一条排在两个列表末尾，完成前后都在视口外。
        var farFiller = notes.Single(note => note.Text == "视口外7");
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            PrepareOffscreenValidationWindow(floating);
            main.Show();
            floating.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);

            CheckBox CompletionCheck(Window source, string id) =>
                VisualDescendants<CheckBox>(source).Single(candidate => candidate.Tag as string == id);
            FrameworkElement Card(Window source, string id)
            {
                var check = CompletionCheck(source, id);
                return VisualDescendants<Border>(source).Single(candidate =>
                    candidate.Child is Grid grid && grid.Children.Contains(check));
            }
            NoteCompletionExit? ExitVisual(ScrollViewer viewport) =>
                AdornerLayer.GetAdornerLayer(viewport)?.GetAdorners(viewport)?
                    .OfType<NoteCompletionExit>()
                    .SingleOrDefault();
            Rect Bounds(FrameworkElement element, ScrollViewer viewport) =>
                element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
            bool OutsideViewport(Rect bounds, ScrollViewer viewport) =>
                bounds.Bottom <= 0 || bounds.Top >= viewport.ActualHeight;
            double TranslationY(FrameworkElement element) =>
                element.RenderTransform is System.Windows.Media.TranslateTransform translate ? translate.Y : 0;
            // 元素自身及其祖先（到视口为止）上的纵向位移之和，即该元素实际受到的补位。
            double Displacement(FrameworkElement element, ScrollViewer viewport)
            {
                var total = 0d;
                for (DependencyObject? current = element;
                     current is not null && !ReferenceEquals(current, viewport);
                     current = System.Windows.Media.VisualTreeHelper.GetParent(current))
                {
                    if (current is FrameworkElement { RenderTransform: System.Windows.Media.TranslateTransform translate })
                    {
                        total += translate.Y;
                    }
                }
                return total;
            }
            // 下沿对齐时，下一张卡在完成前完整位于视口下方，补位从视口外进入；上沿对齐时下一张卡完成前可见。
            void Align(Window source, ScrollViewer viewport, FrameworkElement element, bool bottom)
            {
                element.BringIntoView();
                WindowValidationInput.Pump(source.Dispatcher);
                var bounds = Bounds(element, viewport);
                viewport.ScrollToVerticalOffset(Math.Clamp(
                    viewport.VerticalOffset + (bottom ? bounds.Bottom - viewport.ActualHeight : bounds.Top),
                    0,
                    viewport.ScrollableHeight));
                WindowValidationInput.Pump(source.Dispatcher);
            }
            void Toggle(Window source, string id)
            {
                var peer = new CheckBoxAutomationPeer(CompletionCheck(source, id));
                ((System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
            }
            (NoteCompletionExit Visual, FrameworkElement Next, double Delta) Begin(
                Window source,
                ScrollViewer viewport,
                NoteRecord target,
                NoteRecord next,
                bool nextEntersFromBelow)
            {
                var targetCard = Card(source, target.Id);
                Align(source, viewport, targetCard, bottom: nextEntersFromBelow);
                var cardSize = targetCard.RenderSize;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(targetCard);
                var oldNext = Bounds(Card(source, next.Id), viewport);
                var oldStationary = Bounds(Card(source, stationary.Id), viewport);
                Must(OutsideViewport(oldNext, viewport) == nextEntersFromBelow &&
                    (!nextEntersFromBelow || !OutsideViewport(oldStationary, viewport)) &&
                    OutsideViewport(Bounds(Card(source, farFiller.Id), viewport), viewport),
                    $"#243：完成补位探针的视口前置条件不成立。surface={source.GetType().Name}; " +
                    $"nextTop={oldNext.Top:0.###}; stationaryTop={oldStationary.Top:0.###}; " +
                    $"viewport={viewport.ActualHeight:0.###}; nextEntersFromBelow={nextEntersFromBelow}。");

                // 不泵消息：领域事务、列表重建与反向位移在同一次同步调用里完成，首帧渲染前即可观察。
                Toggle(source, target.Id);
                // 新设的 RenderTransform 要经过一次排列才并入视觉变换，首帧渲染前的布局遍历同样做这一步；
                // UpdateLayout 只处理布局队列，不推进动画时钟。
                viewport.UpdateLayout();
                var visual = ExitVisual(viewport);
                Must(visual is not null && !visual.IsHitTestVisible && Math.Abs(visual.Opacity - 1) <= 0.001 &&
                    new SettingsStore().Load(path).Settings.Notes.Single(note => note.Id == target.Id).IsCompleted,
                    "#243：完成提交后必须立即保留不透明的卡片快照，且写盘时机不变。");
                var nextAfter = Card(source, next.Id);
                var delta = Displacement(nextAfter, viewport);
                var newNextTop = Bounds(nextAfter, viewport).Top - delta;
                Must(nextAfter.RenderTransform is System.Windows.Media.TranslateTransform && delta > 0 &&
                    Math.Abs(delta - (oldNext.Top - newNextTop)) <= 0.1,
                    $"#243：下一张卡的起始位移必须等于旧位置减新位置。surface={source.GetType().Name}; " +
                    $"oldY={oldNext.Top:0.###}; newY={newNextTop:0.###}; displacement={delta:0.###}; " +
                    $"transform={nextAfter.RenderTransform?.GetType().Name ?? "null"}; " +
                    $"captured={visual!.CapturedPositionCountForValidation}; activeFlip={visual.ActiveFlipCountForValidation}。");
                Must(visual.SnapshotPixelWidthForValidation == (int)Math.Ceiling(cardSize.Width * dpi.DpiScaleX) &&
                    visual.SnapshotPixelHeightForValidation == (int)Math.Ceiling(cardSize.Height * dpi.DpiScaleY),
                    "#243：快照像素尺寸必须等于完成卡片尺寸乘DPI，而不是列表视口尺寸。" +
                    $"snapshot={visual.SnapshotPixelWidthForValidation}x{visual.SnapshotPixelHeightForValidation}; " +
                    $"card={cardSize.Width:0.###}x{cardSize.Height:0.###}; dpiScale={dpi.DpiScaleX:0.###}。");
                var farAfter = Card(source, farFiller.Id);
                Must(Math.Abs(Displacement(Card(source, stationary.Id), viewport)) <= 0.001 &&
                    Math.Abs(Displacement(farAfter, viewport)) <= 0.001 &&
                    OutsideViewport(Bounds(farAfter, viewport), viewport),
                    "#243：零位移便签与完成前后都在视口外的便签不得产生补位。");
                return (visual, nextAfter, delta);
            }
            void AssertCleared(ScrollViewer viewport, FrameworkElement moved, string scenario) =>
                Must(ExitVisual(viewport) is null && Math.Abs(Displacement(moved, viewport)) <= 0.001,
                    $"#243：{scenario}后快照必须消失且补位必须立即清零。");

            foreach (var onFloating in new[] { true, false })
            {
                var source = onFloating ? (Window)floating : main;
                var viewport = onFloating ? floating.FloatingBodyScroller : main.NotesListRegion;
                var undo = onFloating ? floating.FloatingDeleteUndoButton : main.MainStatusUndoButton;
                var target = onFloating ? floatingTarget : mainTarget;
                var next = onFloating ? floatingNext : mainNext;
                // 悬浮窗视口约一张半卡高：下一张卡完成前整张在视口下方，从视口外补位进入；管理窗口下一张卡完成前可见。
                var nextEntersFromBelow = onFloating;
                void Undo()
                {
                    undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    WindowValidationInput.Pump(source.Dispatcher);
                }

                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(true))
                {
                    var running = Begin(source, viewport, target, next, nextEntersFromBelow);
                    Must(WindowValidationInput.WaitUntil(source.Dispatcher, () =>
                            running.Visual.Opacity is > 0 and < 1 &&
                            Displacement(running.Next, viewport) is > 0 &&
                            Displacement(running.Next, viewport) < running.Delta),
                        "#243：快照和补位没有经过真实中间帧。");
                    var remaining = Displacement(running.Next, viewport) / running.Delta;
                    Must(Math.Abs(remaining - running.Visual.Opacity) <= 0.04,
                        "#243：快照淡出与补位必须使用同一时钟和缓动。" +
                        $"remaining={remaining:0.####}; opacity={running.Visual.Opacity:0.####}。");
                    Must(WindowValidationInput.WaitUntil(source.Dispatcher, () =>
                            ExitVisual(viewport) is null && Math.Abs(Displacement(running.Next, viewport)) <= 0.001),
                        "#243：160ms完成后快照和补位必须一起释放。");
                    Undo();

                    running = Begin(source, viewport, target, next, nextEntersFromBelow);
                    undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    AssertCleared(viewport, running.Next, "撤销");
                    WindowValidationInput.Pump(source.Dispatcher);

                    running = Begin(source, viewport, target, next, nextEntersFromBelow);
                    if (onFloating) floating.ShowWorkspace(WorkspaceKind.Countdowns);
                    else main.ShowWorkspace(WorkspaceKind.Countdowns);
                    WindowValidationInput.Pump(source.Dispatcher);
                    AssertCleared(viewport, running.Next, "切换工作区");
                    if (onFloating) floating.ShowWorkspace(WorkspaceKind.Notes);
                    else main.ShowWorkspace(WorkspaceKind.Notes);
                    Undo();

                    running = Begin(source, viewport, target, next, nextEntersFromBelow);
                    source.Hide();
                    WindowValidationInput.Pump(source.Dispatcher);
                    AssertCleared(viewport, running.Next, "隐藏");
                    source.Show();
                    WindowValidationInput.Pump(source.Dispatcher);
                    Undo();

                    running = Begin(source, viewport, target, next, nextEntersFromBelow);
                    source.WindowState = WindowState.Minimized;
                    WindowValidationInput.Pump(source.Dispatcher);
                    AssertCleared(viewport, running.Next, "最小化");
                    source.WindowState = WindowState.Normal;
                    WindowValidationInput.Pump(source.Dispatcher);
                    Undo();

                    // 离场进行中再有领域变更触发整表重建：离场立即清掉。
                    running = Begin(source, viewport, target, next, nextEntersFromBelow);
                    MustApply(workspace, new SaveNoteChange(null, "重建新便签", new DateTime(2038, 7, 15),
                        false, [], Array.Empty<ReminderRule>()));
                    var added = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "重建新便签");
                    AssertCleared(viewport, running.Next, "整表重建");
                    Undo();
                    MustApply(workspace, new DeleteNoteChange(added.Id));
                    WindowValidationInput.Pump(source.Dispatcher);

                    if (onFloating)
                    {
                        // 捕获之后、Start 之前的同一同步步骤里既完成目标又插入一条新便签。新便签排在下一张卡之后，
                        // 落在下一张卡完成前的位置上（视口内），捕获时没有旧位置，不得补位；下一张卡照常从旧位置
                        // 补位，证明这次 Start 确实施加了补位。
                        var targetCard = Card(floating, target.Id);
                        Align(floating, viewport, targetCard, bottom: false);
                        var oldNext = Bounds(Card(floating, next.Id), viewport);
                        Must(!OutsideViewport(oldNext, viewport),
                            $"#243：新条目补位探针的视口前置条件不成立。nextTop={oldNext.Top:0.###}; " +
                            $"viewport={viewport.ActualHeight:0.###}。");
                        var exit = NoteCompletionExit.Capture(targetCard, viewport) ??
                            throw new InvalidOperationException("#243：开启动画时完成离场必须截取完成卡片。");
                        MustApply(workspace, new SetNoteCompletionChange(target.Id, true));
                        MustApply(workspace, new SaveNoteChange(null, "补位新便签", new DateTime(2038, 7, 12),
                            false, [], Array.Empty<ReminderRule>()));
                        var inserted = workspace.Snapshot.Settings.Notes.Single(note => note.Text == "补位新便签");
                        exit.Start();
                        viewport.UpdateLayout();
                        var nextAfter = Card(floating, next.Id);
                        var nextDelta = Displacement(nextAfter, viewport);
                        var newNextTop = Bounds(nextAfter, viewport).Top - nextDelta;
                        Must(ExitVisual(viewport) is not null && exit.ActiveFlipCountForValidation > 0 &&
                            nextDelta > 0 && Math.Abs(nextDelta - (oldNext.Top - newNextTop)) <= 0.1,
                            $"#243：插入新便签的同时，原有的下一张卡仍须从旧位置补位。oldY={oldNext.Top:0.###}; " +
                            $"newY={newNextTop:0.###}; displacement={nextDelta:0.###}; " +
                            $"activeFlip={exit.ActiveFlipCountForValidation}。");
                        var insertedCard = Card(floating, inserted.Id);
                        var insertedBounds = Bounds(insertedCard, viewport);
                        var insertedDelta = Displacement(insertedCard, viewport);
                        Must(!OutsideViewport(insertedBounds, viewport) && Math.Abs(insertedDelta) <= 0.001,
                            $"#243：捕获时不存在的新便签不得补位。top={insertedBounds.Top:0.###}; " +
                            $"displacement={insertedDelta:0.###}; viewport={viewport.ActualHeight:0.###}。");
                        exit.Dispose();
                        AssertCleared(viewport, nextAfter, "新条目补位探针结束");
                        MustApply(workspace, new SetNoteCompletionChange(target.Id, false));
                        MustApply(workspace, new DeleteNoteChange(inserted.Id));
                        WindowValidationInput.Pump(floating.Dispatcher);
                    }

                    running = Begin(source, viewport, target, next, nextEntersFromBelow);
                    viewport.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    AssertCleared(viewport, running.Next, "卸载");
                    Undo();

                    if (!onFloating)
                    {
                        Grid DateSection(string? date) => VisualDescendants<Grid>(main).Single(section =>
                            section.Uid == "NoteDateTimelineSection" && section.IsVisible &&
                            VisualDescendants<Button>(section).Any(toggle =>
                                toggle.Uid == "NoteDateDisclosure" &&
                                string.Equals(toggle.Tag as string, date ?? string.Empty, StringComparison.Ordinal)));
                        Align(main, viewport, Card(main, lastInDate.Id), bottom: false);
                        var oldSection = Bounds(DateSection(belowDate.Date), viewport);
                        Toggle(main, lastInDate.Id);
                        viewport.UpdateLayout();
                        var sectionAfter = DateSection(belowDate.Date);
                        var sectionDelta = Displacement(sectionAfter, viewport);
                        var newSectionTop = Bounds(sectionAfter, viewport).Top - sectionDelta;
                        Must(ExitVisual(viewport) is not null &&
                            sectionAfter.RenderTransform is System.Windows.Media.TranslateTransform &&
                            sectionDelta > 0 && Math.Abs(sectionDelta - (oldSection.Top - newSectionTop)) <= 0.1 &&
                            Math.Abs(TranslationY(Card(main, belowDate.Id))) <= 0.001,
                            "#243：日期最后一条便签完成后，下面的日期分组必须整体按旧位置减新位置补位。" +
                            $"oldY={oldSection.Top:0.###}; newY={newSectionTop:0.###}; displacement={sectionDelta:0.###}。");
                        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        AssertCleared(viewport, sectionAfter, "日期分组撤销");
                        WindowValidationInput.Pump(main.Dispatcher);
                    }
                }

                using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
                {
                    Toggle(source, target.Id);
                    Must(ExitVisual(viewport) is null && Math.Abs(Displacement(Card(source, next.Id), viewport)) <= 0.001 &&
                        new SettingsStore().Load(path).Settings.Notes.Single(note => note.Id == target.Id).IsCompleted,
                        "#243：系统关闭动画时不得产生快照或补位，完成事务仍须同步提交。");
                    Undo();
                }
            }

            return true;
        }
        finally
        {
            floating.Close();
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunNoteCompletionUndoFeedback()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-completion-undo-{Guid.NewGuid():N}.json");
        var firstDue = new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(firstDue.AddMinutes(-1));
        var workspace = DesktopWorkspace.Load(path, new SettingsStore(clock: clock));
        var rule = new FixedIntervalReminderRule("completion-undo-repeat", firstDue, new ReminderInterval(15));
        MustApply(workspace, new SaveNoteChange(null, "完成反馈探针", new DateTime(2038, 7, 8), false,
            [], new ReminderRule[] { rule }));
        var note = workspace.Snapshot.Settings.Notes.Single();
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main); PrepareOffscreenValidationWindow(floating);
            main.Show(); floating.Show(); main.ShowWorkspace(WorkspaceKind.Notes); floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            foreach (var onFloating in new[] { true, false })
            {
                var source = onFloating ? (Window)floating : main;
                clock.UtcNow = firstDue.AddMinutes(onFloating ? 7 : 70);
                var check = VisualDescendants<CheckBox>(source).Single(candidate => candidate.Tag as string == note.Id);
                var peer = new CheckBoxAutomationPeer(check);
                ((System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
                WindowValidationInput.Pump(source.Dispatcher);
                var disk = new SettingsStore(clock: clock).Load(path).Settings;
                var undo = onFloating ? floating.FloatingDeleteUndoButton : main.MainStatusUndoButton;
                var message = onFloating ? floating.FloatingDeleteUndoText : main.StatusText;
                Must(disk.Notes.Single().IsCompleted && workspace.Snapshot.Settings.Notes.Single().IsCompleted,
                    "#233：真实勾选没有持久化完成状态。");
                Must(undo.IsVisible && message.Text == $"已完成「{note.Text}」·" &&
                    undo.Content?.ToString() == "撤销" &&
                    AutomationProperties.GetName(undo) == $"撤销完成便签「{note.Text}」",
                    $"#233：{(onFloating ? "悬浮窗" : "管理窗口")}完成后没有就地完成反馈和撤销入口。");
                clock.UtcNow = firstDue.AddMinutes(onFloating ? 68 : 98);
                undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WindowValidationInput.Pump(source.Dispatcher);
                disk = new SettingsStore(clock: clock).Load(path).Settings;
                var handledThrough = firstDue.AddMinutes(onFloating ? 60 : 90);
                Must(!disk.Notes.Single().IsCompleted && !workspace.Snapshot.Settings.Notes.Single().IsCompleted &&
                    disk.ReminderStates[rule.Id].HandledThroughUtc == handledThrough && !undo.IsVisible,
                    "#233：撤销没有经重新打开路径持久化，或没有推进到撤销提交时刻的cursor。");
                Must(workspace.GetDueReminderOccurrences(handledThrough.AddMinutes(14)).Count == 0 &&
                    workspace.GetDueReminderOccurrences(handledThrough.AddMinutes(15))
                        .Any(item => item.Rule.Id == rule.Id && item.ScheduledUtc == handledThrough.AddMinutes(15)),
                    "#233：撤销后没有只恢复严格未来的提醒周期。");
                var beforeFailure = File.ReadAllText(path);
                Directory.CreateDirectory(path + ".tmp");
                check = VisualDescendants<CheckBox>(source).Single(candidate => candidate.Tag as string == note.Id);
                peer = new CheckBoxAutomationPeer(check);
                ((System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
                WindowValidationInput.Pump(source.Dispatcher);
                Must(check.IsChecked == false && !undo.IsVisible && File.ReadAllText(path) == beforeFailure &&
                    !(onFloating ? floating.FloatingNoteMessage.Text : main.StatusText.Text).StartsWith("已完成", StringComparison.Ordinal),
                    "#233：完成失败后勾选、磁盘或成功撤销提示未保持失败合同。");
                Must(!string.IsNullOrWhiteSpace(onFloating ? floating.FloatingNoteMessage.Text : main.StatusText.Text),
                    "#233：完成失败没有保留现有错误信息。");
                DeleteDirectoryIfEmpty(path + ".tmp");
            }
            MustApply(workspace, new SaveNoteChange(null, "最近完成探针", null, false, [], Array.Empty<ReminderRule>()));
            var other = workspace.Snapshot.Settings.Notes.Single(item => item.Id != note.Id);
            void CompleteIn(Window source, string id)
            {
                var check = VisualDescendants<CheckBox>(source).Single(candidate => candidate.Tag as string == id);
                var peer = new CheckBoxAutomationPeer(check);
                ((System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
                WindowValidationInput.Pump(source.Dispatcher);
            }
            CompleteIn(floating, note.Id);
            CompleteIn(main, other.Id);
            Must(!floating.FloatingDeleteUndoButton.IsVisible && main.MainStatusUndoButton.IsVisible,
                "#233：另一窗口完成后必须替换唯一撤销槽。");
            main.MainStatusUndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var latestDisk = new SettingsStore(clock: clock).Load(path).Settings;
            Must(latestDisk.Notes.Single(item => item.Id == note.Id).IsCompleted &&
                !latestDisk.Notes.Single(item => item.Id == other.Id).IsCompleted,
                "#233：跨窗口撤销只能重新打开最近完成的便签。");
            CompleteIn(floating, other.Id);
            MustApply(workspace, new SaveNoteChange(null, "删除替换完成探针", null, false, [], Array.Empty<ReminderRule>()));
            var deleted = workspace.Snapshot.Settings.Notes.Single(item => item.Id != note.Id && item.Id != other.Id);
            QueueConfirmationDialogResponse(main, confirm: true);
            VisualDescendants<Button>(main).Single(button => button.Tag as string == deleted.Id &&
                button.Content?.ToString() == "删除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(!floating.FloatingDeleteUndoButton.IsVisible && main.MainStatusUndoButton.IsVisible &&
                main.DeleteUndoForValidation is not null, "#233：删除必须替换完成撤销，不能保留竞争槽。");
            main.MainStatusUndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            latestDisk = new SettingsStore(clock: clock).Load(path).Settings;
            Must(latestDisk.Notes.Any(item => item.Id == deleted.Id) &&
                latestDisk.Notes.Single(item => item.Id == other.Id).IsCompleted &&
                !floating.FloatingDeleteUndoButton.IsVisible, "#233：删除撤销不能恢复更早的完成撤销提示。");
            CompleteIn(floating, deleted.Id);
            clock.UtcNow = clock.UtcNow.AddSeconds(9);
            workspace.ExpireUndoIfDue();
            Must(floating.FloatingDeleteUndoButton.IsVisible, "#233：完成撤销应保留10秒。");
            clock.UtcNow = clock.UtcNow.AddSeconds(2);
            workspace.ExpireUndoIfDue();
            Must(!floating.FloatingDeleteUndoButton.IsVisible &&
                new SettingsStore(clock: clock).Load(path).Settings.Notes.Single(item => item.Id == deleted.Id).IsCompleted,
                "#233：完成撤销到期应只关闭提示，不重新打开便签。");
            return true;
        }
        finally
        {
            floating.Close(); main.Close();
            DeleteDirectoryIfEmpty(path + ".tmp");
            DeleteSettingsFiles(path); DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunDeleteUndoOfferBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-delete-undo-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        var rule = new AbsoluteOnceReminderRule("undo-absolute-rule", nowUtc.AddMinutes(30));
        MustApply(workspace, new SaveCountdownChange(
            null,
            "撤销倒计时甲",
            nowUtc.AddDays(3),
            Array.Empty<string>(),
            new ReminderRule[] { rule }));
        var countdownA = workspace.Snapshot.Settings.Countdowns.Single();
        MustApply(workspace, new SaveCountdownChange(
            null,
            "撤销倒计时乙",
            nowUtc.AddDays(5),
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var countdownB = workspace.Snapshot.Settings.Countdowns.FirstOrDefault(item =>
            !string.Equals(item.Id, countdownA.Id, StringComparison.OrdinalIgnoreCase));
        Must(countdownB is not null, "无法定位第二个撤销探针倒计时。");
        MustApply(workspace, new SaveNoteChange(
            null,
            "撤销便签丙",
            new DateTime(2038, 7, 8),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var noteC = workspace.Snapshot.Settings.Notes.Single();
        clock.UtcNow = rule.ScheduledUtc;
        Must(
            workspace.ConfirmReminderDisplayed(
                ReminderSourceKind.Countdown,
                countdownA.Id,
                rule.Id,
                rule.ScheduledUtc).Succeeded,
            "无法预置倒计时提醒已送达状态。");
        var stateBefore = workspace.Snapshot.Settings.ReminderStates[rule.Id];

        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            // Roslyn 对该局部在跨 Must/本地函数调用后的空流状态判定偏保守，四处用 ! 断言非空。
            Button undoButton = main.MainStatusUndoButton;
            TextBlock statusText = main.StatusText;
            Must(
                undoButton is not null &&
                statusText is not null &&
                main.DescribeUnsavedExitWork() is null &&
                main.DeleteUndoForValidation is null &&
                undoButton.Visibility == Visibility.Collapsed,
                "撤销入口初始状态必须干净且不可见。");

            Button DeleteButton(string id)
            {
                var button = VisualDescendants<Button>(main).FirstOrDefault(candidate =>
                    string.Equals(candidate.Content?.ToString(), "删除", StringComparison.Ordinal) &&
                    string.Equals(candidate.Tag as string, id, StringComparison.OrdinalIgnoreCase));
                Must(button is not null, $"无法定位记录 {id} 的行内删除按钮。");
                return button!;
            }

            QueueConfirmationDialogResponse(main, confirm: false);
            DeleteButton(countdownA.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdownA.Id) &&
                main.DeleteUndoForValidation is null &&
                undoButton!.Visibility == Visibility.Collapsed,
                "删除倒计时确认框取消后必须原样保留记录且不出现撤销入口。");

            QueueConfirmationDialogResponse(main, confirm: true);
            DeleteButton(countdownA.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var undo = main.DeleteUndoForValidation;
            Must(
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdownA.Id) &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(rule.Id) &&
                undo is not null &&
                undo.Countdown is not null &&
                string.Equals(undo.KindName, "倒计时", StringComparison.Ordinal) &&
                string.Equals(undo.Description, countdownA.Name, StringComparison.Ordinal) &&
                undo.ReminderStates.ContainsKey(rule.Id) &&
                undoButton!.Visibility == Visibility.Visible &&
                string.Equals(
                    AutomationProperties.GetName(undoButton),
                    $"撤销删除倒计时「{countdownA.Name}」",
                    StringComparison.Ordinal) &&
                string.Equals(
                    statusText!.Text,
                    $"已删除倒计时「{countdownA.Name}」。",
                    StringComparison.Ordinal),
                "删除倒计时成功后状态区没有提供带完整上下文的撤销入口，或提醒状态没有随删除清理。");

            undoButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var restoredA = workspace.Snapshot.Settings.Countdowns.FirstOrDefault(item =>
                item.Id == countdownA.Id);
            Must(
                restoredA is not null &&
                string.Equals(restoredA.Name, countdownA.Name, StringComparison.Ordinal) &&
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(rule.Id, out var restoredState) &&
                restoredState == stateBefore &&
                main.DeleteUndoForValidation is null &&
                undoButton.Visibility == Visibility.Collapsed &&
                string.Equals(
                    statusText!.Text,
                    $"已撤销删除倒计时「{countdownA.Name}」。",
                    StringComparison.Ordinal),
                "撤销删除必须恢复倒计时与其规则提醒状态并收起入口。");

            QueueConfirmationDialogResponse(main, confirm: true);
            DeleteButton(countdownB!.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            // 便签行只在便签工作区渲染（RenderVisibleWorkspace），删除/撤销便签前先切换工作区。
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            QueueConfirmationDialogResponse(main, confirm: true);
            DeleteButton(noteC.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var latestUndo = main.DeleteUndoForValidation;
            Must(
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdownB.Id) &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteC.Id) &&
                latestUndo is not null &&
                latestUndo.Note is not null &&
                string.Equals(latestUndo.KindName, "便签", StringComparison.Ordinal) &&
                string.Equals(latestUndo.Description, noteC.Text, StringComparison.Ordinal),
                "连续删除必须只保留最近一次删除的撤销入口。");

            undoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteC.Id) &&
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdownB.Id) &&
                main.DeleteUndoForValidation is null,
                "撤销入口必须只恢复最近一次删除的记录。");

            QueueConfirmationDialogResponse(main, confirm: true);
            DeleteButton(noteC.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.DeleteUndoForValidation is not null &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteC.Id),
                "删除后撤销入口应可用。");
            clock.UtcNow = clock.UtcNow.Add(DesktopWorkspace.UndoWindow);
            workspace.ExpireUndoIfDue();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.DeleteUndoForValidation is null &&
                undoButton!.Visibility == Visibility.Collapsed &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteC.Id),
                "撤销入口过期后必须收起且不影响已删除状态。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #214：悬浮窗删除就地撤销。管理窗口从未创建时，底部状态行出现「撤销」并恢复便签与规则提醒状态；
    // 管理窗口删除仍走状态条。Ctrl+Z 在撤销期间生效。焦点或悬停暂停 10 秒计时，移开后按剩余时间继续。
    public static bool RunDeleteUndoInPlaceBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-undo-inplace-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        var rule = new AbsoluteOnceReminderRule("floating-undo-rule", nowUtc.AddMinutes(30));
        MustApply(workspace, new SaveNoteChange(
            null,
            "悬浮撤销便签甲",
            new DateTime(2038, 7, 8),
            false,
            Array.Empty<string>(),
            new ReminderRule[] { rule }));
        var noteA = workspace.Snapshot.Settings.Notes.Single();
        MustApply(workspace, new SaveNoteChange(
            null,
            "悬浮撤销便签乙",
            new DateTime(2038, 7, 9),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var noteB = workspace.Snapshot.Settings.Notes.First(item =>
            !string.Equals(item.Id, noteA.Id, StringComparison.OrdinalIgnoreCase));
        MustApply(workspace, new SaveCountdownChange(
            null,
            "管理撤销倒计时",
            nowUtc.AddDays(4),
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var countdown = workspace.Snapshot.Settings.Countdowns.Single();
        clock.UtcNow = rule.ScheduledUtc;
        Must(
            workspace.ConfirmReminderDisplayed(
                ReminderSourceKind.Note,
                noteA.Id,
                rule.Id,
                rule.ScheduledUtc).Succeeded,
            "无法预置便签提醒已送达状态。");
        var stateBefore = workspace.Snapshot.Settings.ReminderStates[rule.Id];

        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var preexistingWindows = Application.Current.Windows.Cast<Window>().ToHashSet();
        var lifecycle = new ManagerWindowLifecycle(
            () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
            PrepareOffscreenValidationWindow);
        var floating = new FloatingWindow(
            workspace,
            calendar,
            lifecycle,
            AppContext.BaseDirectory);
        MainWindow? main = null;
        try
        {
            PrepareOffscreenValidationWindow(floating);
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                lifecycle.Window is null &&
                CurrentCreatedMainWindows(preexistingWindows).Length == 0 &&
                floating.FloatingDeleteUndoLine.Visibility == Visibility.Collapsed,
                "探针开始时管理窗口必须尚未创建，悬浮撤销行必须收起。");

            string? cancelBody = null;
            var cancelIsSafeDefault = false;
            DriveFloatingNoteDelete(floating, noteA, confirm: false, inspect: dialog =>
            {
                cancelBody = dialog.BodyText.Text;
                cancelIsSafeDefault =
                    dialog.CancelButton.IsDefault &&
                    dialog.CancelButton.IsCancel &&
                    !dialog.ConfirmButton.IsDefault;
            });
            Must(
                cancelBody is not null &&
                cancelBody.Contains("删除后 10 秒内可撤销", StringComparison.Ordinal),
                "删除确认框必须写明 10 秒内可撤销。实际正文：" + (cancelBody ?? "<null>"));
            Must(
                cancelIsSafeDefault,
                "删除确认框取消必须仍是安全默认。");
            Must(
                workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id) &&
                floating.DeleteUndoForValidation is null &&
                floating.FloatingDeleteUndoLine.Visibility == Visibility.Collapsed &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Collapsed &&
                lifecycle.Window is null,
                "取消删除必须保留便签，且不出现撤销入口、不创建管理窗口。");

            DriveFloatingNoteDelete(floating, noteA, confirm: true);
            Must(
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id) &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(rule.Id) &&
                floating.DeleteUndoForValidation is not null &&
                floating.FloatingDeleteUndoLine.Visibility == Visibility.Visible &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible &&
                string.Equals(
                    floating.FloatingDeleteUndoButton.Content?.ToString(),
                    "撤销",
                    StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetName(floating.FloatingDeleteUndoButton),
                    $"撤销删除便签「{noteA.Text}」",
                    StringComparison.Ordinal) &&
                string.Equals(
                    floating.FloatingDeleteUndoText.Text,
                    $"已删除便签「{noteA.Text}」。",
                    StringComparison.Ordinal) &&
                lifecycle.Window is null,
                "管理窗口从未创建时，悬浮窗删除后底部必须出现撤销，且提醒状态随删除清理。");

            floating.FloatingDeleteUndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            var restoredA = workspace.Snapshot.Settings.Notes.FirstOrDefault(item => item.Id == noteA.Id);
            Must(
                restoredA is not null &&
                string.Equals(restoredA.Text, noteA.Text, StringComparison.Ordinal) &&
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(rule.Id, out var restoredState) &&
                restoredState == stateBefore &&
                floating.DeleteUndoForValidation is null &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Collapsed,
                "点击悬浮窗撤销必须原样恢复便签及其规则提醒状态。");

            DriveFloatingNoteDelete(floating, noteA, confirm: true);
            Must(
                WindowValidationInput.RaisePreviewKey(floating, Key.Z, ModifierKeys.Control),
                "撤销期间 Ctrl+Z 必须被悬浮窗处理。");
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id) &&
                workspace.Snapshot.Settings.ReminderStates.ContainsKey(rule.Id) &&
                floating.DeleteUndoForValidation is null,
                "悬浮窗 Ctrl+Z 必须撤销刚刚的删除。");

            // 撤销计时只读工作区的注入时钟；validate-only 没有 App 节拍，推进时钟后显式检查到期。
            void AdvanceUndoClock(int seconds)
            {
                clock.UtcNow = clock.UtcNow.AddSeconds(seconds);
                workspace.ExpireUndoIfDue();
            }

            DriveFloatingNoteDelete(floating, noteA, confirm: true);
            AdvanceUndoClock(7);
            Must(
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible,
                "未到 10 秒时撤销入口必须仍在。");
            RaiseMouseTransition(floating.FloatingDeleteUndoButton, Mouse.MouseEnterEvent);
            AdvanceUndoClock(10);
            Must(
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id),
                "鼠标停在撤销按钮上时不得自动收起。");
            RaiseMouseTransition(floating.FloatingDeleteUndoButton, Mouse.MouseLeaveEvent);
            AdvanceUndoClock(2);
            Must(
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible,
                "移开鼠标后必须按剩余时间继续，而不是重新算满 10 秒。");
            AdvanceUndoClock(2);
            Must(
                floating.DeleteUndoForValidation is null &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Collapsed &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteA.Id),
                "悬停移开后计时必须继续，到点收起且不恢复便签。");

            DriveFloatingNoteDelete(floating, noteB, confirm: true);
            Must(
                floating.DeleteUndoForValidation is not null &&
                lifecycle.Window is null,
                "第二张便签的撤销仍必须留在悬浮窗，且管理窗口仍未创建。");
            RaiseMouseTransition(floating.FloatingDeleteUndoButton, Mouse.MouseEnterEvent);

            var openManager = (Button)floating.FindName("OpenManagerButton");
            openManager.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            main = lifecycle.Window;
            Must(
                main is not null &&
                CurrentCreatedMainWindows(preexistingWindows).Length == 1 &&
                floating.DeleteUndoForValidation is not null &&
                string.Equals(
                    floating.DeleteUndoForValidation.Description,
                    noteB.Text,
                    StringComparison.Ordinal) &&
                floating.FloatingDeleteUndoButton.Visibility == Visibility.Visible,
                "打开管理窗口不得清掉悬浮窗上尚未撤销的删除。");
            main!.ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(main.Dispatcher);
            string? mainBody = null;
            QueueConfirmationDialogResponse(
                main,
                confirm: true,
                inspect: dialog => mainBody = dialog.BodyText.Text);
            Button MainDeleteButton(string id)
            {
                var button = VisualDescendants<Button>(main).FirstOrDefault(candidate =>
                    string.Equals(candidate.Content?.ToString(), "删除", StringComparison.Ordinal) &&
                    string.Equals(candidate.Tag as string, id, StringComparison.OrdinalIgnoreCase));
                Must(button is not null, $"无法定位记录 {id} 的行内删除按钮。");
                return button!;
            }

            MainDeleteButton(countdown.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                mainBody is not null &&
                mainBody.Contains("删除后 10 秒内可撤销", StringComparison.Ordinal) &&
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdown.Id) &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteB.Id) &&
                floating.FloatingDeleteUndoLine.Visibility == Visibility.Collapsed &&
                floating.DeleteUndoForValidation is null &&
                main.DeleteUndoForValidation is not null &&
                main.MainStatusUndoButton.Visibility == Visibility.Visible &&
                string.Equals(
                    AutomationProperties.GetName(main.MainStatusUndoButton),
                    $"撤销删除倒计时「{countdown.Name}」",
                    StringComparison.Ordinal) &&
                string.Equals(
                    main.StatusText.Text,
                    $"已删除倒计时「{countdown.Name}」。",
                    StringComparison.Ordinal),
                "管理窗口删除必须改由状态条提供撤销，并替换悬浮窗上一次未完成的撤销。");

            Must(
                WindowValidationInput.RaisePreviewKey(main, Key.Z, ModifierKeys.Control),
                "管理窗口撤销期间 Ctrl+Z 必须生效。");
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdown.Id) &&
                !workspace.Snapshot.Settings.Notes.Any(item => item.Id == noteB.Id) &&
                main.DeleteUndoForValidation is null &&
                main.MainStatusUndoButton.Visibility == Visibility.Collapsed,
                "管理窗口 Ctrl+Z 只能恢复最近一次删除。");

            QueueConfirmationDialogResponse(main, confirm: true);
            MainDeleteButton(countdown.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            main.Activate();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.MainStatusUndoButton.Focus(),
                "撤销按钮必须能获得键盘焦点。");
            AdvanceUndoClock(15);
            Must(
                main.MainStatusUndoButton.Visibility == Visibility.Visible &&
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdown.Id),
                "焦点停在撤销按钮上时不得自动收起。");
            Must(main.StatusText.Focus(), "焦点必须能离开撤销按钮。");
            AdvanceUndoClock(12);
            Must(
                main.DeleteUndoForValidation is null &&
                main.MainStatusUndoButton.Visibility == Visibility.Collapsed &&
                !workspace.Snapshot.Settings.Countdowns.Any(item => item.Id == countdown.Id),
                "焦点移开后计时必须继续并到点收起。");
            return true;
        }
        finally
        {
            main?.Close();
            floating.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }

        static void DriveFloatingNoteDelete(
            FloatingWindow floating,
            NoteRecord note,
            bool confirm,
            Action<ConfirmationDialog>? inspect = null)
        {
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);
            floating.UpdateLayout();
            var trigger = VisualDescendants<Button>(floating).FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetName(candidate),
                    $"更多操作：编辑或删除便签 {note.Text}",
                    StringComparison.Ordinal));
            Must(trigger is not null, $"无法定位便签「{note.Text}」的更多操作。");
            var popup = NoteRowOverflowMenuBehavior.GetPopup(trigger!);
            Must(popup is not null, "便签更多操作没有弹出层。");
            if (!popup!.IsOpen)
            {
                trigger!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WindowValidationInput.Pump(floating.Dispatcher);
                floating.UpdateLayout();
            }

            Must(popup.IsOpen, "便签更多操作没有打开。");
            QueueConfirmationDialogResponse(floating, confirm, inspect);
            var menuDelete = VisualDescendants<Button>(popup.Child).FirstOrDefault(candidate =>
                string.Equals(candidate.Content as string, "删除", StringComparison.Ordinal));
            Must(menuDelete is not null, "无法定位悬浮便签删除按钮。");
            menuDelete!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
        }

        static void RaiseMouseTransition(UIElement element, RoutedEvent routedEvent)
        {
            element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = routedEvent,
                Source = element
            });
        }
    }

    // #155 / #213：脏状态描述沿用既有语义；退出确认框以取消为安全默认，
    // 确认文案为「退出」，正文包含「退出后不再提醒」并列出未保存项。
    public static bool RunDirtyExitConfirmationBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-dirty-exit-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var nowUtc = new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
        var clock = new AdvancingValidationClock(nowUtc);
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        MustApply(workspace, new SaveNoteChange(
            null,
            "退出前草稿探针便签",
            new DateTime(2038, 7, 8),
            false,
            Array.Empty<string>(),
            Array.Empty<ReminderRule>()));
        var note = workspace.Snapshot.Settings.Notes.Single();

        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };
        try
        {
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                main.DescribeUnsavedExitWork() is null,
                "干净状态下不应产生退出确认描述。");

            var noteText = (TextBox)main.FindName("NoteText");
            noteText.Text = "未保存的便签草稿";
            Must(
                string.Equals(
                    main.DescribeUnsavedExitWork(),
                    "便签编辑器有未保存的草稿。",
                    StringComparison.Ordinal),
                "便签草稿未保存时退出描述缺失。");
            noteText.Clear();
            Must(main.DescribeUnsavedExitWork() is null, "清空便签草稿后退出描述应消失。");

            // 便签行只在便签工作区渲染（RenderVisibleWorkspace），先切换工作区再驱动便签行。
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var editButton = VisualDescendants<Button>(main).FirstOrDefault(button =>
                string.Equals(button.Content?.ToString(), "编辑", StringComparison.Ordinal) &&
                string.Equals(button.Tag as string, note.Id, StringComparison.OrdinalIgnoreCase));
            Must(editButton is not null, "无法定位便签探针行的编辑按钮。");
            editButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                string.Equals(
                    main.DescribeUnsavedExitWork(),
                    "便签编辑器有未保存的草稿。",
                    StringComparison.Ordinal),
                "便签编辑器编辑态的退出描述缺失。");
            var cancelNoteEdit = (Button)main.FindName("CancelNoteEdit");
            cancelNoteEdit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(main.DescribeUnsavedExitWork() is null, "取消便签编辑后退出描述应消失。");

            var monthlySalary = (TextBox)main.FindName("MonthlySalary");
            monthlySalary.Text = "not-a-number";
            Must(
                string.Equals(
                    main.DescribeUnsavedExitWork(),
                    "显示设置有未保存的更改。",
                    StringComparison.Ordinal),
                "显示设置脏时退出描述缺失。");
            monthlySalary.Text = workspace.Snapshot.Settings.Preferences.MonthlySalary
                .ToString("N2", CultureInfo.CurrentCulture);
            Must(main.DescribeUnsavedExitWork() is null, "显示设置恢复后退出描述应消失。");

            monthlySalary.Text = "not-a-number";
            noteText.Text = "未保存的便签草稿";
            var combined = main.DescribeUnsavedExitWork();
            Must(
                combined is not null &&
                combined.Contains("显示设置", StringComparison.Ordinal) &&
                combined.Contains("便签编辑器", StringComparison.Ordinal),
                "多来源未保存内容的退出描述不完整。");

            var dialogInspected = false;
            QueueConfirmationDialogResponse(
                main,
                confirm: false,
                inspect: dialog =>
                {
                    dialogInspected =
                        dialog.CancelButton.IsDefault &&
                        dialog.CancelButton.IsCancel &&
                        !dialog.ConfirmButton.IsDefault &&
                        string.Equals(
                            dialog.ConfirmButton.Content?.ToString(),
                            "退出",
                            StringComparison.Ordinal) &&
                        dialog.BodyText.Text.Contains("显示设置", StringComparison.Ordinal) &&
                        dialog.BodyText.Text.Contains("便签编辑器", StringComparison.Ordinal) &&
                        dialog.BodyText.Text.Contains("退出后不再提醒", StringComparison.Ordinal);
                });
            var confirmed = ProcessExitConfirmation.Confirm(
                main,
                new[] { main.DescribeUnsavedExitWork()! });
            WindowValidationInput.Pump(main.Dispatcher);
            Must(
                dialogInspected &&
                !confirmed,
                "退出确认框必须以取消为安全默认、携带完整描述且取消时不退出。");

            QueueConfirmationDialogResponse(main, confirm: true);
            confirmed = ProcessExitConfirmation.Confirm(
                main,
                new[] { main.DescribeUnsavedExitWork()! });
            WindowValidationInput.Pump(main.Dispatcher);
            Must(confirmed, "退出确认框确认后必须放行退出。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #213：没有未保存内容时，关闭悬浮窗和系统关闭（Alt+F4 走 Closing）都弹出退出确认，
    // 正文包含「退出后不再提醒」。取消后悬浮窗仍可见，提醒派发未被释放。
    public static bool RunAlwaysConfirmExitKeepsReminderBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-exit-confirm-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(main);
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(floating);
        var coordinator = new ReminderDeliveryCoordinator(workspace, logger);
        var shuttingDown = false;
        void RequestShutdown()
        {
            if (shuttingDown)
            {
                return;
            }

            var reasons = new List<string>();
            if (main.DescribeUnsavedExitWork() is { } mainReason)
            {
                reasons.Add(mainReason);
            }

            if (floating.DescribeUnsavedExitWork() is { } floatingReason)
            {
                reasons.Add(floatingReason);
            }

            if (!ProcessExitConfirmation.Confirm(floating, reasons))
            {
                return;
            }

            shuttingDown = true;
            coordinator.Dispose();
            floating.Close();
        }

        FloatingWindowCloseBridge.Attach(floating, RequestShutdown, () => shuttingDown);
        floating.ExitRequested += (_, _) => RequestShutdown();
        try
        {
            main.Show();
            floating.Show();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                main.DescribeUnsavedExitWork() is null &&
                floating.DescribeUnsavedExitWork() is null,
                "干净退出探针开始时不应有未保存内容。");

            var dialogInspected = false;
            QueueConfirmationDialogResponse(
                floating,
                confirm: false,
                inspect: dialog =>
                {
                    dialogInspected =
                        dialog.CancelButton.IsDefault &&
                        dialog.CancelButton.IsCancel &&
                        dialog.BodyText.Text.Contains("退出后不再提醒", StringComparison.Ordinal) &&
                        !dialog.BodyText.Text.Contains("未保存", StringComparison.Ordinal);
                });
            var confirmed = ProcessExitConfirmation.Confirm(floating, Array.Empty<string>());
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(dialogInspected && !confirmed, "干净状态也必须弹出含「退出后不再提醒」的退出确认，取消不放行。");
            Must(floating.IsVisible && coordinator.IsDispatching, "直接取消退出后悬浮窗和提醒派发必须仍在。");
            coordinator.Tick(workspace.Snapshot.UtcNow);

            dialogInspected = false;
            QueueConfirmationDialogResponse(
                floating,
                confirm: false,
                inspect: dialog =>
                {
                    dialogInspected = dialog.BodyText.Text.Contains("退出后不再提醒", StringComparison.Ordinal);
                });
            var closeButton = floating.FindName("CloseFloatingButton") as Button
                ?? throw new InvalidOperationException("无法定位悬浮窗关闭按钮。");
            closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                dialogInspected && floating.IsVisible && coordinator.IsDispatching,
                "关闭悬浮窗取消后，悬浮窗和提醒派发必须仍在运行。");
            coordinator.Tick(workspace.Snapshot.UtcNow);

            dialogInspected = false;
            QueueConfirmationDialogResponse(
                floating,
                confirm: false,
                inspect: dialog =>
                {
                    dialogInspected = dialog.BodyText.Text.Contains("退出后不再提醒", StringComparison.Ordinal);
                });
            floating.Close();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                dialogInspected && floating.IsVisible && coordinator.IsDispatching,
                "Alt+F4 取消后必须留在应用里，不能留下没有窗口的进程，提醒派发必须仍在。");
            coordinator.Tick(workspace.Snapshot.UtcNow);

            QueueConfirmationDialogResponse(floating, confirm: true);
            floating.Close();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                !floating.IsVisible && !coordinator.IsDispatching,
                "确认退出后必须关掉悬浮窗并停止提醒派发。");
            return true;
        }
        finally
        {
            if (coordinator.IsDispatching)
            {
                coordinator.Dispose();
            }

            if (main.IsLoaded)
            {
                main.Close();
            }

            if (floating.IsLoaded)
            {
                floating.Close();
            }

            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }
}
