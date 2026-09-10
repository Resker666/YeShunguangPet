using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace YeShunguangPet;

public interface IShortcutRegistrar
{
    bool Register(int id, ShortcutGesture gesture);
    void Unregister(int id);
}

public sealed class GlobalShortcuts : IDisposable
{
    private sealed record Registration(int Id, ShortcutGesture Gesture, ShortcutAction Action);
    private readonly IShortcutRegistrar _native;
    private Dictionary<ShortcutGesture, Registration> _active = new();
    private readonly Dictionary<ShortcutAction, string> _failures = new();
    private int _nextId = 0x6000;
    private bool _disposed;
    private bool _initialized;
    public int Generation { get; private set; }
    public GlobalShortcuts(IShortcutRegistrar native) => _native = native;
    public bool IsRegistered(ShortcutAction action) => _active.Values.Any(x => x.Action == action);
    public string? Failure(ShortcutAction action) => _failures.GetValueOrDefault(action);

    public void Initialize(ShortcutOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("Shortcut registry is already initialized.");
        options.Validate();
        _initialized = true;
        foreach (var (action, gesture) in options.Active())
        {
            var id = NextId();
            if (_native.Register(id, gesture)) _active.Add(gesture, new(id, gesture, action));
            else _failures[action] = $"{gesture} 注册失败，可能已被其他应用占用。";
        }
        Generation++;
    }

    public void Apply(ShortcutOptions options, Action persist)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var desired = options.Copy(); desired.Validate();
        var next = new Dictionary<ShortcutGesture, Registration>();
        var acquired = new List<Registration>();
        // Keep old registrations until every new key is acquired and the file is saved.
        // Swapping commands can reuse the same native registrations without losing either key.
        try
        {
            foreach (var (action, gesture) in desired.Active())
            {
                if (_active.TryGetValue(gesture, out var existing)) next[gesture] = existing with { Action = action };
                else
                {
                    var id = NextId();
                    if (!_native.Register(id, gesture)) throw new InvalidOperationException($"{ShortcutOptions.Name(action)}：{gesture} 注册失败，可能已被其他应用占用。原设置未改变。");
                    var registration = new Registration(id, gesture, action);
                    acquired.Add(registration); next.Add(gesture, registration);
                }
            }
            persist();
        }
        catch
        {
            foreach (var item in acquired) _native.Unregister(item.Id);
            throw;
        }
        var retired = _active.Values.Where(x => !next.ContainsKey(x.Gesture)).ToArray();
        _active = next;
        _failures.Clear();
        Generation++;
        foreach (var item in retired) _native.Unregister(item.Id);
    }

    public bool TryResolve(int id, ShortcutGesture gesture, out ShortcutAction action)
    {
        action = default;
        if (_disposed || !_active.TryGetValue(gesture, out var item) || item.Id != id) return false;
        action = item.Action; return true;
    }

    private int NextId()
    {
        if (_nextId > 0xBFFF) _nextId = 0x6000;
        while (_active.Values.Any(x => x.Id == _nextId)) { if (++_nextId > 0xBFFF) _nextId = 0x6000; }
        return _nextId++;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Generation++;
        foreach (var item in _active.Values) _native.Unregister(item.Id);
        _active.Clear(); _failures.Clear();
    }
}

internal sealed class NativeShortcutRegistrar(Window window) : IShortcutRegistrar
{
    public bool Register(int id, ShortcutGesture gesture) => NativeMethods.RegisterGlobalHotKey(window, id, gesture.Modifiers | 0x4000, gesture.Key);
    public void Unregister(int id) => NativeMethods.UnregisterGlobalHotKey(window, id);
}
