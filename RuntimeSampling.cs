using System;
using System.Collections.Generic;
using System.Linq;

namespace YeShunguangPet;

public enum RuntimeTick { Frame, Ambient, Roam, Dock, Click }
[Flags] public enum RuntimeTimers { None = 0, Frame = 1, Ambient = 2, Roam = 4, Dock = 8, Click = 16 }
public sealed record RuntimeRoleSnapshot(int Role, PetActivity Activity, PetState Animation, int Frame, RuntimeTimers Timers,
    long FrameCallbacks, long OtherCallbacks, double FrameLatenessP95Ms, RuleDecision Decision, int RuleIndex);
public sealed record RuntimeTransition(double Seconds, int Role, PetActivity Activity, PetState Animation, RuntimeTimers Timers,
    RuleDecision Decision, int RuleIndex);
public sealed record RuntimeCapture(double Seconds, bool Recording, RuntimeRoleSnapshot[] Roles, RuntimeTransition[] Transitions);

public sealed class RuntimeRoleMeter
{
    private readonly TimeProvider _clock;
    private readonly long _startedAt;
    private readonly int _role;
    private readonly Queue<double> _lateness = new();
    private readonly Queue<RuntimeTransition> _transitions = new();
    private RuntimeRoleSnapshot _state;
    private long? _frameScheduledAt;
    private TimeSpan _frameDelay;
    public int Role => _role;
    public RuntimeRoleMeter(int role, TimeProvider clock, long startedAt)
    {
        _role = role; _clock = clock; _startedAt = startedAt;
        _state = new(role, PetActivity.Inactive, PetState.Idle, 0, RuntimeTimers.None, 0, 0, 0, RuleDecision.None, -1);
    }
    private bool WithinLimit => _clock.GetElapsedTime(_startedAt) < TimeSpan.FromMinutes(5);
    public void ScheduleFrame(TimeSpan delay) { if (WithinLimit) { _frameScheduledAt = _clock.GetTimestamp(); _frameDelay = delay; } }
    public void Tick(RuntimeTick kind)
    {
        if (!WithinLimit) return;
        if (kind != RuntimeTick.Frame) { _state = _state with { OtherCallbacks = _state.OtherCallbacks + 1 }; return; }
        _state = _state with { FrameCallbacks = _state.FrameCallbacks + 1 };
        if (_frameScheduledAt is long start)
        {
            _lateness.Enqueue(Math.Clamp((_clock.GetElapsedTime(start) - _frameDelay).TotalMilliseconds, 0, 300000));
            if (_lateness.Count > 128) _lateness.Dequeue();
        }
    }
    public void Update(PetActivity activity, PetState animation, int frame, RuntimeTimers timers, RuleDecision decision, int ruleIndex)
    {
        if (!WithinLimit) return;
        if (_transitions.Count == 0 || _state.Activity != activity || _state.Animation != animation || _state.Timers != timers || _state.Decision != decision || _state.RuleIndex != ruleIndex)
        {
            _transitions.Enqueue(new(Math.Round(_clock.GetElapsedTime(_startedAt).TotalSeconds, 3), _role, activity, animation, timers, decision, ruleIndex));
            if (_transitions.Count > 128) _transitions.Dequeue();
        }
        _state = _state with { Activity = activity, Animation = animation, Frame = frame, Timers = timers, Decision = decision, RuleIndex = ruleIndex };
    }
    public RuntimeRoleSnapshot Snapshot()
    {
        var values = _lateness.OrderBy(x => x).ToArray();
        return _state with { FrameLatenessP95Ms = values.Length == 0 ? 0 : Math.Round(values[(int)((values.Length - 1) * 0.95)], 2) };
    }
    public RuntimeTransition[] Transitions => _transitions.ToArray();
}

public sealed class RuntimeSamplingSession : IDisposable
{
    private readonly DesktopSession _desktop;
    private readonly Dictionary<MainWindow, (RuntimeRoleMeter Meter, IDisposable Lease)> _roles = new();
    private readonly Queue<RuntimeTransition> _retired = new();
    private readonly Dictionary<int, string> _localNames = new();
    private RuntimeCapture _last = new(0, false, Array.Empty<RuntimeRoleSnapshot>(), Array.Empty<RuntimeTransition>());
    private long _startedAt;
    private int _nextRole;
    private bool _disposed;
    public bool IsRecording { get; private set; }
    internal string LocalRoleName(int role) => _localNames.GetValueOrDefault(role, string.Empty);
    public RuntimeSamplingSession(DesktopSession desktop) => _desktop = desktop;
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Detach(); _retired.Clear(); _localNames.Clear(); _nextRole = 0;
        _startedAt = _desktop.Clock.GetTimestamp(); IsRecording = true; SyncRoles();
    }
    private void SyncRoles()
    {
        foreach (var window in _roles.Keys.Where(w => !_desktop.Windows.Contains(w)).ToArray())
        {
            var entry = _roles[window]; entry.Lease.Dispose(); _roles.Remove(window);
            _localNames.Remove(entry.Meter.Role);
            foreach (var item in entry.Meter.Transitions) { _retired.Enqueue(item); if (_retired.Count > 256) _retired.Dequeue(); }
        }
        foreach (var window in _desktop.Windows)
        {
            if (!_roles.ContainsKey(window))
            {
                var meter = new RuntimeRoleMeter(++_nextRole, _desktop.Clock, _startedAt);
                _roles.Add(window, (meter, window.ObserveRuntime(meter)));
            }
            window.RefreshRuntimeObservation();
            _localNames[_roles[window].Meter.Role] = window.Package.Manifest.Name;
        }
    }
    public RuntimeCapture Capture()
    {
        if (!IsRecording) return _last;
        var seconds = _desktop.Clock.GetElapsedTime(_startedAt).TotalSeconds;
        var done = seconds >= 300;
        if (!done) SyncRoles();
        _last = new(Math.Round(Math.Clamp(seconds, 0, 300), 2), !done,
            _roles.Values.Select(x => x.Meter.Snapshot()).OrderBy(x => x.Role).ToArray(),
            _retired.Concat(_roles.Values.SelectMany(x => x.Meter.Transitions)).OrderBy(x => x.Seconds).ThenBy(x => x.Role).TakeLast(256).ToArray());
        if (done) { IsRecording = false; Detach(); }
        return _last;
    }
    public RuntimeCapture Stop()
    {
        Capture(); IsRecording = false; Detach(); _last = _last with { Recording = false }; return _last;
    }
    private void Detach() { foreach (var item in _roles.Values) item.Lease.Dispose(); _roles.Clear(); }
    public void Dispose() { if (_disposed) return; Stop(); _disposed = true; }
}
