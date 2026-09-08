using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class Program
{
    private static int _passed;
    private static string _root = null!;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        _root = Path.Combine(Path.GetTempPath(), "YeShunguangPet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        try
        {
            var source = Path.GetFullPath(args[0]);
            var manifestPath = Path.Combine(source, "Pets", "YeShunguang", "pet.json");
            var original = PetPackage.Load(manifestPath);
            Check(original.Manifest.Id == PetPackage.DefaultId && original.Manifest.Animations.Count == 9, "default package loads");
            Check(original.SpriteSheet.PixelWidth == 1536 && original.SpriteSheet.PixelHeight == 2288, "original dimensions");
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(manifestPath)!, "spritesheet.png")))) ==
                "42FBB6129741A7468526AD0CAD27DF82EE3939BE140C768D3F431CC0C9A45C2D", "source pixels unchanged");
            Check(original.CanLook && original.CanRoam && original.RandomActions.Length == 2, "default capabilities");
            Check(original.GetAnimation(PetState.Idle).DurationsMs.SequenceEqual(new[] { 280, 110, 110, 140, 140, 320 }), "legacy idle timing");
            Check(original.Manifest.LookDirections[15] == new FrameLocation(10, 7), "look coordinates from manifest");
            using (var resources = new ResourceReader(typeof(MainWindow).Assembly.GetManifestResourceStream("YeShunguangPet.g.resources")!))
            {
                var keys = resources.GetEnumerator();
                var images = false;
                while (keys.MoveNext())
                    images |= keys.Key.ToString()!.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                Check(!images, "application assembly contains no embedded PNG");
            }
            foreach (var animation in original.Manifest.Animations.Keys.Select(original.GetAnimation))
                for (var i = 0; i < animation.FrameCount; i++)
                    _ = original.GetFrame(animation.Row, animation.StartColumn + i);
            Check(true, "all default animation frames decode");

            var fixture = CreateFixture();
            var custom = PetPackage.Load(fixture);
            Check(custom.Manifest.CellWidth == 16 && custom.Preview.PixelHeight == 16, "custom cell size");
            Check(!custom.CanLook && !custom.CanRoam && custom.RandomActions.Length == 0, "optional capabilities absent");
            Check(custom.GetAnimation(PetState.Waving).State == PetState.Idle, "missing action falls back to idle");
            Check(custom.GetAnimation(PetState.Idle).StartColumn == 1 && custom.GetAnimation(PetState.Idle).Row == 1, "nonzero row and first column");

            var old = JsonSerializer.Deserialize<PetSettings>("{\"Scale\":1.4,\"DesktopRoaming\":true}")!;
            Check(old.SelectedPetId == PetPackage.DefaultId && old.Scale == 1.4 && old.DesktopRoaming, "v1.1 settings migration");
            old.SelectedPetId = "custom";
            Check(old.Clone().SelectedPetId == "custom", "selection survives settings clone");
            Check(JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(old))!.SelectedPetId == "custom", "selection JSON roundtrip");

            var users = Path.Combine(_root, "users");
            var catalog = new PetCatalog(Path.Combine(source, "Pets"), users);
            var initialScan = catalog.Scan();
            Check(initialScan.Errors.Count == 0 && initialScan.Pets.Any(p => p.Id == PetPackage.DefaultId),
                "bundled skin catalog is valid", DescribeCatalog(initialScan));
            var expectedPetIds = initialScan.Pets.Select(p => p.Id).Append(custom.Manifest.Id).ToArray();
            var imported = catalog.Import(fixture);
            Check(imported.Id == custom.Manifest.Id, "import preserves skin id");
            CheckCatalog(catalog.Scan(), expectedPetIds, "import and catalog discovery");
            Check(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(fixture)!, "spritesheet.png")).SequenceEqual(
                File.ReadAllBytes(Path.Combine(users, imported.Id, "spritesheet.png"))), "import preserves image bytes");
            ExpectFailure(() => catalog.Import(fixture), "duplicate import does not overwrite");
            Check(!Directory.GetDirectories(users).Any(p => Path.GetFileName(p).StartsWith('.')), "no staging debris");
            Directory.Delete(Path.GetDirectoryName(fixture)!, true);
            Check(catalog.LoadPreferred(imported.Id, out var fallback).Manifest.Id == imported.Id && fallback is null, "import survives source removal");
            var missingPetId = "test-missing-" + Guid.NewGuid().ToString("N");
            Check(catalog.LoadPreferred(missingPetId, out fallback).Manifest.Id == PetPackage.DefaultId && fallback is not null, "missing selected skin fallback");

            VerifySettingsWindow(catalog, original, imported, expectedPetIds, args.Length > 1 ? args[1] : null);
            VerifyRuntimeSwitch(custom, original);
            SkinManagementTests.Run((success, name) => Check(success, name), catalog, imported, _root);
            EditorBackupTests.Run((success, name) => Check(success, name), catalog, imported, _root, args.Length > 1 ? args[1] : null);
            DesktopTests.Run((success, name) => Check(success, name), catalog, imported, _root, args.Length > 1 ? args[1] : null);
            SessionVisualTests.Run((success, name) => Check(success, name), original, custom);
            CompanionTests.Run((success, name) => Check(success, name), original, args.Length > 1 ? args[1] : null);
            EdgeDockTests.Run((success, name) => Check(success, name), original, args.Length > 1 ? args[1] : null);

            var importedJson = File.ReadAllText(imported.ManifestPath);
            void Bad(Action<JsonObject> edit, string name)
            {
                var node = JsonNode.Parse(importedJson)!.AsObject();
                edit(node);
                File.WriteAllText(imported.ManifestPath, node.ToJsonString());
                ExpectFailure(() => PetPackage.Load(imported.ManifestPath), name);
                File.WriteAllText(imported.ManifestPath, importedJson);
            }
            Bad(n => n["schemaVersion"] = 99, "unknown schema version");
            Bad(n => n["id"] = "../escape", "invalid id");
            Bad(n => n["id"] = "custom\n", "trailing newline in id");
            Bad(n => n["spriteSheet"] = "../outside.png", "parent traversal");
            Bad(n => n["spriteSheet"] = @"C:\outside.png", "absolute path");
            Bad(n => n["spriteSheet"] = "https://example.com/image.png", "remote image rejected");
            Bad(n => n["cellWidth"] = 32, "wrong image dimensions");
            Bad(n => n["rows"] = 65, "oversized grid");
            Bad(n => n["animations"] = null, "null animations");
            Bad(n => n["animations"]!["idle"] = null, "null required action");
            Bad(n => n["animations"]!["idle"]!["loop"] = false, "idle must loop");
            Bad(n => n["animations"]!["idle"]!["row"] = 2, "out of bounds row");
            Bad(n => n["animations"]!["idle"]!["startColumn"] = int.MaxValue, "out of bounds column");
            Bad(n => n["animations"]!["idle"]!["durationsMs"] = JsonNode.Parse("[0]"), "zero frame duration");
            Bad(n => n["animations"]!["idle"]!["durationsMs"] = JsonNode.Parse("[]"), "empty frames");
            Bad(n => n["animations"]!["idle"]!["durationsMs"] = JsonNode.Parse("[20,20]"), "frames exceeding columns");
            Bad(n => n["animations"]!["surprise"] = n["animations"]!["idle"]!.DeepClone(), "unknown animation");
            Bad(n => n["animations"]!["0"] = n["animations"]!["idle"]!.DeepClone(), "numeric animation alias");
            Bad(n => n["animations"]!["waving"] = n["animations"]!["idle"]!.DeepClone(), "random action cannot loop forever");
            Bad(n => n["lookDirections"] = JsonNode.Parse("[{\"row\":0,\"column\":0}]"), "partial direction list");
            Bad(n => n["lookDirections"] = JsonNode.Parse("[null]"), "null direction");
            Bad(n => n["description"] = new string('x', 401), "oversized description");
            Bad(n => n["name"] = new string('x', 65), "oversized name");
            File.WriteAllText(imported.ManifestPath, "{\"schemaVersion\":1,\"schemaVersion\":1}");
            ExpectFailure(() => PetPackage.Load(imported.ManifestPath), "duplicate JSON properties");
            File.WriteAllText(imported.ManifestPath, "broken");
            Check(catalog.Scan().Errors.Count > 0 && catalog.LoadPreferred(imported.Id, out fallback).Manifest.Id == PetPackage.DefaultId, "broken skin isolated from startup");
            var badCatalog = new PetCatalog(Path.Combine(source, "Pets"), Path.Combine(_root, "reject-target"));
            ExpectFailure(() => badCatalog.Import(imported.ManifestPath), "invalid import rejected");
            Check(!Directory.Exists(badCatalog.UserDirectory), "invalid import creates no user files");
            File.WriteAllText(imported.ManifestPath, importedJson, new System.Text.UTF8Encoding(true));
            Check(PetPackage.Load(imported.ManifestPath).Manifest.Id == imported.Id, "UTF8 BOM supported");
            File.WriteAllBytes(Path.Combine(users, imported.Id, "spritesheet.png"), new byte[] { 1, 2, 3 });
            ExpectFailure(() => PetPackage.Load(imported.ManifestPath), "invalid PNG");
            ExpectFailure(() => new PetCatalog(Path.Combine(_root, "empty"), Path.Combine(_root, "empty-user")).LoadPreferred("missing", out _), "no skins yields actionable failure");

            Console.WriteLine($"PASS: {_passed} checks");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string CreateFixture()
    {
        var path = Path.Combine(_root, "source");
        Directory.CreateDirectory(path);
        var pixels = new byte[32 * 32 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 50;
            pixels[i + 1] = (byte)(i / 4 % 32 * 7);
            pixels[i + 2] = 200;
            pixels[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 128);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(path, "spritesheet.png"))) encoder.Save(stream);
        var json = """
        {
          "schemaVersion": 1, "id": "custom", "name": "测试皮肤", "description": "自定义尺寸与动作",
          "spriteSheet": "spritesheet.png", "cellWidth": 16, "cellHeight": 16, "columns": 2, "rows": 2,
          "animations": { "idle": { "row": 1, "startColumn": 1, "durationsMs": [200], "loop": true } }
        }
        """;
        var manifest = JsonNode.Parse(json)!;
        manifest["id"] = "test-import-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(path, "pet.json"), manifest.ToJsonString());
        return Path.Combine(path, "pet.json");
    }

    private static void VerifySettingsWindow(PetCatalog catalog, PetPackage original, PetEntry imported,
        string[] expectedPetIds, string? renderRoot)
    {
        var settings = new PetSettings();
        var window = new SettingsWindow(settings, true, catalog, original);
        try
        {
            var selector = (ListBox)window.FindName("PetSelector");
            var displayedIds = selector.Items.Cast<PetEntry>().Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal);
            Check(displayedIds.SequenceEqual(expectedPetIds.OrderBy(id => id, StringComparer.Ordinal)),
                "skin selector shows bundled and imported skins",
                $"Expected: {string.Join(", ", expectedPetIds)}; displayed: {string.Join(", ", displayedIds)}");
            selector.SelectedValue = imported.Id;
            Check(((Image)window.FindName("SkinPreview")).Source is BitmapSource { PixelWidth: 16 }, "preview switches dimensions");
            Check(!((CheckBox)window.FindName("LookAtMouseCheckBox")).IsEnabled &&
                !((CheckBox)window.FindName("DesktopRoamingCheckBox")).IsEnabled, "unavailable capabilities disabled");
            Check(settings.SelectedPetId == PetPackage.DefaultId, "preview does not change live selection");
            Check(((Button)window.FindName("ExportPetButton")).IsEnabled &&
                ((Button)window.FindName("UpdatePetButton")).IsEnabled &&
                ((Button)window.FindName("DeletePetButton")).IsEnabled, "imported skin management controls enabled");
            var manifestText = File.ReadAllText(imported.ManifestPath);
            File.WriteAllText(imported.ManifestPath, "broken");
            typeof(SettingsWindow).GetMethod("Save_Click", PrivateInstance)!.Invoke(window, new object?[] { null, new RoutedEventArgs() });
            Check(window.Result is null && !string.IsNullOrEmpty(((TextBlock)window.FindName("SkinStatus")).Text),
                "changed invalid file blocks save while preserving live settings");
            File.WriteAllText(imported.ManifestPath, manifestText);
            selector.SelectedValue = PetPackage.DefaultId;
            Check(!((Button)window.FindName("UpdatePetButton")).IsEnabled &&
                !((Button)window.FindName("DeletePetButton")).IsEnabled, "bundled skin management controls protected");
            if (renderRoot is not null)
            {
                Render(window, renderRoot, "skins-default.png");
                selector.SelectedValue = imported.Id;
                Render(window, renderRoot, "skins-custom.png");
            }
            var before = window.Result;
            window.Close();
            Check(before is null && settings.SelectedPetId == PetPackage.DefaultId, "cancel keeps live settings");
        }
        finally { window.Close(); }
    }

    private static void VerifyRuntimeSwitch(PetPackage custom, PetPackage original)
    {
        var window = new MainWindow();
        try
        {
            var type = typeof(MainWindow);
            var field = type.GetField("_pet", PrivateInstance)!;
            var cache = (Dictionary<(int, int), BitmapSource>)type.GetField("_frameCache", PrivateInstance)!.GetValue(window)!;
            var animation = type.GetMethod("PlayAnimation", PrivateInstance)!;
            var frame = type.GetMethod("GetFrame", PrivateInstance)!;
            field.SetValue(window, original);
            animation.Invoke(window, new object[] { PetState.Idle, true });
            Check(((BitmapSource)frame.Invoke(window, new object[] { 0, 0 })!).PixelWidth == 192, "original runtime frame");
            // Exercise the same package adoption used by ApplySettings without persisting user preferences.
            type.GetMethod("AdoptPackage", PrivateInstance)!.Invoke(window, new object[] { custom });
            animation.Invoke(window, new object[] { PetState.Idle, true });
            Check(cache.Keys.All(k => k == (1, 1)), "switch discards old cached frames");
            Check(((Image)window.FindName("SpriteImage")).Source is BitmapSource { PixelWidth: 16 }, "runtime draws new sheet");
            type.GetMethod("ApplyScale", PrivateInstance)!.Invoke(window, new object[] { 1.5, false });
            Check(window.Width == 24 && window.Height == 24, "runtime size follows manifest cells");
            Check((TimeSpan)type.GetMethod("CurrentFrameDuration", PrivateInstance)!.Invoke(window, null)! == TimeSpan.FromMilliseconds(200),
                "runtime timing follows manifest");
            type.GetMethod("BuildWindowContextMenu", PrivateInstance)!.Invoke(window, null);
            type.GetMethod("UpdateMenuChecks", PrivateInstance)!.Invoke(window, null);
            Check(window.ContextMenu.Items.OfType<MenuItem>().Where(x => x.Tag is PetState s && s != PetState.Idle).All(x => !x.IsEnabled),
                "runtime disables missing action menu items");
            Check(window.Title == custom.Manifest.Name, "runtime title changes with skin");
            animation.Invoke(window, new object[] { PetState.Waving, true });
            Check((PetState)type.GetField("_state", PrivateInstance)!.GetValue(window)! == PetState.Idle, "unsupported recall action falls back");
        }
        finally
        {
            typeof(MainWindow).GetMethod("PrepareForApplicationShutdown", PrivateInstance)!.Invoke(window, null);
            window.Close();
        }
    }

    private static void Render(SettingsWindow window, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(664, 561));
        root.Arrange(new Rect(0, 0, 664, 561));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(664, 561, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }

    private static void CheckCatalog(PetCatalogResult scan, IEnumerable<string> expectedIds, string name)
    {
        var expected = expectedIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var actual = scan.Pets.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal);
        Check(scan.Errors.Count == 0 && actual.SequenceEqual(expected), name,
            $"Expected: {string.Join(", ", expected)}; {DescribeCatalog(scan)}");
    }

    private static string DescribeCatalog(PetCatalogResult scan)
        => $"Found ({scan.Pets.Count}): {string.Join(", ", scan.Pets.Select(p => p.Id))}; errors: {string.Join("; ", scan.Errors)}";

    private static void Check(bool success, string name, string? details = null)
    {
        if (!success) throw new InvalidOperationException("FAIL: " + name +
            (details is null ? string.Empty : Environment.NewLine + details));
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void ExpectFailure(Action action, string name)
    {
        try { action(); }
        catch (InvalidDataException) { Check(true, name); return; }
        throw new InvalidOperationException("FAIL: accepted " + name);
    }
}
