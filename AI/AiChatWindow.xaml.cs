using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace YeShunguangPet;

public partial class AiChatWindow : ThemedWindow
{
    private readonly PetPackage _pet;
    private readonly AiChatSession _session;
    private readonly Func<IAiProvider?> _providerFactory;
    private readonly Action<Window>? _openSettings;
    private CancellationTokenSource? _request;
    private bool _loading = true, _dirty, _closed;

    public AiChatWindow(PetPackage pet, Func<AiOptions> options, Action<Window>? openSettings = null,
        AiChatStore? store = null, Func<IAiProvider?>? providerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _pet = pet; _openSettings = openSettings;
        _session = new AiChatSession(store ?? new AiChatStore(), pet.Manifest.Id, pet.Manifest.Name, pet.Manifest.Description);
        _providerFactory = providerFactory ?? (() => AiProviderFactory.Create(options(), new AiSecretStore()));
        InitializeComponent();
        Title = pet.Manifest.Name + " · 聊天";
        Portrait.Source = pet.Preview; NameText.Text = pet.Manifest.Name;
        var profile = _session.State.Profile;
        BackgroundInput.Text = profile.Background; PersonalityInput.Text = profile.Personality;
        StyleInput.Text = profile.SpeakingStyle; ExamplesInput.Text = profile.Examples;
        _loading = false;
        RefreshMessages();
        StatusText.Text = options().Enabled ? "准备好了，发送消息开始聊天。" : "请先在 AI 设置中启用并配置服务。人设可先离线编辑。";
        Closed += (_, _) => { _closed = true; _request?.Cancel(); };
        Closing += (_, e) =>
        {
            if (_dirty && AppDialog.Show(this, "人设更改尚未保存，确定放弃更改并关闭？", "未保存的人设", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) e.Cancel = true;
        };
    }

    private void RefreshMessages(string? pendingUser = null, string? pendingAssistant = null)
    {
        var messages = _session.State.Messages.Select(message => new
        {
            Speaker = message.Role == "user" ? "你" : _pet.Manifest.Name,
            Text = message.Content
        }).ToList();
        if (pendingUser is not null) messages.Add(new { Speaker = "你", Text = pendingUser });
        if (pendingAssistant is not null) messages.Add(new { Speaker = _pet.Manifest.Name, Text = pendingAssistant });
        MessagesList.ItemsSource = messages;
        EmptyText.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MessagesScroll.ScrollToEnd();
    }

    private void Profile_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        if (StatusText is not null) StatusText.Text = "人设有未保存的更改。保存后用于下一次聊天和台词生成。";
    }
    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _session.SaveProfile(new AiCharacterProfile { Background = BackgroundInput.Text, Personality = PersonalityInput.Text, SpeakingStyle = StyleInput.Text, Examples = ExamplesInput.Text });
            _dirty = false; StatusText.Text = "人设已保存，将用于下一次聊天和台词生成。";
        }
        catch (Exception ex) { StatusText.Text = "人设保存失败：" + ex.Message; }
    }
    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();
    private async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; await SendAsync(); }
    }
    private async Task SendAsync()
    {
        if (_request is not null || _closed) return;
        if (_dirty) { ChatTabs.SelectedIndex = 1; StatusText.Text = "请先保存人设，再发送消息。"; return; }
        var input = MessageInput.Text.Trim();
        if (input.Length == 0) { StatusText.Text = "先写一句想说的话吧。"; return; }
        using var cancellation = new CancellationTokenSource();
        _request = cancellation;
        SetBusy(true);
        try
        {
            var provider = _providerFactory() ?? throw new InvalidOperationException("请先在 AI 设置中启用并配置服务。");
            StatusText.Text = "正在回复…";
            RefreshMessages(input, string.Empty);
            var progress = new DispatcherProgress(this, text => { if (!_closed) RefreshMessages(input, text); });
            await _session.SendStreamingAsync(provider, input, progress, cancellation.Token);
            if (_closed) return;
            MessageInput.Clear(); RefreshMessages();
            StatusText.Text = "已保存到本机 · 保留最近 12 轮对话";
            SendButton.Content = "发送";
        }
        catch (OperationCanceledException) { if (!_closed) { RefreshMessages(); StatusText.Text = "请求已停止，消息草稿已保留。"; } }
        catch (Exception ex)
        {
            if (!_closed) { RefreshMessages(); StatusText.Text = "发送失败：" + ex.Message + " 草稿已保留，可重试。"; SendButton.Content = "重试"; }
        }
        finally { _request = null; if (!_closed) { SetBusy(false); MessageInput.Focus(); } }
    }
    private void SetBusy(bool busy)
    {
        SendButton.IsEnabled = ClearButton.IsEnabled = SaveProfileButton.IsEnabled = ProfileFields.IsEnabled = !busy;
        MessageInput.IsReadOnly = busy; StopButton.IsEnabled = busy;
    }
    private void Stop_Click(object sender, RoutedEventArgs e) => _request?.Cancel();
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_request is not null) return;
        if (AppDialog.Show(this, "清空这个角色保存在本机的聊天记录？人设会保留。", "清空聊天记录", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try { _session.ClearHistory(); RefreshMessages(); StatusText.Text = "聊天记录已清空，人设已保留。"; }
        catch (Exception ex) { StatusText.Text = "清空失败：" + ex.Message; }
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => _openSettings?.Invoke(this);

    internal void CloseForShutdown()
    {
        _dirty = false;
        _request?.Cancel();
        Close();
    }

    private sealed class DispatcherProgress(AiChatWindow owner, Action<string> update) : IProgress<string>
    {
        public void Report(string value)
        {
            if (owner.Dispatcher.CheckAccess()) update(value);
            else owner.Dispatcher.Invoke(() => update(value));
        }
    }
}
