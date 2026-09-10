using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class MainWindow
{
    private DockLayout? _dockLayout;
    private DockTransition? _dockTransition;
    private bool _initialDockRetraction;
    private bool _dockPositionDirty;
    private DockLayout? _lastDockVisual;
    private double _lastDockProgress = double.NaN;
    private bool _lastDockCollapsed, _lastRetraction;
    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly TranslateTransform _dockOffset = new();
    private bool IsEdgeDocked => _dockLayout is not null;
    private bool CanPlayDockAnimation => !IsEdgeDocked ||
        (_dockTransition is { IsExpanded: true, TargetCollapsed: false });
    private bool EffectiveClickThrough => _settings.ClickThrough && !IsEdgeDocked && !_isDragging;
    private Point PositionToPersist => _dockLayout?.Expanded.TopLeft ?? new Point(Left, Top);

    private void InitializeDocking()
    {
        SpriteImage.RenderTransform = _dockOffset;
        _dockTimer.Tick += DockTimer_Tick;
    }

    private bool DockAfterDrag()
    {
        if (!_settings.EdgeAutoHide) return false;
        var area = GetCurrentWorkAreaInDips();
        var rect = new Rect(Left, Top, Width, Height);
        return EnterDock(EdgeDocking.Detect(rect, area), area, collapse: true);
    }

    private bool EnterDock(DockEdge edge, Rect workArea, bool collapse)
    {
        var layout = EdgeDocking.Create(edge, new Rect(Left, Top, Width, Height), workArea);
        if (layout is null) return false;
        CancelPointerInteraction();
        StopRoaming(returnToIdle: true);
        _dockLayout = layout;
        _dockTransition = new DockTransition(collapse, _clock);
        _initialDockRetraction = collapse;
        _settings.Left = layout.Expanded.Left;
        _settings.Top = layout.Expanded.Top;
        _dockPositionDirty = true;
        PlayAnimation(PetState.Idle, restart: true);
        ApplyEffectiveWindowOptions();
        ApplyDockVisual();
        _dockTimer.Interval = TimeSpan.FromMilliseconds(16);
        _dockTimer.Start();
        return true;
    }

    private void DockTimer_Tick(object? sender, EventArgs e)
    {
        if (_dockLayout is null || _dockTransition is null) return;
        if (!IsVisible) return;
        var local = PointFromScreen(CursorScreenPosition());
        var point = new Point(Left + local.X, Top + local.Y);
        var overHandle = _dockLayout.Handle.Contains(point);
        var busy = _pointerDown || _isDragging || _isMenuOpen || _settingsWindow is not null;
        _dockTransition.Tick(overHandle, _dockLayout.Expanded.Contains(point), busy);
        ApplyDockVisual();
        _dockTimer.Interval = TimeSpan.FromMilliseconds(_dockTransition.IsAnimating ? 16 : 100);
        if (_dockPositionDirty && !_dockTransition.IsAnimating)
        {
            _dockPositionDirty = false;
            SaveWindowPosition();
        }
    }

    private void ApplyDockVisual()
    {
        if (_dockLayout is null || _dockTransition is null) return;
        var collapsed = _dockTransition.IsCollapsed;
        if (collapsed || !_dockTransition.TargetCollapsed) _initialDockRetraction = false;
        var progress = _dockTransition.Progress;
        if (ReferenceEquals(_lastDockVisual, _dockLayout) && _lastDockProgress == progress &&
            _lastDockCollapsed == collapsed && _lastRetraction == _initialDockRetraction) return;
        _lastDockVisual = _dockLayout; _lastDockProgress = progress;
        _lastDockCollapsed = collapsed; _lastRetraction = _initialDockRetraction;
        var viewport = _initialDockRetraction ? _dockLayout.RetractionViewport : _dockLayout.Expanded;
        SetDockBounds(collapsed ? _dockLayout.Handle : viewport);
        var offset = _initialDockRetraction
            ? _dockLayout.RetractionOffset(_dockTransition.Progress)
            : _dockLayout.Offset(_dockTransition.Progress);
        _dockOffset.X = offset.X;
        _dockOffset.Y = offset.Y;
        SpriteImage.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        DockHandle.Width = _dockLayout.Handle.Width;
        DockHandle.Height = _dockLayout.Handle.Height;
        DockHandle.HorizontalAlignment = _dockLayout.Edge switch
        {
            DockEdge.Left => HorizontalAlignment.Left,
            DockEdge.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
        DockHandle.VerticalAlignment = _dockLayout.Edge switch
        {
            DockEdge.Top => VerticalAlignment.Top,
            DockEdge.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center
        };
        DockArrow.Text = _dockLayout.Edge switch
        {
            DockEdge.Left => "\uE76C",
            DockEdge.Right => "\uE76B",
            DockEdge.Top => "\uE70D",
            _ => "\uE70E"
        };
        DockHandle.ToolTip = $"展开 {_pet.Manifest.Name}";
        DockHandle.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        RefreshActivityTimers();
    }

    private void SetDockBounds(Rect bounds)
    {
        Width = bounds.Width;
        Height = bounds.Height;
        Left = bounds.Left;
        Top = bounds.Top;
    }

    private void ExpandDock(bool immediately)
    {
        if (_dockTransition is null) return;
        _dockTransition.Expand(immediately);
        ApplyDockVisual();
        _dockTimer.Interval = TimeSpan.FromMilliseconds(16);
    }

    private DockEdge LeaveDock()
    {
        if (_dockLayout is null) return DockEdge.None;
        var edge = _dockLayout.Edge;
        var expanded = _dockLayout.Expanded;
        _dockTimer.Stop();
        _dockLayout = null;
        _lastDockVisual = null;
        _dockTransition = null;
        _initialDockRetraction = false;
        _dockPositionDirty = false;
        SetDockBounds(expanded);
        _dockOffset.X = _dockOffset.Y = 0;
        SpriteImage.Visibility = Visibility.Visible;
        DockHandle.Visibility = Visibility.Collapsed;
        ApplyEffectiveWindowOptions();
        if (!_isExiting && _pet is not null) PlayAnimation(PetState.Idle, restart: true);
        return edge;
    }

    private void RestoreDock(DockEdge edge)
    {
        if (edge != DockEdge.None && _settings.EdgeAutoHide)
            EnterDock(edge, GetCurrentWorkAreaInDips(), collapse: false);
    }

    private void ApplyEffectiveWindowOptions()
    {
        Topmost = IsEdgeDocked || _settings.Topmost;
        if (_sourceReady) NativeMethods.SetClickThrough(this, EffectiveClickThrough);
    }

    private void OnDockDisplayChanged()
    {
        if (_isExiting || !_loadedOnce) return;
        if (!IsEdgeDocked)
        {
            if (IsVisible) { EnsureWindowInWorkArea(); SaveWindowPosition(); }
            return;
        }
        var currentArea = GetCurrentWorkAreaInDips();
        var previousArea = _dockLayout!.WorkArea;
        if (Math.Abs(currentArea.Left - previousArea.Left) < 0.5 &&
            Math.Abs(currentArea.Top - previousArea.Top) < 0.5 &&
            Math.Abs(currentArea.Width - previousArea.Width) < 0.5 &&
            Math.Abs(currentArea.Height - previousArea.Height) < 0.5) return;
        LeaveDock();
        EnsureWindowInWorkArea();
        SaveWindowPosition();
    }
}
