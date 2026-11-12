using MinminCountdown.Core;

namespace MinminCountdown;

// #273：独立窗口验证程序集只经这个窄 interface 切换工作区；其余观察走 WPF 公共视觉树。
// 实现放在非 partial adapter，避免再给 MainWindow 增加验证专用 partial 成员。
internal interface IMainWindowEmptyStateProbe
{
    void ShowWorkspace(WorkspaceKind workspace);
}

internal sealed class MainWindowEmptyStateProbe : IMainWindowEmptyStateProbe
{
    private readonly MainWindow _window;

    internal MainWindowEmptyStateProbe(MainWindow window) => _window = window;

    public void ShowWorkspace(WorkspaceKind workspace) => _window.ShowWorkspace(workspace);
}
