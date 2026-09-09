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
    internal bool CanShowSpeech => _loadedOnce && IsVisible && !IsDocked && !_isDragging && !_pointerDown && !_isRoaming && !_isMenuOpen && _settingsWindow is null && !_isExiting;

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
