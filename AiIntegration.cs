using System;
using System.IO;

namespace YeShunguangPet;

public enum AiProviderKind { None, Direct, Relay, Local }

public sealed class AiOptions
{
    public bool Enabled { get; set; }
    public AiProviderKind Provider { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool AllowSpeechSuggestions { get; set; }
    public bool AllowStudySummaries { get; set; }
    public AiOptions Clone() => new()
    {
        Enabled = Enabled, Provider = Provider, Endpoint = Endpoint, Model = Model,
        AllowSpeechSuggestions = AllowSpeechSuggestions, AllowStudySummaries = AllowStudySummaries
    };

    public void Normalize()
    {
        Endpoint = (Endpoint ?? string.Empty).Trim();
        Model = (Model ?? string.Empty).Trim();
        if (Endpoint.Length > 512) Endpoint = Endpoint[..512];
        if (Model.Length > 128) Model = Model[..128];
        if (!Enum.IsDefined(Provider)) Provider = AiProviderKind.None;
        if (!Enabled) { AllowSpeechSuggestions = false; AllowStudySummaries = false; }
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Provider)) throw new InvalidDataException("AI 服务商配置无效。");
        if (!Enabled || Provider == AiProviderKind.None) return;
        if (string.IsNullOrWhiteSpace(Model)) throw new InvalidDataException("AI 模型不能为空。");
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new InvalidDataException("AI 服务地址必须是 http 或 https URL。");
        if (Provider == AiProviderKind.Direct && uri.Scheme != "https" && !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) && uri.Host != "127.0.0.1")
            throw new InvalidDataException("直连 AI 服务必须使用 HTTPS，localhost 除外。");
    }
}

public sealed record AiPrompt(string System, string User);
public sealed record AiSuggestion(string Text, string? Mood, string? Action, int? DurationSeconds);
