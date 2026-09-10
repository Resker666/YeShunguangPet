using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace YeShunguangPet;

public enum CaptureMode { Region, CurrentScreen, AllScreens }
public enum CaptureHit { Outside, Move, NorthWest, North, NorthEast, East, SouthEast, South, SouthWest, West }

public sealed class CaptureRegion
{
    public CaptureRect Bounds { get; }
    public CaptureRect Selection { get; private set; }
    public bool IsDragging { get; private set; }
    private CaptureRect _before;
    private Point _start;
    private CaptureHit _hit;
    public CaptureRegion(CaptureRect bounds) => Bounds = bounds;
    public void Set(CaptureRect selection)
    {
        if (!selection.HasArea || selection.X < Bounds.X || selection.Y < Bounds.Y || selection.Right > Bounds.Right || selection.Bottom > Bounds.Bottom)
            throw new ArgumentOutOfRangeException(nameof(selection));
        Selection = selection;
    }
    public IReadOnlyList<(CaptureHit Hit, Point Point)> Handles()
    {
        var r = Selection; var midX = r.X + r.Width / 2.0; var midY = r.Y + r.Height / 2.0;
        return new[] { (CaptureHit.NorthWest, new Point(r.X, r.Y)), (CaptureHit.North, new Point(midX, r.Y)),
            (CaptureHit.NorthEast, new Point(r.Right, r.Y)), (CaptureHit.East, new Point(r.Right, midY)),
            (CaptureHit.SouthEast, new Point(r.Right, r.Bottom)), (CaptureHit.South, new Point(midX, r.Bottom)),
            (CaptureHit.SouthWest, new Point(r.X, r.Bottom)), (CaptureHit.West, new Point(r.X, midY)) };
    }
    public CaptureHit HitTest(Point point, double tolerance)
    {
        if (!Selection.HasArea) return CaptureHit.Outside;
        var nearest = Handles().OrderBy(h => (h.Point - point).LengthSquared).First();
        if (Math.Abs(nearest.Point.X - point.X) <= tolerance && Math.Abs(nearest.Point.Y - point.Y) <= tolerance) return nearest.Hit;
        if (point.Y >= Selection.Y && point.Y <= Selection.Bottom)
        {
            if (Math.Abs(point.X - Selection.X) <= tolerance) return CaptureHit.West;
            if (Math.Abs(point.X - Selection.Right) <= tolerance) return CaptureHit.East;
        }
        if (point.X >= Selection.X && point.X <= Selection.Right)
        {
            if (Math.Abs(point.Y - Selection.Y) <= tolerance) return CaptureHit.North;
            if (Math.Abs(point.Y - Selection.Bottom) <= tolerance) return CaptureHit.South;
        }
        return Selection.ToRect().Contains(point) ? CaptureHit.Move : CaptureHit.Outside;
    }
    public void Begin(Point point, CaptureHit hit)
    {
        Validate(point); _start = point; _before = Selection; _hit = hit; IsDragging = true;
        Update(point);
    }
    public void Update(Point point)
    {
        if (!IsDragging) return; Validate(point);
        if (_hit == CaptureHit.Outside) { Selection = CaptureRect.Select(_start, point, Bounds); return; }
        var dx = (int)Math.Round(point.X - _start.X); var dy = (int)Math.Round(point.Y - _start.Y);
        if (_hit == CaptureHit.Move)
        {
            Selection = _before with { X = Math.Clamp(_before.X + dx, Bounds.X, Bounds.Right - _before.Width), Y = Math.Clamp(_before.Y + dy, Bounds.Y, Bounds.Bottom - _before.Height) }; return;
        }
        var left = _before.X; var top = _before.Y; var right = _before.Right; var bottom = _before.Bottom;
        if (_hit is CaptureHit.NorthWest or CaptureHit.West or CaptureHit.SouthWest) left = Math.Clamp(left + dx, Bounds.X, right - 1);
        if (_hit is CaptureHit.NorthEast or CaptureHit.East or CaptureHit.SouthEast) right = Math.Clamp(right + dx, left + 1, Bounds.Right);
        if (_hit is CaptureHit.NorthWest or CaptureHit.North or CaptureHit.NorthEast) top = Math.Clamp(top + dy, Bounds.Y, bottom - 1);
        if (_hit is CaptureHit.SouthWest or CaptureHit.South or CaptureHit.SouthEast) bottom = Math.Clamp(bottom + dy, top + 1, Bounds.Bottom);
        Selection = new(left, top, right - left, bottom - top);
    }
    public void End(Point point) { Update(point); IsDragging = false; if (!Selection.HasArea) Selection = _before; }
    public void CancelDrag() { if (!IsDragging) return; Selection = _before; IsDragging = false; }
    private static void Validate(Point point) { if (!double.IsFinite(point.X + point.Y)) throw new ArgumentException("Invalid selection point."); }
    public static Rect PlaceToolbar(CaptureRect selection, CaptureRect monitor, Size size, double gap = 8)
    {
        var width = Math.Min(size.Width, Math.Max(1, monitor.Width - gap * 2));
        var height = Math.Min(size.Height, Math.Max(1, monitor.Height - gap * 2));
        var minX = monitor.X + gap; var minY = monitor.Y + gap;
        var maxX = Math.Max(minX, monitor.Right - gap - width); var maxY = Math.Max(minY, monitor.Bottom - gap - height);
        var x = Math.Clamp(selection.Right - width, minX, maxX);
        var y = selection.Bottom + gap;
        if (y > maxY) y = selection.Y - gap - height;
        if (y < minY || y > maxY) y = selection.Bottom - height - gap;
        return new Rect(x, Math.Clamp(y, minY, maxY), width, height);
    }
}
