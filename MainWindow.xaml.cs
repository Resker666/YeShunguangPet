using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace YeShunguangPet;

public partial class MainWindow : Window
{
    private const double MinScale = 0.5;
    private const double MaxScale = 2.5;
    private const double ScaleStep = 0.1;
    private const double LookRadius = 520;
    private const double LookDeadZone = 42;
    private const double MinRoamDistance = 96;
    private const double MaxRoamDistance = 420;

    private const int SummonHotkeyId = 0x5911;

    private readonly PetSettings _settings;
    private readonly PetCatalog _petCatalog;
    private readonly Dictionary<(int Row, int Column), BitmapSource> _frameCache = new();
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _ambientTimer;
    private readonly DispatcherTimer _roamTimer;
    private readonly Random _random = new();

    private PetPackage _pet = null!;
    private PetAnimation _animation = null!;
    private PetState _state = PetState.Idle;
    private int _frameIndex;
    private int _lastLookDirection = -1;
    private bool _isLookMode;
    private bool _isDragging;
    private bool _isMenuOpen;
    private bool _isRoaming;
    private bool _isExiting;
    private bool _sourceReady;
    private double _lastDragLeft;
    private double _roamRemainingDistance;
    private bool _roamPaused;
    private int _roamDirection;
    private DateTime _lastRoamTickUtc;
    private DateTime _nextIdleActionUtc = DateTime.MaxValue;
    private DateTime _nextRoamUtc = DateTime.MaxValue;
    private HwndSource? _windowSource;
    private SettingsWindow? _settingsWindow;

    private MenuItem? _windowTopmostItem;
    private MenuItem? _windowClickThroughItem;
    private MenuItem? _windowStartupItem;
    private MenuItem? _windowRandomIdleItem;
    private MenuItem? _windowRoamingItem;

    public MainWindow() : this(PetSettings.Load(), null, null, string.Empty) { }

    internal MainWindow(PetSettings settings, PetPackage? package, DesktopSession? desktop, string instanceId)
    {
        InitializeComponent();
        _settings = settings;
        _desktop = desktop;
        InstanceId = instanceId;
        _petCatalog = desktop?.Catalog ?? new PetCatalog();
        _pet = package!;
        _imageLease = package?.RetainImage();
        _companion = desktop?.Companion ?? new CompanionRuntime(_settings, persistDurations: draft => draft.Save());
        _focusSession = _companion.Session;
        _frameTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _frameTimer.Tick += FrameTimer_Tick;

        _ambientTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _ambientTimer.Tick += AmbientTimer_Tick;

        _roamTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _roamTimer.Tick += RoamTimer_Tick;
        InitializeClickInteraction();
        InitializeDocking();
        IsVisibleChanged += (_, _) => RefreshActivityTimers();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce) { RefreshActivityTimers(); return; }
        _loadedOnce = true;
        try
        {
            string? fallback = null;
            _pet ??= _petCatalog.LoadPreferred(_settings.SelectedPetId, out fallback);
            _imageLease ??= _pet.RetainImage();
            _settings.SelectedPetId = _pet.Manifest.Id;
            Title = _pet.Manifest.Name;
            if (fallback is not null)
                MessageBox.Show(fallback, "皮肤已恢复", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load sprite sheet.", ex);
            MessageBox.Show(ex.Message, "叶瞬光启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            ExitApplication();
            return;
        }

        BuildWindowContextMenu();
        ApplyScale(_settings.Scale, save: false);
        Topmost = _settings.Topmost;
        SetInitialPosition();
        EnsureWindowInWorkArea();
        SaveWindowPosition();
        UpdateMenuChecks();

        PlayAnimation(PetState.Idle, restart: true);
        InitializeCompanion();
        RefreshActivityTimers();
        AppLogger.Info("Main window loaded.");
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _sourceReady = true;
        NativeMethods.SetClickThrough(this, _settings.ClickThrough);

        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        HidePet();
    }

    protected override void OnClosed(EventArgs e)
    {
        _frameTimer.Stop();
        _ambientTimer.Stop();
        _roamTimer.Stop();
        _dockTimer.Stop();
        CancelPointerInteraction();
        CloseCompanion();
        ReleaseNativeResources();
        _frameCache.Clear();
        SpriteImage.Source = null;
        _imageLease?.Dispose();
        _imageLease = null;
        base.OnClosed(e);
    }

    public void ShowAndActivate()
    {
        if (_isExiting) return;
        ExpandDock(immediately: true);
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsEdgeDocked) EnsureWindowInWorkArea();
        CapturePosition();

        Activate();
        NativeMethods.ActivateWindow(this);
        _desktop?.SetHidden(this, false);
        RefreshActivityTimers();

        if (_state == PetState.Idle && !_isRoaming)
        {
            EnsureIdleBehaviorSchedule();
        }
    }

    public void RecallToPrimaryScreen()
    {
        CancelPointerInteraction();
        LeaveDock();
        StopRoaming(returnToIdle: false);

        if (_settings.ClickThrough)
        {
            SetClickThrough(false);
        }

        ResetPosition();
        ShowAndActivate();
        PlayAnimation(PetState.Waving, restart: true);
        AppLogger.Info("Pet recalled to the primary work area.");
    }

    internal void PrepareForApplicationShutdown()
    {
        _desktop?.DismissSpeech(this);
        _isExiting = true;
        _isRoaming = false;
        _roamTimer.Stop();
        _frameTimer.Stop();
        _ambientTimer.Stop();
        _dockTimer.Stop();
        CancelPointerInteraction();
        CloseCompanion();
        ReleaseNativeResources();
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message is 0x007E or 0x001A or 0x02E0)
            Dispatcher.BeginInvoke(OnDockDisplayChanged);
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == SummonHotkeyId)
        {
            RecallToPrimaryScreen();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void SetInitialPosition()
    {
        if (_settings.Left.HasValue && _settings.Top.HasValue)
        {
            Left = _settings.Left.Value;
            Top = _settings.Top.Value;
            return;
        }

        ResetPosition();
    }

    private void ResetPosition()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 48 - (_desktop?.PlacementOffset(this) ?? 0));
        Top = Math.Max(area.Top, area.Bottom - Height - 32);
        EnsureWindowInWorkArea();
        SaveWindowPosition();
    }

    private void PlayAnimation(PetState state, bool restart = false)
    {
        if (!_pet.Supports(state)) state = PetState.Idle;
        if (!restart && !_isLookMode && _state == state)
        {
            return;
        }
        _sessionOwnsAnimation = false;

        _state = state;
        _animation = _pet.GetAnimation(state);
        _frameIndex = 0;
        _isLookMode = false;
        _lastLookDirection = -1;

        ShowFrame(_animation.Row, _animation.StartColumn + _frameIndex);
        _frameTimer.Interval = CurrentFrameDuration();
        RefreshActivityTimers();

        if (state == PetState.Idle && !_isRoaming)
        {
            EnsureIdleBehaviorSchedule();
        }
    }

    private void TriggerUserAnimation(PetState state)
    {
        ShowAndActivate();
        StopRoaming(returnToIdle: false);
        PlayAnimation(state, restart: true);
    }

    private void FrameTimer_Tick(object? sender, EventArgs e)
    {
        if (_isLookMode || !CanPlayDockAnimation)
        {
            return;
        }

        if (!_animation.Loop && _frameIndex >= _animation.FrameCount - 1)
        {
            if (_sessionOwnsAnimation && _sessionVisuals.IsCompletionActive)
            {
                PlayAnimation(_state, restart: true);
                _sessionOwnsAnimation = true;
            }
            else PlayRestingAnimation();
            return;
        }

        _frameIndex = (_frameIndex + 1) % _animation.FrameCount;
        ShowFrame(_animation.Row, _animation.StartColumn + _frameIndex);
        _frameTimer.Interval = CurrentFrameDuration();
    }

    private TimeSpan CurrentFrameDuration()
    {
        var index = Math.Clamp(_frameIndex, 0, _animation.DurationsMs.Length - 1);
        return TimeSpan.FromMilliseconds(_animation.DurationsMs[index]);
    }

    private void AmbientTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible ||
            _isDragging ||
            _pointerDown ||
            _clickTimer.IsEnabled ||
            _isMenuOpen ||
            _isRoaming ||
            _settingsWindow is not null ||
            IsEdgeDocked ||
            _state != PetState.Idle ||
            SuppressAutomaticBehavior)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_settings.DesktopRoaming && _pet.CanRoam && now >= _nextRoamUtc)
        {
            if (!_settings.PauseNearMouse || !IsCursorNearPet())
            {
                StartRoaming();
                return;
            }
        }

        if (_settings.RandomIdleActions && _pet.RandomActions.Length > 0 && now >= _nextIdleActionUtc)
        {
            _nextIdleActionUtc = DateTime.MaxValue;
            var state = _pet.RandomActions[_random.Next(_pet.RandomActions.Length)];
            PlayAnimation(state, restart: true);
            return;
        }

        if (_settings.LookAtMouse && _pet.CanLook && TryShowLookAtCursor())
        {
            return;
        }

        if (_isLookMode)
        {
            PlayAnimation(PetState.Idle, restart: true);
        }
    }

    private bool TryShowLookAtCursor()
    {
        var cursor = WinForms.Cursor.Position;
        var center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
        var dx = cursor.X - center.X;
        var dy = cursor.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < LookDeadZone || distance > LookRadius)
        {
            return false;
        }

        ShowLookDirection(ComputeLookDirection(dx, dy));
        return true;
    }

    private static int ComputeLookDirection(double dx, double dy)
    {
        var angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (angle < 0)
        {
            angle += 360.0;
        }

        return ((int)Math.Round(angle / 22.5, MidpointRounding.AwayFromZero)) % PetPackage.LookDirectionCount;
    }

    private void ShowLookDirection(int directionIndex)
    {
        if (_isLookMode && _lastLookDirection == directionIndex)
        {
            return;
        }

        _isLookMode = true;
        _lastLookDirection = directionIndex;
        _frameTimer.Stop();

        var frame = _pet.Manifest.LookDirections[directionIndex];
        ShowFrame(frame.Row, frame.Column);
    }

    private void ShowFrame(int row, int column)
    {
        SpriteImage.Source = GetFrame(row, column);
    }

    private BitmapSource GetFrame(int row, int column)
    {
        var key = (row, column);
        if (_frameCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var frame = _pet.GetFrame(row, column);
        _frameCache[key] = frame;
        return frame;
    }

    private void ResetIdleBehaviorSchedule()
    {
        var now = DateTime.UtcNow;
        _nextIdleActionUtc = _settings.RandomIdleActions && _pet.RandomActions.Length > 0
            ? now + RandomizedDelay(_settings.IdleActionIntervalSeconds)
            : DateTime.MaxValue;
        _nextRoamUtc = _settings.DesktopRoaming && _pet.CanRoam
            ? now + RandomizedDelay(_settings.RoamIntervalSeconds)
            : DateTime.MaxValue;
    }

    private void EnsureIdleBehaviorSchedule()
    {
        var now = DateTime.UtcNow;

        if (!_settings.RandomIdleActions || _pet.RandomActions.Length == 0)
        {
            _nextIdleActionUtc = DateTime.MaxValue;
        }
        else if (_nextIdleActionUtc == DateTime.MaxValue)
        {
            _nextIdleActionUtc = now + RandomizedDelay(_settings.IdleActionIntervalSeconds);
        }

        if (!_settings.DesktopRoaming || !_pet.CanRoam)
        {
            _nextRoamUtc = DateTime.MaxValue;
        }
        else if (_nextRoamUtc == DateTime.MaxValue)
        {
            _nextRoamUtc = now + RandomizedDelay(_settings.RoamIntervalSeconds);
        }
    }

    private TimeSpan RandomizedDelay(double baseSeconds)
    {
        var factor = 0.75 + _random.NextDouble() * 0.5;
        return TimeSpan.FromSeconds(baseSeconds * factor);
    }

    private void StartRoaming()
    {
        if (!_pet.CanRoam || IsEdgeDocked) return;
        var workArea = GetCurrentWorkAreaInDips();
        var minimumLeft = workArea.Left;
        var maximumLeft = Math.Max(minimumLeft, workArea.Right - Width);
        Left = Math.Clamp(Left, minimumLeft, maximumLeft);

        var leftSpace = Left - minimumLeft;
        var rightSpace = maximumLeft - Left;
        if (leftSpace < 24 && rightSpace < 24)
        {
            _nextRoamUtc = DateTime.MaxValue;
            EnsureIdleBehaviorSchedule();
            return;
        }

        if (leftSpace >= MinRoamDistance && rightSpace >= MinRoamDistance)
        {
            _roamDirection = _random.Next(2) == 0 ? -1 : 1;
        }
        else
        {
            _roamDirection = rightSpace >= leftSpace ? 1 : -1;
        }

        _roamRemainingDistance = MinRoamDistance + _random.NextDouble() * (MaxRoamDistance - MinRoamDistance);
        _roamPaused = false;
        _lastRoamTickUtc = DateTime.UtcNow;
        _isRoaming = true;
        _nextRoamUtc = DateTime.MaxValue;
        PlayAnimation(_roamDirection > 0 ? PetState.RunningRight : PetState.RunningLeft, restart: true);
        _roamTimer.Start();
    }

    private void RoamTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isRoaming || !_settings.DesktopRoaming || !IsVisible || SuppressAutomaticBehavior)
        {
            StopRoaming(returnToIdle: true);
            return;
        }

        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Clamp((now - _lastRoamTickUtc).TotalSeconds, 0, 0.1);
        _lastRoamTickUtc = now;
        if (_settings.PauseNearMouse && IsCursorNearPet(_roamPaused ? 20 : 0))
        {
            if (!_roamPaused)
            {
                _roamPaused = true;
                PlayAnimation(PetState.Idle, restart: true);
            }
            return;
        }
        var area = GetCurrentWorkAreaInDips();
        var step = DesktopBehavior.Move(Left, _roamDirection, _roamRemainingDistance,
            _settings.RoamSpeed * elapsedSeconds, area.Left, area.Right - Width);
        if (_roamPaused || step.Direction != _roamDirection)
            PlayAnimation(step.Direction > 0 ? PetState.RunningRight : PetState.RunningLeft, restart: true);
        _roamPaused = false;
        _roamDirection = step.Direction;
        _roamRemainingDistance = step.Remaining;
        Left = step.Left;
        if (_roamRemainingDistance <= 0)
        {
            StopRoaming(returnToIdle: true);
        }
    }

    private void StopRoaming(bool returnToIdle)
    {
        if (!_isRoaming)
        {
            return;
        }

        _isRoaming = false;
        _roamPaused = false;
        _roamTimer.Stop();
        EnsureWindowInWorkArea();
        SaveWindowPosition();

        if (returnToIdle && !_isDragging)
        {
            PlayRestingAnimation();
        }
    }

    private Rect GetCurrentWorkAreaInDips()
    {
        if (!NativeMethods.TryGetWindowWorkArea(this, out var nativeArea))
        {
            return SystemParameters.WorkArea;
        }

        try
        {
            var relativeTopLeft = PointFromScreen(new Point(nativeArea.Left, nativeArea.Top));
            var relativeBottomRight = PointFromScreen(new Point(nativeArea.Right, nativeArea.Bottom));
            return new Rect(
                Left + relativeTopLeft.X,
                Top + relativeTopLeft.Y,
                Math.Max(0, relativeBottomRight.X - relativeTopLeft.X),
                Math.Max(0, relativeBottomRight.Y - relativeTopLeft.Y));
        }
        catch (InvalidOperationException)
        {
            return SystemParameters.WorkArea;
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        _desktop?.DismissSpeech(this);
        if (!_isDragging)
        {
            return;
        }

        var dx = Left - _lastDragLeft;
        if (Math.Abs(dx) > 0.5)
        {
            PlayAnimation(dx >= 0 ? PetState.RunningRight : PetState.RunningLeft);
            _lastDragLeft = Left;
        }
    }

    private void FinishDrag()
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        if (DockAfterDrag()) return;
        EnsureWindowInWorkArea();
        SaveWindowPosition();
        PlayRestingAnimation();
        ApplyEffectiveWindowOptions();
        _desktop?.Speak(this, SpeechEvent.Drag);
    }

    private void BuildWindowContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("设置...", (_, _) => Dispatcher.BeginInvoke(OpenSettings)));
        menu.Items.Add(CreateMenuItem("学习陪伴...", (_, _) => Dispatcher.BeginInvoke(OpenFocusWindow)));
        if (_desktop is not null) menu.Items.Add(CreateMenuItem("角色管理...", (_, _) => Dispatcher.BeginInvoke(_desktop.OpenManager)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateAnimationMenuItem("待机", PetState.Idle));
        menu.Items.Add(CreateAnimationMenuItem("打招呼", PetState.Waving));
        menu.Items.Add(CreateAnimationMenuItem("跳一下", PetState.Jumping));
        menu.Items.Add(CreateAnimationMenuItem("工作中", PetState.Running));
        menu.Items.Add(CreateAnimationMenuItem("等待确认", PetState.Waiting));
        menu.Items.Add(CreateAnimationMenuItem("检查成果", PetState.Review));
        menu.Items.Add(CreateAnimationMenuItem("失败一下", PetState.Failed));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("召回主屏幕", (_, _) => RecallToPrimaryScreen(), "Ctrl+Alt+Y"));
        menu.Items.Add(CreateMenuItem("放大", (_, _) => ChangeScale(ScaleStep)));
        menu.Items.Add(CreateMenuItem("缩小", (_, _) => ChangeScale(-ScaleStep)));
        menu.Items.Add(new Separator());

        _windowTopmostItem = CreateCheckMenuItem("总在最前", _settings.Topmost, (_, _) =>
        {
            if (_windowTopmostItem is not null)
            {
                SetTopmost(_windowTopmostItem.IsChecked);
            }
        });
        menu.Items.Add(_windowTopmostItem);

        _windowClickThroughItem = CreateCheckMenuItem("点击穿透", _settings.ClickThrough, (_, _) =>
        {
            if (_windowClickThroughItem is not null)
            {
                SetClickThrough(_windowClickThroughItem.IsChecked);
            }
        });
        menu.Items.Add(_windowClickThroughItem);

        _windowRandomIdleItem = CreateCheckMenuItem("随机待机", _settings.RandomIdleActions, (_, _) =>
        {
            if (_windowRandomIdleItem is not null)
            {
                SetRandomIdle(_windowRandomIdleItem.IsChecked);
            }
        });
        menu.Items.Add(_windowRandomIdleItem);

        _windowRoamingItem = CreateCheckMenuItem("桌面走动", _settings.DesktopRoaming, (_, _) =>
        {
            if (_windowRoamingItem is not null)
            {
                SetDesktopRoaming(_windowRoamingItem.IsChecked);
            }
        });
        menu.Items.Add(_windowRoamingItem);

        _windowStartupItem = CreateCheckMenuItem("开机启动", _settings.LaunchAtStartup, (_, _) =>
        {
            if (_windowStartupItem is not null)
            {
                SetLaunchAtStartup(_windowStartupItem.IsChecked);
            }
        });
        menu.Items.Add(_windowStartupItem);

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("隐藏", (_, _) => HidePet()));
        if (_desktop is not null) menu.Items.Add(CreateMenuItem("关闭此角色", (_, _) => CloseInstance()));
        menu.Items.Add(CreateMenuItem("退出程序", (_, _) => ExitApplication()));
        menu.Opened += (_, _) => BeginMenuInteraction();
        menu.Closed += (_, _) => EndMenuInteraction();
        ContextMenu = menu;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler click, string? gesture = null)
    {
        var item = new MenuItem
        {
            Header = header,
            InputGestureText = gesture ?? string.Empty
        };
        item.Click += click;
        return item;
    }

    private MenuItem CreateAnimationMenuItem(string name, PetState state)
    {
        var item = CreateMenuItem(name, (_, _) => TriggerUserAnimation(state));
        item.Tag = state;
        return item;
    }

    private static MenuItem CreateCheckMenuItem(string header, bool isChecked, RoutedEventHandler click)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = isChecked
        };
        item.Click += click;
        return item;
    }


    private void OpenSettings() => OpenSettingsCore();

    private async void OpenSettingsCore(bool companionTab = false)
    {
        AppLogger.Info("Opening role settings.");
        CancelPointerInteraction();
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        if (_desktop is not null && !_desktop.BeginSettings(this)) return;
        PetSettings? result = null;
        PetPackage? selectedPackage = null;
        try
        {
            ShowAndActivate();
            StopRoaming(returnToIdle: true);
            var scan = await _petCatalog.ScanAsync();
            if (_isExiting) return;
            var dialog = new SettingsWindow(_settings, _desktop?.HotkeyRegistered ?? false, _petCatalog, _pet, scan) { Owner = this };
            _settingsWindow = dialog;
            if (companionTab) dialog.SelectCompanionTab();
            if (dialog.ShowDialog() == true)
            {
                result = dialog.Result;
                selectedPackage = dialog.SelectedPackage;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to open settings.", ex);
            MessageBox.Show(ex.Message, "无法打开设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _settingsWindow = null;
            _desktop?.EndSettings();
        }

        if (result is not null)
        {
            ApplySettings(result, selectedPackage!);
        }
        else if (_state == PetState.Idle)
        {
            EnsureIdleBehaviorSchedule();
        }
    }

    private void BeginMenuInteraction()
    {
        _desktop?.DismissSpeech(this);
        CancelPointerInteraction();
        ExpandDock(immediately: true);
        _isMenuOpen = true;
        StopRoaming(returnToIdle: true);
    }

    private void EndMenuInteraction()
    {
        _isMenuOpen = false;
        if (_state == PetState.Idle)
        {
            EnsureIdleBehaviorSchedule();
        }
    }

    private void ApplySettings(PetSettings updated, PetPackage selectedPackage)
    {
        StopRoaming(returnToIdle: false);
        var dockEdge = LeaveDock();

        if (updated.LaunchAtStartup != _settings.LaunchAtStartup)
        {
            try
            {
                PetSettings.SetLaunchAtStartup(updated.LaunchAtStartup);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to update launch-at-startup setting.", ex);
                MessageBox.Show(ex.Message, "开机启动设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        AdoptPackage(selectedPackage);
        _settings.Scale = updated.Scale;
        _settings.Topmost = updated.Topmost;
        _settings.ClickThrough = updated.ClickThrough;
        _settings.EdgeAutoHide = updated.EdgeAutoHide;
        _settings.LaunchAtStartup = PetSettings.IsLaunchAtStartupEnabled();
        _settings.LookAtMouse = updated.LookAtMouse;
        _settings.RandomIdleActions = updated.RandomIdleActions;
        _settings.IdleActionIntervalSeconds = updated.IdleActionIntervalSeconds;
        _settings.DesktopRoaming = updated.DesktopRoaming;
        _settings.RoamIntervalSeconds = updated.RoamIntervalSeconds;
        _settings.RoamSpeed = updated.RoamSpeed;
        _settings.ClickInteraction = updated.ClickInteraction;
        _settings.PauseNearMouse = updated.PauseNearMouse;
        _settings.MousePauseRadius = updated.MousePauseRadius;
        _settings.FocusMinutes = updated.FocusMinutes;
        _settings.BreakMinutes = updated.BreakMinutes;
        _settings.BreakRemindersEnabled = updated.BreakRemindersEnabled;
        _settings.BreakReminderMinutes = updated.BreakReminderMinutes;
        _settings.NotificationsEnabled = updated.NotificationsEnabled;
        _settings.PauseDuringFocus = updated.PauseDuringFocus;
        _settings.SessionAnimationEnabled = updated.SessionAnimationEnabled;
        _settings.DoNotDisturb = updated.DoNotDisturb;
        _settings.QuietHoursEnabled = updated.QuietHoursEnabled;
        _settings.QuietStartMinute = updated.QuietStartMinute;
        _settings.QuietEndMinute = updated.QuietEndMinute;
        _desktop?.UpdateGlobal(_settings);

        ApplyScale(_settings.Scale, save: false);
        Left = centerX - Width / 2;
        Top = centerY - Height / 2;
        ApplyEffectiveWindowOptions();

        EnsureWindowInWorkArea();
        SaveWindowPosition();
        UpdateMenuChecks();
        ResetIdleBehaviorSchedule();
        ConfigureCompanion();
        PlayRestingAnimation();
        RestoreDock(dockEdge);
        AppLogger.Info("Settings updated.");
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            HidePet();
        }
        else
        {
            ShowAndActivate();
        }
    }

    private void AdoptPackage(PetPackage package)
    {
        _frameTimer.Stop();
        var lease = package.RetainImage();
        _imageLease?.Dispose();
        _imageLease = lease;
        _pet = package;
        _frameCache.Clear();
        _settings.SelectedPetId = package.Manifest.Id;
        Title = package.Manifest.Name;
        _companion.UpdateAvatar(InstanceId, package);
    }

    private void HidePet()
    {
        _desktop?.DismissSpeech(this);
        CancelPointerInteraction();
        LeaveDock();
        StopRoaming(returnToIdle: true);
        Hide();
        _desktop?.SetHidden(this, true);
        RefreshActivityTimers();
    }

    private void ChangeScale(double delta)
    {
        var dockEdge = LeaveDock();
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        ApplyScale(_settings.Scale + delta, save: false);
        Left = centerX - Width / 2;
        Top = centerY - Height / 2;
        EnsureWindowInWorkArea();
        SaveWindowPosition();
        RestoreDock(dockEdge);
    }

    private void ApplyScale(double scale, bool save)
    {
        _settings.Scale = Math.Round(Math.Clamp(scale, MinScale, MaxScale), 2);
        Width = _pet.Manifest.CellWidth * _settings.Scale;
        Height = _pet.Manifest.CellHeight * _settings.Scale;
        SpriteImage.Width = Width;
        SpriteImage.Height = Height;

        if (save)
        {
            PersistSettings();
        }
    }

    private void SetTopmost(bool enabled)
    {
        _settings.Topmost = enabled;
        ApplyEffectiveWindowOptions();
        PersistSettings();
        UpdateMenuChecks();
    }

    private void SetClickThrough(bool enabled)
    {
        _settings.ClickThrough = enabled;
        ApplyEffectiveWindowOptions();

        PersistSettings();
        UpdateMenuChecks();
    }

    private void SetRandomIdle(bool enabled)
    {
        _settings.RandomIdleActions = enabled;
        PersistSettings();
        ResetIdleBehaviorSchedule();
        UpdateMenuChecks();
    }

    private void SetDesktopRoaming(bool enabled)
    {
        _settings.DesktopRoaming = enabled;
        if (!enabled)
        {
            StopRoaming(returnToIdle: true);
        }

        PersistSettings();
        ResetIdleBehaviorSchedule();
        UpdateMenuChecks();
    }

    private void SetLaunchAtStartup(bool enabled)
    {
        try
        {
            PetSettings.SetLaunchAtStartup(enabled);
            _settings.LaunchAtStartup = PetSettings.IsLaunchAtStartupEnabled();
            _desktop?.UpdateGlobal(_settings);
            PersistSettings();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to update launch-at-startup setting.", ex);
            MessageBox.Show(ex.Message, "开机启动设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateMenuChecks();
    }

    private void UpdateMenuChecks()
    {
        if (ContextMenu is not null)
            foreach (var item in ContextMenu.Items.OfType<MenuItem>())
                if (item.Tag is PetState state) item.IsEnabled = _pet.Supports(state);
        if (_windowRandomIdleItem is not null)
        {
            _windowRandomIdleItem.IsEnabled = _pet.RandomActions.Length > 0;
            _windowRandomIdleItem.IsChecked = _settings.RandomIdleActions;
        }
        if (_windowRoamingItem is not null)
        {
            _windowRoamingItem.IsEnabled = _pet.CanRoam;
            _windowRoamingItem.IsChecked = _settings.DesktopRoaming;
        }
        if (_windowTopmostItem is not null) _windowTopmostItem.IsChecked = _settings.Topmost;
        if (_windowClickThroughItem is not null) _windowClickThroughItem.IsChecked = _settings.ClickThrough;
        if (_windowStartupItem is not null) _windowStartupItem.IsChecked = _settings.LaunchAtStartup;
        RefreshActivityTimers();
    }

    private void SaveWindowPosition()
    {
        CapturePosition();
        PersistSettings();
    }

    private void EnsureWindowInWorkArea()
    {
        NativeMethods.EnsureWindowInWorkArea(this);
    }


    private void ReleaseNativeResources()
    {
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
    }

    private void ExitApplication()
    {
        if (_desktop is not null) { _desktop.RequestExit(); return; }
        _dockTimer.Stop();
        CancelPointerInteraction();
        CloseCompanion();
        _isExiting = true;
        StopRoaming(returnToIdle: false);
        _frameTimer.Stop();
        _ambientTimer.Stop();
        _roamTimer.Stop();
        SaveWindowPosition();
        ReleaseNativeResources();
        AppLogger.Info("Exit requested by user.");

        Application.Current.Shutdown();
    }
}
