using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace YeShunguangPet;

public static class SpeechPlacement
{
    public static Rect? Place(Rect anchor, Size bubble, Rect work, double gap = 8)
    {
        if (bubble.Width <= 0 || bubble.Height <= 0 || bubble.Width > work.Width || bubble.Height > work.Height) return null;
        var x = Math.Clamp(anchor.Left + (anchor.Width - bubble.Width) / 2, work.Left, work.Right - bubble.Width);
        var y = Math.Clamp(anchor.Top + (anchor.Height - bubble.Height) / 2, work.Top, work.Bottom - bubble.Height);
        var candidates = new[]
        {
            new Rect(x, anchor.Top - bubble.Height - gap, bubble.Width, bubble.Height),
            new Rect(x, anchor.Bottom + gap, bubble.Width, bubble.Height),
            new Rect(anchor.Right + gap, y, bubble.Width, bubble.Height),
            new Rect(anchor.Left - bubble.Width - gap, y, bubble.Width, bubble.Height)
        };
        foreach (var candidate in candidates) if (work.Contains(candidate) && !candidate.IntersectsWith(anchor)) return candidate;
        return null;
    }
}

public sealed class SpeechBubbleWindow : ThemedWindow
{
    private readonly Polygon _topTail, _bottomTail;
    public string Message { get; }
    public SpeechBubbleWindow(MainWindow owner, string text)
    {
        Owner = owner;
        Title = "角色台词";
        Message = text;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = ShowActivated = Focusable = IsHitTestVisible = false;
        Topmost = owner.Topmost;
        Width = 276;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (_, _) => NativeMethods.SetPassiveOverlay(this);
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        _topTail = Tail(new PointCollection { new(0, 6), new(12, 6), new(6, 0) });
        _bottomTail = Tail(new PointCollection { new(0, 0), new(12, 0), new(6, 6) });
        root.Children.Add(_topTail);
        Grid.SetRow(_bottomTail, 2);
        root.Children.Add(_bottomTail);
        var border = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 10),
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.12 } };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var content = new StackPanel();
        var name = new TextBlock { Text = owner.Package.Manifest.Name, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        content.Children.Add(name);
        content.Children.Add(new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        border.Child = content;
        Grid.SetRow(border, 1);
        root.Children.Add(border);
        Content = root;
    }
    private static Polygon Tail(PointCollection points)
    {
        var tail = new Polygon { Points = points, Width = 12, Height = 6, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Hidden };
        tail.SetResourceReference(Shape.FillProperty, "SurfaceBrush");
        return tail;
    }
    public bool ShowAtPet(MainWindow pet)
    {
        if (!NativeMethods.TryGetWindowBounds(pet, out var anchor) || !NativeMethods.TryGetWindowWorkArea(pet, out var nativeWork)) return false;
        var work = new Rect(nativeWork.Left, nativeWork.Top, nativeWork.Right - nativeWork.Left, nativeWork.Bottom - nativeWork.Top);
        new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.MoveWindowPixels(this, (int)anchor.Left, (int)anchor.Top);
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = Math.Min(276, Math.Max(1, (work.Width - 16) / dpi.DpiScaleX));
        Opacity = 0;
        Show();
        UpdateLayout();
        dpi = VisualTreeHelper.GetDpi(this);
        Width = Math.Min(276, Math.Max(1, (work.Width - 16) / dpi.DpiScaleX));
        UpdateLayout();
        if (!NativeMethods.TryGetWindowBounds(this, out var bounds)) return false;
        var target = SpeechPlacement.Place(anchor, bounds.Size, work, 8 * dpi.DpiScaleY);
        if (target is not Rect placement) return false;
        _bottomTail.Visibility = placement.Bottom <= anchor.Top ? Visibility.Visible : Visibility.Hidden;
        _topTail.Visibility = placement.Top >= anchor.Bottom ? Visibility.Visible : Visibility.Hidden;
        var tailLeft = Math.Clamp((anchor.Left + anchor.Width / 2 - placement.Left) / dpi.DpiScaleX - 14, 8, Math.Max(8, Width - 36));
        foreach (var tail in new[] { _topTail, _bottomTail })
        {
            tail.HorizontalAlignment = HorizontalAlignment.Left;
            tail.Margin = new Thickness(tailLeft, 0, 0, 0);
        }
        NativeMethods.MoveWindowPixels(this, (int)Math.Round(placement.Left), (int)Math.Round(placement.Top));
        Opacity = 1;
        UiMotion.Enter((FrameworkElement)Content);
        return true;
    }
}
