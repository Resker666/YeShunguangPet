using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace YeShunguangPet;

public enum CaptureOutput { Copy, Save, Pin }
public sealed record CaptureResult(CaptureOutput Output, CaptureRect Region, BitmapSource? Image = null);

public sealed class CaptureSelection : IDisposable
{
    private readonly CaptureFrame _frame;
    private readonly Func<Point> _cursor;
    private readonly Action<BitmapSource> _copy;
    private readonly Action<BitmapSource>? _pin;
    private readonly Action<BitmapSource, CaptureRect>? _pinAt;
    private readonly Func<Window, string?>? _savePath;
    private readonly List<Window> _windows = new();
    private readonly TaskCompletionSource<CaptureResult?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CaptureRegion _region;
    private readonly CaptureSurface _annotation;
    private CaptureToolbarWindow? _toolbar;
    private TextBox? _textInput;
    private Canvas? _textHost;
    private Point _textPoint;
    private bool _painting, _finished, _committed, _modalExport, _placing;
    private int _toolbarMonitor;
    private CancellationTokenRegistration _cancellation;
    private Dispatcher? _dispatcher;
    public CaptureDocument Document { get; }
    public CaptureTool? Tool { get; private set; }
    public Color InkColor { get => _annotation.InkColor; set => _annotation.InkColor = value; }
    public double StrokeWidth { get => _annotation.StrokeWidth; set => _annotation.StrokeWidth = value; }
    public double TextSize { get => _annotation.TextSize; set => _annotation.TextSize = value; }
    public double MosaicBlockSize { get => _annotation.MosaicBlockSize; set => _annotation.MosaicBlockSize = value; }
    public string Error { get; private set; } = string.Empty;
    public Task<CaptureResult?> Result => _result.Task;
    public CaptureRect Selection => _region.Selection;
    public bool HasSelection => Selection.HasArea;
    public bool IsDragging => _region.IsDragging || _painting;

    public CaptureSelection(CaptureFrame frame, Func<Point>? cursorPosition = null, Action<BitmapSource>? copy = null, Func<Window, string?>? savePath = null, Action<BitmapSource>? pin = null, Action<BitmapSource, CaptureRect>? pinAt = null)
    {
        if (frame.Monitors.Count == 0 || frame.Image.PixelWidth != frame.Bounds.Width || frame.Image.PixelHeight != frame.Bounds.Height) throw new ArgumentException("Invalid capture frame.");
        _frame = frame; _cursor = cursorPosition ?? ScreenCapture.CursorPosition; _copy = copy ?? Clipboard.SetImage; _savePath = savePath; _pin = pin; _pinAt = pinAt;
        _region = new CaptureRegion(frame.Bounds); Document = new CaptureDocument(frame.Image); _annotation = new CaptureSurface(Document);
        Document.Changed += OnDocumentChanged; _annotation.Error += ShowError;
    }

    public Task<CaptureResult?> ShowAsync(CancellationToken cancellation, CaptureMode mode = CaptureMode.Region, Point? origin = null)
    {
        if (_finished || _windows.Count > 0) return _result.Task;
        if (cancellation.IsCancellationRequested) { Cancel(); return _result.Task; }
        _dispatcher = Dispatcher.CurrentDispatcher;
        _cancellation = cancellation.Register(() => _dispatcher.BeginInvoke(Cancel));
        SystemEvents.DisplaySettingsChanged += CancelForDisplay; SystemEvents.SessionSwitch += CancelForSession;
        try
        {
            foreach (var monitor in _frame.Monitors)
            {
                var window = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                    ShowActivated = false, Topmost = true, Background = Brushes.Black, WindowStartupLocation = WindowStartupLocation.Manual,
                    Width = monitor.Width, Height = monitor.Height, Left = monitor.X, Top = monitor.Y };
                var root = new Grid(); var surface = new SelectionSurface(this, monitor); var textLayer = new Canvas();
                root.Children.Add(surface); root.Children.Add(textLayer); window.Content = root;
                window.SourceInitialized += (_, _) => NativeMethods.SetWindowBoundsPixels(window, monitor);
                window.Loaded += (_, _) => NativeMethods.SetWindowBoundsPixels(window, monitor);
                window.PreviewKeyDown += (_, e) => HandleKey(e);
                window.Closed += (_, _) => { if (!_finished) Cancel(); };
                window.Deactivated += (_, _) => CheckActivation();
                _windows.Add(window); window.Show();
            }
            var point = origin ?? InitialCursor(); _toolbarMonitor = MonitorAt(point);
            _windows[_toolbarMonitor].Activate();
            Prepare(mode, point); if (_committed) ShowToolbar();
        }
        catch { Cancel(); throw; }
        return _result.Task;
    }
    private Point InitialCursor()
    {
        try { return _cursor(); } catch { var monitor = _frame.Monitors[0]; return new Point(monitor.X + monitor.Width / 2.0, monitor.Y + monitor.Height / 2.0); }
    }
    public void Prepare(CaptureMode mode, Point point)
    {
        if (_finished) return;
        if (mode == CaptureMode.CurrentScreen) _region.Set(_frame.Monitors[MonitorAt(point)]);
        else if (mode == CaptureMode.AllScreens) _region.Set(_frame.Bounds);
        else if (mode != CaptureMode.Region) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode != CaptureMode.Region) CommitSelection();
    }
    private int MonitorAt(Point point)
    {
        for (var i = 0; i < _frame.Monitors.Count; i++) if (_frame.Monitors[i].ToRect().Contains(point)) return i;
        return Enumerable.Range(0, _frame.Monitors.Count).OrderBy(i =>
        {
            var r = _frame.Monitors[i]; return (new Point(Math.Clamp(point.X, r.X, r.Right), Math.Clamp(point.Y, r.Y, r.Bottom)) - point).LengthSquared;
        }).First();
    }
    private Point ToImage(Point point) => new(point.X - _frame.Bounds.X, point.Y - _frame.Bounds.Y);
    public CaptureHit HitTest(Point point, double tolerance = 6) => _region.HitTest(point, tolerance);
    public bool Begin(Point point, double tolerance = 6)
    {
        if (_finished || _modalExport) return false;
        if (!CommitText()) return false; Error = string.Empty;
        var hit = HitTest(point, tolerance); _toolbarMonitor = MonitorAt(point);
        if (hit == CaptureHit.Move && Tool is { } tool)
        {
            if (tool == CaptureTool.Text) { BeginText(point); return false; }
            _annotation.Tool = tool; _annotation.BeginAnnotation(ToImage(point)); _painting = true;
        }
        else _region.Begin(point, hit);
        _toolbar?.Hide(); RefreshSurfaces(); return true;
    }
    public void Move(Point point)
    {
        if (_finished) return;
        if (_painting) _annotation.MoveAnnotation(ToImage(point)); else _region.Update(point);
        RefreshSurfaces();
    }
    public void End(Point point)
    {
        if (_finished) return;
        if (_painting) { _annotation.EndAnnotation(ToImage(point)); _painting = false; }
        else if (_region.IsDragging) { _region.End(point); if (HasSelection) CommitSelection(); }
        _toolbarMonitor = MonitorAt(point); RefreshSurfaces(); ShowToolbar();
    }
    public void CancelGesture()
    {
        _painting = false; _annotation.CancelGesture(); _region.CancelDrag(); RefreshSurfaces(); ShowToolbar();
    }
    private void CommitSelection()
    {
        var r = Selection; var initial = !_committed; _committed = true;
        Document.SetSelection(new Int32Rect(r.X - _frame.Bounds.X, r.Y - _frame.Bounds.Y, r.Width, r.Height), initial);
    }
    private void OnDocumentChanged()
    {
        var r = Document.Crop;
        if (!IsDragging) _region.Set(new CaptureRect(r.X + _frame.Bounds.X, r.Y + _frame.Bounds.Y, r.Width, r.Height));
        _annotation.RefreshSize(); RefreshSurfaces(); _toolbar?.Refresh();
    }
    public void SetTool(CaptureTool? tool)
    {
        if (!CommitText()) return; CancelGesture(); Tool = tool == CaptureTool.Crop ? null : tool; Error = string.Empty;
        _toolbar?.Refresh(); PlaceToolbar();
    }
    public void Undo() { if (!CommitText()) return; CancelGesture(); Document.Undo(); ShowToolbar(); }
    public void Redo() { if (!CommitText()) return; CancelGesture(); Document.Redo(); ShowToolbar(); }
    public bool Complete(CaptureOutput output)
    {
        if (_finished || _modalExport || !HasSelection || IsDragging) return false;
        if (!CommitText()) return false;
        Error = string.Empty; _toolbar?.Refresh();
        try
        {
            var image = Document.Flatten();
            if (output == CaptureOutput.Copy) _copy(image);
            else if (output == CaptureOutput.Save)
            {
                if (_toolbar is null && _savePath is null) throw new InvalidOperationException("保存窗口尚未打开。");
                _modalExport = true;
                string? path;
                try
                {
                    if (_savePath is not null) path = _savePath(_toolbar!);
                    else
                    {
                        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", DefaultExt = ".png", AddExtension = true, FileName = $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
                        path = dialog.ShowDialog(_toolbar!) == true ? dialog.FileName : null;
                    }
                }
                finally { _modalExport = false; }
                if (path is null) { _toolbar?.Activate(); return false; }
                CaptureRaster.SavePng(image, path);
            }
            else if (output == CaptureOutput.Pin) { _pinAt?.Invoke(image, Selection); _pin?.Invoke(image); }
            else throw new ArgumentOutOfRangeException(nameof(output));
            Finish(new CaptureResult(output, Selection, output == CaptureOutput.Pin ? image : null)); return true;
        }
        catch (Exception ex) { ShowError(ex.Message); return false; }
    }
    public void Cancel() => Finish(null);
    private void ShowError(string message) { Error = message; ShowToolbar(); _toolbar?.Refresh(); PlaceToolbar(); }
    private void RefreshSurfaces() { foreach (var w in _windows) ((Grid)w.Content).Children[0].InvalidateVisual(); }
    private void ShowToolbar()
    {
        if (!_committed || _finished || IsDragging || _windows.Count == 0) return;
        if (_toolbar is null)
        {
            _toolbar = new CaptureToolbarWindow(this); _toolbar.Deactivated += (_, _) => CheckActivation();
            _toolbar.Closed += (_, _) => { if (!_finished) Cancel(); };
            _toolbar.SizeChanged += (_, _) => PlaceToolbar();
        }
        _toolbar.Refresh(); PlaceToolbar(); if (!_toolbar.IsVisible) _toolbar.Show(); PlaceToolbar();
    }
    internal void PlaceToolbar()
    {
        if (_placing || _toolbar is null || _finished || _modalExport) return;
        _placing = true;
        try
        {
            if (!_frame.Monitors[_toolbarMonitor].ToRect().IntersectsWith(Selection.ToRect())) _toolbarMonitor = MonitorAt(new Point(Selection.Right - 1, Selection.Bottom - 1));
            var monitor = _frame.Monitors[_toolbarMonitor]; var dpi = VisualTreeHelper.GetDpi(_toolbar);
            _toolbar.Width = Math.Min(640, Math.Max(80, (monitor.Width - 16) / dpi.DpiScaleX));
            _toolbar.UpdateLayout();
            var size = new Size(_toolbar.ActualWidth * dpi.DpiScaleX, _toolbar.ActualHeight * dpi.DpiScaleY);
            if (size.Width < 1 || size.Height < 1) return;
            var position = CaptureRegion.PlaceToolbar(Selection, monitor, new Size(size.Width, _toolbar.CommandHeight * dpi.DpiScaleY));
            var top = Math.Clamp(position.Top - _toolbar.OptionsHeight * dpi.DpiScaleY, monitor.Y + 8, Math.Max(monitor.Y + 8, monitor.Bottom - 8 - size.Height));
            NativeMethods.MoveWindowPixels(_toolbar, (int)position.Left, (int)top);
        }
        finally { _placing = false; }
    }
    private void CheckActivation()
    {
        _dispatcher?.BeginInvoke(new Action(() =>
        {
            if (!_finished && !_modalExport && !_windows.Any(w => w.IsActive) && _toolbar?.IsActive != true) Cancel();
        }));
    }
    private void BeginText(Point point)
    {
        if (_windows.Count == 0) return;
        var index = MonitorAt(point); var root = (Grid)_windows[index].Content; var monitor = _frame.Monitors[index];
        _textHost = (Canvas)root.Children[1]; _textPoint = ToImage(point);
        var scale = root.ActualWidth / monitor.Width;
        _textInput = new TextBox { MaxLength = 500, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize = TextSize * scale, Foreground = new SolidColorBrush(InkColor),
            Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(35, 178, 108)), BorderThickness = new Thickness(1), Padding = new Thickness(2), Width = Math.Min(240, root.ActualWidth - 16), MinHeight = 32, MaxHeight = Math.Max(32, root.ActualHeight / 2) };
        Canvas.SetLeft(_textInput, Math.Clamp((point.X - monitor.X) * scale, 4, Math.Max(4, root.ActualWidth - _textInput.Width - 4)));
        Canvas.SetTop(_textInput, Math.Clamp((point.Y - monitor.Y) * root.ActualHeight / monitor.Height, 4, Math.Max(4, root.ActualHeight - 40)));
        _textInput.LostKeyboardFocus += (_, _) => { if (!_finished) CommitText(); };
        _textHost.Children.Add(_textInput); _textInput.Focus();
    }
    public bool CommitText()
    {
        var input = _textInput; if (input is null) return true; _textInput = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(input.Text)) Document.Add(new CaptureMark(CaptureTool.Text, _textPoint, _textPoint, InkColor, StrokeWidth, input.Text, TextSize));
            _textHost?.Children.Remove(input); _textHost = null; return true;
        }
        catch (Exception ex) { _textInput = input; ShowError(ex.Message); return false; }
    }
    private void CancelText() { var input = _textInput; _textInput = null; if (input is not null) _textHost?.Children.Remove(input); _textHost = null; }
    internal void HandleKey(KeyEventArgs e)
    {
        if (_finished || _modalExport) return;
        if (_textInput is not null && _textInput.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { CommitText(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape) { if (IsDragging) CancelGesture(); else Cancel(); }
        else if (Keyboard.FocusedElement is TextBoxBase or RangeBase or Selector) return;
        else if (e.Key == Key.Enter) Complete(CaptureOutput.Copy);
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.C) Complete(CaptureOutput.Copy); else if (e.Key == Key.S) Complete(CaptureOutput.Save);
            else if (e.Key == Key.Z) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) Redo(); else Undo(); }
            else if (e.Key == Key.Y) Redo(); else return;
        }
        else if (HasSelection && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var step = (Keyboard.Modifiers & ModifierKeys.Shift) == 0 ? 1 : 10;
            var point = new Point(Selection.X, Selection.Y); _region.Begin(point, CaptureHit.Move);
            _region.End(point + new Vector(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0));
            CommitSelection(); ShowToolbar();
        }
        else return;
        e.Handled = true;
    }
    private void CancelForDisplay(object? sender, EventArgs e) { if (!_finished) _dispatcher?.BeginInvoke(Cancel); }
    private void CancelForSession(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock) CancelForDisplay(sender, e); }
    private void Finish(CaptureResult? result, Exception? error = null)
    {
        if (_finished) return; _finished = true; CancelText(); _annotation.CancelGesture();
        _cancellation.Dispose(); SystemEvents.DisplaySettingsChanged -= CancelForDisplay; SystemEvents.SessionSwitch -= CancelForSession;
        Document.Changed -= OnDocumentChanged; _annotation.Error -= ShowError;
        _toolbar?.Close(); _toolbar = null;
        foreach (var window in _windows.ToArray()) window.Close(); _windows.Clear();
        if (error is null) _result.TrySetResult(result); else _result.TrySetException(error);
    }
    public void Dispose() => Cancel();

    private sealed class SelectionSurface : FrameworkElement
    {
        private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(35, 178, 108));
        private readonly CaptureSelection _owner;
        private readonly CaptureRect _monitor;
        private readonly BitmapSource _image;
        private bool _ending;
        public SelectionSurface(CaptureSelection owner, CaptureRect monitor)
        {
            _owner = owner; _monitor = monitor; _image = owner._frame.Crop(monitor); Cursor = Cursors.Cross; Focusable = true;
            MouseLeftButtonDown += (_, e) => { WithCursor(point => { Focus(); if (_owner.Begin(point, 6 * _monitor.Width / Math.Max(1, ActualWidth))) CaptureMouse(); }); e.Handled = true; };
            MouseMove += (_, _) => WithCursor(point => { if (IsMouseCaptured) _owner.Move(point); else UpdateCursor(point); });
            MouseLeftButtonUp += (_, e) => { if (IsMouseCaptured) WithCursor(point => { _ending = true; try { ReleaseMouseCapture(); _owner.End(point); } finally { _ending = false; } }); e.Handled = true; };
            LostMouseCapture += (_, _) => { if (!_ending) _owner.CancelGesture(); };
            MouseRightButtonDown += (_, e) => { _owner.Cancel(); e.Handled = true; };
        }
        private void WithCursor(Action<Point> action) { try { action(_owner._cursor()); } catch (Exception ex) { _owner.Finish(null, ex); } }
        private void UpdateCursor(Point point)
        {
            Cursor = _owner.HitTest(point, 6 * _monitor.Width / Math.Max(1, ActualWidth)) switch
            {
                CaptureHit.NorthWest or CaptureHit.SouthEast => Cursors.SizeNWSE,
                CaptureHit.NorthEast or CaptureHit.SouthWest => Cursors.SizeNESW,
                CaptureHit.North or CaptureHit.South => Cursors.SizeNS, CaptureHit.East or CaptureHit.West => Cursors.SizeWE,
                CaptureHit.Move when _owner.Tool is null => Cursors.SizeAll, CaptureHit.Move when _owner.Tool == CaptureTool.Text => Cursors.IBeam, _ => Cursors.Cross
            };
        }
        protected override void OnRender(DrawingContext dc)
        {
            var area = new Rect(0, 0, ActualWidth, ActualHeight); dc.DrawImage(_image, area);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, area);
            var selection = _owner.Selection; if (!selection.HasArea) return;
            var sx = ActualWidth / _monitor.Width; var sy = ActualHeight / _monitor.Height;
            var rect = new Rect((selection.X - _monitor.X) * sx, (selection.Y - _monitor.Y) * sy, selection.Width * sx, selection.Height * sy);
            dc.PushClip(new RectangleGeometry(rect)); dc.DrawImage(_image, area);
            dc.PushTransform(new MatrixTransform(sx, 0, 0, sy, (_owner._frame.Bounds.X - _monitor.X) * sx, (_owner._frame.Bounds.Y - _monitor.Y) * sy));
            _owner.Document.DrawMarks(dc); _owner._annotation.DrawPreview(dc); dc.Pop(); dc.Pop();
            dc.DrawRectangle(null, new System.Windows.Media.Pen(Green, 1.5), rect);
            foreach (var (_, p) in _owner._region.Handles()) dc.DrawRectangle(Green, null, new Rect((p.X - _monitor.X) * sx - 3, (p.Y - _monitor.Y) * sy - 3, 6, 6));
            if (rect.IntersectsWith(area))
            {
                var label = new FormattedText($"{selection.Width} x {selection.Height}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var x = Math.Clamp(rect.Left, 8, Math.Max(8, ActualWidth - label.Width - 16)); var y = Math.Clamp(rect.Top - 28, 8, Math.Max(8, ActualHeight - 28));
                dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(x, y, label.Width + 12, 24), 4, 4); dc.DrawText(label, new Point(x + 6, y + 3));
            }
        }
    }
}
