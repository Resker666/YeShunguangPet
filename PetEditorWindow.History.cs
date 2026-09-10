using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace YeShunguangPet;

public partial class PetEditorWindow
{
    private readonly EditorHistory _history = new();
    private bool _closed, _timelineUpdating;
    private static readonly string[] DraftInputNames = { "NameInput", "IdInput", "DescriptionInput", "CellWidthInput", "CellHeightInput",
        "ColumnsInput", "RowsInput", "ActionRowInput", "ActionColumnInput", "FrameCountInput", "LookRowInput", "LookColumnInput" };

    private sealed record EditorState(PetManifest Manifest, Dictionary<string, string> Inputs, string[] Durations,
        Dictionary<int, string> DurationHistory, Dictionary<PetState, AnimationDefinition> DisabledActions,
        FrameLocation[] LookHistory, PetState Action, int Direction, int Mode, bool Enabled, bool Loop, bool LookEnabled,
        bool BehaviorEnabled, bool BehaviorInitialized, RuleInput[] Rules);

    private string CaptureEditorState() => JsonSerializer.Serialize(new EditorState(_document.Draft,
        DraftInputNames.ToDictionary(name => name, name => ((TextBox)FindName(name)).Text),
        _durations.Select(row => row.Text).ToArray(), _durationHistory, _disabledActions, _lookHistory,
        _selectedState, _direction, ModeTabs.SelectedIndex, EnabledCheck.IsChecked == true, LoopCheck.IsChecked == true, LookEnabledCheck.IsChecked == true,
        BehaviorEnabledCheck.IsChecked == true, _behaviorInitialized, _behaviorRows.Select(row => new RuleInput(row.Condition, row.Action, row.Weight, row.Cooldown)).ToArray()));

    private void CommitEditorChange(string? group = null)
    {
        var valid = ValidateAndPreview();
        _dirtyInputs = !valid || _document.IsDirty;
        _history.Record(CaptureEditorState(), group);
        RefreshHistoryButtons();
    }

    private void RememberNavigation()
    {
        if (_history.Count > 0) _history.UpdateCurrent(CaptureEditorState());
        RefreshHistoryButtons();
    }

    private void RefreshHistoryButtons()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
    }

    private void RestoreEditorState(string json)
    {
        var state = JsonSerializer.Deserialize<EditorState>(json)!;
        PausePreview();
        _document.ReplaceDraft(state.Manifest);
        _selectedState = state.Action; _direction = state.Direction;
        LoadDraft();
        _loading = true;
        try
        {
            foreach (var input in state.Inputs) ((TextBox)FindName(input.Key)).Text = input.Value;
            ClearDurationRows();
            foreach (var text in state.Durations) AddDuration(text);
            _durationHistory.Clear(); foreach (var item in state.DurationHistory) _durationHistory.Add(item.Key, item.Value);
            _disabledActions.Clear(); foreach (var item in state.DisabledActions) _disabledActions.Add(item.Key, item.Value);
            _lookHistory = state.LookHistory;
            EnabledCheck.IsChecked = state.Enabled; LoopCheck.IsChecked = state.Loop; LookEnabledCheck.IsChecked = state.LookEnabled;
            BehaviorEnabledCheck.IsChecked = state.BehaviorEnabled;
            _behaviorInitialized = state.BehaviorInitialized;
            ClearBehaviorRows(); foreach (var rule in state.Rules) AddBehaviorRow(rule);
            ModeTabs.SelectedIndex = state.Mode;
        }
        finally { _loading = false; }
        _dirtyInputs = !ValidateAndPreview() || _document.IsDirty;
        RefreshHistoryButtons();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        CommitDurationCell();
        if (_history.CanUndo) RestoreEditorState(_history.Undo());
    }
    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        CommitDurationCell();
        if (_history.CanRedo) RestoreEditorState(_history.Redo());
    }
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || (Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0 || e.Key is not (Key.Z or Key.Y)) return;
        if (e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) Redo_Click(sender, e);
        else Undo_Click(sender, e);
        e.Handled = true;
    }
    private void Editor_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => _history.BreakGroup();
    private void CommitDurationCell()
    {
        DurationGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        DurationGrid.CommitEdit(DataGridEditingUnit.Row, true);
        RuleGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RuleGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void PausePreview()
    {
        _playing = false; _timer.Stop(); PlayButton.Content = "\uE768";
    }

    private void UpdateTimelineControls()
    {
        var enabled = PlayButton.IsEnabled && _preview is not null && _animation is not null && ModeTabs.SelectedIndex == 0 && _preview.Supports(_selectedState);
        _timelineUpdating = true;
        try
        {
            TimelineSlider.IsEnabled = enabled && _animation!.FrameCount > 1;
            PreviousFrameButton.IsEnabled = enabled && _frame > 0;
            NextFrameButton.IsEnabled = enabled && _frame < _animation!.FrameCount - 1;
            ApplyDurationButton.IsEnabled = enabled;
            if (!enabled) { TimelineText.Text = string.Empty; return; }
            var total = _animation!.DurationsMs.Sum();
            var elapsed = AnimationTimeline.StartAt(_animation.DurationsMs, _frame);
            TimelineSlider.Maximum = Math.Max(1, total - 1);
            TimelineSlider.Value = elapsed;
            TimelineText.Text = $"{elapsed / 1000.0:0.00} / {total / 1000.0:0.00} s";
        }
        finally { _timelineUpdating = false; }
    }

    private void StepFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_animation is null || !TimelineSlider.IsEnabled) return;
        PausePreview();
        var delta = ((Button)sender).Tag as string == "-1" ? -1 : 1;
        _frame = Math.Clamp(_frame + delta, 0, _animation.FrameCount - 1);
        RenderFrame();
    }
    private void Timeline_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _timelineUpdating || _animation is null || !TimelineSlider.IsEnabled) return;
        PausePreview();
        _frame = AnimationTimeline.FrameAt(_animation.DurationsMs, e.NewValue);
        RenderFrame();
    }

    private void ApplyDuration_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyDurationButton.IsEnabled) return;
        try
        {
            var text = Number(BatchDurationInput.Text, "批量时长", 20, 10000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            CommitDurationCell();
            var rows = BatchScope.SelectedIndex == 0 ? _durations.ToArray() : DurationGrid.SelectedItems.OfType<DurationRow>().ToArray();
            if (rows.Length == 0) { StatusText.Text = "尚未选择帧。"; return; }
            _loading = true;
            try { foreach (var row in rows) { row.Text = text; _durationHistory[row.Index] = text; } }
            finally { _loading = false; }
            CommitEditorChange();
        }
        catch (System.IO.InvalidDataException ex) { StatusText.Text = ex.Message; }
    }
}
