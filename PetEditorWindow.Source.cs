using System;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class PetEditorWindow
{
    private PetSourceWatcher? _sourceWatcher;
    private PetSourceSnapshot? _pendingSource;
    private string? _ignoredSource;
    private bool _sourceConflict;
    private long _pendingGeneration;

    private void StartSourceWatcher(bool manual = false)
    {
        if (_closed || !IsLoaded || (!manual && AutoReloadCheck.IsChecked != true) || _sourceWatcher is not null) return;
        try
        {
            var watcher = new PetSourceWatcher(_entry.ManifestPath, _entry.Id);
            _sourceWatcher = watcher;
            var owner = new WeakReference<PetEditorWindow>(this);
            var dispatcher = Dispatcher;
            watcher.Changed += update =>
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (owner.TryGetTarget(out var window) && !window._closed && ReferenceEquals(window._sourceWatcher, watcher) && watcher.IsCurrent(update.Generation))
                    {
                        window.ApplySourceUpdate(update);
                        if (window.AutoReloadCheck.IsChecked != true) window.StopSourceWatcher();
                    }
                }));
            };
            SourceStatus.Text = "正在检查源文件";
            watcher.RequestReload();
        }
        catch (Exception ex) { SourceStatus.Text = "无法监听源文件：" + ex.Message; StopSourceWatcher(); }
    }
    private void StopSourceWatcher()
    {
        _sourceWatcher?.Dispose(); _sourceWatcher = null;
    }
    private void AutoReload_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        if (AutoReloadCheck.IsChecked == true) StartSourceWatcher();
        else { StopSourceWatcher(); SourceStatus.Text = "自动刷新已关闭"; }
    }
    private void ReloadSource_Click(object sender, RoutedEventArgs e)
    {
        _ignoredSource = null;
        StartSourceWatcher(manual: true); _sourceWatcher?.RequestReload();
    }
    private void OpenSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.GetDirectoryName(_entry.ManifestPath)!) { UseShellExecute = true }); }
        catch (Exception ex) { SourceStatus.Text = "无法打开源文件夹：" + ex.Message; }
    }
    private void ApplySourceUpdate(PetSourceUpdate update)
    {
        if (update.Snapshot is not { } snapshot)
        {
            _sourceConflict = true;
            _pendingSource = null; SourceConflictPanel.Visibility = Visibility.Collapsed;
            UpdateButton.IsEnabled = false;
            SourceStatus.Text = "源文件暂不可用，保留当前预览：" + update.Error;
            return;
        }
        if (snapshot.Fingerprint == _document.SourceFingerprint)
        {
            _sourceConflict = false; _pendingSource = null; SourceConflictPanel.Visibility = Visibility.Collapsed;
            SourceStatus.Text = "源文件已同步";
            UpdateButton.IsEnabled = CopyButton.IsEnabled && !_entry.Bundled && _entry.Id == _document.Draft.Id;
            return;
        }
        _sourceConflict = true;
        UpdateButton.IsEnabled = false;
        if (snapshot.Fingerprint == _ignoredSource) return;
        if (_dirtyInputs)
        {
            _pendingSource = snapshot;
            _pendingGeneration = update.Generation;
            SourceConflictPanel.Visibility = Visibility.Visible;
            SourceStatus.Text = "源文件已更新，未覆盖当前草稿";
            return;
        }
        LoadSourceSnapshot(snapshot);
    }
    private void LoadSourceSnapshot(PetSourceSnapshot snapshot)
    {
        var copyId = IdInput.Text;
        _document.Reload(snapshot);
        if (_entry.Bundled) _document.Draft.Id = copyId;
        _document.MarkClean();
        _sourceConflict = false; _ignoredSource = null; _pendingSource = null;
        SourceConflictPanel.Visibility = Visibility.Collapsed;
        SheetView.Sheet = _document.SpriteSheet;
        ImageDetails.Text = $"{_document.SpriteSheet.PixelWidth} × {_document.SpriteSheet.PixelHeight}";
        LoadDraft();
        ValidateAndPreview(preserveFrame: true);
        _dirtyInputs = false;
        _history.Reset(CaptureEditorState());
        RefreshHistoryButtons();
        SourceStatus.Text = "源文件已同步";
    }
    private void LoadExternal_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingSource is not { } snapshot) return;
        if (_sourceWatcher is not null && !_sourceWatcher.IsCurrent(_pendingGeneration))
        {
            SourceStatus.Text = "源文件仍在更新，正在重新检查";
            _sourceWatcher.RequestReload(); return;
        }
        LoadSourceSnapshot(snapshot);
    }
    private void KeepDraft_Click(object sender, RoutedEventArgs e)
    {
        _ignoredSource = _pendingSource?.Fingerprint; _pendingSource = null;
        SourceConflictPanel.Visibility = Visibility.Collapsed;
        SourceStatus.Text = "已保留草稿；源文件有更新，当前草稿可另存或导出";
    }
}
