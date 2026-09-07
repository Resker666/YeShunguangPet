using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace YeShunguangPet;

public static class AppLogger
{
    private const long MaxLogLength = 2 * 1024 * 1024;
    private static readonly object SyncRoot = new();
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static string LogsDirectory => Path.Combine(PetSettings.SettingsDirectory, "logs");
    public static string LogPath => Path.Combine(LogsDirectory, "app.log");

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(LogsDirectory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogLength)
                {
                    File.Move(LogPath, Path.Combine(LogsDirectory, "app.previous.log"), overwrite: true);
                }
            }
            catch
            {
                // Logging must never prevent the pet from starting.
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", detail);
    }

    public static void OpenFolder()
    {
        Initialize();
        Process.Start(new ProcessStartInfo
        {
            FileName = LogsDirectory,
            UseShellExecute = true
        });
    }

    private static void Write(string level, string message)
    {
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(LogsDirectory);
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Utf8WithoutBom);
            }
            catch
            {
                // Logging must remain best-effort.
            }
        }
    }
}
