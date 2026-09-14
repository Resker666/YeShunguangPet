using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace YeShunguangPet;

public sealed class CapturePinWindow : ThemedWindow
{
    private enum ResizeEdge { NorthWest, North, NorthEast, East, SouthEast, South, SouthWest, West }

    public BitmapSource Snapshot { get; }
    private readonly Action<BitmapSource> _copy;
    private readonly Image _image;
    private readonly Border _tools;
    private readonly Border _surface;
    private readonly ToggleButton _pin;
    private readonly ContextMenu _menu;
    private readonly List<Thumb> _resizeHandles = new();
    private readonly List<Border> _resizeIndicators = new();
    private readonly CaptureRect? _anchor;
    private readonly Func<Point> _cursorPosition;
    private bool _hovered;
    private double _zoom = 1;
    private ResizeEdge _resizeEdge;
    private Point _resizeStartCursor;
    private double _resizeStartZoom, _resizeStartLeft, _resizeStartTop, _resizeStartWidth, _resizeStartHeight, _resizeStartImageWidth, _resizeStartImageHeight;
    public double Zoom => _zoom;
    public CapturePinWindow(BitmapSource image, int number, Action<BitmapSource>? copy = null, CaptureRect? anchor = null, Func<Point>? cursorPosition = null)
    {
        Snapshot = image; _copy = copy ?? Clipboard.SetImage; _anchor = anchor; _cursorPosition = cursorPosition ?? ScreenCapture.CursorPosition;
        Title = $"贴图 {number}"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; ShowInTaskbar = false; Topmost = true; ShowActivated = false; WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent; Opacity = 0;
        var grid = new Grid();
        _image = new Image { Source = image, Stretch = Stretch.Uniform };
        grid.Children.Add(_image);
        AddResizeHandle(grid, ResizeEdge.NorthWest, 14, 14, HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
        AddResizeHandle(grid, ResizeEdge.North, 24, 8, HorizontalAlignment.Center, VerticalAlignment.Top, Cursors.SizeNS);
        AddResizeHandle(grid, ResizeEdge.NorthEast, 14, 14, HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
        AddResizeHandle(grid, ResizeEdge.East, 8, 24, HorizontalAlignment.Right, VerticalAlignment.Center, Cursors.SizeWE);
        AddResizeHandle(grid, ResizeEdge.SouthEast, 14, 14, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);
        AddResizeHandle(grid, ResizeEdge.South, 24, 8, HorizontalAlignment.Center, VerticalAlignment.Bottom, Cursors.SizeNS);
        AddResizeHandle(grid, ResizeEdge.SouthWest, 14, 14, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
        AddResizeHandle(grid, ResizeEdge.West, 8, 24, HorizontalAlignment.Left, VerticalAlignment.Center, Cursors.SizeWE);
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        _pin = new ToggleButton { Width = 28, Height = 28, IsChecked = true, Content = Glyph("\uE718"), ToolTip = "置顶贴图" };
        _pin.SetResourceReference(StyleProperty, "ToolToggle"); System.Windows.Automation.AutomationProperties.SetName(_pin, "置顶贴图");
        _pin.Click += (_, _) => Topmost = _pin.IsChecked == true; commands.Children.Add(_pin);
        var more = Tool("\uE712", "贴图操作", () => { _menu!.PlacementTarget = this; _menu.IsOpen = true; }); commands.Children.Add(more);
        commands.Children.Add(Tool("\uE711", "关闭贴图", Close));
        _tools = new Border { Child = commands, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Opacity = 0, IsHitTestVisible = false };
        _tools.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); grid.Children.Add(_tools);
        _surface = new Border { Child = grid, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10) };
        _surface.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 10, Opacity = 0.32 };
        Content = _surface; ApplyTheme();
        _menu = new PetContextMenu();
        AddMenu("复制", () => _copy(Snapshot)); AddMenu("保存 PNG", Save);
        _menu.Items.Add(new Separator()); AddMenu("放大", () => SetZoom(_zoom * 1.25)); AddMenu("缩小", () => SetZoom(_zoom / 1.25)); AddMenu("恢复大小", ResetZoom);
        _menu.Items.Add(new Separator()); AddMenu("隐藏贴图", Hide); AddMenu("关闭贴图", Close);
        ContextMenu = _menu;
        MouseEnter += (_, _) => SetHoverState(true);
        MouseLeave += (_, _) => { if (!_menu.IsOpen) SetHoverState(false); };
        Activated += (_, _) => SetHoverState(true);
        Deactivated += (_, _) => { if (!_menu.IsOpen && !IsMouseOver) SetHoverState(false); };
        _menu.Closed += (_, _) => { if (!IsMouseOver) SetHoverState(false); };
        _surface.MouseLeftButtonDown += (_, e) =>
        {
            for (var node = e.OriginalSource as DependencyObject; node is Visual && node != _surface; node = VisualTreeHelper.GetParent(node))
                if (node is ButtonBase or Thumb) return;
            if (e.ClickCount == 2) ResetZoom();
            else { try { DragMove(); } catch (InvalidOperationException) { } }
            e.Handled = true;
        };
        MouseWheel += (_, e) => { var point = e.GetPosition(this); var width = Width; var height = Height; SetZoom(_zoom * (e.Delta > 0 ? 1.25 : 0.8));
            Left -= (Width - width) * point.X / Math.Max(1, width); Top -= (Height - height) * point.Y / Math.Max(1, height); NativeMethods.EnsureWindowInWorkArea(this); e.Handled = true; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.Delete) Close();
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Run(() => _copy(Snapshot));
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Run(Save);
            else if (e.Key is Key.Add or Key.OemPlus) SetZoom(_zoom * 1.25);
            else if (e.Key is Key.Subtract or Key.OemMinus) SetZoom(_zoom / 1.25);
            else if (e.Key is Key.D0 or Key.NumPad0) ResetZoom(); else return;
            e.Handled = true;
        };
        SourceInitialized += (_, _) =>
        {
            PositionFromAnchor();
            if (_anchor is null) NativeMethods.EnsureWindowInWorkArea(this);
        };
        Loaded += (_, _) =>
        {
            ResetZoom();
            PositionFromAnchor();
            NativeMethods.EnsureWindowInWorkArea(this);
            Opacity = 1;
        };
        Closed += (_, _) => { _menu.IsOpen = false; (_menu as IDisposable)?.Dispose(); ContextMenu = null; _image.Source = null; _surface.Effect = null; };
        Width = Math.Max(96, Math.Min(image.PixelWidth, 800)); Height = Math.Max(48, Width * image.PixelHeight / image.PixelWidth);
    }
    private TextBlock Glyph(string value)
    {
        var glyph = new TextBlock { Text = value, FontSize = 14 }; glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont"); return glyph;
    }
    private void AddResizeHandle(Grid grid, ResizeEdge edge, double width, double height, HorizontalAlignment horizontal, VerticalAlignment vertical, Cursor cursor)
    {
        var indicatorWidth = width == height ? 10 : width > height ? 20 : 6;
        var indicatorHeight = width == height ? 10 : height > width ? 20 : 6;
        var indicator = new Border
        {
            Width = indicatorWidth, Height = indicatorHeight,
            HorizontalAlignment = horizontal, VerticalAlignment = vertical,
            CornerRadius = new CornerRadius(Math.Min(indicatorWidth, indicatorHeight) / 2),
            BorderThickness = new Thickness(1), IsHitTestVisible = false, Opacity = 0
        };
        indicator.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        indicator.SetResourceReference(Border.BorderBrushProperty, "SurfaceBrush");
        _resizeIndicators.Add(indicator);
        grid.Children.Add(indicator);

        var handle = new Thumb
        {
            Tag = edge, Width = width, Height = height, HorizontalAlignment = horizontal, VerticalAlignment = vertical,
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0),
            Opacity = 0, Focusable = false, Cursor = cursor, ToolTip = "拖动调整贴图大小"
        };
        System.Windows.Automation.AutomationProperties.SetName(handle, $"贴图缩放 {ResizeEdgeName(edge)}");
        handle.DragStarted += ResizeStarted;
        handle.DragDelta += ResizeDelta;
        handle.DragCompleted += ResizeCompleted;
        _resizeHandles.Add(handle);
        grid.Children.Add(handle);
    }
    private static string ResizeEdgeName(ResizeEdge edge) => edge switch
    {
        ResizeEdge.NorthWest => "左上角", ResizeEdge.North => "上边", ResizeEdge.NorthEast => "右上角",
        ResizeEdge.East => "右边", ResizeEdge.SouthEast => "右下角", ResizeEdge.South => "下边",
        ResizeEdge.SouthWest => "左下角", _ => "左边"
    };
    private void ResizeStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb { Tag: ResizeEdge edge }) return;
        _resizeEdge = edge;
        _resizeStartZoom = _zoom;
        _resizeStartCursor = _cursorPosition();
        _resizeStartLeft = Left;
        _resizeStartTop = Top;
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        _resizeStartImageWidth = Snapshot.PixelWidth * _resizeStartZoom;
        _resizeStartImageHeight = Snapshot.PixelHeight * _resizeStartZoom;
    }
    private void ResizeDelta(object sender, DragDeltaEventArgs e)
    {
        var cursor = _cursorPosition();
        var horizontal = 1 + (cursor.X - _resizeStartCursor.X) / Math.Max(1, _resizeStartImageWidth) * (HasWestEdge(_resizeEdge) ? -1 : 1);
        var vertical = 1 + (cursor.Y - _resizeStartCursor.Y) / Math.Max(1, _resizeStartImageHeight) * (HasNorthEdge(_resizeEdge) ? -1 : 1);
        var factor = IsHorizontalEdge(_resizeEdge) ? horizontal : IsVerticalEdge(_resizeEdge) ? vertical :
            Math.Abs(horizontal - 1) >= Math.Abs(vertical - 1) ? horizontal : vertical;
        SetZoom(_resizeStartZoom * Math.Max(0.01, factor));
        if (HasWestEdge(_resizeEdge)) Left = _resizeStartLeft + _resizeStartWidth - Width;
        if (HasNorthEdge(_resizeEdge)) Top = _resizeStartTop + _resizeStartHeight - Height;
        e.Handled = true;
    }
    private void ResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled)
        {
            SetZoom(_resizeStartZoom);
            Left = _resizeStartLeft;
            Top = _resizeStartTop;
        }
        NativeMethods.EnsureWindowInWorkArea(this);
        e.Handled = true;
    }
    private static bool HasWestEdge(ResizeEdge edge) => edge is ResizeEdge.NorthWest or ResizeEdge.West or ResizeEdge.SouthWest;
    private static bool HasNorthEdge(ResizeEdge edge) => edge is ResizeEdge.NorthWest or ResizeEdge.North or ResizeEdge.NorthEast;
    private static bool IsHorizontalEdge(ResizeEdge edge) => edge is ResizeEdge.East or ResizeEdge.West;
    private static bool IsVerticalEdge(ResizeEdge edge) => edge is ResizeEdge.North or ResizeEdge.South;
    private Button Tool(string glyph, string tip, Action action)
    {
        var button = new Button { Content = Glyph(glyph), Width = 28, Height = 28, MinHeight = 28, ToolTip = tip };
        button.SetResourceReference(StyleProperty, "IconButton"); System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => Run(action); return button;
    }
    private void AddMenu(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => Run(action); _menu.Items.Add(item); }
    private void Run(Action action) { try { action(); } catch (Exception ex) { AppDialog.Show(this, ex.Message, "贴图操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    internal override void OnThemeUpdated() { base.OnThemeUpdated(); if (_surface is not null) ApplyTheme(); }
    private void ApplyTheme()
    {
        var colors = UiTheme.GetColors(); var surface = colors["SurfaceBrush"]; var accent = colors["AccentBrush"];
        _surface.Background = new SolidColorBrush(Color.FromArgb(218, surface.R, surface.G, surface.B));
        _surface.BorderBrush = new SolidColorBrush(Color.FromArgb(_hovered ? (byte)255 : (byte)235, accent.R, accent.G, accent.B));
        _tools.BorderBrush = new SolidColorBrush(Color.FromArgb(220, accent.R, accent.G, accent.B));
        _tools.BorderThickness = new Thickness(1);
    }
    private void SetHoverState(bool hovered)
    {
        _hovered = hovered; _tools.Opacity = hovered ? 1 : 0; _tools.IsHitTestVisible = hovered; ApplyTheme();
        foreach (var indicator in _resizeIndicators) indicator.Opacity = hovered ? 0.95 : 0;
        _surface.BorderThickness = new Thickness(hovered ? 3 : 2);
    }
    private void PositionFromAnchor()
    {
        if (_anchor is { } anchor) NativeMethods.MoveWindowPixels(this, anchor.X, anchor.Y);
    }
    private double MaximumZoom()
    {
        var dpi = VisualTreeHelper.GetDpi(this); var work = SystemParameters.WorkArea;
        if (NativeMethods.TryGetWindowWorkArea(this, out var area)) work = new Rect(0, 0, (area.Right - area.Left) / dpi.DpiScaleX, (area.Bottom - area.Top) / dpi.DpiScaleY);
        return Math.Min(4, Math.Min((work.Width - 16) * dpi.DpiScaleX / Snapshot.PixelWidth, (work.Height - 16) * dpi.DpiScaleY / Snapshot.PixelHeight));
    }
    public void SetZoom(double zoom)
    {
        var dpi = VisualTreeHelper.GetDpi(this); var maximum = Math.Max(0.01, MaximumZoom());
        var minimum = Math.Max(0.05, Math.Max(94 * dpi.DpiScaleX / Snapshot.PixelWidth, 46 * dpi.DpiScaleY / Snapshot.PixelHeight));
        _zoom = Math.Clamp(zoom, Math.Min(minimum, maximum), maximum);
        _image.Width = Snapshot.PixelWidth * _zoom / dpi.DpiScaleX;
        _image.Height = Snapshot.PixelHeight * _zoom / dpi.DpiScaleY;
        Width = _image.Width + 2;
        Height = _image.Height + 2;
        _tools.Margin = Width >= 160 ? new Thickness(0, 8, 16, 0) : new Thickness(0);
        ToolTip = $"{Snapshot.PixelWidth} x {Snapshot.PixelHeight} · {_zoom:P0}";
    }
    private void ResetZoom() => SetZoom(Math.Min(1, MaximumZoom() * 0.8));
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); if (Snapshot is not null) SetZoom(_zoom); }
    private void Save()
    {
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", DefaultExt = ".png", AddExtension = true, FileName = $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        if (dialog.ShowDialog(this) == true) CaptureRaster.SavePng(Snapshot, dialog.FileName);
    }
}
