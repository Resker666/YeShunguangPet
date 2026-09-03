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
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new PetSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<PetSettings>(json) ?? new PetSettings();
        }
        catch
        {
            return new PetSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public static void SetLaunchAtStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            var exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(exe))
            {
                key.SetValue(AppName, $"\"{exe}\"");
            }
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
