using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YeShunguangPet;

internal static class UpdateTests
{
    public static void Run(Action<bool, string> check, string root)
    {
        var handler = new ResponseHandler("{\"tag_name\":\"v2.27.0\"}");
        using var http = new HttpClient(handler);
        var checker = new GitHubUpdateChecker(http, new Version(2, 26, 0));
        var result = checker.CheckAsync().GetAwaiter().GetResult();
        check(result.IsUpdateAvailable && result.CurrentVersionText == "2.26.0" && result.LatestVersionText == "2.27.0" &&
              result.ReleasePage.AbsoluteUri.EndsWith("/releases/tag/v2.27.0", StringComparison.Ordinal),
            "update checker compares the current assembly version with the latest GitHub release");
        check(handler.LastRequest is not null && handler.LastRequest.RequestUri?.Host == "api.github.com" &&
              handler.LastRequest.Headers.UserAgent.ToString().Contains("YeShunguangPet/2.26.0", StringComparison.Ordinal),
            "update checker sends only a bounded GitHub request with an application user agent");

        using var olderHttp = new HttpClient(new ResponseHandler("{\"tag_name\":\"v2.25.0\"}"));
        var older = new GitHubUpdateChecker(olderHttp, new Version(2, 26, 0)).CheckAsync().GetAwaiter().GetResult();
        check(!older.IsUpdateAvailable, "older GitHub releases never produce a false update notification");
        check(Rejects(() => new GitHubUpdateChecker(new HttpClient(new ResponseHandler("{\"tag_name\":\"latest\"}")), new Version(2, 26, 0))
            .CheckAsync().GetAwaiter().GetResult()), "malformed release versions are rejected");
        check(Rejects(() => new GitHubUpdateChecker(new HttpClient(new ResponseHandler("{}")), new Version(2, 26, 0))
            .CheckAsync().GetAwaiter().GetResult()), "release responses without a tag are rejected");

        var path = Path.Combine(root, "updates-desktop.json");
        var store = new DesktopSettingsStore(path, Path.Combine(root, "updates-legacy.json"));
        var configuration = new DesktopConfiguration(); configuration.Validate();
        check(!configuration.Updates.CheckOnStartup, "existing configurations remain offline by default");
        configuration.Updates.CheckOnStartup = true; store.Save(configuration);
        check(store.Load().Updates.CheckOnStartup, "startup update preference survives the desktop configuration roundtrip");
    }

    internal sealed class FakeChecker(UpdateCheckResult result) : IUpdateChecker
    {
        public int Calls { get; private set; }
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(result);
        }
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
    }

    private sealed class ResponseHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
