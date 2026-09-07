using System;

namespace YeShunguangPet;

public enum SessionPhase { Focus, Break }
public enum SessionStatus { Ready, Running, Paused, Completed }

public sealed class CompanionSession
{
    private readonly TimeProvider _clock;
    private long _startedAt;
    private TimeSpan _remaining;
    private int _focusMinutes = 25;
    private int _breakMinutes = 5;

    public CompanionSession(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _remaining = TimeSpan.FromMinutes(_focusMinutes);
    }

    public SessionPhase Phase { get; private set; } = SessionPhase.Focus;
    public SessionStatus Status { get; private set; } = SessionStatus.Ready;
    public int CompletedFocusSessions { get; private set; }
    public bool IsFocusing => Phase == SessionPhase.Focus && Status == SessionStatus.Running;
    public event Action? Changed;
    public event Action<SessionPhase>? Completed;
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(25);
    public TimeSpan Remaining => Status == SessionStatus.Running
        ? MaxZero(_remaining - _clock.GetElapsedTime(_startedAt))
        : _remaining;

    public void Configure(int focusMinutes, int breakMinutes)
    {
        _focusMinutes = Math.Clamp(focusMinutes, 1, 120);
        _breakMinutes = Math.Clamp(breakMinutes, 1, 60);
        if (Status == SessionStatus.Ready)
            Duration = _remaining = TimeSpan.FromMinutes(_focusMinutes);
        Changed?.Invoke();
    }

    public void StartOrResume()
    {
        if (Status == SessionStatus.Running) return;
        if (Status == SessionStatus.Completed)
            Phase = Phase == SessionPhase.Focus ? SessionPhase.Break : SessionPhase.Focus;
        if (Status != SessionStatus.Paused)
            Duration = _remaining = TimeSpan.FromMinutes(Phase == SessionPhase.Focus ? _focusMinutes : _breakMinutes);
        _startedAt = _clock.GetTimestamp();
        Status = SessionStatus.Running;
        Changed?.Invoke();
    }

    public void Pause()
    {
        Tick();
        if (Status != SessionStatus.Running) return;
        _remaining = Remaining;
        Status = SessionStatus.Paused;
        Changed?.Invoke();
    }

    public void Reset()
    {
        Status = SessionStatus.Ready;
        Phase = SessionPhase.Focus;
        Duration = _remaining = TimeSpan.FromMinutes(_focusMinutes);
        Changed?.Invoke();
    }

    public void SkipBreak()
    {
        if ((Status == SessionStatus.Completed && Phase == SessionPhase.Focus) || Phase == SessionPhase.Break)
            Reset();
    }

    public void Tick()
    {
        if (Status != SessionStatus.Running || Remaining > TimeSpan.Zero) return;
        _remaining = TimeSpan.Zero;
        Status = SessionStatus.Completed;
        if (Phase == SessionPhase.Focus) CompletedFocusSessions++;
        Changed?.Invoke();
        Completed?.Invoke(Phase);
    }

    private static TimeSpan MaxZero(TimeSpan value) => value > TimeSpan.Zero ? value : TimeSpan.Zero;
}

public sealed class BreakReminder
{
    private readonly TimeProvider _clock;
    private long _scheduledAt;
    private TimeSpan _interval;
    private bool _enabled;

    public BreakReminder(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public void Configure(bool enabled, int intervalMinutes)
    {
        _enabled = enabled;
        _interval = TimeSpan.FromMinutes(Math.Clamp(intervalMinutes, 15, 180));
        _scheduledAt = _clock.GetTimestamp();
    }

    public bool Poll(bool suppressed)
    {
        if (!_enabled || _clock.GetElapsedTime(_scheduledAt) < _interval) return false;
        // Missed reminders are discarded, including after sleep or a quiet period.
        _scheduledAt = _clock.GetTimestamp();
        return !suppressed;
    }
}
