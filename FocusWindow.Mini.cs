using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace YeShunguangPet;

public partial class FocusWindow
{
    private void MiniMode_Click(object sender, RoutedEventArgs e) => SetMiniMode(!IsMiniMode);
    private void MiniClose_Click(object sender, RoutedEventArgs e) => Close();

    internal bool SetMiniMode(bool mini)
    {
        if (_layoutClosed || _switchingMode || mini == IsMiniMode) return true;
        if (CanEdit && (!CommitMinutes() || !SaveDuration())) return false;
        Dial.CancelDrag();
        _placementTimer.Stop();
        CaptureNormalPlacement();
        var options = _viewOptions.Copy();
        options.MiniMode = mini;
        if (mini && options.MiniLeftPixels is null)
        {
            options.MiniLeftPixels = options.LeftPixels;
            options.MiniTopPixels = options.TopPixels;
        }
        if (!StoreViewOptions(options)) return false;
        _switchingMode = true;
        try
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            ApplyWindowMode();
            var left = mini ? _viewOptions.MiniLeftPixels : _viewOptions.LeftPixels;
            var top = mini ? _viewOptions.MiniTopPixels : _viewOptions.TopPixels;
            if (_managePlacement && left is int x && top is int y) NativeMethods.MoveWindowPixels(this, x, y);
            FitToScreen();
        }
        finally { _switchingMode = false; }
        RefreshDisplay();
        var surface = mini ? (FrameworkElement)MiniRoot : ContentRoot;
        surface.BeginAnimation(OpacityProperty, null);
        if (IsLoaded && UiTheme.MotionEnabled)
            surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop });
        FocusScroll.ScrollToTop();
        QueueScreenRefresh();
        return true;
    }

    private void ApplyWindowMode()
    {
        var mini = IsMiniMode;
        // Lower the minimum before shrinking; remove the mini maximum before expanding.
        MinWidth = MinHeight = 0;
        MaxWidth = MaxHeight = double.PositiveInfinity;
        if (mini)
        {
            WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(4),
                GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false
            });
        }
        else
        {
            WindowChrome.SetWindowChrome(this, null);
            WindowStyle = WindowStyle.SingleBorderWindow;
        }
        Width = mini ? _viewOptions.MiniWidth : _viewOptions.Width;
        Height = mini ? _viewOptions.MiniHeight : _viewOptions.Height;
        MinWidth = mini ? FocusWindowOptions.MiniMinimumWidth : FocusWindowOptions.MinimumWidth;
        MinHeight = mini ? FocusWindowOptions.MiniMinimumHeight : FocusWindowOptions.MinimumHeight;
        if (mini) { MaxWidth = 480; MaxHeight = 200; }
        Topmost = mini && _viewOptions.MiniTopmost;
        ContentRoot.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
        MiniRoot.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
        MiniPinButton.IsChecked = _viewOptions.MiniTopmost;
        UiTheme.MatchTitleBarBackground(this);
    }

    private void MiniPin_Click(object sender, RoutedEventArgs e)
    {
        var options = _viewOptions.Copy();
        options.MiniTopmost = MiniPinButton.IsChecked == true;
        if (StoreViewOptions(options)) Topmost = IsMiniMode && options.MiniTopmost;
        MiniPinButton.IsChecked = _viewOptions.MiniTopmost;
    }

    private bool StoreViewOptions(FocusWindowOptions options)
    {
        try
        {
            options.Normalize();
            if (_managePlacement) _runtime?.SaveWindowOptions(options);
            _viewOptions = options.Copy();
            _lastNormalPlacement = options.Copy();
            WindowError.Text = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unable to save focus window mode.", ex);
            WindowError.Text = "窗口设置未保存，请检查配置目录权限后重试。";
            RefreshMiniStatus();
            return false;
        }
    }

    private void RefreshMiniStatus()
    {
        MiniTitleText.Text = _session.Phase == SessionPhase.Focus ? "专注" : "休息";
        MiniPhaseText.Text = WindowError.Text.Length > 0 ? "设置未保存" : _session.Status switch
        {
            SessionStatus.Ready => "准备",
            SessionStatus.Running => "进行中",
            SessionStatus.Paused => "已暂停",
            _ => "已完成"
        };
        System.Windows.Automation.AutomationProperties.SetName(MiniPhaseText, WindowError.Text.Length > 0 ? "设置未保存" : PhaseText.Text);
        MiniPhaseText.ToolTip = WindowError.Text.Length > 0 ? WindowError.Text : null;
        MiniProgress.Value = _session.Status == SessionStatus.Ready ? 0 : Math.Clamp(_session.Remaining.TotalSeconds / _session.Duration.TotalSeconds, 0, 1);
    }
}
