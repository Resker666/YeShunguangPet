using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace YeShunguangPet;

public sealed class PetSettings
{
    private const string AppName = "YeShunguangPet";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool LaunchAtStartup { get; set; }

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static PetSettings Load()
    {
        PetSettings settings;
        try
        {
            settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(SettingsPath)) ?? new PetSettings()
                : new PetSettings();
        }
        catch
        {
            settings = new PetSettings();
        }

        settings.Normalize();
        settings.LaunchAtStartup = IsLaunchAtStartupEnabled();
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = SettingsPath + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static void SetLaunchAtStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的开机启动设置。");

        if (enabled)
        {
            var exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(exe))
            {
                throw new InvalidOperationException("无法确定当前程序路径。");
            }

            key.SetValue(AppName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    public static bool IsLaunchAtStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var configuredCommand = key?.GetValue(AppName) as string;
            var exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(configuredCommand) || string.IsNullOrWhiteSpace(exe))
            {
                return false;
            }

            var trimmedCommand = configuredCommand.Trim();
            return string.Equals(trimmedCommand, exe, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmedCommand, $"\"{exe}\"", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void Normalize()
    {
        if (!double.IsFinite(Scale) || Scale <= 0)
        {
            Scale = 1.0;
        }

        if (Left.HasValue && !double.IsFinite(Left.Value))
        {
            Left = null;
        }

        if (Top.HasValue && !double.IsFinite(Top.Value))
        {
            Top = null;
        }
    }
}
