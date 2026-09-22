using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YeShunguangPet;

public enum SpeechEvent { Click, Drag, FocusCompleted, BreakCompleted }

public sealed class SpeechLines
{
    public string[] Click { get; set; } = Array.Empty<string>();
    public string[] Drag { get; set; } = Array.Empty<string>();
    public string[] FocusCompleted { get; set; } = Array.Empty<string>();
    public string[] BreakCompleted { get; set; } = Array.Empty<string>();
    public string[] For(SpeechEvent trigger) => trigger switch
    {
        SpeechEvent.Click => Click,
        SpeechEvent.Drag => Drag,
        SpeechEvent.FocusCompleted => FocusCompleted,
        SpeechEvent.BreakCompleted => BreakCompleted,
        _ => Array.Empty<string>()
    };
    public SpeechLines Clone()
    {
        Validate();
        return new() { Click = Click.ToArray(), Drag = Drag.ToArray(), FocusCompleted = FocusCompleted.ToArray(), BreakCompleted = BreakCompleted.ToArray() };
    }
    public void Validate()
    {
        foreach (var trigger in Enum.GetValues<SpeechEvent>())
        {
            var lines = For(trigger);
            if (lines is null || lines.Length > 8 || lines.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 80 || s.Any(char.IsControl)))
                throw new InvalidDataException("每类台词最多 8 条，每条 1-80 字符，不能含换行或控制字符。");
        }
    }
    public static SpeechLines Default() => new()
    {
        Click = new[] { "我在，今天也一起加油吧。", "需要我陪你一会儿吗？" },
        Drag = new[] { "换个位置，继续陪你。", "就在这里吧。" },
        FocusCompleted = new[] { "这轮专注完成了，歇一会儿吧。", "又完成了一段专注，辛苦了。" },
        BreakCompleted = new[] { "休息结束，准备好再开始吧。", "下一轮，我也在。" }
    };
}

public sealed class SpeechOptions
{
    public bool Enabled { get; set; } = true;
    public int CooldownSeconds { get; set; } = 30;
    public Dictionary<string, SpeechLines> Overrides { get; set; } = new(StringComparer.Ordinal);
    public void Normalize()
    {
        CooldownSeconds = Math.Clamp(CooldownSeconds, 5, 300);
        Overrides ??= new(StringComparer.Ordinal);
        if (Overrides.Count > 32) throw new InvalidDataException("个人台词最多保存 32 个皮肤。");
        foreach (var (id, lines) in Overrides)
        {
            PetPackage.ValidateId(id);
            if (lines is null) throw new InvalidDataException("个人台词内容不能为空。");
            lines.Validate();
        }
    }
    public SpeechOptions Clone() => new() { Enabled = Enabled, CooldownSeconds = CooldownSeconds, Overrides = Overrides.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal) };
    public SpeechLines Resolve(PetPackage pet) => (Overrides.GetValueOrDefault(pet.Manifest.Id) ?? pet.Manifest.Speech ?? SpeechLines.Default()).Clone();
}

public sealed class SpeechDirector
{
    private readonly TimeProvider _clock;
    private long? _lastShown;
    private string? _lastLine;
    public SpeechDirector(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;
    public string? Select(SpeechOptions options, PetPackage pet, SpeechEvent trigger, bool suppressed)
    {
        if (!options.Enabled || suppressed || (_lastShown is long last && _clock.GetElapsedTime(last).TotalSeconds < options.CooldownSeconds)) return null;
        var choices = options.Resolve(pet).For(trigger);
        if (choices.Length == 0) return null;
        var alternatives = choices.Where(line => line != _lastLine).ToArray();
        var line = alternatives.Length > 0 ? alternatives[Random.Shared.Next(alternatives.Length)] : choices[0];
        _lastShown = _clock.GetTimestamp();
        _lastLine = line;
        return line;
    }
}
