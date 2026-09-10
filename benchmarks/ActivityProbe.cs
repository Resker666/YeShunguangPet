using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class ActivityProbe
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static int Run(string source, string output, int seconds)
    {
        if (seconds is < 3 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!; Directory.CreateDirectory(directory);
        typeof(AppLogger).GetMethod("SetSink", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            new object[] { new ResilientLog(new[] { Path.Combine(directory, "probe-logs") }) });
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var anchor = new Window { ShowActivated = false, ShowInTaskbar = false }; app.MainWindow = anchor;
        var settings = new PetSettings { Scale = 0.5, Topmost = false, ClickThrough = true, LookAtMouse = true,
            RandomIdleActions = false, DesktopRoaming = false, NotificationsEnabled = false };
        var config = DesktopConfiguration.Migrate(settings);
        var catalog = new PetCatalog(Path.Combine(Path.GetFullPath(source), "Pets"), Path.Combine(Path.GetTempPath(), "pet-probe-" + Guid.NewGuid().ToString("N")));
        using var desktop = new DesktopSession(config, catalog, _ => { }, false);
        using var process = Process.GetCurrentProcess();
        var measurements = new List<object>();
        var observers = new List<Observer>();
        try
        {
            desktop.Start(false);
            void Attach(MainWindow window)
            {
                window.Opacity = 0;
                window.Show(); window.Left = window.Top = -30000;
                observers.Add(new Observer(window));
            }
            Attach(desktop.Windows.Single());
            void Measure(string name)
            {
                Pump(600);
                foreach (var observer in observers) observer.Reset();
                var cpu = process.TotalProcessorTime.TotalSeconds;
                var allocated = GC.GetTotalAllocatedBytes(true);
                var elapsed = Stopwatch.StartNew(); Pump(seconds * 1000); elapsed.Stop(); process.Refresh();
                var errors = observers.SelectMany(o => o.Lateness).OrderBy(x => x).ToArray();
                var result = new
                {
                    name, seconds = elapsed.Elapsed.TotalSeconds,
                    cpuPercentOneCore = 100 * (process.TotalProcessorTime.TotalSeconds - cpu) / elapsed.Elapsed.TotalSeconds,
                    allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                    privateMiB = process.PrivateMemorySize64 / 1048576.0,
                    callbacks = observers.Sum(o => o.Callbacks), frames = observers.Sum(o => o.Frames),
                    frameLatenessP95Ms = errors.Length == 0 ? 0 : errors[(int)((errors.Length - 1) * 0.95)],
                    activeRoleTimers = desktop.Windows.Sum(w => (w.HasAnimationTimer ? 1 : 0) + (w.HasAmbientTimer ? 1 : 0) + (w.HasRoamingTimer ? 1 : 0) + (w.HasDockTimer ? 1 : 0))
                };
                measurements.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
            }
            Measure("one-idle-look");
            Attach(desktop.Add(PetPackage.DefaultId, false)); Attach(desktop.Add(PetPackage.DefaultId, false));
            Measure("three-idle-look");
            foreach (var window in desktop.Windows)
            {
                var options = (PetSettings)typeof(MainWindow).GetField("_settings", Private)!.GetValue(window)!;
                options.LookAtMouse = false; options.RandomIdleActions = true;
                typeof(MainWindow).GetMethod("ResetIdleBehaviorSchedule", Private)!.Invoke(window, null);
                typeof(MainWindow).GetMethod("RefreshActivityTimers", Private)!.Invoke(window, null);
            }
            Measure("three-random-only");
            desktop.SetQuiet(true); Measure("three-quiet");
            desktop.HideAll(); Measure("three-hidden");
            var report = new
            {
                version = typeof(MainWindow).Assembly.GetName().Version?.ToString(),
                workload = "Offscreen transparent native WPF windows; scheduling and allocation probe, not battery or on-screen GPU measurement",
                measurements
            };
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally { foreach (var observer in observers) observer.Dispose(); desktop.Dispose(); anchor.Close(); app.Shutdown(); }
    }

    private sealed class Observer : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Image _image;
        private readonly DispatcherTimer[] _timers;
        private readonly DependencyPropertyDescriptor _property;
        private long _last;
        private int _duration;
        public int Callbacks, Frames;
        public List<double> Lateness { get; } = new();
        public Observer(MainWindow window)
        {
            _window = window; _image = (Image)window.FindName("SpriteImage");
            _timers = typeof(MainWindow).GetFields(Private).Where(f => f.FieldType == typeof(DispatcherTimer)).Select(f => (DispatcherTimer)f.GetValue(window)!).ToArray();
            foreach (var timer in _timers) timer.Tick += Tick;
            _property = DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));
            _property.AddValueChanged(_image, Frame);
        }
        public void Reset() { Callbacks = Frames = 0; Lateness.Clear(); _last = 0; }
        private void Tick(object? sender, EventArgs e) => Callbacks++;
        private void Frame(object? sender, EventArgs e)
        {
            var now = Stopwatch.GetTimestamp();
            if (_last != 0) Lateness.Add(Math.Max(0, Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds - _duration));
            var animation = (PetAnimation)typeof(MainWindow).GetField("_animation", Private)!.GetValue(_window)!;
            var index = (int)typeof(MainWindow).GetField("_frameIndex", Private)!.GetValue(_window)!;
            _duration = animation.DurationsMs[index]; _last = now; Frames++;
        }
        public void Dispose()
        {
            foreach (var timer in _timers) timer.Tick -= Tick;
            _property.RemoveValueChanged(_image, Frame);
        }
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
}
