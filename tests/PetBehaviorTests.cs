using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class PetBehaviorTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly PetActivityContext Active = new(true, true, false, false, false, true, false, false, false);
    private static readonly PetBehaviorCapabilities Capabilities = new(true, true, true, true);

    public static void Run(Action<bool, string> check)
    {
        VerifyPlayback(check);
        var clock = new QualityClock();
        var settings = new PetSettings { LookAtMouse = false, RandomIdleActions = true, IdleActionIntervalSeconds = 40, DesktopRoaming = false };
        var behavior = new PetBehaviorController(clock, new Random(7));
        behavior.Play(PetState.Idle, new[] { 100, 200 }, true);
        check(behavior.Evaluate(Active, settings, Capabilities).Activity == PetActivity.Idle, "behavior controller begins in idle");
        var delay = behavior.NextAmbientDelay(Active, settings, Capabilities)!.Value;
        check(delay.TotalSeconds is >= 30 and <= 50, "random-only activity sleeps until its jittered deadline");
        clock.Advance(delay.TotalMilliseconds - 1);
        clock.ShiftWall(TimeSpan.FromDays(8));
        check(behavior.ChooseAutomatic(Active, settings, Capabilities, false) == AutomaticPetAction.None, "wall-clock jumps cannot trigger automatic actions early");
        clock.Advance(1);
        check(behavior.ChooseAutomatic(Active, settings, Capabilities, false) == AutomaticPetAction.Gesture &&
            behavior.ChooseAutomatic(Active, settings, Capabilities, false) == AutomaticPetAction.None, "one due gesture is consumed without a catch-up burst");
        settings.LookAtMouse = true;
        check(behavior.NextAmbientDelay(Active, settings, Capabilities) == TimeSpan.FromMilliseconds(100), "pointer tracking keeps its bounded observation cadence");
        var quiet = behavior.Evaluate(Active with { Quiet = true }, settings, Capabilities);
        check(quiet.RunFrames && !quiet.Automatic && !quiet.Reminder && behavior.NextAmbientDelay(Active with { Quiet = true }, settings, Capabilities) is null,
            "quiet mode preserves idle animation without automatic wakeups");
        var focus = behavior.Evaluate(Active with { Focusing = true }, settings, Capabilities);
        check(!focus.Automatic && focus.SessionAnimation, "focus suppresses automatic activity but permits session visuals");
        settings.PauseDuringFocus = false;
        check(behavior.Evaluate(Active with { Focusing = true }, settings, Capabilities).Automatic, "focus pause preference is honored");
        settings.PauseDuringFocus = true;
        var due = new PetBehaviorController(clock, new Random(2));
        var automaticSettings = new PetSettings { LookAtMouse = false, RandomIdleActions = true, DesktopRoaming = true,
            IdleActionIntervalSeconds = 30, RoamIntervalSeconds = 30, PauseNearMouse = true };
        due.EnsureSchedule(automaticSettings, Capabilities); clock.Advance(60_000);
        check(due.ChooseAutomatic(Active, automaticSettings, Capabilities, false) == AutomaticPetAction.Roam, "due roaming takes priority over random gestures");
        check(due.ChooseAutomatic(Active, automaticSettings, Capabilities, true) == AutomaticPetAction.Gesture,
            "pointer-blocked roaming still permits a due idle gesture");
        check(due.NextAmbientDelay(Active, automaticSettings, Capabilities) == TimeSpan.FromMilliseconds(100), "pointer-blocked due movement cannot cause a tight timer loop");
        check(!behavior.Evaluate(Active, settings, Capabilities with { NeedsFrameTimer = false }).RunFrames, "static looping skins need no frame timer");
        check(behavior.NextAmbientDelay(Active, settings, new(false, false, false, false)) is null, "unsupported skin capabilities create no polling work");
        foreach (var context in new[] { Active with { Visible = false }, Active with { Loaded = false }, Active with { SettingsOpen = true },
                     Active with { PointerBusy = true }, Active with { Docked = true, DockAllowsAnimation = false } })
        {
            var plan = behavior.Evaluate(context, settings, Capabilities);
            check(!plan.Automatic && !plan.SessionAnimation && behavior.NextAmbientDelay(context, settings, Capabilities) is null,
                "busy/hidden/docked context blocks automatic and session work: " + plan.Activity);
        }
        behavior.BeginMenu();
        check(behavior.Evaluate(Active, settings, Capabilities).Activity == PetActivity.Menu && !behavior.StartRoaming(200, 200), "menu ownership prevents roaming");
        behavior.BeginDrag();
        check(behavior.Evaluate(Active with { SettingsOpen = true }, settings, Capabilities).Activity == PetActivity.Dragging, "dragging has deterministic priority over competing UI states");
        behavior.EndDrag(); behavior.EndMenu();
        behavior.Look(3);
        check(behavior.Evaluate(Active, settings, Capabilities).Activity == PetActivity.Looking && !behavior.Playback.IsRunning, "look direction pauses the frame clock");
        behavior.Play(PetState.Waving, new[] { 100, 100 }, false);
        check(!behavior.IsLooking && !behavior.Evaluate(Active, settings, Capabilities).SessionAnimation, "manual gesture owns animation until completion");
        behavior.SetSessionOwnership(true);
        check(behavior.Evaluate(Active, settings, Capabilities).SessionAnimation, "session-owned gestures remain eligible for completion updates");
        VerifyMotion(check, behavior, clock);
        VerifySequences(check);
        var player = new SpritePlayback(clock); player.Start(new[] { 100, 200 }, true);
        for (var i = 0; i < 1000; i++) player.Sample();
        var before = GC.GetAllocatedBytesForCurrentThread(); var sum = 0;
        for (var i = 0; i < 100_000; i++) sum += player.Sample().Frame;
        check(GC.GetAllocatedBytesForCurrentThread() - before < 1024, "one hundred thousand warm playback samples allocate less than one KiB");
        GC.KeepAlive(sum);
        behavior.Shutdown();
        behavior.Play(PetState.Idle, new[] { 100 }, true); behavior.BeginDrag(); behavior.BeginMenu(); behavior.Look(2);
        var stopped = behavior.Evaluate(Active, settings, Capabilities);
        check(stopped.Activity == PetActivity.Exited && !stopped.RunFrames && !stopped.RunMotion && !stopped.Automatic && !stopped.Click && !behavior.Playback.IsRunning,
            "shutdown is terminal and releases all activity permissions");
    }

    private static void VerifyPlayback(Action<bool, string> check)
    {
        var clock = new QualityClock(); var player = new SpritePlayback(clock);
        var durations = new[] { 100, 200, 50 }; player.Start(durations, true); durations[0] = 999;
        check(player.Sample() == new SpritePlaybackSample(0, false, TimeSpan.FromMilliseconds(100)), "playback owns immutable frame deadlines");
        clock.Advance(310);
        check(player.Sample() == new SpritePlaybackSample(2, false, TimeSpan.FromMilliseconds(40)), "late tick selects the current frame instead of advancing once");
        player.Pause(); clock.Advance(600_000);
        check(player.Sample().Frame == 2 && player.Sample().UntilNextFrame.TotalMilliseconds == 40, "hidden playback preserves its partial frame duration");
        player.Resume(); clock.Advance(40);
        check(player.Sample().Frame == 0 && player.Sample().UntilNextFrame.TotalMilliseconds == 100, "resuming reaches the correct loop boundary");
        clock.ShiftWall(TimeSpan.FromDays(-100));
        check(player.Sample().Frame == 0, "wall clock corrections cannot rewind sprite playback");
        player.Start(new[] { 100, 200, 50 }, false); clock.Advance(350);
        check(player.Sample().Completed && player.Sample().Frame == 2, "finite animation completes at the full final-frame duration");
        clock.Advance(86_400_000);
        check(player.Sample().Completed && player.Sample().UntilNextFrame == TimeSpan.Zero, "long stalls do not replay expired finite frames");
        player.Start(new[] { 100, 200, 50 }, true); clock.Advance(86_400_123);
        check(player.Sample().Frame == AnimationTimeline.FrameAt(new[] { 100, 200, 50 }, 86_400_123 % 350), "loop catch-up remains correct across a day-long stall");
        foreach (var invalid in new[] { Array.Empty<int>(), new[] { 19 }, new[] { 10001 }, new int[65] })
        {
            try { player.Start(invalid, false); check(false, "invalid playback input rejected"); }
            catch (ArgumentOutOfRangeException) { check(true, "invalid playback input rejected"); }
        }
    }

    private static void VerifyMotion(Action<bool, string> check, PetBehaviorController behavior, QualityClock clock)
    {
        check(!behavior.StartRoaming(10, 10), "insufficient desktop space cannot start a roam");
        check(behavior.StartRoaming(0, 500) && behavior.RoamDirection == 1 && behavior.RoamRemaining is >= 96 and <= 420, "roam chooses a feasible direction and bounded distance");
        clock.Advance(33); var step = behavior.AdvanceRoaming(0, 0, 500, 100, false);
        check(Math.Abs(step.Left - 3.3) < 0.0001, "roam distance uses monotonic elapsed time");
        var remaining = step.Remaining;
        clock.Advance(60_000); step = behavior.AdvanceRoaming(step.Left, 0, 500, 100, true);
        check(behavior.RoamPaused && step.Remaining == remaining, "pointer pause consumes no motion budget");
        clock.Advance(60_000); step = behavior.AdvanceRoaming(step.Left, 0, 500, 100, false);
        check(step.Left <= 13.301 && !behavior.RoamPaused, "resumed movement clamps catch-up to one hundred milliseconds");
        behavior.BeginMenu(); check(!behavior.IsRoaming, "menu transition cancels the roam state"); behavior.EndMenu();
        behavior.StartRoaming(300, 300); behavior.BeginDrag();
        check(!behavior.IsRoaming, "drag transition cancels the roam state"); behavior.EndDrag();
    }

    private static void VerifySequences(Action<bool, string> check)
    {
        for (var seed = 0; seed < 16; seed++)
        {
            var random = new Random(seed); var clock = new QualityClock();
            var behavior = new PetBehaviorController(clock, new Random(seed)); var settings = new PetSettings();
            var trace = new Queue<int>();
            for (var i = 0; i < 4000; i++)
            {
                var op = random.Next(10); trace.Enqueue(op); if (trace.Count > 20) trace.Dequeue();
                switch (op)
                {
                    case 0: behavior.BeginDrag(); break; case 1: behavior.EndDrag(); break;
                    case 2: behavior.BeginMenu(); break; case 3: behavior.EndMenu(); break;
                    case 4: behavior.StartRoaming(200, 200); break; case 5: behavior.StopRoaming(); break;
                    case 6: behavior.Look(random.Next(16)); break;
                    case 7: behavior.Play(PetState.Idle, new[] { 100, 200 }, true); break;
                    case 8: behavior.Play(PetState.Waving, new[] { 100, 200 }, false); break;
                    case 9: clock.Advance(random.Next(60_000)); clock.ShiftWall(TimeSpan.FromHours(random.Next(-24, 25))); break;
                }
                var context = Active with { Visible = random.Next(3) != 0, Quiet = random.Next(2) == 0, PointerBusy = random.Next(3) == 0,
                    SettingsOpen = random.Next(4) == 0, Docked = random.Next(4) == 0, DockAllowsAnimation = random.Next(2) == 0, Focusing = random.Next(2) == 0 };
                var plan = behavior.Evaluate(context, settings, Capabilities);
                var expectedAutomatic = context.Visible && !context.Quiet && !context.Focusing && !context.PointerBusy && !context.SettingsOpen && !context.Docked &&
                    !behavior.IsDragging && !behavior.IsMenuOpen && !behavior.IsRoaming && behavior.Animation == PetState.Idle;
                if (plan.Automatic != expectedAutomatic || (behavior.IsRoaming && (behavior.IsDragging || behavior.IsMenuOpen)) ||
                    (!context.Visible && (plan.RunFrames || plan.RunMotion || plan.SessionAnimation || plan.Reminder)))
                    throw new InvalidOperationException($"Behavior sequence failed: seed={seed}, step={i}, trace={string.Join(',', trace)}");
            }
            check(true, $"behavior seed {seed}: 4000 transitions preserve priority and activity invariants");
        }
    }

    public static void RunLive(Action<bool, string> check, PetCatalog catalog, string output)
    {
        var clock = new QualityClock();
        var config = DesktopConfiguration.Migrate(new PetSettings { Topmost = false, ClickThrough = true, LookAtMouse = true, RandomIdleActions = false });
        using var desktop = new DesktopSession(config, catalog, _ => { }, false, clock);
        desktop.Start(false); desktop.Add(PetPackage.DefaultId, false); desktop.Add(PetPackage.DefaultId, false);
        foreach (var window in desktop.Windows) { window.Opacity = 0; window.Show(); window.Left = window.Top = -30000; }
        FocusVisualGate.Pump(30);
        check(desktop.Windows.All(w => w.HasAnimationTimer && w.HasAmbientTimer), "visible animated roles schedule frames and pointer observation");
        foreach (var window in desktop.Windows) Invoke(window, "PlayAnimation", PetState.Idle, true);
        clock.Advance(500);
        foreach (var window in desktop.Windows) Invoke(window, "FrameTimer_Tick", null, EventArgs.Empty);
        foreach (var window in desktop.Windows)
        {
            var animation = window.Package.GetAnimation(PetState.Idle);
            var expected = AnimationTimeline.FrameAt(animation.DurationsMs, 500);
            check(ReferenceEquals(((Image)window.FindName("SpriteImage")).Source, window.Package.GetFrame(animation.Row, animation.StartColumn + expected)), "native delayed tick renders the monotonic frame for each role");
        }
        var frames = desktop.Windows.Select(w => w.Behavior.Playback.Sample()).ToArray();
        desktop.HideAll(); clock.Advance(60_000);
        check(desktop.Windows.All(w => !w.HasAnimationTimer && !w.HasAmbientTimer && !w.HasRoamingTimer && !w.HasDockTimer), "hidden roles stop every role timer");
        foreach (var window in desktop.Windows) window.Show();
        check(desktop.Windows.Select(w => w.Behavior.Playback.Sample()).SequenceEqual(frames), "showing a role resumes rather than fast-forwards its hidden animation");
        desktop.SetQuiet(true);
        check(desktop.Windows.All(w => w.HasAnimationTimer && !w.HasAmbientTimer), "native quiet roles retain animation without automatic polling");
        desktop.SetQuiet(false);
        foreach (var window in desktop.Windows)
        {
            var settings = (PetSettings)typeof(MainWindow).GetField("_settings", Private)!.GetValue(window)!;
            settings.LookAtMouse = false; settings.RandomIdleActions = true;
            Invoke(window, "ResetIdleBehaviorSchedule"); Invoke(window, "RefreshActivityTimers");
            var timer = (DispatcherTimer)typeof(MainWindow).GetField("_ambientTimer", Private)!.GetValue(window)!;
            check(timer.IsEnabled && timer.Interval.TotalSeconds > 20, "random-only native role sleeps until its actual deadline");
            Invoke(window, "BeginMenuInteraction");
            check(!window.HasAmbientTimer && window.ActivityPlan.Activity == PetActivity.Menu, "opening menu suspends automatic work immediately");
            Invoke(window, "EndMenuInteraction"); check(window.HasAmbientTimer, "closing menu rearms eligible work");
        }
        desktop.Companion.Session.StartOrResume();
        check(desktop.Windows.All(w => !w.HasAmbientTimer && w.Behavior.Animation == PetState.Running), "focus linkage follows controller priority across three roles");
        var selected = desktop.Windows.First();
        Invoke(selected, "PlayAnimation", PetState.Waving, true);
        clock.Advance(selected.Package.GetAnimation(PetState.Waving).DurationsMs.Sum());
        Invoke(selected, "FrameTimer_Tick", null, EventArgs.Empty);
        check(selected.Behavior.Animation == PetState.Running && selected.Behavior.SessionOwnsAnimation, "completed manual gesture returns to shared focus state");
        Directory.CreateDirectory(output);
        var root = (FrameworkElement)selected.Content; root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(selected.Width), (int)Math.Ceiling(selected.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        check(Enumerable.Range(0, bitmap.PixelWidth * bitmap.PixelHeight).Count(i => pixels[i * 4 + 3] > 0) > 20, "refactored native role renders nonblank original sprite pixels");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, "behavior-role.png")); encoder.Save(stream);
        Invoke(selected, "EnterDock", DockEdge.Left, new Rect(-30000, -30000, 1000, 1000), true);
        clock.Advance(200); Invoke(selected, "DockTimer_Tick", null, EventArgs.Empty);
        check(selected.IsDocked && !selected.HasAnimationTimer && !selected.HasAmbientTimer && selected.HasDockTimer,
            "collapsed native dock keeps only its handle observer running");
        var handle = (Border)selected.FindName("DockHandle"); var tooltip = handle.ToolTip;
        Invoke(selected, "DockTimer_Tick", null, EventArgs.Empty); Invoke(selected, "DockTimer_Tick", null, EventArgs.Empty);
        check(ReferenceEquals(tooltip, handle.ToolTip), "unchanged dock ticks do not rebuild visual labels");
        check(desktop.Companion.Session.IsFocusing, "docking preserves the shared focus session");
    }
    private static void Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
}
