using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace YeShunguangPet;

public sealed record PetSourceSnapshot(PetPackage Package, byte[] Json, byte[] Png, string Fingerprint)
{
    public static PetSourceSnapshot Read(string path)
    {
        for (var current = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); current is not null; current = current.Parent)
            PetPackage.RejectLink(current.FullName);
        var source = PetPackage.ReadPackage(path);
        return new(source.Package, source.Json, source.Png, FingerprintOf(source.Json, source.Png));
    }
    internal static string FingerprintOf(byte[] json, byte[] png) =>
        Convert.ToHexString(SHA256.HashData(json)) + Convert.ToHexString(SHA256.HashData(png));
}

public sealed record PetSourceUpdate(long Generation, PetSourceSnapshot? Snapshot, string? Error);

public sealed class PetSourceWatcher : IDisposable
{
    private readonly string _path, _directory, _id;
    private readonly object _gate = new();
    private readonly FileSystemWatcher _watcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private Task _worker = Task.CompletedTask;
    private long _generation;
    private bool _running, _disposed;
    public event Action<PetSourceUpdate>? Changed;
    public Task Completion { get { lock (_gate) return _worker; } }

    public PetSourceWatcher(string path, string id)
    {
        _path = Path.GetFullPath(path);
        _directory = Path.GetDirectoryName(_path)!;
        _id = id;
        _token = _lifetime.Token;
        var parent = Path.GetDirectoryName(_directory) ?? _directory;
        for (var current = new DirectoryInfo(_directory); current is not null; current = current.Parent)
            PetPackage.RejectLink(current.FullName);
        // Watch the parent so atomic replacement of the entire skin directory remains observable.
        _watcher = new FileSystemWatcher(parent)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Changed += FileChanged; _watcher.Created += FileChanged; _watcher.Deleted += FileChanged;
        _watcher.Renamed += (_, e) => { if (Relevant(e.FullPath) || Relevant(e.OldFullPath)) RequestReload(); };
        _watcher.Error += (_, _) => RequestReload();
        try { _watcher.EnableRaisingEvents = true; }
        catch { _watcher.Dispose(); _lifetime.Dispose(); throw; }
    }

    private bool Relevant(string path) => string.Equals(path, _directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private void FileChanged(object sender, FileSystemEventArgs e) { if (Relevant(e.FullPath)) RequestReload(); }

    public bool IsCurrent(long generation) { lock (_gate) return !_disposed && _generation == generation; }

    public void RequestReload()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _generation++;
            if (_running) return;
            _running = true;
            _worker = Task.Run(ReadLoop);
        }
    }

    private async Task ReadLoop()
    {
        try
        {
            while (true)
            {
                long generation;
                lock (_gate) generation = _generation;
                await Task.Delay(350, _token).ConfigureAwait(false);
                if (!IsCurrent(generation)) { _token.ThrowIfCancellationRequested(); continue; }
                PetSourceSnapshot? snapshot = null; string? error = null;
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    _token.ThrowIfCancellationRequested();
                    try
                    {
                        var first = PetSourceSnapshot.Read(_path);
                        await Task.Delay(180, _token).ConfigureAwait(false);
                        var second = PetSourceSnapshot.Read(_path);
                        if (first.Fingerprint != second.Fingerprint) throw new IOException("皮肤文件仍在写入。");
                        if (second.Package.Manifest.Id != _id) throw new InvalidDataException("源文件的皮肤 ID 已变化，未替换当前预览。");
                        snapshot = second; error = null; break;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        error = ex.Message;
                        await Task.Delay(180, _token).ConfigureAwait(false);
                    }
                }
                Action<PetSourceUpdate>? changed;
                lock (_gate)
                {
                    if (_disposed) return;
                    if (_generation != generation) continue;
                    changed = Changed;
                }
                changed?.Invoke(new PetSourceUpdate(generation, snapshot, error));
                lock (_gate)
                {
                    if (_generation != generation) continue;
                    _running = false; return;
                }
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { lock (_gate) _running = false; }
        catch (Exception ex)
        {
            AppLogger.Error("Skin source watcher failed.", ex);
            Action<PetSourceUpdate>? changed; long generation;
            lock (_gate) { _running = false; changed = _disposed ? null : Changed; generation = _generation; }
            changed?.Invoke(new PetSourceUpdate(generation, null, "自动刷新失败，当前预览已保留。"));
        }
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; Changed = null; }
        _watcher.Dispose(); _lifetime.Cancel(); _lifetime.Dispose();
    }
}
