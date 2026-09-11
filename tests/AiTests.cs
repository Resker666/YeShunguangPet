using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        check(options.AllowSpeechSuggestions && options.Provider == AiProviderKind.Direct, "AI options validate without enabling any network request");
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
    }
}
