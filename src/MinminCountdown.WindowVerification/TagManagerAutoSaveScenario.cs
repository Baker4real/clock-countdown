using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #308：标签管理改名按 Enter 或失焦即保存，移除每行“保存”按钮，
// 失败时错误贴在该行并保留输入、焦点回该输入框，Esc 放弃未提交改名并关层返焦，
// 删除标签保持取消默认确认与“只解除关联、不删记录”语义。
internal static class TagManagerAutoSaveScenario
{
    private static readonly DateTimeOffset FixtureUtc = new(2038, 8, 12, 1, 0, 0, TimeSpan.Zero);

    internal static (bool Passed, string Explanation) RunAutoSaveOnEnterAndBlur(string meaning)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-tag-autosave-308-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var failures = new List<string>();
        var observations = new List<string>();
        MainWindow? main = null;
        try
        {
            var clock = new FixedClock(FixtureUtc);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var workTag = MustApply(workspace, new CreateTagChange("工作", "#F4B183")).CreatedTag!;
            var lifeTag = MustApply(workspace, new CreateTagChange("生活", "#E7F4E8")).CreatedTag!;
            MustApply(workspace, new SaveCountdownChange(
                null,
                "自动保存标签倒计时",
                FixtureUtc.AddDays(3),
                new[] { workTag.Id },
                Array.Empty<ReminderRule>()));
            MustApply(workspace, new SaveNoteChange(
                null,
                "自动保存标签便签",
                workspace.Snapshot.BeijingNow.Date,
                false,
                new[] { workTag.Id },
                Array.Empty<ReminderRule>()));

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
            main = CreateActiveMainWindow(workspace, calendar, diagnosticPath);
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            main.Show();
            main.Activate();
            SettleLayout(main);

            var countdownTags = Required<TagSelector>(main, "CountdownTags");
            var noteTags = Required<TagSelector>(main, "NoteTags");
            countdownTags.SetSelection([workTag.Id]);
            noteTags.SetSelection([workTag.Id]);
            SettleLayout(main);

            var popup = Required<Popup>(main, "TagManagerPopup");
            var trigger = Required<Button>(main, "TagManagerButton");
            var newTagInput = Required<TextBox>(main, "NewTagInput");
            var createTagButton = Required<Button>(main, "CreateTagButton");
            var rows = Required<Panel>(main, "TagManagerRows");

            OpenTagManager(main, popup, trigger, newTagInput, failures);

            // 1) 行内不再存在“保存”按钮，仅保留“删除”按钮；新建标签流程（Enter 与按钮点击）不变。
            var saveButtons = LogicalDescendants<Button>(rows)
                .Where(button => Equals(button.Content, "保存") ||
                                 (AutomationProperties.GetName(button) ?? string.Empty).StartsWith("保存标签 ", StringComparison.Ordinal))
                .ToArray();
            Check(
                failures,
                "标签管理行不再渲染“保存”按钮",
                saveButtons.Length == 0,
                $"saveButtonCount={saveButtons.Length}");

            var deleteButtons = LogicalDescendants<Button>(rows)
                .Where(button => Equals(button.Content, "删除"))
                .ToArray();
            Check(
                failures,
                "标签管理每行保留“删除”按钮",
                deleteButtons.Length == 2,
                $"deleteButtonCount={deleteButtons.Length}");

            newTagInput.Text = "学习";
            Keyboard.Focus(newTagInput);
            SettleLayout(main);
            var enterCreated = WindowValidationInput.RaisePreviewKey(newTagInput, Key.Enter);
            SettleLayout(main);
            Check(
                failures,
                "新建标签输入框按 Enter 仍可创建标签并清空输入",
                enterCreated &&
                string.IsNullOrEmpty(newTagInput.Text) &&
                workspace.Snapshot.Settings.Tags.Any(tag => string.Equals(tag.Name, "学习", StringComparison.Ordinal)) &&
                rows.Children.Count == 3,
                $"handled={enterCreated}, input='{newTagInput.Text}', rows={rows.Children.Count}");

            newTagInput.Text = "出行";
            SettleLayout(main);
            createTagButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SettleLayout(main);
            Check(
                failures,
                "点击“新建”按钮仍可创建标签",
                string.IsNullOrEmpty(newTagInput.Text) &&
                workspace.Snapshot.Settings.Tags.Any(tag => string.Equals(tag.Name, "出行", StringComparison.Ordinal)) &&
                rows.Children.Count == 4,
                $"input='{newTagInput.Text}', rows={rows.Children.Count}");

            var stateChangedCount = 0;
            void OnStateChanged(object? _, EventArgs __) => stateChangedCount++;
            workspace.StateChanged += OnStateChanged;
            try
            {
                // 2) 内容未变化（原样或仅首尾空格）时，Enter 与失焦都不产生提交、不重建行。
                var rowsBeforeNoOp = rows.Children.OfType<UIElement>().ToArray();
                var workRow = FindTagRow(rows, "工作");
                var workInput = RowInput(workRow);
                Keyboard.Focus(workInput);
                SettleLayout(main);

                WindowValidationInput.RaisePreviewKey(workInput, Key.Enter);
                SettleLayout(main);
                Check(
                    failures,
                    "未修改文本按 Enter 不触发提交",
                    stateChangedCount == 0 && SameReferences(rowsBeforeNoOp, rows.Children.OfType<UIElement>().ToArray()),
                    $"stateChanged={stateChangedCount}");

                workInput.Text = "  工作  ";
                WindowValidationInput.RaisePreviewKey(workInput, Key.Enter);
                SettleLayout(main);
                Check(
                    failures,
                    "仅增删首尾空白按 Enter 视作未变化、不触发提交并归位文本",
                    stateChangedCount == 0 &&
                    string.Equals(workInput.Text, "工作", StringComparison.Ordinal) &&
                    SameReferences(rowsBeforeNoOp, rows.Children.OfType<UIElement>().ToArray()),
                    $"stateChanged={stateChangedCount}, text='{workInput.Text}'");

                Keyboard.Focus(newTagInput);
                SettleLayout(main);
                Check(
                    failures,
                    "未修改文本失焦不触发提交",
                    stateChangedCount == 0 && SameReferences(rowsBeforeNoOp, rows.Children.OfType<UIElement>().ToArray()),
                    $"stateChanged={stateChangedCount}");

                Keyboard.Focus(workInput);
                SettleLayout(main);
                workInput.Text = "  工作  ";
                Keyboard.Focus(newTagInput);
                SettleLayout(main);
                Check(
                    failures,
                    "仅增删首尾空白失焦不触发提交并归位文本",
                    stateChangedCount == 0 &&
                    string.Equals(workInput.Text, "工作", StringComparison.Ordinal) &&
                    SameReferences(rowsBeforeNoOp, rows.Children.OfType<UIElement>().ToArray()),
                    $"stateChanged={stateChangedCount}, text='{workInput.Text}'");

                // 3) 按 Enter 精确提交一次改名，随后失焦不再重复提交。
                Keyboard.Focus(workInput);
                SettleLayout(main);
                workInput.Text = "项目推进";
                Check(
                    failures,
                    "仅键入新名未按 Enter/失焦前不应提前提交",
                    stateChangedCount == 0 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "工作", StringComparison.Ordinal),
                    $"stateChanged={stateChangedCount}, persisted='{ReadPersistedTagName(settingsPath, workTag.Id)}'");

                var renameEnterHandled = WindowValidationInput.RaisePreviewKey(workInput, Key.Enter);
                SettleLayout(main);
                var rebuiltAfterEnter = FindTagRow(rows, "项目推进");
                var rebuiltInputAfterEnter = RowInput(rebuiltAfterEnter);
                Check(
                    failures,
                    "按 Enter 精确提交一次改名并落盘，且焦点保持在更新后的输入框",
                    renameEnterHandled &&
                    stateChangedCount == 1 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "项目推进", StringComparison.Ordinal) &&
                    ReferenceEquals(Keyboard.FocusedElement, rebuiltInputAfterEnter),
                    $"handled={renameEnterHandled}, stateChanged={stateChangedCount}, persisted='{ReadPersistedTagName(settingsPath, workTag.Id)}', focused={DescribeElement(Keyboard.FocusedElement)}");

                Keyboard.Focus(newTagInput);
                SettleLayout(main);
                Check(
                    failures,
                    "Enter 已提交后再失焦不产生第二次提交",
                    stateChangedCount == 1,
                    $"stateChanged={stateChangedCount}");

                // 4a) 失焦至弹层内其他控件（NewTagInput）精确提交一次改名。
                var rowBeforeBlurToNewTag = FindTagRow(rows, "项目推进");
                var inputBeforeBlurToNewTag = RowInput(rowBeforeBlurToNewTag);
                Keyboard.Focus(inputBeforeBlurToNewTag);
                SettleLayout(main);
                inputBeforeBlurToNewTag.Text = "核心工作";
                Check(
                    failures,
                    "失焦前修改文本尚未提交",
                    stateChangedCount == 1,
                    $"stateChanged={stateChangedCount}");

                Keyboard.Focus(newTagInput);
                SettleLayout(main);
                Check(
                    failures,
                    "失焦至新建输入框精确提交一次改名并落盘，且目标控件保持焦点",
                    stateChangedCount == 2 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "核心工作", StringComparison.Ordinal) &&
                    ReferenceEquals(Keyboard.FocusedElement, newTagInput),
                    $"stateChanged={stateChangedCount}, persisted='{ReadPersistedTagName(settingsPath, workTag.Id)}', focused={DescribeElement(Keyboard.FocusedElement)}");

                // 4b) 失焦至另一标签行输入框：提交一次并把焦点落到重建后的目标行输入框。
                var coreRow = FindTagRow(rows, "核心工作");
                var coreInput = RowInput(coreRow);
                var lifeRow = FindTagRow(rows, "生活");
                var lifeInput = RowInput(lifeRow);
                Keyboard.Focus(coreInput);
                SettleLayout(main);
                coreInput.Text = "重点事项";
                Keyboard.Focus(lifeInput);
                SettleLayout(main);
                var rebuiltLifeInput = RowInput(FindTagRow(rows, "生活"));
                Check(
                    failures,
                    "失焦至另一标签输入框时精确提交一次且焦点落在重建后的目标输入框",
                    stateChangedCount == 3 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "重点事项", StringComparison.Ordinal) &&
                    ReferenceEquals(Keyboard.FocusedElement, rebuiltLifeInput),
                    $"stateChanged={stateChangedCount}, persisted='{ReadPersistedTagName(settingsPath, workTag.Id)}', focused={DescribeElement(Keyboard.FocusedElement)}");

                // 4c) 失焦至同行“删除”按钮（键盘 Tab 路径）：提交一次并把焦点落到重建后的删除按钮。
                var focusRow = FindTagRow(rows, "重点事项");
                var focusInput = RowInput(focusRow);
                var focusDelete = RowDeleteButton(focusRow);
                Keyboard.Focus(focusInput);
                SettleLayout(main);
                focusInput.Text = "终版工作";
                Keyboard.Focus(focusDelete);
                SettleLayout(main);
                var rebuiltFinalDelete = RowDeleteButton(FindTagRow(rows, "终版工作"));
                Check(
                    failures,
                    "键盘移焦至同行删除按钮时精确提交一次并聚焦更新后的删除按钮",
                    stateChangedCount == 4 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "终版工作", StringComparison.Ordinal) &&
                    ReferenceEquals(Keyboard.FocusedElement, rebuiltFinalDelete) &&
                    string.Equals(AutomationProperties.GetName(rebuiltFinalDelete), "删除标签 终版工作", StringComparison.Ordinal),
                    $"stateChanged={stateChangedCount}, persisted='{ReadPersistedTagName(settingsPath, workTag.Id)}', focused={DescribeElement(Keyboard.FocusedElement)}");

                // 5) 核对改名已同步到四处界面（倒计时行、便签卡、筛选标签、标签选择器）。
                PopupMotionBehavior.Close(popup, immediate: true);
                SettleLayout(main);
                var countdownChipText = FindTagChipText(Required<Panel>(main, "CountdownRows"), workTag.Id);
                var filterChip = Required<Panel>(main, "FilterTagOptions").Children
                    .OfType<ToggleButton>()
                    .Single(button => string.Equals(button.Uid, workTag.Id, StringComparison.OrdinalIgnoreCase));
                Required<ButtonBase>(main, "NotesTab").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                SettleLayout(main);
                var noteChipText = FindTagChipText(Required<Panel>(main, "ActiveNoteRows"), workTag.Id);
                Check(
                    failures,
                    "自动保存改名后四处标签界面全部同步显示新名称",
                    string.Equals(countdownChipText, "终版工作", StringComparison.Ordinal) &&
                    string.Equals(noteChipText, "# 终版工作", StringComparison.Ordinal) &&
                    Equals(filterChip.Content, "终版工作") &&
                    string.Equals(countdownTags.SummaryText, "终版工作", StringComparison.Ordinal) &&
                    string.Equals(noteTags.SummaryText, "终版工作", StringComparison.Ordinal),
                    $"countdown='{countdownChipText}', note='{noteChipText}', filter='{filterChip.Content}', picker='{countdownTags.SummaryText}/{noteTags.SummaryText}'");

                observations.Add($"stateChanged={stateChangedCount}, finalTag={ReadPersistedTagName(settingsPath, workTag.Id)}, lifeTag={lifeTag.Name}");
            }
            finally
            {
                workspace.StateChanged -= OnStateChanged;
            }

            Console.Error.WriteLine($"TAG_MANAGER_AUTOSAVE_308_PROBE: {string.Join(" | ", observations)}");
            return failures.Count == 0
                ? (true, meaning)
                : (false, $"{meaning}；失败 {failures.Count} 项：{string.Join("；", failures)}");
        }
        finally
        {
            CloseAndCleanup(main, settingsPath, diagnosticPath);
        }
    }

    internal static (bool Passed, string Explanation) RunErrorsEscDiscardAndDelete(string meaning)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-tag-errors-308-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        var failures = new List<string>();
        var observations = new List<string>();
        MainWindow? main = null;
        try
        {
            var clock = new FixedClock(FixtureUtc);
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            var workTag = MustApply(workspace, new CreateTagChange("工作", "#F4B183")).CreatedTag!;
            var lifeTag = MustApply(workspace, new CreateTagChange("生活", "#E7F4E8")).CreatedTag!;
            MustApply(workspace, new SaveCountdownChange(
                null,
                "错误与删除验证倒计时",
                FixtureUtc.AddDays(4),
                new[] { workTag.Id },
                Array.Empty<ReminderRule>()));
            MustApply(workspace, new SaveNoteChange(
                null,
                "错误与删除验证便签",
                workspace.Snapshot.BeijingNow.Date,
                false,
                new[] { workTag.Id },
                Array.Empty<ReminderRule>()));

            using var calendar = new HolidayCalendar(AppContext.BaseDirectory);
            using var monitor = MonitorWorkAreaResolver.OverrideForValidation(
                new MonitorWorkArea(new Rect(0, 0, 1920, 1032), 1, 1));
            main = CreateActiveMainWindow(workspace, calendar, diagnosticPath);
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            main.Show();
            main.Activate();
            SettleLayout(main);

            var popup = Required<Popup>(main, "TagManagerPopup");
            var trigger = Required<Button>(main, "TagManagerButton");
            var newTagInput = Required<TextBox>(main, "NewTagInput");
            var rows = Required<Panel>(main, "TagManagerRows");

            OpenTagManager(main, popup, trigger, newTagInput, failures);

            var stateChangedCount = 0;
            void OnStateChanged(object? _, EventArgs __) => stateChangedCount++;
            workspace.StateChanged += OnStateChanged;
            try
            {
                var workRow = FindTagRow(rows, "工作");
                var workInput = RowInput(workRow);
                var workError = RowError(workRow);
                var lifeRow = FindTagRow(rows, "生活");
                var lifeError = RowError(lifeRow);

                // 1) 重名失败（Enter）：错误贴在该行、保留用户输入、焦点放回该输入框、兄弟行无错。
                Keyboard.Focus(workInput);
                SettleLayout(main);
                workInput.Text = "生活";
                WindowValidationInput.RaisePreviewKey(workInput, Key.Enter);
                SettleLayout(main);
                Check(
                    failures,
                    "重名按 Enter 失败时错误贴在该行，保留输入并把焦点放回该输入框",
                    stateChangedCount == 0 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "工作", StringComparison.Ordinal) &&
                    workError.Visibility == Visibility.Visible &&
                    workError.Text.Contains("重复", StringComparison.Ordinal) &&
                    AutomationProperties.GetLiveSetting(workError) == AutomationLiveSetting.Polite &&
                    lifeError.Visibility != Visibility.Visible &&
                    InputErrorState.GetHasError(workInput) &&
                    string.Equals(workInput.Text, "生活", StringComparison.Ordinal) &&
                    ReferenceEquals(Keyboard.FocusedElement, workInput),
                    $"stateChanged={stateChangedCount}, errorVis={workError.Visibility}, error='{workError.Text}', hasError={InputErrorState.GetHasError(workInput)}, text='{workInput.Text}', focused={DescribeElement(Keyboard.FocusedElement)}");

                // 修改文本后该行错误与错误边框立即清除。
                workInput.Text = "工作草稿";
                SettleLayout(main);
                Check(
                    failures,
                    "修正重名输入后该行错误与错误边框立即清除",
                    workError.Visibility == Visibility.Collapsed &&
                    string.IsNullOrEmpty(workError.Text) &&
                    !InputErrorState.GetHasError(workInput),
                    $"errorVis={workError.Visibility}, error='{workError.Text}', hasError={InputErrorState.GetHasError(workInput)}");

                // 2) 重名失败（失焦）：错误贴在该行、保留输入并把焦点放回该输入框。
                workInput.Text = "生活";
                Keyboard.Focus(newTagInput);
                SettleLayout(main);
                Check(
                    failures,
                    "重名失焦失败时错误贴在该行，保留输入并把焦点放回该输入框",
                    stateChangedCount == 0 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "工作", StringComparison.Ordinal) &&
                    workError.Visibility == Visibility.Visible &&
                    workError.Text.Contains("重复", StringComparison.Ordinal) &&
                    InputErrorState.GetHasError(workInput) &&
                    string.Equals(workInput.Text, "生活", StringComparison.Ordinal) &&
                    ReferenceEquals(Keyboard.FocusedElement, workInput),
                    $"stateChanged={stateChangedCount}, errorVis={workError.Visibility}, error='{workError.Text}', text='{workInput.Text}', focused={DescribeElement(Keyboard.FocusedElement)}");

                // 3) 空名失败（Enter）与纯空白失败（失焦）：错误贴在该行、保留输入并把焦点放回该输入框。
                workInput.Text = string.Empty;
                WindowValidationInput.RaisePreviewKey(workInput, Key.Enter);
                SettleLayout(main);
                Check(
                    failures,
                    "空名按 Enter 失败时错误贴在该行，保留空输入并把焦点放回该输入框",
                    stateChangedCount == 0 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "工作", StringComparison.Ordinal) &&
                    workError.Visibility == Visibility.Visible &&
                    workError.Text.Contains("不能为空", StringComparison.Ordinal) &&
                    InputErrorState.GetHasError(workInput) &&
                    string.Equals(workInput.Text, string.Empty, StringComparison.Ordinal) &&
                    ReferenceEquals(Keyboard.FocusedElement, workInput),
                    $"stateChanged={stateChangedCount}, errorVis={workError.Visibility}, error='{workError.Text}', text='{workInput.Text}', focused={DescribeElement(Keyboard.FocusedElement)}");

                workInput.Text = "   ";
                Keyboard.Focus(newTagInput);
                SettleLayout(main);
                Check(
                    failures,
                    "纯空白失焦失败时错误贴在该行，保留空白输入并把焦点放回该输入框",
                    stateChangedCount == 0 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "工作", StringComparison.Ordinal) &&
                    workError.Visibility == Visibility.Visible &&
                    workError.Text.Contains("不能为空", StringComparison.Ordinal) &&
                    InputErrorState.GetHasError(workInput) &&
                    string.Equals(workInput.Text, "   ", StringComparison.Ordinal) &&
                    ReferenceEquals(Keyboard.FocusedElement, workInput),
                    $"stateChanged={stateChangedCount}, errorVis={workError.Visibility}, error='{workError.Text}', text='{workInput.Text}', focused={DescribeElement(Keyboard.FocusedElement)}");

                // 4a) 错误态下按 Esc 关闭弹层：放弃编辑、不触发死循环、焦点归还触发按钮、重开恢复原名且无残留错误。
                var escFromErrorHandled = WindowValidationInput.RaisePreviewKey(workInput, Key.Escape);
                var closedFromError = WaitForEscCloseAndReturnFocus(main, popup, trigger);
                Check(
                    failures,
                    "错误态下按 Esc 关闭弹层、放弃编辑并归还焦点给触发按钮",
                    escFromErrorHandled &&
                    closedFromError &&
                    stateChangedCount == 0 &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "工作", StringComparison.Ordinal),
                    $"handled={escFromErrorHandled}, closed={closedFromError}, stateChanged={stateChangedCount}, focused={DescribeElement(Keyboard.FocusedElement)}");

                OpenTagManager(main, popup, trigger, newTagInput, failures);
                workRow = FindTagRow(rows, "工作");
                workInput = RowInput(workRow);
                workError = RowError(workRow);
                Check(
                    failures,
                    "Esc 放弃错误编辑后重开弹层恢复原标签名且无残留错误",
                    string.Equals(workInput.Text, "工作", StringComparison.Ordinal) &&
                    workError.Visibility == Visibility.Collapsed &&
                    !InputErrorState.GetHasError(workInput),
                    $"text='{workInput.Text}', errorVis={workError.Visibility}, hasError={InputErrorState.GetHasError(workInput)}");

                // 4b) 有效未提交改名按 Esc 关闭弹层：放弃未提交改名，标签名不变，重开显示原名。
                Keyboard.Focus(workInput);
                SettleLayout(main);
                workInput.Text = "未提交新名称";
                var escFromValidHandled = WindowValidationInput.RaisePreviewKey(workInput, Key.Escape);
                var closedFromValid = WaitForEscCloseAndReturnFocus(main, popup, trigger);
                Check(
                    failures,
                    "有效未提交改名按 Esc 关闭弹层后未提交改名被放弃，标签名不变且返焦触发按钮",
                    escFromValidHandled &&
                    closedFromValid &&
                    stateChangedCount == 0 &&
                    string.Equals(workspace.Snapshot.Settings.Tags.Single(tag => tag.Id == workTag.Id).Name, "工作", StringComparison.Ordinal) &&
                    string.Equals(ReadPersistedTagName(settingsPath, workTag.Id), "工作", StringComparison.Ordinal),
                    $"handled={escFromValidHandled}, closed={closedFromValid}, stateChanged={stateChangedCount}, persisted='{ReadPersistedTagName(settingsPath, workTag.Id)}'");

                OpenTagManager(main, popup, trigger, newTagInput, failures);
                workRow = FindTagRow(rows, "工作");
                workInput = RowInput(workRow);
                Check(
                    failures,
                    "Esc 放弃有效未提交改名后重开弹层仍显示原标签名",
                    string.Equals(workInput.Text, "工作", StringComparison.Ordinal),
                    $"text='{workInput.Text}'");

                // 5) 含未提交/无效输入时点击“删除”：不陷入焦点死循环、不意外提交改名，
                // 保持取消为安全默认的确认框，以及确认后“只解除关联、不删记录”语义。
                Keyboard.Focus(workInput);
                SettleLayout(main);
                workInput.Text = string.Empty;
                var workDelete = RowDeleteButton(workRow);

                var cancelDialogVerified = false;
                ValidationExercises.QueueConfirmationDialogResponse(main, confirm: false, inspect: dialog =>
                {
                    cancelDialogVerified =
                        dialog.CancelButton.IsDefault &&
                        dialog.CancelButton.IsCancel &&
                        !dialog.ConfirmButton.IsDefault &&
                        Equals(dialog.ConfirmButton.Content, "确认删除");
                });
                SimulateDeleteMouseClick(workDelete);
                SettleLayout(main);
                var afterCancel = DesktopWorkspace.Load(settingsPath).Snapshot.Settings;
                Check(
                    failures,
                    "行内含无效输入时点击删除不锁死焦点，取消默认确认框保留标签与关联",
                    cancelDialogVerified &&
                    stateChangedCount == 0 &&
                    afterCancel.Tags.Any(tag => tag.Id == workTag.Id && string.Equals(tag.Name, "工作", StringComparison.Ordinal)) &&
                    afterCancel.Countdowns.Single().TagIds.Contains(workTag.Id, StringComparer.OrdinalIgnoreCase) &&
                    afterCancel.Notes.Single().TagIds.Contains(workTag.Id, StringComparer.OrdinalIgnoreCase),
                    $"dialogVerified={cancelDialogVerified}, stateChanged={stateChangedCount}, tags={afterCancel.Tags.Count}");

                // 确认删除：仅删除标签并从倒计时/便签解除关联，不删除倒计时与便签记录。
                if (!popup.IsOpen)
                {
                    OpenTagManager(main, popup, trigger, newTagInput, failures);
                }
                workRow = FindTagRowByTagId(rows, workTag.Id);
                workDelete = RowDeleteButton(workRow);
                ValidationExercises.QueueConfirmationDialogResponse(main, confirm: true);
                SimulateDeleteMouseClick(workDelete);
                SettleLayout(main);

                var finalMemory = workspace.Snapshot.Settings;
                var finalDisk = DesktopWorkspace.Load(settingsPath).Snapshot.Settings;
                Check(
                    failures,
                    "确认删除标签后只解除倒计时与便签关联，不删除记录",
                    stateChangedCount == 1 &&
                    finalMemory.Tags.Select(tag => tag.Id).SequenceEqual(new[] { lifeTag.Id }) &&
                    finalDisk.Tags.Select(tag => tag.Id).SequenceEqual(new[] { lifeTag.Id }) &&
                    finalMemory.Countdowns.Single().Name == "错误与删除验证倒计时" &&
                    finalMemory.Notes.Single().Text == "错误与删除验证便签" &&
                    !finalMemory.Countdowns[0].TagIds.Contains(workTag.Id, StringComparer.OrdinalIgnoreCase) &&
                    !finalMemory.Notes[0].TagIds.Contains(workTag.Id, StringComparer.OrdinalIgnoreCase) &&
                    !finalDisk.Countdowns[0].TagIds.Contains(workTag.Id, StringComparer.OrdinalIgnoreCase) &&
                    !finalDisk.Notes[0].TagIds.Contains(workTag.Id, StringComparer.OrdinalIgnoreCase),
                    $"stateChanged={stateChangedCount}, memTags={finalMemory.Tags.Count}, diskTags={finalDisk.Tags.Count}");

                observations.Add($"stateChanged={stateChangedCount}, remainingTags={string.Join(',', finalDisk.Tags.Select(tag => tag.Name))}");
            }
            finally
            {
                workspace.StateChanged -= OnStateChanged;
            }

            Console.Error.WriteLine($"TAG_MANAGER_ERRORS_ESC_DELETE_308_PROBE: {string.Join(" | ", observations)}");
            return failures.Count == 0
                ? (true, meaning)
                : (false, $"{meaning}；失败 {failures.Count} 项：{string.Join("；", failures)}");
        }
        finally
        {
            CloseAndCleanup(main, settingsPath, diagnosticPath);
        }
    }

    private static MainWindow CreateActiveMainWindow(
        DesktopWorkspace workspace,
        HolidayCalendar calendar,
        string diagnosticPath) =>
        new(
            workspace,
            calendar,
            new DiagnosticLogger(diagnosticPath),
            AppContext.BaseDirectory)
        {
            ShowInTaskbar = false,
            ShowActivated = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20_000,
            Top = -20_000,
            Opacity = 1
        };

    private static void OpenTagManager(
        MainWindow main,
        Popup popup,
        Button trigger,
        TextBox newTagInput,
        List<string> failures)
    {
        if (!popup.IsOpen)
        {
            trigger.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        var opened = WindowValidationInput.WaitUntil(
            main.Dispatcher,
            () => popup.IsOpen && newTagInput.IsKeyboardFocusWithin,
            1000);
        Check(
            failures,
            "打开标签管理弹层并聚焦新标签输入框",
            opened,
            $"isOpen={popup.IsOpen}, focused={DescribeElement(Keyboard.FocusedElement)}");
        SettleLayout(main);
    }

    private static bool WaitForEscCloseAndReturnFocus(MainWindow main, Popup popup, Button trigger) =>
        WindowValidationInput.WaitUntil(
            main.Dispatcher,
            () => !popup.IsOpen &&
                  (trigger.IsKeyboardFocusWithin ||
                   ReferenceEquals(FocusManager.GetFocusedElement(main), trigger)),
            1000);

    private static void SimulateDeleteMouseClick(Button deleteButton)
    {
        var mouseDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            Source = deleteButton
        };
        deleteButton.RaiseEvent(mouseDown);
        Keyboard.Focus(deleteButton);
        deleteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static Grid FindTagRow(Panel rows, string expectedText) =>
        rows.Children
            .OfType<Grid>()
            .Single(row => row.Children.OfType<TextBox>().Any(box => string.Equals(box.Text, expectedText, StringComparison.Ordinal)));

    private static Grid FindTagRowByTagId(Panel rows, string tagId) =>
        rows.Children
            .OfType<Grid>()
            .Single(row => row.Children.OfType<Button>().Any(button =>
                string.Equals(button.Tag as string, tagId, StringComparison.OrdinalIgnoreCase)));

    private static TextBox RowInput(Grid row) => row.Children.OfType<TextBox>().Single();

    private static Button RowDeleteButton(Grid row) =>
        row.Children.OfType<Button>().Single(button => Equals(button.Content, "删除"));

    private static TextBlock RowError(Grid row) => row.Children.OfType<TextBlock>().Single();

    private static string ReadPersistedTagName(string settingsPath, string tagId) =>
        DesktopWorkspace.Load(settingsPath).Snapshot.Settings.Tags
            .Single(tag => string.Equals(tag.Id, tagId, StringComparison.OrdinalIgnoreCase))
            .Name;

    private static string FindTagChipText(Panel container, string tagId)
    {
        var chip = VisualDescendants<Border>(container)
            .Single(border => string.Equals(border.Uid, tagId, StringComparison.OrdinalIgnoreCase));
        return (chip.Child as TextBlock)?.Text ?? string.Empty;
    }

    private static DesktopWorkspaceChangeResult MustApply(DesktopWorkspace workspace, DesktopWorkspaceChange change)
    {
        var result = workspace.Apply(change);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"预置标签测试数据失败：{result.Error}");
        }

        return result;
    }

    private static bool SameReferences(IReadOnlyList<UIElement> expected, IReadOnlyList<UIElement> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!ReferenceEquals(expected[index], actual[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void Check(List<string> failures, string label, bool condition, string detail)
    {
        if (!condition)
        {
            failures.Add($"{label}（{detail}）");
        }
    }

    private static T Required<T>(MainWindow main, string name) where T : class =>
        main.FindName(name) as T
        ?? throw new InvalidOperationException($"标签管理自动保存验证窗口缺少控件 {name}（{typeof(T).Name}）");

    private static string DescribeElement(IInputElement? element) =>
        element switch
        {
            null => "null",
            FrameworkElement fe => $"{fe.GetType().Name}(Name={fe.Name}, AutomationName={AutomationProperties.GetName(fe)})",
            _ => element.GetType().Name
        };

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

            if (child is DependencyObject dependencyChild)
            {
                foreach (var descendant in LogicalDescendants<T>(dependencyChild))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void CloseAndCleanup(MainWindow? main, string settingsPath, string diagnosticPath)
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
