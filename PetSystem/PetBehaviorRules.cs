using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YeShunguangPet;

public enum BehaviorCondition { Any, CursorNear, CursorFar }
public enum RuleDecision { None, Legacy, Selected, Cooldown, Condition, Disabled }

public sealed record PetBehaviorRule
{
    public BehaviorCondition Condition { get; set; }
    public PetState Action { get; set; } = PetState.Waving;
    public int Weight { get; set; } = 1;
    public int CooldownSeconds { get; set; } = 30;
}

public sealed class PetBehaviorRules
{
    public int SchemaVersion { get; set; } = 1;
    public List<PetBehaviorRule> Rules { get; set; } = new();
    public void Validate(PetManifest? manifest = null)
    {
        if (SchemaVersion != 1 || Rules is null || Rules.Count > 9)
            throw new InvalidDataException("行为规则版本需为 1，最多配置 9 条规则。");
        var seen = new HashSet<(BehaviorCondition, PetState)>();
        foreach (var rule in Rules)
        {
            if (rule is null || !Enum.IsDefined(rule.Condition) || rule.Action is not (PetState.Waving or PetState.Jumping or PetState.Failed) ||
                rule.Weight is < 1 or > 100 || rule.CooldownSeconds is < 5 or > 3600)
                throw new InvalidDataException("行为规则仅支持挥手、跳跃和失败动作；权重为 1-100，冷却为 5-3600 秒。");
            if (!seen.Add((rule.Condition, rule.Action))) throw new InvalidDataException("同一条件和动作不能重复配置。");
            if (manifest is not null && (!manifest.Animations.TryGetValue(rule.Action, out var animation) || animation is null ||
                animation.Loop || animation.DurationsMs.Sum() > 30000))
                throw new InvalidDataException("行为规则必须引用已启用、不循环且总时长不超过 30 秒的动作。");
        }
    }
}
