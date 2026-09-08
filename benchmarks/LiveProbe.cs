using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class LiveProbe
{
    public static int Run(string source, string output, int seconds, bool nativeIntegration = false)
    {
        if (seconds < 1 || seconds > 3600) throw new ArgumentOutOfRangeException(nameof(seconds));
        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var anchor = new Window { ShowInTaskbar = false };
        app.MainWindow = anchor;
        var config = DesktopConfiguration.Migrate(new PetSettings
        {
            Scale = 0.5, Left = 60, Top = 80, Topmost = false, ClickThrough = true,
            RandomIdleActions = false, LookAtMouse = false, DesktopRoaming = false, NotificationsEnabled = false
        });
        var catalog = new PetCatalog(Path.Combine(source, "Pets"), Path.Combine(source, "artifacts", "probe-empty-users"));
        using var desktop = new DesktopSession(config, catalog, _ => { }, nativeIntegration: nativeIntegration);
        using var process = Process.GetCurrentProcess();
        var phases = new List<object>();
        var lastFrames = new Dictionary<string, ImageSource?>();
        var transitions = 0;
        var phase = -1;
        var watch = Stopwatch.StartNew();
        var started = 0.0;
        var cpu = 0.0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        WeakReference[] closed = Array.Empty<WeakReference>();
        long beforeStress = 0;
        var fault = false;
        var nonblank = true;
        var focusSurvived = true;
        void Begin(int next)
        {
            phase = next;
            started = watch.Elapsed.TotalSeconds;
            process.Refresh();
            cpu = process.TotalProcessorTime.TotalSeconds;
            transitions = 0;
        }
        app.Startup += (_, _) =>
        {
            desktop.Start();
            watch.Restart();
            timer.Start();
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            fault = true;
            File.WriteAllText(output + ".error.txt", e.Exception.ToString());
            timer.Stop();
            desktop.Dispose();
            app.Shutdown(1);
        };
        timer.Tick += (_, _) =>
        {
            if (phase == -1) { if (watch.Elapsed.TotalSeconds >= 2) Begin(0); return; }
            foreach (var window in desktop.Windows)
            {
                var image = (Image)window.FindName("SpriteImage");
                if (lastFrames.TryGetValue(window.InstanceId, out var previous) && !ReferenceEquals(previous, image.Source)) transitions++;
                lastFrames[window.InstanceId] = image.Source;
            }
            if (phase < 3 && watch.Elapsed.TotalSeconds - started < seconds) return;
            if (phase < 3)
            {
                process.Refresh();
                var elapsed = watch.Elapsed.TotalSeconds - started;
                phases.Add(new
                {
                    Phase = new[] { "one-visible", "three-visible", "three-hidden" }[phase],
                    Seconds = elapsed,
                    CpuPercentOneCore = 100 * (process.TotalProcessorTime.TotalSeconds - cpu) / elapsed,
                    PrivateMiB = process.PrivateMemorySize64 / 1048576.0,
                    WorkingSetMiB = process.WorkingSet64 / 1048576.0,
                    FrameTransitions = transitions,
                    RoleTimers = desktop.Windows.Sum(w => (w.HasAnimationTimer ? 1 : 0) + (w.HasAmbientTimer ? 1 : 0) +
                        (w.HasRoamingTimer ? 1 : 0) + (w.HasDockTimer ? 1 : 0))
                });
            }
            if (phase == 0)
            {
                var secondId = catalog.Scan().Pets.FirstOrDefault(p => p.Id != PetPackage.DefaultId)?.Id ?? PetPackage.DefaultId;
                foreach (var petId in new[] { secondId, PetPackage.DefaultId })
                {
                    var window = desktop.Add(petId, show: false);
                    var settings = (PetSettings)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    settings.Scale = 0.5;
                    settings.Left = 60 + desktop.Number(window) * 130;
                    settings.Top = 80;
                    settings.Topmost = settings.RandomIdleActions = settings.LookAtMouse = settings.DesktopRoaming = false;
                    settings.ClickThrough = true;
                    window.Show();
                }
                Begin(1);
            }
            else if (phase == 1)
            {
                foreach (var window in desktop.Windows)
                    nonblank &= Render(window, Path.Combine(Path.GetDirectoryName(output)!, $"live-role-{desktop.Number(window)}.png"));
                desktop.HideAll();
                Begin(2);
            }
            else if (phase == 2)
            {
                if (nativeIntegration)
                {
                    desktop.OpenFocus();
                    desktop.OpenFocus();
                    desktop.Companion.Session.StartOrResume();
                    focusSurvived = desktop.Companion.FocusWindowCount == 1;
                }
                lastFrames.Clear();
                foreach (var window in desktop.Windows.ToArray()) desktop.Remove(window);
                if (nativeIntegration) focusSurvived &= desktop.Companion.Session.IsFocusing && desktop.Companion.FocusWindowCount == 1;
                Collect();
                beforeStress = GC.GetTotalMemory(false);
                closed = Enumerable.Range(0, 150).Select(_ => Cycle(desktop)).ToArray();
                Begin(3);
            }
            else if (watch.Elapsed.TotalSeconds - started >= 3)
            {
                Collect();
                var result = new
                {
                    Version = typeof(MainWindow).Assembly.GetName().Version?.ToString(),
                    Workload = "Real WPF windows with isolated in-memory config; no change to user settings",
                    NativeIntegration = nativeIntegration,
                    GlobalHotkeyRegistered = desktop.HotkeyRegistered,
                    SharedFocusSurvivedRoleRemoval = focusSurvived,
                    Phases = phases,
                    RenderedRolesNonblank = nonblank,
                    LifecycleCycles = closed.Length,
                    ClosedWindowsStillAlive = closed.Count(w => w.IsAlive),
                    ManagedHeapBeforeCycles = beforeStress,
                    ManagedHeapAfterCycles = GC.GetTotalMemory(false),
                    RemainingRoles = desktop.Windows.Count,
                    ActiveImageLeases = PetPackage.ActiveImageLeases
                };
                File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(File.ReadAllText(output));
                timer.Stop();
                desktop.Dispose();
                anchor.Close();
                app.Shutdown(nonblank && closed.All(w => !w.IsAlive) && focusSurvived &&
                    PetPackage.ActiveImageLeases == 0 ? 0 : 1);
            }
        };
        var exit = app.Run();
        return fault ? 1 : exit;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Cycle(DesktopSession desktop)
    {
        var window = desktop.Add(PetPackage.DefaultId, false);
        typeof(MainWindow).GetMethod("InitializeCompanion", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        var reference = new WeakReference(window);
        desktop.Remove(window);
        return reference;
    }

    private static bool Render(MainWindow window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.Width), (int)Math.Ceiling(window.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var nonblank = Enumerable.Range(0, bitmap.PixelWidth * bitmap.PixelHeight).Count(i => pixels[i * 4 + 3] > 0) > 20;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return nonblank;
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
