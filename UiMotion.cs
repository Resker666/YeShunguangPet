using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace YeShunguangPet;

public static class UiMotion
{
    public static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached("Feedback", typeof(bool), typeof(UiMotion), new PropertyMetadata(false, Changed));
    public static void SetFeedback(DependencyObject target, bool value) => target.SetValue(FeedbackProperty, value);
    public static bool GetFeedback(DependencyObject target) => (bool)target.GetValue(FeedbackProperty);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control) return;
        if ((bool)e.NewValue)
        {
            control.MouseEnter += Hover; control.MouseLeave += Hover;
            control.PreviewMouseLeftButtonDown += Press; control.PreviewMouseLeftButtonUp += Press;
            control.PreviewKeyDown += KeyPress; control.PreviewKeyUp += KeyPress;
            control.LostMouseCapture += Release;
            control.Loaded += Loaded;
            if (control is ToggleButton toggle)
            {
                toggle.Checked += Toggle; toggle.Unchecked += Toggle;
                EventHandler? initialize = null;
                initialize = (_, _) =>
                {
                    if (toggle.Template?.FindName("SwitchThumb", toggle) is null) return;
                    MoveToggle(toggle, false);
                    toggle.LayoutUpdated -= initialize;
                };
                toggle.LayoutUpdated += initialize;
                toggle.Unloaded += (_, _) => toggle.LayoutUpdated -= initialize;
            }
        }
        else
        {
            control.MouseEnter -= Hover; control.MouseLeave -= Hover;
            control.PreviewMouseLeftButtonDown -= Press; control.PreviewMouseLeftButtonUp -= Press;
            control.PreviewKeyDown -= KeyPress; control.PreviewKeyUp -= KeyPress;
            control.LostMouseCapture -= Release;
            control.Loaded -= Loaded;
            if (control is ToggleButton toggle) { toggle.Checked -= Toggle; toggle.Unchecked -= Toggle; }
        }
    }
    private static void Hover(object sender, MouseEventArgs e)
    {
        var control = (Control)sender;
        if (control.Template?.FindName("HoverLayer", control) is FrameworkElement layer) Fade(layer, control.IsMouseOver ? 0.065 : 0);
        if (!control.IsMouseOver && control.Template?.FindName("PressLayer", control) is FrameworkElement press) Fade(press, 0);
    }
    private static void Press(object sender, MouseButtonEventArgs e)
    {
        var control = (Control)sender;
        if (control.Template?.FindName("PressLayer", control) is FrameworkElement layer) Fade(layer, e.ButtonState == MouseButtonState.Pressed ? 0.13 : 0);
    }
    private static void KeyPress(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        var control = (Control)sender;
        if (control.Template?.FindName("PressLayer", control) is FrameworkElement layer) Fade(layer, e.IsDown ? 0.13 : 0);
    }
    private static void Release(object sender, MouseEventArgs e)
    {
        var control = (Control)sender;
        if (control.Template?.FindName("PressLayer", control) is FrameworkElement layer) Fade(layer, 0);
    }
    private static void Loaded(object sender, RoutedEventArgs e) { if (sender is ToggleButton toggle) MoveToggle(toggle, false); }
    private static void Toggle(object sender, RoutedEventArgs e) => MoveToggle((ToggleButton)sender, true);
    private static void MoveToggle(ToggleButton toggle, bool animate)
    {
        if (toggle.Template?.FindName("SwitchThumb", toggle) is not FrameworkElement thumb || thumb.RenderTransform is not TranslateTransform transform) return;
        if (transform.IsFrozen) { transform = transform.Clone(); thumb.RenderTransform = transform; }
        var from = transform.X;
        var to = toggle.IsChecked == true ? 14.0 : 0;
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = to;
        if (animate && toggle.IsLoaded && UiTheme.MotionEnabled)
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    private static void Fade(FrameworkElement layer, double opacity)
    {
        var from = layer.Opacity;
        layer.BeginAnimation(UIElement.OpacityProperty, null);
        layer.Opacity = opacity;
        if (layer.IsLoaded && UiTheme.MotionEnabled)
            layer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, opacity, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop });
    }
    public static void Enter(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (!element.IsLoaded || !UiTheme.MotionEnabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
    }
}

public class SmoothScrollViewer : ScrollViewer
{
    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register("AnimatedOffset", typeof(double), typeof(SmoothScrollViewer),
        new PropertyMetadata(0.0, (d, e) => ((SmoothScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
    private double _target;
    private bool _scrolling;
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (CanContentScroll || ScrollableHeight <= 0) { base.OnMouseWheel(e); return; }
        var distance = SystemParameters.WheelScrollLines < 0 ? ViewportHeight : SystemParameters.WheelScrollLines * 20;
        _target = Math.Clamp((_scrolling ? _target : VerticalOffset) - e.Delta / 120.0 * distance, 0, ScrollableHeight);
        var from = VerticalOffset;
        BeginAnimation(AnimatedOffsetProperty, null);
        SetValue(AnimatedOffsetProperty, _target);
        _scrolling = UiTheme.MotionEnabled;
        if (_scrolling)
        {
            var animation = new DoubleAnimation(from, _target, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) => _scrolling = false;
            BeginAnimation(AnimatedOffsetProperty, animation);
        }
        e.Handled = true;
    }
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        StopScrollAnimation();
        base.OnPreviewMouseDown(e);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        StopScrollAnimation();
        base.OnPreviewKeyDown(e);
    }
    public void ResetScroll()
    {
        StopScrollAnimation();
        SetValue(AnimatedOffsetProperty, 0.0);
        _target = 0;
        ScrollToTop();
    }
    private void StopScrollAnimation()
    {
        var offset = VerticalOffset;
        BeginAnimation(AnimatedOffsetProperty, null);
        SetValue(AnimatedOffsetProperty, offset);
        _target = offset;
        _scrolling = false;
    }
}
