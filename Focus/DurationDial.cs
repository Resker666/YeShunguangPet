using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace YeShunguangPet;

public sealed class DurationDial : RangeBase
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(nameof(Ratio), typeof(double), typeof(DurationDial),
        new PropertyMetadata(0d, (d, _) => ((DurationDial)d).UpdateRatio()));
    private static readonly DependencyProperty DisplayRatioProperty = DependencyProperty.Register("DisplayRatio", typeof(double), typeof(DurationDial),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private double _dragAngle;
    private double _previousAngle;
    private double _originalValue;
    public bool IsDragging { get; private set; }
    public double Ratio { get => (double)GetValue(RatioProperty); set => SetValue(RatioProperty, value); }
    public event Action<bool>? CommitRequested;

    public DurationDial()
    {
        Focusable = true;
        SmallChange = 1;
        LargeChange = 5;
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CancelDrag(); InvalidateVisual(); };
        Unloaded += (_, _) => CancelDrag();
    }

    public void RefreshAppearance() { UpdateRatio(); InvalidateVisual(); }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        UIElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(System.Windows.Automation.RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);
    }

    private void UpdateRatio()
    {
        var target = double.IsFinite(Ratio) ? Math.Clamp(Ratio, 0, 1) : 0;
        var current = (double)GetValue(DisplayRatioProperty);
        BeginAnimation(DisplayRatioProperty, null);
        SetValue(DisplayRatioProperty, target);
        if (IsLoaded && !IsDragging && UiTheme.MotionEnabled && Math.Abs(current - target) > 0.00001)
            BeginAnimation(DisplayRatioProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(IsEnabled ? 160 : 240))
            { FillBehavior = FillBehavior.Stop, EasingFunction = IsEnabled ? new CubicEase { EasingMode = EasingMode.EaseOut } : null });
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 14);
        if (radius < 20) return;
        var track = (Brush)FindResource("BorderBrush");
        var ink = (Brush)FindResource("SecondaryTextBrush");
        var surface = (Brush)FindResource("SurfaceBrush");
        var ratio = (double)GetValue(DisplayRatioProperty);
        dc.DrawEllipse(null, new Pen(track, 4), center, radius, radius);
        for (var i = 0; i < 60; i++)
        {
            var angle = i * Math.PI / 30;
            dc.DrawLine(new Pen(ink, i % 5 == 0 ? 1 : 0.5), At(center, radius - (i % 5 == 0 ? 18 : 14), angle), At(center, radius - 11, angle));
        }
        var pen = new Pen(Foreground, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (ratio >= 1) dc.DrawEllipse(null, pen, center, radius, radius);
        else if (ratio > 0)
        {
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(At(center, radius, 0), false, false);
                g.ArcTo(At(center, radius, ratio * Math.PI * 2), new Size(radius, radius), 0, ratio > 0.5, SweepDirection.Clockwise, true, false);
            }
            arc.Freeze();
            dc.DrawGeometry(null, pen, arc);
        }
        if (IsEnabled) dc.DrawEllipse(Foreground, new Pen(surface, 3), At(center, radius, ratio * Math.PI * 2), 7, 7);
        if (IsKeyboardFocused) dc.DrawEllipse(null, new Pen(Foreground, 1) { DashStyle = DashStyles.Dot }, center, radius + 10, radius + 10);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
    {
        var distance = (parameters.HitPoint - new Point(ActualWidth / 2, ActualHeight / 2)).Length;
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - 14;
        return Math.Abs(distance - radius) <= 19 ? new PointHitTestResult(this, parameters.HitPoint) : null;
    }

    private static Point At(Point center, double radius, double angle) => new(center.X + Math.Sin(angle) * radius, center.Y - Math.Cos(angle) * radius);
    private double Angle(Point point)
    {
        var angle = Math.Atan2(point.X - ActualWidth / 2, ActualHeight / 2 - point.Y) * 180 / Math.PI;
        return angle < 0 ? angle + 360 : angle;
    }

    private void BeginDrag(Point point)
    {
        _originalValue = Value;
        _previousAngle = Angle(point);
        _dragAngle = _previousAngle;
        var currentAngle = Value / Maximum * 360;
        var thumbDistance = Math.Abs((_previousAngle - currentAngle + 540) % 360 - 180);
        if (thumbDistance <= 10) _dragAngle = currentAngle;
        if (_dragAngle < 0.001 && Value > (Maximum + Minimum) / 2) _dragAngle = 360;
        IsDragging = true;
        UpdateRatio();
        UpdateDragValue();
    }

    private void MoveDrag(Point point)
    {
        var angle = Angle(point);
        var delta = angle - _previousAngle;
        if (delta > 180) delta -= 360;
        if (delta < -180) delta += 360;
        // Accumulate relative movement so crossing twelve o'clock clamps instead of wrapping.
        _dragAngle = Math.Clamp(_dragAngle + delta, 0, 360);
        _previousAngle = angle;
        UpdateDragValue();
    }

    private void UpdateDragValue() => Value = Math.Clamp(Math.Round(_dragAngle / 360 * Maximum), Minimum, Maximum);

    public void CancelDrag()
    {
        if (!IsDragging) return;
        IsDragging = false;
        Value = _originalValue;
        ReleaseMouseCapture();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        Focus();
        if (CaptureMouse()) BeginDrag(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsDragging) MoveDrag(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsDragging) return;
        IsDragging = false;
        ReleaseMouseCapture();
        CommitRequested?.Invoke(true);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); CancelDrag(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled) return;
        var value = e.Key switch
        {
            Key.Up or Key.Right => Value + 1,
            Key.Down or Key.Left => Value - 1,
            Key.PageUp => Value + 5,
            Key.PageDown => Value - 5,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => double.NaN
        };
        if (e.Key == Key.Escape && IsDragging) { CancelDrag(); e.Handled = true; }
        if (IsDragging || double.IsNaN(value)) return;
        Value = Math.Clamp(value, Minimum, Maximum);
        CommitRequested?.Invoke(false);
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEnabled || !IsKeyboardFocused || IsDragging) return;
        Value = Math.Clamp(Value + Math.Sign(e.Delta), Minimum, Maximum);
        CommitRequested?.Invoke(false);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DialPeer(this);
    private sealed class DialPeer(DurationDial owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => nameof(DurationDial);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => owner.LargeChange;
        public double SmallChange => owner.SmallChange;
        public double Maximum => owner.Maximum;
        public double Minimum => owner.Minimum;
        public double Value => owner.Value;
        public void SetValue(double value)
        {
            if (IsReadOnly) throw new System.Windows.Automation.ElementNotEnabledException();
            if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
            owner.Value = Math.Round(value);
            owner.CommitRequested?.Invoke(false);
        }
    }
}
