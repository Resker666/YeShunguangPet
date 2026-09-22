using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace YeShunguangPet;

public partial class PetEditorWindow
{
    private readonly ObservableCollection<BehaviorRuleRow> _behaviorRows = new();
    private bool _behaviorInitialized;
    public Choice<BehaviorCondition>[] BehaviorConditions { get; } = new Choice<BehaviorCondition>[]
        { new(BehaviorCondition.Any, "任意位置"), new(BehaviorCondition.CursorNear, "鼠标靠近"), new(BehaviorCondition.CursorFar, "鼠标远离") };
    public Choice<PetState>[] BehaviorActions { get; } = new Choice<PetState>[]
        { new(PetState.Waving, "打招呼"), new(PetState.Jumping, "跳跃"), new(PetState.Failed, "失败") };
    public sealed record Choice<T>(T Value, string Name);
    private sealed record RuleInput(BehaviorCondition Condition, PetState Action, string Weight, string Cooldown);

    private void LoadBehaviorRules()
    {
        ClearBehaviorRows();
        BehaviorEnabledCheck.IsChecked = _document.Draft.Behavior is not null;
        _behaviorInitialized = _document.Draft.Behavior is not null;
        foreach (var rule in _document.Draft.Behavior?.Rules ?? Enumerable.Empty<PetBehaviorRule>())
            AddBehaviorRow(new(rule.Condition, rule.Action, rule.Weight.ToString(), rule.CooldownSeconds.ToString()));
    }
    private void ClearBehaviorRows()
    {
        foreach (var row in _behaviorRows) row.PropertyChanged -= BehaviorRow_Changed;
        _behaviorRows.Clear();
    }
    private void AddBehaviorRow(RuleInput input)
    {
        var row = new BehaviorRuleRow { Condition = input.Condition, Action = input.Action, Weight = input.Weight, Cooldown = input.Cooldown };
        row.PropertyChanged += BehaviorRow_Changed; _behaviorRows.Add(row);
    }
    private void BehaviorRow_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading && sender is BehaviorRuleRow row) CommitEditorChange($"rule:{_behaviorRows.IndexOf(row)}:{e.PropertyName}");
    }
    private void CollectBehaviorRules()
    {
        _document.Draft.Behavior = BehaviorEnabledCheck.IsChecked == true ? new PetBehaviorRules
        {
            Rules = _behaviorRows.Select(row => new PetBehaviorRule { Condition = row.Condition, Action = row.Action,
                Weight = Number(row.Weight, "规则权重", 1, 100), CooldownSeconds = Number(row.Cooldown, "规则冷却", 5, 3600) }).ToList()
        } : null;
    }
    private void BehaviorEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (BehaviorEnabledCheck.IsChecked == true && !_behaviorInitialized)
        {
            foreach (var state in new[] { PetState.Waving, PetState.Jumping })
                if (CanUseRuleAction(state)) AddBehaviorRow(new(BehaviorCondition.Any, state, "1", "30"));
            _behaviorInitialized = true;
        }
        CommitEditorChange();
    }
    private bool CanUseRuleAction(PetState state) => _document.Draft.Animations.TryGetValue(state, out var animation) &&
        !animation.Loop && animation.DurationsMs.Sum() <= 30000;
    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (BehaviorEnabledCheck.IsChecked != true || _behaviorRows.Count >= 9) { StatusText.Text = "最多配置 9 条规则。"; return; }
        foreach (var condition in BehaviorConditions)
            foreach (var action in BehaviorActions)
                if (!_behaviorRows.Any(row => row.Condition == condition.Value && row.Action == action.Value) && CanUseRuleAction(action.Value))
                {
                    AddBehaviorRow(new(condition.Value, action.Value, "1", "30"));
                    RuleGrid.SelectedItem = _behaviorRows.Last(); CommitEditorChange(); return;
                }
        StatusText.Text = "没有可添加的动作与条件组合。";
    }
    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (RuleGrid.SelectedItem is not BehaviorRuleRow row) return;
        row.PropertyChanged -= BehaviorRow_Changed; _behaviorRows.Remove(row); CommitEditorChange();
    }
    public sealed class BehaviorRuleRow : INotifyPropertyChanged
    {
        private BehaviorCondition _condition;
        private PetState _action;
        private string _weight = "1", _cooldown = "30";
        public BehaviorCondition Condition { get => _condition; set { _condition = value; Changed(nameof(Condition)); } }
        public PetState Action { get => _action; set { _action = value; Changed(nameof(Action)); } }
        public string Weight { get => _weight; set { _weight = value; Changed(nameof(Weight)); } }
        public string Cooldown { get => _cooldown; set { _cooldown = value; Changed(nameof(Cooldown)); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
    }
}
