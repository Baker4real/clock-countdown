using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    public static bool RunNoteSelectionAccessibility()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-note-selection-{Guid.NewGuid():N}.json");
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(path, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var announcements = new List<string>();
        void RecordSelection(TextBlock block)
        {
            if (block.Name == "SelectedNotesCount") announcements.Add(AutomationProperties.GetName(block));
        }
        UiAccessibility.Announced += RecordSelection;
        try
        {
            for (var index = 0; index < 6; index++)
                MustApply(workspace, new SaveNoteChange(null, $"选择便签 {index}", new DateTime(2038, 7, 8).AddDays(index / 2), false, [], Array.Empty<ReminderRule>()));
            PrepareOffscreenValidationWindow(main); main.Show(); main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var rows = VisualDescendants<Border>(main).Where(row => row.Uid == "SelectableNoteRow" && row.IsVisible).ToArray();
            var count = (TextBlock)main.FindName("SelectedNotesCount");
            Keyboard.Focus(rows[0]); WindowValidationInput.RaisePreviewKey(rows[0], Key.Space, ModifierKeys.Control);
            WindowValidationInput.RaisePreviewKey(rows[0], Key.Down, ModifierKeys.Shift);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(ReferenceEquals(Keyboard.FocusedElement, rows[1]) &&
                ReferenceEquals(rows[1].FocusVisualStyle, main.FindResource("MainKeyboardFocusVisual")),
                "#235：选择行方向键移动后必须保留真实键盘焦点和现役可见焦点样式。");
            Must(count.Text == "已选择 2 条" && UIElementAutomationPeer.CreatePeerForElement(rows[1]) is { } peer &&
                peer.GetName().Contains("已选择", StringComparison.Ordinal) &&
                peer.GetItemStatus() == "已选择" && announcements.Contains("已选择 2 条") &&
                UIElementAutomationPeer.CreatePeerForElement(count)?.GetLiveSetting() == AutomationLiveSetting.Polite,
                "#235：选择行与选中数量必须可被讲述人读取，数量改变发出真实直播区事件。");
            var focused = Keyboard.FocusedElement;
            clock.UtcNow = clock.UtcNow.AddMinutes(1); main.UpdateTime(clock.UtcNow);
            Must(ReferenceEquals(focused, Keyboard.FocusedElement) && count.Text == "已选择 2 条",
                "#235：分钟刷新不得丢失选择与焦点。");
            var selected = workspace.Snapshot.AllNotes.Single(note => note.Id == (string)rows[0].Tag);
            MustApply(workspace, new SaveNoteChange(selected.Id, selected.Text + "已更新", DateTime.Parse(selected.Date!), false, [], Array.Empty<ReminderRule>()));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(count.Text == "已选择 2 条", "#235：仍可见记录的数据刷新必须保留有效选择。");
            ((TextBox)main.FindName("NoteSearch")).Text = "选择便签 0";
            WindowValidationInput.Pump(main.Dispatcher);
            Must(count.Text == "已选择 0 条" && !((Panel)main.FindName("NoteBatchActions")).IsVisible,
                "#235：筛选隐藏的便签不能继续留在批量选择中。");
            ((TextBox)main.FindName("NoteSearch")).Text = string.Empty;
            WindowValidationInput.Pump(main.Dispatcher);
            rows = VisualDescendants<Border>(main).Where(row => row.Uid == "SelectableNoteRow" && row.IsVisible).ToArray();
            Keyboard.Focus(rows[0]); WindowValidationInput.RaisePreviewKey(rows[0], Key.Space, ModifierKeys.Control);
            var disclosure = VisualDescendants<Button>(main).First(button => button.Uid == "NoteDateDisclosure" && button.IsVisible);
            disclosure.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Must(count.Text == "已选择 0 条", "#235：折叠分组必须移除隐藏选择。");
            disclosure.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Keyboard.Focus(rows[0]); WindowValidationInput.RaisePreviewKey(rows[0], Key.Space, ModifierKeys.Control);
            ((ToggleButton)main.FindName("CompletedNotesSegment")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Must(count.Text == "已选择 0 条", "#235：切换完成状态分段必须移除隐藏选择。");
            var sourceNote = workspace.Snapshot.AllNotes[0];
            Must(main.OpenReminderSource(ReminderSourceKind.Note, sourceNote.Id),
                "#235：必须真实打开提醒来源便签以验证来源与选择名称。");
            WindowValidationInput.Pump(main.Dispatcher);
            var sourceRow = VisualDescendants<NoteSelectionCard>(main).Single(row =>
                row.IsVisible && (string)row.Tag == sourceNote.Id);
            var sourcePeer = UIElementAutomationPeer.CreatePeerForElement(sourceRow)!;
            var sourceName = $"当前提醒来源便签：{sourceNote.Text}；";
            Must(sourcePeer.GetName() == sourceName + "未选择",
                "#235：打开来源后的卡片名称必须同时包含提醒来源身份和未选择状态。");
            Keyboard.Focus(sourceRow);
            WindowValidationInput.RaisePreviewKey(sourceRow, Key.Space, ModifierKeys.Control);
            Must(sourcePeer.GetName() == $"已选择便签：{sourceNote.Text}" && sourcePeer.GetItemStatus() == "已选择",
                "#235：临时来源揭示结束后，键盘选中必须恢复普通名称并包含已选择状态。");
            WindowValidationInput.RaisePreviewKey(sourceRow, Key.Space, ModifierKeys.Control);
            Must(sourcePeer.GetName() == $"未选择便签：{sourceNote.Text}" && sourcePeer.GetItemStatus() == "未选择",
                "#235：临时来源揭示结束后，取消选择必须恢复普通名称并包含未选择状态。");
            return true;
        }
        finally { UiAccessibility.Announced -= RecordSelection; main.Close(); DeleteSettingsFiles(path); DeleteFileIfPresent(path + ".log"); }
    }

    public static bool RunNoteBatchDeleteUndo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-batch-delete-{Guid.NewGuid():N}.json");
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(path, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            for (var index = 0; index < 3; index++)
                MustApply(workspace, new SaveNoteChange(null, $"删除批量 {index}", new DateTime(2038, 7, 8), false, [],
                    new ReminderRule[] { new FixedIntervalReminderRule($"batch-delete-{index}", clock.UtcNow.AddMinutes(1), new ReminderInterval(15)) }));
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            foreach (var note in workspace.Snapshot.AllNotes)
            {
                Must(workspace.ConfirmReminderDisplayed(note, note.ReminderRules[0].Id, clock.UtcNow).Succeeded, "批量删除送达夹具失败。");
                Must(workspace.SnoozeReminder(note, note.ReminderRules[0].Id, clock.UtcNow,
                    ReminderSnoozeOption.TenMinutes, null).Succeeded, "批量删除稍后夹具失败。");
            }
            PrepareOffscreenValidationWindow(main); main.Show(); main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var delete = main.FindName("DeleteSelectedNotesButton") as Button;
            Must(delete is not null, "#235：缺少批量删除入口。");
            var rows = VisualDescendants<Border>(main).Where(row => row.Uid == "SelectableNoteRow" && row.IsVisible).ToArray();
            Keyboard.Focus(rows[0]); WindowValidationInput.RaisePreviewKey(rows[0], Key.Space, ModifierKeys.Control);
            Keyboard.Focus(rows[2]); WindowValidationInput.RaisePreviewKey(rows[2], Key.Space, ModifierKeys.Control);
            var ids = new[] { (string)rows[0].Tag, (string)rows[2].Tag };
            var original = new SettingsStore(clock: clock).Load(path).Settings;
            var bytes = File.ReadAllText(path);
            QueueConfirmationDialogResponse(main, confirm: false);
            Keyboard.Focus(delete); WindowValidationInput.RaiseKey(delete!, Key.Enter);
            Must(File.ReadAllText(path) == bytes && ((TextBlock)main.FindName("SelectedNotesCount")).Text == "已选择 2 条",
                "#235：安全默认取消必须保留全部记录和选择。");
            Directory.CreateDirectory(path + ".tmp");
            QueueConfirmationDialogResponse(main, confirm: true);
            delete!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Must(File.ReadAllText(path) == bytes && workspace.Snapshot.AllNotes.Count == 3 && !main.MainStatusUndoButton.IsVisible,
                "#235：批量删除写盘失败必须整批回滚且没有新撤销。");
            DeleteDirectoryIfEmpty(path + ".tmp");
            QueueConfirmationDialogResponse(main, confirm: true);
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var disk = new SettingsStore(clock: clock).Load(path).Settings;
            Must(disk.Notes.Count == 1 && disk.Notes.All(note => !ids.Contains(note.Id)) && disk.ReminderStates.Count == 1 &&
                main.MainStatusUndoButton.IsVisible, "#235：确认一次必须删除全部所选和规则状态，提供一次撤销。");
            Directory.CreateDirectory(path + ".tmp");
            var deletedBytes = File.ReadAllText(path);
            main.MainStatusUndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Must(File.ReadAllText(path) == deletedBytes && main.MainStatusUndoButton.IsVisible,
                "#235：撤销写盘失败必须整批不恢复并保留载荷。");
            DeleteDirectoryIfEmpty(path + ".tmp");
            var collision = original.Notes.First(note => ids.Contains(note.Id));
            MustApply(workspace, new RestoreDeletedNoteChange(collision,
                DeletedRecordUndo.CaptureReminderStates(original.ReminderStates, collision.ReminderRules)));
            var conflictBytes = File.ReadAllText(path);
            main.MainStatusUndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Must(File.ReadAllText(path) == conflictBytes && main.MainStatusUndoButton.IsVisible,
                "#235：一条同ID冲突必须拒绝整批撤销，不覆盖现有记录。");
            MustApply(workspace, new DeleteNoteChange(collision.Id));
            main.MainStatusUndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            disk = new SettingsStore(clock: clock).Load(path).Settings;
            Must(JsonSerializer.Serialize(disk.Notes) == JsonSerializer.Serialize(original.Notes) &&
                original.ReminderStates.All(pair => disk.ReminderStates.TryGetValue(pair.Key, out var state) && state == pair.Value) &&
                !main.MainStatusUndoButton.IsVisible,
                "#235：一次撤销必须恢复全部记录、原顺序和每条规则的cursor/送达/处置/活动稍后。");
            return true;
        }
        finally
        {
            main.Close(); DeleteDirectoryIfEmpty(path + ".tmp"); DeleteSettingsFiles(path); DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunNoteBatchKeyboardCompletion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-batch-notes-{Guid.NewGuid():N}.json");
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(path, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        using var coordinator = new ReminderDeliveryCoordinator(workspace, new DiagnosticLogger(path + ".log"));
        try
        {
            for (var index = 0; index < 3; index++)
                MustApply(workspace, new SaveNoteChange(null, $"批量便签 {index}", new DateTime(2038, 7, 8), false, [],
                    new ReminderRule[] { new FixedIntervalReminderRule($"batch-rule-{index}", clock.UtcNow.AddMinutes(1), new ReminderInterval(15)) }));
            var firstDue = clock.UtcNow.AddMinutes(1);
            clock.UtcNow = firstDue;
            coordinator.Tick(clock.UtcNow); WindowValidationInput.Pump(main.Dispatcher);
            Must(coordinator.ActiveEntryCount == 3, "#235：批量完成探针必须先真实送达三个提醒中心条目。");
            PrepareOffscreenValidationWindow(main);
            main.Show(); main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var complete = main.FindName("CompleteSelectedNotesButton") as Button;
            Must(complete is not null, "#235：缺少主窗口批量完成入口。");
            var rows = VisualDescendants<Border>(main).Where(row => row.Uid == "SelectableNoteRow" && row.IsVisible).ToArray();
            Must(rows.Length == 3, "#235：便签必须提供键盘可达的选择行。");
            Keyboard.Focus(rows[0]);
            WindowValidationInput.RaisePreviewKey(rows[0], Key.Space, ModifierKeys.Control);
            WindowValidationInput.RaisePreviewKey(rows[0], Key.Down, ModifierKeys.Shift);
            WindowValidationInput.Pump(main.Dispatcher);
            var count = (TextBlock)main.FindName("SelectedNotesCount");
            Must(count.Text == "已选择 2 条" && count.IsVisible &&
                AutomationProperties.GetLiveSetting(count) == AutomationLiveSetting.Polite,
                "#235：Ctrl+Space、Shift+Down 必须选中两条并提供可访问数量。");
            var ids = rows.Take(2).Select(row => (string)row.Tag).ToHashSet();
            clock.UtcNow = firstDue.AddMinutes(37);
            var before = File.ReadAllText(path);
            Directory.CreateDirectory(path + ".tmp");
            complete!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Must(File.ReadAllText(path) == before && workspace.Snapshot.AllNotes.All(note => !note.IsCompleted) &&
                count.Text == "已选择 2 条" && !main.MainStatusUndoButton.IsVisible && coordinator.ActiveEntryCount == 3,
                "#235：批量完成写盘失败必须整批不变并保留选择。");
            DeleteDirectoryIfEmpty(path + ".tmp");
            Keyboard.Focus(complete);
            WindowValidationInput.RaiseKey(complete, Key.Enter);
            WindowValidationInput.Pump(main.Dispatcher);
            var disk = new SettingsStore(clock: clock).Load(path).Settings;
            Must(disk.Notes.Where(note => ids.Contains(note.Id)).All(note => note.IsCompleted) &&
                 disk.Notes.Where(note => !ids.Contains(note.Id)).All(note => !note.IsCompleted) &&
                 disk.ReminderStates.Values.Count(state => state.HandledThroughUtc == clock.UtcNow.AddMinutes(-7)) == 2 &&
                 coordinator.ActiveEntryCount == 1 && disk.Notes.Where(note => ids.Contains(note.Id)).All(note =>
                    disk.ReminderStates[note.ReminderRules[0].Id].LastDispositionOccurrenceKey ==
                    disk.ReminderStates[note.ReminderRules[0].Id].LastDeliveredOccurrenceKey),
                "#235：批量完成须逐条推进cursor，未选项不变。");
            Must(main.MainStatusUndoButton.IsVisible, "#235：批量完成应保留唯一撤销入口。");
            clock.UtcNow = clock.UtcNow.AddMinutes(15);
            main.MainStatusUndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            disk = new SettingsStore(clock: clock).Load(path).Settings;
            Must(disk.Notes.All(note => !note.IsCompleted) && disk.Notes.Where(note => ids.Contains(note.Id)).All(note =>
                    disk.ReminderStates[note.ReminderRules[0].Id].HandledThroughUtc == firstDue.AddMinutes(45)),
                "#235：一次撤销应重新打开全部本次完成的便签。");
            return true;
        }
        finally
        {
            main.Close(); DeleteDirectoryIfEmpty(path + ".tmp");
            DeleteSettingsFiles(path); DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool ProbeMotionTokens()
    {
        var resources = Application.Current?.Resources;
        if (resources is null ||
            !resources.Contains("MotionHoverDuration") ||
            !resources.Contains("MotionPressDuration") ||
            !resources.Contains("MotionPopupEnterDuration") ||
            !resources.Contains("MotionPopupExitDuration") ||
            !resources.Contains("MotionIndicatorDuration") ||
            !resources.Contains("MotionCountdownDuration") ||
            !resources.Contains("MotionEaseOut"))
        {
            return false;
        }

        var animationEnabled = SystemParameters.ClientAreaAnimation;
        var expectedHover = TimeSpan.FromMilliseconds(animationEnabled ? 120 : 0);
        var expectedPress = TimeSpan.FromMilliseconds(animationEnabled ? 100 : 0);
        var expectedPopupEnter = TimeSpan.FromMilliseconds(animationEnabled ? 140 : 0);
        var expectedPopupExit = TimeSpan.FromMilliseconds(animationEnabled ? 90 : 0);
        var expectedIndicator = TimeSpan.FromMilliseconds(animationEnabled ? 160 : 0);
        var expectedCountdown = TimeSpan.FromMilliseconds(animationEnabled ? 90 : 0);

        return resources["MotionHoverDuration"] is Duration hover &&
               resources["MotionPressDuration"] is Duration press &&
               resources["MotionPopupEnterDuration"] is Duration popupEnter &&
               resources["MotionPopupExitDuration"] is Duration popupExit &&
               resources["MotionIndicatorDuration"] is Duration indicator &&
               resources["MotionCountdownDuration"] is Duration countdown &&
               hover.TimeSpan == expectedHover &&
               press.TimeSpan == expectedPress &&
               popupEnter.TimeSpan == expectedPopupEnter &&
               popupExit.TimeSpan == expectedPopupExit &&
               indicator.TimeSpan == expectedIndicator &&
               countdown.TimeSpan == expectedCountdown &&
               resources["MotionEaseOut"] is EasingFunctionBase easing &&
               easing.EasingMode == EasingMode.EaseOut;
    }

    public static bool RunValidateOnlySettingsIsolationBehavior()
    {
        var applicationRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-validation-isolation-{Guid.NewGuid():N}");
        var rootSettingsPath = Path.Combine(applicationRoot, "settings.json");
        var isolatedSettingsPath = Path.Combine(applicationRoot, "isolated", "settings.json");

        var missingSettingsRejected = ThrowsArgumentException(
            () => HostOptions.Parse(new[] { "--validate-only" }));
        var rootSettingsRejected = ThrowsArgumentException(() =>
            ApplicationValidation.ResolveSettingsPath(
                HostOptions.Parse(new[]
                {
                    "--validate-only",
                    "--settings-file",
                    rootSettingsPath
                }),
                applicationRoot));
        var isolatedSettingsAccepted = string.Equals(
            isolatedSettingsPath,
            ApplicationValidation.ResolveSettingsPath(
                HostOptions.Parse(new[]
                {
                    "--validate-only",
                    "--settings-file",
                    isolatedSettingsPath
                }),
                applicationRoot),
            StringComparison.OrdinalIgnoreCase);
        var productionDefaultPreserved =
            HostOptions.Parse(Array.Empty<string>()).SettingsFile is null;

        return missingSettingsRejected &&
               rootSettingsRejected &&
               isolatedSettingsAccepted &&
               productionDefaultPreserved;
    }

    public static bool RunLazyManagerLifecycleBehavior()
    {
        AssertLazyManagerFailureRecovery();
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-lazy-manager-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var preexistingWindows = Application.Current.Windows.Cast<Window>().ToHashSet();
        HolidayCalendar? calendar = null;
        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath);
            MustApply(workspace, new SaveNoteChange(
                null,
                "懒加载来源便签",
                new DateTime(2031, 4, 5),
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            var note = workspace.Snapshot.Settings.Notes.Single();
            calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            var lifecycle = new ManagerWindowLifecycle(
                () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
                PrepareOffscreenValidationWindow);
            var floating = new FloatingWindow(
                workspace,
                calendar,
                lifecycle,
                AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(floating);
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);

            Must(
                Application.Current.Windows.OfType<MainWindow>().All(preexistingWindows.Contains) &&
                lifecycle.Window is null,
                "普通启动后 Windows 集合不应包含管理窗口。");

            var openManager = floating.FindName("OpenManagerButton") as Button ??
                              throw new InvalidOperationException("悬浮窗缺少 OpenManagerButton。");
            openManager.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            var created = CurrentCreatedMainWindows(preexistingWindows);
            Must(
                created.Length == 1 &&
                ReferenceEquals(created[0], lifecycle.Window) &&
                created[0].IsVisible &&
                created[0].WindowState == WindowState.Normal &&
                created[0].CurrentWorkspace == floating.CurrentWorkspaceForValidation,
                "第一次管理没有创建唯一可见管理窗口或未沿用当前工作区。");

            var first = created[0];
            openManager.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                CurrentCreatedMainWindows(preexistingWindows).Length == 1 &&
                ReferenceEquals(first, lifecycle.Window),
                "重复打开管理产生了第二个管理窗口。");

            var draft = (TextBox)first.FindName("CountdownName");
            draft.Text = "应保留的倒计时草稿";
            first.Hide();
            WindowValidationInput.Pump(first.Dispatcher);
            Must(
                lifecycle.OpenReminderSource(ReminderSourceKind.Note, note.Id),
                "打开来源失败。");
            WindowValidationInput.Pump(first.Dispatcher);
            Must(
                ReferenceEquals(first, lifecycle.Window) &&
                first.IsVisible &&
                first.WindowState == WindowState.Normal &&
                first.CurrentWorkspace == WorkspaceKind.Notes &&
                string.Equals(draft.Text, "应保留的倒计时草稿", StringComparison.Ordinal) &&
                Keyboard.FocusedElement is Button { Tag: string focusedId } &&
                string.Equals(focusedId, note.Id, StringComparison.OrdinalIgnoreCase),
                "打开来源没有揭示准确来源或覆盖了草稿。");
            return true;
        }
        finally
        {
            foreach (var window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                if (!preexistingWindows.Contains(window))
                {
                    window.Close();
                }
            }

            calendar?.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #231：管理窗口装饰 PNG 按需、按显示尺寸解码——首次显示只解码当前页装饰图，
    // 切换到另一页时才解码那一张；空态吉祥物按 92x64 显示尺寸解码。
    public static bool RunMainDecorationOnDemandDecodeBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-decoration-decode-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var preexistingWindows = Application.Current.Windows.Cast<Window>().ToHashSet();
        HolidayCalendar? calendar = null;
        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath);
            calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            var window = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
            WindowValidationInput.Pump(window.Dispatcher);

            var countdownDecoration = (Image)window.FindName("CountdownReferenceDecoration");
            var notesDecoration = (Image)window.FindName("NotesReferenceDecoration");
            var countdownMascot = (Image)window.FindName("CountdownEmptyMascot");
            var notesMascot = (Image)window.FindName("NotesEmptyMascot");

            var firstShowDecodesOnlyActivePage =
                window.CurrentWorkspace == WorkspaceKind.Countdowns &&
                countdownDecoration.Source is BitmapImage { PixelWidth: 1165, PixelHeight: 1350 } &&
                notesDecoration.Source is null;
            var mascotsDecodeAtDisplaySize =
                countdownMascot.Source is BitmapImage countdownMascotImage &&
                countdownMascotImage.PixelWidth <= 92 &&
                countdownMascotImage.PixelHeight <= 64 &&
                notesMascot.Source is BitmapImage notesMascotImage &&
                notesMascotImage.PixelWidth <= 92 &&
                notesMascotImage.PixelHeight <= 64;

            window.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(window.Dispatcher);
            var switchDecodesNotesPageOnDemand =
                notesDecoration.Source is BitmapImage { PixelWidth: 1165, PixelHeight: 1350 } &&
                notesDecoration.Visibility == Visibility.Visible;

            window.ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(window.Dispatcher);
            var decorationsStayCachedPerPage =
                countdownDecoration.Source is BitmapImage &&
                notesDecoration.Source is BitmapImage &&
                countdownDecoration.Visibility == Visibility.Visible &&
                notesDecoration.Visibility == Visibility.Collapsed;

            return firstShowDecodesOnlyActivePage &&
                   mascotsDecodeAtDisplaySize &&
                   switchDecodesNotesPageOnDemand &&
                   decorationsStayCachedPerPage;
        }
        finally
        {
            foreach (var window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                if (!preexistingWindows.Contains(window))
                {
                    window.Close();
                }
            }

            calendar?.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static void PrepareOffscreenValidationWindow(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20_000;
        window.Top = -20_000;
        window.Opacity = 0;
    }

    private static MainWindow[] CurrentCreatedMainWindows(HashSet<Window> preexistingWindows) =>
        Application.Current.Windows
            .OfType<MainWindow>()
            .Where(window => !preexistingWindows.Contains(window))
            .ToArray();

    private static void AssertLazyManagerFailureRecovery()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-lazy-manager-fail-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var preexistingWindows = Application.Current.Windows.Cast<Window>().ToHashSet();
        HolidayCalendar? calendar = null;
        try
        {
            var workspace = DesktopWorkspace.Load(settingsPath);
            MustApply(workspace, new SaveNoteChange(
                null,
                "失败回滚隔离便签",
                new DateTime(2031, 4, 5),
                false,
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            var settingsBytesBefore = File.ReadAllBytes(settingsPath);
            var baselineCount = Application.Current.Windows.OfType<MainWindow>().Count();
            MainWindow? hostField = null;
            var failAttach = true;
            var lifecycle = new ManagerWindowLifecycle(
                () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
                window =>
                {
                    if (failAttach)
                    {
                        hostField = window;
                        throw new InvalidOperationException("附加管理窗口失败。");
                    }

                    PrepareOffscreenValidationWindow(window);
                    hostField = window;
                });

            var originalThrown = false;
            try
            {
                lifecycle.EnsureCreated();
            }
            catch (InvalidOperationException exception) when (
                string.Equals(exception.Message, "附加管理窗口失败。", StringComparison.Ordinal))
            {
                originalThrown = true;
            }

            WindowValidationInput.Pump(Application.Current.Dispatcher);
            var createdAfterFailure = CurrentCreatedMainWindows(preexistingWindows);
            Must(originalThrown, "attach 失败没有抛出原异常。");
            Must(lifecycle.Window is null, "attach 失败后 lifecycle.Window 应为空。");
            Must(
                Application.Current.Windows.OfType<MainWindow>().Count() == baselineCount &&
                createdAfterFailure.Length == 0,
                "attach 失败后管理窗口计数没有回到基线。");
            Must(
                hostField is null ||
                !Application.Current.Windows.OfType<MainWindow>().Contains(hostField),
                "attach 先写 hostField 再失败后仍指向活孤儿。");
            Must(
                File.Exists(settingsPath) &&
                File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(settingsBytesBefore),
                "attach 失败改写了隔离 settings 文件。");

            failAttach = false;
            lifecycle.Reveal(WorkspaceKind.Notes);
            WindowValidationInput.Pump(Application.Current.Dispatcher);
            var recovered = CurrentCreatedMainWindows(preexistingWindows);
            Must(
                recovered.Length == 1 &&
                ReferenceEquals(recovered[0], lifecycle.Window) &&
                ReferenceEquals(hostField, lifecycle.Window) &&
                recovered[0].IsVisible &&
                recovered[0].WindowState == WindowState.Normal &&
                recovered[0].CurrentWorkspace == WorkspaceKind.Notes,
                "失败后重入没有恢复成恰好一扇可用管理窗口。");
        }
        finally
        {
            foreach (var window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                if (!preexistingWindows.Contains(window))
                {
                    window.Close();
                }
            }

            calendar?.Dispose();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static bool RunAclDeniedMainRecoveryWiringProbe()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-acl-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllBytes(settingsPath, Encoding.UTF8.GetBytes("{acl-denied-main"));
            File.WriteAllBytes(
                settingsPath + ".backup1",
                Encoding.UTF8.GetBytes(SettingsContract.SerializeCanonical(
                    CountdownSettings.Empty with
                    {
                        Preferences = CountdownSettings.Empty.Preferences with { MonthlySalary = 2468m }
                    })));
            var fileOperations = new AclDeniedMainSettingsFileOperations(settingsPath);
            var promptCount = 0;
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(fileOperations: fileOperations),
                _ =>
                {
                    promptCount++;
                    return SettingsRecoveryDecision.RecoverBackup;
                });

            return promptCount == 1 &&
                   workspace.Snapshot.Settings.Preferences.MonthlySalary == 2468m &&
                   fileOperations.DenialClearedByReplace;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static bool RunInstanceActivationFailurePromptBehavior()
    {
        var window = InstanceActivationFailureWindow.CreateForValidation();
        PrepareOffscreenValidationWindow(window);
        try
        {
            window.Show();
            WindowValidationInput.Pump(window.Dispatcher);
            var body = (window.Content as Border)?.Child as StackPanel;
            var texts = body?.Children.OfType<TextBlock>().ToArray() ?? Array.Empty<TextBlock>();
            Must(texts.Any(text => text.IsVisible &&
                text.Text.Contains("任务栏", StringComparison.Ordinal) &&
                text.Text.Contains("托盘", StringComparison.Ordinal) &&
                text.Text.Contains("关闭此提示后重试", StringComparison.Ordinal) &&
                text.Text.Contains("保存其他工作", StringComparison.Ordinal) &&
                text.Text.Contains("正常重启电脑", StringComparison.Ordinal)),
                "#228 激活失败提示缺少可执行的打开、重试和安全恢复步骤。");
            var commands = body?.Children
                .OfType<StackPanel>()
                .SingleOrDefault(panel => panel.Orientation == Orientation.Horizontal);
            var buttons = commands?.Children.OfType<Button>().ToArray() ?? Array.Empty<Button>();
            var dismiss = buttons.SingleOrDefault(button =>
                string.Equals(
                    AutomationProperties.GetName(button),
                    InstanceActivationFailureWindow.DismissButtonText,
                    StringComparison.Ordinal));

            return window.Title == InstanceActivationFailureWindow.PromptTitle &&
                   texts.Any(text =>
                       string.Equals(
                           text.Text,
                           InstanceActivationFailureWindow.PromptHeading,
                           StringComparison.Ordinal)) &&
                   texts.Any(text =>
                       string.Equals(
                           text.Text,
                           InstanceActivationFailureWindow.PromptMessage,
                           StringComparison.Ordinal)) &&
                   buttons.Length == 1 &&
                   dismiss is { IsDefault: true, IsCancel: true, IsEnabled: true };
        }
        finally
        {
            if (!window.Dispatcher.HasShutdownStarted)
            {
                window.Close();
            }
        }
    }

    public static bool RunInstanceActivationStateEqualityBehavior()
    {
        var filterA = DesktopWorkspaceFilter.Empty with
        {
            SelectedTagIds = new[] { "instance-activation-tag-a", "instance-activation-tag-b" }
        };
        var filterB = DesktopWorkspaceFilter.Empty with
        {
            SelectedTagIds = new[] { "INSTANCE-ACTIVATION-TAG-A", "INSTANCE-ACTIVATION-TAG-B" }
        };
        var stateA = new MainWindowActivationState(
            WorkspaceKind.Notes,
            filterA,
            "instance-activation-countdown-draft",
            "instance-activation-note-draft",
            "instance-activation-search",
            new DateTime(2031, 4, 5),
            null,
            null);
        var stateB = stateA with { Filter = filterB };

        var sameContentEquals = stateA == stateB;
        var tagChangeBreaks = stateA != stateA with
        {
            Filter = filterA with
            {
                SelectedTagIds = new[] { "instance-activation-tag-a", "instance-activation-tag-c" }
            }
        };
        var tagCountChangeBreaks = stateA != stateA with
        {
            Filter = filterA with { SelectedTagIds = new[] { "instance-activation-tag-a" } }
        };
        var draftChangeBreaks = stateA != stateA with { CountdownDraft = "changed-draft" };
        var searchChangeBreaks = stateA != stateA with { NoteSearch = "changed-search" };

        return sameContentEquals &&
               tagChangeBreaks &&
               tagCountChangeBreaks &&
               draftChangeBreaks &&
               searchChangeBreaks;
    }
}
