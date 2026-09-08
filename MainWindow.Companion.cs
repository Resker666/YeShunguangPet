using System;

namespace YeShunguangPet;

public partial class MainWindow
{
    private readonly CompanionRuntime _companion;
    private readonly CompanionSession _focusSession;
    private bool _companionAttached;
    private bool _wasSuppressed;
    private readonly SessionVisuals _sessionVisuals = new();
    private bool _sessionOwnsAnimation;

    private bool IsQuietNow => DesktopBehavior.IsQuiet(_settings, DateTime.Now);
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
        _sessionOwnsAnimation = state != PetState.Idle;
    }

    private void RefreshSessionAnimation()
    {
        var available = IsVisible && CanPlayDockAnimation && !_isDragging && !_pointerDown &&
            !_clickTimer.IsEnabled && !_isMenuOpen && _settingsWindow is null && !_isRoaming &&
            (_state == PetState.Idle || _sessionOwnsAnimation);
        var target = _sessionVisuals.Resolve(_focusSession, _settings, _pet, IsQuietNow, available);
        if (!target.HasValue || (target == PetState.Idle && !_sessionOwnsAnimation)) return;
        PlayAnimation(target.Value);
        _sessionOwnsAnimation = target != PetState.Idle;
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
        if (!_settings.NotificationsEnabled || IsQuietNow || !IsVisible || !CanPlayDockAnimation ||
            _isDragging || _pointerDown || _clickTimer.IsEnabled || _isMenuOpen || _settingsWindow is not null) return;
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
