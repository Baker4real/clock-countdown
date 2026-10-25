using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    // #156：非拖动的窗口复位/贴边入口——越界悬浮窗经 Ctrl+R 或托盘菜单「复位窗口位置」
    // 都能被夹回工作区；隐藏态复位先恢复可见再夹回；完成任务的窗口收纳不依赖拖无边框窗。
    public static bool RunWindowPositionResetBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-window-position-reset-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var logger = new DiagnosticLogger(diagnosticPath);
        var lifecycle = new ManagerWindowLifecycle(
            () => new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory),
            PrepareOffscreenValidationWindow);
        var floating = new FloatingWindow(workspace, calendar, lifecycle, AppContext.BaseDirectory);
        PrepareOffscreenValidationWindow(floating);
        var trayController = new FloatingTrayController(
            floating,
            Path.Combine(AppContext.BaseDirectory, "countdown-icon.ico"),
            canTransition: static () => true,
            foregroundInterop: null,
            attachShellNotifyIcon: false);
        floating.Show();
        try
        {
            WindowValidationInput.Pump(floating.Dispatcher);
            var workArea = MonitorWorkAreaResolver.ResolveForWindow(floating).WorkAreaDip;

            floating.Left = workArea.Right + 120;
            floating.Top = workArea.Top + 40;
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                !WindowIntersectsWorkArea(floating),
                "前置：悬浮窗应完全位于工作区右侧之外。");

            WindowValidationInput.RaisePreviewKey(floating, Key.R, ModifierKeys.Control);
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                WindowIntersectsWorkArea(floating),
                "Ctrl+R 必须把越界悬浮窗夹回工作区（非拖动复位）。");

            floating.Left = workArea.Right + 120;
            floating.Top = workArea.Bottom + 120;
            WindowValidationInput.Pump(floating.Dispatcher);
            trayController.InvokeResetPositionForValidation();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                WindowIntersectsWorkArea(floating),
                "托盘菜单「复位窗口位置」必须把越界悬浮窗夹回工作区。");

            floating.Hide();
            WindowValidationInput.Pump(floating.Dispatcher);
            trayController.InvokeResetPositionForValidation();
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(
                floating.IsVisible &&
                floating.WindowState == WindowState.Normal &&
                WindowIntersectsWorkArea(floating),
                "隐藏态的托盘复位必须先恢复悬浮窗再夹回工作区。");
            return true;
        }
        finally
        {
            trayController.Dispose();
            floating.Close();
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #230：沿既有窗口 tick/显示入口验证隐藏期间不重建分格，恢复时立即追上时钟。
    public static bool RunMainWindowHiddenRefreshBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-hidden-refresh-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var clock = new AdvancingValidationClock(
                new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            MustApply(workspace, new SaveCountdownChange(
                null,
                "隐藏刷新倒计时",
                clock.UtcNow.AddMinutes(10),
                Array.Empty<string>(),
                Array.Empty<ReminderRule>()));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var main = new MainWindow(
                workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                main.UpdateLayout();
                var rows = (Panel)main.FindName("CountdownRows");
                var blocksBefore = VisualDescendants<TextBlock>(rows).ToArray();
                var remainingBefore = main.CountdownRemainingTextsForValidation.Values.Single().Text;
                var nowText = (TextBlock)main.FindName("NowText");
                var timeBefore = nowText.Text;
                Must(blocksBefore.Length > 0, "隐藏刷新探针必须先渲染真实倒计时分格。");

                main.Hide();
                clock.UtcNow = clock.UtcNow.AddMinutes(2);
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                Must(
                    !main.IsVisible &&
                    blocksBefore.SequenceEqual(VisualDescendants<TextBlock>(rows)) &&
                    main.CountdownRemainingTextsForValidation.Values.Single().Text == remainingBefore &&
                    nowText.Text == timeBefore,
                    "#230：管理窗口隐藏期间 tick 不得重建倒计时分格或更新时间文本。");

                // 不能再调用 UpdateTime；Show 本身必须补上隐藏期间跳过的刷新。
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                Must(
                    main.IsVisible &&
                    nowText.Text == "当前北京时间：2038 年 7 月 8 日 09:02:00" &&
                    main.CountdownRemainingTextsForValidation.Values.Single().Text != remainingBefore,
                    "#230：管理窗口重新显示后必须立即显示最新时刻与剩余时间，不等待下一次 tick。");
                return true;
            }
            finally
            {
                main.Close();
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    public static bool RunCountdownListFirstBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-list-first-{Guid.NewGuid():N}.json");
        var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
        var workspace = DesktopWorkspace.Load(path, new SettingsStore(clock: clock));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        using var workArea = MonitorWorkAreaResolver.OverrideForValidation(
            new MonitorWorkArea(new Rect(0, 0, 2560, 2000), 1, 1));
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            for (var index = 0; index < 8; index++)
                MustApply(workspace, new SaveCountdownChange(null, $"列表优先 {index}",
                    clock.UtcNow.AddDays(index + 1), Array.Empty<string>(), Array.Empty<ReminderRule>()));
            PrepareOffscreenValidationWindow(main);
            using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(main, new Size(3840, 2160));
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            main.UpdateLayout();
            var list = (ScrollViewer)main.FindName("CountdownListRegion");
            var rows = (Panel)main.FindName("CountdownRows");
            Must(!((FrameworkElement)main.FindName("CountdownHour")).IsVisible &&
                 ((Button)main.FindName("SaveCountdownButton")).IsVisible &&
                 ((TextBox)main.FindName("CountdownName")).IsVisible &&
                 ((DatePicker)main.FindName("CountdownDate")).IsVisible &&
                 !((FrameworkElement)main.FindName("MonthlySalary")).IsVisible,
                "#221：默认新增必须收起，名称/日期/添加可达，偏好不占据列表页面。");
            var third = (FrameworkElement)rows.Children[2];
            var thirdBottom = third.TranslatePoint(new Point(0, third.ActualHeight), list).Y;
            Must(thirdBottom <= list.ViewportHeight + 1,
                $"#221：首选尺寸至少三行完整可见；thirdBottom={thirdBottom}; viewport={list.ViewportHeight}。");
            main.Height = 900;
            WindowValidationInput.Pump(main.Dispatcher);
            var shortHeight = list.ViewportHeight;
            var shortRows = FullyVisibleRows();
            main.Height = 1200;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(list.ViewportHeight > shortHeight + 100 && FullyVisibleRows() > shortRows,
                $"#221：窗口加高时列表须显示更多完整行；before={shortHeight}/{shortRows}; after={list.ViewportHeight}/{FullyVisibleRows()}。");
            return true;

            int FullyVisibleRows() => rows.Children.OfType<FrameworkElement>().Count(row =>
                row.TranslatePoint(new Point(), list).Y >= -1 &&
                row.TranslatePoint(new Point(0, row.ActualHeight), list).Y <= list.ViewportHeight + 1);
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainTextShortcutBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-text-shortcuts-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var name = (TextBox)main.FindName("CountdownName");
            name.Text = "名称输入焦点保存";
            ((DatePicker)main.FindName("CountdownDate")).SelectedDate = workspace.Snapshot.BeijingNow.AddDays(30).Date;
            Keyboard.Focus(name);
            Must(WindowValidationInput.RaisePreviewKey(name, Key.S, ModifierKeys.Control),
                "#224：倒计时名称输入焦点下Ctrl+S必须响应。");
            Must(DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Any(item => item.Name == "名称输入焦点保存"),
                "#224：名称输入中的Ctrl+S必须真实保存倒计时。");
            main.ShowWorkspace(WorkspaceKind.Notes);
            var body = (TextBox)main.FindName("NoteText");
            body.Text = "正文快捷添加";
            Keyboard.Focus(body);
            Must(WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control),
                "#224：便签正文Ctrl+Enter必须响应提交。");
            var added = DesktopWorkspace.Load(path).Snapshot.Settings.Notes.SingleOrDefault(note => note.Text == "正文快捷添加");
            Must(added is not null, "#224：Ctrl+Enter必须持久保存便签。");
            main.LoadNoteIntoEditor(added!);
            body.Text = "正文快捷修改";
            Keyboard.Focus(body);
            Must(WindowValidationInput.RaisePreviewKey(body, Key.S, ModifierKeys.Control) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Notes.Single().Text == "正文快捷修改",
                "#224：便签页Ctrl+S必须保存当前编辑，不能重复添加。");
            body.Text = "首行";
            body.CaretIndex = body.Text.Length;
            Keyboard.Focus(body);
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.Enter),
                "#224：便签正文普通Enter不得被提交快捷键消费。");
            WindowValidationInput.RaiseKey(body, Key.Enter);
            Must(body.Text.Contains('\n') && workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：便签正文普通Enter必须真实换行且不能保存。");
            var popup = (Popup)main.FindName("FilterPopup");
            popup.IsOpen = true;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.S, ModifierKeys.Control) &&
                 !WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control) &&
                 workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：弹层打开时Ctrl+S和Ctrl+Enter不允许穿透保存草稿。");
            WindowValidationInput.ClosePopupForCleanup(popup);
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            var countdownDate = (DatePicker)main.FindName("CountdownDate");
            countdownDate.IsDropDownOpen = true;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(countdownDate.IsDropDownOpen &&
                 !WindowValidationInput.RaisePreviewKey(countdownDate, Key.D2, ModifierKeys.Control) &&
                 main.CurrentWorkspace == WorkspaceKind.Countdowns,
                "#224：原生日历下拉打开时全局Ctrl快捷键仍被屏蔽。");
            countdownDate.IsDropDownOpen = false;
            ((Button)main.FindName("CountdownEditorExpandButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var hour = (ComboBox)main.FindName("CountdownHour");
            hour.IsDropDownOpen = true;
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsDropDownOpen && !WindowValidationInput.RaisePreviewKey(hour, Key.D2, ModifierKeys.Control) &&
                 main.CurrentWorkspace == WorkspaceKind.Countdowns,
                "#224：时间下拉打开时全局Ctrl快捷键仍被屏蔽。");
            hour.IsDropDownOpen = false;
            foreach (var (id, shortcut) in new[]
            {
                ("CountdownTab", "Ctrl+1"), ("NotesTab", "Ctrl+2"), ("MinimizeButton", "Ctrl+M"),
                ("FilterButton", "Ctrl+F"), ("TagManagerButton", "Ctrl+T"),
                ("SaveCountdownButton", "Ctrl+S"), ("SaveNoteButton", "Ctrl+Enter"),
                ("SavePreferencesButton", "Ctrl+Shift+S"), ("CreateTagButton", "Enter")
            })
                Must(((FrameworkElement)main.FindName(id)).ToolTip?.ToString()?.Contains(shortcut, StringComparison.Ordinal) == true,
                    $"#224：{id} ToolTip必须提示{shortcut}。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunSingleLineEnterSubmission()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-enter-form-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var name = (TextBox)main.FindName("CountdownName");
            var date = (DatePicker)main.FindName("CountdownDate");
            name.Text = "名称回车保存";
            date.SelectedDate = workspace.Snapshot.BeijingNow.AddDays(30).Date;
            Keyboard.Focus(name);
            Must(WindowValidationInput.RaisePreviewKey(name, Key.Enter) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Any(item => item.Name == "名称回车保存"),
                "#224：单行倒计时名称Enter必须持久提交所在表单。");
            name.Clear();
            date.SelectedDate = null;
            Keyboard.Focus(name);
            Must(WindowValidationInput.RaisePreviewKey(name, Key.Enter) && name.IsKeyboardFocusWithin &&
                 ((TextBlock)main.FindName("CountdownNameError")).IsVisible && workspace.Snapshot.Settings.Countdowns.Count == 1,
                "#224：Enter校验失败必须保留草稿并聚焦第一个无效字段。");
            var preferences = (Popup)main.FindName("PreferencesPopup");
            preferences.IsOpen = true;
            WindowValidationInput.Pump(main.Dispatcher);
            var salary = (TextBox)main.FindName("MonthlySalary");
            var start = (TextBox)main.FindName("WorkStart");
            var end = (TextBox)main.FindName("WorkEnd");
            salary.Text = "56789"; start.Text = "08:15"; end.Text = "18:45";
            Keyboard.Focus(salary);
            Must(!WindowValidationInput.RaisePreviewKey(salary, Key.D2, ModifierKeys.Control) &&
                 main.CurrentWorkspace == WorkspaceKind.Countdowns,
                "#224：设置弹层文本焦点不允许全局Ctrl快捷键穿透。");
            Must(WindowValidationInput.RaisePreviewKey(salary, Key.Enter) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Preferences.MonthlySalary == 56789m,
                "#224：设置弹层单行字段Enter应提交设置。");
            salary.Text = "坏金额"; start.Text = "坏时间";
            Keyboard.Focus(end);
            Must(WindowValidationInput.RaisePreviewKey(end, Key.Enter) && salary.IsKeyboardFocusWithin &&
                 ((TextBlock)main.FindName("MonthlySalaryError")).IsVisible,
                "#224：工时Enter提交失败必须聚焦首个无效薪资字段。");
            WindowValidationInput.ClosePopupForCleanup(preferences);
            var tags = (Popup)main.FindName("TagManagerPopup");
            ((Button)main.FindName("TagManagerButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var tagName = (TextBox)main.FindName("NewTagInput");
            tagName.Text = "回车新标签";
            Keyboard.Focus(tagName);
            Must(WindowValidationInput.RaisePreviewKey(tagName, Key.Enter) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Tags.Any(tag => tag.Name == "回车新标签"),
                "#224：新标签名Enter必须在标签弹层提交。");
            tagName.Clear();
            Must(WindowValidationInput.RaisePreviewKey(tagName, Key.Enter) && tagName.IsKeyboardFocusWithin &&
                 workspace.Snapshot.Settings.Tags.Count == 1,
                "#224：空标签Enter不得保存且保留名称焦点。");
            WindowValidationInput.ClosePopupForCleanup(tags);
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunFloatingNoteKeyboardSubmission()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-floating-keyboard-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(floating);
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            ((Button)floating.FindName("FloatingAddNoteToggle")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            var body = (TextBox)floating.FindName("FloatingNoteText");
            body.Text = "浮窗正文";
            Keyboard.Focus(body);
            Must(WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Notes.Any(note => note.Text == "浮窗正文"),
                "#224：悬浮便签正文Ctrl+Enter必须持久提交。");
            ((Button)floating.FindName("FloatingAddNoteToggle")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            body.Clear();
            Keyboard.Focus(body);
            Must(WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control) && body.IsKeyboardFocusWithin &&
                 ((TextBlock)floating.FindName("FloatingNoteTextError")).IsVisible && workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：悬浮Ctrl+Enter校验失败必须聚焦正文且不保存。");
            body.Text = "浮窗首行";
            body.CaretIndex = body.Text.Length;
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.Enter),
                "#224：悬浮正文普通Enter不得提交。");
            WindowValidationInput.RaiseKey(body, Key.Enter);
            Must(body.Text.Contains('\n') && workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：悬浮正文Enter必须真实换行。");
            var tags = (TagSelector)floating.FindName("FloatingTags");
            tags.IsPopupOpen = true;
            WindowValidationInput.Pump(floating.Dispatcher);
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.Enter, ModifierKeys.Control) &&
                 workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：悬浮标签弹层打开时不能由Ctrl+Enter保存草稿。");
            WindowValidationInput.ClosePopupForCleanup(tags.Popup);
            Must(((Button)floating.FindName("FloatingAddNote")).ToolTip?.ToString()?.Contains("Ctrl+Enter", StringComparison.Ordinal) == true &&
                 ((Button)floating.FindName("FloatingSearchNotes")).ToolTip?.ToString()?.Contains("Ctrl+F", StringComparison.Ordinal) == true,
                "#224：悬浮添加与搜索按钮必须显示对应快捷键提示。");
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

    public static bool RunListDeleteKeyboardBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-list-delete-key-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        MustApply(workspace, new SaveCountdownChange(null, "键盘删除倒计时", DateTimeOffset.UtcNow.AddDays(30),
            Array.Empty<string>(), Array.Empty<ReminderRule>()));
        MustApply(workspace, new SaveNoteChange(null, "键盘删除便签", workspace.Snapshot.BeijingNow.Date, false,
            Array.Empty<string>(), Array.Empty<ReminderRule>()));
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        void DeleteFrom(Window owner, UIElement source, string expected, bool confirm)
        {
            owner.Activate(); Keyboard.Focus(source);
            var observed = false;
            QueueConfirmationDialogResponse(owner, confirm, dialog =>
            {
                observed = dialog.BodyText.Text.Contains(expected, StringComparison.Ordinal) &&
                    dialog.CancelButton.IsDefault && !dialog.ConfirmButton.IsDefault;
            });
            Must(WindowValidationInput.RaisePreviewKey(source, Key.Delete),
                "#224：列表行焦点Delete必须进入既有删除确认。");
            WindowValidationInput.Pump(owner.Dispatcher);
            Must(observed, "#224：Delete确认必须绑定当前行记录且默认取消。");
        }
        try
        {
            PrepareOffscreenValidationWindow(main); main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var rows = (Panel)main.FindName("CountdownRows");
            var edit = VisualDescendants<Button>(rows).Single(button => Equals(button.Content, "编辑"));
            DeleteFrom(main, edit, "键盘删除倒计时", false);
            Must(workspace.Snapshot.Settings.Countdowns.Count == 1 && edit.IsKeyboardFocusWithin,
                "#224：Delete取消保留倒计时并归还原行焦点。");
            DeleteFrom(main, edit, "键盘删除倒计时", true);
            Must(workspace.Snapshot.Settings.Countdowns.Count == 0 && main.DeleteUndoForValidation is not null,
                "#224：Delete确认仍提供既有撤销。");
            var name = (TextBox)main.FindName("CountdownName");
            name.Text = "输入撤销"; name.CaretIndex = name.Text.Length; name.SelectedText = "保留";
            Keyboard.Focus(name);
            Must(!WindowValidationInput.RaisePreviewKey(name, Key.Z, ModifierKeys.Control) && name.CanUndo &&
                 workspace.Snapshot.Settings.Countdowns.Count == 0,
                "#224：文本Ctrl+Z必须优先保留编辑撤销，不能消耗删除撤销。");
            // 合成Key的modifier只注入应用路由；WPF原生编辑命令经其公开RoutedCommand seam驱动。
            ApplicationCommands.Undo.Execute(null, name);
            Must(!name.Text.EndsWith("保留", StringComparison.Ordinal), "#224：文本Ctrl+Z必须真实撤销输入。");
            Keyboard.Focus((UIElement)main.FindName("CountdownTab"));
            Must(WindowValidationInput.RaisePreviewKey(main, Key.Z, ModifierKeys.Control) &&
                 workspace.Snapshot.Settings.Countdowns.Count == 1, "#224：离开输入框后Ctrl+Z仍能撤销删除。");
            main.ShowWorkspace(WorkspaceKind.Notes);
            rows = (Panel)main.FindName("ActiveNoteRows");
            var check = VisualDescendants<CheckBox>(rows).Single();
            DeleteFrom(main, check, "键盘删除便签", false);
            Must(workspace.Snapshot.Settings.Notes.Count == 1 && !workspace.Snapshot.Settings.Notes.Single().IsCompleted,
                "#224：便签行Delete取消不删除或勾选完成。");
            DeleteFrom(main, check, "键盘删除便签", true);
            Must(workspace.Snapshot.Settings.Notes.Count == 0, "#224：便签行Delete确认应删除目标记录。");
            ((Button)main.FindName("MainStatusUndoButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Must(workspace.Snapshot.Settings.Notes.Count == 1, "#224：便签键盘删除后可撤销。");
            PrepareOffscreenValidationWindow(floating); floating.Show(); floating.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(floating.Dispatcher);
            var floatingCheck = VisualDescendants<CheckBox>((Panel)floating.FindName("FloatingRows")).Single();
            DeleteFrom(floating, floatingCheck, "键盘删除便签", false);
            Must(workspace.Snapshot.Settings.Notes.Count == 1, "#224：悬浮行Delete取消应保留便签。");
            DeleteFrom(floating, floatingCheck, "键盘删除便签", true);
            Must(workspace.Snapshot.Settings.Notes.Count == 0 && floating.DeleteUndoForValidation is not null,
                "#224：悬浮行Delete确认仍提供撤销。");
            ((Button)floating.FindName("FloatingAddNoteToggle")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(floating.Dispatcher);
            var body = (TextBox)floating.FindName("FloatingNoteText");
            body.Text = "浮窗输入"; body.CaretIndex = body.Text.Length; body.SelectedText = "撤销";
            Keyboard.Focus(body);
            Must(!WindowValidationInput.RaisePreviewKey(body, Key.Z, ModifierKeys.Control) &&
                 workspace.Snapshot.Settings.Notes.Count == 0, "#224：悬浮文本撤销不得恢复被删记录。");
            ApplicationCommands.Undo.Execute(null, body);
            Must(!body.Text.EndsWith("撤销", StringComparison.Ordinal), "#224：悬浮文本Ctrl+Z仍真实撤销编辑。");
            Keyboard.Focus((UIElement)floating.FindName("FloatingNotesButton"));
            Must(WindowValidationInput.RaisePreviewKey(floating, Key.Z, ModifierKeys.Control) && workspace.Snapshot.Settings.Notes.Count == 1,
                "#224：悬浮非文本Ctrl+Z仍可恢复删除。");
            return true;
        }
        finally
        {
            floating.Close(); main.Close(); DeleteSettingsFiles(path); DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainNoteEditorSeparation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-note-separation-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var search = (TextBox)main.FindName("NoteSearch");
            var editor = (FrameworkElement)main.FindName("NoteEditorPanel");
            var list = (FrameworkElement)main.FindName("NotesListCard");
            Must(list.IsAncestorOf(search) && !editor.IsAncestorOf(search),
                "#227：搜索便签必须位于状态列表区域，不属于新增编辑表单。");
            var note = new NoteRecord("note-layout-probe", "编辑态便签", "2038-07-08", false,
                Array.Empty<string>(), Array.Empty<ReminderRule>());
            main.LoadNoteIntoEditor(note);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(main.FindName("NoteEditorTitle") is TextBlock { Text: "编辑便签", IsVisible: true },
                "#227：编辑态必须显示编辑便签标题。");
            ((Button)main.FindName("CancelNoteEdit")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(!((FrameworkElement)main.FindName("NoteEditorTitle")).IsVisible,
                "#227：取消编辑后不能继续显示编辑便签标题。");
            var summary = (FrameworkElement)main.FindName("NoteReminderRulesSummary");
            var field = (FrameworkElement)main.FindName("ManageNoteReminderRulesButton");
            Must(Math.Abs(summary.TransformToAncestor(main).Transform(new Point()).X -
                 field.TransformToAncestor(main).Transform(new Point()).X) <= 1,
                "#227：规则摘要应与字段列左对齐。");
            Keyboard.Focus((UIElement)main.FindName("NotesTab"));
            Must(WindowValidationInput.RaisePreviewKey((UIElement)main.FindName("NotesTab"), Key.F, ModifierKeys.Control) &&
                 search.IsKeyboardFocusWithin, "#227：Ctrl+F仍聚焦移动后的搜索便签。");
            Must(main.FindName("NoteGroupingSelector") is null,
                "#227：只有一个选项的按日期分组下拉应移除。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainNoteRowsSimplified()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-note-rows-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        for (var index = 0; index < 5; index++)
        {
            MustApply(workspace, new SaveNoteChange(null, $"交替便签 {index}",
                new DateTime(2038, 7, index < 3 ? 8 : 9), false,
                Array.Empty<string>(), new ReminderRule[] { new AbsoluteOnceReminderRule(
                    $"note-row-rule-{index}", new DateTimeOffset(2038, 7, 10, 1, 0, 0, TimeSpan.Zero)) }));
        }
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            main.ShowWorkspace(WorkspaceKind.Notes);
            WindowValidationInput.Pump(main.Dispatcher);
            var rows = (Panel)main.FindName("ActiveNoteRows");
            void VerifyRows(int count, bool completed = false)
            {
                main.UpdateLayout();
                WindowValidationInput.Pump(main.Dispatcher);
                var bodies = VisualDescendants<TextBlock>(rows).Where(text => text.Uid == "NoteRowBodyText").ToArray();
                Must(bodies.Length == count, "#227：行样式探针必须建立预期非空便签列表。");
                for (var index = 0; index < bodies.Length; index++)
                {
                    var body = bodies[index];
                    var card = VisualDescendants<Border>(rows).Single(border => border.Child is Grid && border.IsAncestorOf(body));
                    var buttons = VisualDescendants<Button>(card).ToArray();
                    Must(buttons.Length == 2 && buttons.Any(button => Equals(button.Content, "编辑")) &&
                         buttons.Any(button => Equals(button.Content, "删除")),
                        "#227：主便签行只保留编辑和删除，不再重复显示更多菜单。");
                    var summaries = VisualDescendants<TextBlock>(card).Where(text => AutomationProperties.GetAutomationId(text) ==
                        MainWindow.NoteCardReminderSummaryAutomationId).ToArray();
                    Must(Math.Abs(body.FontSize - (double)main.FindResource("TypeCaptionFontSize")) <= 0.01 &&
                         summaries.Length == 1 && summaries.All(text => text.IsVisible && text.FontSize <= body.FontSize),
                        "#227/#237：便签正文使用管理窗降一档后的 caption 阶梯，提醒摘要不得大于正文。");
                    var delete = buttons.Single(button => Equals(button.Content, "删除"));
                    Must(delete.Foreground is SolidColorBrush foreground && foreground.Color == (Color)main.FindResource("DangerColor") &&
                         delete.BorderBrush is SolidColorBrush borderBrush && borderBrush.Color.A == 0 &&
                         VisualDescendants<System.Windows.Shapes.Path>(delete).Any(icon => icon.IsVisible),
                        "#227：便签删除应为无红边框的危险色文字图标。");
                    var expected = (SolidColorBrush)main.FindResource(completed || index % 2 == 1 ? "NoteWhiteBrush" : "NoteBrush");
                    Must(card.Background is SolidColorBrush actual && actual.Color == expected.Color,
                        "#227：便签底色必须按跨日期组的可见位置黄白交替，已完成全白。");
                }
            }
            VerifyRows(5);
            ((TextBox)main.FindName("NoteSearch")).Text = "交替便签 4";
            VerifyRows(1);
            ((TextBox)main.FindName("NoteSearch")).Clear();
            VerifyRows(5);
            var removed = workspace.Snapshot.Settings.Notes.First();
            MustApply(workspace, new DeleteNoteChange(removed.Id));
            VerifyRows(4);
            foreach (var note in workspace.Snapshot.Settings.Notes.ToArray())
                MustApply(workspace, new SaveNoteChange(note.Id, note.Text, DateTime.Parse(note.Date!), true,
                    note.TagIds, note.ReminderRules));
            ((ToggleButton)main.FindName("CompletedNotesSegment")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            rows = (Panel)main.FindName("CompletedNoteRows");
            VerifyRows(4, completed: true);
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainRedundancyCleanup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-redundancy-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        var announcements = new List<TextBlock>();
        void Remember(TextBlock block) => announcements.Add(block);
        UiAccessibility.Announced += Remember;
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            FlushNarrator(main.Dispatcher);
            var count = (TextBlock)main.FindName("RecordCountText");
            Must(ReferenceEquals(count.Parent, main.FindName("MainCommandBarZone")) && count.IsVisible &&
                 count.ActualWidth > 0 && count.ActualHeight > 0,
                "#222：数量应为命令栏中的无框纯文字，不再嵌入卡片或图标容器。");
            Must(main.FindName("DayThemeBadge") is null && main.FindName("MainThemeCaption") is null &&
                 !VisualDescendants<TextBlock>(main).Any(text => text.IsVisible &&
                     (text.Text == "日间" || text.Text.Contains("当前主题：", StringComparison.Ordinal))),
                "#222：页头日间和页脚当前主题文案应删除。");
            var status = (TextBlock)main.FindName("StatusText");
            foreach (var kind in new[] { WorkspaceKind.Notes, WorkspaceKind.Countdowns })
            {
                announcements.Clear();
                main.ShowWorkspace(kind);
                FlushNarrator(main.Dispatcher);
                Must(!status.Text.Contains("当前工作区", StringComparison.Ordinal) &&
                     !announcements.Any(text => text.Text.Contains("当前工作区", StringComparison.Ordinal)),
                    "#222：切页不得显示或播报冗余的当前工作区文案。");
                var liveCounts = VisualDescendants<TextBlock>(main).Where(text => text.IsVisible &&
                    AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Polite &&
                    System.Text.RegularExpressions.Regex.IsMatch(text.Text, @"^\d+ 个(?:倒计时|待办)$")).ToArray();
                Must(liveCounts.Length == 1 && ReferenceEquals(liveCounts[0], count),
                    "#222：数量纯文字仍是唯一数量直播区。");
            }
            announcements.Clear();
            MustApply(workspace, new SaveCountdownChange(null, "数量变更", DateTimeOffset.UtcNow.AddDays(3),
                Array.Empty<string>(), Array.Empty<ReminderRule>()));
            FlushNarrator(main.Dispatcher);
            Must(count.Text == "1 个倒计时" && announcements.Count(block => ReferenceEquals(block, count)) == 1,
                "#222：数量改变仍准确播报一次。");
            announcements.Clear();
            main.UpdateTime(ChinaStandardTime.ToBeijingTime(workspace.Snapshot.UtcNow).AddMinutes(1));
            FlushNarrator(main.Dispatcher);
            Must(!announcements.Contains(count), "#222：数量未变的分钟刷新不得重复播报。");
            return true;
        }
        finally
        {
            UiAccessibility.Announced -= Remember;
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainTopmostStateSynchronization()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-topmost-sync-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var check = (CheckBox)main.FindName("TopmostCheck");
            Must(main.Topmost == (check.IsChecked == true), "#222：首次打开时窗口置顶必须与勾选状态一致。");
            var peer = new CheckBoxAutomationPeer(check);
            var toggle = (System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
            var initial = check.IsChecked;
            toggle.Toggle();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(check.IsChecked != initial && main.Topmost == (check.IsChecked == true),
                "#222：切换置顶勾选必须同步窗口 Topmost。");
            toggle.Toggle();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(check.IsChecked == initial && main.Topmost == (check.IsChecked == true),
                "#222：再次切换必须恢复初始勾选与窗口 Topmost。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunCountdownPreferencesPopupBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-preferences-popup-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            main.Activate();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(main.FindName("PreferencesButton") is Button,
                "#221：命令栏应提供设置齿轮入口。");
            var trigger = (Button)main.FindName("PreferencesButton");
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            var popup = (Popup)main.FindName("PreferencesPopup");
            var precision = (ComboBox)main.FindName("DisplayPrecision");
            var salary = (TextBox)main.FindName("MonthlySalary");
            var save = (Button)main.FindName("SavePreferencesButton");
            Must(popup.IsOpen && precision.IsKeyboardFocusWithin,
                "#221：设置弹层打开时应聚焦显示精度。");
            if (!WindowValidationInput.RaisePreviewKey(precision, Key.F4, ModifierKeys.None))
                WindowValidationInput.RaiseKey(precision, Key.F4);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(precision.IsDropDownOpen, "#221：设置中的显示精度可用 F4 打开嵌套下拉。");
            if (!WindowValidationInput.RaisePreviewKey(precision, Key.Escape, ModifierKeys.None))
                WindowValidationInput.RaiseKey(precision, Key.Escape);
            var nestedPopup = (Popup)precision.Template.FindName("PART_Popup", precision);
            Must(WindowValidationInput.WaitForPopupClosed(nestedPopup) &&
                 !precision.IsDropDownOpen && popup.IsOpen && precision.IsVisible && precision.IsKeyboardFocusWithin,
                "#221：嵌套下拉的首次 Esc 只关闭下拉，设置层仍可见且保留显示精度焦点。");
            save.Focus();
            save.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(precision.IsKeyboardFocusWithin, "#221：设置弹层 Tab 必须层内循环。");
            salary.Text = "12345";
            WindowValidationInput.RaisePreviewKey(salary, Key.Escape, ModifierKeys.None);
            Must(WindowValidationInput.WaitUntil(main.Dispatcher, () => !popup.IsOpen && trigger.IsKeyboardFocusWithin),
                "#221：设置弹层 Esc 关闭并归还齿轮焦点。");
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(salary.Text == "12345", "#221：设置弹层收起后保留未保存草稿。");
            salary.Text = "invalid";
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(((TextBlock)main.FindName("MonthlySalaryError")).IsVisible && salary.IsKeyboardFocusWithin &&
                 ((TextBlock)main.FindName("StatusText")).Text == "月薪格式无效。",
                "#221：设置字段错误必须在弹层中可见且镜像状态栏。");
            salary.Text = "12345";
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(((TextBlock)main.FindName("StatusText")).Text == "设置已保存" &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Preferences.MonthlySalary == 12345,
                "#221：设置保存应持久化并显示设置已保存。");
            WindowValidationInput.ClosePopupForCleanup(popup);
            main.Hide();
            var lifecycle = new ManagerWindowLifecycle(
                () => new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory),
                PrepareOffscreenValidationWindow);
            try
            {
                foreach (var host in new IManagerWorkspaceHost[] { main, lifecycle })
                {
                    var floating = new FloatingWindow(workspace, calendar, host, AppContext.BaseDirectory);
                    try
                    {
                        PrepareOffscreenValidationWindow(floating);
                        floating.Show();
                        floating.ShowWorkspace(WorkspaceKind.Notes);
                        WindowValidationInput.Pump(main.Dispatcher);
                        var settingsButton = (Button)floating.FindName("FloatingOpenSettings");
                        Must(settingsButton.IsVisible, "#221：悬浮便签页设置入口应可见。");
                        settingsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        WindowValidationInput.Pump(main.Dispatcher);
                        var revealed = host is MainWindow direct ? direct : lifecycle.Window!;
                        Must(WindowValidationInput.WaitUntil(revealed.Dispatcher, () => revealed.IsVisible && revealed.CurrentWorkspace == WorkspaceKind.Notes &&
                             ((Popup)revealed.FindName("PreferencesPopup")).IsOpen &&
                             ((ComboBox)revealed.FindName("DisplayPrecision")).IsKeyboardFocusWithin),
                            $"#221：悬浮设置应支持直接与懒加载宿主，并直接打开管理窗设置弹层；host={host.GetType().Name}; " +
                            $"visible={revealed.IsVisible}; workspace={revealed.CurrentWorkspace}; " +
                            $"popup={((Popup)revealed.FindName("PreferencesPopup")).IsOpen}; " +
                            $"focus={Keyboard.FocusedElement}; active={revealed.IsActive}; " +
                            $"precisionVisible={((ComboBox)revealed.FindName("DisplayPrecision")).IsVisible}。");
                        WindowValidationInput.ClosePopupForCleanup((Popup)revealed.FindName("PreferencesPopup"));
                        revealed.Hide();
                    }
                    finally { floating.Close(); }
                }
            }
            finally { lifecycle.Window?.Close(); }
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunCountdownEditorExpansionBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-editor-expansion-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        MustApply(workspace, new SaveCountdownChange(null, "编辑展开", DateTimeOffset.UtcNow.AddDays(3),
            Array.Empty<string>(), Array.Empty<ReminderRule>()));
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            main.Activate();
            WindowValidationInput.Pump(main.Dispatcher);
            var name = (TextBox)main.FindName("CountdownName");
            var hour = (ComboBox)main.FindName("CountdownHour");
            var toggle = (Button)main.FindName("CountdownEditorExpandButton");
            var rows = (Panel)main.FindName("CountdownRows");
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsVisible, "#221：展开新增应显示时分秒、标签及提醒规则。");
            name.Text = "未保存草稿";
            hour.SelectedIndex = 7;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            main.ShowWorkspace(WorkspaceKind.Notes);
            main.ShowWorkspace(WorkspaceKind.Countdowns);
            WindowValidationInput.Pump(main.Dispatcher);
            Must(name.Text == "未保存草稿" && hour.SelectedIndex == 7 && !hour.IsVisible,
                "#221：收起和切页须保留新增草稿。");
            var edit = VisualDescendants<Button>(rows).First(button => Equals(button.Content, "编辑"));
            edit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsVisible && ((TextBlock)main.FindName("CountdownEditorTitle")).Text == "编辑倒计时" &&
                 ((Button)main.FindName("CancelCountdownEdit")).IsVisible,
                "#221：编辑现有倒计时应自动展开并显示编辑标题和取消。");
            ((Button)main.FindName("CancelCountdownEdit")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(!hour.IsVisible && name.Text.Length == 0 && main.EditingCountdownId is null,
                "#221：取消编辑返回折叠新增，不写入或删除记录。");
            Must(DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Single().Name == "编辑展开",
                "#221：取消编辑不得改变持久记录。");
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            name.Text = "无效时间草稿";
            hour.SelectedIndex = -1;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            ((Button)main.FindName("SaveCountdownButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsVisible && hour.IsKeyboardFocusWithin && name.Text == "无效时间草稿" &&
                 ((TextBlock)main.FindName("CountdownMessage")).IsVisible &&
                 !string.IsNullOrEmpty(((TextBlock)main.FindName("CountdownMessage")).Text) &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Count == 1,
                "#221：收起后的非法时间草稿提交必须重新展开并聚焦无效时间，保留草稿且不写入。");
            hour.SelectedIndex = 7;
            ((DatePicker)main.FindName("CountdownDate")).SelectedDate = DateTime.Today.AddDays(-1);
            ((RadioButton)main.FindName("CountdownReminderTimeRadio")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var reminderErrorFocused = false;
            main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var editor = main.OwnedWindows.OfType<ReminderRulesEditorWindow>().SingleOrDefault();
                if (editor is null) return;
                try
                {
                    var firstDue = VisualDescendants<DatePicker>(editor).Single(control =>
                        AutomationProperties.GetAutomationId(control) == "ReminderRuleFirstDueDate");
                    var error = VisualDescendants<TextBlock>(editor).Single(control =>
                        AutomationProperties.GetAutomationId(control) == "ReminderRuleFirstDueError");
                    reminderErrorFocused = editor.IsVisible && firstDue.IsKeyboardFocusWithin &&
                        error.IsVisible && !string.IsNullOrEmpty(error.Text);
                }
                finally { editor.Close(); }
            }));
            ((Button)main.FindName("SaveCountdownButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WindowValidationInput.Pump(main.Dispatcher);
            Must(hour.IsVisible && reminderErrorFocused && name.Text == "无效时间草稿" &&
                 DesktopWorkspace.Load(path).Snapshot.Settings.Countdowns.Count == 1,
                "#221：收起后的非法提醒提交必须展开提醒区域并在规则窗口聚焦错误，取消后保留草稿。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunCountdownRowDeadlineAndSecondaryDeleteBehavior()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MinminCountdown-row-deadline-{Guid.NewGuid():N}.json");
        var workspace = DesktopWorkspace.Load(path);
        using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
        MustApply(workspace, new SaveCountdownChange(null, "截止行验证", new DateTimeOffset(2038, 7, 8, 1, 30, 0, TimeSpan.Zero),
            Array.Empty<string>(), Array.Empty<ReminderRule>()));
        var main = new MainWindow(workspace, calendar, new DiagnosticLogger(path + ".log"), AppContext.BaseDirectory);
        try
        {
            PrepareOffscreenValidationWindow(main);
            main.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            var rows = (Panel)main.FindName("CountdownRows");
            Must(VisualDescendants<TextBlock>(rows).Any(text => text.IsVisible && text.Text == "7 月 8 日 09:30 截止"),
                "#221：倒计时列表应显示北京时区的月日时分截止行。");
            var delete = VisualDescendants<Button>(rows).Single(button => Equals(button.Content, "删除"));
            Must(delete.Foreground is SolidColorBrush foreground && foreground.Color == (Color)main.FindResource("DangerColor") &&
                 delete.BorderBrush is SolidColorBrush border && border.Color.A == 0 &&
                 VisualDescendants<System.Windows.Shapes.Path>(delete).Any(path => path.IsVisible),
                "#221：行删除为危险色文字加图标，不使用红色边框。");
            return true;
        }
        finally
        {
            main.Close();
            DeleteSettingsFiles(path);
            DeleteFileIfPresent(path + ".log");
        }
    }

    public static bool RunMainWindowNoteMinuteRefreshBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-note-minute-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var clock = new AdvancingValidationClock(
                new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            for (var index = 0; index < 12; index++)
            {
                MustApply(workspace, new SaveNoteChange(
                    null, $"分钟刷新便签 {index:D2}", new DateTime(2038, 7, 8), false,
                    Array.Empty<string>(), Array.Empty<ReminderRule>()));
            }

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var main = new MainWindow(
                workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                ((ButtonBase)main.FindName("NotesTab")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                Must(((TextBlock)main.FindName("NowText")).Text == "当前北京时间：2038 年 7 月 8 日 09:00:00",
                    "#225 状态栏应显示无前导零、带中文空格的北京时间。");
                var scroller = (ScrollViewer)main.FindName("NotesListRegion");
                var activeRows = (Panel)main.FindName("ActiveNoteRows");
                var trigger = VisualDescendants<Button>(activeRows)
                    .Last(button => button.Uid == "NoteRowEdit");
                Keyboard.Focus(trigger);
                WindowValidationInput.Pump(main.Dispatcher);
                scroller.ScrollToEnd();
                WindowValidationInput.Pump(main.Dispatcher);
                var offset = scroller.VerticalOffset;
                Must(ReferenceEquals(Keyboard.FocusedElement, trigger) && offset > 0,
                    "分钟刷新探针须先聚焦真实便签操作按钮并建立非零滚动偏移。");

                clock.UtcNow = clock.UtcNow.AddMinutes(1);
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                Must(ReferenceEquals(Keyboard.FocusedElement, trigger) &&
                     VisualDescendants<Button>(activeRows).Contains(trigger) &&
                     Math.Abs(scroller.VerticalOffset - offset) <= 0.1,
                    "#230：便签数据未变的分钟刷新必须保留行控件、键盘焦点与滚动位置。");

                var note = workspace.Snapshot.AllNotes.Single(item => item.Id == (string)trigger.Tag);
                var dueUtc = clock.UtcNow.AddMinutes(1);
                MustApply(workspace, new SaveNoteChange(note.Id, note.Text, new DateTime(2038, 7, 8), false,
                    note.TagIds, new ReminderRule[]
                    {
                        new FixedIntervalReminderRule("minute-focus-repeat", dueUtc, new ReminderInterval(30))
                    }));
                WindowValidationInput.Pump(main.Dispatcher);
                trigger = VisualDescendants<Button>(activeRows)
                    .Single(button => button.Uid == "NoteRowEdit" && (string)button.Tag == note.Id);
                Keyboard.Focus(trigger);
                WindowValidationInput.Pump(main.Dispatcher);
                scroller.ScrollToEnd();
                WindowValidationInput.Pump(main.Dispatcher);
                offset = scroller.VerticalOffset;
                var summary = VisualDescendants<TextBlock>(activeRows).Single(text =>
                    AutomationProperties.GetAutomationId(text) == MainWindow.NoteCardReminderSummaryAutomationId);
                Must(summary.Text.Contains("09:02", StringComparison.Ordinal), "周期便签初始摘要应指向首次提醒。");
                ProbeVisibleReminderDates(workspace, calendar, main, note, dueUtc);
                clock.UtcNow = dueUtc;
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();
                var summaryAfter = VisualDescendants<TextBlock>(activeRows).Single(text =>
                    AutomationProperties.GetAutomationId(text) == MainWindow.NoteCardReminderSummaryAutomationId);
                Must(summaryAfter.Text.Contains("09:32", StringComparison.Ordinal),
                    "周期提醒跨首次到期后须显示真实的下一次摘要。");
                Must(ReferenceEquals(Keyboard.FocusedElement, trigger) &&
                     VisualDescendants<Button>(activeRows).Contains(trigger) &&
                     Math.Abs(scroller.VerticalOffset - offset) <= 0.1,
                    "#230：周期提醒摘要变化的分钟刷新必须保留便签操作角色焦点与滚动位置。");
                clock.UtcNow = new DateTimeOffset(2038, 7, 8, 16, 0, 0, TimeSpan.Zero);
                main.UpdateTime(ChinaStandardTime.ToBeijingTime(clock.UtcNow));
                WindowValidationInput.Pump(main.Dispatcher);
                var dateLabel = VisualDescendants<TextBlock>(activeRows)
                    .Single(text => text.Uid == "NoteDateLabel");
                var dateToggle = VisualDescendants<Button>(activeRows)
                    .Single(button => button.Uid == "NoteDateDisclosure");
                Must(dateLabel.Text == "昨天 · 7 月 8 日" &&
                     AutomationProperties.GetName(dateToggle).Contains("昨天 · 7 月 8 日", StringComparison.Ordinal) &&
                     dateToggle.ToolTip is string dateTip && dateTip.Contains("昨天 · 7 月 8 日", StringComparison.Ordinal) &&
                     ReferenceEquals(Keyboard.FocusedElement, trigger) &&
                     Math.Abs(scroller.VerticalOffset - offset) <= 0.1,
                    "#230：跨北京时间午夜应就地更新便签日期与可访问名并保留焦点和滚动。");
                return true;
            }
            finally
            {
                main.Close();
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #211：管理窗口装进任意工作区。注入 1080p 在 100%/125%/150% 缩放下的三档
    // 工作区高度（1032/826/688 DIP），窗口必须取「首选 864×1000 与工作区−安全边距」
    // 的较小值并完整落在工作区内；100% 时首选尺寸一屏放下、无纵向滚动，其余档由外层
    // 纵向滚动兜底；顶端置顶/最小化/关闭免滚动可达；状态栏完整可见；倒计时前三行
    // 逐行滚动可达完整可见；内容不缩放。用户调整
    // 的720×560 位置尺寸在隐藏后再显示时保留；越界显示与 WM_DISPLAYCHANGE 工作区
    // 变化时夹回所在显示器工作区。全程经注入工作区，不依赖真实显示拓扑。
    public static bool RunMainWindowWorkAreaFitBehavior() =>
        RunMainWindowWorkAreaFitBehavior(AppContext.BaseDirectory);

    internal static bool RunMainWindowWorkAreaFitBehavior(string assetRoot)
    {
        // The injected monitor must not depend on the SSH desktop's 1024x768
        // native tracking limit. Verify both the constrained and roomy fixtures.
        foreach (var capacity in new[] { new Size(1044, 788), new Size(3840, 2160) })
        {
            var probe = new Window { Width = 1165, Height = 1008, WindowStyle = WindowStyle.None };
            PrepareOffscreenValidationWindow(probe);
            using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(probe, capacity);
            try
            {
                var preparedHandle = new WindowInteropHelper(probe).Handle;
                Must(preparedHandle != IntPtr.Zero && !probe.IsVisible,
                    "原生尺寸夹具应在首次显示前初始化不可见 HWND。");
                probe.Show();
                Must(new WindowInteropHelper(probe).Handle == preparedHandle,
                    "首次显示应复用已设置尺寸容量的 HWND。");
                probe.Width = 1165;
                probe.Height = 1008;
                WindowValidationInput.Pump(probe.Dispatcher);
                Must(
                    Math.Abs(probe.ActualWidth - Math.Min(1165, capacity.Width)) <= 0.5 &&
                    Math.Abs(probe.ActualHeight - Math.Min(1008, capacity.Height)) <= 0.5,
                    $"原生尺寸夹具未生效：容量 {capacity}，实际 {probe.ActualWidth}x{probe.ActualHeight}。");
            }
            finally
            {
                probe.Close();
            }
        }

        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-workarea-fit-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var clock = new AdvancingValidationClock(new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var seedBase = new DateTimeOffset(2038, 7, 9, 1, 0, 0, TimeSpan.Zero);
            for (var index = 0; index < 3; index++)
            {
                MustApply(workspace, new SaveCountdownChange(
                    null,
                    $"工作区适配倒计时 {index + 1}",
                    seedBase.AddDays(index + 1),
                    Array.Empty<string>(),
                    Array.Empty<ReminderRule>()));
            }

            using var calendar = new HolidayCalendar(assetRoot);
            var logger = new DiagnosticLogger(diagnosticPath);
            var workAreaCases = new[]
            {
                (Width: 1920.0, Height: 1032.0, Dpi: 1.0),
                (Width: 1536.0, Height: 826.0, Dpi: 1.25),
                (Width: 1280.0, Height: 688.0, Dpi: 1.5)
            };
            foreach (var workAreaCase in workAreaCases)
            {
                using var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                    new MonitorWorkArea(
                        new Rect(0, 0, workAreaCase.Width, workAreaCase.Height),
                        workAreaCase.Dpi,
                        workAreaCase.Dpi));
                Must(
                    Math.Abs(MonitorWorkAreaResolver.ResolveForScreenRect(new Rect(100, 100, 200, 200), 1, 1).DpiScaleX - workAreaCase.Dpi) <= 0.001,
                    "注入的工作区解析应按注入值返回 DIP 矩形与 DPI。");
                var main = new MainWindow(workspace, calendar, logger, assetRoot);
                PrepareOffscreenValidationWindow(main);
                using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(main, new Size(3840, 2160));
                try
                {
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();

                    var usableWidth = workAreaCase.Width - 24;
                    var usableHeight = workAreaCase.Height - 24;
                    var expectedWidth = Math.Min(MainWindow.PreferredWidth, usableWidth);
                    var expectedHeight = Math.Min(MainWindow.PreferredHeight, usableHeight);
                    Must(
                        Math.Abs(main.Width - expectedWidth) <= 0.5 &&
                        Math.Abs(main.Height - expectedHeight) <= 0.5,
                        $"注入 {workAreaCase.Width}x{workAreaCase.Height}@{workAreaCase.Dpi} 工作区时窗口应取首选与工作区−安全边距的较小值" +
                        $"（期望 {expectedWidth}x{expectedHeight}，实际 {main.Width}x{main.Height}）。");
                    Must(
                        main.Left >= 12 - 0.5 &&
                        main.Top >= 12 - 0.5 &&
                        main.Left + main.Width <= workAreaCase.Width - 12 + 0.5 &&
                        main.Top + main.Height <= workAreaCase.Height - 12 + 0.5,
                        $"窗口四角应完全落在注入工作区安全边距内（实际 Left={main.Left} Top={main.Top} " +
                        $"Right={main.Left + main.Width} Bottom={main.Top + main.Height}）。");

                    foreach (var controlName in new[] { "TopmostCheck", "MinimizeButton", "CloseButton" })
                    {
                        var control = (FrameworkElement)main.FindName(controlName);
                        var controlBounds = control.TransformToAncestor(main).TransformBounds(
                            new Rect(new Point(0, 0), control.RenderSize));
                        Must(
                            controlBounds.X >= -0.5 &&
                            controlBounds.Y >= -0.5 &&
                            controlBounds.Right <= main.ActualWidth + 0.5 &&
                            controlBounds.Bottom <= main.ActualHeight + 0.5,
                            $"{controlName} 应无需滚动完整落在窗口可视区内（{controlBounds}，窗口 {main.ActualWidth}x{main.ActualHeight}）。");
                    }

                    var scrollHost = (ScrollViewer)main.FindName("MainWindowScrollHost");
                    var fitsPreferredHeight = MainWindow.PreferredHeight <= usableHeight + 0.5;
                    Must(
                        fitsPreferredHeight
                            ? scrollHost.ScrollableHeight <= 0.5
                            : scrollHost.ScrollableHeight > 0.5 ||
                                ((ScrollViewer)main.FindName("CountdownListRegion")).ViewportHeight >= 186,
                        fitsPreferredHeight
                            ? $"首选高度装得进工作区时窗口 {main.ActualWidth}x{main.ActualHeight} 应一屏放下、无纵向滚动（可滚 {scrollHost.ScrollableHeight}）。"
                            : $"窗口收缩时列表保有最小可读视口，必要时外层纵向滚动，实际 {main.ActualWidth}x{main.ActualHeight}。");
                    scrollHost.ScrollToBottom();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    var statusBar = (FrameworkElement)main.FindName("MainStatusBarSurface");
                    var statusBounds = statusBar.TransformToAncestor(main).TransformBounds(
                        new Rect(new Point(0, 0), statusBar.RenderSize));
                    Must(
                        statusBounds.Y >= -0.5 &&
                        statusBounds.Bottom <= main.ActualHeight + 0.5,
                        $"滚到底后状态栏应完整落入窗口可视区（{statusBounds}，窗口高 {main.ActualHeight}）。");
                    scrollHost.ScrollToTop();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();

                    var listRegion = (ScrollViewer)main.FindName("CountdownListRegion");
                    var rows = (Panel)main.FindName("CountdownRows");
                    var firstRows = rows.Children.OfType<FrameworkElement>().Take(3).ToArray();
                    Must(firstRows.Length == 3, "倒计时列表种子应提供至少 3 行。");
                    for (var rowIndex = 0; rowIndex < firstRows.Length; rowIndex++)
                    {
                        firstRows[rowIndex].BringIntoView();
                        WindowValidationInput.Pump(main.Dispatcher);
                        main.UpdateLayout();
                        var rowBounds = firstRows[rowIndex].TransformToAncestor(listRegion).TransformBounds(
                            new Rect(new Point(0, 0), firstRows[rowIndex].RenderSize));
                        Must(
                            rowBounds.Y >= -0.5 &&
                            rowBounds.Bottom <= listRegion.ViewportHeight + 0.5,
                            $"第 {rowIndex + 1} 行倒计时应能滚动到在内层视口完整可见" +
                            $"（行 {rowBounds}，内层视口高 {listRegion.ViewportHeight}）。");
                    }

                    var scaleViewport = (FrameworkElement)main.FindName("MainScaleViewport");
                    Must(
                        Math.Abs(scaleViewport.ActualWidth - MainWindow.PreferredWidth) <= 0.5 &&
                        scaleViewport.LayoutTransform.Value.IsIdentity &&
                        Math.Abs(((TextBox)main.FindName("CountdownName")).FontSize -
                            (double)main.FindResource("TypeBodyFontSize")) <= 0.01,
                        "窗口收缩时业务内容不得缩放：宽度保持 864 DIP、字号不变，列表使用剩余高度。");
                }
                finally
                {
                    main.Close();
                }
            }

            using (var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                       new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1)))
            {
                var main = new MainWindow(workspace, calendar, logger, assetRoot);
                PrepareOffscreenValidationWindow(main);
                using var tracking = NativeWorkAreaProbe.OverrideTrackingCapacity(main, new Size(3840, 2160));
                try
                {
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();

                    var handle = new WindowInteropHelper(main).Handle;
                    Must(handle != IntPtr.Zero, "窗口应已创建 HWND。");
                    // 真实用户拖动/缩放手势走 WM_ENTERSIZEMOVE→几何变化→WM_EXITSIZEMOVE；
                    // 探针沿同一 Win32 路径标记「用户调整过位置和尺寸」。
                    NativeWorkAreaProbe.SendMessage(
                        handle,
                        NativeWorkAreaProbe.WmEnterSizeMove,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    main.Width = 720;
                    main.Height = 560;
                    main.Left = 500;
                    main.Top = 300;
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    NativeWorkAreaProbe.SendMessage(
                        handle,
                        NativeWorkAreaProbe.WmExitSizeMove,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Hide();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    Must(
                        Math.Abs(main.Width - 720) <= 0.5 &&
                        Math.Abs(main.Height - 560) <= 0.5 &&
                        Math.Abs(main.Left - 500) <= 0.5 &&
                        Math.Abs(main.Top - 300) <= 0.5,
                        $"隐藏后再显示应保留本次运行内用户调整的位置和尺寸（实际 Left={main.Left} Top={main.Top} {main.Width}x{main.Height}）。");

                    var scrollHost = (ScrollViewer)main.FindName("MainWindowScrollHost");
                    Must(
                        scrollHost.ScrollableWidth > 0.5 && scrollHost.ScrollableHeight > 0.5,
                        "最小尺寸 720×560 下外层容器应纵横双向可滚动（横向兜底），内容不被裁切。");

                    main.Left = 5000;
                    main.Top = 5000;
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Hide();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.Show();
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    Must(
                        main.Left >= 12 - 0.5 &&
                        main.Top >= 12 - 0.5 &&
                        main.Left + main.ActualWidth <= 1920 - 12 + 0.5 &&
                        main.Top + main.ActualHeight <= 1032 - 12 + 0.5,
                        $"越界位置在显示时应夹回所在显示器工作区（实际 Left={main.Left} Top={main.Top}）。");

                    using var smallerOverride = MonitorWorkAreaResolver.OverrideForValidation(
                        new MonitorWorkArea(new Rect(0, 0, 1280, 688), 1.5, 1.5));
                    NativeWorkAreaProbe.SendMessage(
                        handle,
                        NativeWorkAreaProbe.WmDisplayChange,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    WindowValidationInput.Pump(main.Dispatcher);
                    main.UpdateLayout();
                    Must(
                        main.Left >= 12 - 0.5 &&
                        main.Top >= 12 - 0.5 &&
                        main.Left + main.ActualWidth <= 1280 - 12 + 0.5 &&
                        main.Top + main.ActualHeight <= 688 - 12 + 0.5 &&
                        Math.Abs(main.Width - 720) <= 0.5 &&
                        Math.Abs(main.Height - 560) <= 0.5,
                        $"显示器工作区变化后窗口应夹回新工作区且保留用户尺寸" +
                        $"（实际 Left={main.Left} Top={main.Top} {main.Width}x{main.Height}）。");
                }
                finally
                {
                    main.Close();
                }
            }

            return true;
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    // #211：标题拖动区覆盖整个身份区空白，身份区内控件仍可点击。经真实
    // WM_NCHITTEST 路径判定：身份区中缝空白返回 HTCAPTION，置顶控件中心返回 HTCLIENT。
    public static bool RunMainWindowIdentityZoneDragBehavior()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-main-identity-drag-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new AdvancingValidationClock(
                    new DateTimeOffset(2038, 7, 8, 1, 0, 0, TimeSpan.Zero))));
            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            var logger = new DiagnosticLogger(diagnosticPath);
            using var workAreaOverride = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 2560, 1600), 1, 1));
            var main = new MainWindow(workspace, calendar, logger, AppContext.BaseDirectory);
            PrepareOffscreenValidationWindow(main);
            try
            {
                main.Show();
                WindowValidationInput.Pump(main.Dispatcher);
                main.UpdateLayout();

                var identityZone = (FrameworkElement)main.FindName("MainIdentityZone");
                var titleZone = (FrameworkElement)main.FindName("MainTitleZone");
                var windowControls = (FrameworkElement)main.FindName("MainWindowControls");
                var topmostCheck = (FrameworkElement)main.FindName("TopmostCheck");

                Rect BoundsInMain(FrameworkElement element) =>
                    element.TransformToAncestor(main).TransformBounds(
                        new Rect(new Point(0, 0), element.RenderSize));
                var identityBounds = BoundsInMain(identityZone);
                var titleBounds = BoundsInMain(titleZone);
                var controlsBounds = BoundsInMain(windowControls);
                var emptyX = (titleBounds.Right + controlsBounds.Left) / 2;
                var emptyY = identityBounds.Top + (identityBounds.Height / 2);
                Must(
                    emptyX > titleBounds.Right + 8 &&
                    emptyX < controlsBounds.Left - 8 &&
                    emptyY > identityBounds.Top + 8 &&
                    emptyY < identityBounds.Bottom - 8,
                    $"探针空白点应落在身份区中缝（x={emptyX} 应位于标题右缘 {titleBounds.Right} 与控件左缘 {controlsBounds.Left} 之间）。");

                var emptyHit = SendWindowHitTest(main, new Point(emptyX, emptyY));
                Must(
                    emptyHit == NativeWorkAreaProbe.HitCaption,
                    $"身份区空白处应作为标题拖动区（HTCAPTION=2），实际命中 {emptyHit}。");

                var topmostCenter = topmostCheck.TransformToAncestor(main).Transform(
                    new Point(topmostCheck.RenderSize.Width / 2, topmostCheck.RenderSize.Height / 2));
                var controlHit = SendWindowHitTest(main, topmostCenter);
                Must(
                    controlHit == NativeWorkAreaProbe.HitClient,
                    $"身份区内置顶控件应保持可点击（HTCLIENT=1），实际命中 {controlHit}。");
                return true;
            }
            finally
            {
                main.Close();
            }
        }
        finally
        {
            DeleteSettingsFiles(settingsPath);
            DeleteFileIfPresent(diagnosticPath);
        }
    }

    private static int SendWindowHitTest(Window window, Point dipPoint)
    {
        var screenPoint = window.PointToScreen(dipPoint);
        var coordinates = new IntPtr(((int)screenPoint.Y << 16) | ((int)screenPoint.X & 0xffff));
        var handle = new WindowInteropHelper(window).Handle;
        return NativeWorkAreaProbe
            .SendMessage(handle, NativeWorkAreaProbe.WmNcHitTest, IntPtr.Zero, coordinates)
            .ToInt32();
    }

    internal static IDisposable OverrideWindowTrackingCapacityForValidation(Window window, Size capacity) =>
        NativeWorkAreaProbe.OverrideTrackingCapacity(window, capacity);

    private static class NativeWorkAreaProbe
    {
        private const int WmGetMinMaxInfo = 0x0024;
        internal const int WmNcHitTest = 0x0084;
        internal const int WmDisplayChange = 0x007E;
        internal const int WmEnterSizeMove = 0x0231;
        internal const int WmExitSizeMove = 0x0232;
        internal const int HitClient = 1;
        internal const int HitCaption = 2;

        internal static IDisposable OverrideTrackingCapacity(Window window, Size capacity) =>
            new TrackingCapacityScope(window, capacity);

        private sealed class TrackingCapacityScope : IDisposable
        {
            private readonly Window _window;
            private readonly Size _capacity;
            private HwndSource? _source;

            internal TrackingCapacityScope(Window window, Size capacity)
            {
                _window = window;
                _capacity = capacity;
                window.SourceInitialized += OnSourceInitialized;
                try
                {
                    // Show raises visibility before creating its HWND. Initialize
                    // the native limits before business visibility handlers run.
                    new WindowInteropHelper(window).EnsureHandle();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            private void OnSourceInitialized(object? sender, EventArgs args)
            {
                var handle = new WindowInteropHelper(_window).Handle;
                _source = HwndSource.FromHwnd(handle);
                _source.AddHook(ApplyCapacity);
                // WPF cached the real desktop limit during HWND creation, before
                // SourceInitialized. Feed the fixture limit through its normal
                // WM_GETMINMAXINFO path before validation applies its sizes.
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
                try
                {
                    Marshal.StructureToPtr(new MinMaxInfo(), pointer, false);
                    SendMessage(handle, WmGetMinMaxInfo, IntPtr.Zero, pointer);
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }

            private IntPtr ApplyCapacity(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message == WmGetMinMaxInfo)
                {
                    var dpi = VisualTreeHelper.GetDpi(_window);
                    var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    limits.MaxTrackX = (int)Math.Ceiling(_capacity.Width * dpi.DpiScaleX);
                    limits.MaxTrackY = (int)Math.Ceiling(_capacity.Height * dpi.DpiScaleY);
                    Marshal.StructureToPtr(limits, lParam, false);
                    // Leave unhandled: WPF must consume the injected limits.
                }
                return IntPtr.Zero;
            }

            public void Dispose()
            {
                _window.SourceInitialized -= OnSourceInitialized;
                _source?.RemoveHook(ApplyCapacity);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public int ReservedX, ReservedY;
            public int MaxSizeX, MaxSizeY;
            public int MaxPositionX, MaxPositionY;
            public int MinTrackX, MinTrackY;
            public int MaxTrackX, MaxTrackY;
        }

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "SendMessageW")]
        internal static extern IntPtr SendMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);
    }

    private static bool WindowIntersectsWorkArea(Window window)
    {
        var area = MonitorWorkAreaResolver.ResolveForWindow(window).WorkAreaDip;
        return window.Left < area.Right &&
            window.Left + window.ActualWidth > area.Left &&
            window.Top < area.Bottom &&
            window.Top + window.ActualHeight > area.Top;
    }

    // #156：沿原始视图递归窗口自动化树，断言给定元素不再可达（退出朗读的强证据）。
    private static bool AutomationRawTreeContains(
        System.Windows.Automation.Peers.AutomationPeer root,
        IReadOnlyList<FrameworkElement> targets)
    {
        var visited = 0;
        var queue = new Queue<System.Windows.Automation.Peers.AutomationPeer>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            if (++visited > 4_000)
            {
                // 超出遍历上限视为「不可证明缺席」，按包含处理让门禁红出来，而不是静默放过。
                return true;
            }

            var current = queue.Dequeue();
            var owner = (current as System.Windows.Automation.Peers.UIElementAutomationPeer)?.Owner;
            if (owner is not null && targets.Any(target => ReferenceEquals(target, owner)))
            {
                return true;
            }

            IReadOnlyList<System.Windows.Automation.Peers.AutomationPeer>? children;
            try
            {
                children = current?.GetChildren();
            }
            catch (System.Windows.Automation.ElementNotAvailableException)
            {
                continue;
            }

            if (children is null)
            {
                continue;
            }

            foreach (var child in children)
            {
                queue.Enqueue(child);
            }
        }

        return false;
    }

    private static void ProbeVisibleReminderDates(
        DesktopWorkspace workspace, HolidayCalendar calendar, MainWindow main, NoteRecord note, DateTimeOffset dueUtc)
    {
        var floating = new FloatingWindow(workspace, calendar, main, AppContext.BaseDirectory);
        var center = new ReminderCenterWindow();
        PrepareOffscreenValidationWindow(floating);
        PrepareOffscreenValidationWindow(center);
        var key = new ActiveReminderOccurrenceKey("visible-date-probe", dueUtc);
        center.AddEntry(new ReminderCenterItem(key, note, 0, 0));
        try
        {
            floating.Show();
            floating.ShowWorkspace(WorkspaceKind.Notes);
            center.Show();
            WindowValidationInput.Pump(main.Dispatcher);
            Must(VisualDescendants<TextBlock>(floating).Any(text => text.IsVisible && text.Text == "下次提醒：7 月 8 日 09:02"),
                "#225 浮窗便签下次提醒应使用带中文空格的截止时刻格式。");
            Must(VisualDescendants<TextBlock>(center).Any(text => text.IsVisible && text.Text == "计划：2038 年 7 月 8 日 09:02（北京时间）") &&
                VisualDescendants<FrameworkElement>(center).Any(element =>
                    AutomationProperties.GetName(element).Contains("计划 2038 年 7 月 8 日 09:02 北京时间", StringComparison.Ordinal)),
                "#225 提醒中心可见计划与可访问名应使用同一完整北京时间。");
        }
        finally
        {
            center.RemoveEntry(key);
            center.Close();
            floating.Close();
        }
    }
}
