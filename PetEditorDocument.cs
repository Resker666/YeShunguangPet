using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

public sealed class PetEditorDocument
{
    private byte[] _originalJson;
    private byte[] _png;
    private byte[] _savedJson;
    public PetManifest Draft { get; private set; }
    public BitmapSource SpriteSheet { get; private set; }
    public string SourceFingerprint { get; private set; }
    public bool IsDirty => !PetPackage.SerializeManifest(Draft).SequenceEqual(_savedJson);

    public PetEditorDocument(string source)
    {
        var snapshot = PetArchive.Read(source);
        _originalJson = snapshot.Json;
        _png = snapshot.Png;
        SpriteSheet = snapshot.Package.SpriteSheet;
        Draft = PetPackage.ReadManifest(_originalJson);
        _savedJson = PetPackage.SerializeManifest(Draft);
        SourceFingerprint = PetSourceSnapshot.FingerprintOf(snapshot.Json, snapshot.Png);
    }

    public void Reset() => Draft = PetPackage.ReadManifest(_originalJson);
    internal void ReplaceDraft(PetManifest draft) => Draft = draft;
    internal void MarkClean() => _savedJson = PetPackage.SerializeManifest(Draft);
    internal void Reload(PetSourceSnapshot snapshot)
    {
        _originalJson = snapshot.Json;
        _png = snapshot.Png;
        SpriteSheet = snapshot.Package.SpriteSheet;
        Draft = PetPackage.ReadManifest(snapshot.Json);
        _savedJson = PetPackage.SerializeManifest(Draft);
        SourceFingerprint = snapshot.Fingerprint;
    }
    public PetPackage Preview() => Snapshot().Package;

    public PetEntry SaveUpdate(PetCatalog catalog, PetEntry entry)
    {
        var snapshot = Snapshot();
        if (PetSourceSnapshot.Read(entry.ManifestPath).Fingerprint != SourceFingerprint)
            throw new InvalidDataException("源文件已被其他操作修改。请先载入外部版本，或将当前草稿另存为皮肤。");
        var result = catalog.UpdateSnapshot(entry, snapshot, expectedFingerprint: SourceFingerprint);
        _savedJson = snapshot.Json;
        SourceFingerprint = PetSourceSnapshot.FingerprintOf(snapshot.Json, snapshot.Png);
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
