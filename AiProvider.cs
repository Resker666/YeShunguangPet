using System;
using System.Net.Http;
using System.Net.Http.Headers;
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

public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly HttpClient _http;
    public string Name { get; }

    public OpenAiCompatibleProvider(AiOptions options, string? apiKey = null, HttpClient? http = null)
    {
        options.Normalize(); options.Validate();
        _endpoint = BuildEndpoint(options.Endpoint);
        _model = options.Model;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        Name = options.Provider.ToString();
    }

    public async Task<string> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            model = _model,
            messages = new[] { new { role = "system", content = prompt.System }, new { role = "user", content = prompt.User } },
            temperature = 0.7,
            response_format = new { type = "json_object" }
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        if (_apiKey is not null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AI 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}");
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? throw new InvalidOperationException("AI 返回内容为空。");
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
        var key = options.Provider == AiProviderKind.Direct ? secrets.Load() : null;
        if (options.Provider == AiProviderKind.Direct && string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("直连 AI 服务尚未配置 API Key。");
        return new OpenAiCompatibleProvider(options, key, http ?? SharedHttp);
    }
}
