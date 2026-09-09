using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using YeShunguangPet;

internal static class FocusControllerTests
{
    public static void Run(Action<bool, string> check)
    {
        var clock = new QualityClock();
        var settings = new PetSettings();
        var writes = 0; var fail = false;
        using var service = new CompanionService(settings, clock, persistDurations: draft =>
        {
            if (fail) throw new IOException("Injected persistence failure");
            check(settings.FocusMinutes != draft.FocusMinutes || settings.BreakMinutes != draft.BreakMinutes,
                "duration persistence precedes live adoption");
            writes++;
        });
        service.Configure();
        using var panel = new FocusPanelController(service.Session, settings, service.SetDurations, () => service.IsDisposed);
        check(panel.Snapshot().TimeText == "25:00" && panel.Presets.SequenceEqual(new[] { 15, 25, 45, 60 }), "controller presents initial timer without a window");
        panel.PreviewDuration(42);
        check(writes == 0 && settings.FocusMinutes == 25 && panel.DraftMinutes == 42, "duration preview has no persistence side effects");
        check(panel.SaveDuration() && writes == 1 && settings.FocusMinutes == 42, "explicit duration commit adopts persisted draft");
        panel.SaveDuration();
        check(writes == 1, "unchanged duration is not written twice");
        foreach (var text in new[] { "", "0", "121", "1.5", "-1", " 25", "99999999999999999999" })
        {
            panel.BeginMinuteEdit(); panel.EditMinuteText(text);
            check(!panel.EndMinuteEdit() && panel.HasInvalidInput && panel.MinuteText == text && panel.Error.Length > 0,
                "invalid draft is preserved for correction: " + text);
            check(!panel.Toggle() && !panel.SelectPhase(SessionPhase.Break) && service.Session.Status == SessionStatus.Ready,
                "invalid draft blocks start and phase switch");
        }
        panel.CancelMinuteEdit();
        check(panel.Error.Length == 0 && panel.MinuteText == "42", "cancel restores saved duration");
        fail = true;
        check(!panel.SelectPreset(60) && panel.DraftMinutes == 42 && service.Session.Duration.TotalMinutes == 42 && writes == 1,
            "failed save preserves live state and restores draft");
        check(!panel.Toggle(), "save error cannot silently start a reverted timer");
        fail = false;
        check(panel.SelectPreset(25) && panel.Toggle(), "save can be retried after failure");
        clock.Advance(10_000);
        var remaining = service.Session.Remaining;
        panel.BeginMinuteEdit(); panel.EditMinuteText("1"); panel.PreviewDuration(1);
        check(!panel.CanEdit && !panel.SelectPreset(1) && !panel.SelectPhase(SessionPhase.Break) && settings.FocusMinutes == 25,
            "running timer rejects duration and phase edits");
        panel.Toggle(); clock.Advance(10_000_000);
        check(panel.Snapshot().Status == SessionStatus.Paused && service.Session.Remaining == remaining, "controller pause survives sleep");
        panel.Toggle(); clock.Advance(remaining.TotalMilliseconds); service.Tick(); service.Tick();
        check(service.History.Records.Count == 1 && panel.Snapshot().CompletedSessions == 1 && panel.Snapshot().ToggleText == "开始休息",
            "completion records once and presents the next phase");
        panel.SelectPhase(SessionPhase.Break);
        check(panel.MaximumMinutes == 60 && panel.Presets.SequenceEqual(new[] { 5, 10, 15, 20 }), "break editor has independent bounds and presets");
        panel.BeginMinuteEdit(); panel.EditMinuteText("10");
        check(panel.CommitMinutes() && settings.BreakMinutes == 10 && settings.FocusMinutes == 25, "break edit preserves focus duration");
        var events = 0; panel.Changed += () => events++;
        panel.Dispose(); var before = events;
        service.Session.Reset();
        check(events == before && !panel.Toggle() && !panel.SelectPreset(15), "disposed controller detaches events and ignores commands");

        var session = service.Session;
        var reference = MakeDisposedPanel(session, settings);
        Collect();
        check(!reference.IsAlive, "disposed controller is collectible while its session stays alive");
        GC.KeepAlive(session);
        VerifyService(check);
    }

    private static void VerifyService(Action<bool, string> check)
    {
        var clock = new QualityClock();
        var settings = new PetSettings { FocusMinutes = 1, BreakMinutes = 1, QuietHoursEnabled = true,
            QuietStartMinute = 9 * 60, QuietEndMinute = 10 * 60, BreakRemindersEnabled = true, BreakReminderMinutes = 15 };
        using var service = new CompanionService(settings, clock);
        service.Configure();
        check(service.IsQuiet, "quiet hours use the injected local clock");
        var notifications = 0; var reminders = 0;
        service.Notification += (_, _) => notifications++;
        service.BreakReminderDue += () => reminders++;
        clock.Advance(900_000); service.Tick();
        check(notifications == 0 && reminders == 0, "quiet reminders are discarded");
        clock.ShiftWall(TimeSpan.FromHours(2)); service.Tick();
        check(!service.IsQuiet && notifications == 0, "wall clock change cannot replay suppressed reminders");
        clock.Advance(900_000); service.Tick(); service.Tick();
        check(notifications == 1 && reminders == 1, "service emits one due reminder");
        service.Session.StartOrResume(); clock.Advance(60_000); service.Tick();
        check(service.History.Records.Count == 1 && notifications == 2, "UI-free service records completion and notification");
        service.Dispose(); service.Dispose();
        service.Session.StartOrResume(); clock.Advance(60_000); service.Session.Tick();
        service.Session.StartOrResume(); clock.Advance(60_000); service.Session.Tick(); service.Tick();
        check(service.History.Records.Count == 1 && notifications == 2, "disposed service releases session subscriptions");
        try { service.SetDurations(2, 2); check(false, "disposed service rejects persistence"); }
        catch (ObjectDisposedException) { check(true, "disposed service rejects persistence"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeDisposedPanel(CompanionSession session, PetSettings settings)
    {
        var panel = new FocusPanelController(session, settings); panel.Dispose(); return new WeakReference(panel);
    }
    internal static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}

internal sealed class QualityClock : TimeProvider
{
    private long _ticks;
    private DateTimeOffset _wall = new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override DateTimeOffset GetUtcNow() => _wall;
    public void Advance(double milliseconds)
    {
        var elapsed = TimeSpan.FromMilliseconds(milliseconds);
        _ticks += elapsed.Ticks; _wall += elapsed;
    }
    public void ShiftWall(TimeSpan offset) => _wall += offset;
}
