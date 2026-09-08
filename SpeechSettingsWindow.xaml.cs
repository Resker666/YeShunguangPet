using System;
using System.Linq;
using System.Windows;

namespace YeShunguangPet;

public partial class SpeechSettingsWindow : ThemedWindow
{
    private readonly DesktopSession _desktop;
    private readonly PetPackage _pet;
    public SpeechSettingsWindow(DesktopSession desktop, PetPackage pet)
    {
        _desktop = desktop;
        _pet = pet;
        InitializeComponent();
        Portrait.Source = pet.Preview;
        NameText.Text = pet.Manifest.Name;
        EnabledCheck.IsChecked = desktop.Configuration.Speech.Enabled;
        CooldownSlider.Value = desktop.Configuration.Speech.CooldownSeconds;
        LoadLines(desktop.Configuration.Speech.Resolve(pet));
        SourceText.Text = desktop.Configuration.Speech.Overrides.ContainsKey(pet.Manifest.Id) ? "个人台词" : pet.Manifest.Speech is null ? "通用台词" : "皮肤内置台词";
    }
    private void LoadLines(SpeechLines lines)
    {
        ClickInput.Text = string.Join(Environment.NewLine, lines.Click);
        DragInput.Text = string.Join(Environment.NewLine, lines.Drag);
        FocusInput.Text = string.Join(Environment.NewLine, lines.FocusCompleted);
        BreakInput.Text = string.Join(Environment.NewLine, lines.BreakCompleted);
    }
    private void Cooldown_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CooldownText is not null) CooldownText.Text = $"{e.NewValue:0} 秒";
    }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        LoadLines(_pet.Manifest.Speech ?? SpeechLines.Default());
        SourceText.Text = _pet.Manifest.Speech is null ? "通用台词" : "皮肤内置台词";
        ErrorText.Text = string.Empty;
    }
    internal bool SaveOptions()
    {
        ErrorText.Text = string.Empty;
        try
        {
            static string[] Lines(string value) => value.Split(new[] { '\r', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var lines = new SpeechLines { Click = Lines(ClickInput.Text), Drag = Lines(DragInput.Text), FocusCompleted = Lines(FocusInput.Text), BreakCompleted = Lines(BreakInput.Text) };
            lines.Validate();
            var next = _desktop.Configuration.Speech.Clone();
            next.Enabled = EnabledCheck.IsChecked == true;
            next.CooldownSeconds = (int)CooldownSlider.Value;
            var defaults = _pet.Manifest.Speech ?? SpeechLines.Default();
            if (Enum.GetValues<SpeechEvent>().All(trigger => defaults.For(trigger).SequenceEqual(lines.For(trigger)))) next.Overrides.Remove(_pet.Manifest.Id);
            else next.Overrides[_pet.Manifest.Id] = lines;
            _desktop.UpdateSpeech(next);
            SourceText.Text = next.Overrides.ContainsKey(_pet.Manifest.Id) ? "个人台词" : _pet.Manifest.Speech is null ? "通用台词" : "皮肤内置台词";
            return true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; return false; }
    }
    private void Save_Click(object sender, RoutedEventArgs e) { if (SaveOptions()) DialogResult = true; }
}
