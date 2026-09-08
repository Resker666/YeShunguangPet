using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class PetManagerWindow : ThemedWindow
{
    private readonly DesktopSession _desktop;
    private readonly ObservableCollection<PetCard> _cards = new();
    private readonly HashSet<MainWindow> _pendingSaves = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private IReadOnlyList<PetEntry> _skins = Array.Empty<PetEntry>();
    private bool _ready, _refreshing, _closed, _busy;
    private PetCard? Selected => Instances.SelectedItem as PetCard;

    public PetManagerWindow(DesktopSession desktop)
    {
        _desktop = desktop;
        InitializeComponent();
        Instances.ItemsSource = RoleChoice.ItemsSource = _cards;
        _saveTimer.Tick += (_, _) => FlushSaves();
        _previewTimer.Tick += (_, _) => { foreach (var card in _cards) card.Animate(); };
        _desktop.Changed += RefreshInstances;
        Loaded += async (_, _) => { await RefreshSkinsAsync(); UpdatePreviewTimer(); };
        IsVisibleChanged += (_, _) => UpdatePreviewTimer();
        StateChanged += (_, _) => UpdatePreviewTimer();
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            _desktop.Changed -= RefreshInstances;
            FlushSaves();
            _previewTimer.Stop();
        };
        _ready = true;
        AppScope.IsChecked = RolesNav.IsChecked = true;
        RefreshInstances();
    }

    internal async Task RefreshSkinsAsync()
    {
        try
        {
            var scan = await _desktop.Catalog.ScanAsync(_lifetime.Token);
            if (_closed) return;
            _skins = scan.Pets;
            StatusText.Text = string.Join(Environment.NewLine, scan.Errors);
            RefreshInstances();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) StatusText.Text = ex.Message; }
    }

    private void RefreshInstances()
    {
        if (_closed) return;
        _refreshing = true;
        var windows = _desktop.Windows.ToArray();
        foreach (var stale in _cards.Where(c => !windows.Contains(c.Window)).ToArray()) _cards.Remove(stale);
        foreach (var window in windows)
        {
            var card = _cards.FirstOrDefault(c => c.Window == window);
            if (card is null) { card = new PetCard(window); _cards.Add(card); }
            card.Refresh(_desktop);
        }
        if (Instances.SelectedItem is null) Instances.SelectedItem = _cards.FirstOrDefault();
        RoleChoice.SelectedItem = Selected;
        CountText.Text = $"{_cards.Count} / {DesktopConfiguration.MaximumPets} 个角色";
        AddButton.IsEnabled = !_busy && !_desktop.HasSettingsOpen && _cards.Count < DesktopConfiguration.MaximumPets;
        AppSettingsPanel.IsEnabled = QuietCheck.IsEnabled = !_desktop.HasSettingsOpen;
        EmptyState.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QuietCheck.IsChecked = _desktop.Companion.Settings.DoNotDisturb;
        NotificationsCheck.IsChecked = _desktop.Companion.Settings.NotificationsEnabled;
        StartupCheck.IsChecked = _desktop.Companion.Settings.LaunchAtStartup;
        RefreshAppearance();
        _refreshing = false;
        RefreshSelection();
    }

    private void RefreshAppearance()
    {
        var options = _desktop.Configuration.Appearance;
        var wasRefreshing = _refreshing;
        _refreshing = true;
        SystemTheme.IsChecked = options.Theme == "system";
        LightTheme.IsChecked = options.Theme == "light";
        DarkTheme.IsChecked = options.Theme == "dark";
        foreach (var swatch in new[] { RedAccent, GreenAccent, BlueAccent, GrayAccent }) swatch.IsChecked = (string)swatch.Tag == options.Accent;
        MotionCheck.IsChecked = options.ReduceMotion;
        _refreshing = wasRefreshing;
    }

    private void RefreshSelection()
    {
        if (!_ready || _refreshing) return;
        _refreshing = true;
        var card = Selected;
        RoleChoice.SelectedItem = card;
        QuickPanel.IsEnabled = card is not null && !_desktop.HasSettingsOpen;
        QuickPanel.Visibility = card is null ? Visibility.Collapsed : Visibility.Visible;
        NoRoleText.Visibility = card is null ? Visibility.Visible : Visibility.Collapsed;
        if (card is not null)
        {
            var settings = card.Window.InstanceSettings;
            SelectedTitle.Text = card.Name;
            ScaleSlider.Value = settings.Scale * 100;
            ScaleText.Text = $"{settings.Scale:P0}";
            TopmostCheck.IsChecked = settings.Topmost;
            EdgeCheck.IsChecked = settings.EdgeAutoHide;
            ThroughCheck.IsChecked = settings.ClickThrough;
        }
        _refreshing = false;
    }

    private void Instance_Changed(object sender, SelectionChangedEventArgs e) { if (e.Source == Instances) RefreshSelection(); }
    private void RoleChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_refreshing && RoleChoice.SelectedItem is PetCard card) Instances.SelectedItem = card;
    }
    private PetCard? CardFor(object sender)
    {
        if (sender is FrameworkElement { DataContext: PetCard card }) Instances.SelectedItem = card;
        return Selected;
    }

    private void Display_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _refreshing || Selected is not { } card || _desktop.HasSettingsOpen) return;
        Run(() =>
        {
            card.Window.UpdateDisplay(ScaleSlider.Value / 100, TopmostCheck.IsChecked == true, EdgeCheck.IsChecked == true, ThroughCheck.IsChecked == true);
            ScaleText.Text = $"{card.Window.PetScale:P0}";
            card.Refresh(_desktop);
            _pendingSaves.Add(card.Window);
            _saveTimer.Stop();
            _saveTimer.Start();
        });
    }

    private void FlushSaves()
    {
        _saveTimer.Stop();
        var pending = _pendingSaves.ToArray();
        _pendingSaves.Clear();
        foreach (var window in pending.Where(_desktop.Windows.Contains))
        {
            try { window.SaveDisplay(); }
            catch (Exception ex) { AppLogger.Error("Failed to save display settings.", ex); if (!_closed) StatusText.Text = ex.Message; }
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        AddButton.IsEnabled = false;
        try
        {
            await RefreshSkinsAsync();
            if (_closed) return;
            if (_skins.Count == 0) { StatusText.Text = "没有可用的皮肤，请检查皮肤目录。"; return; }
            var picker = new PetPickerWindow(_skins) { Owner = this };
            if (picker.ShowDialog() != true || picker.Selected is not { } entry) return;
            var window = await _desktop.AddAsync(entry.Id, _lifetime.Token);
            if (!_closed) { RefreshInstances(); Instances.SelectedItem = _cards.First(c => c.Window == window); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) StatusText.Text = ex.Message; }
        finally { _busy = false; if (!_closed) RefreshInstances(); }
    }

    private void Remove(PetCard card)
    {
        if (AppDialog.Show(this, $"关闭“{card.Name}”？皮肤文件和学习计时都会保留。", "关闭角色", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        Run(() => { _pendingSaves.Remove(card.Window); _desktop.Remove(card.Window); });
    }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (CardFor(sender) is not { } card || sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button };
        var recall = new MenuItem { Header = "召回角色" };
        recall.Click += (_, _) => Run(card.Window.RecallToPrimaryScreen);
        var remove = new MenuItem { Header = "关闭角色" };
        remove.SetResourceReference(ForegroundProperty, "DangerBrush");
        remove.Click += (_, _) => Remove(card);
        menu.Items.Add(recall); menu.Items.Add(new Separator()); menu.Items.Add(remove);
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }
    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (CardFor(sender) is not { } card) return;
        Run(() => { if (_desktop.IsHidden(card.Window)) card.Window.ShowAndActivate(); else card.Window.HideInstance(); });
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        FlushSaves();
        if (CardFor(sender) is { } card) Run(card.Window.ConfigureInstance);
    }
    private void CompanionSettings_Click(object sender, RoutedEventArgs e)
    {
        FlushSaves();
        if (Selected is { } card) Run(card.Window.ConfigureCompanionSettings);
        else StatusText.Text = "请先添加一个桌面角色。";
    }
    private void RecallAll_Click(object sender, RoutedEventArgs e) => Run(_desktop.RecallAll);
    private void Focus_Click(object sender, RoutedEventArgs e) => Run(_desktop.OpenFocus);
    private void History_Click(object sender, RoutedEventArgs e) => Run(() => _desktop.Companion.OpenHistory(this));
    private void Speech_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } card) Run(() => _desktop.OpenSpeechSettings(card.Window.Package, this));
        else StatusText.Text = "请先添加一个桌面角色。";
    }
    private void Diagnostics_Click(object sender, RoutedEventArgs e) => Run(() => _desktop.OpenDiagnostics(this));
    private void Quiet_Changed(object sender, RoutedEventArgs e) { if (_ready && !_refreshing) Run(() => _desktop.SetQuiet(QuietCheck.IsChecked == true)); }
    private void Notifications_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _refreshing) return;
        Run(() => { var settings = _desktop.Companion.Settings.Clone(); settings.NotificationsEnabled = NotificationsCheck.IsChecked == true; _desktop.UpdateGlobal(settings); });
    }
    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _refreshing) return;
        Run(() =>
        {
            var settings = _desktop.Companion.Settings.Clone();
            settings.LaunchAtStartup = StartupCheck.IsChecked == true;
            PetSettings.SetLaunchAtStartup(settings.LaunchAtStartup);
            _desktop.UpdateGlobal(settings);
        });
    }
    private void Theme_Changed(object sender, RoutedEventArgs e) { if (sender is RadioButton { Tag: string theme }) ChangeAppearance(o => o.Theme = theme); }
    private void Accent_Changed(object sender, RoutedEventArgs e) { if (sender is RadioButton { Tag: string accent }) ChangeAppearance(o => o.Accent = accent); }
    private void Motion_Changed(object sender, RoutedEventArgs e) => ChangeAppearance(o => o.ReduceMotion = MotionCheck.IsChecked == true);
    private void ChangeAppearance(Action<AppearanceOptions> update)
    {
        if (!_ready || _refreshing) return;
        Run(() => { var options = _desktop.Configuration.Appearance.Clone(); update(options); _desktop.UpdateAppearance(options); });
    }

    private void Navigate_Changed(object sender, RoutedEventArgs e) => UpdateNavigation();
    private void Scope_Changed(object sender, RoutedEventArgs e) => UpdateNavigation();
    private void UpdateNavigation()
    {
        if (!_ready) return;
        var settings = SettingsNav.IsChecked == true;
        var role = settings && RoleScope.IsChecked == true;
        RolesPage.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsPage.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        AppSettingsPanel.Visibility = role ? Visibility.Collapsed : Visibility.Visible;
        RoleSettingsPanel.Visibility = role ? Visibility.Visible : Visibility.Collapsed;
        RoleControlsHost.Content = null;
        SettingsRoleHost.Content = null;
        if (role) SettingsRoleHost.Content = QuickPanel;
        else RoleControlsHost.Content = QuickPanel;
        MainScroll.ResetScroll();
        UiMotion.Enter(settings ? SettingsPage : RolesPage);
        RefreshSelection();
        UpdatePreviewTimer();
    }
    internal override void OnThemeUpdated()
    {
        if (!_ready) return;
        RefreshAppearance();
        UpdatePreviewTimer();
    }
    private void UpdatePreviewTimer()
    {
        if (!_ready) return;
        if (!_closed && IsVisible && WindowState != WindowState.Minimized && RolesNav.IsChecked == true && UiTheme.MotionEnabled) _previewTimer.Start();
        else { _previewTimer.Stop(); foreach (var card in _cards) card.ResetPreview(); }
    }
    private void Run(Action action)
    {
        try { StatusText.Text = string.Empty; action(); }
        catch (Exception ex) { StatusText.Text = ex.Message; RefreshInstances(); }
    }

    private sealed class PetCard : INotifyPropertyChanged
    {
        public MainWindow Window { get; }
        public string Name => Window.Package.Manifest.Name;
        public string NumberLabel { get; private set; } = "";
        public string DisplayName => $"{NumberLabel} · {Name}";
        public string Status { get; private set; } = "";
        public string Scale => $"{Window.PetScale:P0}";
        public string VisibilityAction { get; private set; } = "隐藏角色";
        public BitmapSource Preview { get; private set; }
        private PetPackage _package;
        private bool _hidden;
        private long _nextFrame;
        private int _frame;
        public event PropertyChangedEventHandler? PropertyChanged;
        public PetCard(MainWindow window) { Window = window; _package = window.Package; Preview = _package.Preview; }
        public void Refresh(DesktopSession desktop)
        {
            _hidden = desktop.IsHidden(Window);
            NumberLabel = $"{desktop.Number(Window):00}";
            Status = _hidden ? "已隐藏" : Window.IsDocked ? "已收纳" : "显示中";
            VisibilityAction = _hidden ? "显示角色" : "隐藏角色";
            if (_package != Window.Package) { _package = Window.Package; ResetPreview(); }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
        public void ResetPreview() { _frame = 0; _nextFrame = 0; Preview = _package.Preview; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview))); }
        public void Animate()
        {
            if (_hidden || Environment.TickCount64 < _nextFrame) return;
            var idle = _package.GetAnimation(PetState.Idle);
            Preview = _package.GetFrame(idle.Row, idle.StartColumn + _frame);
            _nextFrame = Environment.TickCount64 + idle.DurationsMs[_frame];
            _frame = (_frame + 1) % idle.DurationsMs.Length;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
        }
    }
}
