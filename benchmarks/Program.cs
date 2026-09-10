using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using YeShunguangPet;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args[0] == "--activity") return ActivityProbe.Run(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 8);
        if (args[0] == "--promotion") return PromotionCapture.Run(args[1], args[2]);
        if (args[0] == "--live") return LiveProbe.Run(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 30);
        if (args[0] == "--native-smoke") return LiveProbe.Run(args[1], args[2], 3, nativeIntegration: true);
        var source = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]);
        var catalog = new PetCatalog(Path.Combine(source, "Pets"), Path.Combine(source, "artifacts", "probe-empty-users"));
        var cold = Stopwatch.StartNew();
        var warm = catalog.LoadPreferred(PetPackage.DefaultId, out _);
        var coldMs = cold.Elapsed.TotalMilliseconds;
        Collect();
        var initial = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        var packages = Enumerable.Range(0, 3).Select(index => catalog.LoadPreferred(PetPackage.DefaultId, out _)).ToArray();
        var loadMs = watch.Elapsed.TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(true) - initial;
        var sheetCount = packages.Select(p => p.SpriteSheet).Distinct().Count();
        var frameWatch = Stopwatch.StartNew();
        var beforeFrames = GC.GetTotalAllocatedBytes(true);
        for (var i = 0; i < 3000; i++)
            foreach (var pet in packages) _ = pet.GetFrame(0, i % 6);
        var frameMs = frameWatch.Elapsed.TotalMilliseconds;
        var frameAllocated = GC.GetTotalAllocatedBytes(true) - beforeFrames;
        Collect();
        using var process = Process.GetCurrentProcess();
        var result = new
        {
            Version = typeof(PetPackage).Assembly.GetName().Version?.ToString(),
            Workload = "One warmup, three simultaneous same-skin packages, 9000 frame requests; no native windows",
            ColdLoadMs = coldMs, ThreeLoadsMs = loadMs, ThreeLoadsAllocatedBytes = allocated,
            DistinctDecodedSheets = sheetCount, FrameRequestsMs = frameMs, FrameAllocatedBytes = frameAllocated,
            ManagedHeapBytes = GC.GetTotalMemory(false), PrivateMiB = process.PrivateMemorySize64 / 1048576.0,
            WorkingSetMiB = process.WorkingSet64 / 1048576.0
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(output, json);
        Console.WriteLine(json);
        GC.KeepAlive(warm);
        GC.KeepAlive(packages);
        return 0;
    }

    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
