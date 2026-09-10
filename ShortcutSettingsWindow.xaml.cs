using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

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
        if (_loading || _desktop is null || !IsLoaded) return;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SecondaryTextBrush");
        StatusText.Text = "有未保存的更改";
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

    public sealed record Choice(uint Value, string Label);
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
        public string AppliedStatus { get; }
        public IReadOnlyList<Choice> ModifierChoices { get; } = new[] {
            new Choice(3, "Ctrl + Alt"), new Choice(6, "Ctrl + Shift"), new Choice(5, "Alt + Shift"), new Choice(7, "Ctrl + Alt + Shift") };
        public IReadOnlyList<Choice> KeyChoices { get; } = Enumerable.Range(0, 256).Where(k => ShortcutGesture.IsKeyAllowed((uint)k))
            .Select(k => new Choice((uint)k, new ShortcutGesture(3, (uint)k).KeyLabel)).ToArray();
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
        }
    }
}
