using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace YeShunguangPet;

public partial class CaptureEditorWindow : ThemedWindow
{
    public CaptureDocument Document { get; }
    public CaptureSurface Surface { get; }
    private readonly Action<BitmapSource> _copy, _pin;
    private bool _fit = true;
    private double _zoom = 1;
    public CaptureEditorWindow(BitmapSource image, Action<BitmapSource> pin, Action<BitmapSource>? copy = null)
    {
        InitializeComponent();
        _copy = copy ?? Clipboard.SetImage; _pin = pin;
        Document = new CaptureDocument(image); Surface = new CaptureSurface(Document);
        Surface.Error += ShowError; SurfaceHost.Children.Add(Surface); Document.Changed += RefreshDocument;
        ArrowTool.IsChecked = RedSwatch.IsChecked = true;
        Loaded += (_, _) => { Fit(); RefreshDocument(); };
        Closed += (_, _) => { Surface.CancelGesture(); Document.Changed -= RefreshDocument; Surface.Error -= ShowError; SurfaceHost.Children.Clear(); };
        RefreshDocument();
    }
    private void RefreshDocument()
    {
        Surface.RefreshSize(); UndoButton.IsEnabled = Document.CanUndo; RedoButton.IsEnabled = Document.CanRedo;
        SizeText.Text = $"{Document.Crop.Width} x {Document.Crop.Height}"; StatusText.Text = string.Empty;
        if (_fit) Fit();
    }
    private void ShowError(string message) { StatusText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush"); StatusText.Text = message; }
    private bool Execute(Action action, string message = "")
    {
        try { Surface.CancelGesture(); action(); StatusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush"); StatusText.Text = message; return true; }
        catch (Exception ex) { ShowError(ex.Message); return false; }
    }
    public bool CopyImage() => Execute(() => _copy(Document.Flatten()), "已复制");
    public bool SaveImage(string path) => Execute(() => CaptureRaster.SavePng(Document.Flatten(), path), "已保存");
    public bool PinImage() => Execute(() => _pin(Document.Flatten()), "已贴到桌面");
    private void Copy_Click(object sender, RoutedEventArgs e) { if (CopyImage()) Close(); }
    private void Pin_Click(object sender, RoutedEventArgs e) { if (PinImage()) Close(); }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", DefaultExt = ".png", AddExtension = true, FileName = $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        if (dialog.ShowDialog(this) == true) SaveImage(dialog.FileName);
    }
    private void Undo_Click(object sender, RoutedEventArgs e) { Surface.CancelGesture(); Document.Undo(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { Surface.CancelGesture(); Document.Redo(); }
    private void Tool_Changed(object sender, RoutedEventArgs e)
    {
        if (Surface is null || (sender as RadioButton)?.Tag is not string tag) return;
        Surface.CancelGesture(); Surface.Tool = Enum.Parse<CaptureTool>(tag); StatusText.Text = string.Empty;
        LabelInput.IsEnabled = Surface.Tool == CaptureTool.Text; TextSize.IsEnabled = Surface.Tool == CaptureTool.Text;
        LineWidth.IsEnabled = Surface.Tool is CaptureTool.Arrow or CaptureTool.Rectangle or CaptureTool.Pen;
    }
    private void Color_Changed(object sender, RoutedEventArgs e) { if (Surface is not null && (sender as RadioButton)?.Tag is string color) Surface.InkColor = (Color)ColorConverter.ConvertFromString(color); }
    private void Options_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Surface is not null) { Surface.StrokeWidth = LineWidth.Value; Surface.TextSize = TextSize.Value; } }
    private void Label_Changed(object sender, TextChangedEventArgs e) { if (Surface is not null) Surface.LabelText = LabelInput.Text; }
    private void SetZoom(double value)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _zoom = Math.Clamp(value, 0.001, 4); Surface.LayoutTransform = new ScaleTransform(_zoom / dpi.DpiScaleX, _zoom / dpi.DpiScaleY); ZoomText.Text = $"{_zoom:P0}";
    }
    private void Fit()
    {
        if (Surface is null || Viewport.ActualWidth <= 24 || Viewport.ActualHeight <= 24) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        SetZoom(Math.Min(1, Math.Min((Viewport.ActualWidth - 28) * dpi.DpiScaleX / Document.Crop.Width, (Viewport.ActualHeight - 28) * dpi.DpiScaleY / Document.Crop.Height)));
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); if (Surface is not null) { if (_fit) Fit(); else SetZoom(_zoom); } }
    private void Viewport_Changed(object sender, SizeChangedEventArgs e) { if (_fit) Fit(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _fit = false; SetZoom(_zoom / 1.25); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _fit = false; SetZoom(_zoom * 1.25); }
    private void Fit_Click(object sender, RoutedEventArgs e) { _fit = true; Fit(); }
    private void Actual_Click(object sender, RoutedEventArgs e) { _fit = false; SetZoom(1); }
    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        _fit = false; SetZoom(_zoom * (e.Delta > 0 ? 1.25 : 0.8)); e.Handled = true;
    }
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Surface.CancelGesture(); return; }
        if (Keyboard.FocusedElement is TextBoxBase || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.Z) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) Document.Redo(); else Document.Undo(); }
        else if (e.Key == Key.Y) Document.Redo(); else if (e.Key == Key.C) Copy_Click(this, e); else if (e.Key == Key.S) Save_Click(this, e); else return;
        e.Handled = true;
    }
}
