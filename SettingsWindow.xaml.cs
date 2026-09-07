using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace YeShunguangPet;

public partial class SettingsWindow : Window
{
    private const string RepositoryUrl = "https://github.com/Resker666/YeShunguangPet";
    private readonly PetSettings _workingSettings;

    public SettingsWindow(PetSettings settings, bool summonHotkeyRegistered)
    {
        InitializeComponent();

        _workingSettings = settings.Clone();
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
    }

    public PetSettings? Result { get; private set; }

    private void UpdateValueLabels()
    {
        ScaleValueText.Text = $"{ScaleSlider.Value:0}%";
        IdleIntervalValueText.Text = $"{IdleIntervalSlider.Value:0} 秒";
        RoamIntervalValueText.Text = $"{RoamIntervalSlider.Value:0} 秒";
        RoamSpeedValueText.Text = $"{RoamSpeedSlider.Value:0} 像素/秒";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
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
}
