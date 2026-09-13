using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YeShunguangPet;

public sealed class AiCharacterProfile
{
    public string Background { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string SpeakingStyle { get; set; } = string.Empty;
    public string Examples { get; set; } = string.Empty;

    public string BuildContext(string name)
    {
        Validate();
        return $"你正在以桌宠角色「{name}」进行自然、亲切的虚构角色扮演。这是用户自定义的非官方演绎，不代表原作官方设定。" +
            "结合下列人物设定，用角色的口吻回应当前对话，保持连贯；不要机械地重复设定或每次介绍身份。" +
            "你只能看到本次提供的角色设定和聊天内容，不要声称读取了桌面、屏幕、文件或用户没有提供的信息。\n" +
            $"背景：{Background}\n性格：{Personality}\n说话风格：{SpeakingStyle}\n示例台词：{Examples}";
    }

    internal void Validate()
    {
        AiChatStore.ValidateText(Background, 2000, "角色背景");
        AiChatStore.ValidateText(Personality, 1000, "角色性格");
        AiChatStore.ValidateText(SpeakingStyle, 1000, "说话风格");
        AiChatStore.ValidateText(Examples, 2000, "示例台词");
    }

    internal AiCharacterProfile Copy() => new() { Background = Background, Personality = Personality, SpeakingStyle = SpeakingStyle, Examples = Examples };
}

public sealed record AiChatMessage(string Role, string Content);

public sealed class AiChatState
{
    public AiCharacterProfile Profile { get; set; } = new();
    public List<AiChatMessage> Messages { get; set; } = new();
    internal AiChatState Copy() => new() { Profile = Profile.Copy(), Messages = new(Messages) };
}

public sealed class AiChatStore
{
    private readonly string _directory;
    public AiChatStore(string? directory = null) => _directory = directory ?? Path.Combine(PetSettings.SettingsDirectory, "ai-characters");

    public AiChatState? Load(string id)
    {
        var path = GetPath(id);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("聊天存档过大。");
        AiChatState state;
        try { state = JsonSerializer.Deserialize<AiChatState>(File.ReadAllText(path)) ?? throw new InvalidDataException("聊天存档为空。"); }
        catch (JsonException ex) { throw new InvalidDataException("聊天存档无法读取。", ex); }
        Validate(state);
        return state;
    }

    public void Save(string id, AiChatState state)
    {
        Validate(state);
        var path = GetPath(id);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(_directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string GetPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("角色标识不能为空。");
        return Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
    }

    internal static void ValidateText(string? text, int limit, string label)
    {
        if (text is null || text.Length > limit) throw new InvalidDataException($"{label}最多 {limit} 个字符。");
    }

    private static void Validate(AiChatState state)
    {
        if (state is null || state.Profile is null || state.Messages is null) throw new InvalidDataException("聊天存档内容无效。");
        state.Profile.Validate();
        if (state.Messages.Count > 24 || state.Messages.Count % 2 != 0) throw new InvalidDataException("聊天历史必须包含最多 12 轮完整对话。");
        for (var i = 0; i < state.Messages.Count; i++)
        {
            var entry = state.Messages[i];
            if (entry is null || entry.Role != (i % 2 == 0 ? "user" : "assistant") || string.IsNullOrWhiteSpace(entry.Content))
                throw new InvalidDataException("聊天历史角色或内容无效。");
            ValidateText(entry.Content, i % 2 == 0 ? 2000 : 8000, "聊天内容");
        }
    }
}

public sealed class AiChatSession
{
    private readonly AiChatStore _store;
    private readonly string _id;
    private readonly string _name;
    private int _busy;
    public AiChatState State { get; private set; }

    public AiChatSession(AiChatStore store, string id, string name, string description)
    {
        _store = store; _id = id; _name = name;
        State = store.Load(id) ?? new AiChatState { Profile = new AiCharacterProfile { Background = description ?? string.Empty } };
    }

    public void SaveProfile(AiCharacterProfile profile)
    {
        Enter();
        try
        {
            profile.Validate();
            var next = State.Copy(); next.Profile = profile.Copy();
            _store.Save(_id, next); State = next;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    public void ClearHistory()
    {
        Enter();
        try
        {
            var next = State.Copy(); next.Messages.Clear();
            _store.Save(_id, next); State = next;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    public async Task<string> SendAsync(IAiProvider provider, string input, CancellationToken token)
    {
        Enter();
        try
        {
            token.ThrowIfCancellationRequested();
            AiChatStore.ValidateText(input, 2000, "消息");
            if (string.IsNullOrWhiteSpace(input)) throw new InvalidDataException("请输入消息。");
            var next = State.Copy();
            var prompt = new AiPrompt(next.Profile.BuildContext(_name) + "\n直接返回自然语言，不要返回 JSON。", input.Trim()) { JsonResponse = false, History = next.Messages.ToArray() };
            var answer = await provider.CompleteAsync(prompt, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            AiChatStore.ValidateText(answer, 8000, "AI 回复");
            if (string.IsNullOrWhiteSpace(answer)) throw new InvalidDataException("AI 返回内容为空。");
            answer = answer.Trim();
            next.Messages.Add(new AiChatMessage("user", input.Trim()));
            next.Messages.Add(new AiChatMessage("assistant", answer));
            if (next.Messages.Count > 24) next.Messages.RemoveRange(0, next.Messages.Count - 24);
            _store.Save(_id, next); State = next;
            return answer;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private void Enter()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("正在回复，请稍候。");
    }
}
