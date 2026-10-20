using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MinminCountdown;

/// <summary>
/// 应用固定为单一日间主题；Windows 对比度模式开启时将语义画刷映射为系统色。
/// Tokens 是 Primitive 原色层（App.xaml *Color 资源）的运行时权威副本：
/// 键与顺序与 App.xaml 的第 1 层保持一一对应，新增或修改颜色必须两处同步。
/// 分层与可读性契约见 DESIGN.md §13。
/// </summary>
internal sealed class ApplicationTheme : IDisposable
{
    private static readonly ThemeToken[] Tokens =
    [
        // 画布与表面
        Token("CanvasColor", "CanvasBrush", "#FFFDF3EB"),
        Token("CanvasDeepColor", "CanvasDeepBrush", "#FFFDE8DC"),
        Token("BackgroundColor", "BackgroundBrush", "#FFFDF0E4"),
        Token("CardColor", "CardBrush", "#FFFEFCFB"),
        Token("PanelColor", "PanelBrush", "#FFFFFDFC"),
        Token("CardAltColor", "CardAltBrush", "#FFFEF9F5"),
        Token("StatusBarColor", "StatusBarBrush", "#FFFDFAF8"),
        Token("NoteColor", "NoteBrush", "#FFFEF5DC"),
        Token("NoteWhiteColor", "NoteWhiteBrush", "#FFFEFDFD"),
        Token("NoteLineColor", "NoteLineBrush", "#FFF6D9B2"),
        Token("NoteActiveRailColor", "NoteActiveRailBrush", "#FFCD623E"),
        Token("NoteCompletedRailColor", "NoteCompletedRailBrush", "#FF8A7069"),
        Token("NoteTimelineColor", "NoteTimelineBrush", "#FFF6DFD3"),

        // 文字与结构线
        Token("InkColor", "InkBrush", "#FF3B190D"),
        Token("BodyColor", "BodyBrush", "#FF6B4A3C"),
        Token("MutedColor", "MutedBrush", "#FF82665F"),
        Token("ActiveColor", "ActiveBrush", "#FFE05A4A"),
        Token("ActiveHoverColor", "ActiveHoverBrush", "#FFB84A36"),
        // #151：文本选区高亮独立语义色（不得复用 1px 结构线色）。
        Token("SelectionColor", "SelectionBrush", "#FFD4526A"),
        // #199：主窗口与悬浮窗统一使用深玫粉主行动；Danger 保留砖红。
        Token("MainActionColor", "MainActionBrush", "#FFC23E68"),
        Token("MainActionHoverColor", "MainActionHoverBrush", "#FFB0355D"),
        Token("MainActionPressedColor", "MainActionPressedBrush", "#FF9B2D50"),
        Token("MainActionFocusColor", "MainActionFocusBrush", "#FFA87324"),
        Token("MainActionTextColor", "MainActionTextBrush", "#FFFDFC"),
        // 历史珊瑚语义色；#217 次级按钮使用 CardAlt + ControlBorder。
        Token("CoralActionColor", "CoralActionBrush", "#FFD68481"),
        Token("PeachLineColor", "PeachLineBrush", "#FFF6DFD3"),
        Token("ControlBorderColor", "ControlBorderBrush", "#FFA87866"),
        Token("DangerColor", "DangerBrush", "#FFB74C39"),
        Token("SuccessColor", "SuccessBrush", "#FF266B5E"),
        Token("TagColor", "TagBrush", "#FFF7E8"),
        Token("TagChipTextColor", "TagChipTextBrush", "#FF3B190D"),
        Token("ExpiredColor", "ExpiredBrush", "#FFF9DFDB"),
        Token("SegmentSelectedColor", "SegmentSelectedBrush", "#FFFEEFE1"),
        Token("SegmentSelectedForegroundColor", "SegmentSelectedForegroundBrush", "#FF3B190D"),
        Token("WorkspacePanelColor", "WorkspacePanelBrush", "#00FFFDFC"),
        Token("WorkspacePanelLineColor", "WorkspacePanelLineBrush", "#00F6DFD3"),

        // 强调与控件
        Token("PrimaryCountdownNumberColor", "PrimaryCountdownNumberBrush", "#FFC0102F"),
        Token("PinkTintColor", "PinkTintBrush", "#FFFEDDDE"),
        Token("PinkAccentStrongColor", "PinkAccentStrongBrush", "#FFC23E68"),
        Token("PinkBadgeColor", "PinkBadgeBrush", "#FFFD97A1"),
        Token("PinkBlockColor", "PinkBlockBrush", "#FEE0E4"),
        Token("PinkTintSoftColor", "PinkTintSoftBrush", "#FEF0F1"),
        Token("SecondaryBlockColor", "SecondaryBlockBrush", "#FAF0EA"),
        Token("PeachTintColor", "PeachTintBrush", "#FEEFE1"),
        Token("PeachRailColor", "PeachRailBrush", "#CD623E"),
        Token("PeachBadgeColor", "PeachBadgeBrush", "#FBAE90"),
        Token("PeachBadgeMutedColor", "PeachBadgeMutedBrush", "#F3DED8"),
        Token("IconAccentColor", "IconAccentBrush", "#F28995"),

        // 便签与提醒
        Token("WarningAccentColor", "WarningAccentBrush", "#FFAD4A10"),
        Token("WarningTintColor", "WarningTintBrush", "#FDEDD8"),
        Token("ReminderCardColor", "ReminderCardBrush", "#FEF9E8"),
        Token("ReminderPageColor", "ReminderPageBrush", "#FEFEFE"),
        Token("SourceNoteIconColor", "SourceNoteIconBrush", "#FED67F"),
        Token("DecoTextColor", "DecoTextBrush", "#D9B7B1"),
        Token("DecoTextSoftColor", "DecoTextSoftBrush", "#E5AE9C"),

        // 日期印记 pastel 轮换
        Token("StampPinkColor", "StampPinkBrush", "#FEEFF1"),
        Token("StampPinkTextColor", "StampPinkTextBrush", "#D41F57"),
        Token("StampBlueColor", "StampBlueBrush", "#EEF5FE"),
        Token("StampBlueTextColor", "StampBlueTextBrush", "#FF1D6BB0"),
        Token("StampGreenColor", "StampGreenBrush", "#EDF6E8"),
        Token("StampGreenTextColor", "StampGreenTextBrush", "#FF26732D"),

        // 角色面板（三丽鸥玩偶泊位）
        Token("CharacterPanelColor", "CharacterPanelBrush", "#FFF7E8"),
        Token("CharacterLineColor", "CharacterLineBrush", "#EFC9A7"),
        Token("CharacterMyMelodyDockColor", "CharacterMyMelodyDockBrush", "#FFE3C7"),
        Token("CharacterCinnamorollDockColor", "CharacterCinnamorollDockBrush", "#FFF0D2"),
        Token("CharacterKuromiDockColor", "CharacterKuromiDockBrush", "#F5DEC5"),

        // 悬浮窗组件原色
        Token("FloatingDayPrimaryCountdownAccentColor", "FloatingDayPrimaryCountdownAccentBrush", "#EE2B46"),
        Token("FloatingDayPlushGlowColor", "FloatingDayPlushGlowBrush", "#B9794E")
    ];

    // 主题画刷一律是冻结的 SolidColorBrush：配色变化时整枚替换资源条目，不就地改色。
    // 就地改色会让 WPF 渲染线程遍历共享画刷的监听者，曾因悬空监听者以 0xC0000409 崩溃。
    // 窗口内的 DynamicResource 自动重取；复制了画刷实例的局部字典与窗口树外的托盘菜单
    // 由各自的主题入口按应用资源重取。
    private static readonly (string Brush, string Color)[] ComponentAliases =
    [
        ("FloatingDayPanelBrush", "CardColor"),
        ("FloatingDayBackdropBrush", "BackgroundColor"),
        ("FloatingDayBorderBrush", "PeachLineColor"),
        ("FloatingDayHeaderBrush", "NoteColor"),
        ("FloatingDayHeaderTextBrush", "InkColor"),
        ("FloatingDayHeaderDangerBrush", "DangerColor"),
        ("FloatingDayHeaderHoverBrush", "CardAltColor"),
        ("FloatingDayHeaderSelectedBrush", "PinkAccentStrongColor"),
        ("FloatingDayHeaderSelectedTextBrush", "MainActionTextColor"),
        ("FloatingDaySurfaceBrush", "CardColor"),
        ("FloatingDayNoteCardBrush", "NoteColor"),
        ("FloatingDayNoteInputBrush", "NoteColor"),
        ("FloatingDayNoteInputTextBrush", "InkColor"),
        ("FloatingDayErrorBrush", "DangerColor"),
        ("FloatingDayTextBrush", "InkColor"),
        ("FloatingDayMutedBrush", "MutedColor"),
        ("FloatingDayMyMelodyDockBrush", "CharacterMyMelodyDockColor"),
        ("FloatingDayCinnamorollDockBrush", "CharacterCinnamorollDockColor"),
        ("FloatingDayKuromiDockBrush", "CharacterKuromiDockColor"),
        ("FloatingHeaderTextBrush", "InkColor"),
        ("FloatingHeaderDangerBrush", "DangerColor"),
        ("FloatingHeaderHoverBrush", "CardAltColor"),
        ("FloatingHeaderSelectedBrush", "PinkAccentStrongColor"),
        ("FloatingHeaderSelectedTextBrush", "MainActionTextColor"),
        ("FloatingNoteInputTextBrush", "InkColor"),
        ("FloatingErrorBrush", "DangerColor"),
        ("FloatingMutedBrush", "MutedColor")
    ];

    private readonly ResourceDictionary _resources;
    private bool _disposed;
    private static bool? _validationAnimationPreference;
    private static bool? _validationHighContrast;
    private static SystemContrastPalette? _validationSystemColors;
    private static event EventHandler? SystemPreferenceChanged;
    internal static event EventHandler? PaletteChanged;
    public static readonly DependencyProperty IsSystemContrastProperty = DependencyProperty.RegisterAttached(
        "IsSystemContrast", typeof(bool), typeof(ApplicationTheme),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetIsSystemContrast(DependencyObject element) => (bool)element.GetValue(IsSystemContrastProperty);
    public static void SetIsSystemContrast(DependencyObject element, bool value) => element.SetValue(IsSystemContrastProperty, value);

    static ApplicationTheme()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((Window)sender).SetResourceReference(IsSystemContrastProperty, "SystemContrastEnabled")));
    }

    internal static bool HighContrastEnabled => _validationHighContrast ?? SystemParameters.HighContrast;

    internal static Color DayColorFor(string brushKey) => Tokens.First(token => token.BrushKey == brushKey).Color;

    internal static HighContrastPreferenceScope OverrideHighContrastForValidation(bool enabled)
    {
        EnsureMotionValidationContext();
        if (_validationHighContrast.HasValue)
        {
            throw new InvalidOperationException("对比度验证偏好作用域不能嵌套。");
        }

        _validationHighContrast = enabled;
        SystemPreferenceChanged?.Invoke(null, EventArgs.Empty);
        return new HighContrastPreferenceScope();
    }

    internal sealed record SystemContrastPalette(Color Window, Color WindowText, Color Control,
        Color ControlText, Color Highlight, Color HighlightText, Color GrayText);

    internal sealed class HighContrastPreferenceScope : IDisposable
    {
        internal void ChangeColors(SystemContrastPalette colors)
        {
            EnsureMotionValidationContext();
            _validationSystemColors = colors;
            SystemPreferenceChanged?.Invoke(null, EventArgs.Empty);
        }

        public void Dispose()
        {
            Application.Current.Dispatcher.VerifyAccess();
            _validationHighContrast = null;
            _validationSystemColors = null;
            SystemPreferenceChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public ApplicationTheme(ResourceDictionary resources)
    {
        _resources = resources;
        SystemPreferenceChanged += OnSystemPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Apply()
    {
        var systemColors = _validationSystemColors ?? new SystemContrastPalette(SystemColors.WindowColor,
            SystemColors.WindowTextColor, SystemColors.ControlColor, SystemColors.ControlTextColor,
            SystemColors.HighlightColor, SystemColors.HighlightTextColor, SystemColors.GrayTextColor);
        // 批量写入：逐键写入时每个键都会遍历全部窗口失效一次，EndInit 只遍历一次。
        _resources.BeginInit();
        try
        {
            _resources["SystemContrastEnabled"] = HighContrastEnabled;
            SetBrush("SystemContrastDisabledTextBrush", systemColors.GrayText);
            SetBrush("SystemContrastControlBrush", systemColors.Control);
            var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
            foreach (var token in Tokens)
            {
                var color = HighContrastEnabled ? SystemColorFor(token.BrushKey, systemColors) : token.Color;
                colors.Add(token.ColorKey, color);
                _resources[token.ColorKey] = color;
                SetBrush(token.BrushKey, color);
            }
            foreach (var alias in ComponentAliases)
            {
                SetBrush(alias.Brush, colors[alias.Color]);
            }
            ApplyMotionTokens();
        }
        finally
        {
            _resources.EndInit();
        }
        foreach (Window window in Application.Current.Windows)
        {
            if (window is MainWindow main) main.ApplyTheme();
            if (window is FloatingWindow floating) floating.ApplyTheme();
            if (window is ConfirmationDialog confirmation) confirmation.ApplyTheme();
        }
        PaletteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetBrush(string key, Color color)
    {
        if (_resources[key] is SolidColorBrush { IsFrozen: true } current && current.Color == color) return;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _resources[key] = brush;
    }

    internal static void ApplyContrastAwareBrush(System.Windows.Controls.Border surface,
        DependencyProperty property, Brush dayBrush, string contrastKey)
    {
        var style = new Style(typeof(System.Windows.Controls.Border), surface.Style);
        style.Setters.Add(new Setter(property, dayBrush));
        var trigger = new Trigger { Property = IsSystemContrastProperty, Value = true };
        trigger.Setters.Add(new Setter(property, new DynamicResourceExtension(contrastKey)));
        style.Triggers.Add(trigger);
        surface.Style = style;
    }

    private static Color SystemColorFor(string key, SystemContrastPalette palette) => key switch
    {
        "MainActionBrush" or "MainActionHoverBrush" or "MainActionPressedBrush" or
        "PinkAccentStrongBrush" or "SelectionBrush" or "SegmentSelectedBrush" or "SuccessBrush" => palette.Highlight,
        "MainActionTextBrush" or "SegmentSelectedForegroundBrush" => palette.HighlightText,
        "ActiveBrush" or "ActiveHoverBrush" or "MainActionFocusBrush" or
        "NoteActiveRailBrush" or "PeachRailBrush" or "FloatingDayPrimaryCountdownAccentBrush" => palette.Highlight,
        "InkBrush" or "BodyBrush" or "MutedBrush" or "ControlBorderBrush" or "PeachLineBrush" or
        "NoteLineBrush" or "NoteCompletedRailBrush" or "NoteTimelineBrush" or "DangerBrush" or
        "PrimaryCountdownNumberBrush" or "IconAccentBrush" or
        "WarningAccentBrush" or "DecoTextBrush" or "DecoTextSoftBrush" or
        "StampPinkTextBrush" or "StampBlueTextBrush" or "StampGreenTextBrush" or
        "CharacterLineBrush" or "WorkspacePanelLineBrush" => palette.WindowText,
        "TagBrush" => palette.Control,
        "TagChipTextBrush" => palette.ControlText,
        "CardAltBrush" or "StatusBarBrush" or "CoralActionBrush" or
        "SourceNoteIconBrush" or "PinkTintBrush" or "PinkBadgeBrush" or "PinkBlockBrush" or
        "PinkTintSoftBrush" or "SecondaryBlockBrush" or "PeachTintBrush" or "PeachBadgeBrush" or
        "PeachBadgeMutedBrush" => palette.Window,
        "CanvasBrush" or "CanvasDeepBrush" or "BackgroundBrush" or "CardBrush" or "PanelBrush" or
        "NoteBrush" or "NoteWhiteBrush" or "ExpiredBrush" or "WorkspacePanelBrush" or "WarningTintBrush" or
        "ReminderCardBrush" or "ReminderPageBrush" or "StampPinkBrush" or "StampBlueBrush" or "StampGreenBrush" or
        "CharacterPanelBrush" or "CharacterMyMelodyDockBrush" or "CharacterCinnamorollDockBrush" or
        "CharacterKuromiDockBrush" or "FloatingDayPlushGlowBrush" => palette.Window,
        _ => throw new InvalidOperationException($"缺少系统对比度映射：{key}")
    };

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName is nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation))
            OnSystemPreferenceChanged(sender, EventArgs.Empty);
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs args)
    {
        if (args.Category is Microsoft.Win32.UserPreferenceCategory.Accessibility or
            Microsoft.Win32.UserPreferenceCategory.Color or Microsoft.Win32.UserPreferenceCategory.VisualStyle)
            OnSystemPreferenceChanged(sender, EventArgs.Empty);
    }

    private void OnSystemPreferenceChanged(object? sender, EventArgs args)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (_disposed || dispatcher.HasShutdownStarted) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => { if (!_disposed) Apply(); });
            return;
        }
        Apply();
    }

    public void Dispose()
    {
        _disposed = true;
        SystemPreferenceChanged -= OnSystemPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void ApplyMotionTokens()
    {
        var enabled = ClientAreaAnimationEnabled;
        _resources["MotionHoverDuration"] = new Duration(
            enabled ? MotionTokens.HoverDuration : TimeSpan.Zero);
        _resources["MotionPressDuration"] = new Duration(
            enabled ? MotionTokens.PressDuration : TimeSpan.Zero);
        _resources["MotionPopupEnterDuration"] = new Duration(
            enabled ? MotionTokens.PopupEnterDuration : TimeSpan.Zero);
        _resources["MotionPopupExitDuration"] = new Duration(
            enabled ? MotionTokens.PopupExitDuration : TimeSpan.Zero);
        _resources["MotionIndicatorDuration"] = new Duration(
            enabled ? MotionTokens.IndicatorDuration : TimeSpan.Zero);
        _resources["MotionCountdownDuration"] = new Duration(
            enabled ? MotionTokens.CountdownDuration : TimeSpan.Zero);
        _resources["MotionNoteCompletionExitDuration"] = new Duration(
            enabled ? MotionTokens.NoteCompletionExitDuration : TimeSpan.Zero);
        _resources["MotionEaseOut"] = MotionTokens.CreateEaseOut();
        _resources["MotionCubicEaseOut"] = new CubicEase
        {
            EasingMode = EasingMode.EaseOut
        };
    }

    internal static bool MeetsContrast(Brush background, Brush foreground, double minimum)
    {
        return background is SolidColorBrush backgroundBrush &&
            foreground is SolidColorBrush foregroundBrush &&
            MeetsContrast(backgroundBrush.Color, foregroundBrush.Color, minimum);
    }

    internal static bool MeetsContrast(Color first, Color second, double minimum)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255d;
                return normalized <= 0.04045
                    ? normalized / 12.92
                    : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(color.R) +
                0.7152 * Channel(color.G) +
                0.0722 * Channel(color.B);
        }

        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        var ratio = (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
        return ratio >= minimum;
    }

    // #194：所有动效的即时等价入口——读取系统「客户区动画」开关
    //（SystemParameters.ClientAreaAnimation）。系统关闭时每个动效 token 都变为零时长。
    internal static bool ClientAreaAnimationEnabled =>
        _validationAnimationPreference ?? SystemParameters.ClientAreaAnimation;

    internal static TimeSpan GetEffectiveMotionDuration(TimeSpan enabledDuration) =>
        ClientAreaAnimationEnabled ? enabledDuration : TimeSpan.Zero;

    internal static bool HasAnimationPreferenceOverrideForValidation =>
        _validationAnimationPreference.HasValue;

    internal static IDisposable OverrideAnimationPreferenceForValidation(bool enabled)
    {
        EnsureMotionValidationContext();
        if (_validationAnimationPreference.HasValue)
        {
            throw new InvalidOperationException("动效验证偏好作用域不能嵌套。");
        }

        _validationAnimationPreference = enabled;
        return new AnimationPreferenceScope(Application.Current.Dispatcher);
    }

    internal static void EnsureMotionValidationContext()
    {
        var application = Application.Current ??
            throw new InvalidOperationException("动效验证需要已初始化的 WPF Application。");
        application.Dispatcher.VerifyAccess();
        if (System.Threading.Thread.CurrentThread.GetApartmentState() != System.Threading.ApartmentState.STA ||
            string.Equals(Environment.GetEnvironmentVariable("MINMIN_SUITE_ALLOW_UNISOLATED"), "1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("动效验证输入只允许 STA 隔离桌面，不能使用非隔离绕过。");
        }

        ApplicationValidation.EnsureIsolatedValidationDesktop();
    }

    private sealed class AnimationPreferenceScope(System.Windows.Threading.Dispatcher dispatcher) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            dispatcher.VerifyAccess();
            if (!_disposed)
            {
                _validationAnimationPreference = null;
                _disposed = true;
            }
        }
    }

    private static ThemeToken Token(
        string colorKey,
        string brushKey,
        string dayColor) =>
        new(colorKey, brushKey, ParseColor(dayColor));

    private static Color ParseColor(string value) =>
        ColorConverter.ConvertFromString(value) is Color color
            ? color
            : throw new InvalidOperationException($"无法解析主题颜色：{value}");

    private sealed record ThemeToken(
        string ColorKey,
        string BrushKey,
        Color Color);
}

internal static class MotionTokens
{
    internal static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(120);
    internal static readonly TimeSpan PressDuration = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan PopupEnterDuration = TimeSpan.FromMilliseconds(140);
    internal static readonly TimeSpan PopupExitDuration = TimeSpan.FromMilliseconds(90);
    internal static readonly TimeSpan IndicatorDuration = TimeSpan.FromMilliseconds(160);
    internal static readonly TimeSpan CountdownDuration = TimeSpan.FromMilliseconds(90);
    internal static readonly TimeSpan NoteCompletionExitDuration = TimeSpan.FromMilliseconds(160);

    internal static PowerEase CreateEaseOut() => new()
    {
        EasingMode = EasingMode.EaseOut,
        Power = 4
    };
}
