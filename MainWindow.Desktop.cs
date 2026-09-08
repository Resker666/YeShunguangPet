using System;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class MainWindow
{
    private readonly DesktopSession? _desktop;
    private bool _loadedOnce;
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
        try { _desktop?.Remove(this); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法关闭角色", MessageBoxButton.OK, MessageBoxImage.Information); }
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
        var visible = _loadedOnce && IsVisible && !_isExiting && _pet is not null;
        SetTimer(_frameTimer, visible && CanPlayDockAnimation && !_isLookMode);
        SetTimer(_ambientTimer, visible && !IsEdgeDocked && !_isRoaming && _state == PetState.Idle &&
            ((_settings.LookAtMouse && _pet!.CanLook) || (_settings.RandomIdleActions && _pet!.RandomActions.Length > 0) || (_settings.DesktopRoaming && _pet!.CanRoam)));
        if (!visible) { _roamTimer.Stop(); _dockTimer.Stop(); }
    }

    private static void SetTimer(DispatcherTimer timer, bool enabled)
    {
        if (enabled) timer.Start();
        else timer.Stop();
    }
}
