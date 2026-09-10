using System;

namespace YeShunguangPet;

public partial class MainWindow
{
    private readonly CompanionRuntime _companion;
    private readonly CompanionSession _focusSession;
    private bool _companionAttached;
    private bool _wasSuppressed;
    private readonly SessionVisuals _sessionVisuals;
    private bool _sessionOwnsAnimation => _behavior.SessionOwnsAnimation;

    private bool IsQuietNow => _settings.DoNotDisturb || (_settings.QuietHoursEnabled && DesktopBehavior.IsQuiet(_settings, _clock.GetLocalNow().DateTime));
    private bool SuppressAutomaticBehavior => IsQuietNow || (_settings.PauseDuringFocus && _focusSession.IsFocusing);

    private void InitializeCompanion()
    {
        if (_companionAttached) return;
        _companionAttached = true;
        _focusSession.Completed += OnSessionCompleted;
        _focusSession.Changed += OnSessionChanged;
        _companion.Pulse += OnCompanionPulse;
        _companion.BreakReminderDue += OnBreakReminder;
        if (_desktop is null) _companion.Start();
        ConfigureCompanion();
    }

    private void ConfigureCompanion()
    {
        if (_desktop is null) _companion.Configure();
        OnCompanionPulse();
    }

    private void OnCompanionPulse()
    {
        UpdateAutomaticSuppression();
        RefreshSessionAnimation();
        RefreshActivityTimers();
    }

    private void OnSessionChanged()
    {
        _sessionVisuals.ClearCompletion();
        OnCompanionPulse();
    }

    private void PlayRestingAnimation()
    {
        var state = SessionVisuals.RestingState(_focusSession, _settings, _pet, IsQuietNow);
        PlayAnimation(state, restart: true);
        _behavior.SetSessionOwnership(state != PetState.Idle);
    }

    private void RefreshSessionAnimation()
    {
        var available = ActivityPlan.SessionAnimation;
        var target = _sessionVisuals.Resolve(_focusSession, _settings, _pet, IsQuietNow, available);
        if (!target.HasValue || (target == PetState.Idle && !_sessionOwnsAnimation)) return;
        PlayAnimation(target.Value);
        _behavior.SetSessionOwnership(target != PetState.Idle);
    }

    private void UpdateAutomaticSuppression()
    {
        var suppressed = SuppressAutomaticBehavior;
        if (suppressed && _isRoaming) StopRoaming(returnToIdle: true);
        if (suppressed && !_wasSuppressed && _isLookMode) PlayAnimation(PetState.Idle, restart: true);
        if (_wasSuppressed && !suppressed) ResetIdleBehaviorSchedule();
        _wasSuppressed = suppressed;
    }

    private void OnSessionCompleted(SessionPhase phase)
    {
        _sessionVisuals.RequestCompletion();
        RefreshSessionAnimation();
    }

    private void OnBreakReminder()
    {
        if (!ActivityPlan.Reminder) return;
        StopRoaming(returnToIdle: true);
        if (_state == PetState.Idle) PlayAnimation(PetState.Waving, restart: true);
    }

    private void OpenFocusWindow()
    {
        CancelPointerInteraction();
        _companion.Open(_pet, InstanceId);
    }

    private void SetDoNotDisturb(bool enabled)
    {
        if (_desktop is not null) _desktop.SetQuiet(enabled);
        else { _settings.DoNotDisturb = enabled; PersistSettings(); OnCompanionPulse(); }
    }

    private void CloseCompanion()
    {
        if (_companionAttached)
        {
            _focusSession.Completed -= OnSessionCompleted;
            _focusSession.Changed -= OnSessionChanged;
            _companion.Pulse -= OnCompanionPulse;
            _companion.BreakReminderDue -= OnBreakReminder;
            _companionAttached = false;
        }
        if (_desktop is null) _companion.Dispose();
    }
}
