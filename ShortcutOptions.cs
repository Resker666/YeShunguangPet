using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace YeShunguangPet;

public enum ShortcutAction { RecallAll, OpenFocus, ToggleFocus, ToggleMini, Capture, CaptureCurrentScreen, CaptureAllScreens }

public sealed record ShortcutGesture(uint Modifiers, uint Key)
{
    public static bool IsKeyAllowed(uint key) => key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A or 0x20;
    public void Validate()
    {
        if (Modifiers is not (3 or 5 or 6 or 7) || !IsKeyAllowed(Key))
            throw new InvalidDataException("快捷键需使用至少两个 Ctrl/Alt/Shift 修饰键，以及字母、数字、空格或 F1-F11。");
    }
    [JsonIgnore] public string KeyLabel => Key == 0x20 ? "Space" : Key is >= 0x70 and <= 0x7A ? $"F{Key - 0x6F}" : ((char)Key).ToString();
    public override string ToString() => string.Join("+", new[] { (Modifiers & 2) != 0 ? "Ctrl" : null,
        (Modifiers & 1) != 0 ? "Alt" : null, (Modifiers & 4) != 0 ? "Shift" : null, KeyLabel }.Where(x => x is not null));
}

public sealed class ShortcutOptions
{
    public ShortcutGesture? RecallAll { get; set; } = new(3, 0x59);
    public ShortcutGesture? OpenFocus { get; set; }
    public ShortcutGesture? ToggleFocus { get; set; }
    public ShortcutGesture? ToggleMini { get; set; }
    public ShortcutGesture? Capture { get; set; }
    public ShortcutGesture? CaptureCurrentScreen { get; set; }
    public ShortcutGesture? CaptureAllScreens { get; set; }
    public ShortcutOptions Copy() => new() { RecallAll = RecallAll, OpenFocus = OpenFocus, ToggleFocus = ToggleFocus, ToggleMini = ToggleMini, Capture = Capture, CaptureCurrentScreen = CaptureCurrentScreen, CaptureAllScreens = CaptureAllScreens };
    public ShortcutGesture? Get(ShortcutAction action) => action switch
    {
        ShortcutAction.RecallAll => RecallAll, ShortcutAction.OpenFocus => OpenFocus,
        ShortcutAction.ToggleFocus => ToggleFocus, ShortcutAction.ToggleMini => ToggleMini,
        ShortcutAction.Capture => Capture,
        ShortcutAction.CaptureCurrentScreen => CaptureCurrentScreen, ShortcutAction.CaptureAllScreens => CaptureAllScreens,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
    public void Set(ShortcutAction action, ShortcutGesture? gesture)
    {
        switch (action)
        {
            case ShortcutAction.RecallAll: RecallAll = gesture; break;
            case ShortcutAction.OpenFocus: OpenFocus = gesture; break;
            case ShortcutAction.ToggleFocus: ToggleFocus = gesture; break;
            case ShortcutAction.ToggleMini: ToggleMini = gesture; break;
            case ShortcutAction.Capture: Capture = gesture; break;
            case ShortcutAction.CaptureCurrentScreen: CaptureCurrentScreen = gesture; break;
            case ShortcutAction.CaptureAllScreens: CaptureAllScreens = gesture; break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }
    public IEnumerable<(ShortcutAction Action, ShortcutGesture Gesture)> Active()
    {
        foreach (var action in Enum.GetValues<ShortcutAction>()) if (Get(action) is { } gesture) yield return (action, gesture);
    }
    public void Validate()
    {
        var used = new HashSet<ShortcutGesture>();
        foreach (var (_, gesture) in Active())
        {
            gesture.Validate();
            if (!used.Add(gesture)) throw new InvalidDataException($"快捷键重复：{gesture}。每个组合只能对应一个操作。");
        }
    }
    public static string Name(ShortcutAction action) => action switch
    {
        ShortcutAction.RecallAll => "召回全部角色", ShortcutAction.OpenFocus => "打开专注计时",
        ShortcutAction.ToggleFocus => "开始 / 暂停计时", ShortcutAction.ToggleMini => "切换迷你模式",
        ShortcutAction.Capture => "区域截图",
        ShortcutAction.CaptureCurrentScreen => "当前屏幕截图", ShortcutAction.CaptureAllScreens => "全部屏幕截图",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
