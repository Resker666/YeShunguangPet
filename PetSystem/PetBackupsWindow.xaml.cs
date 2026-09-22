using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace YeShunguangPet;

public partial class PetBackupsWindow : ThemedWindow
{
    private readonly PetCatalog _catalog;
    private PetBackupResult? _scan;
    public string? RestoredId { get; private set; }

    public PetBackupsWindow(PetCatalog catalog)
    {
        _catalog = catalog;
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        FilterSelector.SelectedIndex = 0;
        RefreshBackups();
    }

    private void RefreshBackups()
    {
        _scan = _catalog.ScanBackups();
        ApplyFilter();
        StatusText.Text = string.Join(Environment.NewLine, _scan.Errors);
    }

    private void ApplyFilter()
    {
        if (_scan is null) return;
        var previous = (BackupSelector.SelectedItem as PetBackupEntry)?.DirectoryPath;
        var items = _scan.Backups.Where(b => FilterSelector.SelectedIndex == 0 || b.Deleted == (FilterSelector.SelectedIndex == 2)).ToArray();
        BackupSelector.ItemsSource = items;
        BackupSelector.SelectedItem = items.FirstOrDefault(b => b.DirectoryPath == previous) ?? items.FirstOrDefault();
        RefreshSelection();
        EmptyText.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Text = $"{items.Length} 份 · {items.Sum(b => b.SizeBytes ?? 0) / 1024.0 / 1024:0.00} MiB" +
            (items.Any(b => b.SizeBytes is null) ? "（部分大小未知）" : "");
    }

    private void Backup_Changed(object sender, SelectionChangedEventArgs e)
        => RefreshSelection();

    private void RefreshSelection()
    {
        var entry = BackupSelector.SelectedItem as PetBackupEntry;
        RestoreButton.IsEnabled = entry?.CanRestore == true;
        ExportButton.IsEnabled = entry?.CanRestore == true;
        CopyButton.IsEnabled = entry?.CanRestore == true;
        PurgeButton.IsEnabled = entry?.CanPurge == true;
        BackupPreview.Source = entry?.Thumbnail;
        if (entry?.CanRestore == true)
        {
            try { BackupPreview.Source = PetPackage.Load(Path.Combine(entry.DirectoryPath, "pet.json")).Preview; }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }
        SelectedName.Text = entry?.Name ?? string.Empty;
        SelectedDetails.Text = entry is null ? string.Empty : $"{entry.Id}\n{entry.Kind} · {entry.SizeText}\n\n{entry.DirectoryPath}\n\n{entry.Error}";
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshBackups();

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupSelector.SelectedItem is not PetBackupEntry entry) return;
        var installed = _catalog.Scan().Pets.FirstOrDefault(p => p.Id == entry.Id);
        if (installed?.Bundled == true) { StatusText.Text = "同 id 的随附皮肤不可覆盖，请选择恢复为副本。"; return; }
        var replacing = installed is not null || Directory.Exists(Path.Combine(_catalog.UserDirectory, entry.Id));
        var message = !replacing ? $"恢复“{entry.Name}”到皮肤库？" : $"用此备份替换“{installed?.Name ?? entry.Id}”现有目录？当前目录会先备份。";
        if (AppDialog.Show(this, message + "\n操作立即生效，选中的备份仍会保留。", "恢复皮肤",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var restored = _catalog.RestoreBackup(entry, replacing);
            RestoredId = restored.Id;
            RefreshBackups();
            StatusText.Text = $"已恢复：{restored.Name}。回到设置并保存后应用到桌面。";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Purge_Click(object sender, RoutedEventArgs e)
    {
        if (BackupSelector.SelectedItem is not PetBackupEntry entry) return;
        if (AppDialog.Show(this, $"永久删除这份“{entry.Name}”{entry.Kind}备份（{entry.SizeText}）？\n此操作不可撤销，不会删除皮肤库中正在使用的副本。",
                "永久清理备份", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        try { _catalog.PurgeBackup(entry); RefreshBackups(); StatusText.Text = "已清理选中的备份。"; }
        catch (Exception ex) { RefreshBackups(); StatusText.Text = ex.Message; }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (BackupSelector.SelectedItem is not PetBackupEntry entry) return;
        if (AppDialog.Show(this, $"将“{entry.Name}”恢复为独立皮肤？不会替换已有皮肤。", "恢复为副本",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var copy = _catalog.RestoreBackupCopy(entry);
            RestoredId = copy.Id;
            RefreshBackups();
            StatusText.Text = $"已恢复副本：{copy.Id}。回到设置并保存后应用。";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (BackupSelector.SelectedItem is not PetBackupEntry entry) return;
        var picker = new SaveFileDialog { Filter = "ZIP 皮肤包 (*.zip)|*.zip", FileName = entry.Id + ".zip", DefaultExt = ".zip", OverwritePrompt = true };
        if (picker.ShowDialog(this) != true) return;
        try { _catalog.ExportBackup(entry, picker.FileName, true); StatusText.Text = "已导出：" + picker.FileName; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
