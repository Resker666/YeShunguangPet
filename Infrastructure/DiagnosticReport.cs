using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace YeShunguangPet;

public sealed class DiagnosticRedactor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(150);
    private static readonly Regex[] Patterns =
    {
        new("""(?im)(?:\b(?:authorization|proxy-authorization|cookie|set-cookie|api[_ -]?key|access[_ -]?token|refresh[_ -]?token|password|passwd|secret|token)\b["']?\s*[:=]\s*|\bBearer\s+)[^\r\n]*""", RegexOptions.CultureInvariant, Timeout),
        new("""(?i)\b(?:https?|ftp|file)://[^\s"'<>]+""", RegexOptions.CultureInvariant, Timeout),
        new("""(?i)(?:\b[A-Z]:[\\/]|\\\\[^\s\\]+\\)[^\r\n"'<>|]*""", RegexOptions.CultureInvariant, Timeout),
        new("""(?<![\w])/(?:[^/\s"'<>]+/)[^\r\n"'<>]*""", RegexOptions.CultureInvariant, Timeout),
        new("""(?i)[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9.-]+\.[A-Z]{2,}""", RegexOptions.CultureInvariant, Timeout),
        new("""\b(?:sk-[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9_]{16,})\b""", RegexOptions.CultureInvariant, Timeout)
    };
    private readonly string[] _privateValues;
    public DiagnosticRedactor(IEnumerable<string>? privateValues = null)
    {
        _privateValues = (privateValues ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x) && x.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Length).ToArray();
    }
    public string Clean(string value)
    {
        if (value.Length > 1024 * 1024) value = value[..(1024 * 1024)] + "\n[truncated]";
        try
        {
            foreach (var pattern in Patterns) value = pattern.Replace(value, "[redacted]");
            foreach (var text in _privateValues) value = value.Replace(text, "[private]", StringComparison.OrdinalIgnoreCase);
            return value;
        }
        catch (RegexMatchTimeoutException) { return "[content omitted because redaction timed out]"; }
    }
}

public sealed record DiagnosticReport(string InformationJson, string LogsText)
{
    public static string ApplicationVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public static DiagnosticReport Capture(DesktopSession? desktop, LogSnapshot logs, Exception? error = null, string? errorId = null, RuntimeCapture? activity = null)
    {
        var privateValues = new List<string> { Environment.UserName, Environment.MachineName };
        using var process = Process.GetCurrentProcess();
        if (desktop is not null)
            privateValues.AddRange(desktop.Windows.SelectMany(w => new[] { w.Package.Manifest.Name, w.Package.Manifest.Id, w.Package.Manifest.Description }));
        var redactor = new DiagnosticRedactor(privateValues);
        var information = new
        {
            FormatVersion = 1,
            CreatedUtc = DateTimeOffset.UtcNow,
            Application = new
            {
                Version = ApplicationVersion,
                Runtime = RuntimeInformation.FrameworkDescription,
                OperatingSystem = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.Is64BitProcess
            },
            Resources = new
            {
                ManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
                PrivateMemoryBytes = process.PrivateMemorySize64,
                SpriteImageLeases = PetPackage.ActiveImageLeases,
                ActiveRoles = desktop?.Windows.Count ?? 0,
                ActivePins = desktop?.PinCount ?? 0,
                CaptureActive = desktop?.IsCapturing ?? false
            },
            Logging = new
            {
                Storage = logs.ActivePath is null ? "memory-only" : logs.ActiveLocation == 0 ? "primary" : "fallback",
                Location = logs.ActiveLocation,
                Failures = logs.Failures.Select(redactor.Clean).ToArray()
            },
            Desktop = desktop is null ? null : new
            {
                desktop.Configuration.SchemaVersion,
                desktop.Configuration.Appearance.Theme,
                desktop.Configuration.Appearance.Accent,
                desktop.Configuration.Appearance.ReduceMotion,
                desktop.Companion.Settings.DoNotDisturb,
                SessionStatus = desktop.Companion.Session.Status.ToString(),
                Roles = desktop.Windows.Select((w, i) => new
                {
                    Number = i + 1,
                    w.IsLoaded,
                    w.IsVisible,
                    w.IsDocked,
                    Scale = w.PetScale,
                    w.InstanceSettings.Topmost,
                    w.InstanceSettings.ClickThrough,
                    w.InstanceSettings.EdgeAutoHide,
                    w.InstanceSettings.LookAtMouse,
                    w.InstanceSettings.RandomIdleActions,
                    w.InstanceSettings.DesktopRoaming,
                    w.Package.Manifest.CellWidth,
                    w.Package.Manifest.CellHeight,
                    AnimationCount = w.Package.Manifest.Animations.Count,
                    w.HasAnimationTimer,
                    w.HasAmbientTimer,
                    w.HasRoamingTimer,
                    w.HasDockTimer
                }).ToArray()
            },
            Error = error is null ? null : new { Id = errorId, Details = redactor.Clean(error.ToString()) },
            ActivitySampling = activity
        };
        var text = new StringBuilder();
        foreach (var excerpt in logs.Excerpts)
        {
            text.AppendLine("## " + redactor.Clean(excerpt.Name));
            text.AppendLine(redactor.Clean(excerpt.Text));
            if (text.Length > 1024 * 1024) { text.Length = 1024 * 1024; text.AppendLine("\n[truncated]"); break; }
        }
        return new DiagnosticReport(JsonSerializer.Serialize(information, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }), text.ToString());
    }

    public void Export(string destination, bool overwrite = false)
    {
        destination = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new IOException("诊断包必须保存为 ZIP 文件。");
        if (!overwrite && File.Exists(destination)) throw new IOException("目标文件已存在，未覆盖。");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Add(archive, "report.json", InformationJson);
                Add(archive, "logs.txt", LogsText);
                Add(archive, "README.txt", "叶瞬光桌面宠物诊断包\n\n仅包含版本、运行状态和有大小上限的最近日志。\n未包含原始配置文件、角色名称清单、精灵图、截图、环境变量清单或系统事件日志。\n常见个人路径、账号、网址和密钥字段已自动脱敏；自由文本无法保证完全识别，分享前请检查内容。\n本程序不会自动上传诊断包。\n");
            }
            File.Move(temporary, destination, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Add(ZipArchive archive, string name, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(value);
    }
}
