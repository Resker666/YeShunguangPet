using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace YeShunguangPet;

public sealed class PetContextMenu : ContextMenu, IDisposable
{
    private bool _listening;
    public PetContextMenu()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/YeShunguangPet;component/Themes/Controls.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/YeShunguangPet;component/Themes/PetMenu.xaml", UriKind.Relative) });
        Resources[typeof(MenuItem)] = Resources["PetMenuItem"];
        Resources[typeof(Separator)] = Resources["PetMenuSeparator"];
        Resources[MenuItem.SeparatorStyleKey] = Resources["PetMenuSeparator"];
        SetResourceReference(StyleProperty, "PetContextMenuStyle");
        ApplyAppearance();
        Opened += (_, _) =>
        {
            if (!IsOpen) return;
            ApplyAppearance();
            if (!_listening) { UiTheme.Changed += ApplyAppearance; _listening = true; }
        };
        Closed += (_, _) => { if (!IsOpen) StopListening(); };
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsOpenProperty && !(bool)e.NewValue) StopListening();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (IsOpen && e.Key == Key.Escape)
        {
            var submenu = Descendants(this).LastOrDefault(item => item.IsSubmenuOpen);
            if (submenu is null) IsOpen = false;
            else { submenu.IsSubmenuOpen = false; submenu.Focus(); }
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void ApplyAppearance()
    {
        UiTheme.SetPalette(Resources);
        Resources["PetMenuAnimation"] = UiTheme.MotionEnabled ? PopupAnimation.Fade : PopupAnimation.None;
        var height = SystemParameters.WorkArea.Height;
        if (PlacementTarget is FrameworkElement target && Window.GetWindow(target) is { } owner && NativeMethods.TryGetWindowWorkArea(owner, out var area))
            height = (area.Bottom - area.Top) / VisualTreeHelper.GetDpi(owner).DpiScaleY;
        Resources["PetMenuMaxHeight"] = Math.Max(80, height - 28);
    }

    public static IEnumerable<MenuItem> Descendants(ItemsControl owner)
    {
        foreach (var value in owner.Items)
            if (value is MenuItem item)
            {
                yield return item;
                foreach (var child in Descendants(item)) yield return child;
            }
    }

    private void StopListening()
    {
        if (!_listening) return;
        UiTheme.Changed -= ApplyAppearance;
        _listening = false;
    }
    public void Dispose() { IsOpen = false; StopListening(); }
}
