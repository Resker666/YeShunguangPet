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
    public string SelectedPetId { get; set; } = PetPackage.DefaultId;
    public double? Top { get; set; }
    public double Scale { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool EdgeAutoHide { get; set; }
    public bool LaunchAtStartup { get; set; }
    public bool LookAtMouse { get; set; } = true;
    public bool RandomIdleActions { get; set; } = true;
    public int IdleActionIntervalSeconds { get; set; } = 45;
    public bool DesktopRoaming { get; set; }
    public int RoamIntervalSeconds { get; set; } = 75;
    public double RoamSpeed { get; set; } = 70;
    public bool ClickInteraction { get; set; } = true;
    public bool PauseNearMouse { get; set; } = true;
    public int MousePauseRadius { get; set; } = 80;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public bool BreakRemindersEnabled { get; set; }
    public int BreakReminderMinutes { get; set; } = 60;
    public bool NotificationsEnabled { get; set; } = true;
    public bool PauseDuringFocus { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public int QuietStartMinute { get; set; } = 22 * 60;
    public int QuietEndMinute { get; set; } = 8 * 60;

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

    public PetSettings Clone()
    {
        return new PetSettings
        {
            Left = Left,
            SelectedPetId = SelectedPetId,
            Top = Top,
            Scale = Scale,
            Topmost = Topmost,
            ClickThrough = ClickThrough,
            EdgeAutoHide = EdgeAutoHide,
            LaunchAtStartup = LaunchAtStartup,
            LookAtMouse = LookAtMouse,
            RandomIdleActions = RandomIdleActions,
            IdleActionIntervalSeconds = IdleActionIntervalSeconds,
            DesktopRoaming = DesktopRoaming,
            RoamIntervalSeconds = RoamIntervalSeconds,
            RoamSpeed = RoamSpeed,
            ClickInteraction = ClickInteraction,
            PauseNearMouse = PauseNearMouse,
            MousePauseRadius = MousePauseRadius,
            FocusMinutes = FocusMinutes,
            BreakMinutes = BreakMinutes,
            BreakRemindersEnabled = BreakRemindersEnabled,
            BreakReminderMinutes = BreakReminderMinutes,
            NotificationsEnabled = NotificationsEnabled,
            PauseDuringFocus = PauseDuringFocus,
            DoNotDisturb = DoNotDisturb,
            QuietHoursEnabled = QuietHoursEnabled,
            QuietStartMinute = QuietStartMinute,
            QuietEndMinute = QuietEndMinute
        };
    }

    private void Normalize()
    {
        if (string.IsNullOrWhiteSpace(SelectedPetId)) SelectedPetId = PetPackage.DefaultId;
        if (!double.IsFinite(Scale))
        {
            Scale = 1.0;
        }

        Scale = Math.Clamp(Scale, 0.5, 2.5);
        IdleActionIntervalSeconds = Math.Clamp(IdleActionIntervalSeconds, 15, 120);
        RoamIntervalSeconds = Math.Clamp(RoamIntervalSeconds, 20, 180);

        if (!double.IsFinite(RoamSpeed))
        {
            RoamSpeed = 70;
        }

        RoamSpeed = Math.Clamp(RoamSpeed, 20, 160);
        MousePauseRadius = Math.Clamp(MousePauseRadius, 20, 200);
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 120);
        BreakMinutes = Math.Clamp(BreakMinutes, 1, 60);
        BreakReminderMinutes = Math.Clamp(BreakReminderMinutes, 15, 180);
        QuietStartMinute = Math.Clamp(QuietStartMinute, 0, 1439);
        QuietEndMinute = Math.Clamp(QuietEndMinute, 0, 1439);

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
