using System;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class FocusWindow : Window
{
    private readonly CompanionSession _session;
    private readonly PetSettings _settings;
    private readonly Func<bool> _isQuiet;
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public FocusWindow(CompanionSession session, PetSettings settings, PetPackage pet, Func<bool> isQuiet)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _isQuiet = isQuiet;
        UpdatePet(pet);
        _session.Changed += RefreshDisplay;
        _displayTimer.Tick += (_, _) => RefreshDisplay();
        Loaded += (_, _) => _displayTimer.Start();
        Closed += (_, _) =>
        {
            _displayTimer.Stop();
            _session.Changed -= RefreshDisplay;
        };
        RefreshDisplay();
    }

    public void UpdatePet(PetPackage pet)
    {
        PetName.Text = pet.Manifest.Name;
        PetImage.Source = pet.Preview;
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Status == SessionStatus.Running) _session.Pause();
        else _session.StartOrResume();
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => _session.Reset();
    private void Skip_Click(object sender, RoutedEventArgs e) => _session.SkipBreak();

    private void RefreshDisplay()
    {
        var seconds = (int)Math.Ceiling(_session.Remaining.TotalSeconds);
        TimeText.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        var phase = _session.Phase == SessionPhase.Focus ? "专注" : "休息";
        PhaseText.Text = _session.Status switch
        {
            SessionStatus.Ready => "准备专注",
            SessionStatus.Paused => phase + "已暂停",
            SessionStatus.Completed => phase + "完成",
            _ => phase + "中"
        };
        ToggleText.Text = _session.Status switch
        {
            SessionStatus.Running => "暂停",
            SessionStatus.Paused => "继续",
            SessionStatus.Completed when _session.Phase == SessionPhase.Focus => "开始休息",
            _ => "开始专注"
        };
        ToggleIcon.Text = _session.Status == SessionStatus.Running ? "\uE769" : "\uE768";
        ResetButton.IsEnabled = _session.Status != SessionStatus.Ready;
        SkipButton.IsEnabled = _session.Phase == SessionPhase.Break ||
            (_session.Phase == SessionPhase.Focus && _session.Status == SessionStatus.Completed);
        Progress.Value = _session.Duration.TotalSeconds > 0
            ? 1 - _session.Remaining.TotalSeconds / _session.Duration.TotalSeconds : 0;
        SessionsText.Text = $"本次已完成 {_session.CompletedFocusSessions} 次专注";
        DurationText.Text = $"专注 {_settings.FocusMinutes} 分钟 · 休息 {_settings.BreakMinutes} 分钟";
        QuietStatus.Text = _isQuiet() ? "勿扰中" : string.Empty;
    }
}
