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
    private enum EditHit { None, Body, NorthWest, North, NorthEast, East, SouthEast, South, SouthWest, West, Start, End }

    public CaptureDocument Document { get; }
    private CaptureTool _tool = CaptureTool.Arrow;
    private Color _inkColor = Colors.Crimson;
    private double _strokeWidth = 4;
    private double _textSize = 22;
    private double _mosaicBlockSize = 8;
    private string _labelText = string.Empty;
    private double _interactionScale = 1;
    private int _selectedIndex = -1;
    private bool _syncingOptions;
    public CaptureTool Tool { get => _tool; set { _tool = value; Cursor = value == CaptureTool.Select ? Cursors.Arrow : Cursors.Cross; InvalidateVisual(); } }
    public Color InkColor { get => _inkColor; set { _inkColor = value; if (!_syncingOptions) ApplySelectedStyle(); } }
    public double StrokeWidth { get => _strokeWidth; set { _strokeWidth = value; if (!_syncingOptions) ApplySelectedStyle(); } }
    public double TextSize { get => _textSize; set { _textSize = value; if (!_syncingOptions) ApplySelectedStyle(); } }
    public double MosaicBlockSize { get => _mosaicBlockSize; set { _mosaicBlockSize = value; if (!_syncingOptions) ApplySelectedStyle(); } }
    public string LabelText { get => _labelText; set => _labelText = value ?? string.Empty; }
    public double InteractionScale { get => _interactionScale; set { _interactionScale = Math.Max(0.01, value); InvalidateVisual(); } }
    public int SelectedIndex => _selectedIndex;
    public CaptureMark? SelectedMark => _selectedIndex >= 0 && _selectedIndex < Document.Marks.Count ? Document.Marks[_selectedIndex] : null;
    public bool IsInteracting => _points is not null || _transforming;
    internal static bool IsDeleteKey(Key key) => key is Key.Delete or Key.Back;
    public event Action<string>? Error;
    public event Action? SelectionChanged;
    public event Action? TextEditRequested;

    private Point _start, _end;
    private List<Point>? _points;
    private Stroke? _inkPreview;
    private bool _finishing;
    private bool _transforming;
    private EditHit _editHit;
    private Point _transformStart;
    private CaptureMark? _transformOriginal;
    private CaptureMark? _transformPreview;

    public CaptureSurface(CaptureDocument document)
    {
        Document = document; Focusable = true; Cursor = Cursors.Cross;
        RefreshSize();
        MouseLeftButtonDown += Begin;
        MouseMove += Move;
        MouseLeftButtonUp += End;
        LostMouseCapture += (_, _) => { if (!_finishing) CancelGesture(); };
        Document.Changed += OnDocumentChanged;
    }

    public void RefreshSize() { CancelGesture(); Width = Document.Crop.Width; Height = Document.Crop.Height; InvalidateVisual(); }
    private Point Position(MouseEventArgs e)
    {
        var p = e.GetPosition(this); var crop = Document.Crop;
        return new(Math.Clamp(p.X, 0, crop.Width) + crop.X, Math.Clamp(p.Y, 0, crop.Height) + crop.Y);
    }

    private void Begin(object sender, MouseButtonEventArgs e)
    {
        Focus(); BeginInteraction(Position(e), e.ClickCount);
        if (IsInteracting) CaptureMouse();
        e.Handled = true;
    }

    public bool BeginInteraction(Point point, int clickCount = 1)
    {
        if (TryBeginSelectionInteraction(point, clickCount)) return true;
        BeginAnnotation(point); return true;
    }

    public bool TryBeginSelectionInteraction(Point point, int clickCount = 1)
    {
        if (clickCount == 2)
        {
            var textIndex = HitMark(point);
            if (textIndex >= 0 && Document.Marks[textIndex].Tool == CaptureTool.Text)
            {
                SelectMark(textIndex); TextEditRequested?.Invoke(); return true;
            }
        }
        var selectedHit = HitSelected(point);
        if (selectedHit != EditHit.None)
        {
            BeginTransform(point, selectedHit); return true;
        }
        var existingIndex = HitMark(point);
        if (existingIndex >= 0)
        {
            SelectMark(existingIndex); BeginTransform(point, EditHit.Body); return true;
        }
        if (Tool == CaptureTool.Select)
        {
            SelectMark(-1);
            return true;
        }
        return false;
    }

    public void BeginAnnotation(Point point)
    {
        CancelGesture();
        if (Tool == CaptureTool.Select) return;
        _start = _end = Clamp(point);
        if (Tool == CaptureTool.Text)
        {
            if (string.IsNullOrWhiteSpace(LabelText)) { Error?.Invoke("请先填写标注文字。"); return; }
            var before = Document.Marks.Count;
            Try(() => Document.Add(new CaptureMark(Tool, _start, _end, InkColor, StrokeWidth, LabelText, TextSize)));
            if (Document.Marks.Count > before) SelectMark(Document.Marks.Count - 1);
            return;
        }
        _points = new List<Point> { _start };
        if (Tool == CaptureTool.Pen) _inkPreview = new Stroke(new StylusPointCollection { new StylusPoint(_start.X, _start.Y) })
        { DrawingAttributes = new DrawingAttributes { Color = InkColor, Width = StrokeWidth, Height = StrokeWidth, IgnorePressure = true } };
        InvalidateVisual();
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        MoveInteraction(Position(e));
    }

    public void MoveInteraction(Point point) { if (_transforming) UpdateTransform(point); else MoveAnnotation(point); }

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
        if (!IsMouseCaptured) return;
        EndInteraction(Position(e));
        e.Handled = true;
    }
    public void EndInteraction(Point point) { if (_transforming) EndTransform(point); else EndAnnotation(point); }

    public void EndAnnotation(Point point)
    {
        if (_points is null) return;
        MoveAnnotation(point); var points = _points.ToArray(); _finishing = true;
        try
        {
            ReleaseMouseCapture(); var before = Document.Marks.Count;
            if (Tool == CaptureTool.Crop)
            {
                var crop = Document.Crop;
                var selection = CaptureRect.Select(_start, _end, new CaptureRect(crop.X, crop.Y, crop.Width, crop.Height));
                if (selection.HasArea) Try(() => Document.SetCrop(new Int32Rect(selection.X, selection.Y, selection.Width, selection.Height)));
            }
            else if (Tool == CaptureTool.Pen || (_end - _start).Length >= 1)
                Try(() => Document.Add(new CaptureMark(Tool, _start, _end, InkColor, WidthFor(Tool), points: points)));
            if (Document.Marks.Count > before) SelectMark(Document.Marks.Count - 1);
        }
        finally { _finishing = false; CancelGesture(); }
    }

    private void BeginTransform(Point point, EditHit hit)
    {
        if (SelectedMark is not { } mark) return;
        CancelGesture(); _transforming = true; _editHit = hit; _transformStart = point; _transformOriginal = _transformPreview = mark;
        InvalidateVisual();
    }

    private void UpdateTransform(Point point)
    {
        if (!_transforming || _transformOriginal is null) return;
        try { _transformPreview = Transform(_transformOriginal, point); InvalidateVisual(); }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
    }

    private void EndTransform(Point point)
    {
        UpdateTransform(point); _finishing = true;
        try
        {
            ReleaseMouseCapture();
            if (_transformPreview is not null && _selectedIndex >= 0) Document.ReplaceMark(_selectedIndex, _transformPreview);
        }
        finally { _finishing = false; _transforming = false; _transformOriginal = _transformPreview = null; InvalidateVisual(); }
    }

    private CaptureMark Transform(CaptureMark mark, Point point)
    {
        var crop = Document.Crop;
        if (_editHit == EditHit.Body)
        {
            var bounds = mark.Bounds; var offset = point - _transformStart;
            offset.X = Math.Clamp(offset.X, crop.X - bounds.Left, crop.X + crop.Width - bounds.Right);
            offset.Y = Math.Clamp(offset.Y, crop.Y - bounds.Top, crop.Y + crop.Height - bounds.Bottom);
            return mark.Move(offset);
        }
        if (_editHit is EditHit.Start or EditHit.End)
        {
            var clamped = Clamp(point);
            return _editHit == EditHit.Start ? mark.WithEndpoints(clamped, mark.End) : mark.WithEndpoints(mark.Start, clamped);
        }
        var source = mark.Bounds; var dx = point.X - _transformStart.X; var dy = point.Y - _transformStart.Y;
        var left = source.Left; var top = source.Top; var right = source.Right; var bottom = source.Bottom;
        if (_editHit is EditHit.NorthWest or EditHit.West or EditHit.SouthWest) left = Math.Clamp(left + dx, crop.X, right - 1);
        if (_editHit is EditHit.NorthEast or EditHit.East or EditHit.SouthEast) right = Math.Clamp(right + dx, left + 1, crop.X + crop.Width);
        if (_editHit is EditHit.NorthWest or EditHit.North or EditHit.NorthEast) top = Math.Clamp(top + dy, crop.Y, bottom - 1);
        if (_editHit is EditHit.SouthWest or EditHit.South or EditHit.SouthEast) bottom = Math.Clamp(bottom + dy, top + 1, crop.Y + crop.Height);
        return mark.Resize(new Rect(left, top, right - left, bottom - top));
    }

    private EditHit HitSelected(Point point)
    {
        if (SelectedMark is not { } mark) return EditHit.None;
        var tolerance = 7 / InteractionScale;
        if (mark.Tool == CaptureTool.Arrow)
        {
            if ((mark.Start - point).Length <= tolerance) return EditHit.Start;
            if ((mark.End - point).Length <= tolerance) return EditHit.End;
        }
        else foreach (var (hit, handle) in Handles(mark.Bounds)) if ((handle - point).Length <= tolerance) return hit;
        return mark.HitTest(point, tolerance) ? EditHit.Body : EditHit.None;
    }

    private int HitMark(Point point)
    {
        var tolerance = 7 / InteractionScale;
        for (var i = Document.Marks.Count - 1; i >= 0; i--) if (Document.Marks[i].HitTest(point, tolerance)) return i;
        return -1;
    }

    private static IReadOnlyList<(EditHit Hit, Point Point)> Handles(Rect bounds)
    {
        var middleX = bounds.Left + bounds.Width / 2; var middleY = bounds.Top + bounds.Height / 2;
        return new[] { (EditHit.NorthWest, bounds.TopLeft), (EditHit.North, new Point(middleX, bounds.Top)),
            (EditHit.NorthEast, bounds.TopRight), (EditHit.East, new Point(bounds.Right, middleY)),
            (EditHit.SouthEast, bounds.BottomRight), (EditHit.South, new Point(middleX, bounds.Bottom)),
            (EditHit.SouthWest, bounds.BottomLeft), (EditHit.West, new Point(bounds.Left, middleY)) };
    }

    public void ClearSelection() => SelectMark(-1);
    private void SelectMark(int index)
    {
        if (index < -1 || index >= Document.Marks.Count) index = -1;
        _selectedIndex = index;
        if (SelectedMark is { } mark)
        {
            _syncingOptions = true;
            _inkColor = mark.Color; _textSize = mark.FontSize;
            if (mark.Tool == CaptureTool.Mosaic) _mosaicBlockSize = mark.Width;
            else if (mark.Tool != CaptureTool.Redact) _strokeWidth = mark.Width;
            if (mark.Tool == CaptureTool.Text) _labelText = mark.Text;
            _syncingOptions = false;
        }
        SelectionChanged?.Invoke(); InvalidateVisual();
    }

    private void ApplySelectedStyle()
    {
        if (SelectedMark is not { } mark || _transforming) return;
        var width = WidthFor(mark.Tool);
        var fontSize = mark.Tool == CaptureTool.Text ? TextSize : mark.FontSize;
        try { Document.ReplaceMark(_selectedIndex, mark.WithStyle(InkColor, width, fontSize)); }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
    }

    private double WidthFor(CaptureTool tool) => tool switch
    {
        CaptureTool.Mosaic => MosaicBlockSize,
        CaptureTool.Redact => 1,
        _ => StrokeWidth
    };

    public void UpdateSelectedText(string text)
    {
        if (SelectedMark is not { Tool: CaptureTool.Text } mark || string.IsNullOrWhiteSpace(text)) return;
        try { _labelText = text; Document.ReplaceMark(_selectedIndex, mark.WithText(text)); }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
    }

    public void DeleteSelected()
    {
        if (_selectedIndex < 0) return;
        var index = _selectedIndex; _selectedIndex = -1;
        Try(() => Document.RemoveMark(index)); SelectionChanged?.Invoke(); InvalidateVisual();
    }
    public void NudgeSelected(Vector offset)
    {
        if (SelectedMark is not { } mark) return;
        var crop = Document.Crop; var bounds = mark.Bounds;
        offset.X = Math.Clamp(offset.X, crop.X - bounds.Left, crop.X + crop.Width - bounds.Right);
        offset.Y = Math.Clamp(offset.Y, crop.Y - bounds.Top, crop.Y + crop.Height - bounds.Bottom);
        Try(() => Document.ReplaceMark(_selectedIndex, mark.Move(offset)));
    }

    private Point Clamp(Point point)
    {
        if (!double.IsFinite(point.X + point.Y)) throw new ArgumentException("Invalid annotation point.");
        var crop = Document.Crop;
        return new(Math.Clamp(point.X, crop.X, crop.X + crop.Width), Math.Clamp(point.Y, crop.Y, crop.Y + crop.Height));
    }

    private void Try(Action action) { try { action(); } catch (Exception ex) { Error?.Invoke(ex.Message); } }
    private void OnDocumentChanged()
    {
        if (_selectedIndex >= Document.Marks.Count) _selectedIndex = -1;
        SelectionChanged?.Invoke(); InvalidateVisual();
    }

    public void CancelGesture()
    {
        _points = null; _inkPreview = null; _transforming = false; _transformOriginal = _transformPreview = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, Width, Height)));
        dc.PushTransform(new TranslateTransform(-Document.Crop.X, -Document.Crop.Y));
        dc.DrawImage(Document.Image, new Rect(0, 0, Document.Image.PixelWidth, Document.Image.PixelHeight));
        DrawMarks(dc);
        DrawPreview(dc); DrawMosaicGuides(dc); DrawSelection(dc);
        dc.Pop(); dc.Pop();
    }

    public void DrawMarks(DrawingContext dc)
    {
        for (var i = 0; i < Document.Marks.Count; i++)
            (i == _selectedIndex && _transformPreview is not null ? _transformPreview : Document.Marks[i]).Draw(dc, Document.Image);
    }

    public void DrawPreview(DrawingContext dc)
    {
        if (_points is null) return;
        if (Tool == CaptureTool.Crop) dc.DrawRectangle(null, new System.Windows.Media.Pen(Brushes.White, 1), new Rect(_start, _end));
        else if (_inkPreview is not null) _inkPreview.Draw(dc);
        else new CaptureMark(Tool, _start, _end, InkColor, WidthFor(Tool)).Draw(dc, Document.Image);
        if (Tool == CaptureTool.Mosaic) DrawMosaicGuide(dc, new Rect(_start, _end));
    }

    private void DrawMosaicGuides(DrawingContext dc)
    {
        for (var i = 0; i < Document.Marks.Count; i++)
        {
            var mark = i == _selectedIndex && _transformPreview is not null ? _transformPreview : Document.Marks[i];
            if (mark.Tool == CaptureTool.Mosaic) DrawMosaicGuide(dc, mark.Bounds);
        }
    }

    public void DrawSelection(DrawingContext dc)
    {
        var mark = _transformPreview ?? SelectedMark;
        if (mark is null) return;
        var scale = InteractionScale; var radius = 4 / scale;
        var halo = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), 3 / scale);
        var outline = new System.Windows.Media.Pen(new SolidColorBrush(mark.Color), 1.5 / scale) { DashStyle = DashStyles.Dash };
        var handleBorder = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromRgb(125, 128, 132)), 1 / scale);
        if (mark.Tool == CaptureTool.Arrow)
        {
            foreach (var point in new[] { mark.Start, mark.End }) dc.DrawEllipse(Brushes.White, handleBorder, point, radius, radius);
            return;
        }
        dc.DrawRectangle(null, halo, mark.Bounds); dc.DrawRectangle(null, outline, mark.Bounds);
        foreach (var (_, point) in Handles(mark.Bounds)) dc.DrawEllipse(Brushes.White, handleBorder, point, radius, radius);
    }

    private static void DrawMosaicGuide(DrawingContext dc, Rect rect)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        var colors = UiTheme.GetColors(); var accent = colors["AccentBrush"];
        var halo = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)), 4);
        var line = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromArgb(245, accent.R, accent.G, accent.B)), 1.5) { DashStyle = DashStyles.Dash };
        halo.Freeze(); line.Freeze(); dc.DrawRectangle(null, halo, rect); dc.DrawRectangle(null, line, rect);
    }
}
