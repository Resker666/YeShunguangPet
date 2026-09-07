using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using YeShunguangPet;

internal static class SkinManagementTests
{
    public static void Run(Action<bool, string> check, PetCatalog sourceCatalog, PetEntry source, string root)
    {
        var catalog = new PetCatalog(sourceCatalog.BundledDirectory, Path.Combine(root, "zip-users"));
        var zip = Path.Combine(root, "skin.zip");
        var json = File.ReadAllBytes(source.ManifestPath);
        var png = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(source.ManifestPath)!, "spritesheet.png"));
        VerifyDuplicateDiscovery(check, root, json, png, source.Id);
        PetArchive.Export(source.ManifestPath, zip);
        using (var archive = ZipFile.OpenRead(zip))
        {
            check(archive.Entries.Count == 2, "export includes only skin JSON and PNG");
            check(Read(archive.GetEntry(source.Id + "/pet.json")!).SequenceEqual(json), "export preserves manifest bytes");
            check(Read(archive.GetEntry(source.Id + "/spritesheet.png")!).SequenceEqual(png), "export preserves PNG bytes");
        }
        var imported = catalog.Import(zip);
        check(imported.Id == source.Id && File.ReadAllBytes(imported.ManifestPath).SequenceEqual(json), "ZIP roundtrip import");
        var originalZip = File.ReadAllBytes(zip);
        try
        {
            PetArchive.Export(source.ManifestPath, zip);
            throw new InvalidOperationException("Export unexpectedly overwrote existing destination.");
        }
        catch (IOException) { check(File.ReadAllBytes(zip).SequenceEqual(originalZip), "export cannot overwrite without explicit opt-in"); }
        PetArchive.Export(source.ManifestPath, zip, overwrite: true);
        check(File.Exists(zip) && !Directory.GetFiles(root, "*.tmp").Any(), "confirmed export replaces destination and cleans temporary file");
        var rootZip = Path.Combine(root, "root.zip");
        var bomJson = new byte[] { 239, 187, 191 }.Concat(json).ToArray();
        WriteZip(rootZip, ("PET.JSON", bomJson), ("spritesheet.png", png));
        var rootCatalog = new PetCatalog(sourceCatalog.BundledDirectory, Path.Combine(root, "root-zip-users"));
        var rootImported = rootCatalog.Import(rootZip);
        check(File.ReadAllBytes(rootImported.ManifestPath).SequenceEqual(bomJson), "ZIP root layout and BOM preserve original manifest bytes");
        var scanned = catalog.Scan().Pets.Single(p => p.Id == source.Id);
        check(scanned.Thumbnail is { IsFrozen: true } thumb && thumb.PixelWidth <= 64 && thumb.PixelHeight <= 64,
            "catalog thumbnails are bounded and frozen");
        ExpectFailure(() => catalog.Import(zip), check, "duplicate ZIP import is non-destructive");
        ExpectFailure(() => catalog.Delete(imported, imported.Id), check, "active skin cannot be deleted");
        var activeWindow = new SettingsWindow(new PetSettings { SelectedPetId = imported.Id }, true, catalog, PetPackage.Load(imported.ManifestPath));
        try { check(!((Button)activeWindow.FindName("DeletePetButton")).IsEnabled, "active imported skin delete control disabled"); }
        finally { activeWindow.Close(); }
        var bundled = catalog.Scan().Pets.First(p => p.Bundled);
        ExpectFailure(() => catalog.Delete(bundled, imported.Id), check, "bundled skin cannot be deleted");
        ExpectFailure(() => catalog.Update(bundled, zip), check, "bundled skin cannot be updated");
        ExpectFailure(() => catalog.Delete(source, bundled.Id), check, "out-of-root skin entry rejected");
        ExpectFailure(() => catalog.Delete(bundled with { Bundled = false }, imported.Id), check, "forged bundled flag cannot permit deletion");

        var updateJson = JsonNode.Parse(json)!.AsObject();
        updateJson["name"] = "Updated skin";
        var updateZip = Path.Combine(root, "update.zip");
        WriteZip(updateZip, ("nested/pet.json", Encoding.UTF8.GetBytes(updateJson.ToJsonString())), ("nested/spritesheet.png", png));
        var updated = catalog.Update(imported, updateZip);
        check(updated.Name == "Updated skin" && PetPackage.Load(updated.ManifestPath).Manifest.Name == updated.Name, "same-ID ZIP update replaces skin");
        var backups = Directory.GetDirectories(catalog.UserDirectory, ".backup-*");
        check(backups.Length == 1 && File.ReadAllBytes(Path.Combine(backups[0], "pet.json")).SequenceEqual(json), "update retains original backup");
        check(catalog.Scan().Errors.Count == 0 && catalog.Scan().Pets.Count(p => p.Id == source.Id) == 1, "backup is excluded from catalog");
        var updatedBytes = File.ReadAllBytes(updated.ManifestPath);
        updateJson["id"] = "different-id";
        WriteZip(updateZip, ("pet.json", Encoding.UTF8.GetBytes(updateJson.ToJsonString())), ("spritesheet.png", png));
        ExpectFailure(() => catalog.Update(imported, updateZip), check, "update rejects mismatched ID");
        check(File.ReadAllBytes(updated.ManifestPath).SequenceEqual(updatedBytes), "rejected update preserves installed skin");
        catalog.Delete(updated, bundled.Id);
        check(!catalog.Scan().Pets.Any(p => p.Id == source.Id), "deleted imported skin leaves catalog");
        check(Directory.GetDirectories(catalog.UserDirectory, ".deleted-*").Length == 1, "deleted skin files retained for recovery");
        check(catalog.Import(zip).Id == source.Id, "deleted skin ID can be imported again");

        var invalid = Path.Combine(root, "invalid.zip");
        void Bad(string name, params (string Name, byte[] Bytes)[] extra)
        {
            WriteZip(invalid, new[] { ("pet.json", json), ("spritesheet.png", png) }.Concat(extra).ToArray());
            var target = new PetCatalog(catalog.BundledDirectory, Path.Combine(root, "rejected-" + Guid.NewGuid().ToString("N")));
            ExpectFailure(() => target.Import(invalid), check, name);
            check(!Directory.Exists(target.UserDirectory), name + " writes no files");
        }
        Bad("ZIP parent traversal rejected", ("../outside.txt", new byte[] { 1 }));
        Bad("ZIP absolute path rejected", ("/outside.txt", new byte[] { 1 }));
        Bad("ZIP drive path rejected", ("C:/outside.txt", new byte[] { 1 }));
        Bad("ZIP backslash path rejected", (@"..\outside.txt", new byte[] { 1 }));
        Bad("ZIP duplicate case-insensitive path rejected", ("PET.JSON", json));
        Bad("ZIP multiple skins rejected", ("other/pet.json", json));
        Bad("ZIP alternate data stream rejected", ("note:stream", new byte[] { 1 }));
        Bad("ZIP excessive entry count rejected", Enumerable.Range(0, 255).Select(i => ($"extra{i}", new byte[] { 1 })).ToArray());
        WriteZip(invalid, ("pet.json", json));
        ExpectFailure(() => new PetCatalog(catalog.BundledDirectory, Path.Combine(root, "missing-png")).Import(invalid), check, "ZIP missing PNG rejected");
        WriteZip(invalid, ("pet.json", json), ("spritesheet.png", new byte[] { 1 }));
        ExpectFailure(() => catalog.Update(catalog.Scan().Pets.Single(p => p.Id == source.Id), invalid), check, "invalid update PNG rejected");
        WriteZip(invalid, ("pet.json", new byte[128 * 1024 + 1]), ("spritesheet.png", png));
        ExpectFailure(() => catalog.Import(invalid), check, "ZIP manifest allocation bounded");
        WriteZip(invalid, ("pet.json", json), ("spritesheet.png", png));
        using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Update))
            archive.GetEntry("spritesheet.png")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        ExpectFailure(() => catalog.Import(invalid), check, "ZIP symlink rejected");
        check(!Directory.GetDirectories(catalog.UserDirectory, ".import-*").Any(), "skin operations clean staging directories");
    }

    private static void VerifyDuplicateDiscovery(Action<bool, string> check, string root, byte[] json, byte[] png, string id)
    {
        var bundledRoot = Path.Combine(root, "duplicates-bundled");
        var userRoot = Path.Combine(root, "duplicates-users");
        string CopyTo(string parent, string name)
        {
            var directory = Path.Combine(parent, name);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "pet.json"), json);
            File.WriteAllBytes(Path.Combine(directory, "spritesheet.png"), png);
            return Path.Combine(directory, "pet.json");
        }
        var bundled = CopyTo(bundledRoot, "original");
        var imported = CopyTo(userRoot, "previous-import");
        var catalog = new PetCatalog(bundledRoot, userRoot);
        var scan = catalog.Scan();
        check(scan.Errors.Count == 0 && scan.Pets.Count == 1 && scan.Pets[0].Bundled && scan.Pets[0].ManifestPath == bundled,
            "identical bundled and imported skin deduplicated without warning");
        check(catalog.LoadPreferred(id, out var fallback).Manifest.Id == id && fallback is null,
            "identical duplicate preserves selected skin");
        check(File.ReadAllBytes(imported).SequenceEqual(json) &&
            File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(imported)!, "spritesheet.png")).SequenceEqual(png),
            "deduplication does not delete or rewrite imported files");
        var panel = new SettingsWindow(new PetSettings { SelectedPetId = id }, true, catalog, PetPackage.Load(bundled));
        try
        {
            typeof(SettingsWindow).GetMethod("RefreshPets_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(panel, new object?[] { null, new System.Windows.RoutedEventArgs() });
            check(((ListBox)panel.FindName("PetSelector")).Items.Count == 1 &&
                string.IsNullOrEmpty(((TextBlock)panel.FindName("SkinStatus")).Text), "settings refresh has no false duplicate warning");
        }
        finally { panel.Close(); }
        ExpectFailure(() => catalog.Import(imported), check, "duplicate import still cannot overwrite bundled skin");

        var changed = JsonNode.Parse(json)!.AsObject();
        changed["description"] = "Changed configuration";
        File.WriteAllText(imported, changed.ToJsonString());
        scan = catalog.Scan();
        check(scan.Pets.Count == 1 && scan.Pets[0].Bundled && scan.Errors.Count == 1 &&
            scan.Errors[0].Contains(bundled) && scan.Errors[0].Contains(imported), "different same-ID manifest reports both conflict locations");
        File.WriteAllBytes(imported, json);
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(PetPackage.Load(imported).SpriteSheet);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, 1, 1), new byte[] { 17, 33, 49, 255 }, 4, 0);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var pngPath = Path.Combine(Path.GetDirectoryName(imported)!, "spritesheet.png");
        using (var stream = File.Create(pngPath)) encoder.Save(stream);
        check(catalog.Scan().Errors.Count == 1, "different same-ID PNG remains a real conflict");
        File.WriteAllBytes(pngPath, png);
        check(catalog.Scan().Errors.Count == 0, "refresh reevaluates content after conflict is resolved");
        File.WriteAllText(imported, "broken");
        check(catalog.Scan().Errors.Count == 1, "invalid duplicate is validated rather than silently hidden");
        File.WriteAllBytes(imported, json);
        CopyTo(userRoot, "second-copy");
        var usersOnly = new PetCatalog(Path.Combine(root, "no-bundled-duplicates"), userRoot).Scan();
        check(usersOnly.Errors.Count == 0 && usersOnly.Pets.Count == 1 && !usersOnly.Pets[0].Bundled,
            "identical copies within user directory deduplicated deterministically");
        var separate = CopyTo(userRoot, "another-character");
        changed = JsonNode.Parse(json)!.AsObject();
        changed["id"] = "different-character";
        File.WriteAllText(separate, changed.ToJsonString());
        check(catalog.Scan().Pets.Count == 2 && catalog.Scan().Errors.Count == 0, "different skin IDs remain separate entries");
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void WriteZip(string path, params (string Name, byte[] Bytes)[] entries)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    private static void ExpectFailure(Action action, Action<bool, string> check, string name)
    {
        try { action(); }
        catch (InvalidDataException) { check(true, name); return; }
        throw new InvalidOperationException("Accepted: " + name);
    }
}
