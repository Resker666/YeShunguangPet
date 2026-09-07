using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

public sealed record PetBackupEntry(string DirectoryPath, string Name, string Id, bool Deleted, DateTime DirectoryTime,
    long? SizeBytes, string? Error, bool CanPurge, BitmapSource? Thumbnail)
{
    public string Kind => Deleted ? "已删除" : "历史版本";
    public bool CanRestore => Error is null;
    public string Status => Error is null ? "可恢复" : "不可恢复";
    public string SizeText => SizeBytes.HasValue ? $"{SizeBytes.Value / 1024.0 / 1024:0.00} MiB" : "未知";
    public string TimeText => DirectoryTime.ToString("yyyy-MM-dd HH:mm");
}

public sealed record PetBackupResult(IReadOnlyList<PetBackupEntry> Backups, IReadOnlyList<string> Errors);

public sealed partial class PetCatalog
{
    private static readonly Regex BackupName = new(@"\A\.(backup|deleted)-([a-z][a-z0-9-]{0,63})-([0-9a-f]{32})\z");

    public PetBackupResult ScanBackups()
    {
        var items = new List<PetBackupEntry>();
        var errors = new List<string>();
        if (!Directory.Exists(UserDirectory)) return new PetBackupResult(items, errors);
        try
        {
            CheckBackupRoot();
            foreach (var directory in Directory.GetDirectories(UserDirectory))
            {
                var match = BackupName.Match(Path.GetFileName(directory));
                if (!match.Success) continue;
                long? size = null;
                string? error = null;
                var name = match.Groups[2].Value;
                BitmapSource? thumbnail = null;
                var canPurge = false;
                try
                {
                    size = InspectBackupTree(directory);
                    canPurge = true;
                    var package = PetPackage.Load(Path.Combine(directory, "pet.json"));
                    if (package.Manifest.Id != name) throw new InvalidDataException("备份目录 id 与清单不一致。");
                    name = package.Manifest.Name;
                    thumbnail = CreateThumbnail(package);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { error = ex.Message; }
                items.Add(new PetBackupEntry(directory, name, match.Groups[2].Value, match.Groups[1].Value == "deleted",
                    Directory.GetLastWriteTime(directory), size, error, canPurge, thumbnail));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { errors.Add(ex.Message); }
        return new PetBackupResult(items.OrderByDescending(b => b.DirectoryTime).ThenBy(b => b.DirectoryPath).ToArray(), errors);
    }

    public PetEntry RestoreBackup(PetBackupEntry entry, bool replaceExisting = false)
    {
        var snapshot = ReadBackup(entry);
        var installed = Scan().Pets.FirstOrDefault(p => p.Id == entry.Id);
        if (installed is null)
        {
            var damaged = Path.Combine(UserDirectory, entry.Id);
            if (!Directory.Exists(damaged)) return ImportSnapshot(snapshot);
            if (!replaceExisting) throw new InvalidDataException("同 id 目录已存在，需要确认替换后才能恢复。");
            return UpdateSnapshot(new PetEntry(entry.Id, entry.Name, Path.Combine(damaged, "pet.json"), false), snapshot, recoverInvalid: true);
        }
        if (installed.Bundled) throw new InvalidDataException("同 id 的随附皮肤已存在，不能覆盖，请恢复为副本。");
        if (!replaceExisting) throw new InvalidDataException("同 id 皮肤已存在，需要确认替换后才能恢复。");
        // Updating makes another backup first; the selected backup remains available.
        return UpdateSnapshot(installed, snapshot);
    }

    public void PurgeBackup(PetBackupEntry entry)
    {
        var directory = GetBackupDirectory(entry);
        InspectBackupTree(directory);
        Directory.Delete(directory, recursive: true);
    }

    public PetEntry RestoreBackupCopy(PetBackupEntry entry)
    {
        var snapshot = ReadBackup(entry);
        var manifest = PetPackage.ReadManifest(snapshot.Json);
        manifest.Id = PetEditorDocument.AvailableId(this, manifest.Id);
        var json = PetPackage.SerializeManifest(manifest);
        return ImportSnapshot((PetPackage.WithManifest(json, snapshot.Package.SpriteSheet), json, snapshot.Png));
    }

    public void ExportBackup(PetBackupEntry entry, string destination, bool overwrite = false)
    {
        PetArchive.ExportSnapshot(ReadBackup(entry), destination, overwrite);
    }

    private (PetPackage Package, byte[] Json, byte[] Png) ReadBackup(PetBackupEntry entry)
    {
        var directory = GetBackupDirectory(entry);
        InspectBackupTree(directory);
        var snapshot = PetPackage.ReadPackage(Path.Combine(directory, "pet.json"));
        if (snapshot.Package.Manifest.Id != entry.Id) throw new InvalidDataException("备份内容已变化，请刷新列表。");
        return snapshot;
    }

    private string GetBackupDirectory(PetBackupEntry entry)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.DirectoryPath));
        var match = BackupName.Match(Path.GetFileName(path));
        if (!string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(UserDirectory), StringComparison.OrdinalIgnoreCase) ||
            !match.Success || match.Groups[2].Value != entry.Id)
            throw new InvalidDataException("只能操作用户皮肤目录中的有效备份目录。");
        CheckBackupRoot();
        PetPackage.RejectLink(path);
        return path;
    }

    private void CheckBackupRoot()
    {
        for (var directory = new DirectoryInfo(UserDirectory); directory is not null; directory = directory.Parent)
            PetPackage.RejectLink(directory.FullName);
    }

    private static long InspectBackupTree(string path)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((path, 0));
        long size = 0;
        var count = 0;
        while (pending.TryPop(out var current))
        {
            PetPackage.RejectLink(current.Path);
            if (current.Depth > 16) throw new InvalidDataException("备份目录层级过深。");
            foreach (var item in new DirectoryInfo(current.Path).EnumerateFileSystemInfos())
            {
                if (++count > 10000) throw new InvalidDataException("备份文件数量过多。");
                PetPackage.RejectLink(item.FullName);
                if (item is DirectoryInfo) pending.Push((item.FullName, current.Depth + 1));
                else size = checked(size + ((FileInfo)item).Length);
            }
        }
        return size;
    }
}
