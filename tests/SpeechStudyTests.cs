using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using YeShunguangPet;

internal static class SpeechStudyTests
{
    public static void Run(Action<bool, string> check, PetCatalog source, string root)
    {
        var folder = Path.Combine(root, "speech-study");
        Directory.CreateDirectory(folder);
        var original = source.LoadPreferred(PetPackage.DefaultId, out _);
        VerifySpeech(check, original, folder);
        VerifyHistory(check, source, folder);
    }

    private static void VerifySpeech(Action<bool, string> check, PetPackage pet, string root)
    {
        check(pet.Manifest.Speech is null && new SpeechOptions().Resolve(pet).Click.Length > 0, "old skins receive offline generic dialogue without modifying their files");
        var options = new SpeechOptions();
        options.Overrides[pet.Manifest.Id] = new SpeechLines { Click = new[] { "first", "second" } };
        var copy = options.Clone();
        copy.Overrides[pet.Manifest.Id].Click[0] = "changed";
        check(options.Overrides[pet.Manifest.Id].Click[0] == "first", "speech drafts do not mutate saved preferences");
        var clock = new ManualClock();
        var director = new SpeechDirector(clock);
        var first = director.Select(options, pet, SpeechEvent.Click, suppressed: false);
        check(first is not null && director.Select(options, pet, SpeechEvent.Click, false) is null, "speech cooldown suppresses repeated clicks");
        clock.Advance(31);
        check(director.Select(options, pet, SpeechEvent.Click, false) is { } second && second != first, "speech avoids immediately repeating the same line when alternatives exist");
        clock.Advance(31);
        check(director.Select(options, pet, SpeechEvent.Drag, false) is null, "empty event dialogue deliberately suppresses that event");
        check(director.Select(options, pet, SpeechEvent.Click, true) is null && director.Select(options, pet, SpeechEvent.Click, false) is not null, "quiet suppression does not consume the next eligible dialogue");
        options.Enabled = false;
        clock.Advance(31);
        check(director.Select(options, pet, SpeechEvent.Click, false) is null, "speech global switch disables all triggers");
        Bad(() => new SpeechLines { Click = new[] { new string('x', 81) } }.Validate(), check, "speech rejects overlong lines");
        Bad(() => new SpeechLines { Click = Enumerable.Repeat("x", 9).ToArray() }.Validate(), check, "speech limits alternatives per event");
        Bad(() => new SpeechLines { Click = new[] { "a\nb" } }.Validate(), check, "speech rejects embedded control characters");
        var store = new DesktopSettingsStore(Path.Combine(root, "prefs.json"), Path.Combine(root, "legacy.json"));
        var config = DesktopConfiguration.Migrate(new PetSettings());
        config.Speech = options;
        store.Save(config);
        check(!store.Load().Speech.Enabled && store.Load().Speech.Overrides[pet.Manifest.Id].Click[0] == "first", "speech preferences roundtrip through existing desktop configuration");

        // Use the already decoded image so the fixture cannot alter any bundled pixels.
        var json = JsonSerializer.SerializeToNode(pet.Manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<PetState>(JsonNamingPolicy.CamelCase) } })!.AsObject();
        json["speech"] = JsonNode.Parse("{\"click\":[\"skin line\"],\"drag\":[],\"focusCompleted\":[],\"breakCompleted\":[]}");
        var method = typeof(PetPackage).GetMethod("WithManifest", BindingFlags.Static | BindingFlags.NonPublic)!;
        var withSpeech = (PetPackage)method.Invoke(null, new object[] { System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), pet.SpriteSheet })!;
        check(new SpeechOptions().Resolve(withSpeech).Click.Single() == "skin line", "optional manifest dialogue overrides generic lines");
        check(ReferenceEquals(withSpeech.SpriteSheet, pet.SpriteSheet), "manifest dialogue leaves original sprite pixels unchanged");
        var writeManifest = typeof(PetPackage).GetMethod("SerializeManifest", BindingFlags.Static | BindingFlags.NonPublic)!;
        using (var parsed = JsonDocument.Parse((byte[])writeManifest.Invoke(null, new object[] { withSpeech.Manifest })!))
            check(parsed.RootElement.GetProperty("speech").GetProperty("click")[0].GetString() == "skin line", "skin editor serialization retains optional dialogue");
        json["speech"]!["unknownTrigger"] = new JsonArray("unknown");
        try { method.Invoke(null, new object[] { System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), pet.SpriteSheet }); throw new Exception("accepted invalid speech"); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { check(true, "skin manifest rejects unknown speech events"); }
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            var work = new Rect(-1000 * scale, 0, 1000 * scale, 700 * scale);
            foreach (var anchor in new[] { new Rect(work.Left, 0, 100 * scale, 120 * scale), new Rect(-100 * scale, 550 * scale, 100 * scale, 120 * scale), new Rect(-600 * scale, 200 * scale, 100 * scale, 120 * scale) })
            {
                var placement = SpeechPlacement.Place(anchor, new Size(276 * scale, 140 * scale), work, 8 * scale);
                check(placement is Rect bounds && work.Contains(bounds) && !bounds.IntersectsWith(anchor), $"speech placement stays visible and avoids its pet at {scale:0.0}x DPI");
            }
        }
        check(SpeechPlacement.Place(new Rect(0, 0, 100, 100), new Size(90, 60), new Rect(0, 0, 100, 100)) is null, "speech is skipped when no non-overlapping space exists");
    }

    private static void VerifyHistory(Action<bool, string> check, PetCatalog source, string root)
    {
        var path = Path.Combine(root, "study-history.json");
        var history = new StudyHistory(path);
        var clock = new ManualClock();
        using var runtime = new CompanionRuntime(new PetSettings { FocusMinutes = 1, BreakMinutes = 1, DoNotDisturb = true }, clock, history);
        runtime.Configure();
        runtime.Session.StartOrResume();
        var remaining = runtime.Session.Remaining;
        history.SetGoal(15);
        check(runtime.Session.IsFocusing && runtime.Session.Remaining == remaining, "changing the daily goal does not reset an active focus session");
        clock.Advance(20);
        runtime.Session.Pause();
        clock.Advance(3600);
        runtime.Tick();
        check(history.Records.Count == 0, "paused focus does not manufacture completed study records");
        runtime.Session.StartOrResume();
        clock.Advance(40);
        runtime.Tick();
        runtime.Tick();
        check(history.Records.Count == 1 && history.Records[0].DurationSeconds == 60, "completed focus records exactly one active duration even in quiet mode");
        check(!history.Record(history.Records[0]) && history.Records.Count == 1, "study record IDs prevent duplicate persistence");
        check(new StudyHistory(path).Records.Count == 1, "completed study survives application restart");
        runtime.Session.StartOrResume();
        clock.Advance(60);
        runtime.Tick();
        check(history.Records.Count == 1, "break completion is not counted as focus");
        runtime.Session.StartOrResume();
        clock.Advance(10);
        runtime.Session.Reset();
        check(history.Records.Count == 1, "cancelled focus is not recorded");
        history.SetGoal(90);
        check(new StudyHistory(path).DailyGoalMinutes == 90 && File.Exists(path + ".bak"), "daily goal persists with an atomic previous-version backup");
        check(history.Totals(history.Records[0].Day).Minutes == 1 && history.Week(history.Records[0].Day).Sum(d => d.Sessions) == 1, "daily and weekly statistics derive from persisted completions");
        var atMidnight = new ManualClock { Utc = new DateTimeOffset(2026, 9, 8, 23, 59, 30, TimeSpan.Zero) };
        using var crossing = new CompanionRuntime(new PetSettings { FocusMinutes = 1 }, atMidnight, history);
        crossing.Configure();
        crossing.Session.StartOrResume();
        atMidnight.Advance(60);
        crossing.Tick();
        check(history.Records.Last().Day == new DateOnly(2026, 9, 9), "cross-midnight focus is counted on its completion day");
        crossing.Session.StartOrResume();
        atMidnight.Advance(90000);
        crossing.Tick();
        check(history.Records.Count == 2, "long sleep cannot add multiple focus records or count a break");
        var offsetRecord = new FocusCompletion(Guid.NewGuid().ToString("N"), new DateTimeOffset(2026, 9, 10, 0, 10, 0, TimeSpan.FromHours(8)), 1500);
        history.Record(offsetRecord);
        check(new StudyHistory(path).Records.Last().Day == new DateOnly(2026, 9, 10), "stored local completion date does not shift with current machine timezone");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            history.Record(new FocusCompletion(Guid.NewGuid().ToString("N"), clock.GetLocalNow(), 60));
            check(history.LastError is not null && history.HasPendingChanges && history.Records.Count == 4, "failed history save keeps the new completion in memory with an explicit warning");
        }
        check(history.RetrySave() && !history.HasPendingChanges && new StudyHistory(path).Records.Count == 4, "history retry persists pending completions without duplication");
        var corruptPath = Path.Combine(root, "corrupt-study.json");
        File.WriteAllText(corruptPath, "{broken");
        var corrupt = new StudyHistory(corruptPath);
        corrupt.Record(new FocusCompletion(Guid.NewGuid().ToString("N"), clock.GetLocalNow(), 60));
        check(corrupt.LastError is not null && corrupt.HasPendingChanges && File.ReadAllText(corruptPath) == "{broken", "corrupt history is never silently overwritten by a new session");
        File.Copy(path, corruptPath, true);
        check(corrupt.RetrySave() && new StudyHistory(corruptPath).Records.Count == 5, "restored history merges unsaved new records by identity");
        var multi = new StudyHistory();
        var state = DesktopConfiguration.Migrate(new PetSettings { FocusMinutes = 1 });
        using (var desktop = new DesktopSession(state, new PetCatalog(source.BundledDirectory, Path.Combine(root, "users")), _ => { }, false, clock, multi))
        {
            desktop.Start(false);
            desktop.Add(PetPackage.DefaultId, false);
            desktop.Add(PetPackage.DefaultId, false);
            desktop.Companion.Session.StartOrResume();
            clock.Advance(60);
            desktop.Companion.Tick();
            check(multi.Records.Count == 1, "three desktop pets share a single study journal completion");
        }
        check(Directory.GetFiles(root, "*.tmp").Length == 0, "study writes clean temporary staging files after failures and retries");
        check(!history.Record(new FocusCompletion("invalid", clock.GetLocalNow(), 60)) && history.LastError is not null, "invalid completion metadata reports a nonfatal recording error");
    }
    private static void Bad(Action action, Action<bool, string> check, string name)
    {
        try { action(); } catch (InvalidDataException) { check(true, name); return; }
        throw new Exception("Unexpected success: " + name);
    }
    internal sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public DateTimeOffset Utc { get; set; } = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(int seconds) { _ticks += TimeSpan.FromSeconds(seconds).Ticks; Utc = Utc.AddSeconds(seconds); }
    }
}
