using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace YeShunguangPet;

public partial class AiSettingsWindow : ThemedWindow
{
    private readonly DesktopSession _desktop;
    private readonly AiSecretStore _secrets = new();
    private readonly AiOptions _draft;
    private readonly Dictionary<string, string> _pendingKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletedProfiles = new(StringComparer.Ordinal);
    private string? _currentProfileId;
    private bool _loading, _busy;
    public AiSettingsWindow(DesktopSession desktop)
    {
        _desktop = desktop; InitializeComponent(); _loading = true;
        _draft = desktop.Configuration.Ai.Clone(); _draft.Normalize();
        EnabledCheck.IsChecked = _draft.Enabled; SpeechCheck.IsChecked = _draft.AllowSpeechSuggestions;
        SummaryCheck.IsChecked = _draft.AllowStudySummaries; CaptureCheck.IsChecked = _draft.AllowCaptureAssistant;
        RefreshProfiles(_draft.ActiveProfileId);
        _loading = false;
    }
    private AiOptions Draft()
    {
        CaptureProfile();
        var options = _draft.Clone(); options.Enabled = EnabledCheck.IsChecked == true;
        options.AllowSpeechSuggestions = SpeechCheck.IsChecked == true; options.AllowStudySummaries = SummaryCheck.IsChecked == true;
        options.AllowCaptureAssistant = CaptureCheck.IsChecked == true; options.ActiveProfileId = _currentProfileId ?? options.ActiveProfileId;
        options.Normalize(); return options;
    }
    private void Draft_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsLoaded) StatusText.Text = "有未保存的更改";
    }
    private void Profile_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || ProfileSelector.SelectedItem is not AiProviderProfile profile) return;
        CaptureProfile(); LoadProfile(profile);
    }
    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_draft.Profiles.Count >= 8) { StatusText.Text = "最多保存 8 套 AI 配置。"; return; }
        CaptureProfile();
        var profile = new AiProviderProfile { Name = $"配置 {_draft.Profiles.Count + 1}" }; _draft.Profiles.Add(profile);
        RefreshProfiles(profile.Id); StatusText.Text = "已新增配置，保存后生效。";
    }
    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProfileId is null || _draft.Profiles.Count <= 1) { StatusText.Text = "至少保留一套 AI 配置。"; return; }
        var profile = _draft.Profiles.Single(item => item.Id == _currentProfileId);
        if (AppDialog.Show(this, $"删除“{profile.Name}”及其本机加密 Key？", "删除 AI 配置", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        _draft.Profiles.Remove(profile); _pendingKeys.Remove(profile.Id); _deletedProfiles.Add(profile.Id);
        RefreshProfiles(_draft.Profiles[0].Id); StatusText.Text = "配置将在保存后删除。";
    }
    private void ClearKey_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProfileId is null) return;
        _secrets.ForProfile(_currentProfileId).Delete(); _pendingKeys.Remove(_currentProfileId); ApiKeyInput.Clear();
        KeyStatusText.Text = "Key 已清除"; StatusText.Text = "有未保存的更改";
    }
    private bool SaveOptions()
    {
        try
        {
            var options = Draft(); options.Normalize(); options.Validate();
            var active = options.ActiveProfile();
            if (options.Enabled && active.Provider == AiProviderKind.Direct && !_secrets.ForProfile(active.Id).HasKey && !_pendingKeys.ContainsKey(active.Id))
                throw new InvalidOperationException("启用直连 API 前请先配置 API Key。");
            foreach (var (id, key) in _pendingKeys) _secrets.ForProfile(id).Save(key);
            foreach (var id in _deletedProfiles) _secrets.ForProfile(id).Delete();
            _desktop.UpdateAi(options); _pendingKeys.Clear(); _deletedProfiles.Clear();
            StatusText.Text = "已保存。AI 仍只会在用户主动点击时请求。"; RefreshKeyStatus(active.Id); return true;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; return false; }
    }
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var options = Draft(); options.Enabled = true; options.Normalize(); options.Validate();
            var active = options.ActiveProfile();
            if (_pendingKeys.TryGetValue(active.Id, out var pending)) _secrets.ForProfile(active.Id).Save(pending);
            if (active.Provider == AiProviderKind.Direct && !_secrets.ForProfile(active.Id).HasKey) throw new InvalidOperationException("请先配置 API Key。");
            _busy = true; TestButton.IsEnabled = SaveButton.IsEnabled = false; StatusText.Text = "正在测试连接…";
            var provider = AiProviderFactory.Create(options, _secrets) ?? throw new InvalidOperationException("AI Provider 未启用。");
            var result = await new AiSuggestionService(provider).SuggestAsync(new AiPrompt("只返回 JSON：{\"text\":\"...\"}。不要执行任何动作。", "返回一句连接测试文本。"), CancellationToken.None);
            StatusText.Text = "连接成功：" + result.Text;
        }
        catch (Exception ex) { StatusText.Text = "连接失败：" + ex.Message; }
        finally { _busy = false; TestButton.IsEnabled = SaveButton.IsEnabled = true; }
    }
    private void Save_Click(object sender, RoutedEventArgs e) { if (SaveOptions()) DialogResult = true; }

    private void RefreshProfiles(string selectedId)
    {
        _loading = true; ProfileSelector.ItemsSource = null; ProfileSelector.ItemsSource = _draft.Profiles;
        ProfileSelector.SelectedValue = selectedId; LoadProfile(_draft.Profiles.First(profile => profile.Id == selectedId));
        DeleteProfileButton.IsEnabled = _draft.Profiles.Count > 1; _loading = false;
    }
    private void CaptureProfile()
    {
        if (_loading || _currentProfileId is null || _draft.Profiles.FirstOrDefault(profile => profile.Id == _currentProfileId) is not { } profile) return;
        profile.Name = ProfileNameInput.Text;
        profile.Provider = Enum.TryParse<AiProviderKind>((ProviderSelector.SelectedValue as string) ?? "Direct", true, out var kind) ? kind : AiProviderKind.Direct;
        profile.Endpoint = EndpointInput.Text; profile.Model = ModelInput.Text;
        if (!string.IsNullOrWhiteSpace(ApiKeyInput.Password)) _pendingKeys[profile.Id] = ApiKeyInput.Password.Trim();
        profile.Normalize();
    }
    private void LoadProfile(AiProviderProfile profile)
    {
        _loading = true; _currentProfileId = profile.Id; ProfileNameInput.Text = profile.Name;
        ProviderSelector.SelectedValue = profile.Provider.ToString(); EndpointInput.Text = profile.Endpoint; ModelInput.Text = profile.Model;
        ApiKeyInput.Password = _pendingKeys.TryGetValue(profile.Id, out var key) ? key : string.Empty;
        RefreshKeyStatus(profile.Id); DeleteProfileButton.IsEnabled = _draft.Profiles.Count > 1; _loading = false;
    }
    private void RefreshKeyStatus(string profileId)
    {
        KeyStatusText.Text = _pendingKeys.ContainsKey(profileId) ? "新 Key 将在保存时加密" :
            _secrets.ForProfile(profileId).HasKey ? "已有本机加密 Key（不会显示原文）" : "尚未配置 Key";
    }
}
