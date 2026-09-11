using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class DesktopTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Action<bool, string> check, PetCatalog previous, PetEntry custom, string root, string? renders)
    {
        var statePath = Path.Combine(root, "desktop-store", "desktop-v2.json");
        var legacyPath = Path.Combine(root, "legacy-settings.json");
        var legacy = new PetSettings { SelectedPetId = "robin", Left = -700, Top = 88, Scale = 1.4, EdgeAutoHide = true,
            Topmost = false, ClickThrough = true, FocusMinutes = 48, BreakMinutes = 9, DoNotDisturb = true, QuietHoursEnabled = true };
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(legacy), new UTF8Encoding(true));
        var originalLegacy = File.ReadAllBytes(legacyPath);
        var store = new DesktopSettingsStore(statePath, legacyPath);
        var state = store.Load();
        check(state.Pets.Count == 1 && state.Pets[0].PetId == "robin" && state.Pets[0].Left == -700 && state.Pets[0].Scale == 1.4,
            "v1 migration preserves selected skin, negative position and scale");
        check(state.Pets[0].ClickThrough && state.Pets[0].EdgeAutoHide && !state.Pets[0].Topmost &&
            state.Companion.FocusMinutes == 48 && state.Companion.DoNotDisturb, "migration separates role options from global companion options");
        store.Save(state);
        check(File.ReadAllBytes(legacyPath).SequenceEqual(originalLegacy), "v2 migration never rewrites legacy settings");
        var firstBytes = File.ReadAllBytes(statePath);
        state.Pets.Add(new PetInstanceOptions { PetId = PetPackage.DefaultId, Hidden = true, Left = 450, Top = 120, Scale = 0.7 });
        store.Save(state);
        check(File.ReadAllBytes(statePath + ".bak").SequenceEqual(firstBytes), "atomic desktop save retains previous configuration");
        var reloaded = store.Load();
        check(reloaded.Pets.Count == 2 && reloaded.Pets[1].Hidden && reloaded.Pets[1].Scale == 0.7 &&
            reloaded.Pets[0].InstanceId != reloaded.Pets[1].InstanceId, "multiple instances roundtrip independently");
        reloaded.Pets.Clear();
        store.Save(reloaded);
        check(store.Load().Pets.Count == 0, "zero roles persist without recreating default role");
        File.WriteAllText(statePath, "{broken");
        var recovered = store.Load();
        check(recovered.Pets.Count == 2 && File.ReadAllText(statePath) == "{broken", "corrupt v2 config recovers the last known-good backup without overwriting the original");
        File.Delete(store.BackupPath);
        File.WriteAllText(statePath, "{}");
        Bad(() => store.Load(), check, "v2 file requires explicit schema version");
        state = DesktopConfiguration.Migrate(legacy);
        state.Pets.Add(new PetInstanceOptions { InstanceId = state.Pets[0].InstanceId });
        Bad(() => state.Validate(), check, "duplicate instance identity rejected");
        state = DesktopConfiguration.Migrate(legacy);
        state.Pets.AddRange(Enumerable.Range(0, 3).Select(_ => new PetInstanceOptions()));
        Bad(() => state.Validate(), check, "configuration cannot exceed three roles");

        var catalog = new PetCatalog(previous.BundledDirectory, Path.Combine(root, "desktop-users"));
        var imported = catalog.Import(custom.ManifestPath);
        var savedCount = 0;
        state = DesktopConfiguration.Migrate(new PetSettings { SelectedPetId = imported.Id, Left = 200, Top = 80 });
        state.Companion.FocusMinutes = state.Companion.BreakMinutes = 1;
        var clock = new ManualClock();
        using var desktop = new DesktopSession(state, catalog, _ => savedCount++, false, clock);
        desktop.Start(showWindows: false);
        var first = desktop.Windows.Single();
        var second = desktop.Add(imported.Id, show: false);
        var third = desktop.Add(PetPackage.DefaultId, show: false);
        check(desktop.Windows.Count == 3 && state.Pets.Count == 3, "desktop owns three independent role windows");
        Bad(() => desktop.Add(PetPackage.DefaultId, show: false), check, "fourth role rejected without creating window");
        check(desktop.Windows.Count == 3 && state.Pets.Count == 3, "failed fourth add leaves roster unchanged");
        check(ReferenceEquals(first.Package.SpriteSheet, second.Package.SpriteSheet) &&
            ReferenceEquals(first.Package.GetFrame(1, 1), second.Package.GetFrame(1, 1)), "same-skin instances share immutable image and frame");
        check(PetPackage.ActiveImageLeases == 3, "each active role retains one shared-image lease");
        VerifyCachePressure(check, first.Package, custom, root);
        var firstSettings = (PetSettings)typeof(MainWindow).GetField("_settings", Private)!.GetValue(first)!;
        var secondSettings = (PetSettings)typeof(MainWindow).GetField("_settings", Private)!.GetValue(second)!;
        check(!ReferenceEquals(firstSettings, secondSettings), "per-role mutable settings are not shared");
        firstSettings.Scale = 1.8;
        firstSettings.Left = 120;
        typeof(MainWindow).GetMethod("PersistSettings", Private)!.Invoke(first, null);
        check(state.Pets[0].Scale == 1.8 && state.Pets[1].Scale == 1 && state.Pets[0].Left == 120,
            "saving one role does not overwrite another role");
        foreach (var window in desktop.Windows) typeof(MainWindow).GetMethod("InitializeCompanion", Private)!.Invoke(window, null);
        check(ReferenceEquals(typeof(MainWindow).GetField("_focusSession", Private)!.GetValue(first),
            typeof(MainWindow).GetField("_focusSession", Private)!.GetValue(second)), "role windows share one focus session");
        var notifications = 0;
        desktop.Companion.Notification += (_, _) => notifications++;
        desktop.Companion.Session.StartOrResume();
        second.HideInstance();
        check(desktop.IsHidden(second) && !second.HasAnimationTimer && !second.HasAmbientTimer && !second.HasRoamingTimer &&
            !second.HasDockTimer && desktop.Companion.Session.IsFocusing, "hidden role stops its timers without stopping focus");
        Bad(() => catalog.Delete(imported, "not-current"), check, "skin used by any active role is protected from deletion");
        desktop.Remove(first);
        check(desktop.Windows.Count == 2 && desktop.Companion.Session.IsFocusing, "closing first role preserves other roles and shared focus");
        check(PetPackage.ActiveImageLeases == 2, "closing one role releases only its image lease");
        clock.Advance(60);
        desktop.Companion.Tick();
        desktop.Companion.Tick();
        check(notifications == 1 && desktop.Companion.Session.CompletedFocusSessions == 1, "one completion produces one notification across roles");
        desktop.SetQuiet(true);
        check(secondSettings.DoNotDisturb && state.Companion.DoNotDisturb && secondSettings.Scale == 1,
            "global quiet propagates without changing role geometry");
        desktop.Companion.Session.StartOrResume();
        clock.Advance(60);
        desktop.Companion.Tick();
        check(notifications == 1, "global quiet suppresses duplicate or delayed completion notifications");
        desktop.SetQuiet(false);
        desktop.Companion.Tick();
        check(notifications == 1, "leaving quiet does not replay notifications");
        if (renders is not null) RenderManager(desktop, renders);
        desktop.Remove(second);
        check(desktop.Companion.Session.Status == SessionStatus.Completed && desktop.Windows.Count == 1, "removing second role leaves global session intact");
        catalog.Delete(imported, "not-current");
        check(!catalog.Scan().Pets.Any(p => p.Id == imported.Id), "skin can be removed once all using roles are closed");
        desktop.Remove(third);
        check(state.Pets.Count == 0 && desktop.Windows.Count == 0, "last role can close while desktop controller survives");
        var refs = Enumerable.Range(0, 30).Select(_ => Lifecycle(desktop)).ToArray();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        check(refs.All(reference => !reference.IsAlive), "closed role windows are collectible after repeated lifecycle changes");
        var final = desktop.Add(PetPackage.DefaultId, show: false);
        var hiddenBeforeExit = state.Pets.Single().Hidden;
        var exited = 0;
        desktop.ExitRequested += () => exited++;
        desktop.RequestExit();
        check(exited == 1 && desktop.Windows.Count == 0 && state.Pets.Single().Hidden == hiddenBeforeExit,
            "exit closes all windows without converting visible roles into hidden roles");
        check(savedCount > 0 && !final.HasAnimationTimer && !final.HasAmbientTimer, "exit persists configuration and releases timers");
        check(PetPackage.ActiveImageLeases == 0, "all image leases released after desktop exit");
        VerifyCacheRefresh(check, custom, root);
        var failSave = false;
        var failureDesktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()),
            new PetCatalog(previous.BundledDirectory, Path.Combine(root, "failure-users")),
            _ => { if (failSave) throw new IOException("Simulated full disk."); }, false);
        failureDesktop.Start(false);
        failSave = true;
        var exitOnFailure = false;
        failureDesktop.ExitRequested += () => exitOnFailure = true;
        failureDesktop.RequestExit();
        check(exitOnFailure && failureDesktop.Windows.Count == 0 && PetPackage.ActiveImageLeases == 0,
            "shutdown releases windows and leases even when configuration save fails");
        failureDesktop.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Lifecycle(DesktopSession desktop)
    {
        var window = desktop.Add(PetPackage.DefaultId, show: false);
        typeof(MainWindow).GetMethod("InitializeCompanion", Private)!.Invoke(window, null);
        var reference = new WeakReference(window);
        desktop.Remove(window);
        return reference;
    }

    private static void VerifyCacheRefresh(Action<bool, string> check, PetEntry source, string root)
    {
        var folder = Path.Combine(root, "cache-refresh");
        Directory.CreateDirectory(folder);
        var manifestPath = Path.Combine(folder, "pet.json");
        var pngPath = Path.Combine(folder, "spritesheet.png");
        File.Copy(source.ManifestPath, manifestPath);
        File.Copy(Path.Combine(Path.GetDirectoryName(source.ManifestPath)!, "spritesheet.png"), pngPath);
        var old = PetPackage.Load(manifestPath);
        var oldFrame = old.Preview;
        var bitmap = new WriteableBitmap(old.SpriteSheet);
        bitmap.WritePixels(new Int32Rect(16, 16, 1, 1), new byte[] { 2, 4, 6, 255 }, 4, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var originalTime = File.GetLastWriteTimeUtc(pngPath);
        using (var stream = File.Create(pngPath)) encoder.Save(stream);
        File.SetLastWriteTimeUtc(pngPath, originalTime);
        var changed = PetPackage.Load(manifestPath);
        check(!ReferenceEquals(old.SpriteSheet, changed.SpriteSheet) && !ReferenceEquals(oldFrame, changed.Preview),
            "sprite cache detects changed pixels even when file timestamp is unchanged");
    }

    private static void VerifyCachePressure(Action<bool, string> check, PetPackage active, PetEntry source, string root)
    {
        var folder = Path.Combine(root, "cache-pressure");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "pet.json");
        File.Copy(source.ManifestPath, path);
        for (var i = 0; i < 80; i++)
        {
            var bitmap = new WriteableBitmap(active.SpriteSheet);
            bitmap.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { (byte)i, 61, 97, 255 }, 4, 0);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(folder, "spritesheet.png"))) encoder.Save(stream);
            _ = PetPackage.Load(path);
        }
        var after = PetPackage.Load(source.ManifestPath);
        check(ReferenceEquals(active.SpriteSheet, after.SpriteSheet), "cache eviction cannot duplicate an active role image");
    }

    private static void RenderManager(DesktopSession desktop, string folder)
    {
        var window = new PetManagerWindow(desktop);
        try
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(764, 471));
            root.Arrange(new Rect(0, 0, 764, 471));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(764, 471, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, 764, 471));
            bitmap.Render(background);
            bitmap.Render(root);
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, "pet-manager.png"));
            encoder.Save(stream);
        }
        finally { window.Close(); }
    }

    private static void Bad(Action action, Action<bool, string> check, string name)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { check(true, name); return; }
        throw new InvalidOperationException("Unexpectedly accepted: " + name);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(int seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
