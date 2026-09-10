using System;
using System.Collections.Generic;
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
    public FocusPanelController Controller { get; }
    private bool CanEdit => Controller.CanEdit;
    private int MaximumMinutes => Controller.MaximumMinutes;

    public FocusWindow(CompanionSession session, PetSettings settings, PetPackage pet, Func<bool> isQuiet, CompanionRuntime? runtime = null, bool managePlacement = false)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _isQuiet = isQuiet;
        _runtime = runtime;
        Controller = new FocusPanelController(session, settings, runtime is null ? null : runtime.SetDurations,
            () => runtime?.IsDisposed == true, ex => AppLogger.Error("Unable to save focus durations.", ex));
        _managePlacement = managePlacement && runtime is not null;
        InitializeWindowLayout();
        MinutesInput.TextChanged += (_, _) => { if (!_refreshing) Controller.EditMinuteText(MinutesInput.Text); };
        Dial.ValueChanged += Dial_ValueChanged;
        Dial.CommitRequested += immediate => { _saveTimer.Stop(); if (immediate) SaveDuration(); else _saveTimer.Start(); };
        _saveTimer.Tick += (_, _) => { if (Dial.IsDragging) return; _saveTimer.Stop(); SaveDuration(); };
        HistoryButton.IsEnabled = runtime is not null;
        UpdatePet(pet);
        _session.Changed += OnSessionChanged;
        _session.Completed += OnCompleted;
        Controller.Changed += RefreshDisplay;
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
            Controller.Changed -= RefreshDisplay;
            Controller.Dispose();
        };
        RefreshDisplay();
    }

    public void UpdatePet(PetPackage pet)
    {
        _pet = pet;
        _frames.Clear();
        _avatarAnimation = null;
        PetImage.ToolTip = pet.Manifest.Name;
        System.Windows.Automation.AutomationProperties.SetName(PetImage, pet.Manifest.Name);
        RefreshAvatar();
    }

    internal void CommitPendingDurations()
    {
        Dial.CancelDrag();
        if (_saveTimer.IsEnabled) SaveDuration();
        if (Controller.IsEditingMinutes && CanEdit) CommitMinutes();
    }

    private void OnSessionChanged()
    {
        _saveTimer.Stop();
        _refreshing = true;
        Dial.CancelDrag();
        _refreshing = false;
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
            if (!UiTheme.MotionEnabled) { ContentRoot.BeginAnimation(OpacityProperty, null); MiniRoot.BeginAnimation(OpacityProperty, null); }
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
        _saveTimer.Stop();
        Controller.Toggle();
    }

    internal void ToggleFromShortcut()
    {
        _saveTimer.Stop();
        if (!Controller.Toggle()) throw new InvalidOperationException(Controller.Error.Length > 0 ? Controller.Error : "当前无法切换计时状态。");
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Controller.Reset();
    private void Skip_Click(object sender, RoutedEventArgs e) => Controller.SkipBreak();
    private void History_Click(object sender, RoutedEventArgs e) => _runtime?.OpenHistory(this);

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        _saveTimer.Stop();
        Controller.SelectPhase((sender as RadioButton)?.Tag as string == "Break" ? SessionPhase.Break : SessionPhase.Focus);
        RefreshDisplay();
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || (sender as RadioButton)?.Tag is not int minutes) return;
        _saveTimer.Stop();
        Controller.SelectPreset(minutes);
    }

    private void Dial_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || !CanEdit) return;
        Controller.PreviewDuration((int)Math.Round(Dial.Value));
    }

    private void Minutes_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!CanEdit) return;
        if (_saveTimer.IsEnabled) SaveDuration();
        Controller.BeginMinuteEdit();
        MinutesInput.SelectAll();
    }

    private void Minutes_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Controller.IsEditingMinutes && CanEdit) { _saveTimer.Stop(); Controller.EndMinuteEdit(); }
    }

    private void Minutes_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { if (CommitMinutes()) ToggleButton.Focus(); e.Handled = true; }
        if (e.Key == Key.Escape)
        {
            Controller.CancelMinuteEdit();
            ToggleButton.Focus();
            e.Handled = true;
        }
    }

    private bool CommitMinutes()
    {
        _saveTimer.Stop();
        return Controller.CommitMinutes();
    }

    private bool SaveDuration()
    {
        _saveTimer.Stop();
        return Controller.SaveDuration();
    }

    private void RefreshDisplay()
    {
        var snapshot = Controller.Snapshot();
        TimeText.Text = snapshot.TimeText;
        PhaseText.Text = snapshot.PhaseText;
        ToggleText.Text = snapshot.ToggleText;
        ToggleIcon.Text = snapshot.Status == SessionStatus.Running ? "\uE769" : "\uE768";
        ResetButton.IsEnabled = snapshot.CanReset;
        SkipButton.IsEnabled = snapshot.CanSkip;
        _refreshing = true;
        FocusMode.IsChecked = _session.Phase == SessionPhase.Focus;
        BreakMode.IsChecked = _session.Phase == SessionPhase.Break;
        FocusMode.IsEnabled = BreakMode.IsEnabled = snapshot.CanSwitchPhase;
        Dial.IsEnabled = CanEdit;
        Dial.Maximum = MaximumMinutes;
        Dial.Value = Controller.DraftMinutes;
        Dial.Ratio = CanEdit ? Controller.DraftMinutes / (double)MaximumMinutes : snapshot.RemainingRatio;
        MinutesInput.Visibility = CanEdit ? Visibility.Visible : Visibility.Hidden;
        TimeText.Visibility = CanEdit ? Visibility.Hidden : Visibility.Visible;
        MinuteUnit.Visibility = CanEdit ? Visibility.Visible : Visibility.Hidden;
        if (MinutesInput.Text != Controller.MinuteText) MinutesInput.Text = Controller.MinuteText;
        DurationError.Text = Controller.Error;
        UpdatePresetLayout();
        var values = Controller.Presets;
        var buttons = new[] { Preset1, Preset2, Preset3, Preset4 };
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Tag = values[i];
            buttons[i].Content = $"{values[i]} 分钟";
            buttons[i].IsChecked = Controller.DraftMinutes == values[i];
        }
        _refreshing = false;
        SessionsText.Text = $"本次完成 {snapshot.CompletedSessions} 次";
        DurationText.Text = $"专注 {_settings.FocusMinutes} 分钟 · 休息 {_settings.BreakMinutes} 分钟";
        QuietStatus.Text = _isQuiet() ? "勿扰中" : string.Empty;
        if (_runtime is not null)
        {
            var today = _runtime.History.Totals(_runtime.Today);
            TodaySummary.Text = (_compactLayout ? string.Empty : $"今日 {today.Minutes} 分钟 · ") + (_runtime.History.LastError is null ? "专注记录" : "记录未保存");
            HistoryButton.ToolTip = _runtime.History.LastError;
        }
        RefreshAvatar();
        UpdateResponsiveLayout();
        RefreshMiniStatus();
    }
}
