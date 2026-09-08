using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace YeShunguangPet;

public sealed record LogExcerpt(string Name, string Text);
public sealed record LogSnapshot(string? ActivePath, int ActiveLocation, LogExcerpt[] Excerpts, string[] Failures);

public sealed class ResilientLog
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly object _gate = new();
    private readonly string[] _directories;
    private readonly int _maxFileBytes, _maxMemoryChars;
    private readonly Queue<string> _recent = new();
    private readonly HashSet<string> _failures = new(StringComparer.Ordinal);
    private int _memoryChars;
    private string? _activePath;
    private int _activeLocation = -1;

    public ResilientLog(IEnumerable<string> directories, int maxFileBytes = 2 * 1024 * 1024, int maxMemoryChars = 128 * 1024)
    {
        if (maxFileBytes < 1024 || maxMemoryChars < 1024) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        _directories = directories.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _maxFileBytes = maxFileBytes;
        _maxMemoryChars = maxMemoryChars;
    }

    public bool Write(string level, string message)
    {
        lock (_gate)
        {
            var limit = Math.Min(16 * 1024, Math.Min(_maxFileBytes / 4 - 128, _maxMemoryChars / 2));
            if (message.Length > limit) message = message[..limit] + "\n[truncated]";
            var line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) +
                $" [{level}] [pid:{Environment.ProcessId}] {message}{Environment.NewLine}";
            _recent.Enqueue(line);
            _memoryChars += line.Length;
            while (_memoryChars > _maxMemoryChars && _recent.Count > 1) _memoryChars -= _recent.Dequeue().Length;
            _activePath = null;
            _activeLocation = -1;
            for (var i = 0; i < _directories.Length; i++)
            {
                try
                {
                    var directory = _directories[i];
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "app.log");
                    var bytes = Utf8.GetBytes(line);
                    if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > _maxFileBytes)
                        File.Move(path, Path.Combine(directory, "app.previous.log"), overwrite: true);
                    // Closing the stream can still fail while flushing buffered data.
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        stream.Write(bytes);
                    _activePath = path;
                    _activeLocation = i;
                    return true;
                }
                catch (Exception ex) { _failures.Add($"location-{i + 1}: {ex.GetType().Name} (0x{ex.HResult:X8})"); }
            }
            return false;
        }
    }

    public LogSnapshot Capture()
    {
        lock (_gate)
        {
            var excerpts = new List<LogExcerpt>();
            for (var i = 0; i < _directories.Length; i++)
            {
                foreach (var file in new[] { "app.previous.log", "app.log" })
                {
                    try
                    {
                        var path = Path.Combine(_directories[i], file);
                        if (!File.Exists(path)) continue;
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                            (File.GetAttributes(_directories[i]) & FileAttributes.ReparsePoint) != 0)
                        {
                            _failures.Add($"location-{i + 1}: linked log omitted");
                            continue;
                        }
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        var offset = Math.Max(0, stream.Length - 64 * 1024);
                        stream.Position = offset;
                        var bytes = new byte[Math.Min(64 * 1024, stream.Length - offset)];
                        var read = 0;
                        while (read < bytes.Length)
                        {
                            var n = stream.Read(bytes, read, bytes.Length - read);
                            if (n == 0) break;
                            read += n;
                        }
                        var text = Utf8.GetString(bytes, 0, read);
                        if (offset > 0) text = text.Contains('\n') ? text[(text.IndexOf('\n') + 1)..] : string.Empty;
                        excerpts.Add(new LogExcerpt($"location-{i + 1}-{file}", text));
                    }
                    catch (Exception ex) { _failures.Add($"location-{i + 1} read: {ex.GetType().Name} (0x{ex.HResult:X8})"); }
                }
            }
            excerpts.Add(new LogExcerpt("current-session", string.Concat(_recent)));
            return new LogSnapshot(_activePath, _activeLocation, excerpts.ToArray(), _failures.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
    }
}
