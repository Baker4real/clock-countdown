namespace MinminCountdown;

// #214：撤销槽与 10 秒计时在 DesktopWorkspace；这里只留删除确认文案。
internal static class DeleteConfirmationCopy
{
    public const string UndoWindowNotice = "删除后 10 秒内可撤销";

    public static string For(string subject) =>
        $"确定删除“{subject}”吗？{UndoWindowNotice}。";
}
