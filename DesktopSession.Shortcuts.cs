using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public sealed partial class DesktopSession
{
    private GlobalShortcuts? _shortcuts;
    private ShortcutSettingsWindow? _shortcutDialog;
    public string ShortcutHint(ShortcutAction action) => Configuration.Shortcuts.Get(action)?.ToString() ?? string.Empty;
    public string ShortcutStatus(ShortcutAction action) => Configuration.Shortcuts.Get(action) is null ? "未启用" :
        _shortcuts?.Failure(action) ?? (_shortcuts?.IsRegistered(action) == true ? "已生效" : "未注册");

    public void UpdateShortcuts(ShortcutOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var next = options.Copy(); next.Validate();
        void Save()
        {
            var previous = Configuration.Shortcuts;
            Configuration.Shortcuts = next;
            try { Persist(); } catch { Configuration.Shortcuts = previous; throw; }
        }
        if (_shortcuts is null) Save(); else _shortcuts.Apply(next, Save);
        Changed?.Invoke();
    }

    public void OpenShortcutSettings(Window? owner = null)
    {
        if (_disposed) return;
        if (_shortcutDialog is not null) { _shortcutDialog.Activate(); return; }
        _shortcutDialog = new ShortcutSettingsWindow(this);
        if (owner is not null) _shortcutDialog.Owner = owner;
        try { _shortcutDialog.ShowDialog(); }
        finally { _shortcutDialog = null; }
    }

    private bool QueueShortcut(int id, IntPtr message)
    {
        var gesture = new ShortcutGesture((uint)(message.ToInt64() & 0xFFFF), (uint)((message.ToInt64() >> 16) & 0xFFFF));
        if (_disposed || _shortcuts is null || !_shortcuts.TryResolve(id, gesture, out var action)) return false;
        if (!CanExecuteShortcut(action)) return true;
        var generation = _shortcuts.Generation;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed || _shortcuts.Generation != generation) return;
            try { ExecuteShortcut(action); }
            catch (Exception ex) { AppLogger.Error("Shortcut command failed: " + action, ex); ShowNotification("快捷键未执行", ex.Message); }
        }));
        return true;
    }

    internal void ExecuteShortcut(ShortcutAction action)
    {
        if (!CanExecuteShortcut(action)) return;
        switch (action)
        {
            case ShortcutAction.RecallAll: RecallAll(); break;
            case ShortcutAction.OpenFocus: OpenFocus(); break;
            case ShortcutAction.ToggleFocus: Companion.ToggleFromShortcut(); break;
            case ShortcutAction.ToggleMini:
                var closed = Companion.FocusWindowCount == 0;
                if (closed) OpenFocus();
                Companion.ToggleMiniFromShortcut(ensureMini: closed); break;
        }
    }

    private bool CanExecuteShortcut(ShortcutAction action) => !_disposed && _shortcutDialog is null &&
        (action == ShortcutAction.RecallAll || (!HasSettingsOpen &&
            Application.Current?.Windows.Cast<Window>().Any(w => w.IsVisible && !NativeMethods.IsWindowInputEnabled(w)) != true));
}
