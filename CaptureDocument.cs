using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

public enum CaptureTool { Crop, Rectangle, Arrow, Pen, Text, Redact, Ellipse, Mosaic }

public sealed class CaptureMark
{
    public CaptureTool Tool { get; }
    public Point Start { get; }
    public Point End { get; }
    public Color Color { get; }
    public double Width { get; }
    public double FontSize { get; }
    public string Text { get; }
    public int PointCount { get; }
    private readonly Stroke? _stroke;
    private readonly Brush _brush;
    private readonly System.Windows.Media.Pen _pen;
    private readonly FormattedText? _text;
    public CaptureMark(CaptureTool tool, Point start, Point end, Color color, double width, string text = "", double fontSize = 22, IEnumerable<Point>? points = null)
    {
        var maxWidth = tool == CaptureTool.Mosaic ? 32 : 16;
        if (!Enum.IsDefined(tool) || tool == CaptureTool.Crop || !double.IsFinite(start.X + start.Y + end.X + end.Y) ||
            !double.IsFinite(width) || width is < 1 || width > maxWidth || !double.IsFinite(fontSize) || fontSize is < 12 or > 64 || text.Length > 500)
            throw new ArgumentException("标注参数无效。");
        Tool = tool; Start = start; End = end; Color = Color.FromRgb(color.R, color.G, color.B); Width = width; Text = text; FontSize = fontSize;
        _brush = new SolidColorBrush(Color); _brush.Freeze();
        _pen = new System.Windows.Media.Pen(_brush, Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; _pen.Freeze();
        if (tool == CaptureTool.Text)
            _text = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Microsoft YaHei UI"), FontSize, _brush, 1) { MaxTextWidth = 1600 };
        if (tool == CaptureTool.Pen)
        {
            var samples = (points ?? new[] { start, end }).Take(10001).ToArray();
            if (samples.Length == 0 || samples.Length > 10000 || samples.Any(p => !double.IsFinite(p.X + p.Y))) throw new ArgumentException("画笔轨迹过长。");
            PointCount = samples.Length;
            _stroke = new Stroke(new StylusPointCollection(samples.Select(p => new StylusPoint(p.X, p.Y))))
            { DrawingAttributes = new DrawingAttributes { Color = Color, Width = width, Height = width, IgnorePressure = true, FitToCurve = false } };
        }
    }
    public void Draw(DrawingContext context, BitmapSource? source = null)
    {
        var pen = _pen;
        var rectangle = new Rect(Start, End);
        switch (Tool)
        {
            case CaptureTool.Rectangle: context.DrawRectangle(null, pen, rectangle); break;
            case CaptureTool.Ellipse: context.DrawEllipse(null, pen, new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2), rectangle.Width / 2, rectangle.Height / 2); break;
            case CaptureTool.Redact:
                var left = Math.Floor(rectangle.Left); var top = Math.Floor(rectangle.Top);
                context.DrawRectangle(Brushes.Black, null, new Rect(left, top, Math.Ceiling(rectangle.Right) - left, Math.Ceiling(rectangle.Bottom) - top)); break;
            case CaptureTool.Mosaic:
                DrawMosaic(context, source, rectangle); break;
            case CaptureTool.Pen: _stroke!.Draw(context); break;
            case CaptureTool.Text:
                context.DrawText(_text!, Start); break;
            case CaptureTool.Arrow:
                var direction = End - Start;
                if (direction.Length < 1) break;
                direction.Normalize(); var normal = new Vector(-direction.Y, direction.X); var length = Math.Max(10, Width * 3);
                context.DrawLine(pen, Start, End);
                context.DrawLine(pen, End, End - direction * length + normal * length * 0.5);
                context.DrawLine(pen, End, End - direction * length - normal * length * 0.5); break;
        }
    }
    private void DrawMosaic(DrawingContext context, BitmapSource? source, Rect rectangle)
    {
        if (source is null)
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 120, 120, 120)), null, rectangle);
            return;
        }
        var left = Math.Clamp((int)Math.Floor(rectangle.Left), 0, source.PixelWidth - 1);
        var top = Math.Clamp((int)Math.Floor(rectangle.Top), 0, source.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(rectangle.Right), left + 1, source.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(rectangle.Bottom), top + 1, source.PixelHeight);
        var width = right - left; var height = bottom - top;
        var block = Math.Clamp((int)Math.Round(Width), 2, 32);
        var smallWidth = Math.Max(1, (int)Math.Ceiling(width / (double)block));
        var smallHeight = Math.Max(1, (int)Math.Ceiling(height / (double)block));
        var crop = new CroppedBitmap(source, new Int32Rect(left, top, width, height));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen()) drawing.DrawImage(crop, new Rect(0, 0, smallWidth, smallHeight));
        var mosaic = new RenderTargetBitmap(smallWidth, smallHeight, 96, 96, PixelFormats.Pbgra32);
        mosaic.Render(visual); mosaic.Freeze();
        var brush = new ImageBrush(mosaic) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        context.DrawRectangle(brush, null, new Rect(left, top, width, height));
    }
}

public sealed class CaptureDocument
{
    private sealed record State(Int32Rect Crop, CaptureMark[] Marks);
    private readonly List<State> _history = new();
    private int _index;
    public BitmapSource Image { get; }
    public Int32Rect Crop => _history[_index].Crop;
    public IReadOnlyList<CaptureMark> Marks => Array.AsReadOnly(_history[_index].Marks);
    public bool CanUndo => _index > 0;
    public bool CanRedo => _index < _history.Count - 1;
    public event Action? Changed;
    public CaptureDocument(BitmapSource image)
    {
        CaptureRaster.ValidateSize(image.PixelWidth, image.PixelHeight);
        Image = image.IsFrozen ? image : CaptureRaster.Crop(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight));
        _history.Add(new(new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight), Array.Empty<CaptureMark>()));
    }
    public void Add(CaptureMark mark)
    {
        if (Marks.Count >= 300 || Marks.Sum(m => m.PointCount) + mark.PointCount > 100000) throw new InvalidOperationException("标注数量已达上限，请保存当前截图。");
        Push(new(Crop, _history[_index].Marks.Append(mark).ToArray()));
    }
    public void SetCrop(Int32Rect crop)
    {
        if (crop.Width < 1 || crop.Height < 1 || crop.X < Crop.X || crop.Y < Crop.Y || (long)crop.X + crop.Width > (long)Crop.X + Crop.Width || (long)crop.Y + crop.Height > (long)Crop.Y + Crop.Height)
            throw new ArgumentOutOfRangeException(nameof(crop));
        if (crop != Crop) Push(new(crop, _history[_index].Marks));
    }
    public void SetSelection(Int32Rect crop, bool initial = false)
    {
        if (crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1 || (long)crop.X + crop.Width > Image.PixelWidth || (long)crop.Y + crop.Height > Image.PixelHeight)
            throw new ArgumentOutOfRangeException(nameof(crop));
        if (initial) { _history.Clear(); _history.Add(new(crop, Array.Empty<CaptureMark>())); _index = 0; Changed?.Invoke(); }
        else if (crop != Crop) Push(new(crop, _history[_index].Marks));
    }
    private void Push(State state)
    {
        _history.RemoveRange(_index + 1, _history.Count - _index - 1);
        _history.Add(state);
        if (_history.Count > 101) _history.RemoveAt(0);
        _index = _history.Count - 1; Changed?.Invoke();
    }
    public void Undo() { if (CanUndo) { _index--; Changed?.Invoke(); } }
    public void Redo() { if (CanRedo) { _index++; Changed?.Invoke(); } }
    public void Draw(DrawingContext context)
    {
        context.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
        DrawMarks(context);
    }
    public void DrawMarks(DrawingContext context)
    {
        foreach (var mark in _history[_index].Marks) mark.Draw(context, Image);
    }
    public BitmapSource Flatten()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, Crop.Width, Crop.Height)));
            dc.PushTransform(new TranslateTransform(-Crop.X, -Crop.Y)); Draw(dc); dc.Pop(); dc.Pop();
        }
        var output = new RenderTargetBitmap(Crop.Width, Crop.Height, 96, 96, PixelFormats.Pbgra32);
        output.Render(visual); output.Freeze(); return output;
    }
}
