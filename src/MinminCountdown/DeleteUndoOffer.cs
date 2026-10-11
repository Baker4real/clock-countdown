using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace MinminCountdown;

// #214：最近一次删除只有一份撤销载荷。发起删除的窗口负责呈现，恢复仍走 DeletedRecordUndo.ToChange()。
internal enum DeleteUndoSurface
{
    None,
    Main,
    Floating
}

internal sealed class DeleteUndoSlot
{
    public DeletedRecordUndo? Payload { get; private set; }

    public DeleteUndoSurface Surface { get; private set; }

    public event EventHandler? Changed;

    public void Offer(DeleteUndoSurface surface, DeletedRecordUndo payload)
    {
        Payload = payload;
        Surface = surface;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (Payload is null && Surface == DeleteUndoSurface.None)
        {
            return;
        }

        Payload = null;
        Surface = DeleteUndoSurface.None;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

internal static class DeleteUndoOffers
{
    private static readonly ConditionalWeakTable<DesktopWorkspace, DeleteUndoSlot> Slots = new();

    public static DeleteUndoSlot For(DesktopWorkspace workspace) =>
        Slots.GetOrCreateValue(workspace);
}

internal static class DeleteConfirmationCopy
{
    public const string UndoWindowNotice = "删除后 10 秒内可撤销";

    public static string For(string subject) =>
        $"确定删除“{subject}”吗？{UndoWindowNotice}。";
}

// #214：10 秒撤销计时。焦点或悬停暂停，移开后按剩余时间继续，而不是重新算满 10 秒。
internal sealed class DeleteUndoCountdown
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(10);

    private readonly DispatcherTimer _timer;
    private readonly Action _expire;
    private TimeSpan _remaining;
    private DateTime _sliceStartedUtc;
    private bool _armed;
    private bool _paused;
    private bool _focused;
    private bool _hovering;

    public DeleteUndoCountdown(Dispatcher dispatcher, Action expire)
    {
        _expire = expire;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = Duration
        };
        _timer.Tick += OnTick;
    }

    public void Restart()
    {
        _remaining = Duration;
        _armed = true;
        _timer.Stop();
        _paused = _focused || _hovering;
        if (_paused)
        {
            return;
        }

        Arm();
    }

    public void Stop()
    {
        _armed = false;
        _paused = false;
        _timer.Stop();
    }

    public void SetFocused(bool focused)
    {
        if (_focused == focused)
        {
            return;
        }

        _focused = focused;
        ApplyHold();
    }

    public void SetHovering(bool hovering)
    {
        if (_hovering == hovering)
        {
            return;
        }

        _hovering = hovering;
        ApplyHold();
    }

    public void Advance(TimeSpan elapsed)
    {
        if (!_armed || _paused || elapsed <= TimeSpan.Zero)
        {
            return;
        }

        _remaining -= elapsed;
        _timer.Stop();
        if (_remaining <= TimeSpan.Zero)
        {
            _armed = false;
            _remaining = TimeSpan.Zero;
            _expire();
            return;
        }

        Arm();
    }

    private void ApplyHold()
    {
        var shouldPause = _focused || _hovering;
        if (shouldPause == _paused)
        {
            return;
        }

        if (!_armed)
        {
            _paused = shouldPause;
            return;
        }

        if (shouldPause)
        {
            var elapsed = DateTime.UtcNow - _sliceStartedUtc;
            if (elapsed > TimeSpan.Zero)
            {
                _remaining -= elapsed;
            }

            if (_remaining < TimeSpan.Zero)
            {
                _remaining = TimeSpan.Zero;
            }

            _timer.Stop();
            _paused = true;
            return;
        }

        _paused = false;
        if (_remaining <= TimeSpan.Zero)
        {
            _armed = false;
            _expire();
            return;
        }

        Arm();
    }

    private void Arm()
    {
        _sliceStartedUtc = DateTime.UtcNow;
        _timer.Interval = _remaining < TimeSpan.FromMilliseconds(1)
            ? TimeSpan.FromMilliseconds(1)
            : _remaining;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs eventArgs)
    {
        if (!_armed || _paused)
        {
            return;
        }

        _armed = false;
        _remaining = TimeSpan.Zero;
        _timer.Stop();
        _expire();
    }
}
