using System;
using System.Collections.Generic;

namespace YeShunguangPet;

public sealed class EditorHistory
{
    private readonly List<string> _states = new();
    private readonly TimeProvider _clock;
    private int _position;
    private string? _group;
    private long _lastEdit;
    public bool CanUndo => _position > 0;
    public bool CanRedo => _position + 1 < _states.Count;
    public int Count => _states.Count;
    public string Current => _states[_position];

    public EditorHistory(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public void Reset(string state)
    {
        _states.Clear(); _states.Add(state); _position = 0; BreakGroup();
    }

    public void Record(string state, string? group = null)
    {
        if (_states.Count == 0) { Reset(state); return; }
        if (state == Current) return;
        var merge = CanUndo && !CanRedo && group is not null && group == _group &&
            _clock.GetElapsedTime(_lastEdit) < TimeSpan.FromMilliseconds(600);
        if (CanRedo) _states.RemoveRange(_position + 1, _states.Count - _position - 1);
        if (merge) _states[_position] = state;
        else { _states.Add(state); _position++; }
        _group = group; _lastEdit = _clock.GetTimestamp();
        long bytes = 0; foreach (var item in _states) bytes += item.Length * 2L;
        while (_states.Count > 1 && (_states.Count > 100 || bytes > 4 * 1024 * 1024))
        {
            bytes -= _states[0].Length * 2L; _states.RemoveAt(0); _position--;
        }
    }

    public string Undo() { BreakGroup(); if (CanUndo) _position--; return Current; }
    public string Redo() { BreakGroup(); if (CanRedo) _position++; return Current; }
    public void UpdateCurrent(string state) { if (_states.Count > 0) _states[_position] = state; BreakGroup(); }
    public void BreakGroup() => _group = null;
}
