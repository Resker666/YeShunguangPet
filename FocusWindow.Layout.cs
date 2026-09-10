using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class FocusWindow
{
    private readonly bool _managePlacement;
    private readonly DispatcherTimer _placementTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private HwndSource? _windowSource;
    private FocusWindowOptions? _lastNormalPlacement;
    private bool _placementReady;
    private bool _insideSizeMove;
    private bool _screenRefreshQueued;
    private bool _layoutUpdating;
    private bool _compactLayout;
    private bool _layoutClosed;
    private bool? _presetsEditable;
    private FocusWindowOptions _viewOptions = new();
    private bool _switchingMode;
    internal bool IsMiniMode => _viewOptions.MiniMode;

    private void InitializeWindowLayout()
    {
        FocusScroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) UpdateResponsiveLayout(); };
        _placementTimer.Tick += (_, _) => { _placementTimer.Stop(); SaveWindowPlacement(); };
        _viewOptions = _runtime?.WindowOptions ?? new FocusWindowOptions();
        if (_managePlacement) ApplyWindowMode();
        if (!_managePlacement) return;
        var options = _viewOptions.Copy();
        var left = options.MiniMode ? options.MiniLeftPixels : options.LeftPixels;
        var top = options.MiniMode ? options.MiniTopPixels : options.TopPixels;
        if (left.HasValue) WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (_, _) =>
        {
            _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _windowSource?.AddHook(WindowLayoutHook);
            if (left is int x && top is int y) NativeMethods.MoveWindowPixels(this, x, y);
        };
        Loaded += (_, _) => { FitToScreen(); _placementReady = true; QueuePlacementSave(); };
        LocationChanged += (_, _) => QueuePlacementSave();
        SizeChanged += (_, _) => QueuePlacementSave();
        StateChanged += (_, _) =>
        {
            if (_switchingMode) return;
            if (WindowState == WindowState.Normal) QueueScreenRefresh();
            else { _placementTimer.Stop(); SaveWindowPlacement(); }
        };
    }

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();
    private void DialArea_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateDialSize();

    private void UpdateResponsiveLayout()
    {
        if (_layoutUpdating || _session is null || FocusScroll is null || FocusScroll.ActualWidth <= 0 || FocusScroll.ActualHeight <= 0) return;
        if (IsMiniMode)
        {
            MiniRoot.Width = FocusScroll.ActualWidth;
            MiniRoot.Height = FocusScroll.ActualHeight;
            FocusScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            return;
        }
        _layoutUpdating = true;
        try
        {
            var width = FocusScroll.ViewportWidth > 0 ? FocusScroll.ViewportWidth : FocusScroll.ActualWidth;
            var height = FocusScroll.ActualHeight;
            var compact = width < 354 || height < 480;
            var compactChanged = compact != _compactLayout;
            _compactLayout = compact;
            var xPadding = _compactLayout ? 16 : 24;
            var yPadding = _compactLayout ? 12 : 16;
            ContentRoot.Margin = new Thickness(xPadding, yPadding, xPadding, yPadding);
            ContentRoot.Width = Math.Max(272, Math.Min(480, width - xPadding * 2));
            FocusScroll.HorizontalScrollBarVisibility = width < 272 + xPadding * 2 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            var errors = (DurationError.Text.Length > 0 ? 38 : 0) + (WindowError.Text.Length > 0 ? 38 : 0);
            ContentRoot.Height = Math.Max((_compactLayout ? 392 : 439) - (CanEdit ? 0 : 36) + errors, Math.Min(660, height - yPadding * 2));
            HeaderRow.Height = _compactLayout ? 32 : 36;
            PetImage.Width = _compactLayout ? 24 : 28;
            PetImage.Height = _compactLayout ? 28 : 32;
            SessionsText.Visibility = _compactLayout ? Visibility.Collapsed : Visibility.Visible;
            DurationText.Visibility = _compactLayout ? Visibility.Collapsed : Visibility.Visible;
            if (compactChanged && _runtime is not null)
            {
                var today = _runtime.History.Totals(_runtime.Today);
                TodaySummary.Text = (_compactLayout ? string.Empty : $"今日 {today.Minutes} 分钟 · ") + (_runtime.History.LastError is null ? "专注记录" : "记录未保存");
            }
            UpdateDialSize();
        }
        finally { _layoutUpdating = false; }
    }

    private void UpdateDialSize()
    {
        if (IsMiniMode) return;
        if (DialArea is null || DialArea.ActualHeight <= 0 || DialArea.ActualWidth <= 0) return;
        var diameter = Math.Min(Math.Min(312, DialArea.ActualWidth), Math.Max(188, DialArea.ActualHeight - 12));
        DialFrame.Width = DialFrame.Height = diameter;
        var small = diameter < 224;
        NumberArea.Width = small ? 128 : 180;
        NumberArea.Height = small ? 62 : 78;
        MinutesInput.FontSize = small ? 44 : 58;
        TimeText.FontSize = small ? 32 : 48;
    }

    private void UpdatePresetLayout(bool force = false)
    {
        if (!force && _presetsEditable == CanEdit) return;
        var current = PresetHost.Height;
        _presetsEditable = CanEdit;
        Presets.IsHitTestVisible = CanEdit;
        Presets.Visibility = CanEdit ? Visibility.Visible : Visibility.Hidden;
        PresetHost.BeginAnimation(HeightProperty, null);
        PresetHost.Height = CanEdit ? 36 : 0;
        if (!force && IsLoaded && UiTheme.MotionEnabled)
            PresetHost.BeginAnimation(HeightProperty, new DoubleAnimation(current, PresetHost.Height, TimeSpan.FromMilliseconds(160))
            { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private bool IsWindowDragTarget(DependencyObject? source)
    {
        for (var current = source; current is not null && current != FocusScroll;)
        {
            if (current == DialFrame || current is ButtonBase or TextBoxBase or RangeBase or ScrollBar) return false;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return source is not null;
    }

    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || Mouse.LeftButton != MouseButtonState.Pressed || !IsWindowDragTarget(e.OriginalSource as DependencyObject)) return;
        e.Handled = true;
        try { DragMove(); }
        catch (InvalidOperationException) { /* Mouse release can race entry to the native move loop. */ }
    }

    private IntPtr WindowLayoutHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case 0x0231: _insideSizeMove = true; _placementTimer.Stop(); break;
            case 0x0232: _insideSizeMove = false; FitToScreen(); SaveWindowPlacement(); break;
            case 0x007E: // Display topology changed.
            case 0x02E0: QueueScreenRefresh(); break; // Let WPF apply its DPI change first.
            case 0x001A:
                if (wParam.ToInt64() == 0x002F) QueueScreenRefresh();
                break;
            case 0x0112:
                var command = wParam.ToInt64() & 0xFFF0;
                if (IsMiniMode && command == 0xF030) { handled = true; return IntPtr.Zero; }
                if (command is 0xF020 or 0xF030) SaveWindowPlacement();
                break;
        }
        return IntPtr.Zero;
    }

    private void QueueScreenRefresh()
    {
        if (_screenRefreshQueued || _layoutClosed) return;
        _screenRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _screenRefreshQueued = false;
            if (_layoutClosed || _insideSizeMove || _switchingMode) return;
            FitToScreen();
            QueuePlacementSave();
        }));
    }

    private void FitToScreen()
    {
        if (!_managePlacement || WindowState != WindowState.Normal || !NativeMethods.TryGetWindowWorkArea(this, out var area)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var availableWidth = (area.Right - area.Left) / dpi.DpiScaleX;
        var availableHeight = (area.Bottom - area.Top) / dpi.DpiScaleY;
        MinWidth = Math.Min(IsMiniMode ? FocusWindowOptions.MiniMinimumWidth : FocusWindowOptions.MinimumWidth, availableWidth);
        MinHeight = Math.Min(IsMiniMode ? FocusWindowOptions.MiniMinimumHeight : FocusWindowOptions.MinimumHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
        NativeMethods.EnsureWindowInWorkArea(this);
    }

    private void CaptureNormalPlacement()
    {
        if (_switchingMode || WindowState != WindowState.Normal) return;
        var hasBounds = NativeMethods.TryGetWindowBounds(this, out var bounds);
        if (hasBounds && !NativeMethods.IsWindowNormal(this)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var options = _viewOptions.Copy();
        var width = hasBounds ? bounds.Width / dpi.DpiScaleX : Width;
        var height = hasBounds ? bounds.Height / dpi.DpiScaleY : Height;
        if (IsMiniMode)
        {
            options.MiniWidth = width; options.MiniHeight = height;
            if (hasBounds) { options.MiniLeftPixels = (int)bounds.Left; options.MiniTopPixels = (int)bounds.Top; }
        }
        else
        {
            options.Width = width; options.Height = height;
            if (hasBounds) { options.LeftPixels = (int)bounds.Left; options.TopPixels = (int)bounds.Top; }
        }
        _viewOptions = options;
        _lastNormalPlacement = options.Copy();
    }

    private void QueuePlacementSave()
    {
        if (!_managePlacement || !_placementReady || _layoutClosed || _switchingMode || _runtime?.IsDisposed == true || WindowState != WindowState.Normal) return;
        CaptureNormalPlacement();
        if (_insideSizeMove) return;
        _placementTimer.Stop();
        _placementTimer.Start();
    }

    internal void SaveWindowPlacement()
    {
        _placementTimer.Stop();
        if (!_managePlacement || !_placementReady || _switchingMode || _runtime?.IsDisposed != false) return;
        CaptureNormalPlacement();
        if (_lastNormalPlacement is null) return;
        try { _runtime.SaveWindowOptions(_lastNormalPlacement); WindowError.Text = string.Empty; }
        catch (Exception ex)
        {
            AppLogger.Error("Unable to save focus window placement.", ex);
            WindowError.Text = "窗口位置未保存，请检查配置目录权限后重试。";
        }
        UpdateResponsiveLayout();
        RefreshMiniStatus();
    }

    private void CloseWindowLayout()
    {
        _layoutClosed = true;
        _placementTimer.Stop();
        _windowSource?.RemoveHook(WindowLayoutHook);
        PresetHost.BeginAnimation(HeightProperty, null);
        ContentRoot.BeginAnimation(OpacityProperty, null);
        MiniRoot.BeginAnimation(OpacityProperty, null);
    }
}
