using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

public sealed class SpriteGridView : FrameworkElement
{
    private const double Gutter = 24;
    public BitmapSource? Sheet { get; set; }
    public int CellWidth { get; set; }
    public int CellHeight { get; set; }
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int SelectedRow { get; set; }
    public int SelectedColumn { get; set; }
    public int SelectedCount { get; set; }
    public double Zoom { get; private set; } = 0.25;
    public event Action<int, int>? CellSelected;

    public SpriteGridView()
    {
        Cursor = Cursors.Cross;
        ToolTip = "选择动作起始格，行列从 0 开始";
    }

    public void Refresh(double zoom)
    {
        Zoom = Math.Clamp(zoom, 0.05, 2);
        Width = (Sheet?.PixelWidth ?? 1) * Zoom + Gutter;
        Height = (Sheet?.PixelHeight ?? 1) * Zoom + Gutter;
        InvalidateVisual();
    }

    public FrameLocation? HitCell(Point position)
    {
        if (CellWidth <= 0 || CellHeight <= 0 || position.X < Gutter || position.Y < Gutter) return null;
        var column = (int)((position.X - Gutter) / (CellWidth * Zoom));
        var row = (int)((position.Y - Gutter) / (CellHeight * Zoom));
        return column < Columns && row < Rows ? new FrameLocation(row, column) : null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (HitCell(e.GetPosition(this)) is { } cell) { CellSelected?.Invoke(cell.Row, cell.Column); e.Handled = true; }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Sheet is null) return;
        dc.DrawRectangle(ThemeBrush("SubtleBrush", Brushes.WhiteSmoke), null, new Rect(RenderSize));
        dc.DrawImage(Sheet, new Rect(Gutter, Gutter, Sheet.PixelWidth * Zoom, Sheet.PixelHeight * Zoom));
        if (CellWidth <= 0 || CellHeight <= 0 || Columns is < 1 or > 64 || Rows is < 1 or > 64) return;
        var pen = new Pen(ThemeBrush("BorderBrush", Brushes.LightGray), 1);
        for (var c = 0; c <= Columns; c++)
        {
            var x = Gutter + c * CellWidth * Zoom;
            dc.DrawLine(pen, new Point(x, Gutter), new Point(x, Gutter + Rows * CellHeight * Zoom));
            if (c < Columns) DrawLabel(dc, c, new Point(x + 3, 3));
        }
        for (var r = 0; r <= Rows; r++)
        {
            var y = Gutter + r * CellHeight * Zoom;
            dc.DrawLine(pen, new Point(Gutter, y), new Point(Gutter + Columns * CellWidth * Zoom, y));
            if (r < Rows) DrawLabel(dc, r, new Point(3, y + 3));
        }
        if (SelectedCount > 0 && SelectedRow >= 0 && SelectedRow < Rows && SelectedColumn >= 0 && SelectedColumn < Columns)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 20, 140, 135)), new Pen(ThemeBrush("AccentTextBrush", Brushes.Teal), 2),
                new Rect(Gutter + SelectedColumn * CellWidth * Zoom, Gutter + SelectedRow * CellHeight * Zoom,
                    Math.Min(SelectedCount, Columns - SelectedColumn) * CellWidth * Zoom, CellHeight * Zoom));
    }

    private void DrawLabel(DrawingContext dc, int value, Point position)
    {
        dc.DrawText(new FormattedText(value.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, ThemeBrush("SecondaryTextBrush", Brushes.DimGray), VisualTreeHelper.GetDpi(this).PixelsPerDip), position);
    }

    private Brush ThemeBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
