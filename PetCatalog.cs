using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YeShunguangPet;

public sealed record PetEntry(string Id, string Name, string ManifestPath, bool Bundled);
public sealed record PetCatalogResult(IReadOnlyList<PetEntry> Pets, IReadOnlyList<string> Errors);

public sealed class PetCatalog
{
    public string BundledDirectory { get; }
    public string UserDirectory { get; }

    public PetCatalog(string? bundledDirectory = null, string? userDirectory = null)
    {
        BundledDirectory = bundledDirectory ?? Path.Combine(AppContext.BaseDirectory, "Pets");
        UserDirectory = userDirectory ?? Path.Combine(PetSettings.SettingsDirectory, "Pets");
    }

    public PetCatalogResult Scan()
    {
        var pets = new List<PetEntry>();
        var errors = new List<string>();
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
                        var manifest = PetPackage.Load(path).Manifest;
                        if (pets.Any(p => p.Id == manifest.Id)) throw new InvalidDataException($"重复 id：{manifest.Id}");
                        pets.Add(new PetEntry(manifest.Id, manifest.Name, path, bundled));
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
        // Copy only the validated snapshot, then publish the directory atomically.
        var snapshot = PetPackage.ReadPackage(manifestPath);
        var id = snapshot.Package.Manifest.Id;
        if (id == PetPackage.DefaultId || Scan().Pets.Any(p => p.Id == id))
            throw new InvalidDataException($"皮肤 id {id} 已存在，请修改新皮肤的 id 后再导入。");
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
}
