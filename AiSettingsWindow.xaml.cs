using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace YeShunguangPet;

public partial class AiSettingsWindow : ThemedWindow
{
    private readonly DesktopSession _desktop;
    private readonly AiSecretStore _secrets = new();
    private bool _loading, _busy;
    public AiSettingsWindow(DesktopSession desktop)
    {
        _desktop = desktop; InitializeComponent();
        var options = desktop.Configuration.Ai.Clone();
        EnabledCheck.IsChecked = options.Enabled;
        ProviderSelector.SelectedValue = options.Provider == AiProviderKind.None ? "Direct" : options.Provider.ToString();
        EndpointInput.Text = string.IsNullOrWhiteSpace(options.Endpoint) ? "https://api.openai.com/v1" : options.Endpoint;
        ModelInput.Text = string.IsNullOrWhiteSpace(options.Model) ? "gpt-4o-mini" : options.Model;
        SpeechCheck.IsChecked = options.AllowSpeechSuggestions;
        SummaryCheck.IsChecked = options.AllowStudySummaries;
        KeyStatusText.Text = _secrets.HasKey ? "已有本机加密 Key（不会显示原文）" : "尚未配置 Key";
        _loading = false;
    }
    private AiOptions Draft()
    {
        var provider = Enum.TryParse<AiProviderKind>((ProviderSelector.SelectedValue as string) ?? "Direct", true, out var kind) ? kind : AiProviderKind.Direct;
        return new AiOptions { Enabled = EnabledCheck.IsChecked == true, Provider = provider, Endpoint = EndpointInput.Text, Model = ModelInput.Text,
            AllowSpeechSuggestions = SpeechCheck.IsChecked == true, AllowStudySummaries = SummaryCheck.IsChecked == true };
    }
    private void Draft_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsLoaded) StatusText.Text = "有未保存的更改";
    }
    private void ClearKey_Click(object sender, RoutedEventArgs e) { _secrets.Delete(); KeyStatusText.Text = "Key 已清除"; StatusText.Text = "有未保存的更改"; }
    private bool SaveOptions()
    {
        try
        {
            var options = Draft(); options.Normalize(); options.Validate();
            if (options.Enabled && options.Provider == AiProviderKind.Direct && !_secrets.HasKey && string.IsNullOrWhiteSpace(ApiKeyInput.Password))
                throw new InvalidOperationException("启用直连 API 前请先配置 API Key。");
            if (!string.IsNullOrWhiteSpace(ApiKeyInput.Password)) _secrets.Save(ApiKeyInput.Password);
            _desktop.UpdateAi(options); StatusText.Text = "已保存。AI 仍只会在用户主动点击时请求。"; KeyStatusText.Text = _secrets.HasKey ? "已有本机加密 Key（不会显示原文）" : "尚未配置 Key"; return true;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; return false; }
    }
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var options = Draft(); options.Enabled = true; options.Normalize(); options.Validate();
            if (options.Provider == AiProviderKind.Direct && !_secrets.HasKey && string.IsNullOrWhiteSpace(ApiKeyInput.Password)) throw new InvalidOperationException("请先配置 API Key。");
            if (!string.IsNullOrWhiteSpace(ApiKeyInput.Password)) _secrets.Save(ApiKeyInput.Password);
            _busy = true; TestButton.IsEnabled = SaveButton.IsEnabled = false; StatusText.Text = "正在测试连接…";
            var provider = AiProviderFactory.Create(options, _secrets) ?? throw new InvalidOperationException("AI Provider 未启用。");
            var result = await new AiSuggestionService(provider).SuggestAsync(new AiPrompt("只返回 JSON：{\"text\":\"...\"}。不要执行任何动作。", "返回一句连接测试文本。"), CancellationToken.None);
            StatusText.Text = "连接成功：" + result.Text;
        }
        catch (Exception ex) { StatusText.Text = "连接失败：" + ex.Message; }
        finally { _busy = false; TestButton.IsEnabled = SaveButton.IsEnabled = true; }
    }
    private void Save_Click(object sender, RoutedEventArgs e) { if (SaveOptions()) DialogResult = true; }
}
