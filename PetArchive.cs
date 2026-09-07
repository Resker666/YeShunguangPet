using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace YeShunguangPet;

public static class PetArchive
{
    private const long MaxArchiveBytes = 80 * 1024 * 1024;

    internal static (PetPackage Package, byte[] Json, byte[] Png) Read(string path)
    {
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return PetPackage.ReadPackage(path);
        PetPackage.RejectLink(path);
        using var file = File.OpenRead(path);
        if (file.Length > MaxArchiveBytes) throw new InvalidDataException("皮肤 ZIP 最大为 80 MB。");
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        if (archive.Entries.Count > 256) throw new InvalidDataException("皮肤 ZIP 文件数量过多。");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            // Never extract arbitrary paths. Only the validated JSON and its sibling PNG are read.
            var name = entry.FullName.TrimEnd('/');
            if (string.IsNullOrEmpty(name) || name.Split('/').Any(p =>
                    string.IsNullOrEmpty(p) || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                    p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("ZIP 包含不安全的路径或链接。");
            if (!entries.TryAdd(name, entry)) throw new InvalidDataException("ZIP 包含重复文件名。");
            total = checked(total + entry.Length);
            if (total > MaxArchiveBytes) throw new InvalidDataException("ZIP 解压后大小超出限制。");
        }
        var manifests = entries.Where(e => Path.GetFileName(e.Key).Equals("pet.json", StringComparison.OrdinalIgnoreCase) && !e.Value.FullName.EndsWith('/')).ToArray();
        if (manifests.Length != 1) throw new InvalidDataException("ZIP 必须只包含一个 pet.json。");
        var json = ReadEntry(manifests[0].Value, PetPackage.MaxManifestBytes);
        var manifest = PetPackage.ReadManifest(json);
        var slash = manifests[0].Key.LastIndexOf('/');
        var pngName = manifests[0].Key[..(slash + 1)] + manifest.SpriteSheet;
        if (!entries.TryGetValue(pngName, out var pngEntry)) throw new InvalidDataException("ZIP 缺少与 pet.json 同目录的精灵图。");
        var png = ReadEntry(pngEntry, PetPackage.MaxSpriteBytes);
        return (PetPackage.Decode(manifest, png), json, png);
    }

    public static void Export(string manifestPath, string destination, bool overwrite = false)
    {
        var snapshot = PetPackage.ReadPackage(manifestPath);
        destination = Path.GetFullPath(destination);
        if (!Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("导出文件必须为 ZIP。");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var prefix = snapshot.Package.Manifest.Id + "/";
                WriteEntry(archive, prefix + "pet.json", snapshot.Json);
                WriteEntry(archive, prefix + snapshot.Package.Manifest.SpriteSheet, snapshot.Png);
            }
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximum)
    {
        if (entry.Length <= 0 || entry.Length > maximum) throw new InvalidDataException("ZIP 内皮肤文件为空或超出大小限制。");
        using var stream = entry.Open();
        var bytes = new byte[checked((int)entry.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("ZIP 文件长度不一致。");
        return bytes;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }
}
