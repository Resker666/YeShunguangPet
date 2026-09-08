using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace YeShunguangPet;

public partial class PetEditorWindow : ThemedWindow
{
    private readonly PetCatalog _catalog;
    private readonly PetEntry _entry;
    private readonly PetEditorDocument _document;
    private readonly DispatcherTimer _timer = new();
    private readonly ObservableCollection<DurationRow> _durations = new();
    private readonly Dictionary<int, string> _durationHistory = new();
    private readonly Dictionary<PetState, AnimationDefinition> _disabledActions = new();
    private FrameLocation[] _lookHistory = Array.Empty<FrameLocation>();
    private bool _loading = true;
    private bool _dirtyInputs;
    private bool _playing = true;
    private PetState _selectedState = PetState.Idle;
    private int _direction;
    private PetPackage? _preview;
    private PetAnimation? _animation;
    private int _frame;
    public PetEntry? SavedEntry { get; private set; }

    public PetEditorWindow(PetCatalog catalog, PetEntry entry)
    {
        _catalog = catalog;
        _entry = entry;
        _document = new PetEditorDocument(entry.ManifestPath);
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        ActionSelector.ItemsSource = Enum.GetValues<PetState>().Select(s => new ActionItem(s, SettingsWindow.ActionName(s))).ToArray();
        DurationGrid.ItemsSource = _durations;
        DirectionSelector.ItemsSource = Enumerable.Range(0, 16).Select(i => $"{i}: {i * 22.5:0.#}°").ToArray();
        SheetView.Sheet = _document.SpriteSheet;
        SheetView.CellSelected += SelectCell;
        ImageDetails.Text = $"{_document.SpriteSheet.PixelWidth} × {_document.SpriteSheet.PixelHeight}";
        _timer.Tick += Preview_Tick;
        Loaded += (_, _) => { FitSheet(); if (_playing && _preview is not null) _timer.Start(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) _timer.Stop(); else ValidateAndPreview(); };
        Closing += ConfirmClose;
        Closed += (_, _) => _timer.Stop();
        LoadDraft();
        if (entry.Bundled)
        {
            _loading = true;
            IdInput.Text = PetEditorDocument.AvailableId(catalog, entry.Id);
            _loading = false;
        }
        _loading = false;
        if (!UiTheme.MotionEnabled) _playing = false;
        ValidateAndPreview();
        _dirtyInputs = false;
    }

    private void LoadDraft()
    {
        _loading = true;
        var m = _document.Draft;
        _disabledActions.Clear();
        _lookHistory = m.LookDirections.ToArray();
        NameInput.Text = m.Name;
        IdInput.Text = m.Id;
        DescriptionInput.Text = m.Description;
        CellWidthInput.Text = m.CellWidth.ToString();
        CellHeightInput.Text = m.CellHeight.ToString();
        ColumnsInput.Text = m.Columns.ToString();
        RowsInput.Text = m.Rows.ToString();
        ActionSelector.SelectedIndex = (int)_selectedState;
        LookEnabledCheck.IsChecked = m.LookDirections.Count == 16;
        DirectionSelector.SelectedIndex = _direction;
        LoadAction();
        LoadDirection();
        _loading = false;
    }

    internal override void OnThemeUpdated() => SheetView?.InvalidateVisual();

    private void LoadAction()
    {
        var definition = _document.Draft.Animations.GetValueOrDefault(_selectedState);
        EnabledCheck.IsChecked = definition is not null;
        EnabledCheck.IsEnabled = _selectedState != PetState.Idle;
        LoopCheck.IsChecked = definition?.Loop ?? false;
        LoopCheck.IsEnabled = definition is not null && _selectedState is not (PetState.Idle or PetState.Waving or PetState.Jumping or PetState.Failed);
        ActionRowInput.Text = (definition?.Row ?? 0).ToString();
        ActionColumnInput.Text = (definition?.StartColumn ?? 0).ToString();
        FrameCountInput.Text = (definition?.DurationsMs.Length ?? 1).ToString();
        _durations.Clear();
        _durationHistory.Clear();
        foreach (var duration in definition?.DurationsMs ?? new[] { 150 }) AddDuration(duration.ToString());
    }

    private void AddDuration(string text)
    {
        var row = new DurationRow(_durations.Count, text);
        _durationHistory[row.Index] = text;
        row.PropertyChanged += (_, _) => { if (!_loading) { _durationHistory[row.Index] = row.Text; _dirtyInputs = true; ValidateAndPreview(); } };
        _durations.Add(row);
    }

    private void LoadDirection()
    {
        var location = _document.Draft.LookDirections.ElementAtOrDefault(_direction) ?? new FrameLocation(0, 0);
        LookRowInput.Text = location.Row.ToString();
        LookColumnInput.Text = location.Column.ToString();
    }

    private void Draft_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _dirtyInputs = true;
        ValidateAndPreview();
    }

    private bool CollectDraft()
    {
        var m = _document.Draft;
        m.Name = NameInput.Text;
        m.Id = IdInput.Text;
        m.Description = DescriptionInput.Text;
        m.CellWidth = Number(CellWidthInput.Text, "格宽", 16, 512);
        m.CellHeight = Number(CellHeightInput.Text, "格高", 16, 512);
        m.Columns = Number(ColumnsInput.Text, "列数", 1, 64);
        m.Rows = Number(RowsInput.Text, "行数", 1, 64);
        if (EnabledCheck.IsChecked == true)
        {
            var count = Number(FrameCountInput.Text, "帧数", 1, 64);
            while (_durations.Count > count) _durations.RemoveAt(_durations.Count - 1);
            while (_durations.Count < count) AddDuration(_durationHistory.GetValueOrDefault(_durations.Count) ?? _durations.LastOrDefault()?.Text ?? "150");
            m.Animations[_selectedState] = new AnimationDefinition
            {
                Row = Number(ActionRowInput.Text, "动作行", 0, 63),
                StartColumn = Number(ActionColumnInput.Text, "动作列", 0, 63),
                Loop = LoopCheck.IsChecked == true,
                DurationsMs = _durations.Select(r => Number(r.Text, $"帧 {r.Index} 时长", 20, 10000)).ToArray()
            };
        }
        else m.Animations.Remove(_selectedState);
        if (LookEnabledCheck.IsChecked == true)
        {
            if (m.LookDirections.Count == 0) m.LookDirections = Enumerable.Repeat(new FrameLocation(0, 0), 16).ToList();
            m.LookDirections[_direction] = new FrameLocation(Number(LookRowInput.Text, "注视行", 0, 63), Number(LookColumnInput.Text, "注视列", 0, 63));
        }
        else m.LookDirections.Clear();
        return true;
    }

    private static int Number(string text, string name, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
            throw new InvalidDataException($"{name}需为 {min}–{max} 的整数。");
        return value;
    }

    private bool ValidateAndPreview()
    {
        if (_loading) return false;
        _timer.Stop();
        try
        {
            CollectDraft();
            RefreshGrid();
            _preview = _document.Preview();
            _animation = _preview.GetAnimation(_selectedState);
            _frame = 0;
            RenderFrame();
            StatusText.Text = string.Empty;
            CopyButton.IsEnabled = ExportButton.IsEnabled = true;
            UpdateButton.IsEnabled = !_entry.Bundled && _entry.Id == _document.Draft.Id;
            PlayButton.IsEnabled = ModeTabs.SelectedIndex == 0 && _preview.Supports(_selectedState);
            if (_playing && PlayButton.IsEnabled && IsLoaded && WindowState != WindowState.Minimized) _timer.Start();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _preview = null;
            AnimationPreview.Source = null;
            FrameStatus.Text = "配置无效";
            StatusText.Text = ex.Message;
            CopyButton.IsEnabled = ExportButton.IsEnabled = UpdateButton.IsEnabled = false;
            PlayButton.IsEnabled = false;
            return false;
        }
    }

    private void RefreshGrid()
    {
        var m = _document.Draft;
        SheetView.CellWidth = m.CellWidth;
        SheetView.CellHeight = m.CellHeight;
        SheetView.Columns = m.Columns;
        SheetView.Rows = m.Rows;
        if (ModeTabs.SelectedIndex == 1)
        {
            var look = m.LookDirections.ElementAtOrDefault(_direction);
            SheetView.SelectedRow = look?.Row ?? 0;
            SheetView.SelectedColumn = look?.Column ?? 0;
            SheetView.SelectedCount = look is null ? 0 : 1;
        }
        else
        {
            var action = m.Animations.GetValueOrDefault(_selectedState);
            SheetView.SelectedRow = action?.Row ?? 0;
            SheetView.SelectedColumn = action?.StartColumn ?? 0;
            SheetView.SelectedCount = action?.DurationsMs.Length ?? 0;
        }
        SheetView.Refresh(ZoomSlider.Value / 100);
        ZoomText.Text = $"{ZoomSlider.Value:0}%";
    }

    private void RenderFrame()
    {
        if (_preview is null || _animation is null) return;
        if (ModeTabs.SelectedIndex == 1)
        {
            var look = _preview.Manifest.LookDirections.ElementAtOrDefault(_direction);
            AnimationPreview.Source = look is null ? null : _preview.GetFrame(look.Row, look.Column);
            FrameStatus.Text = look is null ? "未启用" : $"方向 {_direction}";
        }
        else if (!_preview.Supports(_selectedState))
        {
            AnimationPreview.Source = _preview.Preview;
            FrameStatus.Text = "未启用";
        }
        else
        {
            AnimationPreview.Source = _preview.GetFrame(_animation.Row, _animation.StartColumn + _frame);
            FrameStatus.Text = $"帧 {_frame + 1}/{_animation.FrameCount} · {_animation.DurationsMs[_frame]} ms";
            _timer.Interval = TimeSpan.FromMilliseconds(_animation.DurationsMs[_frame]);
        }
    }

    private void Preview_Tick(object? sender, EventArgs e)
    {
        if (_preview is null || _animation is null) return;
        if (!_animation.Loop && _frame == _animation.FrameCount - 1)
        {
            _timer.Stop();
            _playing = false;
            PlayButton.Content = "\uE768";
            return;
        }
        _frame = (_frame + 1) % _animation.FrameCount;
        RenderFrame();
    }

    private void Action_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ActionSelector.SelectedItem is not ActionItem item) return;
        try { CollectDraft(); }
        catch (InvalidDataException ex)
        {
            _loading = true;
            ActionSelector.SelectedIndex = (int)_selectedState;
            _loading = false;
            StatusText.Text = ex.Message;
            return;
        }
        _loading = true;
        _selectedState = item.State;
        LoadAction();
        _loading = false;
        ValidateAndPreview();
    }

    private void Enabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _loading = true;
        if (EnabledCheck.IsChecked == true)
            _document.Draft.Animations.TryAdd(_selectedState, _disabledActions.GetValueOrDefault(_selectedState) ??
                new AnimationDefinition { DurationsMs = new[] { 150 }, Loop = _selectedState is not (PetState.Waving or PetState.Jumping or PetState.Failed) });
        else if (_document.Draft.Animations.Remove(_selectedState, out var previous)) _disabledActions[_selectedState] = previous;
        LoadAction();
        _loading = false;
        _dirtyInputs = true;
        ValidateAndPreview();
    }

    private void Direction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DirectionSelector.SelectedIndex < 0) return;
        try { CollectDraft(); }
        catch (InvalidDataException ex)
        {
            _loading = true;
            DirectionSelector.SelectedIndex = _direction;
            _loading = false;
            StatusText.Text = ex.Message;
            return;
        }
        _loading = true;
        _direction = DirectionSelector.SelectedIndex;
        LoadDirection();
        _loading = false;
        ValidateAndPreview();
    }

    private void LookEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (LookEnabledCheck.IsChecked == false) _lookHistory = _document.Draft.LookDirections.ToArray();
        else if (_lookHistory.Length == 16 && _document.Draft.LookDirections.Count == 0) _document.Draft.LookDirections = _lookHistory.ToList();
        _dirtyInputs = true;
        ValidateAndPreview();
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ReferenceEquals(e.Source, ModeTabs)) ValidateAndPreview();
    }

    private void SelectCell(int row, int column)
    {
        _loading = true;
        if (ModeTabs.SelectedIndex == 1) { LookRowInput.Text = row.ToString(); LookColumnInput.Text = column.ToString(); }
        else { ActionRowInput.Text = row.ToString(); ActionColumnInput.Text = column.ToString(); }
        _loading = false;
        _dirtyInputs = true;
        ValidateAndPreview();
    }

    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) RefreshGrid(); }
    private void Fit_Click(object sender, RoutedEventArgs e) => FitSheet();
    private void FitSheet() => ZoomSlider.Value = Math.Clamp(100 * Math.Min((SheetScroll.ViewportWidth - 28) / _document.SpriteSheet.PixelWidth,
        (SheetScroll.ViewportHeight - 28) / _document.SpriteSheet.PixelHeight), 5, 200);
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        _playing = !_playing;
        PlayButton.Content = _playing ? "\uE769" : "\uE768";
        if (!_playing) _timer.Stop();
        else if (_preview is not null && ModeTabs.SelectedIndex == 0)
        {
            if (_animation is { Loop: false } && _frame == _animation.FrameCount - 1) { _frame = 0; RenderFrame(); }
            _timer.Start();
        }
    }
    private void Replay_Click(object sender, RoutedEventArgs e) { _playing = true; PlayButton.Content = "\uE769"; ValidateAndPreview(); }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (AppDialog.Show(this, "恢复打开时的全部配置？当前修改将丢弃。", "重置配置", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _document.Reset();
        LoadDraft();
        _dirtyInputs = false;
        ValidateAndPreview();
    }
    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateAndPreview()) return;
        if (AppDialog.Show(this, "更新此导入皮肤？原版本会保留为备份，保存到皮肤库后无法通过设置的取消按钮撤销。", "保存更新", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        Save(() => _document.SaveUpdate(_catalog, _entry));
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (ValidateAndPreview()) Save(() => _document.SaveCopy(_catalog));
    }
    private void Save(Func<PetEntry> action)
    {
        try { SavedEntry = action(); _dirtyInputs = false; DialogResult = true; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateAndPreview()) return;
        var picker = new SaveFileDialog { Filter = "ZIP 皮肤包 (*.zip)|*.zip", FileName = _document.Draft.Id + ".zip", DefaultExt = ".zip", OverwritePrompt = true };
        if (picker.ShowDialog(this) != true) return;
        try { _document.Export(picker.FileName, true); StatusText.Text = "已导出：" + picker.FileName; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ConfirmClose(object? sender, CancelEventArgs e)
    {
        if (SavedEntry is null && _dirtyInputs && AppDialog.Show(this, "配置尚未保存到皮肤库，关闭并放弃修改？", "关闭编辑器",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) e.Cancel = true;
    }

    private sealed record ActionItem(PetState State, string Name);
    public sealed class DurationRow : INotifyPropertyChanged
    {
        public int Index { get; }
        private string _text;
        public string Text { get => _text; set { _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public DurationRow(int index, string text) { Index = index; _text = text; }
    }
}
