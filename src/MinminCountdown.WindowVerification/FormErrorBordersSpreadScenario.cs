using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #313：将 #303 的统一输入错误边框规则（出错无焦 1 DIP 危险色、出错有焦 2 DIP 危险色、
// 有效有焦 2 DIP 暖金焦点色、零布局位移、修正即清、提交失败聚焦首错、HelpText 同步、
// 原有播报不变、系统对比度可区分）铺到其余全部错误表单并产出次级界面实拍。
internal static class FormErrorBordersSpreadScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);
    private const double Tolerance = 0.05;

    private static string? _evidenceDirectory;
    private static bool _evidenceDirectoryInitialized;

    internal static (bool Passed, string Explanation) RunMainAndFloating(string meaning)
    {
        var failures = new List<string>();
        var capture = GetEvidenceWriter();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-313-main-floating-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var diagnosticPath = Path.Combine(tempRoot, "diagnostic.jsonl");
        var fileOps = new ToggleableFailureSettingsFileOperations();
        var announcements = new List<(string Id, string Text, AutomationLiveSetting Live)>();
        MainWindow? main = null;
        FloatingWindow? floating = null;

        void Observe(TextBlock block) => announcements.Add((
            ResolveAnnouncementId(block),
            AutomationProperties.GetName(block) ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));

        UiAccessibility.Announced += Observe;
        try
        {
            var clock = new FixedClock(FixtureUtc);
            var store = new SettingsStore(clock: clock, fileOperations: fileOps);
            var workspace = DesktopWorkspace.Load(settingsPath, store);
            var workTag = MustApply(workspace, new CreateTagChange("工作", "#F4B183")).CreatedTag!;
            _ = MustApply(workspace, new CreateTagChange("生活", "#E7F4E8")).CreatedTag!;
            var today = workspace.Snapshot.BeijingNow.Date;
            var editableNote = MustApply(workspace, new SaveNoteChange(
                null,
                "待编辑验证便签",
                today,
                false,
                new[] { workTag.Id },
                Array.Empty<ReminderRule>()));
            _ = editableNote;

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
            main = new MainWindow(workspace, calendar, new DiagnosticLogger(diagnosticPath), AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            ShowAndWaitFirstFrame(main);

            VerifyMainNoteEditor(main, today, announcements, capture, failures);
            VerifyMainDisplaySettings(main, announcements, capture, failures);
            VerifyMainTagManager(main, announcements, capture, failures);
            VerifyMainTagSelectors(main, fileOps, announcements, capture, failures);

            floating = new FloatingWindow(
                workspace,
                calendar,
                new StubManagerHost(),
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            ShowAndWaitFirstFrame(floating);

            VerifyFloatingQuickAddAndSearch(floating, today, announcements, capture, failures);
            VerifyFloatingTagSelector(floating, fileOps, announcements, capture, failures);
        }
        finally
        {
            UiAccessibility.Announced -= Observe;
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

            TryDeleteDirectory(tempRoot);
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败 {failures.Count} 项：{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunSecondaryWindows(string meaning)
    {
        var failures = new List<string>();
        var capture = GetEvidenceWriter();
        var announcements = new List<(string Id, string Text, AutomationLiveSetting Live)>();

        void Observe(TextBlock block) => announcements.Add((
            ResolveAnnouncementId(block),
            AutomationProperties.GetName(block) ?? string.Empty,
            AutomationProperties.GetLiveSetting(block)));

        UiAccessibility.Announced += Observe;
        try
        {
            VerifyRuleEditor(announcements, capture, failures);
            VerifyReminderCenterCustomSnooze(announcements, capture, failures);
        }
        finally
        {
            UiAccessibility.Announced -= Observe;
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；失败 {failures.Count} 项：{string.Join(" | ", failures)}");
    }

    private static void VerifyMainNoteEditor(
        MainWindow main,
        DateTime today,
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        var notesTab = Required<ToggleButton>(main, "NotesTab");
        Click(notesTab, main);
        var editorSurface = Required<FrameworkElement>(main, "NoteEditorSurface");
        var noteText = Required<TextBox>(main, "NoteText");
        var noteDate = Required<DatePicker>(main, "NoteDate");
        var noteTextError = Required<TextBlock>(main, "NoteTextError");
        var noteDateError = Required<TextBlock>(main, "NoteDateError");
        var noteMessage = Required<TextBlock>(main, "NoteMessage");
        var saveNote = Required<Button>(main, "SaveNoteButton");
        var cancelNote = Required<Button>(main, "CancelNoteEdit");

        // 1. 新增态：有效聚焦暖金边 + 零位移
        noteText.Text = "有效便签正文";
        noteDate.SelectedDate = today;
        Keyboard.Focus(saveNote);
        Settle(main);
        var resting = CaptureRects(editorSurface, noteText, noteDate, saveNote);

        Keyboard.Focus(noteText);
        Settle(main);
        RequireInputBorder(main, noteText, "MainActionFocusBrush", 2, "主窗便签正文有效聚焦", failures);
        RequireUnchangedRects(resting, CaptureRects(editorSurface, noteText, noteDate, saveNote), "主窗便签正文有效聚焦", failures);

        Keyboard.Focus(noteDate);
        Settle(main);
        RequireInputBorder(main, noteDate, "MainActionFocusBrush", 2, "主窗便签日期有效聚焦", failures);
        RequireUnchangedRects(resting, CaptureRects(editorSurface, noteText, noteDate, saveNote), "主窗便签日期有效聚焦", failures);

        // 2. 新增态双字段同时出错：首错聚焦（2 DIP 危险色），次错无焦（1 DIP 危险色），HelpText 同步，礼貌播报
        announcements.Clear();
        noteText.Text = string.Empty;
        noteDate.SelectedDate = null;
        Keyboard.Focus(saveNote);
        Settle(main);
        Click(saveNote, main);

        Check(failures, "便签新增双错时焦点落首个出错字段 NoteText", noteText.IsKeyboardFocusWithin);
        Check(failures, "便签新增双错时两个字段均置 HasError", InputErrorState.GetHasError(noteText) && InputErrorState.GetHasError(noteDate));
        RequireInputBorder(main, noteText, "DangerBrush", 2, "便签新增正文出错有焦点", failures);
        RequireInputBorder(main, noteDate, "DangerBrush", 1, "便签新增日期出错无焦点", failures);
        RequireAccessibleFieldError(noteTextError, noteText, "请先写下便签内容。", "便签新增正文", failures);
        RequireAccessibleFieldError(noteDateError, noteDate, "请选择便签日期，当前草稿已保留。", "便签新增日期", failures);
        Check(failures, "便签新增正文错误触发礼貌直播区播报",
            announcements.Any(a => a.Id == "NoteTextError" && a.Text == "请先写下便签内容。" && a.Live == AutomationLiveSetting.Polite));
        Check(failures, "便签新增日期错误触发礼貌直播区播报",
            announcements.Any(a => a.Id == "NoteDateError" && a.Text == "请选择便签日期，当前草稿已保留。" && a.Live == AutomationLiveSetting.Polite));
        capture?.Invoke(main, editorSurface, "main-note-add-error");

        // 切焦观察三态切换零位移
        var errorRects = CaptureRects(editorSurface, noteText, noteDate, saveNote);
        Keyboard.Focus(noteDate);
        Settle(main);
        RequireInputBorder(main, noteText, "DangerBrush", 1, "便签新增正文出错无焦点", failures);
        RequireInputBorder(main, noteDate, "DangerBrush", 2, "便签新增日期出错有焦点", failures);
        RequireUnchangedRects(errorRects, CaptureRects(editorSurface, noteText, noteDate, saveNote), "便签新增错态切焦", failures);

        // 修正正文仅清正文错误，保留日期错误；并验证系统对比度区分有效焦点与错误
        Keyboard.Focus(noteText);
        Settle(main);
        noteText.Text = "已修正便签正文";
        Settle(main);
        RequireClearedFieldError(noteText, noteTextError, "便签新增正文修正后", failures);
        RequireInputBorder(main, noteText, "MainActionFocusBrush", 2, "便签新增正文修正后有焦点", failures);
        Check(failures, "修正便签正文不应误清便签日期错误",
            noteDateError.IsVisible && InputErrorState.GetHasError(noteDate));
        RequireInputBorder(main, noteDate, "DangerBrush", 1, "便签新增正文修正后日期仍出错无焦点", failures);

        VerifyHighContrastDistinguishable(
            main,
            main,
            validFocused: noteText,
            erroredTarget: noteDate,
            captureTarget: editorSurface,
            captureLabel: "main-note-high-contrast-error",
            surfaceLabel: "主窗便签编辑",
            capture: capture,
            failures: failures);

        // 仅日期无效提交 -> 聚焦 NoteDate
        Keyboard.Focus(saveNote);
        Settle(main);
        Click(saveNote, main);
        Check(failures, "便签新增仅日期无效时聚焦 NoteDate", noteDate.IsKeyboardFocusWithin);
        RequireInputBorder(main, noteDate, "DangerBrush", 2, "便签新增仅日期出错有焦点", failures);
        Check(failures, "便签新增仅日期无效时 NoteText 无错误边", !InputErrorState.GetHasError(noteText));

        // 修正日期后立即清除所有错误与边界
        noteDate.SelectedDate = today;
        Settle(main);
        RequireClearedFieldError(noteDate, noteDateError, "便签新增日期修正后", failures);
        Check(failures, "便签新增全部修正后 NoteMessage 清除", string.IsNullOrEmpty(noteMessage.Text));
        RequireInputBorder(main, noteDate, "MainActionFocusBrush", 2, "便签新增日期修正后有焦点", failures);

        // 3. 编辑态：进入编辑已有带日期便签 -> 双错 -> 修正 -> 取消清理
        var activeRows = Required<Panel>(main, "ActiveNoteRows");
        var editButton = VisualDescendants<Button>(activeRows)
            .FirstOrDefault(button => Equals(button.Content, "编辑"));
        Check(failures, "便签列表存在可进入编辑态的“编辑”按钮", editButton is not null);
        if (editButton is null)
        {
            return;
        }

        Click(editButton, main);
        Check(failures, "点击编辑进入便签编辑态", cancelNote.Visibility == Visibility.Visible);
        noteText.Text = string.Empty;
        noteDate.SelectedDate = null;
        Keyboard.Focus(saveNote);
        Settle(main);
        Click(saveNote, main);

        Check(failures, "便签编辑态双错时聚焦 NoteText", noteText.IsKeyboardFocusWithin);
        RequireInputBorder(main, noteText, "DangerBrush", 2, "便签编辑态正文出错有焦点", failures);
        RequireInputBorder(main, noteDate, "DangerBrush", 1, "便签编辑态日期出错无焦点", failures);
        RequireAccessibleFieldError(noteTextError, noteText, "请先写下便签内容。", "便签编辑态正文", failures);
        RequireAccessibleFieldError(noteDateError, noteDate, "该便签已设置日期，不能清空日期保存，当前草稿已保留。", "便签编辑态日期", failures);

        noteText.Text = "编辑态修正正文";
        Settle(main);
        RequireClearedFieldError(noteText, noteTextError, "便签编辑态正文修正后", failures);
        Click(saveNote, main);
        Check(failures, "便签编辑态仅日期无效时聚焦 NoteDate", noteDate.IsKeyboardFocusWithin);
        RequireInputBorder(main, noteDate, "DangerBrush", 2, "便签编辑态仅日期出错有焦点", failures);

        Click(cancelNote, main);
        RequireClearedFieldError(noteText, noteTextError, "取消便签编辑后正文", failures);
        RequireClearedFieldError(noteDate, noteDateError, "取消便签编辑后日期", failures);
    }

    private static void VerifyMainDisplaySettings(
        MainWindow main,
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        var preferencesButton = Required<Button>(main, "PreferencesButton");
        var preferencesPopup = Required<Popup>(main, "PreferencesPopup");
        var preferencesSurface = Required<Border>(main, "CountdownPreferencesSurface");
        var salary = Required<TextBox>(main, "MonthlySalary");
        var workStart = Required<TextBox>(main, "WorkStart");
        var workEnd = Required<TextBox>(main, "WorkEnd");
        var salaryError = Required<TextBlock>(main, "MonthlySalaryError");
        var workStartError = Required<TextBlock>(main, "WorkStartError");
        var workEndError = Required<TextBlock>(main, "WorkEndError");
        var savePreferences = Required<Button>(main, "SavePreferencesButton");
        var statusText = Required<TextBlock>(main, "StatusText");

        Click(preferencesButton, main);
        WindowValidationInput.WaitUntil(main.Dispatcher, () => preferencesPopup.IsOpen && preferencesSurface.Opacity >= 0.99, 800);
        Settle(main);
        Check(failures, "显示设置弹层正常打开", preferencesPopup.IsOpen);
        if (!preferencesPopup.IsOpen)
        {
            return;
        }

        // 1. 三字段同时出错提交：全部挂错误边，首个出错字段（月薪）聚焦（2 DIP），其余两字段 1 DIP 无焦点
        announcements.Clear();
        salary.Text = "-50";
        workStart.Text = "25:00";
        workEnd.Text = "99:00";
        Keyboard.Focus(savePreferences);
        Settle(main);
        Click(savePreferences, main);

        Check(failures, "显示设置三字段同时无效提交时聚焦首错 MonthlySalary", salary.IsKeyboardFocusWithin);
        Check(failures, "显示设置三字段同时无效提交时三处均置 HasError",
            InputErrorState.GetHasError(salary) &&
            InputErrorState.GetHasError(workStart) &&
            InputErrorState.GetHasError(workEnd));
        RequireInputBorder(main, salary, "DangerBrush", 2, "显示设置月薪出错有焦点", failures);
        RequireInputBorder(main, workStart, "DangerBrush", 1, "显示设置上班时间出错无焦点", failures);
        RequireInputBorder(main, workEnd, "DangerBrush", 1, "显示设置下班时间出错无焦点", failures);
        RequireAccessibleFieldError(salaryError, salary, "月薪不能小于 0。", "显示设置月薪", failures);
        RequireAccessibleFieldError(workStartError, workStart, "上班时间请使用 HH:mm。", "显示设置上班时间", failures);
        RequireAccessibleFieldError(workEndError, workEnd, "下班时间请使用 HH:mm。", "显示设置下班时间", failures);
        Check(failures, "显示设置错误均触发礼貌播报",
            announcements.Any(a => a.Id == "MonthlySalaryError" && a.Live == AutomationLiveSetting.Polite) &&
            announcements.Any(a => a.Id == "WorkStartError" && a.Live == AutomationLiveSetting.Polite) &&
            announcements.Any(a => a.Id == "WorkEndError" && a.Live == AutomationLiveSetting.Polite));
        capture?.Invoke(main, preferencesSurface, "main-display-settings-multi-error");

        // 三错态切焦零位移
        var errorRects = CaptureRects(preferencesSurface, salary, workStart, workEnd);
        Keyboard.Focus(workStart);
        Settle(main);
        RequireInputBorder(main, salary, "DangerBrush", 1, "显示设置月薪切焦后无焦点", failures);
        RequireInputBorder(main, workStart, "DangerBrush", 2, "显示设置上班时间切焦后有焦点", failures);
        RequireUnchangedRects(errorRects, CaptureRects(preferencesSurface, salary, workStart, workEnd), "显示设置三错态切焦", failures);

        // 修正月薪立即清月薪错误，其余两字段保留错误；再提交聚焦首错 WorkStart
        salary.Text = "10000";
        Settle(main);
        RequireClearedFieldError(salary, salaryError, "显示设置月薪修正后", failures);
        Check(failures, "显示设置修正月薪不误清上班/下班错误",
            InputErrorState.GetHasError(workStart) && InputErrorState.GetHasError(workEnd));

        Keyboard.Focus(savePreferences);
        Settle(main);
        Click(savePreferences, main);
        Check(failures, "显示设置修正月薪后再提交聚焦首个剩余错误 WorkStart", workStart.IsKeyboardFocusWithin);
        RequireInputBorder(main, workStart, "DangerBrush", 2, "显示设置 WorkStart 首错有焦点", failures);
        RequireInputBorder(main, workEnd, "DangerBrush", 1, "显示设置 WorkEnd 次错无焦点", failures);

        // 系统对比度区分（有效聚焦月薪 vs 出错上班时间）
        Keyboard.Focus(salary);
        Settle(main);
        VerifyHighContrastDistinguishable(
            main,
            main,
            validFocused: salary,
            erroredTarget: workStart,
            captureTarget: null,
            captureLabel: null,
            surfaceLabel: "主窗显示设置",
            capture: null,
            failures: failures);

        // 全部修正后立即清除所有边界、错误文本、HelpText 及镜像底栏
        workStart.Text = "09:00";
        workEnd.Text = "18:00";
        Settle(main);
        RequireClearedFieldError(workStart, workStartError, "显示设置上班时间修正后", failures);
        RequireClearedFieldError(workEnd, workEndError, "显示设置下班时间修正后", failures);
        Check(failures, "显示设置全部修正后镜像底栏错误清除",
            !string.Equals(statusText.Text, "上班时间请使用 HH:mm。", StringComparison.Ordinal));

        PopupMotionBehavior.Close(preferencesPopup, immediate: true);
        Settle(main);
    }

    private static void VerifyMainTagManager(
        MainWindow main,
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        var trigger = Required<Button>(main, "TagManagerButton");
        var popup = Required<Popup>(main, "TagManagerPopup");
        var surface = popup.Child as Border ?? throw new InvalidOperationException("TagManagerPopup 缺少表面 Border。");
        var newTagInput = Required<TextBox>(main, "NewTagInput");
        var createTagButton = Required<Button>(main, "CreateTagButton");
        var tagMessage = Required<TextBlock>(main, "TagManagerMessage");
        var rows = Required<Panel>(main, "TagManagerRows");

        Click(trigger, main);
        WindowValidationInput.WaitUntil(main.Dispatcher, () => popup.IsOpen && newTagInput.IsKeyboardFocusWithin && surface.Opacity >= 0.99, 800);
        Settle(main);
        Check(failures, "标签管理弹层正常打开并聚焦 NewTagInput", popup.IsOpen && newTagInput.IsKeyboardFocusWithin);
        if (!popup.IsOpen)
        {
            return;
        }

        // 1. NewTagInput 有效聚焦暖金边 + 零位移
        var resting = CaptureRects(surface, newTagInput, createTagButton);
        RequireInputBorder(main, newTagInput, "MainActionFocusBrush", 2, "标签管理新标签有效聚焦", failures);

        // 2. 点击「新建」提交空名 -> 焦点回到 NewTagInput，2 DIP 危险边，HelpText 同步，礼貌播报
        announcements.Clear();
        newTagInput.Text = string.Empty;
        Keyboard.Focus(createTagButton);
        Settle(main);
        Click(createTagButton, main);

        Check(failures, "NewTagInput 空名点击新建失败后焦点回到 NewTagInput", newTagInput.IsKeyboardFocusWithin);
        Check(failures, "NewTagInput 空名失败置 HasError", InputErrorState.GetHasError(newTagInput));
        RequireInputBorder(main, newTagInput, "DangerBrush", 2, "NewTagInput 空名出错有焦点", failures);
        Check(failures, "NewTagInput 空名错误文案与 HelpText 同步",
            string.Equals(tagMessage.Text, "请输入标签名称。", StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetHelpText(newTagInput), "请输入标签名称。", StringComparison.Ordinal));
        Check(failures, "TagManagerMessage 触发礼貌直播区播报",
            announcements.Any(a => a.Id == "TagManagerMessage" && a.Text == "请输入标签名称。" && a.Live == AutomationLiveSetting.Polite));

        // 移焦到「新建」按钮 -> NewTagInput 变为 1 DIP 无焦点危险边，零位移
        Keyboard.Focus(createTagButton);
        Settle(main);
        RequireInputBorder(main, newTagInput, "DangerBrush", 1, "NewTagInput 出错无焦点", failures);
        RequireUnchangedRects(resting, CaptureRects(surface, newTagInput, createTagButton), "NewTagInput 出错无焦点", failures);

        // 3. 重名提交（Enter 与按钮点击）
        newTagInput.Text = "工作";
        Settle(main);
        Check(failures, "修改 NewTagInput 后空名错误与边界立即清除",
            !InputErrorState.GetHasError(newTagInput) &&
            string.IsNullOrEmpty(AutomationProperties.GetHelpText(newTagInput)) &&
            string.IsNullOrEmpty(tagMessage.Text));

        Keyboard.Focus(createTagButton);
        Settle(main);
        Click(createTagButton, main);
        Check(failures, "NewTagInput 重名提交失败聚焦 NewTagInput 并置错误边",
            newTagInput.IsKeyboardFocusWithin &&
            InputErrorState.GetHasError(newTagInput) &&
            tagMessage.Text.Contains("重复", StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetHelpText(newTagInput), tagMessage.Text, StringComparison.Ordinal));
        RequireInputBorder(main, newTagInput, "DangerBrush", 2, "NewTagInput 重名出错有焦点", failures);
        capture?.Invoke(main, surface, "main-tag-manager-new-tag-error");

        // 高对比度检查：行内改名有效聚焦 vs NewTagInput 出错无焦点
        var workRow = rows.Children.OfType<Grid>()
            .First(row => row.Children.OfType<TextBox>().Any(box => string.Equals(box.Text, "工作", StringComparison.Ordinal)));
        var workRowInput = workRow.Children.OfType<TextBox>().Single();
        var workRowError = workRow.Children.OfType<TextBlock>().Single();
        Keyboard.Focus(workRowInput);
        Settle(main);
        VerifyHighContrastDistinguishable(
            main,
            main,
            validFocused: workRowInput,
            erroredTarget: newTagInput,
            captureTarget: null,
            captureLabel: null,
            surfaceLabel: "主窗标签管理新标签",
            capture: null,
            failures: failures);

        // 修正 NewTagInput 立即清错
        newTagInput.Text = "工作新名";
        Settle(main);
        Check(failures, "修正 NewTagInput 文本后立即清除重名错误与边框",
            !InputErrorState.GetHasError(newTagInput) &&
            string.IsNullOrEmpty(AutomationProperties.GetHelpText(newTagInput)) &&
            string.IsNullOrEmpty(tagMessage.Text));

        // 4. 行内改名错误边框三态 + 零位移 + 修正即清 + Esc 清错
        Keyboard.Focus(workRowInput);
        Settle(main);
        var rowResting = CaptureRects(workRow, workRowInput);
        RequireInputBorder(main, workRowInput, "MainActionFocusBrush", 2, "标签管理行输入有效聚焦", failures);

        workRowInput.Text = "生活";
        WindowValidationInput.RaisePreviewKey(workRowInput, Key.Enter);
        Settle(main);
        Check(failures, "标签管理行重名失败聚焦本行并置错误边",
            workRowInput.IsKeyboardFocusWithin &&
            InputErrorState.GetHasError(workRowInput) &&
            workRowError.IsVisible &&
            string.Equals(AutomationProperties.GetHelpText(workRowInput), workRowError.Text, StringComparison.Ordinal));
        RequireInputBorder(main, workRowInput, "DangerBrush", 2, "标签管理行出错有焦点", failures);
        RequireUnchangedRects(rowResting, CaptureRects(workRow, workRowInput), "标签管理行出错有焦点", failures);

        workRowInput.Text = "工作临时";
        Settle(main);
        RequireClearedFieldError(workRowInput, workRowError, "标签管理行修正后", failures);
        RequireInputBorder(main, workRowInput, "MainActionFocusBrush", 2, "标签管理行修正后有焦点", failures);

        // 再次触发 NewTagInput 错误并按 Esc 关闭 -> 再次打开无残留错误
        newTagInput.Text = string.Empty;
        Click(createTagButton, main);
        Check(failures, "关闭前 NewTagInput 处于错误态", InputErrorState.GetHasError(newTagInput));
        WindowValidationInput.RaisePreviewKey(newTagInput, Key.Escape);
        WindowValidationInput.WaitUntil(main.Dispatcher, () => !popup.IsOpen, 800);
        Settle(main);
        Check(failures, "Esc 关闭标签管理后清除 NewTagInput 错误边框与 HelpText",
            !InputErrorState.GetHasError(newTagInput) &&
            string.IsNullOrEmpty(AutomationProperties.GetHelpText(newTagInput)));
    }

    private static void VerifyMainTagSelectors(
        MainWindow main,
        ToggleableFailureSettingsFileOperations fileOps,
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        // NoteTags（当前处于便签页）与 CountdownTags（倒计时页）共用 TagSelector，逐一核验。
        var noteTags = Required<TagSelector>(main, "NoteTags");
        VerifyTagSelectorErrorFlow(
            main,
            main,
            noteTags,
            fileOps,
            announcements,
            "主窗便签标签选择器",
            "main-tag-selector-error",
            capture,
            failures);

        var countdownTab = Required<ToggleButton>(main, "CountdownTab");
        Click(countdownTab, main);
        var expand = Required<Button>(main, "CountdownEditorExpandButton");
        var countdownTags = Required<TagSelector>(main, "CountdownTags");
        if (!countdownTags.IsVisible)
        {
            Click(expand, main);
        }

        VerifyTagSelectorErrorFlow(
            main,
            main,
            countdownTags,
            fileOps,
            announcements,
            "主窗倒计时标签选择器",
            captureLabel: null,
            capture: null,
            failures: failures);
    }

    private static void VerifyFloatingTagSelector(
        FloatingWindow floating,
        ToggleableFailureSettingsFileOperations fileOps,
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        var notesButton = Required<ToggleButton>(floating, "FloatingNotesButton");
        if (notesButton.IsChecked != true)
        {
            Click(notesButton, floating);
        }

        var entry = Required<Border>(floating, "FloatingNoteEntry");
        var toggle = Required<Button>(floating, "FloatingAddNoteToggle");
        if (entry.Visibility != Visibility.Visible)
        {
            Click(toggle, floating);
        }

        var floatingTags = Required<TagSelector>(floating, "FloatingTags");
        VerifyTagSelectorErrorFlow(
            floating,
            floating,
            floatingTags,
            fileOps,
            announcements,
            "悬浮窗快速新增标签选择器",
            "floating-tag-selector-error",
            capture,
            failures);
    }

    private static void VerifyTagSelectorErrorFlow(
        Window hostWindow,
        FrameworkElement resourceOwner,
        TagSelector selector,
        ToggleableFailureSettingsFileOperations fileOps,
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        string label,
        string? captureLabel,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        Click(selector.PopupTrigger, hostWindow);
        WindowValidationInput.WaitUntil(
            hostWindow.Dispatcher,
            () => selector.IsPopupOpen &&
                  selector.PopupInitialFocus.IsKeyboardFocusWithin &&
                  (selector.Popup.Child is not FrameworkElement child || child.Opacity >= 0.99),
            800);
        Settle(hostWindow);
        Check(failures, $"{label} 弹层打开并聚焦搜索输入框", selector.IsPopupOpen && selector.PopupInitialFocus.IsKeyboardFocusWithin);
        if (!selector.IsPopupOpen)
        {
            return;
        }

        var surface = selector.Popup.Child as Border
            ?? throw new InvalidOperationException($"{label} Popup 缺少表面 Border。");
        var search = selector.PopupInitialFocus;
        var create = LogicalDescendants<Button>(surface).Single();
        var error = FindByAutomationId<TextBlock>(surface, "TagSelectorCreateError");

        var resting = CaptureRects(surface, search);
        RequireInputBorder(resourceOwner, search, "MainActionFocusBrush", 2, $"{label} 搜索框有效聚焦", failures);

        announcements.Clear();
        search.Text = "写入受限标签";
        Settle(hostWindow);
        Check(failures, $"{label} 输入新标签名后显示“新建标签”按钮", create.Visibility == Visibility.Visible);

        fileOps.FailWrites = true;
        try
        {
            Keyboard.Focus(create);
            Settle(hostWindow);
            Click(create, hostWindow);

            Check(failures, $"{label} 创建失败后焦点回到搜索输入框", search.IsKeyboardFocusWithin);
            Check(failures, $"{label} 创建失败置 HasError", InputErrorState.GetHasError(search));
            RequireInputBorder(resourceOwner, search, "DangerBrush", 2, $"{label} 出错有焦点", failures);
            Check(failures, $"{label} 错误行可见且 HelpText 同步",
                error.IsVisible &&
                !string.IsNullOrWhiteSpace(error.Text) &&
                string.Equals(AutomationProperties.GetHelpText(search), error.Text, StringComparison.Ordinal) &&
                AutomationProperties.GetLiveSetting(error) == AutomationLiveSetting.Polite);
            Check(failures, $"{label} 错误行触发礼貌直播区播报",
                announcements.Any(a => a.Id == "TagSelectorCreateError" && a.Text == error.Text && a.Live == AutomationLiveSetting.Polite));
            if (captureLabel is not null)
            {
                capture?.Invoke(hostWindow, surface, captureLabel);
            }

            // 移焦到创建按钮 -> 1 DIP 无焦点危险边，且输入框几何不被边框厚度挤动
            var erroredRects = CaptureRects(surface, search);
            Keyboard.Focus(create);
            Settle(hostWindow);
            RequireInputBorder(resourceOwner, search, "DangerBrush", 1, $"{label} 出错无焦点", failures);
            RequireUnchangedRects(erroredRects, CaptureRects(surface, search), $"{label} 错态切焦", failures);

            // 高对比度下错误与有效焦点可区分
            using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
            {
                contrast.ChangeColors(HighContrastPalette);
                Settle(hostWindow);
                RequireInputBorderColor(search, Colors.White, 1, $"{label} 高对比度出错无焦点", failures);
                Keyboard.Focus(search);
                Settle(hostWindow);
                RequireInputBorderColor(search, Colors.White, 2, $"{label} 高对比度出错有焦点", failures);

                // 修正输入立即清除错误、边框和 HelpText，高对比度下切回 Highlight 2 DIP
                search.Text = "修正后标签";
                Settle(hostWindow);
                RequireClearedFieldError(search, error, $"{label} 修正输入后", failures);
                RequireInputBorderColor(search, Colors.Yellow, 2, $"{label} 高对比度修正后有效焦点", failures);
            }
            Settle(hostWindow);
            RequireInputBorder(resourceOwner, search, "MainActionFocusBrush", 2, $"{label} 恢复日间有效焦点", failures);
            RequireUnchangedRects(resting, CaptureRects(surface, search), $"{label} 恢复常态", failures);
        }
        finally
        {
            fileOps.FailWrites = false;
            PopupMotionBehavior.Close(selector.Popup, immediate: true);
            Settle(hostWindow);
        }
    }

    private static void VerifyFloatingQuickAddAndSearch(
        FloatingWindow floating,
        DateTime today,
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        var root = (FrameworkElement)floating.Content;
        var noteEntry = Required<Border>(floating, "FloatingNoteEntry");
        var notesButton = Required<ToggleButton>(floating, "FloatingNotesButton");
        var toggle = Required<Button>(floating, "FloatingAddNoteToggle");
        var noteText = Required<TextBox>(floating, "FloatingNoteText");
        var noteDate = Required<DatePicker>(floating, "FloatingNoteDate");
        var noteTextError = Required<TextBlock>(floating, "FloatingNoteTextError");
        var noteDateError = Required<TextBlock>(floating, "FloatingNoteDateError");
        var noteMessage = Required<TextBlock>(floating, "FloatingNoteMessage");
        var addButton = Required<Button>(floating, "FloatingAddNote");
        var search = Required<TextBox>(floating, "FloatingNoteSearch");

        Click(notesButton, floating);
        Click(toggle, floating);

        // 1. 快速新增有效聚焦暖金边 + 零位移（输入控件相对滚动内容宿主 FloatingNoteEntry 测量，排除 ScrollViewer 自动滚入视口偏移）
        noteText.Text = "有效悬浮便签";
        noteDate.SelectedDate = today;
        Keyboard.Focus(addButton);
        Settle(floating);
        var restingFields = CaptureRects(noteEntry, noteText, noteDate);
        var restingAction = CaptureRects(root, addButton);

        Keyboard.Focus(noteText);
        Settle(floating);
        RequireInputBorder(floating, noteText, "MainActionFocusBrush", 2, "悬浮快速新增正文有效聚焦", failures);
        RequireUnchangedRects(restingFields, CaptureRects(noteEntry, noteText, noteDate), "悬浮快速新增正文有效聚焦", failures);
        RequireUnchangedRects(restingAction, CaptureRects(root, addButton), "悬浮快速新增正文有效聚焦动作栏", failures);

        Keyboard.Focus(noteDate);
        Settle(floating);
        RequireInputBorder(floating, noteDate, "MainActionFocusBrush", 2, "悬浮快速新增日期有效聚焦", failures);
        RequireUnchangedRects(restingFields, CaptureRects(noteEntry, noteText, noteDate), "悬浮快速新增日期有效聚焦", failures);
        RequireUnchangedRects(restingAction, CaptureRects(root, addButton), "悬浮快速新增日期有效聚焦动作栏", failures);

        // 2. 双字段同时出错提交：首个出错字段 FloatingNoteText 聚焦 2 DIP，FloatingNoteDate 无焦点 1 DIP
        announcements.Clear();
        noteText.Text = string.Empty;
        noteDate.SelectedDate = null;
        Keyboard.Focus(addButton);
        Settle(floating);
        Click(addButton, floating);

        Check(failures, "悬浮快速新增双错时焦点落首错 FloatingNoteText", noteText.IsKeyboardFocusWithin);
        Check(failures, "悬浮快速新增双错时两个字段均置 HasError",
            InputErrorState.GetHasError(noteText) && InputErrorState.GetHasError(noteDate));
        RequireInputBorder(floating, noteText, "DangerBrush", 2, "悬浮快速新增正文出错有焦点", failures);
        RequireInputBorder(floating, noteDate, "DangerBrush", 1, "悬浮快速新增日期出错无焦点", failures);
        RequireAccessibleFieldError(noteTextError, noteText, "请先写下便签内容。", "悬浮快速新增正文", failures);
        RequireAccessibleFieldError(noteDateError, noteDate, "请选择便签日期。", "悬浮快速新增日期", failures);
        Check(failures, "悬浮快速新增两处错误行触发礼貌直播区播报",
            announcements.Any(a => a.Id == "FloatingNoteTextError" && a.Text == "请先写下便签内容。" && a.Live == AutomationLiveSetting.Polite) &&
            announcements.Any(a => a.Id == "FloatingNoteDateError" && a.Text == "请选择便签日期。" && a.Live == AutomationLiveSetting.Polite));
        capture?.Invoke(floating, root, "floating-quick-add-error");

        // 切焦观察 1/2 DIP 切换与零位移
        var errorRects = CaptureRects(noteEntry, noteText, noteDate);
        Keyboard.Focus(noteDate);
        Settle(floating);
        RequireInputBorder(floating, noteText, "DangerBrush", 1, "悬浮快速新增正文切焦后无焦点", failures);
        RequireInputBorder(floating, noteDate, "DangerBrush", 2, "悬浮快速新增日期切焦后有焦点", failures);
        RequireUnchangedRects(errorRects, CaptureRects(noteEntry, noteText, noteDate), "悬浮快速新增错态切焦", failures);

        // 修正正文仅清正文错误，保留日期错误；验证高对比度区分有效焦点与错误
        Keyboard.Focus(noteText);
        Settle(floating);
        noteText.Text = "修正后悬浮便签";
        Settle(floating);
        RequireClearedFieldError(noteText, noteTextError, "悬浮快速新增正文修正后", failures);
        Check(failures, "悬浮快速新增修正正文不误清日期错误",
            noteDateError.IsVisible && InputErrorState.GetHasError(noteDate));

        VerifyHighContrastDistinguishable(
            floating,
            floating,
            validFocused: noteText,
            erroredTarget: noteDate,
            captureTarget: root,
            captureLabel: "floating-quick-add-high-contrast-error",
            surfaceLabel: "悬浮窗快速新增",
            capture: capture,
            failures: failures);

        // 仅日期无效提交 -> 聚焦 FloatingNoteDate
        Keyboard.Focus(addButton);
        Settle(floating);
        Click(addButton, floating);
        Check(failures, "悬浮快速新增仅日期无效时聚焦 FloatingNoteDate", noteDate.IsKeyboardFocusWithin);
        RequireInputBorder(floating, noteDate, "DangerBrush", 2, "悬浮快速新增仅日期出错有焦点", failures);

        // 修正日期后立即清错
        noteDate.SelectedDate = today;
        Settle(floating);
        RequireClearedFieldError(noteDate, noteDateError, "悬浮快速新增日期修正后", failures);
        Check(failures, "悬浮快速新增全部修正后 FloatingNoteMessage 清除", string.IsNullOrEmpty(noteMessage.Text));

        // 3. 审计悬浮搜索 FloatingNoteSearch：按设计无校验错误态，仅保持共享暖金 2 DIP 焦点边、零位移与高对比度 Highlight
        noteText.Text = string.Empty;
        Click(toggle, floating);
        Check(failures, "悬浮窗 Ctrl+F 展开搜索", WindowValidationInput.RaisePreviewKey(floating, Key.F, ModifierKeys.Control));
        WindowValidationInput.WaitUntil(floating.Dispatcher, () => search.IsVisible && search.IsKeyboardFocusWithin, 800);
        Settle(floating);
        var searchFocusedRects = CaptureRects(root, search);
        RequireInputBorder(floating, search, "MainActionFocusBrush", 2, "悬浮搜索框有效聚焦", failures);
        Check(failures, "悬浮搜索框常态无错误态", !InputErrorState.GetHasError(search));
        search.Text = "无匹配关键词XYZ";
        Settle(floating);
        Check(failures, "悬浮搜索无匹配为列表空态而非输入错误态", !InputErrorState.GetHasError(search));
        Keyboard.Focus(toggle);
        Settle(floating);
        RequireUnchangedRects(searchFocusedRects, CaptureRects(root, search), "悬浮搜索框失焦", failures);
        Keyboard.Focus(search);
        Settle(floating);
        using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            contrast.ChangeColors(HighContrastPalette);
            Settle(floating);
            RequireInputBorderColor(search, Colors.Yellow, 2, "悬浮搜索框高对比度有效焦点", failures);
        }
        Settle(floating);
        WindowValidationInput.RaisePreviewKey(floating, Key.Escape);
        Settle(floating);
    }

    private static void VerifyRuleEditor(
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        var clock = new FixedClock(FixtureUtc);
        var targetUtc = FixtureUtc.AddDays(30);
        var editor = new ReminderRulesEditorWindow(
            Array.Empty<ReminderRule>(),
            clock,
            countdownTargetUtc: targetUtc)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 0
        };

        try
        {
            ShowAndWaitFirstFrame(editor);
            var root = (FrameworkElement)editor.Content;
            var mode = FindByAutomationId<ComboBox>(editor, "ReminderRuleMode");
            var firstDueDate = FindByAutomationId<DatePicker>(editor, "ReminderRuleFirstDueDate");
            var firstDueTime = FindByAutomationId<TextBox>(editor, "ReminderRuleFirstDueTime");
            var interval = FindByAutomationId<ComboBox>(editor, "ReminderRuleInterval");
            var customInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCustomInterval");
            var relativeOffset = FindByAutomationId<TextBox>(editor, "ReminderRuleRelativeOffsetMinutes");
            var calendarFrequency = FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarFrequency");
            var calendarInterval = FindByAutomationId<TextBox>(editor, "ReminderRuleCalendarInterval");
            var calendarMonth = FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarMonth");
            var calendarDay = FindByAutomationId<ComboBox>(editor, "ReminderRuleCalendarDay");
            var calendarLastDay = FindByAutomationId<CheckBox>(editor, "ReminderRuleCalendarLastDay");
            var saveRule = FindByAutomationId<Button>(editor, "SaveReminderRuleButton");
            var cancelDraft = FindByAutomationId<Button>(editor, "CancelReminderRulesButton");

            var modeError = FindByAutomationId<TextBlock>(editor, "ReminderRuleModeError");
            var firstDueError = FindByAutomationId<TextBlock>(editor, "ReminderRuleFirstDueError");
            var intervalError = FindByAutomationId<TextBlock>(editor, "ReminderRuleIntervalError");
            var relativeError = FindByAutomationId<TextBlock>(editor, "ReminderRuleRelativeOffsetError");
            var calendarIntervalError = FindByAutomationId<TextBlock>(editor, "ReminderRuleCalendarIntervalError");
            var calendarSelectorError = FindByAutomationId<TextBlock>(editor, "ReminderRuleCalendarSelectorError");

            var futureBeijing = ChinaStandardTime.ToBeijingTime(FixtureUtc.AddDays(5));
            var pastBeijing = ChinaStandardTime.ToBeijingTime(FixtureUtc.AddDays(-1));

            // 1) _mode（ReminderRuleMode）
            announcements.Clear();
            mode.SelectedIndex = -1;
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            VerifySingleControlErrorCycle(
                editor, root, mode, modeError, saveRule, "规则编辑器 Mode",
                fix: () => mode.SelectedIndex = 0,
                announcements: announcements,
                expectedErrorId: "ReminderRuleModeError",
                failures: failures);

            // 2) _firstDueDate & _firstDueTime（单错日期、单错时间、双错过去时间）
            firstDueDate.SelectedDate = null;
            firstDueTime.Text = "10:30";
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            Check(failures, "规则编辑器仅首次日期为空时仅日期出错且聚焦日期",
                firstDueDate.IsKeyboardFocusWithin &&
                InputErrorState.GetHasError(firstDueDate) &&
                !InputErrorState.GetHasError(firstDueTime) &&
                firstDueError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(firstDueDate), firstDueError.Text, StringComparison.Ordinal) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(firstDueTime)));
            RequireInputBorder(editor, firstDueDate, "DangerBrush", 2, "规则编辑器首次日期出错有焦点", failures);
            firstDueDate.SelectedDate = futureBeijing.Date;
            Settle(editor);
            RequireClearedFieldError(firstDueDate, firstDueError, "规则编辑器首次日期修正后", failures);

            firstDueTime.Text = "25:00";
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            Check(failures, "规则编辑器仅首次时刻非法时仅时刻出错且聚焦时刻",
                firstDueTime.IsKeyboardFocusWithin &&
                InputErrorState.GetHasError(firstDueTime) &&
                !InputErrorState.GetHasError(firstDueDate) &&
                firstDueError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(firstDueTime), firstDueError.Text, StringComparison.Ordinal) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(firstDueDate)));
            RequireInputBorder(editor, firstDueTime, "DangerBrush", 2, "规则编辑器首次时刻出错有焦点", failures);
            firstDueTime.Text = "10:30";
            Settle(editor);
            RequireClearedFieldError(firstDueTime, firstDueError, "规则编辑器首次时刻修正后", failures);

            // 首次日期与时刻双错：先修正日期 -> 日期错误边清零且时刻保留错误边与 HelpText；再修正时刻 -> 两者均清；反序亦然
            firstDueDate.SelectedDate = null;
            firstDueTime.Text = "25:00";
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            Check(failures, "规则编辑器首次日期与时刻双错时聚焦首错日期并双挂错误边",
                firstDueDate.IsKeyboardFocusWithin &&
                InputErrorState.GetHasError(firstDueDate) &&
                InputErrorState.GetHasError(firstDueTime) &&
                firstDueError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(firstDueDate), firstDueError.Text, StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(firstDueTime), firstDueError.Text, StringComparison.Ordinal));

            firstDueDate.SelectedDate = futureBeijing.Date;
            Settle(editor);
            Check(failures, "规则编辑器首次双错仅修正日期后清除日期错误边并保留时刻错误边与 HelpText",
                !InputErrorState.GetHasError(firstDueDate) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(firstDueDate)) &&
                InputErrorState.GetHasError(firstDueTime) &&
                firstDueError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(firstDueTime), firstDueError.Text, StringComparison.Ordinal));
            firstDueTime.Text = "10:30";
            Settle(editor);
            RequireClearedFieldError(firstDueTime, firstDueError, "规则编辑器首次双错补修时刻后", failures);

            firstDueDate.SelectedDate = null;
            firstDueTime.Text = "25:00";
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            firstDueTime.Text = "10:30";
            Settle(editor);
            Check(failures, "规则编辑器首次双错仅修正时刻后清除时刻错误边并保留日期错误边与 HelpText",
                !InputErrorState.GetHasError(firstDueTime) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(firstDueTime)) &&
                InputErrorState.GetHasError(firstDueDate) &&
                firstDueError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(firstDueDate), firstDueError.Text, StringComparison.Ordinal));
            firstDueDate.SelectedDate = futureBeijing.Date;
            Settle(editor);
            RequireClearedFieldError(firstDueDate, firstDueError, "规则编辑器首次双错补修日期后", failures);

            // 过去时间：日期与时刻双错，提交聚焦首错日期（2 DIP），时刻无焦点（1 DIP），切焦零位移
            firstDueDate.SelectedDate = pastBeijing.Date;
            firstDueTime.Text = pastBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            Check(failures, "规则编辑器过去时间双错时聚焦首错日期并双挂错误边",
                firstDueDate.IsKeyboardFocusWithin &&
                InputErrorState.GetHasError(firstDueDate) &&
                InputErrorState.GetHasError(firstDueTime));
            RequireInputBorder(editor, firstDueDate, "DangerBrush", 2, "规则编辑器过去时间日期出错有焦点", failures);
            RequireInputBorder(editor, firstDueTime, "DangerBrush", 1, "规则编辑器过去时间时刻出错无焦点", failures);
            capture?.Invoke(editor, root, "rule-editor-first-due-error");
            var firstDueRects = CaptureRects(root, firstDueDate, firstDueTime);
            Keyboard.Focus(firstDueTime);
            Settle(editor);
            RequireInputBorder(editor, firstDueDate, "DangerBrush", 1, "规则编辑器过去时间日期切焦后无焦点", failures);
            RequireInputBorder(editor, firstDueTime, "DangerBrush", 2, "规则编辑器过去时间时刻切焦后有焦点", failures);
            RequireUnchangedRects(firstDueRects, CaptureRects(root, firstDueDate, firstDueTime), "规则编辑器首次提醒错态切焦", failures);
            firstDueDate.SelectedDate = futureBeijing.Date;
            Settle(editor);
            Check(failures, "规则编辑器修正首次日期后双边框均立即清除",
                !firstDueError.IsVisible &&
                !InputErrorState.GetHasError(firstDueDate) &&
                !InputErrorState.GetHasError(firstDueTime));

            // 3) _interval & _customInterval
            mode.SelectedIndex = 1;
            Settle(editor);
            announcements.Clear();
            interval.SelectedIndex = -1;
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            VerifySingleControlErrorCycle(
                editor, root, interval, intervalError, saveRule, "规则编辑器固定间隔下拉",
                fix: () => interval.SelectedIndex = 1,
                announcements: announcements,
                expectedErrorId: "ReminderRuleIntervalError",
                failures: failures);

            interval.SelectedIndex = 3;
            Settle(editor);
            announcements.Clear();
            customInterval.Text = "0";
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            VerifySingleControlErrorCycle(
                editor, root, customInterval, intervalError, saveRule, "规则编辑器自定义间隔",
                fix: () => customInterval.Text = "25",
                announcements: announcements,
                expectedErrorId: "ReminderRuleIntervalError",
                failures: failures);

            // 4) _relativeOffset
            mode.SelectedIndex = 2;
            Settle(editor);
            announcements.Clear();
            relativeOffset.Text = "-5";
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            VerifySingleControlErrorCycle(
                editor, root, relativeOffset, relativeError, saveRule, "规则编辑器提前分钟",
                fix: () => relativeOffset.Text = "15",
                announcements: announcements,
                expectedErrorId: "ReminderRuleRelativeOffsetError",
                failures: failures);

            // 5) 日历模式全部字段：_calendarFrequency, _calendarInterval, _calendarDay(月), _calendarMonth & _calendarDay(年)
            mode.SelectedIndex = 3;
            firstDueDate.SelectedDate = futureBeijing.Date;
            firstDueTime.Text = "09:30";
            Settle(editor);

            // 5a) _calendarFrequency 非法 (-1)
            announcements.Clear();
            calendarFrequency.SelectedIndex = -1;
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            VerifySingleControlErrorCycle(
                editor, root, calendarFrequency, calendarSelectorError, saveRule, "规则编辑器重复周期",
                fix: () => calendarFrequency.SelectedIndex = 0,
                announcements: announcements,
                expectedErrorId: "ReminderRuleCalendarSelectorError",
                failures: failures);

            // 5b) _calendarFrequency 每天重复日程冲突 -> 聚焦 _calendarFrequency 并挂错误边
            calendarFrequency.SelectedIndex = 0;
            calendarInterval.Text = "1";
            firstDueDate.SelectedDate = futureBeijing.Date;
            firstDueTime.Text = "09:30";
            Settle(editor);
            InvokeCommandButton(saveRule, editor); // 先成功添加一条每天 09:30 规则
            mode.SelectedIndex = 3;
            calendarFrequency.SelectedIndex = 0;
            calendarInterval.Text = "1";
            firstDueDate.SelectedDate = futureBeijing.Date;
            firstDueTime.Text = "09:30";
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor); // 添加重复每天规则
            Check(failures, "规则编辑器每天重复日程冲突时聚焦重复周期并置 HasError",
                calendarFrequency.IsKeyboardFocusWithin &&
                InputErrorState.GetHasError(calendarFrequency) &&
                calendarSelectorError.IsVisible &&
                calendarSelectorError.Text.Contains("重复", StringComparison.Ordinal));
            RequireInputBorder(editor, calendarFrequency, "DangerBrush", 2, "规则编辑器每天重复冲突有焦点", failures);
            calendarInterval.Text = "2";
            Settle(editor);
            Check(failures, "规则编辑器修改周期数后清除每天重复冲突错误与边界",
                !calendarSelectorError.IsVisible && !InputErrorState.GetHasError(calendarFrequency));

            // 5c) _calendarInterval 非法 ("0")
            announcements.Clear();
            calendarInterval.Text = "0";
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            VerifySingleControlErrorCycle(
                editor, root, calendarInterval, calendarIntervalError, saveRule, "规则编辑器每隔几个周期",
                fix: () => calendarInterval.Text = "2",
                announcements: announcements,
                expectedErrorId: "ReminderRuleCalendarIntervalError",
                failures: failures);

            // 5d) 按月重复 _calendarDay 未选
            calendarFrequency.SelectedIndex = 2;
            Settle(editor);
            calendarLastDay.IsChecked = false;
            calendarDay.SelectedIndex = -1;
            announcements.Clear();
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            VerifySingleControlErrorCycle(
                editor, root, calendarDay, calendarSelectorError, saveRule, "规则编辑器按月具体日期",
                fix: () => calendarDay.SelectedIndex = 4,
                announcements: announcements,
                expectedErrorId: "ReminderRuleCalendarSelectorError",
                failures: failures);

            // 5e) 按年重复：月份与日期双错 -> 聚焦首错月份（2 DIP），日期无焦点（1 DIP），切焦零位移，高对比度区分
            calendarFrequency.SelectedIndex = 3;
            Settle(editor);
            calendarLastDay.IsChecked = false;
            calendarMonth.SelectedIndex = -1;
            calendarDay.SelectedIndex = -1;
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            Check(failures, "规则编辑器按年双错时聚焦首错月份并双挂错误边",
                calendarMonth.IsKeyboardFocusWithin &&
                InputErrorState.GetHasError(calendarMonth) &&
                InputErrorState.GetHasError(calendarDay) &&
                calendarSelectorError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(calendarMonth), calendarSelectorError.Text, StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(calendarDay), calendarSelectorError.Text, StringComparison.Ordinal));
            RequireInputBorder(editor, calendarMonth, "DangerBrush", 2, "规则编辑器按年月份出错有焦点", failures);
            RequireInputBorder(editor, calendarDay, "DangerBrush", 1, "规则编辑器按年日期出错无焦点", failures);
            capture?.Invoke(editor, root, "rule-editor-calendar-yearly-error");

            var yearlyRects = CaptureRects(root, calendarMonth, calendarDay);
            Keyboard.Focus(calendarDay);
            Settle(editor);
            RequireInputBorder(editor, calendarMonth, "DangerBrush", 1, "规则编辑器按年月份切焦无焦点", failures);
            RequireInputBorder(editor, calendarDay, "DangerBrush", 2, "规则编辑器按年日期切焦有焦点", failures);
            RequireUnchangedRects(yearlyRects, CaptureRects(root, calendarMonth, calendarDay), "规则编辑器按年错态切焦", failures);

            // 按年双错时仅修正月份：月份错误边与 HelpText 立即清除，未修正的日期错误边、HelpText 与错误提示继续保留
            calendarMonth.SelectedIndex = 5;
            Settle(editor);
            Check(failures, "按年双错仅修正月份后立即清除月份错误边并保留未修正日期错误边与 HelpText",
                !InputErrorState.GetHasError(calendarMonth) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(calendarMonth)) &&
                InputErrorState.GetHasError(calendarDay) &&
                calendarSelectorError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(calendarDay), calendarSelectorError.Text, StringComparison.Ordinal));
            RequireInputBorder(editor, calendarDay, "DangerBrush", 2, "规则编辑器按年仅修正月份后日期仍保持出错有焦点", failures);

            // 仅按年日期未选再次提交 -> 仅日期出错且聚焦日期；同时验证高对比度区分有效聚焦月份与出错日期
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            Check(failures, "规则编辑器按年仅日期未选时仅日期出错且聚焦日期",
                calendarDay.IsKeyboardFocusWithin &&
                InputErrorState.GetHasError(calendarDay) &&
                !InputErrorState.GetHasError(calendarMonth) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(calendarMonth)));
            RequireInputBorder(editor, calendarDay, "DangerBrush", 2, "规则编辑器按年仅日期出错有焦点", failures);

            Keyboard.Focus(calendarMonth);
            Settle(editor);
            VerifyHighContrastDistinguishable(
                editor,
                editor,
                validFocused: calendarMonth,
                erroredTarget: calendarDay,
                captureTarget: root,
                captureLabel: "rule-editor-high-contrast-error",
                surfaceLabel: "规则编辑器",
                capture: capture,
                failures: failures);

            calendarDay.SelectedIndex = 14;
            Settle(editor);
            RequireClearedFieldError(calendarDay, calendarSelectorError, "规则编辑器按年日期修正后", failures);

            // 按年双错反序：仅修正日期时保留月份错误边与 HelpText，再修正月份后两者均清
            calendarMonth.SelectedIndex = -1;
            calendarDay.SelectedIndex = -1;
            Keyboard.Focus(saveRule);
            Settle(editor);
            InvokeCommandButton(saveRule, editor);
            calendarDay.SelectedIndex = 14;
            Settle(editor);
            Check(failures, "按年双错仅修正日期后清除日期错误边并保留月份错误边与 HelpText",
                !InputErrorState.GetHasError(calendarDay) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(calendarDay)) &&
                InputErrorState.GetHasError(calendarMonth) &&
                calendarSelectorError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(calendarMonth), calendarSelectorError.Text, StringComparison.Ordinal));
            calendarMonth.SelectedIndex = 5;
            Settle(editor);
            RequireClearedFieldError(calendarMonth, calendarSelectorError, "规则编辑器按年反序补修月份后", failures);

            _ = cancelDraft;
        }
        finally
        {
            editor.Close();
            WindowValidationInput.Pump(editor.Dispatcher);
        }
    }

    private static void VerifyReminderCenterCustomSnooze(
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        var dueUtc = FixtureUtc;
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-313-snooze-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;

        try
        {
            var clock = new MutableClock(dueUtc.AddMinutes(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            MustApply(workspace, new SaveNoteChange(
                null,
                "自定义稍后错误边框验证便签",
                new DateTime(2038, 7, 8),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateRepeating(dueUtc, 15)));

            var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            coordinator.Tick(dueUtc);

            var center = coordinator.ActiveWindow
                ?? throw new InvalidOperationException("提醒中心未打开。");
            center.ShowInTaskbar = false;
            center.WindowStartupLocation = WindowStartupLocation.Manual;
            center.Left = -20_000;
            center.Top = -20_000;
            center.Opacity = 0;
            center.Topmost = false;
            Settle(center);

            var row = VisualDescendants<Border>(center)
                .Single(candidate => candidate.Tag is ActiveReminderOccurrenceKey candidateKey && candidateKey == key);
            var snoozeButton = VisualDescendants<Button>(row).Single(button =>
                (AutomationProperties.GetName(button) ?? string.Empty).StartsWith("稍后提醒：", StringComparison.Ordinal));
            var snoozeMenu = snoozeButton.ContextMenu
                ?? throw new InvalidOperationException("稍后提醒按钮缺少 ContextMenu。");

            center.Activate();
            WindowValidationInput.InvokeButton(snoozeButton);
            Settle(center);
            var customMenuItem = snoozeMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "自定义"));
            customMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, customMenuItem));
            Settle(center);

            var customDate = VisualDescendants<DatePicker>(row).Single();
            var customTime = FindByAutomationId<TextBox>(row, "ReminderEntryCustomSnoozeTime");
            var customConfirm = FindByAutomationId<Button>(row, "ReminderEntryCustomSnoozeConfirm");
            var customError = FindByAutomationId<TextBlock>(row, "ReminderEntryCustomError");

            var futureBeijing = ChinaStandardTime.ToBeijingTime(dueUtc.AddMinutes(45));
            var pastBeijing = ChinaStandardTime.ToBeijingTime(dueUtc.AddMinutes(-15));

            // 1. 有效聚焦暖金边 + 零位移（CustomDate 与 CustomTime）
            customDate.SelectedDate = futureBeijing.Date;
            customTime.Text = futureBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            Keyboard.Focus(customConfirm);
            Settle(center);
            var resting = CaptureRects(row, customDate, customTime, customConfirm);

            Keyboard.Focus(customDate);
            Settle(center);
            RequireInputBorder(center, customDate, "MainActionFocusBrush", 2, "提醒中心自定义日期有效聚焦", failures);
            RequireUnchangedRects(resting, CaptureRects(row, customDate, customTime, customConfirm), "提醒中心自定义日期有效聚焦", failures);

            Keyboard.Focus(customTime);
            Settle(center);
            RequireInputBorder(center, customTime, "MainActionFocusBrush", 2, "提醒中心自定义时间有效聚焦", failures);
            RequireUnchangedRects(resting, CaptureRects(row, customDate, customTime, customConfirm), "提醒中心自定义时间有效聚焦", failures);

            // 2. 仅日期为空提交 -> 仅 CustomDate 出错并获焦（2 DIP），CustomTime 保持干净
            announcements.Clear();
            customDate.SelectedDate = null;
            Keyboard.Focus(customConfirm);
            Settle(center);
            WindowValidationInput.InvokeButton(customConfirm);
            Settle(center);

            Check(failures, "提醒中心仅自定义日期为空提交后聚焦 CustomDate", customDate.IsKeyboardFocusWithin);
            Check(failures, "提醒中心仅自定义日期为空时仅 CustomDate 置 HasError",
                InputErrorState.GetHasError(customDate) && !InputErrorState.GetHasError(customTime));
            RequireInputBorder(center, customDate, "DangerBrush", 2, "提醒中心自定义日期出错有焦点", failures);
            Check(failures, "提醒中心仅自定义日期出错时 HelpText 同步且时间框无误标 HelpText",
                customError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(customDate), customError.Text, StringComparison.Ordinal) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(customTime)));
            Check(failures, "提醒中心自定义错误保留既有 Assertive 播报",
                announcements.Any(a => a.Id == "ReminderEntryCustomError" && a.Text == customError.Text && a.Live == AutomationLiveSetting.Assertive));

            Keyboard.Focus(customConfirm);
            Settle(center);
            RequireInputBorder(center, customDate, "DangerBrush", 1, "提醒中心自定义日期出错无焦点", failures);
            RequireUnchangedRects(resting, CaptureRects(row, customDate, customTime, customConfirm), "提醒中心自定义日期错态", failures);

            customDate.SelectedDate = futureBeijing.Date;
            Settle(center);
            RequireClearedFieldError(customDate, customError, "提醒中心自定义日期修正后", failures);

            // 3. 仅时间非法：失焦 1 DIP 无焦点（不抢焦），提交聚焦 CustomTime（2 DIP），CustomDate 保持干净
            Keyboard.Focus(customTime);
            Settle(center);
            customTime.Text = "25:00";
            Settle(center);
            Keyboard.Focus(customConfirm);
            Settle(center);
            Check(failures, "提醒中心自定义时间非法失焦时不抢回焦点",
                customConfirm.IsKeyboardFocusWithin && !customTime.IsKeyboardFocusWithin);
            RequireInputBorder(center, customTime, "DangerBrush", 1, "提醒中心自定义时间失焦出错无焦点", failures);
            Check(failures, "提醒中心仅时间非法时 CustomDate 无错误边与 HelpText",
                !InputErrorState.GetHasError(customDate) && string.IsNullOrEmpty(AutomationProperties.GetHelpText(customDate)));

            WindowValidationInput.InvokeButton(customConfirm);
            Settle(center);
            Check(failures, "提醒中心仅时间非法提交失败时聚焦 CustomTime", customTime.IsKeyboardFocusWithin);
            RequireInputBorder(center, customTime, "DangerBrush", 2, "提醒中心自定义时间提交出错有焦点", failures);

            // 系统对比度区分（有效聚焦 CustomDate vs 出错 CustomTime）
            Keyboard.Focus(customDate);
            Settle(center);
            VerifyHighContrastDistinguishable(
                center,
                center,
                validFocused: customDate,
                erroredTarget: customTime,
                captureTarget: (FrameworkElement)center.Content,
                captureLabel: "reminder-center-high-contrast-error",
                surfaceLabel: "提醒中心自定义稍后",
                capture: capture,
                failures: failures);

            customTime.Text = futureBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            Settle(center);
            RequireClearedFieldError(customTime, customError, "提醒中心自定义时间修正后", failures);

            // 3b. 日期为空且时间非法双错：先修正日期 -> 日期清错且时间保留错误边与 HelpText；再修正时间 -> 两者均清；反序亦然
            customDate.SelectedDate = null;
            customTime.Text = "25:00";
            Keyboard.Focus(customConfirm);
            Settle(center);
            WindowValidationInput.InvokeButton(customConfirm);
            Settle(center);
            Check(failures, "提醒中心自定义日期与时间双错时聚焦首错日期并双挂错误边",
                customDate.IsKeyboardFocusWithin &&
                InputErrorState.GetHasError(customDate) &&
                InputErrorState.GetHasError(customTime) &&
                customError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(customDate), customError.Text, StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(customTime), customError.Text, StringComparison.Ordinal));

            customDate.SelectedDate = futureBeijing.Date;
            Settle(center);
            Check(failures, "提醒中心自定义双错仅修正日期后清除日期错误边并保留时间错误边与 HelpText",
                !InputErrorState.GetHasError(customDate) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(customDate)) &&
                InputErrorState.GetHasError(customTime) &&
                customError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(customTime), customError.Text, StringComparison.Ordinal));
            customTime.Text = futureBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            Settle(center);
            RequireClearedFieldError(customTime, customError, "提醒中心自定义双错补修时间后", failures);

            customDate.SelectedDate = null;
            customTime.Text = "25:00";
            Keyboard.Focus(customConfirm);
            Settle(center);
            WindowValidationInput.InvokeButton(customConfirm);
            Settle(center);
            customTime.Text = futureBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            Settle(center);
            Check(failures, "提醒中心自定义双错仅修正时间后清除时间错误边并保留日期错误边与 HelpText",
                !InputErrorState.GetHasError(customTime) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(customTime)) &&
                InputErrorState.GetHasError(customDate) &&
                customError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(customDate), customError.Text, StringComparison.Ordinal));
            customDate.SelectedDate = futureBeijing.Date;
            Settle(center);
            RequireClearedFieldError(customDate, customError, "提醒中心自定义双错补修日期后", failures);

            // 4. 过去时间提交（真实 coordinator 拒绝）：双挂错误边，提交聚焦首错 CustomDate（2 DIP），CustomTime 无焦（1 DIP），切焦零位移
            customDate.SelectedDate = pastBeijing.Date;
            customTime.Text = pastBeijing.ToString("HH:mm", CultureInfo.InvariantCulture);
            Keyboard.Focus(customConfirm);
            Settle(center);
            WindowValidationInput.InvokeButton(customConfirm);
            Settle(center);

            Check(failures, "提醒中心过去时间提交失败聚焦首错 CustomDate 并双挂错误边",
                customDate.IsKeyboardFocusWithin &&
                InputErrorState.GetHasError(customDate) &&
                InputErrorState.GetHasError(customTime) &&
                customError.IsVisible &&
                string.Equals(AutomationProperties.GetHelpText(customDate), customError.Text, StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(customTime), customError.Text, StringComparison.Ordinal));
            RequireInputBorder(center, customDate, "DangerBrush", 2, "提醒中心过去时间日期出错有焦点", failures);
            RequireInputBorder(center, customTime, "DangerBrush", 1, "提醒中心过去时间时间出错无焦点", failures);
            capture?.Invoke(center, (FrameworkElement)center.Content, "reminder-center-custom-snooze-error");

            var pastRects = CaptureRects(row, customDate, customTime, customConfirm);
            Keyboard.Focus(customTime);
            Settle(center);
            RequireInputBorder(center, customDate, "DangerBrush", 1, "提醒中心过去时间日期切焦无焦点", failures);
            RequireInputBorder(center, customTime, "DangerBrush", 2, "提醒中心过去时间时间切焦有焦点", failures);
            RequireUnchangedRects(pastRects, CaptureRects(row, customDate, customTime, customConfirm), "提醒中心过去时间错态切焦", failures);

            // 修正为未来时间立即清除所有错误边、文案与 HelpText
            customDate.SelectedDate = futureBeijing.Date.AddDays(1);
            Settle(center);
            Check(failures, "提醒中心修正过去时间为未来日期后立即清除双边框与错误文案",
                !customError.IsVisible &&
                !InputErrorState.GetHasError(customDate) &&
                !InputErrorState.GetHasError(customTime) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(customDate)) &&
                string.IsNullOrEmpty(AutomationProperties.GetHelpText(customTime)));
        }
        finally
        {
            coordinator?.Dispose();
            DeleteSettingsFiles(settingsPath);
        }
    }

    private static void VerifySingleControlErrorCycle(
        Window host,
        FrameworkElement container,
        Control input,
        TextBlock error,
        Button idleFocusTarget,
        string label,
        Action fix,
        List<(string Id, string Text, AutomationLiveSetting Live)> announcements,
        string expectedErrorId,
        List<string> failures)
    {
        Check(failures, $"{label} 提交失败聚焦自身", input.IsKeyboardFocusWithin);
        Check(failures, $"{label} 提交失败置 HasError", InputErrorState.GetHasError(input));
        Check(failures, $"{label} 错误行可见且 HelpText 同步",
            error.IsVisible &&
            !string.IsNullOrWhiteSpace(error.Text) &&
            string.Equals(AutomationProperties.GetHelpText(input), error.Text, StringComparison.Ordinal));
        Check(failures, $"{label} 触发礼貌播报",
            announcements.Any(a => a.Id == expectedErrorId && a.Text == error.Text && a.Live == AutomationLiveSetting.Polite));
        RequireInputBorder(host, input, "DangerBrush", 2, $"{label} 出错有焦点", failures);

        var focusedErrorRects = CaptureRects(container, input);
        Keyboard.Focus(idleFocusTarget);
        Settle(host);
        RequireInputBorder(host, input, "DangerBrush", 1, $"{label} 出错无焦点", failures);
        RequireUnchangedRects(focusedErrorRects, CaptureRects(container, input), $"{label} 错态失焦", failures);

        Keyboard.Focus(input);
        Settle(host);
        fix();
        Settle(host);
        RequireClearedFieldError(input, error, $"{label} 修正后", failures);
        RequireInputBorder(host, input, "MainActionFocusBrush", 2, $"{label} 修正后有效有焦点", failures);
    }

    private static readonly ApplicationTheme.SystemContrastPalette HighContrastPalette = new(
        Colors.Black,
        Colors.White,
        Colors.Black,
        Colors.White,
        Colors.Yellow,
        Colors.Black,
        Colors.Gray);

    private static void VerifyHighContrastDistinguishable(
        Window host,
        FrameworkElement resourceOwner,
        Control validFocused,
        Control erroredTarget,
        FrameworkElement? captureTarget,
        string? captureLabel,
        string surfaceLabel,
        Action<Window, FrameworkElement, string>? capture,
        List<string> failures)
    {
        using (var contrast = ApplicationTheme.OverrideHighContrastForValidation(true))
        {
            contrast.ChangeColors(HighContrastPalette);
            Keyboard.Focus(validFocused);
            Settle(host);

            var focusColor = BrushColor(resourceOwner.FindResource("MainActionFocusBrush"));
            var dangerColor = BrushColor(resourceOwner.FindResource("DangerBrush"));
            Check(failures, $"{surfaceLabel} 系统对比度下焦点映射 Highlight、错误映射 WindowText 且可区分",
                focusColor == Colors.Yellow && dangerColor == Colors.White && focusColor != dangerColor);
            RequireInputBorderColor(validFocused, Colors.Yellow, 2, $"{surfaceLabel} 高对比度有效聚焦", failures);
            RequireInputBorderColor(erroredTarget, Colors.White, 1, $"{surfaceLabel} 高对比度出错无焦点", failures);
            if (capture is not null && captureTarget is not null && captureLabel is not null)
            {
                capture(host, captureTarget, captureLabel);
            }

            Keyboard.Focus(erroredTarget);
            Settle(host);
            RequireInputBorderColor(erroredTarget, Colors.White, 2, $"{surfaceLabel} 高对比度出错有焦点", failures);
        }

        Settle(host);
        var restoredFocusColor = BrushColor(resourceOwner.FindResource("MainActionFocusBrush"));
        var restoredDangerColor = BrushColor(resourceOwner.FindResource("DangerBrush"));
        Check(failures, $"{surfaceLabel} 退出系统对比度后恢复日间色板",
            restoredFocusColor == ApplicationTheme.DayColorFor("MainActionFocusBrush") &&
            restoredDangerColor == ApplicationTheme.DayColorFor("DangerBrush"));
    }

    private static void RequireAccessibleFieldError(
        TextBlock error,
        Control source,
        string expected,
        string label,
        List<string> failures)
    {
        Check(failures, $"{label} 错误行可见且文案精确匹配",
            error.IsVisible && string.Equals(error.Text, expected, StringComparison.Ordinal));
        Check(failures, $"{label} 错误行名称/帮助、来源 HelpText 与礼貌播报级别同步",
            string.Equals(AutomationProperties.GetName(error), expected, StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetHelpText(error), expected, StringComparison.Ordinal) &&
            string.Equals(AutomationProperties.GetHelpText(source), expected, StringComparison.Ordinal) &&
            AutomationProperties.GetLiveSetting(error) == AutomationLiveSetting.Polite);
    }

    private static void RequireClearedFieldError(Control source, TextBlock error, string label, List<string> failures)
    {
        Check(failures, $"{label} 错误文案与可见性立即清除", !error.IsVisible && string.IsNullOrEmpty(error.Text));
        Check(failures, $"{label} InputErrorState.HasError 立即清除", !InputErrorState.GetHasError(source));
        Check(failures, $"{label} 来源控件 HelpText 立即清除", string.IsNullOrEmpty(AutomationProperties.GetHelpText(source)));
    }

    private static void RequireInputBorder(
        FrameworkElement resourceOwner,
        Control input,
        string expectedBrushKey,
        double expectedDip,
        string label,
        List<string> failures)
    {
        var expectedColor = BrushColor(resourceOwner.FindResource(expectedBrushKey));
        RequireInputBorderColor(input, expectedColor, expectedDip, $"{label}（{expectedBrushKey}）", failures);
    }

    private static void RequireInputBorderColor(
        Control input,
        Color expectedColor,
        double expectedDip,
        string label,
        List<string> failures)
    {
        input.ApplyTemplate();
        if (input is DatePicker picker)
        {
            var text = picker.Template?.FindName("PART_TextBox", picker) as Control;
            var button = picker.Template?.FindName("PART_Button", picker) as Control;
            if (text is null || button is null || text.BorderThickness != default || button.BorderThickness != default)
            {
                failures.Add($"{label}：DatePicker 内部部件不应绘独立边框（text={text?.BorderThickness}, button={button?.BorderThickness}）。");
            }
        }

        var surface = input.Template?.FindName("Surface", input) as Border;
        if (surface is null)
        {
            failures.Add($"{label}：控件缺少 Surface 模板边框。");
            return;
        }

        if (surface.BorderBrush is not SolidColorBrush actual || actual.Color != expectedColor)
        {
            failures.Add($"{label}：边框色不符（expected={expectedColor}, actual={surface.BorderBrush}）。");
        }

        var t = surface.BorderThickness;
        if (Math.Abs(t.Left - expectedDip) > 0.01 ||
            Math.Abs(t.Top - expectedDip) > 0.01 ||
            Math.Abs(t.Right - expectedDip) > 0.01 ||
            Math.Abs(t.Bottom - expectedDip) > 0.01)
        {
            failures.Add($"{label}：边厚不是 {expectedDip} DIP（actual={t}）。");
        }
    }

    private static Rect[] CaptureRects(Visual root, params FrameworkElement[] elements) =>
        elements.Select(element => Bounds(element, root)).ToArray();

    private static void RequireUnchangedRects(
        Rect[] before,
        Rect[] after,
        string label,
        List<string> failures)
    {
        if (before.Length != after.Length)
        {
            failures.Add($"{label}：几何采样数量不一致。");
            return;
        }

        for (var i = 0; i < before.Length; i++)
        {
            var b = before[i];
            var a = after[i];
            if (Math.Abs(b.X - a.X) > Tolerance ||
                Math.Abs(b.Y - a.Y) > Tolerance ||
                Math.Abs(b.Width - a.Width) > Tolerance ||
                Math.Abs(b.Height - a.Height) > Tolerance)
            {
                failures.Add($"{label} 挤动布局[{i}]：before={b}，after={a}。");
            }
        }
    }

    private static Color BrushColor(object resource) =>
        resource is SolidColorBrush solid
            ? solid.Color
            : throw new InvalidOperationException($"期望 SolidColorBrush 资源，实际为 {resource?.GetType().Name ?? "null"}。");

    private static string ResolveAnnouncementId(TextBlock block)
    {
        var id = AutomationProperties.GetAutomationId(block);
        return !string.IsNullOrEmpty(id) ? id : block.Name ?? string.Empty;
    }

    private static void ShowAndWaitFirstFrame(Window window)
    {
        var rendered = false;
        window.ContentRendered += (_, _) => rendered = true;
        window.Show();
        window.Activate();
        var pumped = WindowValidationInput.PumpRenderedFrames(window.Dispatcher, 2000, () => rendered);
        Settle(window);
        if (!rendered || !pumped.Completed)
        {
            throw new InvalidOperationException($"{window.GetType().Name} 未完成首帧渲染。");
        }
    }

    private static void Click(ButtonBase button, Window window)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Settle(window);
    }

    private static void InvokeCommandButton(Button button, Window window)
    {
        WindowValidationInput.InvokeButton(button);
        Settle(window);
    }

    private static void Settle(Window window)
    {
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
        WindowValidationInput.Pump(window.Dispatcher);
        window.UpdateLayout();
    }

    private static T Required<T>(FrameworkElement host, string name) where T : class =>
        host.FindName(name) as T
        ?? throw new InvalidOperationException($"{host.GetType().Name} 缺少控件 {name}（{typeof(T).Name}）。");

    private static T FindByAutomationId<T>(DependencyObject root, string automationId) where T : DependencyObject
    {
        foreach (var candidate in WalkVisualAndLogical(root))
        {
            if (candidate is T match &&
                string.Equals(AutomationProperties.GetAutomationId(match), automationId, StringComparison.Ordinal))
            {
                return match;
            }
        }

        throw new InvalidOperationException($"缺少 AutomationId={automationId} 的 {typeof(T).Name}。");
    }

    private static IEnumerable<DependencyObject> WalkVisualAndLogical(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        visited.Add(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;
            if (current is Visual)
            {
                var count = VisualTreeHelper.GetChildrenCount(current);
                for (var i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(current, i);
                    if (visited.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            foreach (var logical in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
            {
                if (visited.Add(logical))
                {
                    queue.Enqueue(logical);
                }
            }
        }
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T match)
            {
                yield return match;
            }

            if (child is DependencyObject dep)
            {
                foreach (var nested in LogicalDescendants<T>(dep))
                {
                    yield return nested;
                }
            }
        }
    }

    private static DesktopWorkspaceChangeResult MustApply(DesktopWorkspace workspace, DesktopWorkspaceChange change)
    {
        var result = workspace.Apply(change);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"预置验证数据失败：{result.Error}");
        }

        return result;
    }

    private static void Check(List<string> failures, string label, bool condition)
    {
        if (!condition)
        {
            failures.Add(label);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static Action<Window, FrameworkElement, string>? GetEvidenceWriter()
    {
        if (!_evidenceDirectoryInitialized)
        {
            _evidenceDirectoryInitialized = true;
            var explicitDirectory = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_ISSUE313_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(explicitDirectory))
            {
                _evidenceDirectory = Path.GetFullPath(explicitDirectory);
                Directory.CreateDirectory(_evidenceDirectory);
            }
            else
            {
                var sourceRoot = Environment.GetEnvironmentVariable("MINMIN_COUNTDOWN_VISUAL_SOURCE_ROOT");
                if (!string.IsNullOrWhiteSpace(sourceRoot) && Directory.Exists(sourceRoot))
                {
                    _evidenceDirectory = Path.Combine(
                        Path.GetFullPath(sourceRoot),
                        "logs",
                        "ui-captures",
                        $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-form-error-borders-spread");
                    Directory.CreateDirectory(_evidenceDirectory);
                }
            }
        }

        if (_evidenceDirectory is null)
        {
            return null;
        }

        var directory = _evidenceDirectory;
        return (window, target, label) =>
        {
            var originalOpacity = window.Opacity;
            try
            {
                if (originalOpacity == 0)
                {
                    window.Opacity = 1;
                    Settle(window);
                }

                target.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(target);
                var dpiX = dpi.PixelsPerInchX > 0 ? dpi.PixelsPerInchX : 96;
                var dpiY = dpi.PixelsPerInchY > 0 ? dpi.PixelsPerInchY : 96;
                var logicalWidth = Math.Max(1, target.ActualWidth);
                var logicalHeight = Math.Max(1, target.ActualHeight);
                var pixelWidth = Math.Max(1, (int)Math.Ceiling(logicalWidth * dpiX / 96.0));
                var pixelHeight = Math.Max(1, (int)Math.Ceiling(logicalHeight * dpiY / 96.0));
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(
                        new VisualBrush(target) { Stretch = Stretch.None },
                        null,
                        new Rect(0, 0, logicalWidth, logicalHeight));
                }

                var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                bitmap.Freeze();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new FileStream(
                    Path.Combine(directory, label + ".png"),
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
                encoder.Save(stream);
            }
            finally
            {
                if (originalOpacity == 0)
                {
                    window.Opacity = 0;
                    Settle(window);
                }
            }
        };
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class StubManagerHost : IManagerWorkspaceHost
    {
        public void Reveal(WorkspaceKind workspace) { }
        public void RevealPreferences(WorkspaceKind workspace) { }
        public void RevealNoteEditor(string noteId) { }
    }

    private sealed class ToggleableFailureSettingsFileOperations : ISettingsFileOperations
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public bool FailWrites { get; set; }

        public SettingsPathState GetPathState(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0
                    ? SettingsPathState.Occupied
                    : SettingsPathState.RegularFile;
            }
            catch (FileNotFoundException)
            {
                return SettingsPathState.Absent;
            }
            catch (DirectoryNotFoundException)
            {
                return SettingsPathState.Absent;
            }
            catch (UnauthorizedAccessException)
            {
                return SettingsPathState.Unreadable;
            }
            catch (IOException)
            {
                return SettingsPathState.Unreadable;
            }
        }

        public bool FileExists(string path) => File.Exists(path);
        public string ReadUtf8Text(string path) => File.ReadAllText(path, Encoding.UTF8);
        public byte[] ReadFileBytes(string path) => File.ReadAllBytes(path);
        public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

        public void WriteUtf8NoBomText(string path, string content)
        {
            ThrowIfWriteBlocked();
            File.WriteAllText(path, content, Utf8NoBom);
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            ThrowIfWriteBlocked();
            File.Copy(sourcePath, destinationPath, overwrite);
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            ThrowIfWriteBlocked();
            File.Move(sourcePath, destinationPath, overwrite);
        }

        public void ReplaceFile(string sourcePath, string destinationPath, string? backupPath, bool ignoreMetadataErrors)
        {
            ThrowIfWriteBlocked();
            File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);
        }

        public void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteNewFileAndFlush(string path, ReadOnlySpan<byte> content)
        {
            ThrowIfWriteBlocked();
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        private void ThrowIfWriteBlocked()
        {
            if (FailWrites)
            {
                throw new IOException("模拟写入受限，无法保存标签。");
            }
        }
    }
}
