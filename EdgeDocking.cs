using System;
using System.Windows;

namespace YeShunguangPet;

public enum DockEdge { None, Left, Right, Top, Bottom }

public sealed record DockLayout(DockEdge Edge, Rect WorkArea, Rect Expanded, Rect Handle)
{
    public Rect ReleaseBounds { get; init; } = Expanded;

    public Rect RetractionViewport
    {
        get
        {
            var viewport = Rect.Union(Expanded, ReleaseBounds);
            viewport.Intersect(WorkArea);
            return viewport;
        }
    }

    public Vector RetractionOffset(double progress)
    {
        var start = ReleaseBounds.TopLeft;
        var end = Expanded.TopLeft + Offset(1);
        return start + (end - start) * Math.Clamp(progress, 0, 1) - RetractionViewport.TopLeft;
    }

    public Vector Offset(double progress) => Edge switch
    {
        DockEdge.Left => new Vector(-Expanded.Width * progress, 0),
        DockEdge.Right => new Vector(Expanded.Width * progress, 0),
        DockEdge.Top => new Vector(0, -Expanded.Height * progress),
        DockEdge.Bottom => new Vector(0, Expanded.Height * progress),
        _ => default
    };
}

public static class EdgeDocking
{
    public const double SnapDistance = 24;
    public const double HandleThickness = 12;
    public const double HandleLength = 56;

    public static DockEdge Detect(Rect pet, Rect area)
    {
        if (!Fits(pet, area)) return DockEdge.None;
        var edges = new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom };
        var distances = new[] { Math.Max(0, pet.Left - area.Left), Math.Max(0, area.Right - pet.Right),
            Math.Max(0, pet.Top - area.Top), Math.Max(0, area.Bottom - pet.Bottom) };
        var result = DockEdge.None;
        var nearest = SnapDistance + 0.001;
        for (var i = 0; i < edges.Length; i++)
            if (distances[i] < nearest)
            {
                nearest = distances[i];
                result = edges[i];
            }
        return result;
    }

    public static DockLayout? Create(DockEdge edge, Rect pet, Rect area)
    {
        if (edge == DockEdge.None || !Enum.IsDefined(edge) || !Fits(pet, area)) return null;
        var x = Math.Clamp(pet.Left, area.Left, area.Right - pet.Width);
        var y = Math.Clamp(pet.Top, area.Top, area.Bottom - pet.Height);
        if (edge == DockEdge.Left) x = area.Left;
        if (edge == DockEdge.Right) x = area.Right - pet.Width;
        if (edge == DockEdge.Top) y = area.Top;
        if (edge == DockEdge.Bottom) y = area.Bottom - pet.Height;
        var expanded = new Rect(x, y, pet.Width, pet.Height);
        var vertical = edge is DockEdge.Left or DockEdge.Right;
        var width = Math.Min(pet.Width, vertical ? HandleThickness : HandleLength);
        var height = Math.Min(pet.Height, vertical ? HandleLength : HandleThickness);
        var handleX = x + (pet.Width - width) / 2;
        var handleY = y + (pet.Height - height) / 2;
        if (edge == DockEdge.Left) handleX = area.Left;
        if (edge == DockEdge.Right) handleX = area.Right - width;
        if (edge == DockEdge.Top) handleY = area.Top;
        if (edge == DockEdge.Bottom) handleY = area.Bottom - height;
        return new DockLayout(edge, area, expanded, new Rect(handleX, handleY, width, height)) { ReleaseBounds = pet };
    }

    private static bool Fits(Rect pet, Rect area)
        => !pet.IsEmpty && !area.IsEmpty && pet.Width > 0 && pet.Height > 0 &&
           pet.Width <= area.Width && pet.Height <= area.Height;
}

public sealed class DockTransition
{
    private readonly TimeProvider _clock;
    private long _startedAt;
    private long? _leftAt;
    private double _from;
    private double _target;
    private bool _hoverArmed;
    public double Progress { get; private set; }
    public bool IsCollapsed => Progress == 1;
    public bool IsExpanded => Progress == 0;
    public bool TargetCollapsed => _target == 1;
    public bool IsAnimating => Progress != _target;

    public DockTransition(bool collapse, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        if (collapse) Request(1);
    }

    public void Expand(bool immediately = false)
    {
        _leftAt = null;
        Request(0);
        if (immediately) Progress = _from = 0;
    }

    public void Tick(bool overHandle, bool overPet, bool busy)
    {
        if (IsAnimating)
        {
            var t = Math.Clamp(_clock.GetElapsedTime(_startedAt).TotalMilliseconds / 180, 0, 1);
            Progress = t >= 1 ? _target : _from + (_target - _from) * t * t * (3 - 2 * t);
        }
        if (busy)
        {
            Expand();
        }
        else if (IsCollapsed && TargetCollapsed)
        {
            // A handle appearing under the release cursor is not a new hover.
            if (!overHandle) _hoverArmed = true;
            else if (_hoverArmed) Expand();
        }
        else if (IsExpanded && !TargetCollapsed)
        {
            if (overPet) _leftAt = null;
            else
            {
                _leftAt ??= _clock.GetTimestamp();
                if (_clock.GetElapsedTime(_leftAt.Value) >= TimeSpan.FromSeconds(1)) Request(1);
            }
        }
    }

    private void Request(double target)
    {
        if (_target == target) return;
        _from = Progress;
        _target = target;
        _startedAt = _clock.GetTimestamp();
        _leftAt = null;
        _hoverArmed = false;
    }
}
