using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YeShunguangPet;

public interface IAiProvider
{
    string Name { get; }
    Task<string> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default);
}

public interface IStreamingAiProvider : IAiProvider
{
    IAsyncEnumerable<string> StreamAsync(AiPrompt prompt, CancellationToken cancellationToken = default);
}

public sealed class OpenAiCompatibleProvider : IStreamingAiProvider
{
    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly HttpClient _http;
    public string Name { get; }

    public OpenAiCompatibleProvider(AiOptions options, string? apiKey = null, HttpClient? http = null)
        : this(Prepare(options), apiKey, http) { }

    public OpenAiCompatibleProvider(AiProviderProfile profile, string? apiKey = null, HttpClient? http = null)
    {
        profile.Normalize(); profile.Validate();
        _endpoint = BuildEndpoint(profile.Endpoint);
        _model = profile.Model;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        Name = profile.Name;
    }

    public async Task<string> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default)
    {
        var request = Request(Messages(prompt), prompt.JsonResponse, stream: false);
        using var message = Message(request);
        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AI 请求失败：{(int)response.StatusCode} {response.ReasonPhrase} {TrimError(body)}".Trim());
        return ParseCompletion(body);
    }

    public async IAsyncEnumerable<string> StreamAsync(AiPrompt prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = Messages(prompt);
        var request = Request(messages, jsonResponse: false, stream: true);
        using var message = Message(request);
        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"AI 请求失败：{(int)response.StatusCode} {response.ReasonPhrase} {TrimError(error)}".Trim());
        }
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            yield return ParseCompletion(body); yield break;
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var received = false;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;
            if (data.Length == 0) continue;
            string? delta;
            try
            {
                using var document = JsonDocument.Parse(data);
                var choice = document.RootElement.GetProperty("choices")[0];
                delta = choice.TryGetProperty("delta", out var update) && update.TryGetProperty("content", out var content)
                    ? content.GetString() : null;
            }
            catch (JsonException ex) { throw new InvalidOperationException("AI 流式响应无法解析。", ex); }
            if (!string.IsNullOrEmpty(delta)) { received = true; yield return delta; }
        }
        if (!received) throw new InvalidOperationException("AI 流式响应没有返回文字。");
    }

    private static AiProviderProfile Prepare(AiOptions options)
    {
        options.Normalize(); options.Validate(); return options.ActiveProfile().Clone();
    }
    private List<object> Messages(AiPrompt prompt)
    {
        var messages = new List<object> { new { role = "system", content = prompt.System } };
        if (prompt.History is not null)
            foreach (var entry in prompt.History)
            {
                if (entry.Role is not ("user" or "assistant")) throw new InvalidOperationException("聊天历史角色无效。");
                messages.Add(new { role = entry.Role, content = entry.Content });
            }
        messages.Add(new { role = "user", content = prompt.User }); return messages;
    }
    private Dictionary<string, object> Request(List<object> messages, bool jsonResponse, bool stream)
    {
        var request = new Dictionary<string, object> { ["model"] = _model, ["messages"] = messages, ["temperature"] = 0.7 };
        if (jsonResponse) request["response_format"] = new { type = "json_object" };
        if (stream) request["stream"] = true;
        return request;
    }
    private HttpRequestMessage Message(Dictionary<string, object> request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json") };
        if (_apiKey is not null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return message;
    }
    private static string TrimError(string value) => value.Length <= 300 ? value : value[..300];
    private static string ParseCompletion(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                ?? throw new InvalidOperationException("AI 返回内容为空。");
        }
        catch (JsonException ex) { throw new InvalidOperationException("AI 返回格式无法解析。", ex); }
    }

    private static Uri BuildEndpoint(string endpoint)
    {
        var value = endpoint.TrimEnd('/');
        if (!value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) value += "/chat/completions";
        return new Uri(value, UriKind.Absolute);
    }
}

public static class AiProviderFactory
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(45) };
    public static IAiProvider? Create(AiOptions options, AiSecretStore secrets, HttpClient? http = null)
    {
        options.Normalize(); options.Validate();
        if (!options.Enabled || options.Provider == AiProviderKind.None) return null;
        var profile = options.ActiveProfile();
        var key = profile.Provider == AiProviderKind.Direct ? secrets.ForProfile(profile.Id).Load() : null;
        if (profile.Provider == AiProviderKind.Direct && string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("直连 AI 服务尚未配置 API Key。");
        return new OpenAiCompatibleProvider(profile, key, http ?? SharedHttp);
    }
}
