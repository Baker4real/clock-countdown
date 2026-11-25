using System.IO;
using System.Windows;
using System.Windows.Controls;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationExercises
{
    public static bool RunDualWindowValidationFixtureLifecycle()
    {
        var preexisting = Application.Current.Windows.Cast<Window>().ToHashSet();
        string? root = null;
        MainWindow? main = null;
        FloatingWindow? floating = null;
        HolidayCalendar? calendar = null;
        void AssertReleased(string reason)
        {
            Must(root is not null && !Directory.Exists(root), $"#256：{reason}后临时状态未清理：{root}。");
            Must(Application.Current.Windows.Cast<Window>().ToHashSet().SetEquals(preexisting),
                $"#256：{reason}后窗口集合未恢复，或触及了场景前窗口。");
        }

        DualWindowValidationFixture.Run("lifecycle-success", fixture =>
        {
            root = fixture.RootPath;
            var workspace = fixture.Workspace;
            calendar = fixture.Calendar;
            Must(calendar.State == "Pending" && calendar.StatusText == "节假日日历正在加载",
                "#256：fixture日历必须保持原场景的初始Pending状态。");
            Must(workspace.Snapshot.Settings.Notes.Count == 0 && workspace.Snapshot.Settings.Countdowns.Count == 0,
                "#256：新fixture必须从独立空设置开始。");
            MustApply(workspace, new SaveNoteChange(null, "fixture独立便签", new DateTime(2038, 7, 8), false, [], []));
            fixture.ShowWindows(WorkspaceKind.Notes);
            main = fixture.Main;
            floating = fixture.Floating;
            var id = workspace.Snapshot.Settings.Notes.Single().Id;
            foreach (var window in new Window[] { main, floating })
            {
                Must(window.IsVisible && !window.ShowInTaskbar && !window.ShowActivated && window.Opacity == 0 &&
                    window.WindowStartupLocation == WindowStartupLocation.Manual &&
                    VisualDescendants<CheckBox>(window).Any(check => check.Tag as string == id),
                    "#256：fixture必须显示真实共享workspace双窗并保持离屏配置。");
            }
            Must(new SettingsStore().Load(fixture.SettingsPath).Settings.Notes.Single().Id == id,
                "#256：fixture设置必须走真实持久化路径。");
            fixture.Logger.Info("fixture-lifecycle");
            // 同一私有根中的备份、日志轮转和失败写入目录也由fixture清理。
            File.WriteAllText(fixture.SettingsPath + ".backup1", "fixture-backup");
            fixture.WriteTemporaryFile(Path.Combine("settings.json.tmp", "blocked-write"), "fixture");
            File.WriteAllText(fixture.Logger.Path + ".1", "fixture-archive");
            return true;
        });
        AssertReleased("成功场景");
        try
        {
            _ = calendar!.RefreshAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            throw new InvalidOperationException("#256：fixture结束后日历仍允许刷新。");
        }
        catch (ObjectDisposedException) { }

        DualWindowValidationFixture.Run("lifecycle-next", fixture =>
        {
            Must(fixture.RootPath != root && !File.Exists(fixture.SettingsPath) &&
                fixture.Workspace.Snapshot.Settings.Notes.Count == 0,
                "#256：后继fixture不得继承前一场景文件或workspace状态。");
            root = fixture.RootPath;
            fixture.ShowWindows();
            Must(!ReferenceEquals(fixture.Main, main) && !ReferenceEquals(fixture.Floating, floating) &&
                fixture.Main.CurrentWorkspace == WorkspaceKind.Countdowns,
                "#256：后继fixture必须重新创建窗口并恢复默认工作区。");
            return true;
        });
        AssertReleased("后继场景");

        var behaviorFailure = new InvalidOperationException("fixture-behavior-sentinel");
        try
        {
            DualWindowValidationFixture.Run<bool>("lifecycle-behavior-failure", fixture =>
            {
                root = fixture.RootPath;
                fixture.ShowWindows();
                throw behaviorFailure;
            });
            throw new InvalidOperationException("#256：fixture吞掉场景失败。");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, behaviorFailure)) { }
        AssertReleased("行为失败");

        // 用真实解码失败打断构造：分别覆盖MainWindow未返回，以及Main已创建而Floating未返回。
        foreach (var asset in new[] { "main-decorations", "floating-plush" })
        {
            try
            {
                DualWindowValidationFixture.Run("lifecycle-construction-" + asset, fixture =>
                {
                    root = fixture.RootPath;
                    var relativePath = asset == "floating-plush"
                        ? Path.Combine("assets", asset, "component-day-outer-shell.png")
                        : Path.Combine("assets", "sanrio-official-mymelody.png");
                    fixture.WriteTemporaryFile(relativePath, "not a PNG");
                    fixture.ShowWindows(assetRoot: root);
                    return true;
                });
                throw new InvalidOperationException("#256：损坏图片必须在真实窗口构造时失败。");
            }
            catch (DualWindowFixtureException exception) when (exception.Phase == "setup")
            {
                Must(exception.InnerException is not null && exception.RootPath == root,
                    "#256：部分构造失败必须归因setup并保留原始异常与临时根。");
            }
            AssertReleased(asset + "部分构造失败");
        }

        FileStream? locked = null;
        DualWindowFixtureException? cleanupFailure = null;
        try
        {
            try
            {
                DualWindowValidationFixture.Run<bool>("lifecycle-cleanup-failure", fixture =>
                {
                    root = fixture.RootPath;
                    fixture.ShowWindows();
                    var file = fixture.WriteTemporaryFile("locked-evidence", "fixture");
                    locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    throw behaviorFailure;
                });
                throw new InvalidOperationException("#256：锁文件导致的清理失败不得被吞掉。");
            }
            catch (DualWindowFixtureException exception) when (exception.Phase == "cleanup")
            {
                cleanupFailure = exception;
                Must(ReferenceEquals(exception.ScenarioFailure, behaviorFailure) && exception.RootPath == root &&
                    exception.CleanupFailures.Count > 0 && Directory.Exists(root) &&
                    Application.Current.Windows.Cast<Window>().ToHashSet().SetEquals(preexisting),
                    "#256：清理失败必须明确归因、保留行为失败，并继续关闭全部自有窗口。");
            }
        }
        finally
        {
            // 负例只解除自己持有的阻断；窗口、文件与目录仍由同一个fixture有界清理。
            locked?.Dispose();
            cleanupFailure?.RetryCleanup();
        }
        AssertReleased("清理失败负例解除阻断");

        string? unknownFile = null;
        cleanupFailure = null;
        try
        {
            try
            {
                DualWindowValidationFixture.Run("lifecycle-unknown-residue", fixture =>
                {
                    root = fixture.RootPath;
                    unknownFile = Path.Combine(root, "unknown-residue");
                    File.WriteAllText(unknownFile, "must not be swept");
                    return true;
                });
                throw new InvalidOperationException("#256：未知残留必须让清理失败闭锁。");
            }
            catch (DualWindowFixtureException exception) when (exception.Phase == "cleanup")
            {
                cleanupFailure = exception;
                Must(exception.ScenarioFailure is null && File.Exists(unknownFile),
                    "#256：未知文件不得被递归清理或误报成产品行为失败。");
            }
        }
        finally
        {
            if (unknownFile is not null && File.Exists(unknownFile)) File.Delete(unknownFile);
            cleanupFailure?.RetryCleanup();
        }
        AssertReleased("未知残留负例解除阻断");
        Console.Error.WriteLine("DUAL_WINDOW_FIXTURE_LIFECYCLE: setup-equivalent; released; next-isolated; behavior-preserved; partial-main+floating-rollback; cleanup-attributed+retry");
        return true;
    }
}
