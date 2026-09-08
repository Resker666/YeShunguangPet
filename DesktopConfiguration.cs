using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace YeShunguangPet;

public sealed class CompanionOptions
{
    public bool LaunchAtStartup { get; set; }
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public bool BreakRemindersEnabled { get; set; }
    public int BreakReminderMinutes { get; set; } = 60;
    public bool NotificationsEnabled { get; set; } = true;
    public bool PauseDuringFocus { get; set; } = true;
    public bool SessionAnimationEnabled { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public int QuietStartMinute { get; set; } = 1320;
    public int QuietEndMinute { get; set; } = 480;

    public static CompanionOptions From(PetSettings s) => new()
    {
        LaunchAtStartup = s.LaunchAtStartup, FocusMinutes = s.FocusMinutes, BreakMinutes = s.BreakMinutes,
        BreakRemindersEnabled = s.BreakRemindersEnabled, BreakReminderMinutes = s.BreakReminderMinutes,
        NotificationsEnabled = s.NotificationsEnabled, PauseDuringFocus = s.PauseDuringFocus,
        SessionAnimationEnabled = s.SessionAnimationEnabled, DoNotDisturb = s.DoNotDisturb,
        QuietHoursEnabled = s.QuietHoursEnabled, QuietStartMinute = s.QuietStartMinute, QuietEndMinute = s.QuietEndMinute
    };

    public void ApplyTo(PetSettings s)
    {
        s.LaunchAtStartup = LaunchAtStartup;
        s.FocusMinutes = FocusMinutes;
        s.BreakMinutes = BreakMinutes;
        s.BreakRemindersEnabled = BreakRemindersEnabled;
        s.BreakReminderMinutes = BreakReminderMinutes;
        s.NotificationsEnabled = NotificationsEnabled;
        s.PauseDuringFocus = PauseDuringFocus;
        s.SessionAnimationEnabled = SessionAnimationEnabled;
        s.DoNotDisturb = DoNotDisturb;
        s.QuietHoursEnabled = QuietHoursEnabled;
        s.QuietStartMinute = QuietStartMinute;
        s.QuietEndMinute = QuietEndMinute;
    }
}

public sealed class PetInstanceOptions
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public bool Hidden { get; set; }
    public string PetId { get; set; } = PetPackage.DefaultId;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = 1;
    public bool Topmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool EdgeAutoHide { get; set; }
    public bool LookAtMouse { get; set; } = true;
    public bool RandomIdleActions { get; set; } = true;
    public int IdleActionIntervalSeconds { get; set; } = 45;
    public bool DesktopRoaming { get; set; }
    public int RoamIntervalSeconds { get; set; } = 75;
    public double RoamSpeed { get; set; } = 70;
    public bool ClickInteraction { get; set; } = true;
    public bool PauseNearMouse { get; set; } = true;
    public int MousePauseRadius { get; set; } = 80;

    public PetSettings ToSettings(CompanionOptions global)
    {
        var settings = new PetSettings
        {
            SelectedPetId = PetId, Left = Left, Top = Top, Scale = Scale, Topmost = Topmost, ClickThrough = ClickThrough,
            EdgeAutoHide = EdgeAutoHide, LookAtMouse = LookAtMouse, RandomIdleActions = RandomIdleActions,
            IdleActionIntervalSeconds = IdleActionIntervalSeconds, DesktopRoaming = DesktopRoaming,
            RoamIntervalSeconds = RoamIntervalSeconds, RoamSpeed = RoamSpeed, ClickInteraction = ClickInteraction,
            PauseNearMouse = PauseNearMouse, MousePauseRadius = MousePauseRadius
        };
        global.ApplyTo(settings);
        settings.Normalize();
        return settings;
    }

    public void Capture(PetSettings settings)
    {
        PetId = settings.SelectedPetId;
        Left = settings.Left;
        Top = settings.Top;
        Scale = settings.Scale;
        Topmost = settings.Topmost;
        ClickThrough = settings.ClickThrough;
        EdgeAutoHide = settings.EdgeAutoHide;
        LookAtMouse = settings.LookAtMouse;
        RandomIdleActions = settings.RandomIdleActions;
        IdleActionIntervalSeconds = settings.IdleActionIntervalSeconds;
        DesktopRoaming = settings.DesktopRoaming;
        RoamIntervalSeconds = settings.RoamIntervalSeconds;
        RoamSpeed = settings.RoamSpeed;
        ClickInteraction = settings.ClickInteraction;
        PauseNearMouse = settings.PauseNearMouse;
        MousePauseRadius = settings.MousePauseRadius;
    }
}

public sealed class DesktopConfiguration
{
    public const int MaximumPets = 3;
    public int SchemaVersion { get; set; } = 2;
    public CompanionOptions Companion { get; set; } = new();
    public AppearanceOptions Appearance { get; set; } = new();
    public SpeechOptions Speech { get; set; } = new();
    public FocusWindowOptions FocusWindow { get; set; } = new();
    public List<PetInstanceOptions> Pets { get; set; } = new();

    public static DesktopConfiguration Migrate(PetSettings legacy)
    {
        var normalized = legacy.Clone();
        normalized.Normalize();
        var instance = new PetInstanceOptions();
        instance.Capture(normalized);
        return new DesktopConfiguration { Companion = CompanionOptions.From(normalized), Pets = new() { instance } };
    }

    public void Validate()
    {
        if (SchemaVersion != 2 || Companion is null || Pets is null || Pets.Count > MaximumPets)
            throw new InvalidDataException("桌面配置版本或角色数量无效。原配置未被覆盖。");
        Appearance ??= new AppearanceOptions();
        Appearance.Normalize();
        Speech ??= new SpeechOptions();
        Speech.Normalize();
        FocusWindow ??= new FocusWindowOptions();
        FocusWindow.Normalize();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var global = new PetSettings();
        Companion.ApplyTo(global);
        global.Normalize();
        Companion = CompanionOptions.From(global);
        foreach (var instance in Pets)
        {
            if (instance is null || !Guid.TryParseExact(instance.InstanceId, "N", out _) || !ids.Add(instance.InstanceId))
                throw new InvalidDataException("角色实例 ID 无效或重复。原配置未被覆盖。");
            instance.Capture(instance.ToSettings(Companion));
        }
    }
}

public sealed class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string Path { get; }
    private readonly string _legacyPath;

    public DesktopSettingsStore(string? path = null, string? legacyPath = null)
    {
        Path = path ?? System.IO.Path.Combine(PetSettings.SettingsDirectory, "desktop-v2.json");
        _legacyPath = legacyPath ?? PetSettings.SettingsPath;
    }

    public DesktopConfiguration Load()
    {
        try
        {
            DesktopConfiguration configuration;
            if (File.Exists(Path))
            {
                var bytes = Read(Path);
                using var document = JsonDocument.Parse(bytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty(nameof(DesktopConfiguration.SchemaVersion), out var version) ||
                    version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 2)
                    throw new InvalidDataException("桌面配置缺少有效版本号，原文件未被修改。");
                configuration = JsonSerializer.Deserialize<DesktopConfiguration>(bytes) ?? throw new InvalidDataException("桌面配置为空。");
            }
            else configuration = DesktopConfiguration.Migrate(File.Exists(_legacyPath)
                ? JsonSerializer.Deserialize<PetSettings>(Read(_legacyPath)) ?? throw new InvalidDataException("旧配置为空。") : new PetSettings());
            configuration.Validate();
            return configuration;
        }
        catch (JsonException ex) { throw new InvalidDataException("配置无法解析，请恢复配置备份。原文件未修改。", ex); }
    }

    public void Save(DesktopConfiguration configuration)
    {
        configuration.Validate();
        var json = JsonSerializer.SerializeToUtf8Bytes(configuration, Options);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, json);
            if (File.Exists(Path)) File.Replace(temporary, Path, Path + ".bak", ignoreMetadataErrors: false);
            else File.Move(temporary, Path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static byte[] Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("配置文件超过 1 MiB 限制。");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? bytes[3..] : bytes;
    }
}
