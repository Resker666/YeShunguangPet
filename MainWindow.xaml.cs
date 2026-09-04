using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace YeShunguangPet;

public partial class MainWindow : Window
{
    private const double MinScale = 0.5;
    private const double MaxScale = 2.5;
    private const double ScaleStep = 0.1;
    private const double LookRadius = 520;
    private const double LookDeadZone = 42;

    private readonly PetSettings _settings;
    private readonly Dictionary<(int Row, int Column), BitmapSource> _frameCache = new();
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _ambientTimer;

    private BitmapSource? _spriteSheet;
    private PetAnimation _animation = PetAnimations.Get(PetState.Idle);
    private PetState _state = PetState.Idle;
    private int _frameIndex;
    private int _lastLookDirection = -1;
    private bool _isLookMode;
    private bool _isDragging;
    private bool _isExiting;
    private bool _sourceReady;
    private double _lastDragLeft;

    private WinForms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _applicationIcon;
    private WinForms.ToolStripMenuItem? _trayTopmostItem;
    private WinForms.ToolStripMenuItem? _trayClickThroughItem;
    private WinForms.ToolStripMenuItem? _trayStartupItem;
    private MenuItem? _windowTopmostItem;
    private MenuItem? _windowClickThroughItem;
    private MenuItem? _windowStartupItem;

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
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadSpriteSheet();
        }
        catch (Exception ex)
        {
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
        UpdateMenuChecks();

        PlayAnimation(PetState.Idle, restart: true);
        _ambientTimer.Start();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _sourceReady = true;
        NativeMethods.SetClickThrough(this, _settings.ClickThrough);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        _applicationIcon?.Dispose();
        base.OnClosed(e);
    }

    public void ShowAndActivate()
    {
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
    }

    private void LoadSpriteSheet()
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.UriSource = new Uri("pack://application:,,,/Assets/spritesheet.png", UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var expectedWidth = PetAnimations.Columns * PetAnimations.CellWidth;
        var expectedHeight = PetAnimations.Rows * PetAnimations.CellHeight;
        if (bitmap.PixelWidth != expectedWidth || bitmap.PixelHeight != expectedHeight)
        {
            throw new InvalidOperationException(
                $"精灵图尺寸应为 {expectedWidth} x {expectedHeight}，当前是 {bitmap.PixelWidth} x {bitmap.PixelHeight}。");
        }

        _spriteSheet = bitmap;
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
        if (!restart && !_isLookMode && _state == state)
        {
            return;
        }

        _state = state;
        _animation = PetAnimations.Get(state);
        _frameIndex = 0;
        _isLookMode = false;
        _lastLookDirection = -1;

        ShowFrame(_animation.Row, _frameIndex);
        _frameTimer.Interval = CurrentFrameDuration();
        _frameTimer.Start();
    }

    private void FrameTimer_Tick(object? sender, EventArgs e)
    {
        if (_isLookMode)
        {
            return;
        }

        if (!_animation.Loop && _frameIndex >= _animation.FrameCount - 1)
        {
            PlayAnimation(PetState.Idle, restart: true);
            return;
        }

        _frameIndex = (_frameIndex + 1) % _animation.FrameCount;
        ShowFrame(_animation.Row, _frameIndex);
        _frameTimer.Interval = CurrentFrameDuration();
    }

    private TimeSpan CurrentFrameDuration()
    {
        var index = Math.Clamp(_frameIndex, 0, _animation.DurationsMs.Length - 1);
        return TimeSpan.FromMilliseconds(_animation.DurationsMs[index]);
    }

    private void AmbientTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDragging || _state != PetState.Idle)
        {
            return;
        }

        var cursor = WinForms.Cursor.Position;
        var center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
        var dx = cursor.X - center.X;
        var dy = cursor.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < LookDeadZone || distance > LookRadius)
        {
            if (_isLookMode)
            {
                PlayAnimation(PetState.Idle, restart: true);
            }

            return;
        }

        ShowLookDirection(ComputeLookDirection(dx, dy));
    }

    private static int ComputeLookDirection(double dx, double dy)
    {
        var angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (angle < 0)
        {
            angle += 360.0;
        }

        return ((int)Math.Round(angle / 22.5, MidpointRounding.AwayFromZero)) % PetAnimations.LookDirectionCount;
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

        if (directionIndex <= 7)
        {
            ShowFrame(9, directionIndex);
        }
        else
        {
            ShowFrame(10, directionIndex - 8);
        }
    }

    private void ShowFrame(int row, int column)
    {
        SpriteImage.Source = GetFrame(row, column);
    }

    private BitmapSource GetFrame(int row, int column)
    {
        if (_spriteSheet is null)
        {
            throw new InvalidOperationException("精灵图还没有加载。");
        }

        var key = (row, column);
        if (_frameCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var rect = new Int32Rect(
            column * PetAnimations.CellWidth,
            row * PetAnimations.CellHeight,
            PetAnimations.CellWidth,
            PetAnimations.CellHeight);
        var frame = new CroppedBitmap(_spriteSheet, rect);
        frame.Freeze();
        _frameCache[key] = frame;
        return frame;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.ClickThrough || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _isDragging = true;
        _lastDragLeft = Left;
        PlayAnimation(PetState.RunningRight, restart: true);
        e.Handled = true;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse can be released before WPF starts the native drag loop.
        }
        finally
        {
            FinishDrag();
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
        EnsureWindowInWorkArea();
        SaveWindowPosition();
        PlayAnimation(PetState.Idle, restart: true);
    }

    private void BuildWindowContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("待机", (_, _) => PlayAnimation(PetState.Idle, restart: true)));
        menu.Items.Add(CreateMenuItem("打招呼", (_, _) => PlayAnimation(PetState.Waving, restart: true)));
        menu.Items.Add(CreateMenuItem("跳一下", (_, _) => PlayAnimation(PetState.Jumping, restart: true)));
        menu.Items.Add(CreateMenuItem("工作中", (_, _) => PlayAnimation(PetState.Running, restart: true)));
        menu.Items.Add(CreateMenuItem("等待确认", (_, _) => PlayAnimation(PetState.Waiting, restart: true)));
        menu.Items.Add(CreateMenuItem("检查成果", (_, _) => PlayAnimation(PetState.Review, restart: true)));
        menu.Items.Add(CreateMenuItem("失败一下", (_, _) => PlayAnimation(PetState.Failed, restart: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("放大", (_, _) => ChangeScale(ScaleStep)));
        menu.Items.Add(CreateMenuItem("缩小", (_, _) => ChangeScale(-ScaleStep)));
        menu.Items.Add(CreateMenuItem("重置位置", (_, _) => ResetPosition()));
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

        _windowStartupItem = CreateCheckMenuItem("开机启动", _settings.LaunchAtStartup, (_, _) =>
        {
            if (_windowStartupItem is not null)
            {
                SetLaunchAtStartup(_windowStartupItem.IsChecked);
            }
        });
        menu.Items.Add(_windowStartupItem);

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("隐藏", (_, _) => Hide()));
        menu.Items.Add(CreateMenuItem("退出", (_, _) => ExitApplication()));
        ContextMenu = menu;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler click)
    {
        var item = new MenuItem { Header = header };
        item.Click += click;
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
        menu.Items.Add("显示/隐藏", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add("待机", null, (_, _) => Dispatcher.Invoke(() => PlayAnimation(PetState.Idle, restart: true)));
        menu.Items.Add("打招呼", null, (_, _) => Dispatcher.Invoke(() => PlayAnimation(PetState.Waving, restart: true)));
        menu.Items.Add("重置位置", null, (_, _) => Dispatcher.Invoke(ResetPosition));
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
            Text = "叶瞬光",
            Icon = LoadApplicationIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleVisibility);
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowAndActivate();
        }
    }

    private void ChangeScale(double delta)
    {
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        ApplyScale(_settings.Scale + delta, save: true);
        Left = centerX - Width / 2;
        Top = centerY - Height / 2;
        EnsureWindowInWorkArea();
        SaveWindowPosition();
    }

    private void ApplyScale(double scale, bool save)
    {
        _settings.Scale = Math.Round(Math.Clamp(scale, MinScale, MaxScale), 2);
        Width = PetAnimations.CellWidth * _settings.Scale;
        Height = PetAnimations.CellHeight * _settings.Scale;
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
        Topmost = enabled;
        _settings.Save();
        UpdateMenuChecks();
    }

    private void SetClickThrough(bool enabled)
    {
        _settings.ClickThrough = enabled;
        if (_sourceReady)
        {
            NativeMethods.SetClickThrough(this, enabled);
        }

        _settings.Save();
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
            MessageBox.Show(ex.Message, "开机启动设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateMenuChecks();
    }

    private void UpdateMenuChecks()
    {
        if (_windowTopmostItem is not null)
        {
            _windowTopmostItem.IsChecked = _settings.Topmost;
        }

        if (_windowClickThroughItem is not null)
        {
            _windowClickThroughItem.IsChecked = _settings.ClickThrough;
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

        if (_trayStartupItem is not null)
        {
            _trayStartupItem.Checked = _settings.LaunchAtStartup;
        }
    }

    private void SaveWindowPosition()
    {
        _settings.Left = Left;
        _settings.Top = Top;
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
        catch
        {
            _applicationIcon = null;
        }

        return _applicationIcon ?? Drawing.SystemIcons.Application;
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _frameTimer.Stop();
        _ambientTimer.Stop();
        SaveWindowPosition();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        Application.Current.Shutdown();
    }
}
