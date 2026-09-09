using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using YeShunguangPet;

internal static class PetSourceWatcherTests
{
    public static void Run(Action<bool, string> check, PetCatalog existing, string root)
    {
        var catalog = new PetCatalog(existing.BundledDirectory, Path.Combine(root, "watcher-users"));
        var source = existing.Scan().Pets.Single(p => p.Id == PetPackage.DefaultId);
        var entry = new PetEditorDocument(source.ManifestPath).SaveCopy(catalog);
        var snapshot = Exercise(check, entry).GetAwaiter().GetResult();
        check(snapshot.Package.Preview.IsFrozen && snapshot.Package.GetFrame(0, 0).IsFrozen,
            "background hot-reload snapshot can create frozen frames on the UI thread");
    }

    private static async Task<PetSourceSnapshot> Exercise(Action<bool, string> check, PetEntry entry)
    {
        var updates = new ConcurrentQueue<PetSourceUpdate>();
        using var watcher = new PetSourceWatcher(entry.ManifestPath, entry.Id);
        watcher.Changed += updates.Enqueue;
        watcher.RequestReload();
        await Until(() => updates.Any(x => x.Snapshot is not null));
        var first = updates.Last();
        check(watcher.IsCurrent(first.Generation), "source updates identify the current generation");
        updates.Clear();
        var original = File.ReadAllText(entry.ManifestPath);
        for (var i = 0; i < 12; i++)
        {
            var json = JsonNode.Parse(original)!; json["name"] = "Burst revision " + i;
            File.WriteAllText(entry.ManifestPath, json.ToJsonString()); watcher.RequestReload();
            await Task.Delay(15);
        }
        await Until(() => updates.Any(x => x.Snapshot?.Package.Manifest.Name == "Burst revision 11"));
        check(updates.Last().Snapshot?.Package.Manifest.Name == "Burst revision 11" && !watcher.IsCurrent(first.Generation),
            "rapid file writes settle on the latest revision and invalidate older queued generations");
        var good = updates.Last().Snapshot!;
        updates.Clear();
        using (var locked = new FileStream(entry.ManifestPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            watcher.RequestReload();
            await Until(() => updates.Any(x => x.Error is not null));
            check(updates.All(x => x.Snapshot is null), "locked source cannot publish a partial package");
        }
        updates.Clear(); watcher.RequestReload();
        await Until(() => updates.Any(x => x.Snapshot is not null));
        check(updates.Last().Snapshot!.Fingerprint == good.Fingerprint, "retry after a file lock recovers the same validated revision");
        updates.Clear();
        var unsafeJson = JsonNode.Parse(original)!; unsafeJson["spriteSheet"] = "../outside.png";
        File.WriteAllText(entry.ManifestPath, unsafeJson.ToJsonString()); watcher.RequestReload();
        await Until(() => updates.Any(x => x.Error is not null));
        check(updates.All(x => x.Snapshot is null), "hot reload uses the existing path-traversal validator");
        updates.Clear();
        File.WriteAllText(entry.ManifestPath, original); watcher.RequestReload();
        await Until(() => updates.Any(x => x.Snapshot is not null));
        var restored = updates.Last().Snapshot!;
        updates.Clear(); watcher.RequestReload(); watcher.Dispose();
        await watcher.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(450);
        check(updates.IsEmpty && !watcher.IsCurrent(first.Generation), "disposed watcher cancels pending reads and rejects late results");
        return restored;
    }

    private static async Task Until(Func<bool> predicate)
    {
        var elapsed = Stopwatch.StartNew();
        while (!predicate())
        {
            if (elapsed.Elapsed.TotalSeconds > 12) throw new TimeoutException("Source watcher result timed out");
            await Task.Delay(20);
        }
    }
}
