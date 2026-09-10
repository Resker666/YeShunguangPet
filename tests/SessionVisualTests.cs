using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class SessionVisualTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Action<bool, string> check, PetPackage pet, PetPackage minimal)
    {
        var clock = new ManualClock();
        var session = new CompanionSession(clock);
        var visuals = new SessionVisuals(clock);
        var settings = new PetSettings();
        PetState? Resolve(bool available = true, bool quiet = false, PetPackage? skin = null)
            => visuals.Resolve(session, settings, skin ?? pet, quiet, available);
        session.Configure(1, 1);
        check(Resolve() == PetState.Idle, "ready avatar is idle");
        session.StartOrResume();
        check(Resolve() == PetState.Running, "focus uses working animation");
        check(Resolve(skin: minimal) == PetState.Idle, "missing working animation falls back to idle");
        check(Resolve(quiet: true) == PetState.Idle, "quiet mode suppresses linked work");
        settings.SessionAnimationEnabled = false;
        check(Resolve() == PetState.Idle, "linkage can be disabled");
        check(!JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(settings.Clone()))!.SessionAnimationEnabled,
            "linkage preference survives clone and JSON");
        check(JsonSerializer.Deserialize<PetSettings>("{}")!.SessionAnimationEnabled, "old settings enable linkage by default");
        settings.SessionAnimationEnabled = true;
        session.Pause();
        check(Resolve() == PetState.Idle, "pause restores idle");
        session.StartOrResume();
        check(Resolve() == PetState.Running, "resume restores work");
        clock.Advance(60);
        session.Tick();
        visuals.RequestCompletion();
        check(Resolve(false) is null && !visuals.IsCompletionActive, "busy or hidden pet defers completion without taking control");
        clock.Advance(2);
        check(Resolve() == PetState.Waving && visuals.IsCompletionActive, "available pet acknowledges completion");
        clock.Advance(2.9);
        check(Resolve() == PetState.Waving, "completion remains visible for three seconds");
        clock.Advance(0.1);
        check(Resolve() == PetState.Idle && !visuals.IsCompletionActive, "completion returns to idle after bounded duration");
        visuals.RequestCompletion();
        Resolve(false);
        clock.Advance(10);
        check(Resolve() == PetState.Idle, "hidden completion expires without later surprise");
        visuals.RequestCompletion();
        check(Resolve(skin: minimal) == PetState.Idle, "missing completion animations safely fall back");
        visuals.RequestCompletion();
        check(Resolve(quiet: true) == PetState.Idle && Resolve() == PetState.Idle, "quiet completion discarded without backlog");
        settings.NotificationsEnabled = false;
        visuals.RequestCompletion();
        check(Resolve() == PetState.Idle, "notification preference suppresses completion gesture");
        settings.NotificationsEnabled = true;
        visuals.RequestCompletion();
        session.StartOrResume();
        check(session.Phase == SessionPhase.Break && Resolve() == PetState.Idle, "break is idle and cancels preceding completion cue");
        clock.Advance(60);
        session.Tick();
        visuals.RequestCompletion();
        check(Resolve() == PetState.Waving, "break completion acknowledges once");
        session.Reset();
        check(Resolve() == PetState.Idle, "reset cancels acknowledgement");

        VerifyFocusWindow(check, pet, minimal);
        VerifyDesktopReturn(check, pet);
    }

    private static void VerifyFocusWindow(Action<bool, string> check, PetPackage pet, PetPackage minimal)
    {
        var clock = new ManualClock();
        var session = new CompanionSession(clock);
        session.Configure(1, 1);
        var settings = new PetSettings();
        var panel = new FocusWindow(session, settings, pet, () => settings.DoNotDisturb);
        var type = typeof(FocusWindow);
        PetState State() => ((PetAnimation)type.GetField("_avatarAnimation", Private)!.GetValue(panel)!).State;
        try
        {
            session.StartOrResume();
            check(State() == PetState.Running, "focus panel observes running session");
            var before = ((Image)panel.FindName("PetImage")).Source;
            type.GetMethod("AvatarTimer_Tick", Private)!.Invoke(panel, new object?[] { null, EventArgs.Empty });
            check(!ReferenceEquals(before, ((Image)panel.FindName("PetImage")).Source), "focus avatar advances frames");
            var timer = (DispatcherTimer)type.GetField("_avatarTimer", Private)!.GetValue(panel)!;
            check(timer.Interval == TimeSpan.FromMilliseconds(pet.GetAnimation(PetState.Running).DurationsMs[1]), "avatar respects manifest timing");
            session.Pause();
            check(State() == PetState.Idle, "focus panel idle on pause");
            session.StartOrResume();
            clock.Advance(60);
            session.Tick();
            check(State() == PetState.Waving, "focus panel receives completion gesture");
            settings.DoNotDisturb = true;
            type.GetMethod("RefreshDisplay", Private)!.Invoke(panel, null);
            check(State() == PetState.Idle, "focus panel quiet toggle cancels gesture");
            settings.DoNotDisturb = false;
            session.Reset();
            session.StartOrResume();
            panel.UpdatePet(minimal);
            check(State() == PetState.Idle && ((Image)panel.FindName("PetImage")).Source is BitmapSource { PixelWidth: 16 }, "focus avatar switches skin and clears stale frames");
            panel.WindowState = WindowState.Minimized;
            check(!timer.IsEnabled && session.IsFocusing, "minimized panel stops avatar but keeps session");
            panel.Close();
            check(!timer.IsEnabled, "closing panel stops avatar timer");
        }
        finally { panel.Close(); }
    }

    private static void VerifyDesktopReturn(Action<bool, string> check, PetPackage pet)
    {
        var clock = new ManualClock();
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()),
            new PetCatalog(Path.Combine(AppContext.BaseDirectory, "Pets"), Path.Combine(Path.GetTempPath(), "pet-session-" + Guid.NewGuid().ToString("N"))), _ => { }, false, clock);
        desktop.Start(false);
        var window = desktop.Windows.First();
        var type = typeof(MainWindow);
        try
        {
            type.GetField("_pet", Private)!.SetValue(window, pet);
            var session = (CompanionSession)type.GetField("_focusSession", Private)!.GetValue(window)!;
            var settings = (PetSettings)type.GetField("_settings", Private)!.GetValue(window)!;
            settings.SessionAnimationEnabled = true;
            settings.DoNotDisturb = settings.QuietHoursEnabled = false;
            session.StartOrResume();
            type.GetMethod("PlayRestingAnimation", Private)!.Invoke(window, null);
            check(window.Behavior.Animation == PetState.Running, "desktop resting state follows focus");
            type.GetMethod("PlayAnimation", Private)!.Invoke(window, new object[] { PetState.Waving, true });
            check(!window.Behavior.SessionOwnsAnimation, "manual gesture releases session ownership");
            window.Behavior.Playback.Resume();
            clock.Advance(pet.GetAnimation(PetState.Waving).DurationsMs.Sum() / 1000.0);
            type.GetMethod("FrameTimer_Tick", Private)!.Invoke(window, new object?[] { null, EventArgs.Empty });
            check(window.Behavior.Animation == PetState.Running, "manual gesture returns to focus work");
            session.Pause();
            type.GetMethod("PlayRestingAnimation", Private)!.Invoke(window, null);
            check(window.Behavior.Animation == PetState.Idle, "desktop returns to idle after pause");
        }
        finally
        {
            type.GetMethod("PrepareForApplicationShutdown", Private)!.Invoke(window, null);
            window.Close();
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
