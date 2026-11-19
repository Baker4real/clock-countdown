using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MinminCountdown;

/// <summary>
/// 独立窗口树外的原生菜单共享同一份暖粉样式，并只在打开期间跟随应用配色。
/// </summary>
internal static class SharedMenuStyles
{
    internal static void Apply(ContextMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        var styles = new ResourceDictionary
        {
            Source = new Uri("/MinminCountdown;component/SharedMenuStyles.xaml", UriKind.Relative)
        };
        menu.Resources.MergedDictionaries.Add(styles);
        menu.Style = (Style)styles["SharedContextMenuStyle"];

        // 应用资源整枚替换时，独立 ContextMenu 不会自动重取局部画刷。
        EventHandler refreshTheme = (_, _) => ApplyThemeResources(menu);
        menu.Opened += (_, _) =>
        {
            ApplyThemeResources(menu);
            ApplicationTheme.PaletteChanged += refreshTheme;
        };
        menu.Closed += (_, _) => ApplicationTheme.PaletteChanged -= refreshTheme;
    }

    private static void ApplyThemeResources(ContextMenu menu)
    {
        ApplicationTheme.SetIsSystemContrast(menu, ApplicationTheme.HighContrastEnabled);
        foreach (var key in new[]
        {
            "CanvasBrush", "ControlBorderBrush", "InkBrush", "MainActionBrush", "MainActionTextBrush",
            "SystemContrastDisabledTextBrush", "SystemContrastControlBrush"
        })
        {
            if (Application.Current?.TryFindResource(key) is Brush brush)
            {
                menu.Resources[key] = brush;
            }
        }
    }
}
