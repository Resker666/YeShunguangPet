using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class CapturePinEditToolbarWindow : ThemedWindow
{
    private readonly CapturePinWindow _pin;
    private bool _refreshing;

    public CapturePinEditToolbarWindow(CapturePinWindow pin)
    {
        _pin = pin;
        InitializeComponent();
        Owner = pin;
        RedSwatch.IsChecked = true;
        Loaded += (_, _) => { Refresh(); _pin.PlaceEditToolbar(); };
    }

    internal override void OnThemeUpdated()
    {
        base.OnThemeUpdated();
        if (GlassSurface is null) return;
        var colors = UiTheme.GetColors(); var surface = colors["SurfaceBrush"]; var border = colors["BorderBrush"];
        GlassSurface.Background = new SolidColorBrush(Color.FromArgb(232, surface.R, surface.G, surface.B));
        GlassSurface.BorderBrush = new SolidColorBrush(Color.FromArgb(190, border.R, border.G, border.B));
    }

    public void Refresh()
    {
        var surface = _pin.EditSurface; var document = _pin.EditDocument;
        if (surface is null || document is null) return;
        _refreshing = true;
        foreach (var button in new[] { CropTool, RectangleTool, EllipseTool, ArrowTool, PenTool, TextTool, MosaicTool })
            button.IsChecked = (string)button.Tag == surface.Tool.ToString();
        foreach (var swatch in ToolOptions.Children.OfType<RadioButton>())
            if (swatch.Tag is string value) swatch.IsChecked = (Color)ColorConverter.ConvertFromString(value) == surface.InkColor;
        var color = surface.Tool is CaptureTool.Rectangle or CaptureTool.Ellipse or CaptureTool.Arrow or CaptureTool.Pen or CaptureTool.Text;
        var mosaic = surface.Tool == CaptureTool.Mosaic;
        ToolOptions.Visibility = color || mosaic ? Visibility.Visible : Visibility.Collapsed;
        FontOptions.Visibility = surface.Tool == CaptureTool.Text ? Visibility.Visible : Visibility.Collapsed;
        StrokeOptions.Visibility = color && surface.Tool != CaptureTool.Text ? Visibility.Visible : Visibility.Collapsed;
        MosaicOptions.Visibility = mosaic ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.IsEnabled = document.CanUndo; RedoButton.IsEnabled = document.CanRedo;
        LineWidth.Value = surface.StrokeWidth; TextSize.Value = surface.TextSize; MosaicStrength.Value = surface.MosaicBlockSize;
        if (LabelInput.Text != surface.LabelText) LabelInput.Text = surface.LabelText;
        ErrorText.Text = _pin.EditError; ErrorText.Visibility = string.IsNullOrEmpty(_pin.EditError) ? Visibility.Collapsed : Visibility.Visible;
        _refreshing = false;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing || (sender as RadioButton)?.Tag is not string tag) return;
        _pin.SetEditTool(Enum.Parse<CaptureTool>(tag));
        if (tag == nameof(CaptureTool.Text)) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => LabelInput.Focus()));
    }
    private void Color_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing || (sender as RadioButton)?.Tag is not string color || _pin.EditSurface is not { } surface) return;
        surface.InkColor = (Color)ColorConverter.ConvertFromString(color);
    }
    private void Options_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || _pin.EditSurface is not { } surface || LineWidth is null || TextSize is null || MosaicStrength is null) return;
        surface.StrokeWidth = LineWidth.Value; surface.TextSize = TextSize.Value; surface.MosaicBlockSize = MosaicStrength.Value;
    }
    private void Label_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_refreshing && _pin.EditSurface is { } surface) surface.LabelText = LabelInput.Text;
    }
    private void Undo_Click(object sender, RoutedEventArgs e) => _pin.UndoEdit();
    private void Redo_Click(object sender, RoutedEventArgs e) => _pin.RedoEdit();
    private void Cancel_Click(object sender, RoutedEventArgs e) => _pin.CompleteEdit(false);
    private void Done_Click(object sender, RoutedEventArgs e) => _pin.CompleteEdit(true);
    private void Toolbar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _pin.CompleteEdit(false); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBoxBase || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.Z) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _pin.RedoEdit(); else _pin.UndoEdit(); }
        else if (e.Key == Key.Y) _pin.RedoEdit(); else return;
        e.Handled = true;
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(_pin.PlaceEditToolbar);
    }
}
