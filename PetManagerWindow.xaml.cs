using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

public partial class PetManagerWindow : Window
{
    private readonly DesktopSession _desktop;
    private bool _refreshing;

    public PetManagerWindow(DesktopSession desktop)
    {
        _desktop = desktop;
        InitializeComponent();
        _desktop.Changed += RefreshInstances;
        Closed += (_, _) => _desktop.Changed -= RefreshInstances;
        RefreshSkins();
        RefreshInstances();
    }

    private void RefreshSkins()
    {
        var selected = (SkinChoice.SelectedItem as PetEntry)?.Id;
        var scan = _desktop.Catalog.Scan();
        SkinChoice.ItemsSource = scan.Pets;
        SkinChoice.SelectedItem = scan.Pets.FirstOrDefault(p => p.Id == selected) ?? scan.Pets.FirstOrDefault();
        StatusText.Text = string.Join(Environment.NewLine, scan.Errors);
        RefreshInstances();
    }

    private void RefreshInstances()
    {
        _refreshing = true;
        var selected = (Instances.SelectedItem as InstanceRow)?.InstanceId;
        var rows = _desktop.Windows.Select(w => new InstanceRow(w.InstanceId, $"{_desktop.Number(w)} · {w.Package.Manifest.Name}",
            _desktop.IsHidden(w) ? "已隐藏" : w.IsDocked ? "已收纳" : "显示中", $"{w.PetScale:P0}", w.Package.Preview, w)).ToArray();
        Instances.ItemsSource = rows;
        Instances.SelectedItem = rows.FirstOrDefault(r => r.InstanceId == selected) ?? rows.FirstOrDefault();
        CountText.Text = $"桌面角色  {rows.Length}/{DesktopConfiguration.MaximumPets}";
        AddButton.IsEnabled = rows.Length < DesktopConfiguration.MaximumPets && SkinChoice.Items.Count > 0;
        QuietCheck.IsChecked = _desktop.Companion.Settings.DoNotDisturb;
        _refreshing = false;
        UpdateButtons();
    }

    private void Instance_Changed(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void UpdateButtons()
    {
        var row = Instances.SelectedItem as InstanceRow;
        RemoveButton.IsEnabled = SettingsButton.IsEnabled = RecallButton.IsEnabled = HideButton.IsEnabled = row is not null;
        HideButton.Content = row is not null && _desktop.IsHidden(row.Window) ? "显示" : "隐藏";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (SkinChoice.SelectedItem is PetEntry entry) Run(() => _desktop.Add(entry.Id));
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Instances.SelectedItem is not InstanceRow row) return;
        if (MessageBox.Show(this, $"关闭“{row.Name}”？皮肤文件和学习计时都会保留。", "关闭角色", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        Run(() => _desktop.Remove(row.Window));
    }
    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (Instances.SelectedItem is not InstanceRow row) return;
        Run(() => { if (_desktop.IsHidden(row.Window)) row.Window.ShowAndActivate(); else row.Window.HideInstance(); });
    }
    private void Settings_Click(object sender, RoutedEventArgs e) { if (Instances.SelectedItem is InstanceRow row) Run(row.Window.ConfigureInstance); }
    private void Recall_Click(object sender, RoutedEventArgs e) { if (Instances.SelectedItem is InstanceRow row) Run(row.Window.RecallToPrimaryScreen); }
    private void RecallAll_Click(object sender, RoutedEventArgs e) => Run(_desktop.RecallAll);
    private void Focus_Click(object sender, RoutedEventArgs e) => Run(_desktop.OpenFocus);
    private void Refresh_Click(object sender, RoutedEventArgs e) => Run(RefreshSkins);
    private void Quiet_Changed(object sender, RoutedEventArgs e) { if (!_refreshing) Run(() => _desktop.SetQuiet(QuietCheck.IsChecked == true)); }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private sealed record InstanceRow(string InstanceId, string Name, string Status, string Scale, BitmapSource Preview, MainWindow Window);
}
