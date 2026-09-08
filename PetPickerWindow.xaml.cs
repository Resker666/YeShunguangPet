using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace YeShunguangPet;

public partial class PetPickerWindow : ThemedWindow
{
    public PetEntry? Selected => Choices.SelectedItem as PetEntry;
    public PetPickerWindow(IReadOnlyList<PetEntry> choices)
    {
        InitializeComponent();
        Choices.ItemsSource = choices;
        Choices.SelectedIndex = choices.Count > 0 ? 0 : -1;
        AcceptButton.IsEnabled = choices.Count > 0;
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) { if (AcceptButton is not null) AcceptButton.IsEnabled = Selected is not null; }
    private void Accept_Click(object sender, RoutedEventArgs e) { if (Selected is not null) DialogResult = true; }
}
