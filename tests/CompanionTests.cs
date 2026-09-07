using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class CompanionTests
{
    public static void Run(Action<bool, string> check, PetPackage pet, string? renderRoot)
    {
        var clock = new ManualClock();
        var session = new CompanionSession(clock);
        var completions = new List<SessionPhase>();
        session.Completed += completions.Add;
        session.Configure(1, 1);
        check(session.Status == SessionStatus.Ready && session.Remaining == TimeSpan.FromMinutes(1), "focus ready duration");
        session.StartOrResume();
        clock.Advance(TimeSpan.FromSeconds(20));
        check(session.IsFocusing && session.Remaining == TimeSpan.FromSeconds(40), "focus elapsed time");
        session.Pause();
        clock.Advance(TimeSpan.FromMinutes(10));
        check(session.Status == SessionStatus.Paused && session.Remaining == TimeSpan.FromSeconds(40), "paused time does not elapse");
        session.Configure(2, 2);
        check(session.Remaining == TimeSpan.FromSeconds(40) && session.Duration == TimeSpan.FromMinutes(1), "settings keep active session duration");
        session.StartOrResume();
        clock.Advance(TimeSpan.FromSeconds(40));
        session.Tick();
        session.Tick();
        check(session.Status == SessionStatus.Completed && session.CompletedFocusSessions == 1 && completions.Count == 1,
            "focus completes once and awaits manual break");
        clock.Advance(TimeSpan.FromHours(8));
        session.Tick();
        check(session.CompletedFocusSessions == 1 && completions.Count == 1, "sleep cannot manufacture completed sessions");
        session.StartOrResume();
        check(session.Phase == SessionPhase.Break && session.Remaining == TimeSpan.FromMinutes(2), "manual break uses configured duration");
        clock.Advance(TimeSpan.FromMinutes(2));
        session.Pause();
        check(session.Status == SessionStatus.Completed && completions.Count == 2 && completions[1] == SessionPhase.Break,
            "pause at deadline delivers completion instead of pausing zero");
        session.StartOrResume();
        check(session.IsFocusing && session.Remaining == TimeSpan.FromMinutes(2), "next focus begins manually");
        session.Reset();
        check(session.Status == SessionStatus.Ready && session.CompletedFocusSessions == 1, "end resets current session only");
        session.StartOrResume();
        session.SkipBreak();
        check(session.IsFocusing, "skip break cannot abandon active focus");
        clock.Advance(TimeSpan.FromMinutes(2));
        session.Tick();
        session.SkipBreak();
        check(session.Phase == SessionPhase.Focus && session.Status == SessionStatus.Ready, "completed focus can skip planned break");

        var reminder = new BreakReminder(clock);
        reminder.Configure(true, 15);
        clock.Advance(TimeSpan.FromMinutes(14));
        check(!reminder.Poll(false), "reminder does not arrive early");
        clock.Advance(TimeSpan.FromMinutes(1));
        check(!reminder.Poll(true) && !reminder.Poll(false), "quiet reminder is discarded without backlog");
        clock.Advance(TimeSpan.FromMinutes(15));
        check(reminder.Poll(false) && !reminder.Poll(false), "reminder fires once per interval");
        reminder.Configure(false, 15);
        clock.Advance(TimeSpan.FromHours(1));
        check(!reminder.Poll(false), "disabled reminder remains silent");

        var settings = JsonSerializer.Deserialize<PetSettings>("{\"Scale\":1.4,\"SelectedPetId\":\"robin\"}")!;
        check(settings.ClickInteraction && settings.PauseNearMouse && !settings.BreakRemindersEnabled &&
            !settings.DoNotDisturb && settings.FocusMinutes == 25 && settings.BreakMinutes == 5,
            "v1.2 settings migrate to conservative companion defaults");
        settings.QuietHoursEnabled = true;
        check(DesktopBehavior.IsQuiet(settings, At(22, 0)) && DesktopBehavior.IsQuiet(settings, At(7, 59)) &&
            !DesktopBehavior.IsQuiet(settings, At(8, 0)) && !DesktopBehavior.IsQuiet(settings, At(12, 0)),
            "overnight quiet hours have correct boundaries");
        settings.QuietStartMinute = 9 * 60;
        settings.QuietEndMinute = 17 * 60;
        check(DesktopBehavior.IsQuiet(settings, At(9, 0)) && !DesktopBehavior.IsQuiet(settings, At(17, 0)),
            "daytime quiet hours have correct boundaries");
        settings.QuietEndMinute = settings.QuietStartMinute;
        check(DesktopBehavior.IsQuiet(settings, At(3, 0)), "equal quiet endpoints mean all day");
        settings.QuietHoursEnabled = false;
        settings.DoNotDisturb = true;
        check(DesktopBehavior.IsQuiet(settings, At(12, 0)), "manual quiet works without schedule");
        settings.FocusMinutes = 40;
        settings.MousePauseRadius = 120;
        var clone = JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(settings.Clone()))!;
        check(clone.DoNotDisturb && clone.FocusMinutes == 40 && clone.MousePauseRadius == 120 && clone.SelectedPetId == "robin",
            "companion preferences survive clone and serialization");

        var step = DesktopBehavior.Move(90, 1, 50, 20, 0, 100);
        check(step.Left == 90 && step.Direction == -1 && step.Remaining == 30, "roam reverses at right edge without overshoot");
        step = DesktopBehavior.Move(10, -1, 50, 20, 0, 100);
        check(step.Left == 10 && step.Direction == 1 && step.Remaining == 30, "roam reverses at left edge without overshoot");
        step = DesktopBehavior.Move(-1800, -1, 50, 20, -1920, -1800);
        check(step.Left == -1820, "roaming supports monitors with negative coordinates");
        step = DesktopBehavior.Move(90, 1, 10, 20, 0, 100);
        check(step.Left == 100 && step.Remaining == 0, "roaming finishes at distance budget");
        step = DesktopBehavior.Move(900, 1, 50, 20, 0, 100);
        check(step.Left == 80, "display bounds changes clamp roaming position");
        step = DesktopBehavior.Move(10, 1, 20, 10, 0, 0);
        check(step.Left == 0 && step.Remaining == 0, "oversized pet cannot roam outside work area");
        check(DesktopBehavior.IsNear(-40, 80, 192, 208, 80) &&
            !DesktopBehavior.IsNear(-81, 80, 192, 208, 80), "proximity measures from pet bounds");
        check(!DesktopBehavior.ExceedsDragThreshold(2, 2, 4, 4) &&
            DesktopBehavior.ExceedsDragThreshold(6 / 1.5, 0, 4, 4), "click and DPI-normalized drag thresholds");
        check(DesktopBehavior.IsNear(-90, 80, 192, 208, 100) &&
            !DesktopBehavior.IsNear(-90, 80, 192, 208, 80), "proximity hysteresis prevents rapid pause toggles");

        VerifyWindows(check, pet, renderRoot);
        VerifyClickEvents(check, pet);
    }

    private static void VerifyClickEvents(Action<bool, string> check, PetPackage pet)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        var window = new MainWindow();
        try
        {
            type.GetField("_pet", flags)!.SetValue(window, pet);
            ((PetSettings)type.GetField("_settings", flags)!.GetValue(window)!).ClickInteraction = true;
            type.GetMethod("PlayAnimation", flags)!.Invoke(window, new object[] { PetState.Idle, true });
            var mouseUp = type.GetMethod("Window_MouseLeftButtonUp", flags)!;
            var args = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                Environment.TickCount, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent
            };
            type.GetField("_pointerDown", flags)!.SetValue(window, true);
            type.GetField("_doublePress", flags)!.SetValue(window, false);
            mouseUp.Invoke(window, new object[] { window, args });
            var timer = (System.Windows.Threading.DispatcherTimer)type.GetField("_clickTimer", flags)!.GetValue(window)!;
            check(timer.IsEnabled && (PetState)type.GetField("_state", flags)!.GetValue(window)! == PetState.Idle,
                "single release waits for double-click window");
            type.GetMethod("CancelPointerInteraction", flags)!.Invoke(window, null);
            check(!timer.IsEnabled, "menu and drag cancellation clear pending click");
            type.GetField("_pointerDown", flags)!.SetValue(window, true);
            type.GetField("_doublePress", flags)!.SetValue(window, true);
            mouseUp.Invoke(window, new object[] { window, args });
            check(!timer.IsEnabled && (PetState)type.GetField("_state", flags)!.GetValue(window)! == PetState.Jumping,
                "double-click release triggers one jump");
        }
        finally
        {
            type.GetMethod("PrepareForApplicationShutdown", flags)!.Invoke(window, null);
            window.Close();
        }
    }

    private static void VerifyWindows(Action<bool, string> check, PetPackage pet, string? renderRoot)
    {
        var clock = new ManualClock();
        var session = new CompanionSession(clock);
        var settings = new PetSettings();
        var focus = new FocusWindow(session, settings, pet, () => settings.DoNotDisturb);
        try
        {
            check(((TextBlock)focus.FindName("TimeText")).Text == "25:00", "focus window displays initial duration");
            ((Button)focus.FindName("ToggleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(session.IsFocusing && ((TextBlock)focus.FindName("ToggleText")).Text == "暂停", "focus start command");
            ((Button)focus.FindName("ToggleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(session.Status == SessionStatus.Paused, "focus pause command");
            if (renderRoot is not null) Render(focus, renderRoot, "focus-paused.png", 364, 371);
        }
        finally { focus.Close(); }
        check(session.Status == SessionStatus.Paused, "closing focus panel preserves session");

        var catalog = new PetCatalog(Path.Combine(AppContext.BaseDirectory, "Pets"),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var window = new SettingsWindow(settings, true, catalog, pet);
        try
        {
            var collect = typeof(SettingsWindow).GetMethod("CollectCompanionSettings", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ((CheckBox)window.FindName("QuietHoursCheckBox")).IsChecked = true;
            ((TextBox)window.FindName("QuietStartInput")).Text = "25:80";
            check(!(bool)collect.Invoke(window, null)!, "settings reject malformed quiet times");
            ((TextBox)window.FindName("QuietStartInput")).Text = "23:30";
            ((TextBox)window.FindName("QuietEndInput")).Text = "07:15";
            ((Slider)window.FindName("FocusMinutesSlider")).Value = 45;
            ((CheckBox)window.FindName("SessionAnimationCheckBox")).IsChecked = false;
            check((bool)collect.Invoke(window, null)!, "settings accept valid overnight times");
            var draft = (PetSettings)typeof(SettingsWindow).GetField("_workingSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            check(draft.QuietStartMinute == 1410 && draft.QuietEndMinute == 435 && draft.FocusMinutes == 45 &&
                settings.FocusMinutes == 25, "settings edit draft without changing live timer preferences");
            check(!draft.SessionAnimationEnabled && settings.SessionAnimationEnabled, "linkage checkbox only edits settings draft");
            if (renderRoot is not null)
            {
                var tabs = (TabControl)window.FindName("SettingsTabs");
                tabs.SelectedItem = window.FindName("CompanionTab");
                Render(window, renderRoot, "companion-settings.png", 664, 561);
                tabs.SelectedIndex = 2;
                Render(window, renderRoot, "interaction-settings.png", 664, 561);
            }
        }
        finally { window.Close(); }
    }

    private static void Render(Window window, string directory, string name, int width, int height)
    {
        Directory.CreateDirectory(directory);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }

    private static DateTime At(int hour, int minute) => new(2026, 9, 7, hour, minute, 0);

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }
}
