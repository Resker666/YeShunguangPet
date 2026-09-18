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
    private const double TitleBarHeight = 36;

    public BitmapSource Snapshot { get; }
    private readonly Action<BitmapSource> _copy;
    private readonly Image _image;
    private readonly Border _tools;
    private readonly Border _surface;
    private readonly Border _titleBar;
    private readonly ToggleButton _pin;
    private readonly Button _maximize;
    private readonly ContextMenu _menu;
    private readonly List<Thumb> _resizeHandles = new();
    private readonly List<Border> _resizeIndicators = new();
    private readonly CaptureRect? _anchor;
    private readonly Func<Point> _cursorPosition;
    private bool _hovered;
    private bool _maximized;
    private CaptureRect? _restoreBounds;
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
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _image = new Image { Source = image, Stretch = Stretch.Uniform };
        Grid.SetRow(_image, 1); layout.Children.Add(_image);
        var titleContent = new Grid();
        titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _pin = new ToggleButton { Width = 34, Height = 34, IsChecked = true, Content = Glyph("\uE718"), ToolTip = "置顶贴图",
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0) };
        _pin.SetResourceReference(StyleProperty, "ToolToggle"); System.Windows.Automation.AutomationProperties.SetName(_pin, "置顶贴图");
        _pin.Click += (_, _) => Topmost = _pin.IsChecked == true; titleContent.Children.Add(_pin);
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        commands.Children.Add(Tool("\uE712", "贴图操作", () => { _menu!.PlacementTarget = this; _menu.IsOpen = true; }));
        commands.Children.Add(Tool("\uE921", "隐藏贴图", Hide));
        _maximize = Tool("\uE922", "最大化贴图", ToggleMaximize); commands.Children.Add(_maximize);
        commands.Children.Add(Tool("\uE711", "关闭贴图", Close));
        _tools = new Border { Child = commands, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Opacity = 1, IsHitTestVisible = true };
        Grid.SetColumn(_tools, 1); titleContent.Children.Add(_tools);
        _titleBar = new Border { Height = TitleBarHeight, Child = titleContent, BorderThickness = new Thickness(0, 0, 0, 1) };
        layout.Children.Add(_titleBar);
        grid.Children.Add(layout);
        AddResizeHandle(grid, ResizeEdge.NorthWest, 14, 14, HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
        AddResizeHandle(grid, ResizeEdge.North, 24, 8, HorizontalAlignment.Center, VerticalAlignment.Top, Cursors.SizeNS);
        AddResizeHandle(grid, ResizeEdge.NorthEast, 14, 14, HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
        AddResizeHandle(grid, ResizeEdge.East, 8, 24, HorizontalAlignment.Right, VerticalAlignment.Center, Cursors.SizeWE);
        AddResizeHandle(grid, ResizeEdge.SouthEast, 14, 14, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);
        AddResizeHandle(grid, ResizeEdge.South, 24, 8, HorizontalAlignment.Center, VerticalAlignment.Bottom, Cursors.SizeNS);
        AddResizeHandle(grid, ResizeEdge.SouthWest, 14, 14, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
        AddResizeHandle(grid, ResizeEdge.West, 8, 24, HorizontalAlignment.Left, VerticalAlignment.Center, Cursors.SizeWE);
        _surface = new Border { Child = grid, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        _surface.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Opacity = 0.22 };
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
            var titleHit = false;
            for (var node = e.OriginalSource as DependencyObject; node is Visual && node != _surface; node = VisualTreeHelper.GetParent(node))
            {
                if (node is ButtonBase or Thumb) return;
                if (node == _titleBar) titleHit = true;
            }
            if (e.ClickCount == 2 && titleHit) ToggleMaximize();
            else if (e.ClickCount == 2) ResetZoom();
            else if (!_maximized) { try { DragMove(); } catch (InvalidOperationException) { } }
            e.Handled = true;
        };
        MouseWheel += (_, e) => { if (_maximized) { e.Handled = true; return; } var point = e.GetPosition(this); var width = Width; var height = Height; SetZoom(_zoom * (e.Delta > 0 ? 1.25 : 0.8));
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
        Width = Math.Max(96, Math.Min(image.PixelWidth, 800)) + 2;
        Height = Math.Max(48, (Width - 2) * image.PixelHeight / image.PixelWidth) + TitleBarHeight + 2;
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
        if (_maximized || sender is not Thumb { Tag: ResizeEdge edge }) return;
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
        if (_maximized) return;
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
        var button = new Button { Content = Glyph(glyph), Width = 34, Height = 34, MinHeight = 34, ToolTip = tip, Padding = new Thickness(0) };
        button.SetResourceReference(StyleProperty, "IconButton"); System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => Run(action); return button;
    }
    private void AddMenu(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => Run(action); _menu.Items.Add(item); }
    private void Run(Action action) { try { action(); } catch (Exception ex) { AppDialog.Show(this, ex.Message, "贴图操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    internal override void OnThemeUpdated() { base.OnThemeUpdated(); if (_surface is not null) ApplyTheme(); }
    private void ApplyTheme()
    {
        var colors = UiTheme.GetColors(); var surface = colors["SurfaceBrush"];
        var dark = surface.R * 0.2126 + surface.G * 0.7152 + surface.B * 0.0722 < 128;
        var frame = dark ? Color.FromRgb(108, 112, 118) : Color.FromRgb(151, 154, 159);
        var title = dark ? Color.FromRgb(42, 44, 49) : Color.FromRgb(238, 239, 241);
        var separator = dark ? Color.FromRgb(73, 76, 82) : Color.FromRgb(205, 207, 211);
        _surface.Background = new SolidColorBrush(surface);
        _surface.BorderBrush = new SolidColorBrush(frame);
        _surface.BorderThickness = new Thickness(1);
        _titleBar.Background = new SolidColorBrush(title);
        _titleBar.BorderBrush = new SolidColorBrush(separator);
        _pin.Foreground = new SolidColorBrush(Color.FromRgb(25, 178, 111));
        foreach (var indicator in _resizeIndicators)
        {
            indicator.Background = new SolidColorBrush(Color.FromArgb(230, frame.R, frame.G, frame.B));
            indicator.BorderBrush = new SolidColorBrush(surface);
        }
    }
    private void SetHoverState(bool hovered)
    {
        _hovered = hovered; ApplyTheme();
        foreach (var indicator in _resizeIndicators) indicator.Opacity = 0;
    }
    private void PositionFromAnchor()
    {
        if (_maximized || _anchor is not { } anchor) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        NativeMethods.MoveWindowPixels(this, anchor.X, anchor.Y - (int)Math.Round((TitleBarHeight + 1) * dpi.DpiScaleY));
    }
    private double MaximumZoom()
    {
        var dpi = VisualTreeHelper.GetDpi(this); var work = SystemParameters.WorkArea;
        if (NativeMethods.TryGetWindowWorkArea(this, out var area)) work = new Rect(0, 0, (area.Right - area.Left) / dpi.DpiScaleX, (area.Bottom - area.Top) / dpi.DpiScaleY);
        return Math.Min(4, Math.Min((work.Width - 16) * dpi.DpiScaleX / Snapshot.PixelWidth,
            Math.Max(1, work.Height - TitleBarHeight - 18) * dpi.DpiScaleY / Snapshot.PixelHeight));
    }
    public void SetZoom(double zoom)
    {
        var dpi = VisualTreeHelper.GetDpi(this); var maximum = Math.Max(0.01, MaximumZoom());
        var minimum = Math.Max(0.05, Math.Max(174 * dpi.DpiScaleX / Snapshot.PixelWidth, 46 * dpi.DpiScaleY / Snapshot.PixelHeight));
        _zoom = Math.Clamp(zoom, Math.Min(minimum, maximum), maximum);
        if (_maximized) return;
        _image.Width = Snapshot.PixelWidth * _zoom / dpi.DpiScaleX;
        _image.Height = Snapshot.PixelHeight * _zoom / dpi.DpiScaleY;
        Width = _image.Width + 2;
        Height = _image.Height + TitleBarHeight + 2;
        ToolTip = $"{Snapshot.PixelWidth} x {Snapshot.PixelHeight} · {_zoom:P0}";
    }
    private void ResetZoom()
    {
        if (_maximized) ToggleMaximize();
        SetZoom(Math.Min(1, MaximumZoom() * 0.8));
    }
    private void ToggleMaximize()
    {
        if (!_maximized)
        {
            if (!NativeMethods.TryGetWindowBounds(this, out var bounds) || !NativeMethods.TryGetWindowWorkArea(this, out var area)) return;
            _restoreBounds = new CaptureRect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
            _maximized = true;
            _image.Width = double.NaN;
            _image.Height = double.NaN;
            foreach (var handle in _resizeHandles) handle.IsHitTestVisible = false;
            NativeMethods.SetWindowBoundsPixels(this, new CaptureRect(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top));
        }
        else
        {
            var bounds = _restoreBounds;
            _maximized = false;
            foreach (var handle in _resizeHandles) handle.IsHitTestVisible = true;
            SetZoom(_zoom);
            if (bounds is { } restore) NativeMethods.SetWindowBoundsPixels(this, restore);
        }
        _maximize.Content = Glyph(_maximized ? "\uE923" : "\uE922");
        _maximize.ToolTip = _maximized ? "还原贴图" : "最大化贴图";
        System.Windows.Automation.AutomationProperties.SetName(_maximize, _maximized ? "还原贴图" : "最大化贴图");
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); if (Snapshot is not null) SetZoom(_zoom); }
    private void Save()
    {
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", DefaultExt = ".png", AddExtension = true, FileName = $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        if (dialog.ShowDialog(this) == true) CaptureRaster.SavePng(Snapshot, dialog.FileName);
    }
}
