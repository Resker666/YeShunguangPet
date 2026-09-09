using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class PromotionCapture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static int Run(string source, string output)
    {
        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.Combine(source, "artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Capture output must be under the project's artifacts directory.");
        Directory.CreateDirectory(output);
        typeof(AppLogger).GetMethod("SetSink", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            new object[] { new ResilientLog(new[] { Path.Combine(output, "logs") }) });
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var clock = new DemoClock();
        var config = DesktopConfiguration.Migrate(new PetSettings { FocusMinutes = 25, BreakMinutes = 5, NotificationsEnabled = false });
        config.Appearance = new AppearanceOptions { Theme = "light", Accent = "#24796E", ReduceMotion = true };
        var catalog = new PetCatalog(Path.Combine(source, "Pets"), Path.Combine(output, "empty-users"));
        using var desktop = new DesktopSession(config, catalog, _ => { }, false, clock);
        desktop.Start(false);
        var pet = desktop.Windows.First().Package;
        var robin = catalog.LoadPreferred("robin", out _);
        ExportSprites(pet, output);
        ExportSprites(robin, output);
        var focus = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, pet, () => false, desktop.Companion);
        var skins = new SettingsWindow(new PetSettings(), false, catalog, pet);
        try
        {
            ShowOffscreen(focus);
            Capture(focus, Path.Combine(output, "focus-ready.png"));
            ((Button)focus.FindName("ToggleButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Wait(60);
            Capture(focus, Path.Combine(output, "focus-running.png"));
            typeof(FocusWindow).GetMethod("SetMiniMode", Private)!.Invoke(focus, new object[] { true });
            Wait(100);
            for (var second = 0; second < 9; second++)
            {
                if (second > 0) clock.Advance(TimeSpan.FromSeconds(1));
                desktop.Companion.Tick();
                typeof(FocusWindow).GetMethod("RefreshDisplay", Private)!.Invoke(focus, null);
                Wait(20);
                Capture(focus, Path.Combine(output, $"mini-{second:00}.png"));
            }
            focus.UpdatePet(robin); Wait(60);
            Capture(focus, Path.Combine(output, "mini-robin.png"));
            ShowOffscreen(skins);
            Capture(skins, Path.Combine(output, "skin-leaf.png"));
            var selector = (ListBox)skins.FindName("PetSelector");
            selector.SelectedItem = selector.Items.Cast<PetEntry>().Single(p => p.Id == "robin");
            Wait(120);
            Capture(skins, Path.Combine(output, "skin-robin.png"));
            File.WriteAllText(Path.Combine(output, "capture.json"), JsonSerializer.Serialize(new
            {
                Version = typeof(PetPackage).Assembly.GetName().Version?.ToString(3),
                Source = "Native WPF controls rendered offscreen with isolated demonstration settings. Not a recording of the user's desktop.",
                PrivateUserDataLoaded = false,
                Skins = new[] { "YeShunguang", "robin" }.Select(folder => new
                {
                    Folder = folder,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "Pets", folder, "spritesheet.png"))))
                })
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { skins.Close(); focus.Close(); desktop.Dispose(); app.Shutdown(); }
        Console.WriteLine("Native promotion captures: " + output);
        return 0;
    }

    private static void ExportSprites(PetPackage package, string output)
    {
        var folder = Path.Combine(output, package.Manifest.Id);
        Directory.CreateDirectory(folder);
        foreach (var state in new[] { PetState.Idle, PetState.Waving, PetState.Jumping, PetState.Running, PetState.RunningRight })
        {
            var animation = package.GetAnimation(state);
            for (var i = 0; i < animation.FrameCount; i++)
                Save(package.GetFrame(animation.Row, animation.StartColumn + i), Path.Combine(folder, $"{state}-{i}.png"));
        }
        File.WriteAllText(Path.Combine(folder, "animations.json"), JsonSerializer.Serialize(
            new[] { PetState.Idle, PetState.Waving, PetState.Jumping, PetState.Running, PetState.RunningRight }
                .ToDictionary(s => s.ToString(), s => package.GetAnimation(s).DurationsMs)));
    }
    private static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = window.Top = -30000;
        window.ShowActivated = window.ShowInTaskbar = false;
        window.Show(); Wait(150);
    }
    private static void Capture(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right),
            (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        image.Render(background); image.Render(root);
        Save(image, path);
    }
    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Wait(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
    private sealed class DemoClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero).AddTicks(_ticks);
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }
}
