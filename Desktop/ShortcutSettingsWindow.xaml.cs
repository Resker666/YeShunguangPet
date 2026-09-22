using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace YeShunguangPet;

public partial class ShortcutSettingsWindow : ThemedWindow
{
    private readonly DesktopSession _desktop;
    private List<ShortcutRow> _rows = new();
    private bool _loading;
    public ShortcutSettingsWindow(DesktopSession desktop)
    {
        InitializeComponent();
        _desktop = desktop;
        LoadRows(desktop.Configuration.Shortcuts);
    }
    private void LoadRows(ShortcutOptions options)
    {
        _loading = true;
        _rows = Enum.GetValues<ShortcutAction>().Select(action => new ShortcutRow(action, options.Get(action),
            $"当前：{_desktop.ShortcutHint(action)} {_desktop.ShortcutStatus(action)}".Trim())).ToList();
        ShortcutRows.ItemsSource = _rows;
        ShortcutRows.UpdateLayout();
        _loading = false;
    }
    private void Draft_Changed(object sender, RoutedEventArgs e)
    {
        MarkDraftChanged();
    }
    private void MarkDraftChanged(string message = "有未保存的更改")
    {
        if (_loading || _desktop is null || !IsLoaded) return;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SecondaryTextBrush");
        StatusText.Text = message;
    }
    private void Gesture_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShortcutRow row }) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
        {
            e.Handled = true;
            return;
        }
        var modifiers = (uint)(Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift));
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (modifiers == 0)
        {
            MarkDraftChanged("请至少按一个 Ctrl、Alt 或 Shift，再按字母、数字或功能键");
            e.Handled = true;
            return;
        }
        if (!ShortcutGesture.IsKeyAllowed(virtualKey))
        {
            MarkDraftChanged("暂不支持这个按键，请使用字母、数字、空格或 F1-F12");
            e.Handled = true;
            return;
        }
        row.Modifiers = modifiers; row.Key = virtualKey;
        MarkDraftChanged(); e.Handled = true;
    }
    private void ClearGesture_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShortcutRow row) row.Enabled = false;
        MarkDraftChanged();
    }
    private void Defaults_Click(object sender, RoutedEventArgs e) { LoadRows(new ShortcutOptions()); Draft_Changed(sender, e); }
    private bool SaveOptions()
    {
        try
        {
            var options = new ShortcutOptions();
            foreach (var row in _rows) options.Set(row.Action, row.Enabled ? new ShortcutGesture(row.Modifiers, row.Key) : null);
            _desktop.UpdateShortcuts(options);
            StatusText.Text = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not save shortcuts.", ex);
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DangerBrush");
            StatusText.Text = ex.Message;
            return false;
        }
    }
    private void Save_Click(object sender, RoutedEventArgs e) { if (SaveOptions()) DialogResult = true; }

    public sealed class ShortcutRow : INotifyPropertyChanged
    {
        private bool _enabled;
        private uint _modifiers, _key;
        public event PropertyChangedEventHandler? PropertyChanged;
        public ShortcutAction Action { get; }
        public string Name => ShortcutOptions.Name(Action);
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
        public uint Modifiers { get => _modifiers; set => Set(ref _modifiers, value); }
        public uint Key { get => _key; set => Set(ref _key, value); }
        public string GestureText => Enabled ? new ShortcutGesture(Modifiers, Key).ToString() : "未设置";
        public string AppliedStatus { get; }
        public ShortcutRow(ShortcutAction action, ShortcutGesture? gesture, string status)
        {
            Action = action; Enabled = gesture is not null; Modifiers = gesture?.Modifiers ?? 3;
            Key = gesture?.Key ?? action switch { ShortcutAction.RecallAll => 0x59u, ShortcutAction.OpenFocus => 0x46u, ShortcutAction.ToggleFocus => 0x20u, ShortcutAction.Capture => 0x53u, ShortcutAction.CaptureCurrentScreen => 0x44u, ShortcutAction.CaptureAllScreens => 0x41u, _ => 0x4Du };
            AppliedStatus = status;
        }
        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (name is nameof(Enabled) or nameof(Modifiers) or nameof(Key)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GestureText)));
        }
    }
}
