using System;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class MainWindow
{
    private readonly DesktopSession? _desktop;
    private bool _loadedOnce;
    private bool _confirmingClose;
    private PetAboutWindow? _aboutWindow;
    private IDisposable? _imageLease;
    internal PetSettings InstanceSettings => _settings;
    public string InstanceId { get; }
    public PetPackage Package => _pet;
    public bool IsDocked => IsEdgeDocked;
    public double PetScale => _settings.Scale;
    public bool HasAnimationTimer => _frameTimer.IsEnabled;
    public bool HasAmbientTimer => _ambientTimer.IsEnabled;
    public bool HasRoamingTimer => _roamTimer.IsEnabled;
    public bool HasDockTimer => _dockTimer.IsEnabled;
    private PetActivityContext ActivityContext => new(_loadedOnce, IsVisible, _pointerDown || _clickTimer.IsEnabled,
        _settingsWindow is not null || _openingSettings, IsEdgeDocked, CanPlayDockAnimation, EffectiveClickThrough, IsQuietNow, _focusSession.IsFocusing);
    private PetBehaviorCapabilities BehaviorCapabilities => new(_pet?.CanLook == true, _pet?.CanRoam == true,
        _pet?.RandomActions.Length > 0, _animation is not null && (_animation.FrameCount > 1 || !_animation.Loop));
    public PetActivityPlan ActivityPlan => _behavior.Evaluate(ActivityContext, _settings, BehaviorCapabilities);
    internal bool CanShowSpeech => ActivityPlan.Speech;

    internal void CapturePosition()
    {
        if (!_loadedOnce) return;
        var position = PositionToPersist;
        _settings.Left = position.X;
        _settings.Top = position.Y;
    }

    private void PersistSettings()
    {
        if (_desktop is null) _settings.Save();
        else _desktop.Capture(this);
    }

    public void HideInstance() => HidePet();
    public void ConfigureInstance() => OpenSettings();
    public void ConfigureCompanionSettings() => OpenSettingsCore(companionTab: true);
    internal bool CanEditSpeech => _desktop is not null;
    internal void OpenSpeechSettings(Window owner, PetPackage pet) => _desktop?.OpenSpeechSettings(pet, owner);
    internal void OpenDiagnostics(Window owner)
    {
        if (_desktop is not null) _desktop.OpenDiagnostics(owner);
        else new DiagnosticsWindow(null) { Owner = owner }.ShowDialog();
    }
    internal void UpdateDisplay(double scale, bool topmost, bool edgeAutoHide, bool clickThrough)
    {
        if (_isExiting) return;
        if (Math.Abs(scale - _settings.Scale) > 0.001)
        {
            StopRoaming(returnToIdle: false);
            var edge = LeaveDock();
            var centerX = Left + Width / 2;
            var centerY = Top + Height / 2;
            ApplyScale(scale, save: false);
            if (_loadedOnce)
            {
                Left = centerX - Width / 2;
                Top = centerY - Height / 2;
                EnsureWindowInWorkArea();
                RestoreDock(edge);
            }
            PlayRestingAnimation();
        }
        if (_settings.EdgeAutoHide && !edgeAutoHide) LeaveDock();
        _settings.Topmost = topmost;
        _settings.EdgeAutoHide = edgeAutoHide;
        _settings.ClickThrough = clickThrough;
        ApplyEffectiveWindowOptions();
        CapturePosition();
        UpdateMenuChecks();
    }

    internal void SaveDisplay() { CapturePosition(); PersistSettings(); }
    internal void ActivateSettings() => _settingsWindow?.Activate();
    private void CloseInstance()
    {
        if (_desktop is null || _isExiting || _confirmingClose) return;
        _confirmingClose = true;
        BeginMenuInteraction();
        try
        {
            if (AppDialog.Show(this, $"关闭“{_pet.Manifest.Name}”？\n皮肤文件和学习计时会保留。", "关闭角色", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                _desktop.Remove(this);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法关闭角色", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { _confirmingClose = false; if (!_isExiting) EndMenuInteraction(); }
    }

    private void OpenAbout()
    {
        if (_isExiting) return;
        if (_aboutWindow is not null) { _aboutWindow.Activate(); return; }
        BeginMenuInteraction();
        try
        {
            _aboutWindow = new PetAboutWindow(_pet) { Owner = this };
            _aboutWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to open character introduction.", ex);
            AppDialog.Show(this, ex.Message, "无法打开角色介绍", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _aboutWindow = null; if (!_isExiting) EndMenuInteraction(); }
    }

    internal void ApplyGlobal(CompanionOptions options)
    {
        options.ApplyTo(_settings);
        if (_loadedOnce)
        {
            UpdateAutomaticSuppression();
            RefreshSessionAnimation();
            UpdateMenuChecks();
            RefreshActivityTimers();
        }
    }

    private void RefreshActivityTimers()
    {
        if (_frameTimer is null || _ambientTimer is null || _roamTimer is null) return;
        var plan = ActivityPlan;
        if (plan.RunFrames)
        {
            _behavior.Playback.Resume();
            if (!_frameTimer.IsEnabled)
            {
                SetFrameDelay(_behavior.Playback.Sample().UntilNextFrame);
                _frameTimer.Start();
            }
        }
        else { _frameTimer.Stop(); _behavior.Playback.Pause(); }
        ArmAmbient(_behavior.NextAmbientDelay(ActivityContext, _settings, BehaviorCapabilities));
        if (!plan.RunMotion) _roamTimer.Stop();
        if (!_loadedOnce || !IsVisible || _isExiting) _dockTimer.Stop();
        ObserveRuntimeState();
    }

    private void ArmAmbient(TimeSpan? delay)
    {
        if (!delay.HasValue) { _ambientTimer.Stop(); return; }
        var next = TimerDelay(delay.Value);
        var remaining = _ambientTimer.Interval - _clock.GetElapsedTime(_ambientArmedAt);
        if (_ambientTimer.IsEnabled && remaining <= next + TimeSpan.FromMilliseconds(2)) return;
        _ambientArmedAt = _clock.GetTimestamp();
        _ambientTimer.Interval = next;
        _ambientTimer.Start();
    }
    private static TimeSpan TimerDelay(TimeSpan value) => TimeSpan.FromMilliseconds(Math.Clamp(Math.Ceiling(value.TotalMilliseconds), 1, int.MaxValue - 1));
}
