using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using YeShunguangPet;

internal static class ShortcutTests
{
    internal sealed class FakeRegistrar : IShortcutRegistrar
    {
        public Dictionary<int, ShortcutGesture> Active { get; } = new();
        public HashSet<ShortcutGesture> Blocked { get; } = new();
        public int Registers { get; private set; }
        public bool Register(int id, ShortcutGesture gesture)
        {
            Registers++;
            if (Blocked.Contains(gesture) || Active.Values.Contains(gesture)) return false;
            Active.Add(id, gesture); return true;
        }
        public void Unregister(int id) => Active.Remove(id);
    }
    internal static object? Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static bool Rejects(Action action)
    {
        try { action(); return false; } catch (InvalidDataException) { return true; } catch (InvalidOperationException) { return true; }
    }
    public static void Run(Action<bool, string> check, PetCatalog catalog, string temporary)
    {
        var config = JsonSerializer.Deserialize<DesktopConfiguration>("{\"SchemaVersion\":2,\"Pets\":[]}")!; config.Validate();
        check(config.Shortcuts.RecallAll == new ShortcutGesture(3, 0x59) && config.Shortcuts.Active().Count() == 1, "old desktops retain only the original recall hotkey by default");
        check(new ShortcutGesture(7, 0x7A).ToString() == "Ctrl+Alt+Shift+F11" && new ShortcutGesture(3, 0x20).ToString() == "Ctrl+Alt+Space", "shortcut formatting is deterministic and uses no localized parsing");
        foreach (var gesture in new[] { new ShortcutGesture(0, 0x59), new(2, 0x59), new(8, 0x59), new(0x4003, 0x59), new(3, 0x7B), new(3, 0x2E), new(3, 0) })
            check(Rejects(gesture.Validate), "unsafe or unsupported shortcut is rejected: " + gesture);
        var first = new ShortcutGesture(3, 0x59); var second = new ShortcutGesture(6, 0x46); var third = new ShortcutGesture(7, 0x4D);
        var options = new ShortcutOptions { OpenFocus = second };
        var store = new DesktopSettingsStore(Path.Combine(temporary, "shortcuts.json"));
        config.Shortcuts = options; store.Save(config);
        check(store.Load().Shortcuts.Active().SequenceEqual(options.Active()), "shortcut options roundtrip through the existing desktop store");
        check(Rejects(() => new ShortcutOptions { OpenFocus = first }.Validate()), "duplicate enabled shortcuts cannot be saved");
        var native = new FakeRegistrar(); using var registry = new GlobalShortcuts(native);
        registry.Initialize(options);
        check(native.Active.Count == 2 && registry.IsRegistered(ShortcutAction.OpenFocus), "registry owns independent active shortcuts");
        var before = native.Active.ToArray(); var generation = registry.Generation; var saved = false;
        native.Blocked.Add(third);
        check(Rejects(() => registry.Apply(new ShortcutOptions { OpenFocus = new(3, 0x47), ToggleMini = third }, () => saved = true)), "system conflict aborts the complete draft");
        check(!saved && native.Active.OrderBy(x => x.Key).SequenceEqual(before.OrderBy(x => x.Key)) && registry.Generation == generation, "failed registration releases staged keys and preserves old routes");
        native.Blocked.Clear();
        check(Rejects(() => registry.Apply(new ShortcutOptions { OpenFocus = third }, () => throw new InvalidOperationException("disk blocked"))), "persistence failure is surfaced to the editor");
        check(native.Active.OrderBy(x => x.Key).SequenceEqual(before.OrderBy(x => x.Key)), "persistence failure leaves every original registration intact");
        var count = native.Registers;
        registry.Apply(new ShortcutOptions { RecallAll = second, OpenFocus = first }, () => saved = true);
        var oldFirstId = before.Single(x => x.Value == first).Key;
        check(saved && native.Registers == count && registry.TryResolve(oldFirstId, first, out var swapped) && swapped == ShortcutAction.OpenFocus, "swapping commands reuses native registrations without a conflict window");
        check(!registry.TryResolve(oldFirstId, third, out _) && !registry.TryResolve(-1, first, out _), "stale or mismatched native messages cannot resolve a command");
        registry.Apply(new ShortcutOptions { RecallAll = null }, () => { });
        check(native.Active.Count == 0 && !registry.IsRegistered(ShortcutAction.RecallAll), "all shortcuts can be disabled without disabling tray commands");
        native.Blocked.Add(first);
        using var startup = new GlobalShortcuts(native); startup.Initialize(options);
        check(!startup.IsRegistered(ShortcutAction.RecallAll) && startup.Failure(ShortcutAction.RecallAll) is not null && startup.IsRegistered(ShortcutAction.OpenFocus), "startup conflict is isolated while other commands remain available");
        native.Blocked.Clear(); startup.Apply(options, () => { });
        check(startup.IsRegistered(ShortcutAction.RecallAll) && startup.Failure(ShortcutAction.RecallAll) is null, "saving retries a previously occupied shortcut");
        startup.Dispose(); startup.Dispose();
        check(native.Active.Count == 0, "shortcut shutdown releases all owned keys and is idempotent");
        var fail = false;
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()), catalog, state => { if (fail) throw new IOException("simulated shortcut save failure"); store.Save(state); }, false, shortcutRegistrar: native);
        desktop.Start(false);
        desktop.UpdateShortcuts(options); fail = true;
        try { desktop.UpdateShortcuts(new ShortcutOptions { OpenFocus = third }); } catch (IOException) { }
        check(desktop.Configuration.Shortcuts.OpenFocus == second && store.Load().Shortcuts.OpenFocus == second && native.Active.Values.Contains(second) && !native.Active.Values.Contains(third), "desktop save failure preserves configuration, disk and native registrations together");
        fail = false;
        options.OpenFocus = third;
        check(desktop.Configuration.Shortcuts.OpenFocus == second, "external draft changes cannot mutate accepted shortcut settings");
        desktop.Dispose(); check(native.Active.Count == 0, "desktop shutdown disposes its shortcut registry");
        VerifyNativeConflict(check);
    }

    private static void VerifyNativeConflict(Action<bool, string> check)
    {
        var holder = new Window { ShowInTaskbar = false }; var target = new Window { ShowInTaskbar = false };
        var firstHandle = new WindowInteropHelper(holder).EnsureHandle(); var secondHandle = new WindowInteropHelper(target).EnsureHandle();
        ShortcutGesture? selected = null;
        try
        {
            for (uint key = 0x70; key <= 0x7A; key++)
                if (RegisterHotKey(firstHandle, 0x5A01, 0x4007, key)) { selected = new ShortcutGesture(7, key); break; }
            check(selected is not null, "native conflict test reserves one available test-only combination");
            using var registry = new GlobalShortcuts(new NativeTestRegistrar(secondHandle));
            var options = new ShortcutOptions { RecallAll = selected };
            registry.Initialize(options);
            check(!registry.IsRegistered(ShortcutAction.RecallAll) && registry.Failure(ShortcutAction.RecallAll) is not null, "real Windows registration reports a competing window's occupied key");
            UnregisterHotKey(firstHandle, 0x5A01);
            registry.Apply(options, () => { });
            check(registry.IsRegistered(ShortcutAction.RecallAll), "real Windows registration recovers after the competing owner releases its key");
            registry.Dispose();
            check(RegisterHotKey(firstHandle, 0x5A01, 0x4007, selected!.Key), "disposed registry returns its key to Windows");
        }
        finally { UnregisterHotKey(firstHandle, 0x5A01); holder.Close(); target.Close(); }
    }
    private sealed class NativeTestRegistrar(IntPtr handle) : IShortcutRegistrar
    {
        public bool Register(int id, ShortcutGesture gesture) => RegisterHotKey(handle, id, gesture.Modifiers | 0x4000, gesture.Key);
        public void Unregister(int id) => UnregisterHotKey(handle, id);
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr handle, int id);
}
