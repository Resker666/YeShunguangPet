using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal static class ArchitectureTests
{
    private static readonly Dictionary<string, string[]> ForbiddenReferences = new(StringComparer.Ordinal)
    {
        ["Focus"] = new[] { "AiOptions", "IAiProvider", "AiProviderFactory", "AiSecretStore", "AiPrompt", "AiSuggestionService", "DesktopBehavior", "DesktopSession", "MainWindow" },
        ["Capture"] = new[] { "AiOptions", "IAiProvider", "AiProviderFactory", "AiSecretStore", "AiPrompt", "AiSuggestionService", "DesktopSession", "MainWindow", "PetPackage", "PetSettings" },
        ["AI"] = new[] { "DesktopSession", "MainWindow", "DesktopConfiguration" },
        ["Infrastructure"] = new[] { "DesktopSession", "MainWindow", "PetPackage", "PetSettings", "CompanionRuntime", "CaptureSelection", "AiOptions" },
        ["PetSystem"] = new[] { "DesktopSession", "MainWindow", "CompanionRuntime", "CaptureSelection", "AiOptions", "DesktopBehavior" },
        ["Shared"] = new[] { "DesktopSession", "MainWindow", "PetPackage", "PetSettings", "CompanionRuntime", "CaptureSelection", "AiOptions", "AppLogger", "NativeMethods" }
    };

    public static void Run(Action<bool, string> check, string source)
    {
        var rootSources = Directory.GetFiles(source, "*.cs", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        check(rootSources.SequenceEqual(new[] { "App.xaml.cs" }), "production root retains only the WPF composition entry point");
        check(!Directory.EnumerateFiles(Path.Combine(source, "Pets"), "*.cs", SearchOption.AllDirectories).Any() &&
              !Directory.EnumerateFiles(Path.Combine(source, "Pets"), "*.xaml", SearchOption.AllDirectories).Any(),
            "runtime Pets tree contains assets rather than application source");

        foreach (var (module, forbidden) in ForbiddenReferences)
        {
            var directory = Path.Combine(source, module);
            var violations = new List<string>();
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var identifier in forbidden)
                    if (Regex.IsMatch(text, $@"\b{Regex.Escape(identifier)}\b", RegexOptions.CultureInvariant))
                        violations.Add($"{Path.GetRelativePath(source, file)} -> {identifier}");
            }
            check(violations.Count == 0, violations.Count == 0
                ? $"{module} keeps its declared dependency direction"
                : $"{module} keeps its declared dependency direction [{string.Join(" | ", violations)}]");
        }
    }
}
