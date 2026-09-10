using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace YeShunguangPet;

public partial class CaptureToolbarWindow : ThemedWindow
{
    private readonly CaptureSelection _capture;
    private bool _refreshing;
    internal double CommandHeight => CommandRow.ActualHeight + 20;
    internal double OptionsHeight => ToolOptions.Visibility == Visibility.Visible ? ToolOptions.ActualHeight + 6 : 0;
    public CaptureToolbarWindow(CaptureSelection capture)
    {
        _capture = capture; InitializeComponent(); RedSwatch.IsChecked = true;
        Loaded += (_, _) => _capture.PlaceToolbar(); Refresh();
    }
    public void Refresh()
    {
        _refreshing = true;
        foreach (var button in new[] { SelectTool, RectangleTool, EllipseTool, ArrowTool, PenTool, TextTool, RedactTool })
            button.IsChecked = (string)button.Tag == (_capture.Tool ?? CaptureTool.Crop).ToString();
        var color = _capture.Tool is CaptureTool.Rectangle or CaptureTool.Ellipse or CaptureTool.Arrow or CaptureTool.Pen or CaptureTool.Text;
        ToolOptions.Visibility = color ? Visibility.Visible : Visibility.Collapsed;
        FontOptions.Visibility = _capture.Tool == CaptureTool.Text ? Visibility.Visible : Visibility.Collapsed;
        StrokeOptions.Visibility = color && _capture.Tool != CaptureTool.Text ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.IsEnabled = _capture.Document.CanUndo; RedoButton.IsEnabled = _capture.Document.CanRedo;
        ErrorText.Text = _capture.Error; ErrorText.Visibility = string.IsNullOrEmpty(_capture.Error) ? Visibility.Collapsed : Visibility.Visible;
        LineWidth.Value = _capture.StrokeWidth; TextSize.Value = _capture.TextSize;
        _refreshing = false;
    }
    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing || (sender as RadioButton)?.Tag is not string tag) return;
        var tool = Enum.Parse<CaptureTool>(tag); _capture.SetTool(_capture.Tool == tool || tool == CaptureTool.Crop ? null : tool);
    }
    private void Color_Changed(object sender, RoutedEventArgs e)
    {
        if ((sender as RadioButton)?.Tag is string color) _capture.InkColor = (Color)ColorConverter.ConvertFromString(color);
    }
    private void Size_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || LineWidth is null || TextSize is null) return;
        _capture.StrokeWidth = LineWidth.Value; _capture.TextSize = TextSize.Value;
    }
    private void Undo_Click(object sender, RoutedEventArgs e) => _capture.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _capture.Redo();
    private void Save_Click(object sender, RoutedEventArgs e) => _capture.Complete(CaptureOutput.Save);
    private void Pin_Click(object sender, RoutedEventArgs e) => _capture.Complete(CaptureOutput.Pin);
    private void Done_Click(object sender, RoutedEventArgs e) => _capture.Complete(CaptureOutput.Copy);
    private void Cancel_Click(object sender, RoutedEventArgs e) => _capture.Cancel();
    private void Toolbar_KeyDown(object sender, KeyEventArgs e) => _capture.HandleKey(e);
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); if (_capture is not null) Dispatcher.BeginInvoke(_capture.PlaceToolbar); }
}
