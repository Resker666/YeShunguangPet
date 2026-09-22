using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YeShunguangPet;

public sealed class UpdateOptions
{
    public bool CheckOnStartup { get; set; }
    public UpdateOptions Clone() => new() { CheckOnStartup = CheckOnStartup };
}

public sealed record UpdateCheckResult(Version CurrentVersion, Version LatestVersion, Uri ReleasePage)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
    public string CurrentVersionText => CurrentVersion.ToString(3);
    public string LatestVersionText => LatestVersion.ToString(3);
}

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class GitHubUpdateChecker : IUpdateChecker
{
    public static readonly Uri LatestReleasePage = new("https://github.com/Resker666/YeShunguangPet/releases/latest");
    private static readonly Uri ApiEndpoint = new("https://api.github.com/repos/Resker666/YeShunguangPet/releases/latest");
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private const int MaximumResponseCharacters = 128 * 1024;
    private readonly HttpClient _http;
    private readonly Version _currentVersion;

    public static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? new Version(0, 0, 0) : new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        }
    }

    public GitHubUpdateChecker(HttpClient? http = null, Version? currentVersion = null)
    {
        _http = http ?? SharedHttp;
        _currentVersion = currentVersion ?? CurrentVersion;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoint);
        request.Headers.UserAgent.ParseAdd($"YeShunguangPet/{_currentVersion.ToString(3)}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"更新检查失败：{(int)response.StatusCode} {response.ReasonPhrase}");
        if (response.Content.Headers.ContentLength is > MaximumResponseCharacters)
            throw new InvalidOperationException("更新信息过大，已停止读取。");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > MaximumResponseCharacters) throw new InvalidOperationException("更新信息过大，已停止读取。");
        try
        {
            using var document = JsonDocument.Parse(body);
            var tag = document.RootElement.GetProperty("tag_name").GetString()?.Trim() ?? string.Empty;
            var value = tag.StartsWith('v') ? tag[1..] : tag;
            if (value.Split('.').Length != 3 || !Version.TryParse(value, out var latest) || latest.Major < 0 || latest.Minor < 0 || latest.Build < 0)
                throw new InvalidOperationException("GitHub Release 版本号无效。");
            var releasePage = new Uri($"https://github.com/Resker666/YeShunguangPet/releases/tag/{Uri.EscapeDataString(tag)}");
            return new UpdateCheckResult(_currentVersion, latest, releasePage);
        }
        catch (JsonException ex) { throw new InvalidOperationException("GitHub Release 信息无法解析。", ex); }
        catch (KeyNotFoundException ex) { throw new InvalidOperationException("GitHub Release 信息缺少版本号。", ex); }
    }

    public static void OpenReleasePage(Uri? page = null)
    {
        var target = page ?? LatestReleasePage;
        if (target.Scheme != Uri.UriSchemeHttps || !target.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("更新页面地址无效。");
        Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
    }
}
