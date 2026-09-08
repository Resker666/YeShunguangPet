using System;

namespace YeShunguangPet;

public sealed record FocusWindowOptions
{
    public const double MinimumWidth = 320;
    public const double MinimumHeight = 460;
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 560;
    // Native screen coordinates avoid treating mixed-DPI monitor origins as WPF units.
    public int? LeftPixels { get; set; }
    public int? TopPixels { get; set; }

    public FocusWindowOptions Copy() => this with { };
    public void Normalize()
    {
        Width = double.IsFinite(Width) ? Math.Round(Math.Clamp(Width, MinimumWidth, 4096), 1) : 400;
        Height = double.IsFinite(Height) ? Math.Round(Math.Clamp(Height, MinimumHeight, 4096), 1) : 560;
        if (LeftPixels is null || TopPixels is null || LeftPixels is < -1000000 or > 1000000 || TopPixels is < -1000000 or > 1000000)
            LeftPixels = TopPixels = null;
    }
}
