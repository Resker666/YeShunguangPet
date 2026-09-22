using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace YeShunguangPet;

public enum AiProviderKind { None, Direct, Relay, Local }

public sealed class AiProviderProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新配置";
    public AiProviderKind Provider { get; set; } = AiProviderKind.Direct;
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
    public AiProviderProfile Clone() => new() { Id = Id, Name = Name, Provider = Provider, Endpoint = Endpoint, Model = Model };
    public void Normalize()
    {
        Id = (Id ?? string.Empty).Trim(); Name = (Name ?? string.Empty).Trim();
        Endpoint = (Endpoint ?? string.Empty).Trim(); Model = (Model ?? string.Empty).Trim();
        if (Name.Length > 40) Name = Name[..40];
        if (Endpoint.Length > 512) Endpoint = Endpoint[..512];
        if (Model.Length > 128) Model = Model[..128];
    }
    public void Validate()
    {
        if (Id != AiOptions.DefaultProfileId && !Guid.TryParseExact(Id, "N", out _)) throw new InvalidDataException("AI 配置标识无效。");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("AI 配置名称不能为空。");
        if (Provider is AiProviderKind.None || !Enum.IsDefined(Provider)) throw new InvalidDataException("AI 服务类型无效。");
        if (string.IsNullOrWhiteSpace(Model)) throw new InvalidDataException("AI 模型不能为空。");
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new InvalidDataException("AI 服务地址必须是 http 或 https URL。");
        if (Provider == AiProviderKind.Direct && uri.Scheme != "https" && !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) && uri.Host != "127.0.0.1")
            throw new InvalidDataException("直连 AI 服务必须使用 HTTPS，localhost 除外。");
    }
}

public sealed class AiOptions
{
    public const string DefaultProfileId = "default";
    public bool Enabled { get; set; }
    public AiProviderKind Provider { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool AllowSpeechSuggestions { get; set; }
    public bool AllowStudySummaries { get; set; }
    public bool AllowCaptureAssistant { get; set; }
    public string ActiveProfileId { get; set; } = string.Empty;
    public List<AiProviderProfile> Profiles { get; set; } = new();
    public AiOptions Clone() => new()
    {
        Enabled = Enabled, Provider = Provider, Endpoint = Endpoint, Model = Model,
        AllowSpeechSuggestions = AllowSpeechSuggestions, AllowStudySummaries = AllowStudySummaries, AllowCaptureAssistant = AllowCaptureAssistant,
        ActiveProfileId = ActiveProfileId, Profiles = Profiles.Select(profile => profile.Clone()).ToList()
    };

    public void Normalize()
    {
        Endpoint = (Endpoint ?? string.Empty).Trim();
        Model = (Model ?? string.Empty).Trim();
        if (Endpoint.Length > 512) Endpoint = Endpoint[..512];
        if (Model.Length > 128) Model = Model[..128];
        if (!Enum.IsDefined(Provider)) Provider = AiProviderKind.None;
        Profiles ??= new();
        if (Profiles.Count == 0)
        {
            Profiles.Add(new AiProviderProfile
            {
                Id = DefaultProfileId, Name = "默认配置", Provider = Provider == AiProviderKind.None ? AiProviderKind.Direct : Provider,
                Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? "https://api.openai.com/v1" : Endpoint,
                Model = string.IsNullOrWhiteSpace(Model) ? "gpt-4o-mini" : Model
            });
        }
        foreach (var profile in Profiles) profile?.Normalize();
        if (Profiles.All(profile => profile?.Id != ActiveProfileId)) ActiveProfileId = Profiles[0]?.Id ?? string.Empty;
        if (Profiles.FirstOrDefault(profile => profile?.Id == ActiveProfileId) is { } active)
        {
            Provider = active.Provider; Endpoint = active.Endpoint; Model = active.Model;
        }
        if (!Enabled) { AllowSpeechSuggestions = false; AllowStudySummaries = false; AllowCaptureAssistant = false; }
    }

    public void Validate()
    {
        Normalize();
        if (Profiles is null || Profiles.Count is < 1 or > 8 || Profiles.Any(profile => profile is null)) throw new InvalidDataException("AI 配置数量无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in Profiles) { profile.Validate(); if (!ids.Add(profile.Id)) throw new InvalidDataException("AI 配置标识重复。"); }
        if (!ids.Contains(ActiveProfileId)) throw new InvalidDataException("当前 AI 配置无效。");
    }

    public AiProviderProfile ActiveProfile() => Profiles.First(profile => profile.Id == ActiveProfileId);
}

public sealed record AiPrompt(string System, string User)
{
    public IReadOnlyList<AiChatMessage>? History { get; init; }
    public bool JsonResponse { get; init; } = true;
}
public sealed record AiSuggestion(string Text, string? Mood, string? Action, int? DurationSeconds);
