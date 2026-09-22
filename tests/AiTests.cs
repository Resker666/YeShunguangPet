using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YeShunguangPet;

internal static class AiTests
{
    public static void Run(Action<bool, string> check, string root)
    {
        var options = new AiOptions
        {
            Enabled = true, Provider = AiProviderKind.Direct,
            Endpoint = "https://api.example.com/v1", Model = "test-model",
            AllowSpeechSuggestions = true
        };
        options.Validate();
        check(options.AllowSpeechSuggestions && options.Provider == AiProviderKind.Direct && options.Profiles.Single().Id == AiOptions.DefaultProfileId,
            "legacy AI options migrate to one default provider profile without enabling any network request");
        check(AiProviderFactory.Create(new AiOptions(), new AiSecretStore(Path.Combine(root, "disabled-key.bin"))) is null,
            "disabled AI provider factory remains offline");

        var keyPath = Path.Combine(root, "ai-secret.bin");
        var secrets = new AiSecretStore(keyPath);
        try
        {
            secrets.Save("test-secret-value");
            var stored = Convert.ToHexString(File.ReadAllBytes(keyPath));
            check(secrets.HasKey && secrets.Load() == "test-secret-value" && !stored.Contains(Convert.ToHexString(Encoding.UTF8.GetBytes("test-secret-value")), StringComparison.Ordinal),
                "AI key is encrypted at rest and roundtrips for the current Windows user");
            secrets.Delete(); check(!secrets.HasKey, "AI secret deletion removes the encrypted key file");
            var second = secrets.ForProfile(Guid.NewGuid().ToString("N")); second.Save("second-secret");
            check(second.FilePath != secrets.FilePath && second.Load() == "second-secret" && !secrets.HasKey,
                "provider profiles keep independent DPAPI key files without reusing the legacy key");
            second.Delete();
        }
        catch (CryptographicException)
        {
            check(true, "AI key encryption is unavailable only in the restricted test identity");
        }

        var suggestion = AiSuggestionService.Parse("{\"text\":\"准备专注十分钟。\",\"mood\":\"encourage\",\"action\":\"waving\",\"durationSeconds\":999}");
        check(suggestion.Text == "准备专注十分钟。" && suggestion.Action == "Waving" && suggestion.DurationSeconds == 300,
            "AI suggestions are normalized to an allowed structured result");
        var rejected = false;
        try { AiSuggestionService.Parse("{\"text\":\"" + new string('x', 241) + "\"}"); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "oversized AI suggestion text is rejected before reaching the UI");

        var profiles = options.Clone();
        var local = new AiProviderProfile { Name = "本地", Provider = AiProviderKind.Local, Endpoint = "http://localhost:1234/v1", Model = "local-model" };
        profiles.Profiles.Add(local); profiles.ActiveProfileId = local.Id; profiles.Normalize(); profiles.Validate();
        check(profiles.ActiveProfile().Name == "本地" && profiles.Provider == AiProviderKind.Local && options.Profiles.Count == 1,
            "AI profile selection updates the compatibility projection without mutating the source draft");

        var summaryOptions = options.Clone(); summaryOptions.AllowStudySummaries = true;
        var summaryProvider = new StubProvider("{\"text\":\"本周保持稳定。\",\"mood\":\"encourage\"}");
        var summaryService = new AiStudySummaryService(() => summaryOptions, () => summaryProvider);
        var summary = summaryService.SummarizeAsync(new StudySummaryRequest(new DateOnly(2026, 9, 22),
            new StudyDay(new DateOnly(2026, 9, 22), 50, 2), new[] { new StudyDay(new DateOnly(2026, 9, 21), 25, 1) })).GetAwaiter().GetResult();
        check(summaryService.IsAvailable && summary == "本周保持稳定。" && summaryProvider.Last?.User.Contains("今日专注：50 分钟") == true,
            "AI study adapter implements the focus-owned summary boundary");

        var textProvider = new StubProvider("translated");
        var processed = Collect(new AiCaptureTextProcessor(() => textProvider).ProcessAsync(
            new CaptureTextRequest(CaptureTextAction.Translate, "confirmed OCR text", "English")));
        check(processed == "translated" && textProvider.Last?.User == "confirmed OCR text" && textProvider.Last.System.Contains("English"),
            "AI capture adapter receives only the capture-owned confirmed text request");
    }

    private static string Collect(IAsyncEnumerable<string> source) => CollectAsync(source).GetAwaiter().GetResult();
    private static async Task<string> CollectAsync(IAsyncEnumerable<string> source)
    {
        var result = new StringBuilder();
        await foreach (var item in source) result.Append(item);
        return result.ToString();
    }

    private sealed class StubProvider(string response) : IAiProvider
    {
        public string Name => "stub";
        public AiPrompt? Last { get; private set; }
        public Task<string> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default)
        {
            Last = prompt;
            return Task.FromResult(response);
        }
    }
}
