using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace YeShunguangPet;

public sealed class CaptureSurface : FrameworkElement
{
    public CaptureDocument Document { get; }
    public CaptureTool Tool { get; set; } = CaptureTool.Arrow;
    public Color InkColor { get; set; } = Colors.Crimson;
    public double StrokeWidth { get; set; } = 4;
    public double TextSize { get; set; } = 22;
    public double MosaicBlockSize { get; set; } = 8;
    public string LabelText { get; set; } = string.Empty;
    public event Action<string>? Error;
    private Point _start, _end;
    private List<Point>? _points;
    private Stroke? _inkPreview;
    private bool _finishing;
    public CaptureSurface(CaptureDocument document)
    {
        Document = document; Focusable = true; Cursor = Cursors.Cross;
        RefreshSize();
        MouseLeftButtonDown += Begin;
        MouseMove += Move;
        MouseLeftButtonUp += End;
        LostMouseCapture += (_, _) => { if (!_finishing) CancelGesture(); };
    }
    public void RefreshSize() { CancelGesture(); Width = Document.Crop.Width; Height = Document.Crop.Height; InvalidateVisual(); }
    private Point Position(MouseEventArgs e)
    {
        var p = e.GetPosition(this); var crop = Document.Crop;
        return new(Math.Clamp(p.X, 0, crop.Width) + crop.X, Math.Clamp(p.Y, 0, crop.Height) + crop.Y);
    }
    private void Begin(object sender, MouseButtonEventArgs e)
    {
        Focus(); BeginAnnotation(Position(e)); if (_points is not null) CaptureMouse(); e.Handled = true;
    }
    public void BeginAnnotation(Point point)
    {
        CancelGesture(); _start = _end = Clamp(point);
        if (Tool == CaptureTool.Text)
        {
            if (string.IsNullOrWhiteSpace(LabelText)) { Error?.Invoke("请先填写标注文字。"); return; }
            Try(() => Document.Add(new CaptureMark(Tool, _start, _end, InkColor, StrokeWidth, LabelText, TextSize))); return;
        }
        _points = new List<Point> { _start };
        if (Tool == CaptureTool.Pen) _inkPreview = new Stroke(new StylusPointCollection { new StylusPoint(_start.X, _start.Y) })
        { DrawingAttributes = new DrawingAttributes { Color = InkColor, Width = StrokeWidth, Height = StrokeWidth, IgnorePressure = true } };
        InvalidateVisual();
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (IsMouseCaptured) MoveAnnotation(Position(e));
    }
    public void MoveAnnotation(Point point)
    {
        if (_points is null) return;
        _end = Clamp(point);
        if (Tool == CaptureTool.Pen && _points.Count < 10000 && (_end - _points[^1]).Length >= 0.75)
        {
            _points.Add(_end); _inkPreview?.StylusPoints.Add(new StylusPoint(_end.X, _end.Y));
        }
        InvalidateVisual();
    }
    private void End(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured || _points is null) return;
        EndAnnotation(Position(e)); e.Handled = true;
    }
    public void EndAnnotation(Point point)
    {
        if (_points is null) return;
        MoveAnnotation(point); var points = _points.ToArray(); _finishing = true;
        try
        {
            ReleaseMouseCapture();
            if (Tool == CaptureTool.Crop)
            {
                var crop = Document.Crop;
                var selection = CaptureRect.Select(_start, _end, new CaptureRect(crop.X, crop.Y, crop.Width, crop.Height));
                if (selection.HasArea) Try(() => Document.SetCrop(new Int32Rect(selection.X, selection.Y, selection.Width, selection.Height)));
            }
            else if (Tool == CaptureTool.Pen || (_end - _start).Length >= 1)
                Try(() => Document.Add(new CaptureMark(Tool, _start, _end, InkColor, Tool == CaptureTool.Mosaic ? MosaicBlockSize : StrokeWidth, points: points)));
        }
        finally { _finishing = false; CancelGesture(); }
    }
    private Point Clamp(Point point)
    {
        if (!double.IsFinite(point.X + point.Y)) throw new ArgumentException("Invalid annotation point.");
        var crop = Document.Crop;
        return new(Math.Clamp(point.X, crop.X, crop.X + crop.Width), Math.Clamp(point.Y, crop.Y, crop.Y + crop.Height));
    }
    private void Try(Action action) { try { action(); } catch (Exception ex) { Error?.Invoke(ex.Message); } }
    public void CancelGesture()
    {
        _points = null; _inkPreview = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, Width, Height)));
        dc.PushTransform(new TranslateTransform(-Document.Crop.X, -Document.Crop.Y)); Document.Draw(dc);
        DrawPreview(dc);
        DrawMosaicGuides(dc);
        dc.Pop(); dc.Pop();
    }
    public void DrawPreview(DrawingContext dc)
    {
        if (_points is not null)
        {
            if (Tool == CaptureTool.Crop) dc.DrawRectangle(null, new System.Windows.Media.Pen(Brushes.White, 1), new Rect(_start, _end));
            else if (_inkPreview is not null) _inkPreview.Draw(dc);
            else new CaptureMark(Tool, _start, _end, InkColor, Tool == CaptureTool.Mosaic ? MosaicBlockSize : StrokeWidth).Draw(dc, Document.Image);
            if (Tool == CaptureTool.Mosaic) DrawMosaicGuide(dc, new Rect(_start, _end));
        }
    }
    private void DrawMosaicGuides(DrawingContext dc)
    {
        foreach (var mark in Document.Marks.Where(mark => mark.Tool == CaptureTool.Mosaic)) DrawMosaicGuide(dc, mark.Bounds);
    }
    private static void DrawMosaicGuide(DrawingContext dc, Rect rect)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        var colors = UiTheme.GetColors();
        var accent = colors["AccentBrush"];
        var halo = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)), 4);
        var line = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromArgb(245, accent.R, accent.G, accent.B)), 1.5) { DashStyle = DashStyles.Dash };
        halo.Freeze(); line.Freeze();
        dc.DrawRectangle(null, halo, rect); dc.DrawRectangle(null, line, rect);
    }
}
