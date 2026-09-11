using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YeShunguangPet;

internal static class DiagnosticsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check, PetCatalog source, string root)
    {
        var folder = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(folder);
        VerifyLogging(check, folder);
        VerifyRedaction(check, folder);
        VerifyFatalHandling(check);
        VerifyStartup(check, source, folder);
    }

    private static void VerifyLogging(Action<bool, string> check, string root)
    {
        var primary = Path.Combine(root, "primary");
        var secondary = Path.Combine(root, "secondary");
        var temp = Path.Combine(root, "temporary");
        File.WriteAllText(primary, "blocked directory");
        var log = new ResilientLog(new[] { primary, secondary, temp });
        check(log.Write("ERROR", "first failure"), "logging falls back when primary directory cannot be created");
        var snapshot = log.Capture();
        check(snapshot.ActiveLocation == 1 && snapshot.Failures.Length > 0 && File.ReadAllText(snapshot.ActivePath!).Contains("first failure"), "fallback records write status and preserves original error");
        using (var locked = new FileStream(snapshot.ActivePath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            check(log.Write("ERROR", "locked log failure") && log.Capture().ActiveLocation == 2, "locked log switches to the temporary fallback");
            check(log.Capture().Excerpts.Any(e => e.Text.Contains("locked log failure")), "reading locked logs still preserves current-session diagnostics");
        }
        File.Delete(primary);
        check(log.Write("INFO", "primary recovered") && log.Capture().ActiveLocation == 0, "logger returns to preferred directory when it becomes writable");
        var blocked = Path.Combine(root, "all-blocked");
        File.WriteAllText(blocked, "not a directory");
        var memoryLog = new ResilientLog(new[] { blocked }, maxMemoryChars: 1024);
        check(!memoryLog.Write("ERROR", "in-memory failure") && memoryLog.Capture().ActivePath is null, "all unwritable destinations are reported as memory-only");
        check(memoryLog.Capture().Excerpts.Single().Text.Contains("in-memory failure"), "memory-only mode keeps an exportable error record");
        for (var i = 0; i < 30; i++) memoryLog.Write("INFO", new string('x', 300) + i);
        check(memoryLog.Capture().Excerpts.Single().Text.Length <= 1024, "in-memory logging is bounded");

        var rotationDir = Path.Combine(root, "rolling");
        var rolling = new ResilientLog(new[] { rotationDir }, maxFileBytes: 1024);
        for (var i = 0; i < 20; i++) rolling.Write("INFO", "entry-" + i + new string('z', 120));
        check(File.Exists(Path.Combine(rotationDir, "app.previous.log")), "logs rotate during a running session rather than only at startup");
        check(Directory.GetFiles(rotationDir).Length == 2 && Directory.GetFiles(rotationDir).All(f => new FileInfo(f).Length <= 1024), "rolling logs bound disk usage to current and previous files");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            rolling.Write("INFO", "culture test");
            check(File.ReadAllText(Path.Combine(rotationDir, "app.log")).Contains(DateTime.Now.Year.ToString(CultureInfo.InvariantCulture) + "-"), "log timestamps use stable Gregorian formatting across cultures");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        var concurrent = new ResilientLog(new[] { Path.Combine(root, "concurrent") });
        Parallel.For(0, 40, i => concurrent.Write("INFO", "parallel-" + i.ToString("D3")));
        var recent = concurrent.Capture().Excerpts.Single(e => e.Name == "current-session").Text;
        check(Enumerable.Range(0, 40).All(i => recent.Contains("parallel-" + i.ToString("D3"))), "concurrent log writes preserve complete records");
    }

    private static void VerifyRedaction(Action<bool, string> check, string root)
    {
        var redactor = new DiagnosticRedactor(new[] { "PrivateComputer", "PrivateSkinName" });
        var samples = new Dictionary<string, string>
        {
            [@"Cannot read 'C:\Users\alice\Private Document\pet.json'."] = "alice",
            [@"in D:/agent/PrivateProject/App.cs:line 20"] = "PrivateProject",
            [@"Cannot read \\private-server\private-share\file.log"] = "private-server",
            ["Cannot read '/home/private-user/secret/file.json'"] = "private-user",
            ["Email private.person@example.com"] = "private.person",
            ["https://private.example.org/report?token=sensitive-value"] = "private.example.org",
            ["Authorization: Bearer sensitive-value"] = "sensitive-value",
            ["api_key=sk-abcdefghijklmnopqrstuv"] = "abcdefghijklmnopqrstuv",
            ["\"password\": \"private-password\""] = "private-password",
            ["token: secret-part-one\nsecond line"] = "secret-part-one",
            ["PrivateComputer PrivateSkinName"] = "Private"
        };
        foreach (var (input, secret) in samples)
            check(!redactor.Clean(input).Contains(secret, StringComparison.Ordinal), "diagnostic redaction removes " + secret);
        check(redactor.Clean("System.InvalidOperationException at YeShunguangPet.UiMotion.MoveToggle") ==
            "System.InvalidOperationException at YeShunguangPet.UiMotion.MoveToggle", "redaction retains useful exception types and application stack methods");
        var snapshot = new LogSnapshot(@"C:\Users\alice\logs\app.log", 1,
            new[] { new LogExcerpt("recent", "api_key=secret-value\nStack at Example.Method\nC:\\Users\\alice\\private-file.txt") }, new[] { "location-1: IOException" });
        var report = DiagnosticReport.Capture(null, snapshot, new IOException("Denied 'C:\\Users\\alice\\private-file.txt'"), "test-error");
        var zip = Path.Combine(root, "diagnostic.zip");
        report.Export(zip);
        using (var archive = ZipFile.OpenRead(zip))
        {
            check(archive.Entries.Select(e => e.FullName).OrderBy(x => x).SequenceEqual(new[] { "logs.txt", "README.txt", "report.json" }.OrderBy(x => x)), "diagnostic ZIP contains only the three allowlisted text files");
            using var reader = new StreamReader(archive.GetEntry("report.json")!.Open());
            var json = reader.ReadToEnd();
            using var parsed = JsonDocument.Parse(json);
            check(parsed.RootElement.GetProperty("Logging").GetProperty("Storage").GetString() == "fallback", "diagnostic JSON exposes storage mode without exposing the active path");
            check(!json.Contains("alice") && !report.LogsText.Contains("secret-value") && !report.LogsText.Contains("private-file"), "export removes private paths and credentials from both report and logs");
        }
        var before = File.ReadAllBytes(zip);
        var refused = false;
        try { report.Export(zip); } catch (IOException) { refused = true; }
        check(refused && File.ReadAllBytes(zip).SequenceEqual(before), "diagnostic export cannot overwrite an existing file without explicit approval");
        report.Export(zip, overwrite: true);
        check(Directory.GetFiles(root, "*.tmp").Length == 0, "diagnostic export cleans temporary files after success");
        var directoryTarget = Path.Combine(root, "directory.zip");
        Directory.CreateDirectory(directoryTarget);
        var failed = false;
        try { report.Export(directoryTarget, overwrite: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        check(failed && Directory.Exists(directoryTarget) && Directory.GetFiles(root, "*.tmp").Length == 0, "failed export preserves destination and cleans staging output");
        var noFile = Path.Combine(root, "protected.json");
        try { report.Export(noFile); } catch (IOException) { }
        check(!File.Exists(noFile), "diagnostic export cannot overwrite configuration-shaped paths");
    }

    private static void VerifyFatalHandling(Action<bool, string> check)
    {
        var coordinator = new FatalErrorCoordinator();
        var order = new List<string>();
        var secondary = new InvalidOperationException("secondary");
        coordinator.Run(new InvalidOperationException("original"),
            () => { order.Add("stop"); coordinator.Run(secondary, () => order.Add("bad"), () => order.Add("bad"), () => order.Add("bad"), _ => order.Add("bad")); },
            () => { order.Add("present"); coordinator.Run(secondary, () => order.Add("bad"), () => order.Add("bad"), () => order.Add("bad"), _ => order.Add("bad")); },
            () => order.Add("shutdown"), _ => order.Add("log"));
        check(order.SequenceEqual(new[] { "log", "stop", "present", "shutdown" }), "fatal errors stop activity before presenting exactly one dialog despite reentrancy");
        check(coordinator.IsHandling && !coordinator.Run(secondary, () => { }, () => { }, () => { }, _ => { }), "fatal error guard remains closed through shutdown");
        var errors = 0;
        var shutdowns = 0;
        new FatalErrorCoordinator().Run(secondary, () => throw new IOException("cleanup failed"), () => throw new IOException("dialog failed"),
            () => shutdowns++, _ => errors++);
        check(errors == 3 && shutdowns == 1, "cleanup and dialog failures cannot prevent the final shutdown attempt");
        var actions = 0;
        new FatalErrorCoordinator().Run(secondary, () => actions++, () => actions++, () => actions++, _ => throw new IOException("logger failed"));
        check(actions == 3, "logging failures cannot replace fatal error handling");
        var parallel = new FatalErrorCoordinator();
        var prompts = 0;
        Parallel.For(0, 30, _ => parallel.Run(secondary, () => { }, () => Interlocked.Increment(ref prompts), () => { }, _ => { }));
        check(prompts == 1, "concurrent fatal signals claim only one error report");
    }

    private static void VerifyStartup(Action<bool, string> check, PetCatalog source, string root)
    {
        var configPath = Path.Combine(root, "fresh", "desktop-v2.json");
        var legacyPath = Path.Combine(root, "fresh", "settings.json");
        var store = new DesktopSettingsStore(configPath, legacyPath);
        var config = store.Load();
        check(config.Pets.Count == 1 && config.Pets[0].PetId == PetPackage.DefaultId && !File.Exists(configPath), "first launch creates default state without writing during load");
        var catalog = new PetCatalog(source.BundledDirectory, Path.Combine(root, "fresh-users"));
        using (var desktop = new DesktopSession(config, catalog, store.Save, nativeIntegration: false))
        {
            desktop.Start(showWindows: false);
            check(File.Exists(configPath) && !File.Exists(legacyPath) && desktop.Windows.Count == 1, "first desktop startup persists one role without fabricating legacy settings");
            var saved = File.ReadAllBytes(configPath);
            var instance = desktop.Windows.Single();
            var settings = (PetSettings)typeof(MainWindow).GetField("_settings", Private)!.GetValue(instance)!;
            settings.Scale = 2;
            var log = new ResilientLog(Array.Empty<string>());
            log.Write("ERROR", "diagnostic failure");
            var report = DiagnosticReport.Capture(desktop, log.Capture());
            using var reportJson = JsonDocument.Parse(report.InformationJson);
            check(!report.InformationJson.Contains("InstanceId") && !report.InformationJson.Contains("PetId") && !report.InformationJson.Contains("Left") && !report.InformationJson.Contains("1680"), "runtime snapshot omits role identity and screen coordinates");
            check(reportJson.RootElement.GetProperty("Resources").GetProperty("SpriteImageLeases").GetInt32() > 0, "diagnostics include bounded resource usage without private configuration");
            typeof(DesktopSession).GetMethod("DisposeAfterFailure", Private)!.Invoke(desktop, null);
            check(File.ReadAllBytes(configPath).SequenceEqual(saved) && store.Load().Pets[0].Scale == 1, "fatal cleanup never overwrites last saved configuration with crashing state");
            check(desktop.Windows.Count == 0 && !instance.HasAnimationTimer && !instance.HasAmbientTimer && PetPackage.ActiveImageLeases == 0, "fatal cleanup releases all role activity and image leases");
        }
        var id = store.Load().Pets[0].InstanceId;
        using (var upgraded = new DesktopSession(store.Load(), catalog, store.Save, nativeIntegration: false))
        {
            upgraded.Start(false);
            check(upgraded.Windows.Single().InstanceId == id && upgraded.Windows.Count == 1, "restart and patch upgrade reuse the existing role identity");
        }
        var legacy = new PetSettings { Scale = 1.4, EdgeAutoHide = true, FocusMinutes = 38 };
        var upgradePath = Path.Combine(root, "upgrade-v2.json");
        var oldPath = Path.Combine(root, "upgrade-v1.json");
        File.WriteAllText(oldPath, JsonSerializer.Serialize(legacy));
        var oldBytes = File.ReadAllBytes(oldPath);
        var upgradeStore = new DesktopSettingsStore(upgradePath, oldPath);
        var migrated = upgradeStore.Load();
        using (var upgraded = new DesktopSession(migrated, catalog, upgradeStore.Save, nativeIntegration: false)) upgraded.Start(false);
        check(upgradeStore.Load().Pets[0].Scale == 1.4 && upgradeStore.Load().Companion.FocusMinutes == 38 && File.ReadAllBytes(oldPath).SequenceEqual(oldBytes), "legacy upgrade retains behavior and original v1 file end to end");
        File.WriteAllText(configPath, "{broken");
        var recovered = store.Load();
        check(recovered.Pets.Count == 1 && File.ReadAllText(configPath) == "{broken", "corrupt configuration recovers the last known-good backup without rewriting the original");
        File.Delete(store.BackupPath);
        File.WriteAllText(configPath, "{\"SchemaVersion\":999}");
        var rejected = false;
        try { store.Load(); } catch (InvalidDataException) { rejected = true; }
        check(rejected && File.ReadAllText(configPath).Contains("999"), "unsupported future configuration remains untouched");
    }
}
