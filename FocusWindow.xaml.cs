using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class FocusWindow : ThemedWindow
{
    private readonly CompanionSession _session;
    private readonly PetSettings _settings;
    private readonly Func<bool> _isQuiet;
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _avatarTimer = new();
    private readonly SessionVisuals _visuals = new();
    private readonly Dictionary<(int, int), BitmapSource> _frames = new();
    private PetPackage _pet = null!;
    private PetAnimation? _avatarAnimation;
    private int _avatarFrame;
    private readonly CompanionRuntime? _runtime;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _refreshing;
    private bool _editingMinutes;
    private bool _invalidInput;
    private int _draftMinutes;
    private bool CanEdit => _session.Status == SessionStatus.Ready;
    private int MaximumMinutes => _session.Phase == SessionPhase.Focus ? 120 : 60;
    private int ConfiguredMinutes => _session.Phase == SessionPhase.Focus ? _settings.FocusMinutes : _settings.BreakMinutes;

    public FocusWindow(CompanionSession session, PetSettings settings, PetPackage pet, Func<bool> isQuiet, CompanionRuntime? runtime = null, bool managePlacement = false)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _isQuiet = isQuiet;
        _runtime = runtime;
        _managePlacement = managePlacement && runtime is not null;
        InitializeWindowLayout();
        _draftMinutes = ConfiguredMinutes;
        Dial.ValueChanged += Dial_ValueChanged;
        Dial.CommitRequested += immediate => { _saveTimer.Stop(); if (immediate) SaveDuration(); else _saveTimer.Start(); };
        _saveTimer.Tick += (_, _) => { if (Dial.IsDragging) return; _saveTimer.Stop(); SaveDuration(); };
        HistoryButton.IsEnabled = runtime is not null;
        UpdatePet(pet);
        _session.Changed += OnSessionChanged;
        _session.Completed += OnCompleted;
        _displayTimer.Tick += (_, _) => RefreshDisplay();
        _avatarTimer.Tick += AvatarTimer_Tick;
        Loaded += (_, _) => { _displayTimer.Start(); RefreshAvatar(); UpdateResponsiveLayout(); };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) { _displayTimer.Stop(); Dial.CancelDrag(); }
            else { _displayTimer.Start(); RefreshDisplay(); }
            RefreshAvatar();
        };
        Closing += (_, _) => { if (_runtime?.IsDisposed != true) { CommitPendingDurations(); SaveWindowPlacement(); } };
        Closed += (_, _) =>
        {
            _displayTimer.Stop();
            _avatarTimer.Stop();
            _saveTimer.Stop();
            CloseWindowLayout();
            _session.Changed -= OnSessionChanged;
            _session.Completed -= OnCompleted;
        };
        RefreshDisplay();
    }

    public void UpdatePet(PetPackage pet)
    {
        _pet = pet;
        _frames.Clear();
        _avatarAnimation = null;
        PetName.Text = pet.Manifest.Name;
        RefreshAvatar();
    }

    internal void CommitPendingDurations()
    {
        Dial.CancelDrag();
        if (_saveTimer.IsEnabled) SaveDuration();
        if (_editingMinutes && CanEdit) CommitMinutes();
    }

    private void OnSessionChanged()
    {
        _saveTimer.Stop();
        _refreshing = true;
        Dial.CancelDrag();
        _refreshing = false;
        _editingMinutes = false;
        _invalidInput = false;
        _draftMinutes = ConfiguredMinutes;
        _visuals.ClearCompletion();
        RefreshDisplay();
    }

    private void OnCompleted(SessionPhase phase)
    {
        _visuals.RequestCompletion();
        RefreshAvatar();
    }

    private void RefreshAvatar()
    {
        var target = _visuals.Resolve(_session, _settings, _pet, _isQuiet(), WindowState != WindowState.Minimized);
        if (!target.HasValue) { _avatarTimer.Stop(); return; }
        if (_avatarAnimation?.State != target)
        {
            _avatarAnimation = _pet.GetAnimation(target.Value);
            _avatarFrame = 0;
            RenderAvatar();
        }
        if (IsLoaded && UiTheme.MotionEnabled) _avatarTimer.Start();
        else _avatarTimer.Stop();
    }

    private void AvatarTimer_Tick(object? sender, EventArgs e)
    {
        var previous = _avatarAnimation;
        RefreshAvatar();
        if (WindowState == WindowState.Minimized || _avatarAnimation is null || previous != _avatarAnimation) return;
        // Completion gestures repeat for a bounded three-second acknowledgement.
        _avatarFrame = (_avatarFrame + 1) % _avatarAnimation.FrameCount;
        RenderAvatar();
    }

    internal override void OnThemeUpdated()
    {
        if (_pet is not null && _session is not null)
        {
            RefreshAvatar(); Dial.RefreshAppearance();
            UiTheme.MatchTitleBarBackground(this);
            UpdatePresetLayout(force: true);
            UpdateResponsiveLayout();
        }
    }

    private void RenderAvatar()
    {
        if (_avatarAnimation is null) return;
        var key = (_avatarAnimation.Row, _avatarAnimation.StartColumn + _avatarFrame);
        if (!_frames.TryGetValue(key, out var frame)) _frames[key] = frame = _pet.GetFrame(key.Item1, key.Item2);
        PetImage.Source = frame;
        _avatarTimer.Interval = TimeSpan.FromMilliseconds(_avatarAnimation.DurationsMs[_avatarFrame]);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (CanEdit && (!CommitMinutes() || !SaveDuration())) return;
        if (_session.Status == SessionStatus.Running) _session.Pause();
        else _session.StartOrResume();
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => _session.Reset();
    private void Skip_Click(object sender, RoutedEventArgs e) => _session.SkipBreak();
    private void History_Click(object sender, RoutedEventArgs e) => _runtime?.OpenHistory(this);

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Status is SessionStatus.Running or SessionStatus.Paused) return;
        if (CanEdit && (!CommitMinutes() || !SaveDuration())) { RefreshDisplay(); return; }
        _session.PreparePhase((sender as RadioButton)?.Tag as string == "Break" ? SessionPhase.Break : SessionPhase.Focus);
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || (sender as RadioButton)?.Tag is not int minutes) return;
        _editingMinutes = _invalidInput = false;
        _draftMinutes = minutes;
        SaveDuration();
    }

    private void Dial_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || !CanEdit) return;
        _editingMinutes = _invalidInput = false;
        _draftMinutes = (int)Math.Round(Dial.Value);
        DurationError.Text = string.Empty;
        RefreshDisplay();
    }

    private void Minutes_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!CanEdit) return;
        if (_saveTimer.IsEnabled) SaveDuration();
        _editingMinutes = true;
        MinutesInput.SelectAll();
    }

    private void Minutes_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_editingMinutes && CanEdit) { CommitMinutes(); _editingMinutes = false; }
    }

    private void Minutes_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { if (CommitMinutes()) ToggleButton.Focus(); e.Handled = true; }
        if (e.Key == Key.Escape)
        {
            _editingMinutes = _invalidInput = false;
            DurationError.Text = string.Empty;
            _draftMinutes = ConfiguredMinutes;
            RefreshDisplay();
            ToggleButton.Focus();
            e.Handled = true;
        }
    }

    private bool CommitMinutes()
    {
        if (!CanEdit) return true;
        if (!_editingMinutes && !_invalidInput) return DurationError.Text.Length == 0;
        if (!int.TryParse(MinutesInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes < 1 || minutes > MaximumMinutes)
        {
            _invalidInput = true;
            DurationError.Text = $"请输入 1–{MaximumMinutes} 之间的整数分钟。";
            return false;
        }
        _invalidInput = _editingMinutes = false;
        _draftMinutes = minutes;
        return SaveDuration();
    }

    private bool SaveDuration()
    {
        _saveTimer.Stop();
        if (!CanEdit || _runtime?.IsDisposed == true) return true;
        try
        {
            var focus = _session.Phase == SessionPhase.Focus ? _draftMinutes : _settings.FocusMinutes;
            var rest = _session.Phase == SessionPhase.Break ? _draftMinutes : _settings.BreakMinutes;
            if (_runtime is not null) _runtime.SetDurations(focus, rest);
            else
            {
                _settings.FocusMinutes = focus;
                _settings.BreakMinutes = rest;
                _session.Configure(focus, rest);
            }
            DurationError.Text = string.Empty;
            _invalidInput = false;
            RefreshDisplay();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unable to save focus durations.", ex);
            _draftMinutes = ConfiguredMinutes;
            _editingMinutes = _invalidInput = false;
            DurationError.Text = ex is InvalidOperationException ? ex.Message : "时长未保存，已恢复原值。请检查配置目录权限后重试。";
            RefreshDisplay();
            return false;
        }
    }

    private void RefreshDisplay()
    {
        var seconds = (int)Math.Ceiling(_session.Remaining.TotalSeconds);
        TimeText.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        var phase = _session.Phase == SessionPhase.Focus ? "专注" : "休息";
        PhaseText.Text = _session.Status switch
        {
            SessionStatus.Ready => "准备" + phase,
            SessionStatus.Paused => phase + "已暂停",
            SessionStatus.Completed => phase + "完成",
            _ => phase + "中"
        };
        ToggleText.Text = _session.Status switch
        {
            SessionStatus.Running => "暂停",
            SessionStatus.Paused => "继续",
            SessionStatus.Completed when _session.Phase == SessionPhase.Focus => "开始休息",
            SessionStatus.Ready when _session.Phase == SessionPhase.Break => "开始休息",
            _ => "开始专注"
        };
        ToggleIcon.Text = _session.Status == SessionStatus.Running ? "\uE769" : "\uE768";
        ResetButton.IsEnabled = _session.Status != SessionStatus.Ready;
        SkipButton.IsEnabled = _session.Phase == SessionPhase.Break ||
            (_session.Phase == SessionPhase.Focus && _session.Status == SessionStatus.Completed);
        _refreshing = true;
        FocusMode.IsChecked = _session.Phase == SessionPhase.Focus;
        BreakMode.IsChecked = _session.Phase == SessionPhase.Break;
        FocusMode.IsEnabled = BreakMode.IsEnabled = _session.Status is SessionStatus.Ready or SessionStatus.Completed;
        Dial.IsEnabled = CanEdit;
        Dial.Maximum = MaximumMinutes;
        Dial.Value = _draftMinutes;
        Dial.Ratio = CanEdit ? _draftMinutes / (double)MaximumMinutes : _session.Remaining.TotalSeconds / _session.Duration.TotalSeconds;
        MinutesInput.Visibility = CanEdit ? Visibility.Visible : Visibility.Hidden;
        TimeText.Visibility = CanEdit ? Visibility.Hidden : Visibility.Visible;
        MinuteUnit.Visibility = CanEdit ? Visibility.Visible : Visibility.Hidden;
        if (!_editingMinutes && !_invalidInput) MinutesInput.Text = _draftMinutes.ToString(CultureInfo.InvariantCulture);
        UpdatePresetLayout();
        var values = _session.Phase == SessionPhase.Focus ? new[] { 15, 25, 45, 60 } : new[] { 5, 10, 15, 20 };
        var buttons = new[] { Preset1, Preset2, Preset3, Preset4 };
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Tag = values[i];
            buttons[i].Content = $"{values[i]} 分钟";
            buttons[i].IsChecked = _draftMinutes == values[i];
        }
        _refreshing = false;
        SessionsText.Text = $"本次完成 {_session.CompletedFocusSessions} 次";
        DurationText.Text = $"专注 {_settings.FocusMinutes} 分钟 · 休息 {_settings.BreakMinutes} 分钟";
        QuietStatus.Text = _isQuiet() ? "勿扰中" : string.Empty;
        if (_runtime is not null)
        {
            var today = _runtime.History.Totals(_runtime.Today);
            TodaySummary.Text = (_compactLayout ? string.Empty : $"今日 {today.Minutes} 分钟 · ") + (_runtime.History.LastError is null ? "学习记录" : "记录未保存");
            HistoryButton.ToolTip = _runtime.History.LastError;
        }
        RefreshAvatar();
        UpdateResponsiveLayout();
    }
}
