using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace YeShunguangPet;

public static class AppLogger
{
    private static readonly Lazy<ResilientLog> Sink = new(CreateSink);
    private static ResilientLog? _configuredSink;
    private static ResilientLog CurrentSink => Volatile.Read(ref _configuredSink) ?? Sink.Value;
    internal static void SetSink(ResilientLog sink) => Volatile.Write(ref _configuredSink, sink ?? throw new ArgumentNullException(nameof(sink)));

    public static string LogsDirectory => Path.Combine(PetSettings.SettingsDirectory, "logs");
    public static string LogPath => CurrentSink.Capture().ActivePath ?? Path.Combine(LogsDirectory, "app.log");
    public static LogSnapshot Capture() => CurrentSink.Capture();

    public static void Initialize()
    {
        Info("Log session initialized.");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", detail);
    }

    public static void OpenFolder()
    {
        Info("Opening active log folder.");
        var path = Capture().ActivePath ?? throw new IOException("日志目录均不可写，当前记录只保存在内存中。请导出诊断包。");
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetDirectoryName(path)!,
            UseShellExecute = true
        });
    }

    private static void Write(string level, string message)
    {
        try { CurrentSink.Write(level, message); }
        catch { /* Logging must never replace the original application error. */ }
    }

    private static ResilientLog CreateSink()
    {
        var directories = new System.Collections.Generic.List<string>();
        foreach (var folder in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            try
            {
                var root = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(root)) directories.Add(Path.Combine(root, "YeShunguangPet", "logs"));
            }
            catch { }
        }
        try { directories.Add(Path.Combine(Path.GetTempPath(), "YeShunguangPet", "logs")); } catch { }
        return new ResilientLog(directories);
    }
}
