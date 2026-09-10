using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class FocusVisualGate
{
    public static IReadOnlyList<object> Run(string source, string output, bool record = false)
    {
        Directory.CreateDirectory(output);
        var baselines = Path.Combine(source, "tests", "baselines", "focus");
        if (record) Directory.CreateDirectory(baselines);
        var results = new List<object>();
        var package = PetPackage.Load(Path.Combine(source, "Pets", "YeShunguang", "pet.json"));
        foreach (var theme in new[] { "light", "dark" })
            foreach (var mode in new[] { "full", "compact", "mini" })
            {
                UiTheme.Apply(new AppearanceOptions { Theme = theme, Accent = "#24796E", ReduceMotion = true });
                using var runtime = new CompanionRuntime(new PetSettings());
                runtime.Configure();
                var window = new FocusWindow(runtime.Session, runtime.Settings, package, () => false, runtime);
                try
                {
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = window.Top = -30000;
                    window.ShowActivated = window.ShowInTaskbar = false;
                    if (mode == "compact") { window.Width = 320; window.Height = 460; }
                    window.Show(); Pump(100);
                    if (mode == "mini")
                    {
                        ((Button)window.FindName("MiniModeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        Pump(100);
                    }
                    var actual = Capture(window, mode);
                    var name = $"{mode}-{theme}.png";
                    Save(actual, Path.Combine(output, name));
                    var blank = new RenderTargetBitmap(actual.PixelWidth, actual.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                    var fill = new DrawingVisual();
                    using (var dc = fill.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, blank.PixelWidth, blank.PixelHeight));
                    blank.Render(fill);
                    var blankDiff = Compare(actual, blank);
                    if (Accepts(blankDiff))
                        throw new InvalidOperationException($"Visual gate could accept blank content: {name}, diff={blankDiff}. Review the comparison sensitivity.");
                    var baseline = Path.Combine(baselines, name);
                    if (record) Save(actual, baseline);
                    else
                    {
                        using var input = File.OpenRead(baseline);
                        var expected = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                        var diff = Compare(expected, actual);
                        results.Add(new { name, diff.Mean, diff.ChangedBlocks, diff.WorstRegion });
                        if (!Accepts(diff))
                            throw new InvalidOperationException($"Visual gate failed: {name}, mean={diff.Mean:F5}, changed={diff.ChangedBlocks:P2}, worst region={diff.WorstRegion:F5}. Inspect {output}; do not regenerate baselines automatically.");
                    }
                }
                finally { window.Close(); Pump(20); }
            }
        return results;
    }

    internal static bool Accepts((double Mean, double ChangedBlocks, double WorstRegion) diff) =>
        diff.Mean <= 0.035 && diff.ChangedBlocks <= 0.08 && diff.WorstRegion <= 0.10;

    internal static (double Mean, double ChangedBlocks, double WorstRegion) Compare(BitmapSource expected, BitmapSource actual)
    {
        if (expected.PixelWidth != actual.PixelWidth || expected.PixelHeight != actual.PixelHeight)
            throw new InvalidOperationException($"Visual dimensions differ: {expected.PixelWidth}x{expected.PixelHeight} vs {actual.PixelWidth}x{actual.PixelHeight}.");
        byte[] Pixels(BitmapSource image)
        {
            var formatted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var bytes = new byte[formatted.PixelWidth * formatted.PixelHeight * 4];
            formatted.CopyPixels(bytes, formatted.PixelWidth * 4, 0);
            return bytes;
        }
        var a = Pixels(expected); var b = Pixels(actual);
        double total = 0; var blocks = 0; var changed = 0;
        var regionColumns = (actual.PixelWidth + 31) / 32;
        var regions = new double[regionColumns * ((actual.PixelHeight + 31) / 32)];
        var regionBlocks = new int[regions.Length];
        for (var y = 0; y < actual.PixelHeight; y += 8)
            for (var x = 0; x < actual.PixelWidth; x += 8)
            {
                double error = 0; var samples = 0;
                for (var yy = y; yy < Math.Min(y + 8, actual.PixelHeight); yy++)
                    for (var xx = x; xx < Math.Min(x + 8, actual.PixelWidth); xx++)
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var i = (yy * actual.PixelWidth + xx) * 4 + channel;
                            error += Math.Abs(a[i] - b[i]); samples++;
                        }
                error /= samples * 255.0;
                total += error; blocks++;
                var region = (y / 32) * regionColumns + x / 32;
                regions[region] += error; regionBlocks[region]++;
                if (error > 0.16) changed++;
            }
        // A missing control must not be diluted by a mostly empty surrounding window.
        var worstRegion = regions.Select((sum, i) => sum / regionBlocks[i]).Max();
        return (total / blocks, (double)changed / blocks, worstRegion);
    }

    private static BitmapSource Capture(Window window, string mode)
    {
        var root = (FrameworkElement)window.Content;
        // Compare client content at fixed 96-DPI dimensions, independent of native titlebar metrics.
        root.Width = mode == "full" ? 384 : mode == "compact" ? 304 : 320;
        root.Height = mode == "full" ? 521 : mode == "compact" ? 421 : 104;
        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var fill = new DrawingVisual();
        using (var dc = fill.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(fill); bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }
    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    internal static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
}
