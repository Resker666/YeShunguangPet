using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

public sealed class PetEditorDocument
{
    private readonly byte[] _originalJson;
    private readonly byte[] _png;
    private byte[] _savedJson;
    public PetManifest Draft { get; private set; }
    public BitmapSource SpriteSheet { get; }
    public bool IsDirty => !PetPackage.SerializeManifest(Draft).SequenceEqual(_savedJson);

    public PetEditorDocument(string source)
    {
        var snapshot = PetArchive.Read(source);
        _originalJson = snapshot.Json;
        _png = snapshot.Png;
        SpriteSheet = snapshot.Package.SpriteSheet;
        Draft = PetPackage.ReadManifest(_originalJson);
        _savedJson = PetPackage.SerializeManifest(Draft);
    }

    public void Reset() => Draft = PetPackage.ReadManifest(_originalJson);
    public PetPackage Preview() => Snapshot().Package;

    public PetEntry SaveUpdate(PetCatalog catalog, PetEntry entry)
    {
        var snapshot = Snapshot();
        var result = catalog.UpdateSnapshot(entry, snapshot);
        _savedJson = snapshot.Json;
        return result;
    }

    public PetEntry SaveCopy(PetCatalog catalog)
    {
        var snapshot = Snapshot();
        var copy = PetPackage.ReadManifest(snapshot.Json);
        copy.Id = AvailableId(catalog, copy.Id);
        var json = PetPackage.SerializeManifest(copy);
        var result = catalog.ImportSnapshot((PetPackage.WithManifest(json, SpriteSheet), json, _png));
        Draft = copy;
        _savedJson = json;
        return result;
    }

    public void Export(string destination, bool overwrite = false) => PetArchive.ExportSnapshot(Snapshot(), destination, overwrite);

    public static string AvailableId(PetCatalog catalog, string id)
    {
        var used = catalog.Scan().Pets.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        bool Available(string candidate) => candidate != PetPackage.DefaultId && !used.Contains(candidate) &&
            !Directory.Exists(Path.Combine(catalog.UserDirectory, candidate)) && !File.Exists(Path.Combine(catalog.UserDirectory, candidate));
        // Validate before using an edited ID in any filesystem path.
        PetPackage.ValidateId(id);
        if (Available(id)) return id;
        var prefix = id[..Math.Min(id.Length, 48)];
        for (var i = 1; i <= 10000; i++)
        {
            var candidate = prefix + "-copy" + (i == 1 ? "" : "-" + i);
            if (Available(candidate)) return candidate;
        }
        throw new InvalidDataException("无法分配副本 id，请手动输入新 id。");
    }

    private (PetPackage Package, byte[] Json, byte[] Png) Snapshot()
    {
        var json = PetPackage.SerializeManifest(Draft);
        return (PetPackage.WithManifest(json, SpriteSheet), json, _png);
    }
}
