using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using Drawing = System.Drawing;

namespace YeShunguangPet;

public static class CompletionBadge
{
    public static ImageSource CreateOverlay()
    {
        var visual = new DrawingGroup();
        using (var dc = visual.Open())
        {
            dc.DrawEllipse(Brushes.Black, new System.Windows.Media.Pen(Brushes.White, 1), new Point(8, 8), 7.5, 7.5);
            dc.DrawText(new FormattedText("1", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 12, Brushes.White, 1), new Point(4.4, -0.5));
        }
        var image = new DrawingImage(visual); image.Freeze(); return image;
    }
    public static void Apply(Window window, bool pending)
    {
        if (window.TaskbarItemInfo is null) { if (!pending) return; window.TaskbarItemInfo = new TaskbarItemInfo(); }
        if (pending == (window.TaskbarItemInfo.Overlay is not null)) return;
        window.TaskbarItemInfo.Overlay = pending ? CreateOverlay() : null;
    }
    public static Drawing.Icon CreateTrayIcon(Drawing.Icon original)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.DrawIcon(original, new Drawing.Rectangle(0, 0, 32, 32));
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.FillEllipse(Drawing.Brushes.Black, new Drawing.Rectangle(15, 15, 16, 16));
            graphics.DrawEllipse(Drawing.Pens.White, new Drawing.Rectangle(15, 15, 16, 16));
            using var font = new Drawing.Font("Segoe UI", 13, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            graphics.DrawString("1", font, Drawing.Brushes.White, 18, 14);
        }
        var handle = bitmap.GetHicon();
        try { using var icon = Drawing.Icon.FromHandle(handle); return (Drawing.Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr handle);
}
