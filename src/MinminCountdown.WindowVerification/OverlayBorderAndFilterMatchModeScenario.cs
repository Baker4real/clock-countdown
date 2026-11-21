using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MinminCountdown.Core;
using static MinminCountdown.WindowVerification.EmptyStateScenarioSupport;

namespace MinminCountdown.WindowVerification;

// #305：四个弹层（筛选、标签管理、显示设置、标签选择）边界对比度、筛选匹配方式条件披露与弹层焦点生命周期的独立窗口验证。
internal static class OverlayBorderAndFilterMatchModeScenario
{
    private const double MinimumBorderContrast = 3.0;
    private const double GeometryTolerance = 0.5;
    private static readonly DateTimeOffset FixtureUtc = new(2038, 7, 8, 1, 0, 0, TimeSpan.Zero);

    private static readonly Color ExpectedDayControlBorder = ParseColor("#FFA87866");
    private static readonly Color ExpectedDayCanvas = ParseColor("#FFFDF3EB");
    private static readonly Color ExpectedDayBackground = ParseColor("#FFFDF0E4");
    private static readonly Color ExpectedDayCard = ParseColor("#FFFEFCFB");

    private static readonly (string Name, string ResourceKey)[] MainAdjacentSurfaces =
    [
        ("CanvasBrush", "CanvasBrush"),
        ("BackgroundBrush", "BackgroundBrush"),
        ("CardBrush", "CardBrush"),
        ("PanelBrush", "PanelBrush"),
        ("CardAltBrush", "CardAltBrush"),
        ("NoteBrush", "NoteBrush"),
        ("NoteWhiteBrush", "NoteWhiteBrush"),
        ("StatusBarBrush", "StatusBarBrush"),
        ("CharacterPanelBrush", "CharacterPanelBrush"),
        ("CanvasDeepBrush", "CanvasDeepBrush")
    ];

    private static readonly (string Name, string ResourceKey)[] FloatingAdjacentSurfaces =
    [
        ("BackgroundBrush", "BackgroundBrush"),
        ("CardBrush", "CardBrush"),
        ("PanelBrush", "PanelBrush"),
        ("CardAltBrush", "CardAltBrush"),
        ("NoteBrush", "NoteBrush"),
        ("NoteWhiteBrush", "NoteWhiteBrush"),
        ("PinkTintSoftBrush", "PinkTintSoftBrush")
    ];

    internal static (bool Passed, string Explanation) RunOverlayBordersAndContrast(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        var contrastLogs = new List<string>();
        using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
        {
            theme.Apply();
            try
            {
                WithWindows((main, floating, workspace) =>
                {
                    var probe = new MainWindowEmptyStateProbe(main);
                    var expectedRadius = RequiredCornerRadius(main, "RadiusOverlay");
                    var overlays = CollectOverlayDescriptors(main, floating, probe);

                    // 1) 日间主题：逐一打开四个弹层（全部 6 个实例），核对边界角色、底色、圆角、无投影及对各邻近表面的 WCAG ≥ 3:1 对比度。
                    foreach (var overlay in overlays)
                    {
                        overlay.PrepareHost();
                        OpenPopup(overlay);
                        try
                        {
                            VerifyDayOverlayBorderAndContrast(overlay, expectedRadius, failures, contrastLogs);
                        }
                        finally
                        {
                            ClosePopup(overlay);
                        }
                    }

                    // 2) 系统高对比度：深色对比度 → 运行中切浅色对比度 → 退出恢复暖粉日间。
                    var darkHighContrast = new ApplicationTheme.SystemContrastPalette(
                        Window: Color.FromRgb(0x00, 0x00, 0x00),
                        WindowText: Color.FromRgb(0xFF, 0xFF, 0xFF),
                        Control: Color.FromRgb(0x10, 0x10, 0x10),
                        ControlText: Color.FromRgb(0xFF, 0xFF, 0xFF),
                        Highlight: Color.FromRgb(0x1A, 0xEB, 0xFF),
                        HighlightText: Color.FromRgb(0x00, 0x00, 0x00),
                        GrayText: Color.FromRgb(0x3F, 0xF2, 0x3F));
                    var lightHighContrast = new ApplicationTheme.SystemContrastPalette(
                        Window: Color.FromRgb(0xFF, 0xFF, 0xFF),
                        WindowText: Color.FromRgb(0x00, 0x00, 0x00),
                        Control: Color.FromRgb(0xF5, 0xF5, 0xF5),
                        ControlText: Color.FromRgb(0x00, 0x00, 0x00),
                        Highlight: Color.FromRgb(0x37, 0x00, 0x6E),
                        HighlightText: Color.FromRgb(0xFF, 0xFF, 0xFF),
                        GrayText: Color.FromRgb(0x60, 0x00, 0x00));

                    using (var scope = ApplicationTheme.OverrideHighContrastForValidation(true))
                    {
                        scope.ChangeColors(darkHighContrast);
                        SettleBoth(main, floating);

                        foreach (var overlay in overlays)
                        {
                            overlay.PrepareHost();
                            OpenPopup(overlay);
                            try
                            {
                                VerifyHighContrastOverlay(overlay, darkHighContrast, "深色对比度", expectedRadius, failures);
                            }
                            finally
                            {
                                ClosePopup(overlay);
                            }
                        }

                        // 验证打开期间实时响应系统对比度配色变化（主窗筛选弹层 + 浮窗标签选择弹层）。
                        var mainFilterOverlay = overlays.Single(item => item.Name == "MainWindow.FilterPopup");
                        var floatingTagOverlay = overlays.Single(item => item.Name == "FloatingWindow.FloatingTags");
                        mainFilterOverlay.PrepareHost();
                        OpenPopup(mainFilterOverlay);
                        floatingTagOverlay.PrepareHost();
                        OpenPopup(floatingTagOverlay);
                        try
                        {
                            scope.ChangeColors(lightHighContrast);
                            SettleBoth(main, floating);
                            VerifyHighContrastOverlay(mainFilterOverlay, lightHighContrast, "打开中切浅色对比度", expectedRadius, failures);
                            VerifyHighContrastOverlay(floatingTagOverlay, lightHighContrast, "打开中切浅色对比度", expectedRadius, failures);
                        }
                        finally
                        {
                            ClosePopup(mainFilterOverlay);
                            ClosePopup(floatingTagOverlay);
                        }
                    }

                    // 退出系统对比度后全部 6 个实例恢复暖粉日间 ControlBorderBrush 与原底色。
                    SettleBoth(main, floating);
                    foreach (var overlay in overlays)
                    {
                        overlay.PrepareHost();
                        OpenPopup(overlay);
                        try
                        {
                            VerifyDayOverlayBorderAndContrast(overlay, expectedRadius, failures, null);
                        }
                        finally
                        {
                            ClosePopup(overlay);
                        }
                    }
                });
            }
            catch (Exception exception)
            {
                failures.Add($"弹层边界对比度验证异常：{exception.Message}");
            }
        }

        theme.Apply();
        if (contrastLogs.Count > 0)
        {
            Console.Error.WriteLine($"OVERLAY_BORDER_305_CONTRAST: {string.Join(" || ", contrastLogs)}");
        }

        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    internal static (bool Passed, string Explanation) RunFilterMatchModeAndFocusLifecycle(
        ApplicationTheme theme,
        string meaning)
    {
        var failures = new List<string>();
        using (ApplicationTheme.OverrideAnimationPreferenceForValidation(false))
        {
            theme.Apply();
            try
            {
                WithWindows((main, floating, workspace) =>
                {
                    var probe = new MainWindowEmptyStateProbe(main);
                    var seed = SeedFilterFixture(workspace, main.Dispatcher);
                    SettleBoth(main, floating);

                    VerifyFilterMatchModeAndMutualExclusion(main, workspace, probe, seed, failures);
                    VerifyAllOverlayFocusLifecycles(main, floating, workspace, probe, seed, failures);
                });
            }
            catch (Exception exception)
            {
                failures.Add($"筛选匹配方式与弹层焦点生命周期验证异常：{exception.Message}");
            }
        }

        theme.Apply();
        return failures.Count == 0
            ? (true, meaning)
            : (false, $"{meaning}；{string.Join(" | ", failures)}");
    }

    private static void VerifyDayOverlayBorderAndContrast(
        OverlayDescriptor overlay,
        CornerRadius expectedRadius,
        ICollection<string> failures,
        ICollection<string>? contrastLogs)
    {
        if (overlay.Popup.Child is not Border surface)
        {
            failures.Add($"{overlay.Name}：Popup.Child 不是 Border。");
            return;
        }

        surface.UpdateLayout();
        var expectedBorderBrush = RequiredBrush(overlay.ResourceScope, "ControlBorderBrush");
        if (!ReferenceEquals(surface.BorderBrush, expectedBorderBrush))
        {
            failures.Add(
                $"{overlay.Name}：日间边界未绑定 ControlBorderBrush（actual={DescribeBrush(surface.BorderBrush)}, expected={DescribeBrush(expectedBorderBrush)}）。");
        }

        if (surface.BorderBrush is not SolidColorBrush actualBorderBrush ||
            actualBorderBrush.Color != ExpectedDayControlBorder)
        {
            failures.Add(
                $"{overlay.Name}：日间边界色值不等于 ControlBorderColor（actual={DescribeBrush(surface.BorderBrush)}, expected={ExpectedDayControlBorder}）。");
        }

        if (surface.BorderThickness != new Thickness(1))
        {
            failures.Add($"{overlay.Name}：边界厚度不为 1 DIP（actual={surface.BorderThickness}）。");
        }

        if (surface.Background is not SolidColorBrush actualBackground ||
            actualBackground.Color != overlay.ExpectedDayBackground)
        {
            failures.Add(
                $"{overlay.Name}：弹层底色改变（actual={DescribeBrush(surface.Background)}, expected={overlay.ExpectedDayBackground}）。");
        }

        if (surface.CornerRadius != expectedRadius)
        {
            failures.Add($"{overlay.Name}：弹层圆角改变（actual={surface.CornerRadius}, expected={expectedRadius}）。");
        }

        if (surface.Effect is not null || HasAnyDropShadowEffect(surface))
        {
            failures.Add($"{overlay.Name}：弹层不得添加投影（Effect={surface.Effect?.GetType().Name ?? "descendant-DropShadow"}）。");
        }

        if (surface.BorderBrush is not SolidColorBrush borderSolid ||
            surface.Background is not SolidColorBrush backgroundSolid)
        {
            return;
        }

        var measurements = new List<string>();
        var ownContrast = ComputeContrastRatio(borderSolid.Color, backgroundSolid.Color);
        measurements.Add($"own({backgroundSolid.Color})={ownContrast:0.00}:1");
        if (ownContrast < MinimumBorderContrast)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{overlay.Name}：边界对自身底色对比度不足 3:1（{ownContrast:0.00}:1）"));
        }

        foreach (var (surfaceName, resourceKey) in overlay.AdjacentSurfaces)
        {
            var adjacentColor = RequiredBrush(Application.Current.Resources, resourceKey).Color;
            var ratio = ComputeContrastRatio(borderSolid.Color, adjacentColor);
            measurements.Add($"{surfaceName}={ratio:0.00}:1");
            if (ratio < MinimumBorderContrast)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{overlay.Name}：边界对邻近表面 {surfaceName}({adjacentColor}) 对比度不足 3:1（{ratio:0.00}:1）"));
            }
        }

        contrastLogs?.Add($"{overlay.Name}({borderSolid.Color}): {string.Join(", ", measurements)}");
    }

    private static void VerifyHighContrastOverlay(
        OverlayDescriptor overlay,
        ApplicationTheme.SystemContrastPalette palette,
        string phase,
        CornerRadius expectedRadius,
        ICollection<string> failures)
    {
        if (overlay.Popup.Child is not Border surface)
        {
            failures.Add($"{overlay.Name}（{phase}）：Popup.Child 不是 Border。");
            return;
        }

        surface.UpdateLayout();
        if (surface.BorderBrush is not SolidColorBrush borderSolid || borderSolid.Color != palette.WindowText)
        {
            failures.Add(
                $"{overlay.Name}（{phase}）：系统对比度下边界未映射到 WindowText（actual={DescribeBrush(surface.BorderBrush)}, expected={palette.WindowText}）。");
        }

        if (surface.Background is not SolidColorBrush bgSolid || bgSolid.Color != palette.Window)
        {
            failures.Add(
                $"{overlay.Name}（{phase}）：系统对比度下底色未映射到 Window（actual={DescribeBrush(surface.Background)}, expected={palette.Window}）。");
        }

        if (surface.CornerRadius != expectedRadius)
        {
            failures.Add($"{overlay.Name}（{phase}）：系统对比度下圆角改变（actual={surface.CornerRadius}）。");
        }

        if (surface.Effect is not null || HasAnyDropShadowEffect(surface))
        {
            failures.Add($"{overlay.Name}（{phase}）：系统对比度下出现投影。");
        }

        if (surface.BorderBrush is SolidColorBrush hcBorder && surface.Background is SolidColorBrush hcBg)
        {
            var ratio = ComputeContrastRatio(hcBorder.Color, hcBg.Color);
            if (ratio < MinimumBorderContrast)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{overlay.Name}（{phase}）：系统对比度下边界对比度不足 3:1（{ratio:0.00}:1）"));
            }
        }
    }

    private static void VerifyFilterMatchModeAndMutualExclusion(
        MainWindow main,
        DesktopWorkspace workspace,
        MainWindowEmptyStateProbe probe,
        FilterFixtureSeed seed,
        ICollection<string> failures)
    {
        probe.ShowWorkspace(WorkspaceKind.Notes);
        main.Opacity = 1;
        main.Activate();
        SettleLayout(main);

        var filterButton = Required<Button>(main, "FilterButton");
        var filterPopup = Required<Popup>(main, "FilterPopup");
        var filterTagOptions = Required<WrapPanel>(main, "FilterTagOptions");
        var untaggedOnly = Required<ToggleButton>(main, "UntaggedOnly");
        var matchModeGroup = main.FindName("TagMatchModeGroup") as FrameworkElement;
        var anyTagMode = Required<ToggleButton>(main, "AnyTagMode");
        var allTagMode = Required<ToggleButton>(main, "AllTagMode");
        var clearFilterButton = Required<Button>(main, "ClearFilterButton");

        if (matchModeGroup is null)
        {
            failures.Add("筛选弹层缺少 TagMatchModeGroup 匹配方式容器。");
            return;
        }

        // 1) 「无标签」必须在与具体标签芯片相同的 FilterTagOptions 容器内，且芯片间距符合紧凑契约。
        if (!ReferenceEquals(VisualTreeHelper.GetParent(untaggedOnly), filterTagOptions) &&
            !filterTagOptions.Children.Contains(untaggedOnly))
        {
            failures.Add("「无标签」未放在与具体标签芯片相同的 FilterTagOptions 芯片组容器内。");
        }

        if (untaggedOnly.Margin.Right < 8 - GeometryTolerance || untaggedOnly.Margin.Bottom < 8 - GeometryTolerance)
        {
            failures.Add($"「无标签」芯片间距不满足同组 8 DIP 间距（margin={untaggedOnly.Margin}）。");
        }

        ToggleButton FindTagChip(string tagId) =>
            filterTagOptions.Children
                .OfType<ToggleButton>()
                .Single(toggle => string.Equals(toggle.Uid, tagId, StringComparison.OrdinalIgnoreCase));

        void AssertMatchModeCollapsedAndUnfocusable(string stateLabel)
        {
            SettleLayout(main);
            if (matchModeGroup.Visibility != Visibility.Collapsed ||
                matchModeGroup.IsVisible ||
                anyTagMode.IsVisible ||
                allTagMode.IsVisible)
            {
                failures.Add(
                    $"{stateLabel}：匹配方式组必须为 Collapsed 且不可见（group={matchModeGroup.Visibility}/{matchModeGroup.IsVisible}, any={anyTagMode.IsVisible}, all={allTagMode.IsVisible}）。");
            }

            Keyboard.Focus(anyTagMode);
            WindowValidationInput.Pump(main.Dispatcher);
            if (anyTagMode.IsKeyboardFocused || anyTagMode.IsKeyboardFocusWithin)
            {
                failures.Add($"{stateLabel}：折叠态的「任一匹配」仍能直接获得键盘焦点。");
            }

            Keyboard.Focus(allTagMode);
            WindowValidationInput.Pump(main.Dispatcher);
            if (allTagMode.IsKeyboardFocused || allTagMode.IsKeyboardFocusWithin)
            {
                failures.Add($"{stateLabel}：折叠态的「全部匹配」仍能直接获得键盘焦点。");
            }

            var visited = WalkTabLoop(filterPopup, untaggedOnly, main.Dispatcher, out var escaped, out var cycled);
            if (escaped || !cycled)
            {
                failures.Add($"{stateLabel}：筛选弹层 Tab 焦点未能留在层内循环（escaped={escaped}, cycled={cycled}）。");
            }

            if (visited.Any(element => ReferenceEquals(element, anyTagMode) || ReferenceEquals(element, allTagMode)))
            {
                failures.Add($"{stateLabel}：折叠态的匹配方式仍出现在筛选弹层 Tab 循环中。");
            }
        }

        void AssertMatchModeVisibleAndFocusable(string stateLabel, TagMatchMode expectedMode)
        {
            SettleLayout(main);
            if (matchModeGroup.Visibility != Visibility.Visible ||
                !matchModeGroup.IsVisible ||
                !anyTagMode.IsVisible ||
                !allTagMode.IsVisible)
            {
                failures.Add(
                    $"{stateLabel}：已选 ≥2 个具体标签时匹配方式组必须可见（group={matchModeGroup.Visibility}/{matchModeGroup.IsVisible}, any={anyTagMode.IsVisible}, all={allTagMode.IsVisible}）。");
            }

            var expectedAny = expectedMode == TagMatchMode.Any;
            if (anyTagMode.IsChecked != expectedAny ||
                allTagMode.IsChecked == expectedAny ||
                workspace.Snapshot.Filter.MatchMode != expectedMode)
            {
                failures.Add(
                    $"{stateLabel}：匹配方式选中态不符合预期（expected={expectedMode}, any={anyTagMode.IsChecked}, all={allTagMode.IsChecked}, filter={workspace.Snapshot.Filter.MatchMode}）。");
            }

            var visited = WalkTabLoop(filterPopup, untaggedOnly, main.Dispatcher, out var escaped, out var cycled);
            if (escaped || !cycled)
            {
                failures.Add($"{stateLabel}：筛选弹层 Tab 焦点未能留在层内循环（escaped={escaped}, cycled={cycled}）。");
            }

            if (!visited.Any(element => ReferenceEquals(element, anyTagMode)) ||
                !visited.Any(element => ReferenceEquals(element, allTagMode)))
            {
                failures.Add($"{stateLabel}：可见态的「任一匹配」「全部匹配」未全部进入 Tab 循环。");
            }
        }

        void AssertFilteredResultsMatchOracle(string stateLabel, DesktopWorkspaceFilter expectedOracleFilter)
        {
            var snapshot = workspace.Snapshot;
            var expectedCountdowns = snapshot.Settings.Countdowns
                .Where(record => expectedOracleFilter.Matches(record, snapshot.UtcNow))
                .Select(record => record.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var actualCountdowns = snapshot.VisibleCountdowns
                .Select(record => record.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (!expectedCountdowns.SequenceEqual(actualCountdowns, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{stateLabel}：倒计时筛选结果与原定义不一致（expected={string.Join(',', expectedCountdowns)}, actual={string.Join(',', actualCountdowns)}）。");
            }

            var expectedNotes = snapshot.Settings.Notes
                .Where(expectedOracleFilter.Matches)
                .Select(record => record.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var actualNotes = snapshot.VisibleNotes
                .Select(record => record.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (!expectedNotes.SequenceEqual(actualNotes, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{stateLabel}：便签筛选结果与原定义不一致（expected={string.Join(',', expectedNotes)}, actual={string.Join(',', actualNotes)}）。");
            }
        }

        ClickButton(filterButton, main.Dispatcher);
        try
        {
            if (!filterPopup.IsOpen)
            {
                failures.Add("点击筛选按钮未打开筛选弹层。");
                return;
            }

            // State 0: 0 个标签
            AssertMatchModeCollapsedAndUnfocusable("0 个标签");
            AssertFilteredResultsMatchOracle("0 个标签", DesktopWorkspaceFilter.Empty);
            if (workspace.Snapshot.VisibleNotes.Count != 5 || workspace.Snapshot.VisibleCountdowns.Count != 5)
            {
                failures.Add($"0 个标签应显示全部 5 条记录（notes={workspace.Snapshot.VisibleNotes.Count}, countdowns={workspace.Snapshot.VisibleCountdowns.Count}）。");
            }

            // State 1: 选 1 个具体标签（工作）
            ClickToggle(FindTagChip(seed.WorkTagId), main.Dispatcher);
            AssertMatchModeCollapsedAndUnfocusable("1 个具体标签");
            AssertFilteredResultsMatchOracle("1 个具体标签", DesktopWorkspaceFilter.Empty with
            {
                SelectedTagIds = [seed.WorkTagId],
                MatchMode = TagMatchMode.Any
            });
            if (workspace.Snapshot.VisibleNotes.Count != 2 || workspace.Snapshot.VisibleCountdowns.Count != 2)
            {
                failures.Add($"1 个标签（工作）应匹配 2 条记录（notes={workspace.Snapshot.VisibleNotes.Count}, countdowns={workspace.Snapshot.VisibleCountdowns.Count}）。");
            }

            // State 2: 再选第 2 个具体标签（生活）-> 匹配方式出现，默认任一匹配
            ClickToggle(FindTagChip(seed.LifeTagId), main.Dispatcher);
            AssertMatchModeVisibleAndFocusable("2 个具体标签（默认任一匹配）", TagMatchMode.Any);
            AssertFilteredResultsMatchOracle("2 个具体标签-任一匹配", DesktopWorkspaceFilter.Empty with
            {
                SelectedTagIds = [seed.WorkTagId, seed.LifeTagId],
                MatchMode = TagMatchMode.Any
            });
            if (workspace.Snapshot.VisibleNotes.Count != 3 || workspace.Snapshot.VisibleCountdowns.Count != 3)
            {
                failures.Add($"2 个标签任一匹配应匹配 3 条记录（notes={workspace.Snapshot.VisibleNotes.Count}, countdowns={workspace.Snapshot.VisibleCountdowns.Count}）。");
            }

            // State 2b: 切换到「全部匹配」
            ClickToggle(allTagMode, main.Dispatcher);
            AssertMatchModeVisibleAndFocusable("2 个具体标签（切换全部匹配）", TagMatchMode.All);
            AssertFilteredResultsMatchOracle("2 个具体标签-全部匹配", DesktopWorkspaceFilter.Empty with
            {
                SelectedTagIds = [seed.WorkTagId, seed.LifeTagId],
                MatchMode = TagMatchMode.All
            });
            if (workspace.Snapshot.VisibleNotes.Count != 1 || workspace.Snapshot.VisibleCountdowns.Count != 1)
            {
                failures.Add($"2 个标签全部匹配应仅匹配 1 条双标签记录（notes={workspace.Snapshot.VisibleNotes.Count}, countdowns={workspace.Snapshot.VisibleCountdowns.Count}）。");
            }

            // State 2c: 切回「任一匹配」
            ClickToggle(anyTagMode, main.Dispatcher);
            AssertMatchModeVisibleAndFocusable("2 个具体标签（切回任一匹配）", TagMatchMode.Any);
            if (workspace.Snapshot.VisibleNotes.Count != 3 || workspace.Snapshot.VisibleCountdowns.Count != 3)
            {
                failures.Add($"切回任一匹配应恢复 3 条记录（notes={workspace.Snapshot.VisibleNotes.Count}, countdowns={workspace.Snapshot.VisibleCountdowns.Count}）。");
            }

            // State 3: 选第 3 个标签（紧急）-> 匹配方式保持可见
            ClickToggle(FindTagChip(seed.UrgentTagId), main.Dispatcher);
            AssertMatchModeVisibleAndFocusable("3 个具体标签（任一匹配）", TagMatchMode.Any);
            if (workspace.Snapshot.VisibleNotes.Count != 4 || workspace.Snapshot.VisibleCountdowns.Count != 4)
            {
                failures.Add($"3 个标签任一匹配应匹配 4 条记录（notes={workspace.Snapshot.VisibleNotes.Count}, countdowns={workspace.Snapshot.VisibleCountdowns.Count}）。");
            }

            // State 4: 点「无标签」-> 与具体标签互斥，清空已选具体标签并折叠匹配方式
            ClickToggle(untaggedOnly, main.Dispatcher);
            var afterUntagged = workspace.Snapshot.Filter;
            if (!afterUntagged.UntaggedOnly ||
                afterUntagged.SelectedTagIds.Count != 0 ||
                untaggedOnly.IsChecked != true ||
                FindTagChip(seed.WorkTagId).IsChecked == true ||
                FindTagChip(seed.LifeTagId).IsChecked == true ||
                FindTagChip(seed.UrgentTagId).IsChecked == true)
            {
                failures.Add(
                    $"点击「无标签」未与具体标签互斥（untagged={afterUntagged.UntaggedOnly}, selected={afterUntagged.SelectedTagIds.Count}, untaggedChecked={untaggedOnly.IsChecked}）。");
            }

            AssertMatchModeCollapsedAndUnfocusable("选中「无标签」");
            AssertFilteredResultsMatchOracle("选中「无标签」", DesktopWorkspaceFilter.Empty with { UntaggedOnly = true });
            if (workspace.Snapshot.VisibleNotes.Count != 1 || workspace.Snapshot.VisibleCountdowns.Count != 1)
            {
                failures.Add($"「无标签」应仅匹配 1 条无标签记录（notes={workspace.Snapshot.VisibleNotes.Count}, countdowns={workspace.Snapshot.VisibleCountdowns.Count}）。");
            }

            // State 5: 在「无标签」选中态点击具体标签（工作）-> 反向互斥，取消「无标签」
            ClickToggle(FindTagChip(seed.WorkTagId), main.Dispatcher);
            var afterConcrete = workspace.Snapshot.Filter;
            if (afterConcrete.UntaggedOnly ||
                untaggedOnly.IsChecked == true ||
                !afterConcrete.SelectedTagIds.SequenceEqual([seed.WorkTagId], StringComparer.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"「无标签」态下点击具体标签未反向互斥（untagged={afterConcrete.UntaggedOnly}, untaggedChecked={untaggedOnly.IsChecked}, selected={string.Join(',', afterConcrete.SelectedTagIds)}）。");
            }

            AssertMatchModeCollapsedAndUnfocusable("从「无标签」切回 1 个具体标签");
            AssertFilteredResultsMatchOracle("从「无标签」切回 1 个具体标签", DesktopWorkspaceFilter.Empty with
            {
                SelectedTagIds = [seed.WorkTagId]
            });

            // State 6: 选 2 个标签后取消 1 个 -> 回到 1 个标签时匹配方式立即收起
            ClickToggle(FindTagChip(seed.LifeTagId), main.Dispatcher);
            AssertMatchModeVisibleAndFocusable("再次选满 2 个标签", TagMatchMode.Any);
            ClickToggle(FindTagChip(seed.LifeTagId), main.Dispatcher);
            AssertMatchModeCollapsedAndUnfocusable("取消第 2 个标签回到 1 个标签");

            // State 7: 选 2 个标签后删除其中一个标签 -> 剩余 1 个标签时匹配方式自动收起
            ClickToggle(FindTagChip(seed.UrgentTagId), main.Dispatcher);
            AssertMatchModeVisibleAndFocusable("删标签前 2 个标签", TagMatchMode.Any);
            Apply(workspace, new DeleteTagChange(seed.UrgentTagId), main.Dispatcher);
            AssertMatchModeCollapsedAndUnfocusable("删除已选标签降回 1 个标签");

            // State 8: 重置筛选恢复初始态（0 个标签、任一匹配、匹配方式收起）
            ClickButton(clearFilterButton, main.Dispatcher);
            var cleared = workspace.Snapshot.Filter;
            if (cleared.SelectedTagIds.Count != 0 ||
                cleared.UntaggedOnly ||
                cleared.MatchMode != TagMatchMode.Any)
            {
                failures.Add("重置筛选未恢复无标签约束与默认任一匹配。");
            }

            AssertMatchModeCollapsedAndUnfocusable("重置筛选后");
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(filterPopup);
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
            SettleLayout(main);
        }
    }

    private static void VerifyAllOverlayFocusLifecycles(
        MainWindow main,
        FloatingWindow floating,
        DesktopWorkspace workspace,
        MainWindowEmptyStateProbe probe,
        FilterFixtureSeed seed,
        ICollection<string> failures)
    {
        var filterPopup = Required<Popup>(main, "FilterPopup");
        var filterButton = Required<Button>(main, "FilterButton");
        var untaggedOnly = Required<ToggleButton>(main, "UntaggedOnly");
        var anyTagMode = Required<ToggleButton>(main, "AnyTagMode");
        var allTagMode = Required<ToggleButton>(main, "AllTagMode");
        var tagManagerPopup = Required<Popup>(main, "TagManagerPopup");
        var tagManagerButton = Required<Button>(main, "TagManagerButton");
        var newTagInput = Required<TextBox>(main, "NewTagInput");
        var preferencesPopup = Required<Popup>(main, "PreferencesPopup");
        var preferencesButton = Required<Button>(main, "PreferencesButton");
        var displayPrecision = Required<ComboBox>(main, "DisplayPrecision");
        var noteTags = Required<TagSelector>(main, "NoteTags");
        var countdownTags = Required<TagSelector>(main, "CountdownTags");
        var floatingTags = Required<TagSelector>(floating, "FloatingTags");

        // 1) 筛选弹层（0 标签，匹配方式折叠）
        workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
        probe.ShowWorkspace(WorkspaceKind.Notes);
        VerifySingleOverlayFocusLifecycle(
            main,
            "筛选弹层（<2 标签）",
            filterPopup,
            filterButton,
            untaggedOnly,
            expectedInTabLoop: [untaggedOnly],
            forbiddenInTabLoop: [anyTagMode, allTagMode],
            failures);

        // 2) 筛选弹层（2 标签，匹配方式可见）
        workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with { SelectedTagIds = [seed.WorkTagId, seed.LifeTagId] });
        SettleLayout(main);
        VerifySingleOverlayFocusLifecycle(
            main,
            "筛选弹层（≥2 标签）",
            filterPopup,
            filterButton,
            untaggedOnly,
            expectedInTabLoop: [untaggedOnly, anyTagMode, allTagMode],
            forbiddenInTabLoop: Array.Empty<UIElement>(),
            failures);
        workspace.ApplyFilter(DesktopWorkspaceFilter.Empty);
        SettleLayout(main);

        // 3) 标签管理弹层
        VerifySingleOverlayFocusLifecycle(
            main,
            "标签管理弹层",
            tagManagerPopup,
            tagManagerButton,
            newTagInput,
            expectedInTabLoop: [newTagInput],
            forbiddenInTabLoop: Array.Empty<UIElement>(),
            failures);

        // 4) 显示设置弹层
        VerifySingleOverlayFocusLifecycle(
            main,
            "显示设置弹层",
            preferencesPopup,
            preferencesButton,
            displayPrecision,
            expectedInTabLoop: [displayPrecision],
            forbiddenInTabLoop: Array.Empty<UIElement>(),
            failures);

        // 5) 便签页标签选择弹层
        probe.ShowWorkspace(WorkspaceKind.Notes);
        SettleLayout(main);
        VerifySingleOverlayFocusLifecycle(
            main,
            "主窗便签标签选择弹层",
            noteTags.Popup,
            noteTags.PopupTrigger,
            noteTags.PopupInitialFocus,
            expectedInTabLoop: [noteTags.PopupInitialFocus],
            forbiddenInTabLoop: Array.Empty<UIElement>(),
            failures);

        // 6) 倒计时页标签选择弹层（展开编辑区后）
        probe.ShowWorkspace(WorkspaceKind.Countdowns);
        var expandButton = Required<Button>(main, "CountdownEditorExpandButton");
        var expandedPanel = Required<FrameworkElement>(main, "MainCountdownTimeTagRow");
        var wasExpanded = expandedPanel.Visibility == Visibility.Visible;
        if (!wasExpanded)
        {
            ClickButton(expandButton, main.Dispatcher);
        }

        try
        {
            SettleLayout(main);
            VerifySingleOverlayFocusLifecycle(
                main,
                "主窗倒计时标签选择弹层",
                countdownTags.Popup,
                countdownTags.PopupTrigger,
                countdownTags.PopupInitialFocus,
                expectedInTabLoop: [countdownTags.PopupInitialFocus],
                forbiddenInTabLoop: Array.Empty<UIElement>(),
                failures);
        }
        finally
        {
            if (!wasExpanded && expandedPanel.Visibility == Visibility.Visible)
            {
                ClickButton(expandButton, main.Dispatcher);
            }
        }

        // 7) 悬浮窗标签选择弹层（便签工作区展开快速新增后）
        floating.ShowWorkspace(WorkspaceKind.Notes);
        floating.SetQuickAddExpanded(true);
        SettleWindow(floating);
        VerifySingleOverlayFocusLifecycle(
            floating,
            "悬浮窗标签选择弹层",
            floatingTags.Popup,
            floatingTags.PopupTrigger,
            floatingTags.PopupInitialFocus,
            expectedInTabLoop: [floatingTags.PopupInitialFocus],
            forbiddenInTabLoop: Array.Empty<UIElement>(),
            failures);
        floating.SetQuickAddExpanded(false);
    }

    private static void VerifySingleOverlayFocusLifecycle(
        Window hostWindow,
        string label,
        Popup popup,
        Button trigger,
        UIElement expectedFirstFocus,
        IReadOnlyList<UIElement> expectedInTabLoop,
        IReadOnlyList<UIElement> forbiddenInTabLoop,
        ICollection<string> failures)
    {
        WindowValidationInput.ClosePopupForCleanup(popup);
        hostWindow.Opacity = 1;
        hostWindow.Activate();
        Keyboard.Focus(trigger);
        SettleWindow(hostWindow);

        try
        {
            ClickButton(trigger, hostWindow.Dispatcher);
            var openedWithInitialFocus = WindowValidationInput.WaitUntil(
                hostWindow.Dispatcher,
                () => popup.IsOpen &&
                      (ReferenceEquals(Keyboard.FocusedElement, expectedFirstFocus) ||
                       expectedFirstFocus.IsKeyboardFocusWithin),
                1000);
            if (!openedWithInitialFocus)
            {
                failures.Add(
                    $"{label}：打开后未聚焦首项（isOpen={popup.IsOpen}, focused={DescribeFocus(Keyboard.FocusedElement)}, expected={DescribeFocus(expectedFirstFocus)}）。");
                return;
            }

            var firstFocusElement = Keyboard.FocusedElement as UIElement ?? expectedFirstFocus;
            var visited = WalkTabLoop(popup, firstFocusElement, hostWindow.Dispatcher, out var escaped, out var cycled);
            if (escaped)
            {
                failures.Add($"{label}：层内 Tab 焦点逃出了弹层。");
            }

            if (!cycled)
            {
                failures.Add($"{label}：层内 Tab 未循环回到首项（visited={visited.Count}）。");
            }

            foreach (var expected in expectedInTabLoop)
            {
                if (!visited.Any(item => ReferenceEquals(item, expected) || IsDescendantOf(item, expected)))
                {
                    failures.Add($"{label}：层内 Tab 循环漏过了预期控件 {DescribeFocus(expected)}。");
                }
            }

            foreach (var forbidden in forbiddenInTabLoop)
            {
                if (visited.Any(item => ReferenceEquals(item, forbidden) || IsDescendantOf(item, forbidden)))
                {
                    failures.Add($"{label}：层内 Tab 循环错误访问了应折叠控件 {DescribeFocus(forbidden)}。");
                }
            }

            // 从层内按 Esc 关闭弹层并把焦点还给触发按钮。
            var escSource = Keyboard.FocusedElement as UIElement ?? firstFocusElement;
            var handled = WindowValidationInput.RaisePreviewKey(escSource, Key.Escape);
            var closedAndReturned = WindowValidationInput.WaitUntil(
                hostWindow.Dispatcher,
                () => !popup.IsOpen &&
                      (trigger.IsKeyboardFocusWithin ||
                       ReferenceEquals(FocusManager.GetFocusedElement(hostWindow), trigger)),
                1000);
            if (!handled || !closedAndReturned)
            {
                failures.Add(
                    $"{label}：Esc 未关闭弹层或未把焦点还给触发按钮（handled={handled}, isOpen={popup.IsOpen}, focused={DescribeFocus(Keyboard.FocusedElement)}）。");
            }
        }
        finally
        {
            WindowValidationInput.ClosePopupForCleanup(popup);
            SettleWindow(hostWindow);
        }
    }

    private static List<DependencyObject> WalkTabLoop(
        Popup popup,
        UIElement startElement,
        System.Windows.Threading.Dispatcher dispatcher,
        out bool escaped,
        out bool cycled)
    {
        escaped = false;
        cycled = false;
        var visited = new List<DependencyObject>();
        if (popup.Child is not DependencyObject surface || !popup.IsOpen)
        {
            return visited;
        }

        Keyboard.Focus(startElement);
        WindowValidationInput.Pump(dispatcher);
        if (Keyboard.FocusedElement is not DependencyObject first || !IsDescendantOf(first, surface))
        {
            escaped = true;
            return visited;
        }

        visited.Add(first);
        var current = first;
        for (var step = 0; step < 64; step++)
        {
            var moved = current is UIElement uiElement &&
                uiElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            WindowValidationInput.Pump(dispatcher);
            if (!moved || Keyboard.FocusedElement is not DependencyObject next || !IsDescendantOf(next, surface))
            {
                escaped = true;
                return visited;
            }

            if (ReferenceEquals(next, first))
            {
                cycled = true;
                return visited;
            }

            visited.Add(next);
            current = next;
        }

        return visited;
    }

    private static OverlayDescriptor[] CollectOverlayDescriptors(
        MainWindow main,
        FloatingWindow floating,
        MainWindowEmptyStateProbe probe)
    {
        var filterPopup = Required<Popup>(main, "FilterPopup");
        var filterButton = Required<Button>(main, "FilterButton");
        var tagManagerPopup = Required<Popup>(main, "TagManagerPopup");
        var tagManagerButton = Required<Button>(main, "TagManagerButton");
        var preferencesPopup = Required<Popup>(main, "PreferencesPopup");
        var preferencesButton = Required<Button>(main, "PreferencesButton");
        var countdownTags = Required<TagSelector>(main, "CountdownTags");
        var noteTags = Required<TagSelector>(main, "NoteTags");
        var floatingTags = Required<TagSelector>(floating, "FloatingTags");
        var countdownExpandButton = Required<Button>(main, "CountdownEditorExpandButton");
        var countdownExpandedFields = Required<FrameworkElement>(main, "MainCountdownTimeTagRow");

        return
        [
            new OverlayDescriptor(
                "MainWindow.FilterPopup",
                main,
                filterPopup,
                filterButton,
                main,
                ExpectedDayCanvas,
                MainAdjacentSurfaces,
                () =>
                {
                    probe.ShowWorkspace(WorkspaceKind.Notes);
                    SettleLayout(main);
                }),
            new OverlayDescriptor(
                "MainWindow.TagManagerPopup",
                main,
                tagManagerPopup,
                tagManagerButton,
                main,
                ExpectedDayCanvas,
                MainAdjacentSurfaces,
                () =>
                {
                    probe.ShowWorkspace(WorkspaceKind.Notes);
                    SettleLayout(main);
                }),
            new OverlayDescriptor(
                "MainWindow.PreferencesPopup",
                main,
                preferencesPopup,
                preferencesButton,
                main,
                ExpectedDayBackground,
                MainAdjacentSurfaces,
                () =>
                {
                    probe.ShowWorkspace(WorkspaceKind.Countdowns);
                    SettleLayout(main);
                }),
            new OverlayDescriptor(
                "MainWindow.CountdownTags",
                main,
                countdownTags.Popup,
                countdownTags.PopupTrigger,
                main,
                ExpectedDayCanvas,
                MainAdjacentSurfaces,
                () =>
                {
                    probe.ShowWorkspace(WorkspaceKind.Countdowns);
                    if (countdownExpandedFields.Visibility != Visibility.Visible)
                    {
                        ClickButton(countdownExpandButton, main.Dispatcher);
                    }

                    SettleLayout(main);
                }),
            new OverlayDescriptor(
                "MainWindow.NoteTags",
                main,
                noteTags.Popup,
                noteTags.PopupTrigger,
                main,
                ExpectedDayCanvas,
                MainAdjacentSurfaces,
                () =>
                {
                    probe.ShowWorkspace(WorkspaceKind.Notes);
                    SettleLayout(main);
                }),
            new OverlayDescriptor(
                "FloatingWindow.FloatingTags",
                floating,
                floatingTags.Popup,
                floatingTags.PopupTrigger,
                floatingTags,
                ExpectedDayCard,
                FloatingAdjacentSurfaces,
                () =>
                {
                    floating.ShowWorkspace(WorkspaceKind.Notes);
                    floating.SetQuickAddExpanded(true);
                    SettleWindow(floating);
                })
        ];
    }

    private static FilterFixtureSeed SeedFilterFixture(
        DesktopWorkspace workspace,
        System.Windows.Threading.Dispatcher dispatcher)
    {
        Apply(workspace, new CreateTagChange("工作", "#F4B6A6"), dispatcher);
        Apply(workspace, new CreateTagChange("生活", "#F4D58D"), dispatcher);
        Apply(workspace, new CreateTagChange("紧急", "#F9DFDB"), dispatcher);
        var tags = workspace.Snapshot.Settings.Tags;
        var workId = tags.Single(tag => tag.Name == "工作").Id;
        var lifeId = tags.Single(tag => tag.Name == "生活").Id;
        var urgentId = tags.Single(tag => tag.Name == "紧急").Id;

        var beijingDate = workspace.Snapshot.BeijingNow.Date;
        var cases = new (string Label, string[] TagIds, int DayOffset)[]
        {
            ("仅工作", [workId], 2),
            ("仅生活", [lifeId], 3),
            ("工作与生活", [workId, lifeId], 4),
            ("仅紧急", [urgentId], 5),
            ("无标签项", Array.Empty<string>(), 6)
        };
        foreach (var (label, tagIds, dayOffset) in cases)
        {
            Apply(workspace, new SaveCountdownChange(
                null,
                $"倒计时-{label}",
                FixtureUtc.AddDays(dayOffset),
                tagIds,
                Array.Empty<ReminderRule>()), dispatcher);
            Apply(workspace, new SaveNoteChange(
                null,
                $"便签-{label}",
                beijingDate,
                false,
                tagIds,
                Array.Empty<ReminderRule>()), dispatcher);
        }

        return new FilterFixtureSeed(workId, lifeId, urgentId);
    }

    private static void WithWindows(Action<MainWindow, FloatingWindow, DesktopWorkspace> verify)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-overlay-305-{Guid.NewGuid():N}.settings.json");
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        MainWindow? main = null;
        FloatingWindow? floating = null;
        try
        {
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: new FixedClock(FixtureUtc)));
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
            floating = new FloatingWindow(
                workspace,
                calendar,
                main,
                AppContext.BaseDirectory)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = false,
                Opacity = 0
            };
            using var tracking = ValidationExercises.OverrideWindowTrackingCapacityForValidation(
                main,
                new Size(3840, 2160));
            var mainRendered = false;
            main.ContentRendered += (_, _) => mainRendered = true;
            main.Show();
            _ = WindowValidationInput.PumpRenderedFrames(main.Dispatcher, 2000, () => mainRendered);

            var floatingRendered = false;
            floating.ContentRendered += (_, _) => floatingRendered = true;
            floating.Show();
            _ = WindowValidationInput.PumpRenderedFrames(floating.Dispatcher, 2000, () => floatingRendered);

            SettleBoth(main, floating);
            verify(main, floating, workspace);
        }
        finally
        {
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

    private static void OpenPopup(OverlayDescriptor overlay)
    {
        WindowValidationInput.ClosePopupForCleanup(overlay.Popup);
        overlay.HostWindow.Activate();
        SettleWindow(overlay.HostWindow);
        ClickButton(overlay.Trigger, overlay.HostWindow.Dispatcher);
        _ = WindowValidationInput.WaitUntil(overlay.HostWindow.Dispatcher, () => overlay.Popup.IsOpen, 1000);
        SettleWindow(overlay.HostWindow);
    }

    private static void ClosePopup(OverlayDescriptor overlay)
    {
        WindowValidationInput.ClosePopupForCleanup(overlay.Popup);
        SettleWindow(overlay.HostWindow);
    }

    private static void ClickButton(ButtonBase button, System.Windows.Threading.Dispatcher dispatcher)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(dispatcher);
        WindowValidationInput.Pump(dispatcher);
    }

    private static void ClickToggle(ToggleButton toggle, System.Windows.Threading.Dispatcher dispatcher)
    {
        toggle.IsChecked = !(toggle.IsChecked == true);
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WindowValidationInput.Pump(dispatcher);
        WindowValidationInput.Pump(dispatcher);
    }

    private static void SettleBoth(MainWindow main, FloatingWindow floating)
    {
        SettleLayout(main);
        SettleWindow(floating);
    }

    private static void SettleWindow(Window window)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            WindowValidationInput.Pump(window.Dispatcher);
            window.UpdateLayout();
        }
    }

    private static bool HasAnyDropShadowEffect(DependencyObject root)
    {
        if (root is UIElement { Effect: DropShadowEffect })
        {
            return true;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (HasAnyDropShadowEffect(VisualTreeHelper.GetChild(root, index)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDescendantOf(DependencyObject? candidate, DependencyObject root)
    {
        var current = candidate;
        while (current is not null)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static double ComputeContrastRatio(Color first, Color second)
    {
        static double Channel(byte value)
        {
            var srgb = value / 255.0;
            return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) =>
            (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

        var l1 = Luminance(first);
        var l2 = Luminance(second);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static Color ParseColor(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush RequiredBrush(FrameworkElement scope, string key) =>
        scope.FindResource(key) as SolidColorBrush
        ?? throw new InvalidOperationException($"缺少画刷资源 {key}。");

    private static SolidColorBrush RequiredBrush(ResourceDictionary resources, string key) =>
        resources[key] as SolidColorBrush
        ?? throw new InvalidOperationException($"应用资源缺少画刷 {key}。");

    private static CornerRadius RequiredCornerRadius(FrameworkElement scope, string key) =>
        scope.FindResource(key) is CornerRadius radius
            ? radius
            : throw new InvalidOperationException($"缺少圆角资源 {key}。");

    private static T Required<T>(FrameworkElement host, string name) where T : class =>
        host.FindName(name) as T
        ?? throw new InvalidOperationException($"{host.GetType().Name} 缺少控件 {name}（{typeof(T).Name}）。");

    private static string DescribeBrush(Brush? brush) =>
        brush is SolidColorBrush solid ? solid.Color.ToString(CultureInfo.InvariantCulture) : brush?.GetType().Name ?? "null";

    private static string DescribeFocus(object? element) =>
        element switch
        {
            null => "null",
            FrameworkElement fe when !string.IsNullOrEmpty(fe.Name) => $"{fe.GetType().Name}({fe.Name})",
            FrameworkElement fe => fe.GetType().Name,
            _ => element.GetType().Name
        };

    private sealed record OverlayDescriptor(
        string Name,
        Window HostWindow,
        Popup Popup,
        Button Trigger,
        FrameworkElement ResourceScope,
        Color ExpectedDayBackground,
        IReadOnlyList<(string Name, string ResourceKey)> AdjacentSurfaces,
        Action PrepareHost);

    private sealed record FilterFixtureSeed(string WorkTagId, string LifeTagId, string UrgentTagId);
}
