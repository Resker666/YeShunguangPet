using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class FocusLayoutTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private)!.Invoke(owner, args);
    private static T Control<T>(Window window, string name) => (T)window.FindName(name);

    public static void Run(Action<bool, string> check, PetCatalog catalog, string temporary)
    {
        var legacy = JsonSerializer.Deserialize<DesktopConfiguration>("{\"SchemaVersion\":2,\"Companion\":{},\"Pets\":[]}")!;
        legacy.Validate();
        check(legacy.FocusWindow.Width == 400 && legacy.FocusWindow.Height == 560 && legacy.FocusWindow.LeftPixels is null, "older desktops get centered focus-window defaults without a schema migration");
        var invalid = new FocusWindowOptions { Width = double.NaN, Height = double.PositiveInfinity, LeftPixels = int.MinValue, TopPixels = 10 };
        invalid.Normalize();
        check(invalid.Width == 400 && invalid.Height == 560 && invalid.LeftPixels is null && invalid.TopPixels is null, "nonfinite dimensions and invalid screen positions normalize safely");
        invalid = new FocusWindowOptions { Width = 12, Height = 99999, LeftPixels = -1800, TopPixels = -200 };
        invalid.Normalize();
        check(invalid.Width == 320 && invalid.Height == 4096 && invalid.LeftPixels == -1800, "window dimensions are bounded while negative monitor coordinates remain valid");
        var store = new DesktopSettingsStore(Path.Combine(temporary, "focus-layout.json"));
        var writes = 0;
        var fail = false;
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()), catalog,
            c => { if (fail) throw new IOException("simulated window save failure"); writes++; store.Save(c); }, false);
        desktop.Start(false);
        var runtime = desktop.Companion;
        runtime.Session.StartOrResume();
        runtime.Session.Pause();
        var remaining = runtime.Session.Remaining;
        var options = new FocusWindowOptions { Width = 520, Height = 680, LeftPixels = -1400, TopPixels = 50 };
        runtime.SaveWindowOptions(options);
        check(store.Load().FocusWindow == options && runtime.WindowOptions == options, "normal window dimensions and native coordinates survive a disk roundtrip");
        check(runtime.Session.Remaining == remaining && runtime.Session.Status == SessionStatus.Paused, "saving window geometry does not reconfigure or reset a paused session");
        var before = writes;
        runtime.SaveWindowOptions(options.Copy());
        check(writes == before, "unchanged geometry does not create duplicate writes");
        var exposed = runtime.WindowOptions;
        exposed.Width = 900;
        check(runtime.WindowOptions.Width == 520, "window option snapshots cannot mutate live preferences");
        fail = true;
        try { runtime.SaveWindowOptions(new FocusWindowOptions { Width = 600 }); } catch (IOException) { }
        check(runtime.WindowOptions == options && desktop.Configuration.FocusWindow == options && store.Load().FocusWindow == options, "failed geometry save preserves runtime, configuration and last good file");
        fail = false;
        VerifyHiddenPlacement(check, catalog);
    }

    private static Rect NativeBounds(Window window)
    {
        var native = typeof(MainWindow).Assembly.GetType("YeShunguangPet.NativeMethods")!;
        var args = new object[] { window, Rect.Empty };
        native.GetMethod("TryGetWindowBounds", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return (Rect)args[1];
    }

    private static void VerifyHiddenPlacement(Action<bool, string> check, PetCatalog catalog)
    {
        var config = DesktopConfiguration.Migrate(new PetSettings());
        config.FocusWindow = new FocusWindowOptions { Width = 500, Height = 580, LeftPixels = -900000, TopPixels = 900000 };
        var writes = 0;
        using var desktop = new DesktopSession(config, catalog, _ => writes++, false);
        desktop.Start(false);
        var window = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, desktop.Windows.First().Package, () => false, desktop.Companion, managePlacement: true);
        try
        {
            new WindowInteropHelper(window).EnsureHandle();
            check(window.Width == 500 && window.Height == 580, "focus window restores saved DIP dimensions before opening");
            Call(window, "FitToScreen");
            var bounds = NativeBounds(window);
            var work = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
            check(bounds.Left >= work.Left && bounds.Top >= work.Top && bounds.Right <= work.Right + 1 && bounds.Bottom <= work.Bottom + 1,
                "unplugged-monitor coordinates recover into a current work area without showing a window");
            typeof(FocusWindow).GetField("_placementReady", Private)!.SetValue(window, true);
            Call(window, "QueuePlacementSave");
            var before = writes;
            Call(window, "QueuePlacementSave");
            check(writes == before, "window movement queues one delayed persistence operation");
            Call(window, "SaveWindowPlacement");
            var saved = desktop.Companion.WindowOptions;
            check(writes == before + 1 && saved.LeftPixels == (int)bounds.Left && saved.TopPixels == (int)bounds.Top, "clamped position is persisted using physical screen coordinates");
            var snapshot = typeof(FocusWindow).GetField("_lastNormalPlacement", Private)!.GetValue(window);
            window.WindowState = WindowState.Minimized;
            Call(window, "SaveWindowPlacement");
            check(desktop.Companion.WindowOptions == saved && Equals(snapshot, typeof(FocusWindow).GetField("_lastNormalPlacement", Private)!.GetValue(window)), "minimization never replaces the normal rectangle with offscreen system coordinates");
            window.WindowState = WindowState.Maximized;
            Call(window, "SaveWindowPlacement");
            check(desktop.Companion.WindowOptions == saved, "maximization never overwrites the normal window size");
        }
        finally { window.Close(); }
        var reopened = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, desktop.Windows.First().Package, () => false, desktop.Companion, managePlacement: true);
        try
        {
            new WindowInteropHelper(reopened).EnsureHandle();
            check(reopened.Width == desktop.Companion.WindowOptions.Width && reopened.Height == desktop.Companion.WindowOptions.Height,
                "reopening restores the last normal size instead of the minimized size");
        }
        finally { reopened.Close(); }
    }

    public static void RunLive(Action<bool, string> check, PetCatalog catalog, string renders)
    {
        var clock = new SpeechStudyTests.ManualClock();
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()), catalog, _ => { }, false, clock);
        desktop.Start(false);
        foreach (var theme in new[] { "light", "dark" })
        {
            desktop.UpdateAppearance(new AppearanceOptions { Theme = theme, Accent = "#24796E", ReduceMotion = true });
            var window = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, desktop.Windows.First().Package, () => false, desktop.Companion);
            try
            {
                window.Left = window.Top = -30000;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.ShowActivated = window.ShowInTaskbar = false;
                window.Show(); Wait(100);
                check(window.ResizeMode == ResizeMode.CanResize, "focus window retains native resizing and maximization");
                foreach (var size in new[] { new Size(320, 460), new Size(400, 560), new Size(600, 720) })
                {
                    desktop.Companion.Session.Reset();
                    desktop.Companion.SetDurations(120, 60);
                    window.Width = size.Width; window.Height = size.Height; Wait(100);
                    var name = $"{(int)size.Width}x{(int)size.Height}-{theme}";
                    var scroll = (ScrollViewer)window.Content;
                    var dial = Control<DurationDial>(window, "Dial");
                    var frame = Control<Grid>(window, "DialFrame");
                    check(scroll.ScrollableWidth == 0 && scroll.ScrollableHeight == 0, "responsive timer fits without scrolling at " + name);
                    check(Math.Abs(dial.ActualWidth - dial.ActualHeight) < 1 && dial.ActualWidth is >= 188 and <= 312, "dial remains circular and bounded at " + name);
                    foreach (var controlName in new[] { "ToggleButton", "ResetButton", "SkipButton", "HistoryButton", "Presets", "DialFrame" })
                        check(IsInside(Control<FrameworkElement>(window, controlName), scroll), controlName + " remains inside the visible client area at " + name);
                    var center = Control<Grid>(window, "NumberArea");
                    check(center.ActualWidth < frame.ActualWidth - 48, "numeric edit stays clear of the dial track at " + name);
                    Render(window, renders, "focus-responsive-ready-" + name + ".png");
                    var previousSize = new Size(window.Width, window.Height);
                    desktop.Companion.Session.StartOrResume();
                    Wait(80);
                    var time = Control<TextBlock>(window, "TimeText");
                    check(time.Text == "120:00" && time.ActualWidth <= center.ActualWidth && IsInside(time, scroll), "maximum countdown fits the responsive number field at " + name);
                    check(new Size(window.Width, window.Height) == previousSize && Control<Border>(window, "PresetHost").ActualHeight == 0,
                        "starting a timer collapses unused presets without resizing the window at " + name);
                    Render(window, renders, "focus-responsive-running-" + name + ".png");
                    clock.Advance(12);
                    var remaining = desktop.Companion.Session.Remaining;
                    window.Width += 10; window.Height += 10; window.Left += 5; Wait(60);
                    check(desktop.Companion.Session.Remaining == remaining && desktop.Companion.Session.Status == SessionStatus.Running, "moving and resizing preserve active elapsed time at " + name);
                    desktop.Companion.Session.Pause();
                    window.Width = size.Width; window.Height = size.Height; Wait(60);
                    check(desktop.Companion.Session.Remaining == remaining, "paused time survives responsive layout changes at " + name);
                }
                foreach (var name in new[] { "Dial", "DialFrame", "MinutesInput", "TimeText", "ToggleText", "Preset1", "TodaySummary", "FocusMode" })
                    check(!(bool)Call(window, "IsWindowDragTarget", Control<DependencyObject>(window, name))!, "window dragging excludes " + name);
                check((bool)Call(window, "IsWindowDragTarget", Control<DependencyObject>(window, "PetName"))! &&
                      (bool)Call(window, "IsWindowDragTarget", Control<DependencyObject>(window, "ContentRoot"))!, "header and noninteractive blank areas can move the window");
                desktop.Companion.Session.Reset();
                typeof(FocusWindow).GetField("_editingMinutes", Private)!.SetValue(window, true);
                Control<TextBox>(window, "MinutesInput").Text = "42";
                window.Width = 320; window.Height = 460; Wait(300);
                check(Control<TextBox>(window, "MinutesInput").Text == "42" && desktop.Companion.Settings.FocusMinutes == 120, "resize preserves an uncommitted numeric draft");
                Call(window, "CommitMinutes");
                desktop.Companion.SetDurations(1, 1);
                desktop.Companion.Session.StartOrResume(); clock.Advance(60); desktop.Companion.Tick(); Wait(80);
                check(Control<TextBlock>(window, "PhaseText").Text == "专注完成" && Control<TextBlock>(window, "ToggleText").Text == "开始休息", "compact completed timer retains its next-stage command");
                Render(window, renders, "focus-responsive-completed-compact-" + theme + ".png");
            }
            finally { window.Close(); }
        }
    }

    private static bool IsInside(FrameworkElement element, FrameworkElement root)
    {
        var box = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        return box.Left >= -1 && box.Top >= -1 && box.Right <= root.ActualWidth + 1 && box.Bottom <= root.ActualHeight + 1;
    }
    private static void Wait(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
    private static void Render(Window window, string directory, string name)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
