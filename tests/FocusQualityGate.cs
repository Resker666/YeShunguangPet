using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class FocusQualityGate
{
    public static void Run(Action<bool, string> check, string source, string output, int soakSeconds, int? seed)
    {
        Directory.CreateDirectory(output);
        var report = new Dictionary<string, object?>
        {
            ["version"] = typeof(FocusWindow).Assembly.GetName().Version?.ToString(),
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = RuntimeInformation.OSDescription,
            ["processorCount"] = Environment.ProcessorCount,
            ["startedAtUtc"] = DateTimeOffset.UtcNow,
            ["passed"] = false
        };
        var elapsed = Stopwatch.StartNew();
        try
        {
            FocusControllerTests.Run(check);
            report["controllerContracts"] = "passed";
            report["sequences"] = SessionSequenceTests.Run(check, output, seed);
            var pet = PetPackage.Load(Path.Combine(source, "Pets", "YeShunguang", "pet.json"));
            var performance = new List<object>();
            report["performance"] = performance;
            Performance(check, pet, source, performance);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // WPF retains its first window as MainWindow even after Close. Keep a separate owner for the harness.
            var keeper = new Window { ShowInTaskbar = false, ShowActivated = false };
            app.MainWindow = keeper;
            try
            {
                VerifyVisualComparator(check);
                report["visuals"] = FocusVisualGate.Run(source, output);
                check(true, "six normalized timer screenshots match pre-refactor baselines");
                report["lifecycle"] = Lifecycle(check, pet, soakSeconds, output);
            }
            finally { keeper.Close(); app.Shutdown(); }
            report["passed"] = true;
        }
        catch (Exception ex) { report["error"] = ex.ToString(); throw; }
        finally
        {
            report["elapsedSeconds"] = elapsed.Elapsed.TotalSeconds;
            File.WriteAllText(Path.Combine(output, "quality-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void Performance(Action<bool, string> check, PetPackage pet, string source, List<object> results)
    {
        var clock = new QualityClock(); var writes = 0;
        using var service = new CompanionService(new PetSettings(), clock, persistDurations: _ => writes++);
        service.Configure();
        using var panel = new FocusPanelController(service.Session, service.Settings, service.SetDurations);
        panel.Toggle();
        var first = pet.GetFrame(0, 0);
        for (var i = 0; i < 10_000; i++) { panel.Snapshot(); pet.GetFrame(0, 0); service.Tick(); }
        void Measure(string name, int iterations, long allocationBudget, double timeBudgetMs, Action action)
        {
            var watch = Stopwatch.StartNew();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++) action();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            watch.Stop();
            results.Add(new { name, iterations, allocatedBytes = allocated, elapsedMs = watch.Elapsed.TotalMilliseconds, allocationBudget, timeBudgetMs });
            Console.WriteLine($"MEASURE: {name}: {watch.Elapsed.TotalMilliseconds:F1}ms, {allocated} bytes / {iterations} iterations");
            check(allocated <= allocationBudget && watch.Elapsed.TotalMilliseconds <= timeBudgetMs, "performance budget: " + name);
        }
        var checksum = 0;
        Measure("timer snapshots", 250_000, 1_048_576, 10_000, () => checksum ^= panel.Snapshot().RemainingSeconds);
        Measure("cached sprite frames", 100_000, 1_048_576, 10_000, () => { if (!ReferenceEquals(first, pet.GetFrame(0, 0))) throw new InvalidOperationException("Frame cache missed"); });
        Measure("service polling", 100_000, 1_048_576, 10_000, service.Tick);
        Measure("timer display formatting", 20_000, 16_777_216, 10_000, () => { var state = panel.Snapshot(); checksum ^= state.TimeText.Length + state.ToggleText.Length + state.PhaseText.Length; });
        check(writes == 0 && service.History.Records.Count == 0, "polling and rendering do not write settings or fabricate history");
        var copies = Enumerable.Range(0, 3).Select(_ => PetPackage.Load(Path.Combine(source, "Pets", "YeShunguang", "pet.json"))).ToArray();
        check(copies.All(p => ReferenceEquals(p.SpriteSheet, pet.SpriteSheet) && ReferenceEquals(p.GetFrame(0, 0), first)), "three instances reuse the decoded sheet and cached frame");
        GC.KeepAlive(checksum);
    }

    private static void VerifyVisualComparator(Action<bool, string> check)
    {
        BitmapSource Solid(byte color)
        {
            var bytes = Enumerable.Repeat(color, 64 * 64 * 4).ToArray();
            return BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, bytes, 64 * 4);
        }
        var black = Solid(0); var white = Solid(255);
        check(FocusVisualGate.Compare(black, black) == (0, 0, 0), "visual comparator accepts identical pixels");
        var changed = FocusVisualGate.Compare(black, white);
        check(!FocusVisualGate.Accepts(changed), "visual comparator rejects a blank/opposite-color frame");
    }

    private static object Lifecycle(Action<bool, string> check, PetPackage pet, int soakSeconds, string output)
    {
        UiTheme.Apply(new AppearanceOptions { Theme = "light", ReduceMotion = false, Accent = "#24796E" });
        using var runtime = new CompanionRuntime(new PetSettings(), new QualityClock());
        runtime.Start();
        for (var i = 0; i < 6; i++) Cycle(runtime, pet, i);
        Settle();
        var baseline = Sample();
        check(baseline.Handles > 0 && baseline.UserObjects > 0, "native resource counters are available");
        var references = new List<WeakReference>();
        var samples = new List<MemorySample> { baseline };
        var elapsed = Stopwatch.StartNew();
        var count = 0;
        do
        {
            references.AddRange(Cycle(runtime, pet, count));
            count++;
            if (count % 10 == 0) { Settle(); samples.Add(Sample()); }
            if (soakSeconds > 0) FocusVisualGate.Pump(100);
        } while (count < 30 || elapsed.Elapsed.TotalSeconds < soakSeconds);
        Settle();
        var final = Sample(); samples.Add(final);
        var alive = references.Count(r => r.IsAlive);
        var metrics = new
        {
            cycles = count, requestedSeconds = soakSeconds, elapsedSeconds = elapsed.Elapsed.TotalSeconds,
            survivingWindowsOrControllers = alive, samples,
            managedGrowthBytes = final.Managed - baseline.Managed, privateGrowthBytes = final.Private - baseline.Private,
            handleGrowth = final.Handles - baseline.Handles, userGrowth = final.UserObjects - baseline.UserObjects, gdiGrowth = final.GdiObjects - baseline.GdiObjects,
            budgets = new { managedGrowthBytes = 16 * 1024 * 1024, privateGrowthBytes = 64 * 1024 * 1024, handles = 64, userObjects = 16, gdiObjects = 32 }
        };
        // Preserve measurements even when the lifecycle assertions below fail.
        File.WriteAllText(Path.Combine(output, "lifecycle-report.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"MEASURE: {count} window cycles in {elapsed.Elapsed.TotalSeconds:F1}s; alive={alive}, managed growth={metrics.managedGrowthBytes}, private growth={metrics.privateGrowthBytes}");
        check(alive == 0, "closed windows and controllers are collectible with the runtime still alive");
        check(metrics.managedGrowthBytes <= metrics.budgets.managedGrowthBytes && metrics.privateGrowthBytes <= metrics.budgets.privateGrowthBytes,
            "repeated window lifecycle stays within managed and private memory growth budgets");
        check(metrics.handleGrowth <= 64 && metrics.userGrowth <= 16 && metrics.gdiGrowth <= 32, "native handles and USER/GDI objects stay within growth budgets");
        GC.KeepAlive(runtime);
        return metrics;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] Cycle(CompanionRuntime runtime, PetPackage pet, int index)
    {
        var window = new FocusWindow(runtime.Session, runtime.Settings, pet, () => false, runtime, managePlacement: true)
        { ShowInTaskbar = false, ShowActivated = false, Opacity = 0, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
        try
        {
            window.Show(); FocusVisualGate.Pump(5);
            window.Controller.Reset();
            window.Controller.SelectPreset(index % 2 == 0 ? 25 : 45);
            window.Controller.Toggle(); window.Controller.Toggle();
            Click(window, "MiniModeButton"); FocusVisualGate.Pump(5);
            window.Width += 4;
            window.WindowState = WindowState.Minimized; window.WindowState = WindowState.Normal;
            Click(window, "ExpandButton");
            FocusVisualGate.Pump(5);
            return new[] { new WeakReference(window), new WeakReference(window.Controller) };
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, string name) => ((ButtonBase)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Settle()
    {
        for (var i = 0; i < 3; i++) { FocusVisualGate.Pump(40); FocusControllerTests.Collect(); }
    }
    private sealed record MemorySample(long Managed, long Private, int Handles, int UserObjects, int GdiObjects);
    private static MemorySample Sample()
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        return new(GC.GetTotalMemory(false), process.PrivateMemorySize64, process.HandleCount, GetGuiResources(process.Handle, 1), GetGuiResources(process.Handle, 0));
    }
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
}
