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
using Drawing = System.Drawing;
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
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyY = 0x59;

    private readonly PetSettings _settings;
    private readonly PetCatalog _petCatalog = new();
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
    private bool _summonHotkeyRegistered;
    private double _lastDragLeft;
    private double _roamRemainingDistance;
    private bool _roamPaused;
    private int _roamDirection;
    private DateTime _lastRoamTickUtc;
    private DateTime _nextIdleActionUtc = DateTime.MaxValue;
    private DateTime _nextRoamUtc = DateTime.MaxValue;
    private HwndSource? _windowSource;
    private SettingsWindow? _settingsWindow;

    private WinForms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _applicationIcon;
    private WinForms.ToolStripMenuItem? _trayTopmostItem;
    private WinForms.ToolStripMenuItem? _trayClickThroughItem;
    private WinForms.ToolStripMenuItem? _trayStartupItem;
    private WinForms.ToolStripMenuItem? _trayRandomIdleItem;
    private WinForms.ToolStripMenuItem? _trayRoamingItem;
    private MenuItem? _windowTopmostItem;
    private MenuItem? _windowClickThroughItem;
    private MenuItem? _windowStartupItem;
    private MenuItem? _windowRandomIdleItem;
    private MenuItem? _windowRoamingItem;

    public MainWindow()
    {
        InitializeComponent();

        _settings = PetSettings.Load();
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
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _pet = _petCatalog.LoadPreferred(_settings.SelectedPetId, out var fallback);
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
        CreateTrayIcon();
        ApplyScale(_settings.Scale, save: false);
        Topmost = _settings.Topmost;
        SetInitialPosition();
        EnsureWindowInWorkArea();
        SaveWindowPosition();
        UpdateMenuChecks();

        PlayAnimation(PetState.Idle, restart: true);
        InitializeCompanion();
        _ambientTimer.Start();
        AppLogger.Info("Main window loaded.");
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _sourceReady = true;
        NativeMethods.SetClickThrough(this, _settings.ClickThrough);

        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
        _summonHotkeyRegistered = NativeMethods.RegisterGlobalHotKey(
            this,
            SummonHotkeyId,
            ModControl | ModAlt | ModNoRepeat,
            VirtualKeyY);

        if (!_summonHotkeyRegistered)
        {
            AppLogger.Error("Failed to register Ctrl+Alt+Y summon hotkey.");
        }
        else
        {
            AppLogger.Info("Ctrl+Alt+Y summon hotkey registered.");
        }
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
        _dockTimer.Stop();
        CancelPointerInteraction();
        CloseCompanion();
        ReleaseNativeResources();
        _trayIcon?.Dispose();
        _applicationIcon?.Dispose();
        base.OnClosed(e);
    }

    public void ShowAndActivate()
    {
        ExpandDock(immediately: true);
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        NativeMethods.ActivateWindow(this);

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
        Left = Math.Max(area.Left, area.Right - Width - 48);
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
        _frameTimer.Start();

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
    }

    private void BuildWindowContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("设置...", (_, _) => Dispatcher.BeginInvoke(OpenSettings)));
        menu.Items.Add(CreateMenuItem("学习陪伴...", (_, _) => Dispatcher.BeginInvoke(OpenFocusWindow)));
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
        menu.Items.Add(CreateMenuItem("退出", (_, _) => ExitApplication()));
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

    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Opening += (_, _) => Dispatcher.Invoke(BeginMenuInteraction);
        menu.Closed += (_, _) => Dispatcher.Invoke(EndMenuInteraction);
        menu.Items.Add("设置...", null, (_, _) => Dispatcher.BeginInvoke(OpenSettings));
        menu.Items.Add("学习陪伴...", null, (_, _) => Dispatcher.BeginInvoke(OpenFocusWindow));
        _trayQuietItem = new WinForms.ToolStripMenuItem("勿扰模式") { CheckOnClick = true, Checked = _settings.DoNotDisturb };
        _trayQuietItem.Click += (_, _) => Dispatcher.Invoke(() => SetDoNotDisturb(_trayQuietItem.Checked));
        menu.Items.Add(_trayQuietItem);
        menu.Items.Add("显示/隐藏", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add("召回主屏幕 (Ctrl+Alt+Y)", null, (_, _) => Dispatcher.Invoke(RecallToPrimaryScreen));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("待机", null, (_, _) => Dispatcher.Invoke(() => TriggerUserAnimation(PetState.Idle))).Tag = PetState.Idle;
        menu.Items.Add("打招呼", null, (_, _) => Dispatcher.Invoke(() => TriggerUserAnimation(PetState.Waving))).Tag = PetState.Waving;
        menu.Items.Add(new WinForms.ToolStripSeparator());

        _trayTopmostItem = new WinForms.ToolStripMenuItem("总在最前")
        {
            CheckOnClick = true,
            Checked = _settings.Topmost
        };
        _trayTopmostItem.Click += (_, _) => Dispatcher.Invoke(() => SetTopmost(_trayTopmostItem.Checked));
        menu.Items.Add(_trayTopmostItem);

        _trayClickThroughItem = new WinForms.ToolStripMenuItem("点击穿透")
        {
            CheckOnClick = true,
            Checked = _settings.ClickThrough
        };
        _trayClickThroughItem.Click += (_, _) => Dispatcher.Invoke(() => SetClickThrough(_trayClickThroughItem.Checked));
        menu.Items.Add(_trayClickThroughItem);

        _trayRandomIdleItem = new WinForms.ToolStripMenuItem("随机待机")
        {
            CheckOnClick = true,
            Checked = _settings.RandomIdleActions
        };
        _trayRandomIdleItem.Click += (_, _) => Dispatcher.Invoke(() => SetRandomIdle(_trayRandomIdleItem.Checked));
        menu.Items.Add(_trayRandomIdleItem);

        _trayRoamingItem = new WinForms.ToolStripMenuItem("桌面走动")
        {
            CheckOnClick = true,
            Checked = _settings.DesktopRoaming
        };
        _trayRoamingItem.Click += (_, _) => Dispatcher.Invoke(() => SetDesktopRoaming(_trayRoamingItem.Checked));
        menu.Items.Add(_trayRoamingItem);

        _trayStartupItem = new WinForms.ToolStripMenuItem("开机启动")
        {
            CheckOnClick = true,
            Checked = _settings.LaunchAtStartup
        };
        _trayStartupItem.Click += (_, _) => Dispatcher.Invoke(() => SetLaunchAtStartup(_trayStartupItem.Checked));
        menu.Items.Add(_trayStartupItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = _pet.Manifest.Name,
            Icon = LoadApplicationIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleVisibility);
        _trayIcon.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(OpenFocusWindow);
    }

    private void OpenSettings()
    {
        CancelPointerInteraction();
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        ShowAndActivate();
        StopRoaming(returnToIdle: true);

        var dialog = new SettingsWindow(_settings, _summonHotkeyRegistered, _petCatalog, _pet)
        {
            Owner = this
        };

        PetSettings? result = null;
        _settingsWindow = dialog;
        try
        {
            if (dialog.ShowDialog() == true)
            {
                result = dialog.Result;
            }
        }
        finally
        {
            _settingsWindow = null;
        }

        if (result is not null)
        {
            ApplySettings(result, dialog.SelectedPackage!);
        }
        else if (_state == PetState.Idle)
        {
            EnsureIdleBehaviorSchedule();
        }
    }

    private void BeginMenuInteraction()
    {
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
        _pet = package;
        _frameCache.Clear();
        _settings.SelectedPetId = package.Manifest.Id;
        Title = package.Manifest.Name;
        if (_trayIcon is not null) _trayIcon.Text = package.Manifest.Name;
        _focusWindow?.UpdatePet(package);
    }

    private void HidePet()
    {
        CancelPointerInteraction();
        LeaveDock();
        StopRoaming(returnToIdle: true);
        Hide();
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
            _settings.Save();
        }
    }

    private void SetTopmost(bool enabled)
    {
        _settings.Topmost = enabled;
        ApplyEffectiveWindowOptions();
        _settings.Save();
        UpdateMenuChecks();
    }

    private void SetClickThrough(bool enabled)
    {
        _settings.ClickThrough = enabled;
        ApplyEffectiveWindowOptions();

        _settings.Save();
        UpdateMenuChecks();
    }

    private void SetRandomIdle(bool enabled)
    {
        _settings.RandomIdleActions = enabled;
        _settings.Save();
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

        _settings.Save();
        ResetIdleBehaviorSchedule();
        UpdateMenuChecks();
    }

    private void SetLaunchAtStartup(bool enabled)
    {
        try
        {
            PetSettings.SetLaunchAtStartup(enabled);
            _settings.LaunchAtStartup = PetSettings.IsLaunchAtStartupEnabled();
            _settings.Save();
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
        if (_trayQuietItem is not null) _trayQuietItem.Checked = _settings.DoNotDisturb;
        foreach (var item in ContextMenu.Items.OfType<MenuItem>())
            if (item.Tag is PetState state) item.IsEnabled = _pet.Supports(state);
        if (_trayIcon?.ContextMenuStrip is { } trayMenu)
            foreach (WinForms.ToolStripItem item in trayMenu.Items)
                if (item.Tag is PetState state) item.Enabled = _pet.Supports(state);
        if (_windowRandomIdleItem is not null) _windowRandomIdleItem.IsEnabled = _pet.RandomActions.Length > 0;
        if (_trayRandomIdleItem is not null) _trayRandomIdleItem.Enabled = _pet.RandomActions.Length > 0;
        if (_windowRoamingItem is not null) _windowRoamingItem.IsEnabled = _pet.CanRoam;
        if (_trayRoamingItem is not null) _trayRoamingItem.Enabled = _pet.CanRoam;
        if (_windowTopmostItem is not null)
        {
            _windowTopmostItem.IsChecked = _settings.Topmost;
        }

        if (_windowClickThroughItem is not null)
        {
            _windowClickThroughItem.IsChecked = _settings.ClickThrough;
        }

        if (_windowRandomIdleItem is not null)
        {
            _windowRandomIdleItem.IsChecked = _settings.RandomIdleActions;
        }

        if (_windowRoamingItem is not null)
        {
            _windowRoamingItem.IsChecked = _settings.DesktopRoaming;
        }

        if (_windowStartupItem is not null)
        {
            _windowStartupItem.IsChecked = _settings.LaunchAtStartup;
        }

        if (_trayTopmostItem is not null)
        {
            _trayTopmostItem.Checked = _settings.Topmost;
        }

        if (_trayClickThroughItem is not null)
        {
            _trayClickThroughItem.Checked = _settings.ClickThrough;
        }

        if (_trayRandomIdleItem is not null)
        {
            _trayRandomIdleItem.Checked = _settings.RandomIdleActions;
        }

        if (_trayRoamingItem is not null)
        {
            _trayRoamingItem.Checked = _settings.DesktopRoaming;
        }

        if (_trayStartupItem is not null)
        {
            _trayStartupItem.Checked = _settings.LaunchAtStartup;
        }
    }

    private void SaveWindowPosition()
    {
        var position = PositionToPersist;
        _settings.Left = position.X;
        _settings.Top = position.Y;
        _settings.Save();
    }

    private void EnsureWindowInWorkArea()
    {
        NativeMethods.EnsureWindowInWorkArea(this);
    }

    private Drawing.Icon LoadApplicationIcon()
    {
        if (_applicationIcon is not null)
        {
            return _applicationIcon;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                _applicationIcon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load application icon.", ex);
            _applicationIcon = null;
        }

        return _applicationIcon ?? Drawing.SystemIcons.Application;
    }

    private void ReleaseNativeResources()
    {
        if (_summonHotkeyRegistered)
        {
            NativeMethods.UnregisterGlobalHotKey(this, SummonHotkeyId);
            _summonHotkeyRegistered = false;
        }

        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
    }

    private void ExitApplication()
    {
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

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        Application.Current.Shutdown();
    }
}
