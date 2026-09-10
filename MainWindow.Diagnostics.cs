using System;
using System.Collections.Generic;

namespace YeShunguangPet;

public partial class MainWindow
{
    private List<RuntimeRoleMeter>? _runtimeMeters;
    public bool IsRuntimeObserved => _runtimeMeters is { Count: > 0 };
    internal IDisposable ObserveRuntime(RuntimeRoleMeter meter)
    {
        (_runtimeMeters ??= new()).Add(meter);
        ObserveRuntimeState();
        return new RuntimeLease(this, meter);
    }
    internal void RefreshRuntimeObservation() => ObserveRuntimeState();
    private void ObserveRuntimeTick(RuntimeTick kind)
    {
        if (_runtimeMeters is null) return;
        foreach (var meter in _runtimeMeters) meter.Tick(kind);
    }
    private void SetFrameDelay(TimeSpan delay)
    {
        _frameTimer.Interval = TimerDelay(delay);
        if (_runtimeMeters is null) return;
        foreach (var meter in _runtimeMeters) meter.ScheduleFrame(_frameTimer.Interval);
    }
    private void ObserveRuntimeState()
    {
        if (_runtimeMeters is null) return;
        var timers = (_frameTimer.IsEnabled ? RuntimeTimers.Frame : 0) | (_ambientTimer.IsEnabled ? RuntimeTimers.Ambient : 0) |
            (_roamTimer.IsEnabled ? RuntimeTimers.Roam : 0) | (_dockTimer.IsEnabled ? RuntimeTimers.Dock : 0) | (_clickTimer.IsEnabled ? RuntimeTimers.Click : 0);
        var state = ActivityPlan.Activity;
        foreach (var meter in _runtimeMeters)
            meter.Update(state, _behavior.Animation, _frameIndex, timers, _behavior.LastRuleDecision, _behavior.LastRuleIndex);
    }
    private sealed class RuntimeLease : IDisposable
    {
        private MainWindow? _owner;
        private readonly RuntimeRoleMeter _meter;
        public RuntimeLease(MainWindow owner, RuntimeRoleMeter meter) { _owner = owner; _meter = meter; }
        public void Dispose()
        {
            if (_owner is null) return;
            _owner._runtimeMeters?.Remove(_meter);
            if (_owner._runtimeMeters?.Count == 0) _owner._runtimeMeters = null;
            _owner = null;
        }
    }
}
