using System;
using System.Diagnostics;
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
    private readonly DispatcherTimer _previewTimer = new();
    private PetPackage? _previewPet;
    private PetAnimation? _previewAnimation;
    private int _previewFrame;

    public SettingsWindow(PetSettings settings, bool summonHotkeyRegistered, PetCatalog catalog, PetPackage currentPet)
    {
        InitializeComponent();

        _workingSettings = settings.Clone();
        _catalog = catalog;
        ScaleSlider.Value = _workingSettings.Scale * 100;
        TopmostCheckBox.IsChecked = _workingSettings.Topmost;
        ClickThroughCheckBox.IsChecked = _workingSettings.ClickThrough;
        LaunchAtStartupCheckBox.IsChecked = _workingSettings.LaunchAtStartup;
        LookAtMouseCheckBox.IsChecked = _workingSettings.LookAtMouse;
        RandomIdleCheckBox.IsChecked = _workingSettings.RandomIdleActions;
        IdleIntervalSlider.Value = _workingSettings.IdleActionIntervalSeconds;
        DesktopRoamingCheckBox.IsChecked = _workingSettings.DesktopRoaming;
        RoamIntervalSlider.Value = _workingSettings.RoamIntervalSeconds;
        RoamSpeedSlider.Value = _workingSettings.RoamSpeed;

        ScaleSlider.ValueChanged += (_, _) => UpdateValueLabels();
        IdleIntervalSlider.ValueChanged += (_, _) => UpdateValueLabels();
        RoamIntervalSlider.ValueChanged += (_, _) => UpdateValueLabels();
        RoamSpeedSlider.ValueChanged += (_, _) => UpdateValueLabels();

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
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
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
            Filter = "皮肤清单 (pet.json)|pet.json",
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
