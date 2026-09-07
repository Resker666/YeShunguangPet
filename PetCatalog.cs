using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

public sealed record PetEntry(string Id, string Name, string ManifestPath, bool Bundled)
{
    public BitmapSource? Thumbnail { get; init; }
    public string SourceLabel => Bundled ? "随附" : "已导入";
}
public sealed record PetCatalogResult(IReadOnlyList<PetEntry> Pets, IReadOnlyList<string> Errors);

public sealed partial class PetCatalog
{
    public string BundledDirectory { get; }
    public string UserDirectory { get; }

    public PetCatalog(string? bundledDirectory = null, string? userDirectory = null)
    {
        BundledDirectory = Path.GetFullPath(bundledDirectory ?? Path.Combine(AppContext.BaseDirectory, "Pets"));
        UserDirectory = Path.GetFullPath(userDirectory ?? Path.Combine(PetSettings.SettingsDirectory, "Pets"));
    }

    public PetCatalogResult Scan()
    {
        var pets = new List<PetEntry>();
        var errors = new List<string>();
        var fingerprints = new Dictionary<string, (string Json, string Png)>();
        foreach (var (root, bundled) in new[] { (BundledDirectory, true), (UserDirectory, false) })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                PetPackage.RejectLink(root);
                foreach (var directory in Directory.GetDirectories(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (Path.GetFileName(directory).StartsWith('.')) continue;
                    try
                    {
                        var path = Path.Combine(directory, "pet.json");
                        var snapshot = PetPackage.ReadPackage(path);
                        var package = snapshot.Package;
                        var manifest = package.Manifest;
                        var fingerprint = (Convert.ToHexString(SHA256.HashData(snapshot.Json)),
                            Convert.ToHexString(SHA256.HashData(snapshot.Png)));
                        if (fingerprints.TryGetValue(manifest.Id, out var existingFingerprint))
                        {
                            // Older imports can become bundled skins after an upgrade; keep both files untouched.
                            if (existingFingerprint == fingerprint) continue;
                            var selected = pets.First(p => p.Id == manifest.Id);
                            throw new InvalidDataException($"重复 id：{manifest.Id}，但内容不同。已使用 {selected.ManifestPath}；请检查 {path}，或为冲突皮肤修改 id。");
                        }
                        pets.Add(new PetEntry(manifest.Id, manifest.Name, path, bundled) { Thumbnail = CreateThumbnail(package) });
                        fingerprints.Add(manifest.Id, fingerprint);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        errors.Add($"{Path.GetFileName(directory)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                errors.Add($"{root}: {ex.Message}");
            }
        }
        return new PetCatalogResult(pets, errors);
    }

    public PetPackage LoadPreferred(string id, out string? fallbackMessage)
    {
        var scan = Scan();
        var selected = scan.Pets.FirstOrDefault(p => p.Id == id);
        selected ??= scan.Pets.FirstOrDefault(p => p.Id == PetPackage.DefaultId) ?? scan.Pets.FirstOrDefault();
        if (selected is null)
            throw new InvalidDataException("没有可用皮肤。请完整解压 ZIP，确保 EXE 旁有 Pets 目录。\n" + string.Join("\n", scan.Errors));
        fallbackMessage = selected.Id == id ? null : $"原皮肤不可用，已切换为 {selected.Name}。";
        return PetPackage.Load(selected.ManifestPath);
    }

    public PetEntry Import(string manifestPath)
    {
        return ImportSnapshot(PetArchive.Read(manifestPath));
    }

    internal PetEntry ImportSnapshot((PetPackage Package, byte[] Json, byte[] Png) snapshot)
    {
        // Copy only the validated snapshot, then publish the directory atomically.
        var id = snapshot.Package.Manifest.Id;
        if (id == PetPackage.DefaultId || Scan().Pets.Any(p => p.Id == id))
            throw new InvalidDataException($"皮肤 id {id} 已存在。请选择已有的导入皮肤并使用“更新皮肤”，或修改新皮肤的 id。");
        Directory.CreateDirectory(UserDirectory);
        PetPackage.RejectLink(UserDirectory);
        var destination = Path.Combine(UserDirectory, id);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidDataException($"目录 {id} 已存在，未覆盖任何文件。");
        var staging = Path.Combine(UserDirectory, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            File.WriteAllBytes(Path.Combine(staging, "pet.json"), snapshot.Json);
            File.WriteAllBytes(Path.Combine(staging, snapshot.Package.Manifest.SpriteSheet), snapshot.Png);
            PetPackage.Load(Path.Combine(staging, "pet.json"));
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        return new PetEntry(id, snapshot.Package.Manifest.Name, Path.Combine(destination, "pet.json"), false);
    }

    public PetEntry Update(PetEntry entry, string source)
    {
        return UpdateSnapshot(entry, PetArchive.Read(source));
    }

    internal PetEntry UpdateSnapshot(PetEntry entry, (PetPackage Package, byte[] Json, byte[] Png) snapshot, bool recoverInvalid = false)
    {
        var destination = GetManagedDirectory(entry, recoverInvalid);
        if (snapshot.Package.Manifest.Id != entry.Id) throw new InvalidDataException("更新包的 id 必须与选中的皮肤相同。");
        var staging = Path.Combine(UserDirectory, ".import-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(UserDirectory, ".backup-" + entry.Id + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            File.WriteAllBytes(Path.Combine(staging, "pet.json"), snapshot.Json);
            File.WriteAllBytes(Path.Combine(staging, snapshot.Package.Manifest.SpriteSheet), snapshot.Png);
            PetPackage.Load(Path.Combine(staging, "pet.json"));
            Directory.Move(destination, backup);
            try { Directory.Move(staging, destination); }
            catch
            {
                Directory.Move(backup, destination);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        return new PetEntry(entry.Id, snapshot.Package.Manifest.Name, entry.ManifestPath, false);
    }

    public void Delete(PetEntry entry, string activePetId)
    {
        if (entry.Id == activePetId) throw new InvalidDataException("不能删除当前正在使用的皮肤，请先切换并保存其他皮肤。");
        var directory = GetManagedDirectory(entry);
        // Retain files for recovery, but omit them from the selectable catalog.
        Directory.Move(directory, Path.Combine(UserDirectory, ".deleted-" + entry.Id + "-" + Guid.NewGuid().ToString("N")));
    }

    private string GetManagedDirectory(PetEntry entry, bool recoverInvalid = false)
    {
        var path = Path.GetFullPath(entry.ManifestPath);
        var directory = Path.GetDirectoryName(path)!;
        if (entry.Bundled || entry.Id == PetPackage.DefaultId ||
            Path.GetFileName(directory).StartsWith('.') ||
            !string.Equals(Path.GetDirectoryName(directory), Path.TrimEndingDirectorySeparator(UserDirectory), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(path), "pet.json", StringComparison.OrdinalIgnoreCase) ||
            Scan().Pets.Any(p => p.Bundled && p.Id == entry.Id))
            throw new InvalidDataException("只能管理用户目录中导入的皮肤，随附皮肤不可修改。");
        PetPackage.RejectLink(UserDirectory);
        PetPackage.RejectLink(directory);
        if (recoverInvalid)
        {
            PetPackage.ValidateId(entry.Id);
            if (Path.GetFileName(directory) != entry.Id) throw new InvalidDataException("恢复目录与 id 不一致。");
            PetPackage? current = null;
            try { current = PetPackage.Load(path); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
            if (current is not null && current.Manifest.Id != entry.Id)
                throw new InvalidDataException("该目录属于另一个有效皮肤，不能覆盖。请恢复为副本。");
        }
        else if (PetPackage.Load(path).Manifest.Id != entry.Id) throw new InvalidDataException("皮肤已变化，请刷新列表。");
        return directory;
    }

    private static BitmapSource CreateThumbnail(PetPackage package)
    {
        var preview = package.Preview;
        var scale = Math.Min(64.0 / preview.PixelWidth, 64.0 / preview.PixelHeight);
        var scaled = new TransformedBitmap(preview, new ScaleTransform(scale, scale));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        // Detach the small image so the list does not retain every decoded sprite sheet.
        var thumbnail = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
            PixelFormats.Pbgra32, null, pixels, stride);
        thumbnail.Freeze();
        return thumbnail;
    }
}
