using System;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace YeShunguangPet;

public partial class MainWindow
{
    private readonly CompanionSession _focusSession = new();
    private readonly BreakReminder _breakReminder = new();
    private readonly DispatcherTimer _companionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private FocusWindow? _focusWindow;
    private WinForms.ToolStripMenuItem? _trayQuietItem;
    private bool _wasSuppressed;
    private readonly SessionVisuals _sessionVisuals = new();
    private bool _sessionOwnsAnimation;

    private bool IsQuietNow => DesktopBehavior.IsQuiet(_settings, DateTime.Now);
    private bool SuppressAutomaticBehavior => IsQuietNow || (_settings.PauseDuringFocus && _focusSession.IsFocusing);

    private void InitializeCompanion()
    {
        _focusSession.Completed += OnSessionCompleted;
        _focusSession.Changed += OnSessionChanged;
        _companionTimer.Tick += (_, _) =>
        {
            _focusSession.Tick();
            UpdateAutomaticSuppression();
            RefreshSessionAnimation();
            if (_breakReminder.Poll(IsQuietNow || !_settings.NotificationsEnabled || _focusSession.Status == SessionStatus.Running))
                NotifyCompanion("休息一下", "站起来活动，喝口水，放松一下眼睛。");
        };
        ConfigureCompanion();
        _companionTimer.Start();
    }

    private void ConfigureCompanion()
    {
        _focusSession.Configure(_settings.FocusMinutes, _settings.BreakMinutes);
        _breakReminder.Configure(_settings.BreakRemindersEnabled, _settings.BreakReminderMinutes);
        UpdateAutomaticSuppression();
        RefreshSessionAnimation();
    }

    private void OnSessionChanged()
    {
        _sessionVisuals.ClearCompletion();
        UpdateAutomaticSuppression();
        RefreshSessionAnimation();
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
        _breakReminder.Configure(_settings.BreakRemindersEnabled, _settings.BreakReminderMinutes);
        var title = phase == SessionPhase.Focus ? "专注完成" : "休息结束";
        var message = phase == SessionPhase.Focus
            ? $"完成一次专注，可以开始 {_settings.BreakMinutes} 分钟休息。"
            : "准备好了就开始下一次专注。";
        NotifyCompanion(title, message, playGesture: false);
        _sessionVisuals.RequestCompletion();
        RefreshSessionAnimation();
    }

    private void NotifyCompanion(string title, string message, bool playGesture = true)
    {
        if (!_settings.NotificationsEnabled || IsQuietNow) return;
        _trayIcon?.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.None);
        if (playGesture && IsVisible && CanPlayDockAnimation && !_isDragging && !_pointerDown && !_clickTimer.IsEnabled && !_isMenuOpen && _settingsWindow is null)
        {
            StopRoaming(returnToIdle: true);
            if (_state == PetState.Idle) PlayAnimation(PetState.Waving, restart: true);
        }
    }

    private void OpenFocusWindow()
    {
        CancelPointerInteraction();
        if (_focusWindow is not null)
        {
            if (_focusWindow.WindowState == WindowState.Minimized) _focusWindow.WindowState = WindowState.Normal;
            _focusWindow.Activate();
            return;
        }
        _focusWindow = new FocusWindow(_focusSession, _settings, _pet, () => IsQuietNow);
        _focusWindow.Closed += (_, _) => _focusWindow = null;
        _focusWindow.Show();
    }

    private void SetDoNotDisturb(bool enabled)
    {
        _settings.DoNotDisturb = enabled;
        _settings.Save();
        if (_trayQuietItem is not null) _trayQuietItem.Checked = enabled;
        UpdateAutomaticSuppression();
        RefreshSessionAnimation();
    }

    private void CloseCompanion()
    {
        _companionTimer.Stop();
        _focusSession.Completed -= OnSessionCompleted;
        _focusSession.Changed -= OnSessionChanged;
        _focusWindow?.Close();
    }
}
