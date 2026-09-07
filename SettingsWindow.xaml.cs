using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace YeShunguangPet;

public partial class SettingsWindow : Window
{
    private const string RepositoryUrl = "https://github.com/Resker666/YeShunguangPet";
    private readonly PetSettings _workingSettings;
    private readonly PetCatalog _catalog;
    private readonly string _activePetId;
    private readonly DispatcherTimer _previewTimer = new();
    private PetPackage? _previewPet;
    private PetAnimation? _previewAnimation;
    private int _previewFrame;

    public SettingsWindow(PetSettings settings, bool summonHotkeyRegistered, PetCatalog catalog, PetPackage currentPet)
    {
        InitializeComponent();

        _workingSettings = settings.Clone();
        _catalog = catalog;
        _activePetId = currentPet.Manifest.Id;
        ScaleSlider.Value = _workingSettings.Scale * 100;
        TopmostCheckBox.IsChecked = _workingSettings.Topmost;
        ClickThroughCheckBox.IsChecked = _workingSettings.ClickThrough;
        EdgeAutoHideCheckBox.IsChecked = _workingSettings.EdgeAutoHide;
        LaunchAtStartupCheckBox.IsChecked = _workingSettings.LaunchAtStartup;
        LookAtMouseCheckBox.IsChecked = _workingSettings.LookAtMouse;
        RandomIdleCheckBox.IsChecked = _workingSettings.RandomIdleActions;
        IdleIntervalSlider.Value = _workingSettings.IdleActionIntervalSeconds;
        DesktopRoamingCheckBox.IsChecked = _workingSettings.DesktopRoaming;
        RoamIntervalSlider.Value = _workingSettings.RoamIntervalSeconds;
        RoamSpeedSlider.Value = _workingSettings.RoamSpeed;
        ClickInteractionCheckBox.IsChecked = _workingSettings.ClickInteraction;
        PauseNearMouseCheckBox.IsChecked = _workingSettings.PauseNearMouse;
        MouseRadiusSlider.Value = _workingSettings.MousePauseRadius;
        FocusMinutesSlider.Value = _workingSettings.FocusMinutes;
        BreakMinutesSlider.Value = _workingSettings.BreakMinutes;
        BreakRemindersCheckBox.IsChecked = _workingSettings.BreakRemindersEnabled;
        BreakReminderSlider.Value = _workingSettings.BreakReminderMinutes;
        NotificationsCheckBox.IsChecked = _workingSettings.NotificationsEnabled;
        PauseDuringFocusCheckBox.IsChecked = _workingSettings.PauseDuringFocus;
        SessionAnimationCheckBox.IsChecked = _workingSettings.SessionAnimationEnabled;
        DoNotDisturbCheckBox.IsChecked = _workingSettings.DoNotDisturb;
        QuietHoursCheckBox.IsChecked = _workingSettings.QuietHoursEnabled;
        QuietStartInput.Text = TimeSpan.FromMinutes(_workingSettings.QuietStartMinute).ToString(@"hh\:mm");
        QuietEndInput.Text = TimeSpan.FromMinutes(_workingSettings.QuietEndMinute).ToString(@"hh\:mm");

        ScaleSlider.ValueChanged += (_, _) => UpdateValueLabels();
        IdleIntervalSlider.ValueChanged += (_, _) => UpdateValueLabels();
        RoamIntervalSlider.ValueChanged += (_, _) => UpdateValueLabels();
        RoamSpeedSlider.ValueChanged += (_, _) => UpdateValueLabels();
        MouseRadiusSlider.ValueChanged += (_, _) => UpdateValueLabels();
        FocusMinutesSlider.ValueChanged += (_, _) => UpdateValueLabels();
        BreakMinutesSlider.ValueChanged += (_, _) => UpdateValueLabels();
        BreakReminderSlider.ValueChanged += (_, _) => UpdateValueLabels();

        HotkeyStatusText.Text = summonHotkeyRegistered ? "Ctrl + Alt + Y" : "快捷键注册失败";
        VersionText.Text = $"版本 {GetApplicationVersion()}";
        UpdateValueLabels();
        _previewTimer.Tick += PreviewTimer_Tick;
        Closed += (_, _) => _previewTimer.Stop();
        SettingsTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, SettingsTabs)) return;
            if (SkinTab.IsSelected) StartPreview();
            else _previewTimer.Stop();
        };
        RefreshPets(currentPet.Manifest.Id);
    }

    public PetSettings? Result { get; private set; }
    public PetPackage? SelectedPackage { get; private set; }

    private void UpdateValueLabels()
    {
        ScaleValueText.Text = $"{ScaleSlider.Value:0}%";
        IdleIntervalValueText.Text = $"{IdleIntervalSlider.Value:0} 秒";
        RoamIntervalValueText.Text = $"{RoamIntervalSlider.Value:0} 秒";
        RoamSpeedValueText.Text = $"{RoamSpeedSlider.Value:0} 像素/秒";
        MouseRadiusText.Text = $"{MouseRadiusSlider.Value:0} 像素";
        FocusMinutesText.Text = $"{FocusMinutesSlider.Value:0} 分钟";
        BreakMinutesText.Text = $"{BreakMinutesSlider.Value:0} 分钟";
        BreakReminderText.Text = $"{BreakReminderSlider.Value:0} 分钟";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!CollectCompanionSettings()) return;
        if (PetSelector.SelectedItem is not PetEntry entry) return;
        try
        {
            SelectedPackage = PetPackage.Load(entry.ManifestPath);
        }
        catch (Exception ex)
        {
            SkinStatus.Text = ex.Message;
            SettingsTabs.SelectedItem = SkinTab;
            return;
        }
        _workingSettings.SelectedPetId = SelectedPackage.Manifest.Id;
        _workingSettings.Scale = ScaleSlider.Value / 100;
        _workingSettings.Topmost = TopmostCheckBox.IsChecked == true;
        _workingSettings.ClickThrough = ClickThroughCheckBox.IsChecked == true;
        _workingSettings.EdgeAutoHide = EdgeAutoHideCheckBox.IsChecked == true;
        _workingSettings.LaunchAtStartup = LaunchAtStartupCheckBox.IsChecked == true;
        _workingSettings.LookAtMouse = LookAtMouseCheckBox.IsChecked == true;
        _workingSettings.RandomIdleActions = RandomIdleCheckBox.IsChecked == true;
        _workingSettings.IdleActionIntervalSeconds = (int)Math.Round(IdleIntervalSlider.Value);
        _workingSettings.DesktopRoaming = DesktopRoamingCheckBox.IsChecked == true;
        _workingSettings.RoamIntervalSeconds = (int)Math.Round(RoamIntervalSlider.Value);
        _workingSettings.RoamSpeed = RoamSpeedSlider.Value;

        Result = _workingSettings;
        DialogResult = true;
    }

    private bool CollectCompanionSettings()
    {
        CompanionErrorText.Text = string.Empty;
        if (QuietHoursCheckBox.IsChecked == true)
        {
            if (!TimeOnly.TryParseExact(QuietStartInput.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var start) ||
                !TimeOnly.TryParseExact(QuietEndInput.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var end))
            {
                CompanionErrorText.Text = "请输入有效时间，例如 22:00 和 08:00。";
                SettingsTabs.SelectedItem = CompanionTab;
                Dispatcher.BeginInvoke(() => CompanionErrorText.BringIntoView());
                return false;
            }
            _workingSettings.QuietStartMinute = start.Hour * 60 + start.Minute;
            _workingSettings.QuietEndMinute = end.Hour * 60 + end.Minute;
        }
        _workingSettings.ClickInteraction = ClickInteractionCheckBox.IsChecked == true;
        _workingSettings.PauseNearMouse = PauseNearMouseCheckBox.IsChecked == true;
        _workingSettings.MousePauseRadius = (int)Math.Round(MouseRadiusSlider.Value);
        _workingSettings.FocusMinutes = (int)Math.Round(FocusMinutesSlider.Value);
        _workingSettings.BreakMinutes = (int)Math.Round(BreakMinutesSlider.Value);
        _workingSettings.BreakRemindersEnabled = BreakRemindersCheckBox.IsChecked == true;
        _workingSettings.BreakReminderMinutes = (int)Math.Round(BreakReminderSlider.Value);
        _workingSettings.NotificationsEnabled = NotificationsCheckBox.IsChecked == true;
        _workingSettings.PauseDuringFocus = PauseDuringFocusCheckBox.IsChecked == true;
        _workingSettings.SessionAnimationEnabled = SessionAnimationCheckBox.IsChecked == true;
        _workingSettings.DoNotDisturb = DoNotDisturbCheckBox.IsChecked == true;
        _workingSettings.QuietHoursEnabled = QuietHoursCheckBox.IsChecked == true;
        return true;
    }

    private void Recall_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.RecallToPrimaryScreen();
            ClickThroughCheckBox.IsChecked = false;
        }
    }

    private void OpenRepository_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RepositoryUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to open repository URL.", ex);
            MessageBox.Show(this, ex.Message, "无法打开项目主页", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLogger.OpenFolder();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to open logs directory.", ex);
            MessageBox.Show(this, ex.Message, "无法打开日志目录", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string GetApplicationVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知";
    }

    private void RefreshPets(string? selectId = null)
    {
        var scan = _catalog.Scan();
        PetSelector.ItemsSource = scan.Pets;
        PetSelector.SelectedItem = scan.Pets.FirstOrDefault(p => p.Id == (selectId ?? _workingSettings.SelectedPetId))
            ?? scan.Pets.FirstOrDefault(p => p.Id == PetPackage.DefaultId) ?? scan.Pets.FirstOrDefault();
        if (scan.Errors.Count > 0) SkinStatus.Text = string.Join(Environment.NewLine, scan.Errors);
        else if (scan.Pets.Count == 0) SkinStatus.Text = "没有可用皮肤。";
    }

    private void PetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _previewTimer.Stop();
        _previewPet = null;
        _previewAnimation = null;
        SkinPreview.Source = null;
        PreviewActionSelector.ItemsSource = null;
        AboutPetImage.Source = null;
        AboutPetName.Text = string.Empty;
        AboutPetDescription.Text = string.Empty;
        SkinDescription.Text = string.Empty;
        SkinDetails.Text = string.Empty;
        SaveButton.IsEnabled = false;
        ExportPetButton.IsEnabled = false;
        UpdatePetButton.IsEnabled = false;
        DeletePetButton.IsEnabled = false;
        if (PetSelector.SelectedItem is not PetEntry entry) return;
        try
        {
            _previewPet = PetPackage.Load(entry.ManifestPath);
            var m = _previewPet.Manifest;
            SkinDescription.Text = m.Description;
            SkinDetails.Text = $"{m.CellWidth} × {m.CellHeight} · {m.Animations.Count} 个动作 · {(entry.Bundled ? "随附" : "已导入")}";
            AboutPetImage.Source = _previewPet.Preview;
            AboutPetName.Text = m.Name;
            AboutPetDescription.Text = m.Description;
            LookAtMouseCheckBox.IsEnabled = _previewPet.CanLook;
            RandomIdleCheckBox.IsEnabled = _previewPet.RandomActions.Length > 0;
            DesktopRoamingCheckBox.IsEnabled = _previewPet.CanRoam;
            PreviewActionSelector.ItemsSource = Enum.GetValues<PetState>().Where(_previewPet.Supports)
                .Select(s => new PreviewAction(s, ActionName(s))).ToArray();
            PreviewActionSelector.SelectedIndex = 0;
            SaveButton.IsEnabled = true;
            ExportPetButton.IsEnabled = true;
            UpdatePetButton.IsEnabled = !entry.Bundled;
            DeletePetButton.IsEnabled = !entry.Bundled && entry.Id != _activePetId;
            DeletePetButton.ToolTip = entry.Bundled ? "随附皮肤不可删除" : entry.Id == _activePetId
                ? "请先切换并保存其他皮肤，再删除当前皮肤" : "删除选中的导入皮肤";
            SkinStatus.Text = string.Empty;
        }
        catch (Exception ex)
        {
            SkinStatus.Text = ex.Message;
        }
    }

    private void ImportPet_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "导入皮肤",
            Filter = "皮肤包 (*.zip;pet.json)|*.zip;pet.json|ZIP 皮肤包 (*.zip)|*.zip|皮肤清单 (pet.json)|pet.json",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var imported = _catalog.Import(picker.FileName);
            RefreshPets(imported.Id);
            SkinStatus.Text = $"已导入：{imported.Name}";
        }
        catch (Exception ex)
        {
            SkinStatus.Text = ex.Message;
        }
    }

    private void RefreshPets_Click(object sender, RoutedEventArgs e)
        => RefreshPets((PetSelector.SelectedItem as PetEntry)?.Id);

    private void ExportPet_Click(object sender, RoutedEventArgs e)
    {
        if (PetSelector.SelectedItem is not PetEntry entry) return;
        var picker = new SaveFileDialog { Title = "导出皮肤", Filter = "ZIP 皮肤包 (*.zip)|*.zip",
            FileName = entry.Id + ".zip", DefaultExt = ".zip", AddExtension = true, OverwritePrompt = true };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            PetArchive.Export(entry.ManifestPath, picker.FileName, overwrite: true);
            SkinStatus.Text = $"已导出：{picker.FileName}";
        }
        catch (Exception ex) { SkinStatus.Text = ex.Message; }
    }

    private void UpdatePet_Click(object sender, RoutedEventArgs e)
    {
        if (PetSelector.SelectedItem is not PetEntry { Bundled: false } entry) return;
        var picker = new OpenFileDialog { Title = "更新皮肤（相同 id）",
            Filter = "皮肤包 (*.zip;pet.json)|*.zip;pet.json", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, $"更新“{entry.Name}”？\n旧文件会保留为备份。此操作立即生效，不受设置页的取消按钮影响。",
                "更新皮肤", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var updated = _catalog.Update(entry, picker.FileName);
            RefreshPets(updated.Id);
            SkinStatus.Text = $"已更新：{updated.Name}。保存后应用到桌面。";
        }
        catch (Exception ex) { SkinStatus.Text = ex.Message; }
    }

    private void DeletePet_Click(object sender, RoutedEventArgs e)
    {
        if (PetSelector.SelectedItem is not PetEntry { Bundled: false } entry) return;
        if (MessageBox.Show(this, $"从列表删除“{entry.Name}”？\n原文件会移到皮肤目录内的 .deleted- 备份文件夹。此操作立即生效。",
                "删除皮肤", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        try
        {
            _catalog.Delete(entry, _activePetId);
            RefreshPets(_activePetId);
            SkinStatus.Text = $"已删除：{entry.Name}。原文件已保留。";
        }
        catch (Exception ex) { SkinStatus.Text = ex.Message; }
    }

    private void OpenPets_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_catalog.UserDirectory);
            Process.Start(new ProcessStartInfo(_catalog.UserDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SkinStatus.Text = ex.Message;
        }
    }

    private void PreviewAction_SelectionChanged(object sender, SelectionChangedEventArgs e) => StartPreview();
    private void Replay_Click(object sender, RoutedEventArgs e) => StartPreview();

    private void StartPreview()
    {
        _previewTimer.Stop();
        if (_previewPet is null || PreviewActionSelector.SelectedItem is not PreviewAction action) return;
        _previewAnimation = _previewPet.GetAnimation(action.State);
        _previewFrame = 0;
        RenderPreview();
        if (SkinTab.IsSelected) _previewTimer.Start();
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_previewAnimation is null) return;
        if (!_previewAnimation.Loop && _previewFrame == _previewAnimation.FrameCount - 1)
        {
            _previewTimer.Stop();
            return;
        }
        _previewFrame = (_previewFrame + 1) % _previewAnimation.FrameCount;
        RenderPreview();
    }

    private void RenderPreview()
    {
        if (_previewPet is null || _previewAnimation is null) return;
        SkinPreview.Source = _previewPet.GetFrame(_previewAnimation.Row, _previewAnimation.StartColumn + _previewFrame);
        _previewTimer.Interval = TimeSpan.FromMilliseconds(_previewAnimation.DurationsMs[_previewFrame]);
    }

    private static string ActionName(PetState state) => state switch
    {
        PetState.Idle => "待机",
        PetState.RunningRight => "向右走动",
        PetState.RunningLeft => "向左走动",
        PetState.Waving => "打招呼",
        PetState.Jumping => "跳跃",
        PetState.Failed => "失败",
        PetState.Waiting => "等待",
        PetState.Running => "工作",
        PetState.Review => "检查",
        _ => state.ToString()
    };

    private sealed record PreviewAction(PetState State, string Name);
}
