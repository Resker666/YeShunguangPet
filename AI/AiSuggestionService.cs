using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YeShunguangPet;

public sealed class AiSuggestionService
{
    private readonly IAiProvider _provider;
    public AiSuggestionService(IAiProvider provider) => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public async Task<AiSuggestion> SuggestAsync(AiPrompt prompt, CancellationToken cancellationToken = default, int maxTextLength = 240)
    {
        var raw = await _provider.CompleteAsync(prompt, cancellationToken);
        return Parse(raw, maxTextLength);
    }

    public static AiSuggestion Parse(string json, int maxTextLength = 240)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var text = root.TryGetProperty("text", out var textValue) ? textValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxTextLength) throw new InvalidDataException("AI 建议文本为空或超出长度限制。");
        var mood = root.TryGetProperty("mood", out var moodValue) ? moodValue.GetString() : null;
        var action = root.TryGetProperty("action", out var actionValue) ? actionValue.GetString() : null;
        if (action is not null)
            action = Enum.TryParse<PetState>(action, true, out var state) && Enum.IsDefined(state) ? state.ToString() : null;
        int? duration = null;
        if (root.TryGetProperty("durationSeconds", out var durationValue) && durationValue.ValueKind == JsonValueKind.Number && durationValue.TryGetInt32(out var seconds))
            duration = Math.Clamp(seconds, 0, 300);
        return new AiSuggestion(text.Trim(), mood?.Trim(), action, duration);
    }
}
