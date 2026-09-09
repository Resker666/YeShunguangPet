using System;
using System.Windows;

namespace YeShunguangPet;

public partial class PetAboutWindow : ThemedWindow
{
    public PetAboutWindow(PetPackage pet)
    {
        InitializeComponent();
        CharacterImage.Source = pet.Preview;
        CharacterName.Text = pet.Manifest.Name;
        CharacterDescription.Text = pet.Manifest.Description;
        CharacterDetails.Text = $"{pet.Manifest.CellWidth} × {pet.Manifest.CellHeight} · {pet.Manifest.Animations.Count} 个动作\n桌面宠物 v{typeof(PetPackage).Assembly.GetName().Version?.ToString(3)}";
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        MaxHeight = Math.Min(MaxHeight, SystemParameters.WorkArea.Height);
        Loaded += (_, _) => NativeMethods.EnsureWindowInWorkArea(this);
    }
}
