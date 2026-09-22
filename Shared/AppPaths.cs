using System;
using System.IO;

namespace YeShunguangPet;

public static class AppPaths
{
    public const string AppName = "YeShunguangPet";

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string LegacySettingsPath => Path.Combine(SettingsDirectory, "settings.json");
    public static string DesktopSettingsPath => Path.Combine(SettingsDirectory, "desktop-v2.json");
    public static string StudyHistoryPath => Path.Combine(SettingsDirectory, "study-history.json");
    public static string AiSecretPath => Path.Combine(SettingsDirectory, "ai-secret.bin");
    public static string AiCharactersDirectory => Path.Combine(SettingsDirectory, "ai-characters");
    public static string UserPetsDirectory => Path.Combine(SettingsDirectory, "Pets");
    public static string LogsDirectory => Path.Combine(SettingsDirectory, "logs");
}
