using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace YeShunguangPet;

public partial class SpeechSettingsWindow : ThemedWindow
{
    private readonly DesktopSession _desktop;
    private readonly PetPackage _pet;
    private bool _generating;
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
    private async void GenerateAi_Click(object sender, RoutedEventArgs e)
    {
        if (_generating) return;
        var options = _desktop.Configuration.Ai;
        if (!options.Enabled || !options.AllowSpeechSuggestions)
        {
            ErrorText.Text = "请先在控制中心左侧的“AI 设置”中启用 AI 和角色台词建议。";
            return;
        }
        try
        {
            _generating = true; GenerateAiButton.IsEnabled = false; ErrorText.Text = "正在生成，未保存当前台词草稿…";
            var provider = AiProviderFactory.Create(options, new AiSecretStore()) ?? throw new InvalidOperationException("AI Provider 未启用。");
            var tabs = (TabControl)FindName("Tabs");
            var trigger = (tabs.SelectedItem as TabItem)?.Header?.ToString() ?? "当前事件";
            var character = new AiChatSession(new AiChatStore(), _pet.Manifest.Id, _pet.Manifest.Name, _pet.Manifest.Description).State.Profile;
            var prompt = new AiPrompt(character.BuildContext(_pet.Manifest.Name) + "\n你是桌宠的台词助手。只返回 JSON：{\"text\":\"一句中文短台词\",\"mood\":\"encourage\"}。不要执行动作，不要输出 JSON 之外的内容。台词最多 60 个汉字。",
                $"角色：{_pet.Manifest.Name}\n事件：{trigger}\n请生成一句自然、简短、不冒充官方台词的桌宠台词。");
            var suggestion = await new AiSuggestionService(provider).SuggestAsync(prompt, CancellationToken.None);
            var target = CurrentInput(); target.Text = string.IsNullOrWhiteSpace(target.Text) ? suggestion.Text : target.Text.TrimEnd() + Environment.NewLine + suggestion.Text;
            ErrorText.Text = "已加入当前草稿，点击保存后生效。";
        }
        catch (Exception ex) { ErrorText.Text = "AI 生成失败：" + ex.Message; }
        finally { _generating = false; GenerateAiButton.IsEnabled = true; }
    }
    private TextBox CurrentInput()
    {
        return ((TabControl)FindName("Tabs"))?.SelectedIndex switch
        {
            1 => DragInput, 2 => FocusInput, 3 => BreakInput, _ => ClickInput
        } ?? ClickInput;
    }
    private void Persona_Click(object sender, RoutedEventArgs e) => _desktop.OpenChat(_pet, this, editProfile: true);
}
