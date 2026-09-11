using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace YeShunguangPet;

public partial class DiagnosticsWindow : ThemedWindow
{
    private readonly DesktopSession? _desktop;
    private readonly ResilientLog? _log;
    private readonly DesktopSettingsStore _configStore = new();
    private DiagnosticReport? _report;
    private string? _logPath;
    private LogSnapshot? _lastLogs;
    private bool _closed, _busy;
    public DiagnosticsWindow(DesktopSession? desktop, ResilientLog? log = null)
    {
        _desktop = desktop;
        _log = log;
        InitializeComponent();
        InitializeSampling();
        VersionText.Text = "版本 " + DiagnosticReport.ApplicationVersion;
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => { _closed = true; CloseSampling(); };
    }

    internal async Task RefreshAsync()
    {
        if (_closed || _busy) return;
        _busy = true;
        RefreshButton.IsEnabled = ExportButton.IsEnabled = false;
        StatusText.Text = "正在读取诊断信息…";
        try
        {
            var logs = await Task.Run(() =>
            {
                if (_log is null) { AppLogger.Info("Diagnostic snapshot requested."); return AppLogger.Capture(); }
                _log.Write("INFO", "Diagnostic snapshot requested.");
                return _log.Capture();
            });
            if (_closed) return;
            _lastLogs = logs;
            _report = DiagnosticReport.Capture(_desktop, logs, activity: SamplingCapture);
            _logPath = logs.ActivePath;
            LogStatusText.Text = logs.ActivePath is null ? "日志仅保存在内存中" : logs.ActiveLocation == 0 ? "日志写入正常" : "正在使用备用日志目录";
            LogPathText.Text = logs.ActivePath ?? "没有可写的日志文件";
            FolderButton.IsEnabled = _logPath is not null;
            RefreshResourceStatus(_report.InformationJson);
            RefreshConfigStatus();
            InformationText.Text = _report.InformationJson;
            LogsText.Text = _report.LogsText;
            StatusText.Text = DateTime.Now.ToString("HH:mm:ss") + " 已刷新";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to capture diagnostic information.", ex);
            if (!_closed) StatusText.Text = "无法读取诊断信息：" + ex.Message;
        }
        finally
        {
            _busy = false;
            if (!_closed) { RefreshButton.IsEnabled = true; ExportButton.IsEnabled = _report is not null; }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null || _busy) return;
        if (_lastLogs is not null) _report = DiagnosticReport.Capture(_desktop, _lastLogs, activity: SamplingCapture);
        var picker = new SaveFileDialog { Title = "导出脱敏诊断包", Filter = "诊断包 (*.zip)|*.zip", DefaultExt = ".zip", AddExtension = true,
            FileName = $"YeShunguangPet-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip", OverwritePrompt = true };
        if (picker.ShowDialog(this) != true) return;
        var report = _report;
        _busy = true;
        ExportButton.IsEnabled = RefreshButton.IsEnabled = false;
        try
        {
            await Task.Run(() => report.Export(picker.FileName, overwrite: true));
            if (!_closed) StatusText.Text = "已保存：" + picker.FileName;
        }
        catch (Exception ex) { AppLogger.Error("Failed to export diagnostics.", ex); if (!_closed) StatusText.Text = "导出失败：" + ex.Message; }
        finally { _busy = false; if (!_closed) ExportButton.IsEnabled = RefreshButton.IsEnabled = true; }
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_logPath is null) return;
        try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(_logPath)!) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void RefreshResourceStatus(string information)
    {
        try
        {
            using var document = JsonDocument.Parse(information);
            var resources = document.RootElement.GetProperty("Resources");
            ResourceStatusText.Text = $"资源：托管内存 {FormatBytes(resources.GetProperty("ManagedMemoryBytes").GetInt64())} · 私有内存 {FormatBytes(resources.GetProperty("PrivateMemoryBytes").GetInt64())} · 精灵租约 {resources.GetProperty("SpriteImageLeases").GetInt32()} · 角色 {resources.GetProperty("ActiveRoles").GetInt32()} · 贴图 {resources.GetProperty("ActivePins").GetInt32()}";
        }
        catch { ResourceStatusText.Text = "资源摘要暂不可用"; }
    }
    private void RefreshConfigStatus()
    {
        try
        {
            if (!File.Exists(_configStore.BackupPath))
            {
                ConfigStatusText.Text = "配置备份：暂无 .bak 备份";
                RestoreConfigButton.IsEnabled = false;
                return;
            }
            var info = new FileInfo(_configStore.BackupPath);
            ConfigStatusText.Text = $"配置备份：{info.LastWriteTime:yyyy-MM-dd HH:mm} · {FormatBytes(info.Length)} · 恢复后需重启程序";
            RestoreConfigButton.IsEnabled = true;
        }
        catch (Exception ex) { ConfigStatusText.Text = "配置备份状态读取失败：" + ex.Message; RestoreConfigButton.IsEnabled = false; }
    }
    private void RestoreConfig_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_configStore.BackupPath)) return;
        if (AppDialog.Show(this, "将使用最后一次有效配置替换当前配置。当前文件会保留为 .pre-recovery.bak，恢复后请重启程序。继续？", "恢复配置备份", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        try { _configStore.RestoreBackup(); AppLogger.Info("Configuration backup restored from diagnostics window."); StatusText.Text = "配置已恢复，请重启程序使其生效。"; RefreshConfigStatus(); }
        catch (Exception ex) { AppLogger.Error("Failed to restore configuration backup.", ex); StatusText.Text = "恢复失败：" + ex.Message; }
    }
    private static string FormatBytes(long bytes)
        => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.0} KiB" : $"{bytes / 1024.0 / 1024:0.0} MiB";
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
