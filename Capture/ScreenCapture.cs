using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace YeShunguangPet;

public readonly record struct CaptureRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public bool HasArea => Width > 0 && Height > 0;
    public Rect ToRect() => new(X, Y, Math.Max(0, Width), Math.Max(0, Height));
    public static CaptureRect Select(Point first, Point last, CaptureRect bounds)
    {
        if (!double.IsFinite(first.X + first.Y + last.X + last.Y)) throw new ArgumentException("Invalid capture coordinates.");
        var x = (int)Math.Floor(Math.Clamp(Math.Min(first.X, last.X), bounds.X, bounds.Right));
        var y = (int)Math.Floor(Math.Clamp(Math.Min(first.Y, last.Y), bounds.Y, bounds.Bottom));
        var right = (int)Math.Ceiling(Math.Clamp(Math.Max(first.X, last.X), bounds.X, bounds.Right));
        var bottom = (int)Math.Ceiling(Math.Clamp(Math.Max(first.Y, last.Y), bounds.Y, bounds.Bottom));
        return new(x, y, right - x, bottom - y);
    }
}

public sealed record CaptureFrame(BitmapSource Image, CaptureRect Bounds, IReadOnlyList<CaptureRect> Monitors)
{
    public BitmapSource Crop(CaptureRect region) => CaptureRaster.Crop(Image, new Int32Rect(region.X - Bounds.X, region.Y - Bounds.Y, region.Width, region.Height));
}

public static class CaptureRaster
{
    public const long MaximumPixels = 32_000_000;
    public static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 32768 || height > 32768 || (long)width * height > MaximumPixels)
            throw new InvalidOperationException("截图面积超过限制（3200 万像素），请减少显示器数量或分辨率后重试。");
    }
    public static BitmapSource Crop(BitmapSource source, Int32Rect area)
    {
        ValidateSize(area.Width, area.Height);
        if (area.X < 0 || area.Y < 0 || (long)area.X + area.Width > source.PixelWidth || (long)area.Y + area.Height > source.PixelHeight)
            throw new ArgumentOutOfRangeException(nameof(area));
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var stride = checked(area.Width * 4); var pixels = new byte[checked(stride * area.Height)];
        converted.CopyPixels(area, pixels, stride, 0);
        var result = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze(); return result;
    }
    public static void SavePng(BitmapSource image, string path)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请选择 PNG 文件名。");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(output); output.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class ScreenCapture
{
    public static CaptureFrame ReadDesktop()
    {
        var screens = System.Windows.Forms.Screen.AllScreens.Select(s => new CaptureRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToArray();
        if (screens.Length == 0) throw new InvalidOperationException("没有可用的显示器。");
        var left = screens.Min(s => s.X); var top = screens.Min(s => s.Y);
        var bounds = new CaptureRect(left, top, screens.Max(s => s.Right) - left, screens.Max(s => s.Bottom) - top);
        return new(ReadRegion(bounds), bounds, screens);
    }
    public static BitmapSource ReadRegion(CaptureRect region)
    {
        CaptureRaster.ValidateSize(region.Width, region.Height);
        using var bitmap = new Drawing.Bitmap(region.Width, region.Height, Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(Drawing.Color.Black);
            graphics.CopyFromScreen(region.X, region.Y, 0, 0, bitmap.Size, Drawing.CopyPixelOperation.SourceCopy);
        }
        var locked = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var stride = checked(region.Width * 4); var bytes = new byte[checked(stride * region.Height)];
            for (var y = 0; y < region.Height; y++) Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), bytes, y * stride, stride);
            var image = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgr32, null, bytes, stride);
            image.Freeze(); return image;
        }
        finally { bitmap.UnlockBits(locked); }
    }
    internal static Point CursorPosition()
    {
        if (!GetCursorPos(out var point)) throw new InvalidOperationException("无法读取选区位置。");
        return new Point(point.X, point.Y);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
}
