using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YeShunguangPet;

internal static class AiChatTests
{
    public static void Run(Action<bool, string> check, string root) => RunAsync(check, root).GetAwaiter().GetResult();

    private static async Task RunAsync(Action<bool, string> check, string root)
    {
        var directory = Path.Combine(root, "ai-chat");
        var store = new AiChatStore(directory);
        var session = new AiChatSession(store, "../character-a", "小光", "默认背景");
        check(session.State.Profile.Background == "默认背景", "new chat uses the manifest description as background");
        check(!session.State.Profile.BuildContext("小光").Contains("不要返回 JSON"), "shared persona does not contradict structured speech suggestions");
        session.SaveProfile(new AiCharacterProfile { Background = "", Personality = "开朗", SpeakingStyle = "简短口语", Examples = "嗨！" });
        var provider = new FakeProvider();
        await session.SendAsync(provider, "你好", default);
        check(provider.Prompt is { JsonResponse: false } && provider.Prompt.System.Contains("开朗") && provider.Prompt.System.Contains("小光"), "chat request includes editable character persona and requests natural text");
        await session.SendAsync(provider, "还记得吗", default);
        check(provider.Prompt!.History!.Select(x => x.Role).SequenceEqual(new[] { "user", "assistant" }), "second chat request carries typed complete history");
        var reloaded = new AiChatSession(store, "../character-a", "小光", "新描述");
        check(reloaded.State.Profile.Background == "" && reloaded.State.Messages.Count == 4, "saved empty background and conversation survive reload");
        check(new AiChatSession(store, "character-b", "乙", "乙背景").State.Messages.Count == 0 && Directory.GetFiles(directory).Length == 1, "character chat is isolated and unsafe IDs stay inside the store");
        var before = File.ReadAllText(Directory.GetFiles(directory).Single());
        provider.Failure = new InvalidOperationException("offline");
        await Reject(() => session.SendAsync(provider, "失败", default), check, "provider failure leaves conversation unchanged");
        provider.Failure = null;
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await Reject(() => session.SendAsync(provider, "取消", canceled.Token), check, "canceled request is rejected");
        }
        check(session.State.Messages.Count == 4 && File.ReadAllText(Directory.GetFiles(directory).Single()) == before, "failed and canceled sends do not alter persisted history");
        using (var canceled = new CancellationTokenSource())
        {
            var delayed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Pending = delayed.Task;
            var canceledSend = session.SendAsync(provider, "中途取消", canceled.Token);
            canceled.Cancel(); delayed.SetResult("迟到回复");
            await Reject(() => canceledSend, check, "late provider completion after cancellation is discarded");
            check(session.State.Messages.Count == 4, "in-flight cancellation preserves complete conversation pairs");
            provider.Pending = null;
        }
        await Reject(() => session.SendAsync(provider, new string('x', 2001), default), check, "chat rejects oversized user input");
        provider.Answer = new string('x', 8001);
        await Reject(() => session.SendAsync(provider, "输出过长", default), check, "chat rejects oversized assistant output");
        provider.Answer = "回答";
        for (var i = 0; i < 13; i++) await session.SendAsync(provider, "问题" + i, default);
        check(session.State.Messages.Count == 24 && session.State.Messages[0].Content == "问题1", "chat retains only the latest twelve complete exchanges");
        var blocked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Pending = blocked.Task;
        var pending = session.SendAsync(provider, "等待", default);
        await Reject(() => session.SendAsync(provider, "同时发送", default), check, "chat prevents overlapping sends");
        blocked.SetResult("完成"); await pending; provider.Pending = null;
        var file = Directory.GetFiles(directory).Single();
        using (var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Reject(() => session.SendAsync(provider, "保存失败", default), check, "failed save rejects the exchange");
        check(session.State.Messages[^2].Content == "等待" && new AiChatSession(store, "../character-a", "光", "").State.Messages[^2].Content == "等待", "failed save preserves in-memory and on-disk history");
        await Reject(() => Task.Run(() => session.SaveProfile(new AiCharacterProfile { Examples = new string('x', 8001) })), check, "profile rejects oversized fields");
        session.ClearHistory();
        check(store.Load("../character-a")!.Messages.Count == 0 && session.State.Profile.Personality == "开朗", "clear history persists while retaining persona");
        await Reject(() => Task.Run(() => store.Save("invalid", new AiChatState { Messages = new() { new("system", "injected") } })), check, "store rejects incomplete or injected-role histories");
        File.WriteAllText(file, "{broken");
        await Reject(() => Task.Run(() => store.Load("../character-a")), check, "corrupt chat archive is reported without silently overwriting it");
        check(File.ReadAllText(file) == "{broken", "failed load preserves corrupt archive for recovery");

        var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var live = new OpenAiCompatibleProvider(new AiOptions { Enabled = true, Provider = AiProviderKind.Local, Endpoint = "http://localhost:1234/v1", Model = "model" }, http: http);
        await live.CompleteAsync(new AiPrompt("system", "latest") { JsonResponse = false, History = new[] { new AiChatMessage("user", "prior"), new AiChatMessage("assistant", "reply") } });
        using (var json = JsonDocument.Parse(handler.Body!))
        {
            check(!json.RootElement.TryGetProperty("response_format", out _) && json.RootElement.GetProperty("messages").EnumerateArray().Select(x => x.GetProperty("role").GetString()).SequenceEqual(new[] { "system", "user", "assistant", "user" }), "provider serializes ordered chat roles without JSON mode");
        }
        await live.CompleteAsync(new AiPrompt("system", "suggestion"));
        using (var json = JsonDocument.Parse(handler.Body!))
            check(json.RootElement.GetProperty("response_format").GetProperty("type").GetString() == "json_object", "existing suggestions preserve JSON response mode");
        var fallback = new StringBuilder();
        await foreach (var delta in live.StreamAsync(new AiPrompt("system", "fallback") { JsonResponse = false })) fallback.Append(delta);
        check(fallback.ToString() == "answer", "streaming provider falls back to a regular JSON response for compatible local services");

        var streamHandler = new StreamHandler(); using var streamHttp = new HttpClient(streamHandler);
        var streamProvider = new OpenAiCompatibleProvider(new AiProviderProfile { Name = "stream", Provider = AiProviderKind.Local,
            Endpoint = "http://localhost:1234/v1", Model = "model" }, http: streamHttp);
        var streamed = new StringBuilder();
        await foreach (var delta in streamProvider.StreamAsync(new AiPrompt("system", "stream") { JsonResponse = false })) streamed.Append(delta);
        using (var json = JsonDocument.Parse(streamHandler.Body!))
            check(streamed.ToString() == "流式回复" && json.RootElement.GetProperty("stream").GetBoolean(),
                "OpenAI-compatible streaming parses SSE deltas and marks the request as streaming");

        var streamStore = new AiChatStore(Path.Combine(root, "ai-chat-stream"));
        var streamSession = new AiChatSession(streamStore, "stream-character", "流光", "");
        var progress = new ProgressCapture();
        await streamSession.SendStreamingAsync(new StreamingProvider(), "开始", progress, default);
        check(progress.Values.SequenceEqual(new[] { "分段", "分段完成" }) && streamSession.State.Messages[^1].Content == "分段完成",
            "streaming chat reports cumulative text but persists only the complete exchange");
    }

    private static async Task Reject(Func<Task> action, Action<bool, string> check, string name)
    {
        try { await action(); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or OperationCanceledException or IOException or UnauthorizedAccessException) { check(true, name); return; }
        check(false, name);
    }

    private sealed class FakeProvider : IAiProvider
    {
        public string Name => "Fake";
        public AiPrompt? Prompt;
        public Exception? Failure;
        public string Answer = "回答";
        public Task<string>? Pending;
        public Task<string> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default)
        {
            Prompt = prompt;
            return Failure is not null ? Task.FromException<string>(Failure) : Pending ?? Task.FromResult(Answer);
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"answer\"}}]}", Encoding.UTF8, "application/json") };
        }
    }
    private sealed class StreamHandler : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"流式\"}}]}\n\n" +
                      "data: {\"choices\":[{\"delta\":{\"content\":\"回复\"}}]}\n\n" + "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
        }
    }
    private sealed class StreamingProvider : IStreamingAiProvider
    {
        public string Name => "streaming-test";
        public Task<string> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public async IAsyncEnumerable<string> StreamAsync(AiPrompt prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return "分段"; await Task.Yield(); cancellationToken.ThrowIfCancellationRequested(); yield return "完成";
        }
    }
    private sealed class ProgressCapture : IProgress<string>
    {
        public System.Collections.Generic.List<string> Values { get; } = new();
        public void Report(string value) => Values.Add(value);
    }
}
