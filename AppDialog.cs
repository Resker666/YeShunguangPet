using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YeShunguangPet;

public static class AppDialog
{
    public static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new ThemedWindow { Owner = owner, Title = title, Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        content.Children.Add(new ScrollViewer { Content = text, MaxHeight = Math.Max(120, SystemParameters.WorkArea.Height - 260), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        Button? cancel = null;
        if (buttons != MessageBoxButton.OK)
        {
            cancel = new Button { Content = buttons == MessageBoxButton.YesNo ? "否" : "取消", IsCancel = true, MinWidth = 76, Margin = new Thickness(0, 0, 8, 0) };
            actions.Children.Add(cancel);
        }
        var accept = new Button { Content = buttons == MessageBoxButton.OK ? "知道了" : buttons == MessageBoxButton.YesNo ? "是" : "确认", MinWidth = 76, IsDefault = true };
        accept.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        if (image == MessageBoxImage.Warning && buttons != MessageBoxButton.OK)
            accept.Background = new SolidColorBrush(Color.FromRgb(178, 38, 62));
        accept.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(accept);
        content.Children.Add(actions);
        dialog.Content = content;
        dialog.Loaded += (_, _) => (cancel ?? accept).Focus();
        return dialog.ShowDialog() == true ? buttons == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK :
            buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
    }
}
