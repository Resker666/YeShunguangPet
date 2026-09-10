using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace YeShunguangPet;

public partial class MainWindow
{
    private bool _pointerDown;
    private bool _doublePress;
    private Point _pressScreen;
    private readonly DispatcherTimer _clickTimer = new();

    private void InitializeClickInteraction()
    {
        _clickTimer.Interval = TimeSpan.FromMilliseconds(WinForms.SystemInformation.DoubleClickTime);
        _clickTimer.Tick += (_, _) =>
        {
            ObserveRuntimeTick(RuntimeTick.Click);
            _clickTimer.Stop();
            if (ActivityPlan.Click)
            {
                PlayAnimation(PetState.Waving, restart: true);
                _desktop?.Speak(this, SpeechEvent.Click);
            }
            RefreshActivityTimers();
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (EffectiveClickThrough || e.ChangedButton != MouseButton.Left) return;
        ExpandDock(immediately: true);
        _desktop?.DismissSpeech(this);
        _clickTimer.Stop();
        StopRoaming(returnToIdle: true);
        _pointerDown = true;
        _doublePress = e.ClickCount == 2;
        _pressScreen = CursorScreenPosition();
        CaptureMouse();
        RefreshActivityTimers();
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pointerDown || e.LeftButton != MouseButtonState.Pressed) return;
        var cursor = CursorScreenPosition();
        var dpi = VisualTreeHelper.GetDpi(this);
        if (!DesktopBehavior.ExceedsDragThreshold((cursor.X - _pressScreen.X) / dpi.DpiScaleX,
            (cursor.Y - _pressScreen.Y) / dpi.DpiScaleY,
            SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance)) return;
        _pointerDown = false;
        ReleaseMouseCapture();
        _behavior.BeginDrag();
        LeaveDock();
        ApplyEffectiveWindowOptions();
        _lastDragLeft = Left;
        PlayAnimation(cursor.X >= _pressScreen.X ? PetState.RunningRight : PetState.RunningLeft, restart: true);
        try { DragMove(); }
        catch (InvalidOperationException) { }
        finally { FinishDrag(); }
        e.Handled = true;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown) return;
        _pointerDown = false;
        ReleaseMouseCapture();
        if (_settings.ClickInteraction)
        {
            if (_doublePress)
            {
                PlayAnimation(_pet.Supports(PetState.Jumping) ? PetState.Jumping : PetState.Waving, restart: true);
                _desktop?.Speak(this, SpeechEvent.Click);
            }
            else
                _clickTimer.Start();
        }
        RefreshActivityTimers();
        e.Handled = true;
    }

    private void Window_LostMouseCapture(object sender, MouseEventArgs e) { _pointerDown = false; RefreshActivityTimers(); }

    private void CancelPointerInteraction()
    {
        _clickTimer.Stop();
        _pointerDown = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private bool IsCursorNearPet(double extraRadius = 0)
    {
        var point = PointFromScreen(CursorScreenPosition());
        return DesktopBehavior.IsNear(point.X, point.Y, ActualWidth, ActualHeight, _settings.MousePauseRadius + extraRadius);
    }

    private static Point CursorScreenPosition()
    {
        var cursor = WinForms.Cursor.Position;
        return new Point(cursor.X, cursor.Y);
    }
}
