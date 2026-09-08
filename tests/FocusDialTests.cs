using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class FocusDialTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private)!.Invoke(owner, args);
    private static T Control<T>(Window window, string name) => (T)window.FindName(name);
    private static void Click(Window window, string name) => Control<System.Windows.Controls.Primitives.ButtonBase>(window, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    public static void Run(Action<bool, string> check, PetCatalog catalog, string temporary)
    {
        var clock = new SpeechStudyTests.ManualClock();
        var store = new DesktopSettingsStore(Path.Combine(temporary, "dial-desktop.json"));
        var config = DesktopConfiguration.Migrate(new PetSettings());
        var writes = 0;
        var fail = false;
        using var desktop = new DesktopSession(config, catalog, draft => { if (fail) throw new IOException("simulated duration save failure"); writes++; store.Save(draft); }, false, clock);
        desktop.Start(false);
        var runtime = desktop.Companion;
        var session = runtime.Session;
        var pet = desktop.Windows.First().Package;
        var focus = new FocusWindow(session, runtime.Settings, pet, () => false, runtime);
        try
        {
            var input = Control<TextBox>(focus, "MinutesInput");
            var dial = Control<DurationDial>(focus, "Dial");
            check(input.Text == "25" && dial.Value == 25 && dial.Maximum == 120, "dial prepares persisted focus minutes");
            Click(focus, "BreakMode");
            check(session.Phase == SessionPhase.Break && session.Status == SessionStatus.Ready && input.Text == "5" && dial.Maximum == 60, "break segment prepares a separately editable duration");
            Click(focus, "Preset2");
            check(store.Load().Companion.BreakMinutes == 10 && runtime.Settings.FocusMinutes == 25 && session.Remaining.TotalMinutes == 10, "break preset persists without changing focus duration");
            Click(focus, "FocusMode");
            Click(focus, "Preset3");
            check(store.Load().Companion.FocusMinutes == 45 && runtime.Settings.BreakMinutes == 10, "focus preset persists without changing break duration");
            var instance = (PetSettings)typeof(MainWindow).GetField("_settings", Private)!.GetValue(desktop.Windows.First())!;
            check(instance.FocusMinutes == 45 && instance.BreakMinutes == 10, "saved durations propagate to all role preferences");
            typeof(FocusWindow).GetField("_editingMinutes", Private)!.SetValue(focus, true);
            input.Text = "120";
            check((bool)Call(focus, "CommitMinutes")! && session.Duration.TotalMinutes == 120, "numeric edit accepts maximum focus duration");
            foreach (var invalid in new[] { "", "0", "121", "1.5", "abc", "-1" })
            {
                typeof(FocusWindow).GetField("_editingMinutes", Private)!.SetValue(focus, true);
                input.Text = invalid;
                check(!(bool)Call(focus, "CommitMinutes")! && runtime.Settings.FocusMinutes == 120, "invalid duration cannot replace saved minutes: " + invalid);
                Click(focus, "ToggleButton");
                check(session.Status == SessionStatus.Ready, "invalid input blocks starting an unexpected duration");
            }
            Click(focus, "Preset2");
            check(input.Text == "25" && Control<TextBlock>(focus, "DurationError").Text.Length == 0, "preset recovers from an invalid input draft");
            fail = true;
            Click(focus, "Preset3");
            check(runtime.Settings.FocusMinutes == 25 && config.Companion.FocusMinutes == 25 && session.Duration.TotalMinutes == 25 && input.Text == "25", "failed save restores configuration, display and timer duration");
            check(Control<TextBlock>(focus, "DurationError").Text.Length > 0, "failed duration save is visible without an unhandled exception");
            Click(focus, "ToggleButton");
            check(session.Status == SessionStatus.Ready, "save failure cannot silently start the reverted duration");
            fail = false;
            Click(focus, "Preset3");
            check(runtime.Settings.FocusMinutes == 45 && Control<TextBlock>(focus, "DurationError").Text.Length == 0, "duration save can be retried after an IO failure");
            Call(desktop, "BeginSettings", desktop.Windows.First());
            Click(focus, "Preset1");
            check(runtime.Settings.FocusMinutes == 45 && config.Companion.FocusMinutes == 45 && Control<TextBlock>(focus, "DurationError").Text.Contains("设置窗口"), "open role settings cannot be overwritten by a conflicting timer edit");
            Call(desktop, "EndSettings");
            Click(focus, "Preset3");
            Click(focus, "ToggleButton");
            clock.Advance(17);
            var remaining = session.Remaining;
            check(!dial.IsEnabled && !Control<RadioButton>(focus, "BreakMode").IsEnabled && input.Visibility != Visibility.Visible, "running focus locks dial, mode switch and numeric editing");
            Click(focus, "BreakMode");
            Click(focus, "Preset1");
            session.PreparePhase(SessionPhase.Break);
            check(session.Phase == SessionPhase.Focus && session.Remaining == remaining, "programmatic disabled edits also preserve the active timer");
            Click(focus, "ToggleButton");
            try { runtime.SetDurations(1, 1); check(false, "paused configuration rejected"); } catch (InvalidOperationException) { check(true, "paused configuration rejected"); }
            clock.Advance(300);
            check(session.Remaining == remaining && !dial.IsEnabled, "paused timer keeps its remaining time and edit lock");
            Click(focus, "ResetButton");
            check(dial.IsEnabled && session.Phase == SessionPhase.Focus && input.Text == "45", "reset unlocks the latest saved focus duration");
            Click(focus, "BreakMode");
            Click(focus, "ToggleButton");
            clock.Advance(600);
            runtime.Tick();
            check(session.Status == SessionStatus.Completed && runtime.History.Records.Count == 0, "standalone break completion never records focus minutes");
            Click(focus, "ToggleButton");
            clock.Advance(2700);
            runtime.Tick();
            check(runtime.History.Records.Single().DurationSeconds == 2700 && session.CompletedFocusSessions == 1, "dial-configured focus completes and records its actual duration once");
            Click(focus, "BreakMode");
            check(session.Status == SessionStatus.Ready && input.Text == "10", "completed state can prepare and adjust the next break");
            session.Configure(35, 8);
            check(session.Duration.TotalMinutes == 8, "configuring a ready break respects the selected phase");
        }
        finally { focus.Close(); }
        using var restored = new DesktopSession(store.Load(), catalog, _ => { }, false);
        restored.Start(false);
        check(restored.Companion.Settings.FocusMinutes == 45 && restored.Companion.Settings.BreakMinutes == 10, "duration preferences survive a fresh desktop runtime");
        VerifyDial(check);
    }

    private static void VerifyDial(Action<bool, string> check)
    {
        var host = new ThemedWindow();
        var dial = new DurationDial { Minimum = 1, Maximum = 120, Value = 25, Width = 240, Height = 240 };
        host.Content = dial;
        dial.Measure(new Size(240, 240)); dial.Arrange(new Rect(0, 0, 240, 240));
        try
        {
            Call(dial, "BeginDrag", new Point(226, 120));
            check(dial.Value == 30, "dial right cardinal maps to one quarter of the minute range");
            Call(dial, "MoveDrag", new Point(120, 226));
            check(dial.Value == 60, "dial drag follows clockwise minutes");
            Call(dial, "MoveDrag", new Point(14, 120));
            Call(dial, "MoveDrag", new Point(118, 14));
            Call(dial, "MoveDrag", new Point(122, 14));
            check(dial.Value == 120, "dial clamps across the maximum seam instead of jumping to one minute");
            dial.CancelDrag();
            check(dial.Value == 25 && !dial.IsDragging, "cancelled drag restores its original draft");
            dial.Value = 1;
            Call(dial, "BeginDrag", new Point(122, 14));
            Call(dial, "MoveDrag", new Point(118, 14));
            check(dial.Value == 1, "dial clamps counterclockwise at its minimum");
            dial.CancelDrag();
            dial.Value = 120;
            Call(dial, "BeginDrag", new Point(122, 14));
            check(dial.Value == 120, "grabbing the maximum handle near twelve o'clock does not reset duration");
            dial.CancelDrag();
            var peer = UIElementAutomationPeer.CreatePeerForElement(dial)!;
            var provider = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
            var commits = 0; dial.CommitRequested += _ => commits++;
            provider.SetValue(42);
            check(provider.Value == 42 && commits == 1 && provider.SmallChange == 1, "dial exposes native accessible range editing");
            dial.IsEnabled = false;
            try { provider.SetValue(5); check(false, "read-only automation edit rejected"); } catch (System.Windows.Automation.ElementNotEnabledException) { check(true, "read-only automation edit rejected"); }
        }
        finally { host.Close(); }
    }

    public static void RunLive(Action<bool, string> check, PetCatalog catalog, string renders)
    {
        var config = DesktopConfiguration.Migrate(new PetSettings());
        config.Appearance.Accent = "#24796E";
        var saves = 0;
        using var desktop = new DesktopSession(config, catalog, _ => saves++, false);
        desktop.Start(false);
        foreach (var theme in new[] { "light", "dark" })
        {
            desktop.UpdateAppearance(new AppearanceOptions { Theme = theme, Accent = "#24796E", ReduceMotion = true });
            var window = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, desktop.Windows.First().Package, () => false, desktop.Companion);
            try
            {
                window.Left = window.Top = -30000;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.ShowInTaskbar = window.ShowActivated = false;
                window.Show(); Wait(60);
                Click(window, "FocusMode"); Click(window, "Preset2");
                var dial = Control<DurationDial>(window, "Dial");
                check(dial.IsVisible && dial.ActualWidth >= 188 && dial.ActualWidth <= 312 && dial.ActualWidth == dial.ActualHeight, "native timer dial keeps a bounded circular layout in " + theme);
                check(((ScrollViewer)window.Content).ScrollableHeight == 0, "default focus window fits without an unnecessary scrollbar");
                Render(window, renders, "focus-dial-" + theme + ".png");
                var provider = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(dial)!.GetPattern(PatternInterface.RangeValue);
                var before = saves;
                provider.SetValue(32); provider.SetValue(33); provider.SetValue(34);
                check(saves == before, "rapid accessible minute edits do not write every intermediate value");
                Wait(500);
                check(saves == before + 1 && config.Companion.FocusMinutes == 34, "debounced native edits persist the final duration once");
                provider.SetValue(35);
                var input = Control<TextBox>(window, "MinutesInput");
                Call(window, "Minutes_GotFocus", input, new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, dial, input));
                input.Text = "36";
                Wait(500);
                check(input.Text == "36" && config.Companion.FocusMinutes == 35, "pending dial save cannot erase a subsequent numeric draft");
                check((bool)Call(window, "CommitMinutes")! && config.Companion.FocusMinutes == 36, "numeric draft commits after switching from a queued dial edit");
                Click(window, "ToggleButton");
                Render(window, renders, "focus-running-" + theme + ".png");
                var buttonSize = Control<Button>(window, "ToggleButton").RenderSize;
                Click(window, "ToggleButton");
                check(Control<Button>(window, "ToggleButton").RenderSize == buttonSize && !dial.IsEnabled, "pause does not move controls or unlock time editing");
                Click(window, "ResetButton");
                provider.SetValue(120); Wait(500);
                Render(window, renders, "focus-maximum-" + theme + ".png");
                Click(window, "ToggleButton");
                var time = Control<TextBlock>(window, "TimeText");
                check(time.ActualWidth <= ((FrameworkElement)time.Parent).ActualWidth && time.Text.StartsWith("120:"), "maximum countdown fits its stable numeric container");
                Render(window, renders, "focus-maximum-running-" + theme + ".png");
                Click(window, "ResetButton");
                Click(window, "BreakMode");
                Render(window, renders, "focus-break-" + theme + ".png");
                window.Height = 470; Wait(60);
                var scroll = (ScrollViewer)window.Content;
                scroll.ScrollToBottom(); Wait(60);
                check(scroll.ScrollableHeight == 0 && Control<Button>(window, "HistoryButton").IsVisible, "compact focus layout keeps actions reachable without scrolling");
                Render(window, renders, "focus-compact-" + theme + ".png");
                provider.SetValue(12);
            }
            finally { window.Close(); }
            check(config.Companion.BreakMinutes == 12, "closing the focus panel flushes its pending duration edit");
        }
        foreach (var fatal in new[] { false, true })
        {
            var persistedMinutes = 0;
            using var exitDesktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()), catalog, c => persistedMinutes = c.Companion.FocusMinutes, false);
            exitDesktop.Start(false);
            var exitWindow = new FocusWindow(exitDesktop.Companion.Session, exitDesktop.Companion.Settings, exitDesktop.Windows.First().Package, () => false, exitDesktop.Companion);
            typeof(CompanionRuntime).GetField("_window", Private)!.SetValue(exitDesktop.Companion, exitWindow);
            exitWindow.Left = exitWindow.Top = -30000;
            exitWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            exitWindow.ShowInTaskbar = exitWindow.ShowActivated = false;
            exitWindow.Show(); Wait(40);
            var provider = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(Control<DurationDial>(exitWindow, "Dial"))!.GetPattern(PatternInterface.RangeValue);
            provider.SetValue(41);
            if (fatal) Call(exitDesktop, "DisposeAfterFailure"); else exitDesktop.Dispose();
            check(persistedMinutes == (fatal ? 25 : 41), fatal ? "fatal shutdown discards pending edits without overwriting saved configuration" : "orderly application exit flushes the final debounced timer edit");
        }
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
        Directory.CreateDirectory(directory);
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
