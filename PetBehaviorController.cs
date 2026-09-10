using System;
using System.Linq;

namespace YeShunguangPet;

public enum PetActivity { Inactive, Hidden, Exited, Dragging, Settings, Menu, Pointer, Docked, Roaming, RoamPaused, Gesture, Session, Looking, Idle }
public enum AutomaticPetAction { None, Roam, Gesture, Look }
public readonly record struct PetActivityContext(bool Loaded, bool Visible, bool PointerBusy, bool SettingsOpen,
    bool Docked, bool DockAllowsAnimation, bool ClickThrough, bool Quiet, bool Focusing);
public readonly record struct PetBehaviorCapabilities(bool Look, bool Roam, bool RandomActions, bool NeedsFrameTimer);
public readonly record struct PetActivityPlan(PetActivity Activity, bool RunFrames, bool RunMotion, bool Automatic,
    bool SessionAnimation, bool Reminder, bool Click, bool Speech);

public sealed class PetBehaviorController
{
    private readonly TimeProvider _clock;
    private readonly Random _random;
    private long? _idleAt, _roamAt;
    private TimeSpan _idleDelay, _roamDelay;
    private long _motionAt;
    private PetBehaviorRule[]? _rules;
    private long?[] _rulePlayedAt = Array.Empty<long?>();
    public bool UsesPointerRules => _rules?.Any(rule => rule.Condition != BehaviorCondition.Any) == true;
    public RuleDecision LastRuleDecision { get; private set; }
    public int LastRuleIndex { get; private set; } = -1;
    public SpritePlayback Playback { get; }
    public PetState Animation { get; private set; } = PetState.Idle;
    public bool SessionOwnsAnimation { get; private set; }
    public bool IsDragging { get; private set; }
    public bool IsMenuOpen { get; private set; }
    public bool IsRoaming { get; private set; }
    public bool RoamPaused { get; private set; }
    public bool Exited { get; private set; }
    public int LookDirection { get; private set; } = -1;
    public bool IsLooking => LookDirection >= 0;
    public int RoamDirection { get; private set; }
    public double RoamRemaining { get; private set; }

    public PetBehaviorController(TimeProvider? clock = null, Random? random = null)
    {
        _clock = clock ?? TimeProvider.System; _random = random ?? new Random();
        Playback = new SpritePlayback(_clock);
    }

    public PetActivityPlan Evaluate(PetActivityContext context, PetSettings settings, PetBehaviorCapabilities capabilities)
    {
        var active = context.Loaded && context.Visible && !Exited;
        var busy = IsDragging || IsMenuOpen || context.PointerBusy || context.SettingsOpen;
        var suppressed = context.Quiet || (settings.PauseDuringFocus && context.Focusing);
        var automatic = active && !busy && !context.Docked && !IsRoaming && Animation == PetState.Idle && !suppressed;
        var activity = Exited ? PetActivity.Exited : !context.Loaded ? PetActivity.Inactive : !context.Visible ? PetActivity.Hidden :
            IsDragging ? PetActivity.Dragging : context.SettingsOpen ? PetActivity.Settings : IsMenuOpen ? PetActivity.Menu :
            context.PointerBusy ? PetActivity.Pointer : context.Docked ? PetActivity.Docked :
            IsRoaming ? (RoamPaused ? PetActivity.RoamPaused : PetActivity.Roaming) :
            IsLooking ? PetActivity.Looking : SessionOwnsAnimation ? PetActivity.Session :
            Animation != PetState.Idle ? PetActivity.Gesture : PetActivity.Idle;
        return new(activity,
            active && context.DockAllowsAnimation && !IsLooking && capabilities.NeedsFrameTimer,
            active && !busy && !context.Docked && !suppressed && settings.DesktopRoaming && IsRoaming,
            automatic,
            active && context.DockAllowsAnimation && !busy && !IsRoaming && (Animation == PetState.Idle || SessionOwnsAnimation),
            active && context.DockAllowsAnimation && !busy && !context.Quiet && settings.NotificationsEnabled,
            active && context.DockAllowsAnimation && !IsDragging && !IsMenuOpen && !context.SettingsOpen && !context.ClickThrough && settings.ClickInteraction,
            active && !busy && !context.Docked && !IsRoaming);
    }

    public void Play(PetState state, int[] durations, bool loop)
    {
        if (Exited) return;
        Playback.Start(durations, loop);
        Animation = state; SessionOwnsAnimation = false; LookDirection = -1;
    }
    public void SetSessionOwnership(bool owns) { if (!Exited) SessionOwnsAnimation = owns; }
    public void Look(int direction)
    {
        if (Exited || IsDragging || IsMenuOpen || IsRoaming || direction is < 0 or >= 16) return;
        LookDirection = direction; Playback.Pause();
    }
    public void BeginDrag() { if (Exited) return; StopRoaming(); LookDirection = -1; IsDragging = true; }
    public void EndDrag() => IsDragging = false;
    public void BeginMenu() { if (Exited) return; StopRoaming(); IsMenuOpen = true; }
    public void EndMenu() => IsMenuOpen = false;

    public void ConfigureRules(PetBehaviorRules? rules, bool forceReset = false)
    {
        if (Exited) return;
        rules?.Validate();
        var next = rules?.Rules.Select(rule => rule with { }).ToArray();
        if (!forceReset && (next is null && _rules is null || next is not null && _rules is not null && next.SequenceEqual(_rules))) return;
        _rules = next; _rulePlayedAt = new long?[next?.Length ?? 0];
        LastRuleDecision = next is { Length: 0 } ? RuleDecision.Disabled : RuleDecision.None;
        LastRuleIndex = -1; _idleAt = null;
    }

    private TimeSpan CooldownRemaining(int index) => _rulePlayedAt[index] is long last
        ? MaxZero(TimeSpan.FromSeconds(_rules![index].CooldownSeconds) - _clock.GetElapsedTime(last)) : TimeSpan.Zero;
    private bool Matches(int index, bool near) => _rules![index].Condition switch
    {
        BehaviorCondition.CursorNear => near, BehaviorCondition.CursorFar => !near, _ => true
    };
    private bool HasEligibleRule(bool near)
    {
        if (_rules is null) return true;
        var ready = false;
        for (var i = 0; i < _rules.Length; i++)
            if (CooldownRemaining(i) == TimeSpan.Zero) { ready = true; if (Matches(i, near)) return true; }
        LastRuleDecision = _rules.Length == 0 ? RuleDecision.Disabled : ready ? RuleDecision.Condition : RuleDecision.Cooldown;
        LastRuleIndex = -1;
        return false;
    }

    public void ResetSchedule(PetSettings settings, PetBehaviorCapabilities capabilities)
    {
        _idleAt = _roamAt = null; EnsureSchedule(settings, capabilities);
    }
    public void EnsureSchedule(PetSettings settings, PetBehaviorCapabilities capabilities)
    {
        if (Exited) return;
        if (!settings.RandomIdleActions || !capabilities.RandomActions || _rules is { Length: 0 }) _idleAt = null;
        else if (!_idleAt.HasValue) { _idleAt = _clock.GetTimestamp(); _idleDelay = Delay(settings.IdleActionIntervalSeconds); }
        if (!settings.DesktopRoaming || !capabilities.Roam) _roamAt = null;
        else if (!_roamAt.HasValue) { _roamAt = _clock.GetTimestamp(); _roamDelay = Delay(settings.RoamIntervalSeconds); }
    }
    private TimeSpan Delay(int seconds) => TimeSpan.FromSeconds(Math.Max(1, seconds) * (0.75 + _random.NextDouble() * 0.5));
    private TimeSpan Remaining(long? since, TimeSpan delay) => since.HasValue ? MaxZero(delay - _clock.GetElapsedTime(since.Value)) : TimeSpan.MaxValue;
    private static TimeSpan MaxZero(TimeSpan value) => value > TimeSpan.Zero ? value : TimeSpan.Zero;

    public TimeSpan? NextAmbientDelay(PetActivityContext context, PetSettings settings, PetBehaviorCapabilities capabilities)
    {
        if (!Evaluate(context, settings, capabilities).Automatic) return null;
        EnsureSchedule(settings, capabilities);
        if (settings.LookAtMouse && capabilities.Look) return TimeSpan.FromMilliseconds(100);
        var idle = Remaining(_idleAt, _idleDelay); var roam = Remaining(_roamAt, _roamDelay);
        if (_idleAt.HasValue && _rules is { Length: > 0 })
        {
            var cooldown = TimeSpan.MaxValue;
            for (var i = 0; i < _rules.Length; i++) { var remaining = CooldownRemaining(i); if (remaining < cooldown) cooldown = remaining; }
            if (cooldown > idle) idle = cooldown;
        }
        var next = idle < roam ? idle : roam;
        if (next == TimeSpan.MaxValue) return null;
        // A due roam can be held by pointer proximity; do not spin while waiting for it to leave.
        return next <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(100) : next;
    }
    public AutomaticPetAction ChooseAutomatic(PetActivityContext context, PetSettings settings, PetBehaviorCapabilities capabilities, bool cursorNear)
    {
        if (!Evaluate(context, settings, capabilities).Automatic) return AutomaticPetAction.None;
        EnsureSchedule(settings, capabilities);
        if (_roamAt.HasValue && Remaining(_roamAt, _roamDelay) == TimeSpan.Zero && (!settings.PauseNearMouse || !cursorNear)) return AutomaticPetAction.Roam;
        if (_idleAt.HasValue && Remaining(_idleAt, _idleDelay) == TimeSpan.Zero && HasEligibleRule(cursorNear)) { _idleAt = null; return AutomaticPetAction.Gesture; }
        return settings.LookAtMouse && capabilities.Look ? AutomaticPetAction.Look : AutomaticPetAction.None;
    }
    public PetState ChooseGesture(PetState[] actions, bool cursorNear = false)
    {
        if (Exited) return PetState.Idle;
        if (_rules is null) { LastRuleDecision = RuleDecision.Legacy; LastRuleIndex = -1; return actions.Length == 0 ? PetState.Idle : actions[_random.Next(actions.Length)]; }
        var total = 0;
        for (var i = 0; i < _rules.Length; i++)
            if (Matches(i, cursorNear) && CooldownRemaining(i) == TimeSpan.Zero && Array.IndexOf(actions, _rules[i].Action) >= 0) total += _rules[i].Weight;
        if (total == 0) { if (HasEligibleRule(cursorNear)) LastRuleDecision = RuleDecision.Disabled; LastRuleIndex = -1; return PetState.Idle; }
        var selected = _random.Next(total);
        for (var i = 0; i < _rules.Length; i++)
        {
            if (!Matches(i, cursorNear) || CooldownRemaining(i) != TimeSpan.Zero || Array.IndexOf(actions, _rules[i].Action) < 0) continue;
            selected -= _rules[i].Weight;
            if (selected >= 0) continue;
            _rulePlayedAt[i] = _clock.GetTimestamp(); LastRuleDecision = RuleDecision.Selected; LastRuleIndex = i;
            return _rules[i].Action;
        }
        return PetState.Idle;
    }

    public bool StartRoaming(double leftSpace, double rightSpace)
    {
        if (Exited || IsDragging || IsMenuOpen) return false;
        _roamAt = null;
        if (leftSpace < 24 && rightSpace < 24) return false;
        RoamDirection = leftSpace >= 96 && rightSpace >= 96 ? (_random.Next(2) == 0 ? -1 : 1) : (rightSpace >= leftSpace ? 1 : -1);
        RoamRemaining = 96 + _random.NextDouble() * (420 - 96);
        RoamPaused = false; IsRoaming = true; LookDirection = -1;
        _motionAt = _clock.GetTimestamp(); return true;
    }
    public RoamStep AdvanceRoaming(double left, double minimum, double maximum, double speed, bool cursorNear)
    {
        if (!IsRoaming) return new(left, RoamDirection, 0);
        var seconds = Math.Clamp(_clock.GetElapsedTime(_motionAt).TotalSeconds, 0, 0.1);
        _motionAt = _clock.GetTimestamp(); RoamPaused = cursorNear;
        if (cursorNear) return new(left, RoamDirection, RoamRemaining);
        var step = DesktopBehavior.Move(left, RoamDirection, RoamRemaining, speed * seconds, minimum, maximum);
        RoamDirection = step.Direction; RoamRemaining = step.Remaining;
        return step;
    }
    public void StopRoaming() { IsRoaming = RoamPaused = false; RoamRemaining = 0; }
    public void Shutdown()
    {
        Exited = true; StopRoaming(); IsDragging = IsMenuOpen = false; LookDirection = -1;
        _idleAt = _roamAt = null; Playback.Pause();
    }
}
