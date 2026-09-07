using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class EdgeDockTests
{
    public static void Run(Action<bool, string> check, PetPackage pet, string? renderRoot)
    {
        var area = new Rect(0, 0, 1920, 1040);
        var positions = new[]
        {
            (DockEdge.Left, new Rect(4, 420, 192, 208)),
            (DockEdge.Right, new Rect(1725, 420, 192, 208)),
            (DockEdge.Top, new Rect(820, 4, 192, 208)),
            (DockEdge.Bottom, new Rect(820, 828, 192, 208))
        };
        foreach (var (edge, rect) in positions)
        {
            check(EdgeDocking.Detect(rect, area) == edge, $"detect {edge} edge");
            var layout = EdgeDocking.Create(edge, rect, area)!;
            check(area.Contains(layout.Expanded) && area.Contains(layout.Handle), $"{edge} docking respects taskbar work area");
            check(layout.RetractionViewport.Contains(rect) &&
                layout.RetractionViewport.TopLeft + layout.RetractionOffset(0) == rect.TopLeft,
                $"{edge} first frame preserves release position without snapping");
            var vertical = edge is DockEdge.Left or DockEdge.Right;
            check(layout.Handle.Size == (vertical ? new Size(12, 56) : new Size(56, 12)), $"{edge} handle dimensions");
            var offset = layout.Offset(1);
            check(edge switch
            {
                DockEdge.Left => offset.X == -192 && offset.Y == 0,
                DockEdge.Right => offset.X == 192 && offset.Y == 0,
                DockEdge.Top => offset.X == 0 && offset.Y == -208,
                _ => offset.X == 0 && offset.Y == 208
            }, $"{edge} slide direction");
            VerifyWindow(check, pet, layout, renderRoot);
        }
        check(EdgeDocking.Detect(new Rect(400, 300, 192, 208), area) == DockEdge.None, "interior drop does not dock");
        check(EdgeDocking.Detect(new Rect(24, 300, 192, 208), area) == DockEdge.Left &&
            EdgeDocking.Detect(new Rect(25, 300, 192, 208), area) == DockEdge.None, "dock threshold uses 24 DIP");
        check(EdgeDocking.Detect(new Rect(0, 0, 192, 208), area) == DockEdge.Left, "corner tie has deterministic edge");
        foreach (var (edge, release) in new[]
        {
            (DockEdge.Left, new Rect(-60, 420, 192, 208)),
            (DockEdge.Right, new Rect(1800, 420, 192, 208)),
            (DockEdge.Top, new Rect(820, -60, 192, 208)),
            (DockEdge.Bottom, new Rect(820, 940, 192, 208))
        })
        {
            check(EdgeDocking.Detect(release, area) == edge, $"{edge} overshoot docks before clamping window");
            var layout = EdgeDocking.Create(edge, release, area)!;
            check(area.Contains(layout.RetractionViewport) &&
                layout.RetractionViewport.TopLeft + layout.RetractionOffset(0) == release.TopLeft,
                $"{edge} overshoot keeps visible pixels at release position");
            var finalFrame = new Rect(layout.RetractionViewport.TopLeft + layout.RetractionOffset(1), release.Size);
            var visible = Rect.Intersect(finalFrame, area);
            check(visible.IsEmpty || visible.Width == 0 || visible.Height == 0, $"{edge} overshoot retracts fully");
        }
        var negative = new Rect(-1920, -1080, 1920, 1040);
        var negativeLayout = EdgeDocking.Create(DockEdge.Bottom, new Rect(-1200, -900, 192, 208), negative)!;
        check(negativeLayout.Handle.Bottom == -40 && negative.Contains(negativeLayout.Expanded), "negative monitor coordinates");
        check(EdgeDocking.Create(DockEdge.Left, new Rect(0, 0, 900, 1200), area) is null, "oversized pet remains undocked");
        var small = EdgeDocking.Create(DockEdge.Top, new Rect(0, 0, 8, 8), area)!;
        check(small.Handle.Width == 8 && small.Handle.Height == 8, "tiny skins retain a bounded handle");
        var narrow = new Rect(40, 0, 900, 650);
        var adjusted = EdgeDocking.Create(DockEdge.Right, new Rect(900, 900, 384, 416), narrow)!;
        check(narrow.Contains(adjusted.Expanded) && narrow.Contains(adjusted.Handle), "resized skin clamps along docked edge");
        var preferences = JsonSerializer.Deserialize<PetSettings>("{\"ClickThrough\":true}")!;
        check(!preferences.EdgeAutoHide, "edge hiding defaults off for existing users");
        preferences.EdgeAutoHide = true;
        check(JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(preferences.Clone()))!.EdgeAutoHide,
            "edge hiding preference persists");

        var clock = new Clock();
        var transition = new DockTransition(true, clock);
        clock.Advance(90);
        transition.Tick(false, true, false);
        check(transition.Progress == 0.5 && transition.TargetCollapsed, "initial drop retracts even while pointer is over pet");
        clock.Advance(90);
        transition.Tick(false, false, false);
        check(transition.IsCollapsed, "collapse completes at 180 milliseconds");
        transition.Tick(true, false, false);
        clock.Advance(180);
        transition.Tick(false, true, false);
        check(transition.IsExpanded && !transition.TargetCollapsed, "hovering handle expands");
        clock.Advance(5000);
        transition.Tick(false, true, false);
        check(transition.IsExpanded && !transition.TargetCollapsed, "hover over pet keeps it expanded");
        transition.Tick(false, false, false);
        clock.Advance(999);
        transition.Tick(false, false, false);
        check(!transition.TargetCollapsed, "leave delay does not retract early");
        clock.Advance(1);
        transition.Tick(false, false, false);
        check(transition.TargetCollapsed, "one second outside starts retraction");
        clock.Advance(90);
        transition.Tick(false, false, true);
        clock.Advance(180);
        transition.Tick(false, false, true);
        check(transition.IsExpanded && !transition.TargetCollapsed, "menu or drag interrupts retraction");
        clock.Advance(3000);
        transition.Tick(false, false, true);
        check(transition.IsExpanded, "open settings prevents timed retraction");
        transition.Tick(false, false, false);
        clock.Advance(900);
        transition.Tick(false, false, false);
        check(!transition.TargetCollapsed, "closing settings starts a fresh leave delay");
        transition.Expand(immediately: true);
        check(transition.IsExpanded && !transition.IsAnimating, "recall can restore full bounds immediately");
        VerifyDropHover(check);
    }

    private static void VerifyDropHover(Action<bool, string> check)
    {
        var clock = new Clock();
        var transition = new DockTransition(true, clock);
        clock.Advance(90);
        transition.Tick(true, true, false);
        check(transition.TargetCollapsed && transition.Progress == 0.5,
            "handle under release cursor cannot reverse initial collapse");
        clock.Advance(90);
        transition.Tick(true, true, false);
        check(transition.IsCollapsed && transition.TargetCollapsed, "stationary cursor permits complete retraction");
        clock.Advance(5000);
        transition.Tick(true, true, false);
        check(transition.IsCollapsed && transition.TargetCollapsed, "elapsed time alone does not rearm hover");
        transition.Tick(false, false, false);
        transition.Tick(true, true, false);
        clock.Advance(180);
        transition.Tick(true, true, false);
        check(transition.IsExpanded && !transition.TargetCollapsed, "leave and reenter handle deliberately expands");

        clock = new Clock();
        transition = new DockTransition(true, clock);
        clock.Advance(60);
        transition.Tick(false, true, false);
        clock.Advance(60);
        transition.Tick(true, true, false);
        clock.Advance(60);
        transition.Tick(true, true, false);
        check(transition.IsCollapsed && transition.TargetCollapsed, "exit during slide does not arm hover before collapse finishes");
        transition.Expand(immediately: true);
        check(transition.IsExpanded && !transition.TargetCollapsed, "explicit handle click bypasses hover guard");

        transition.Tick(false, false, false);
        clock.Advance(1000);
        transition.Tick(false, false, false);
        clock.Advance(90);
        transition.Tick(true, true, false);
        check(transition.TargetCollapsed, "later retraction also ignores a newly appearing handle");
    }

    private static void VerifyWindow(Action<bool, string> check, PetPackage pet, DockLayout layout, string? renderRoot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        var window = new MainWindow();
        try
        {
            type.GetField("_pet", flags)!.SetValue(window, pet);
            type.GetMethod("PlayAnimation", flags)!.Invoke(window, new object[] { PetState.Idle, true });
            var settings = (PetSettings)type.GetField("_settings", flags)!.GetValue(window)!;
            settings.ClickThrough = true;
            settings.Topmost = false;
            var clock = new Clock();
            var transition = new DockTransition(true, clock);
            type.GetField("_dockLayout", flags)!.SetValue(window, layout);
            type.GetField("_dockTransition", flags)!.SetValue(window, transition);
            type.GetField("_initialDockRetraction", flags)!.SetValue(window, true);
            var applyVisual = type.GetMethod("ApplyDockVisual", flags)!;
            applyVisual.Invoke(window, null);
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(window.Width, window.Height));
            root.Arrange(new Rect(0, 0, window.Width, window.Height));
            root.UpdateLayout();
            var sprite = (Image)window.FindName("SpriteImage");
            var localOrigin = sprite.TransformToAncestor(root).Transform(new Point());
            check(new Point(window.Left + localOrigin.X, window.Top + localOrigin.Y) == layout.ReleaseBounds.TopLeft,
                $"{layout.Edge} rendered first frame has no layout-centering jump");
            type.GetMethod("ApplyEffectiveWindowOptions", flags)!.Invoke(window, null);
            check(!(bool)type.GetProperty("EffectiveClickThrough", flags)!.GetValue(window)! && window.Topmost,
                $"{layout.Edge} dock remains interactive above other windows");
            clock.Advance(90);
            transition.Tick(false, false, false);
            applyVisual.Invoke(window, null);
            check(((Grid)window.FindName("PetSurface")).ClipToBounds &&
                layout.WorkArea.Contains(new Rect(window.Left, window.Top, window.Width, window.Height)),
                $"{layout.Edge} animation clips within current monitor");
            check(((Border)window.FindName("DockHandle")).Visibility == Visibility.Collapsed,
                $"{layout.Edge} handle stays hidden until retraction completes");
            if (renderRoot is not null) Render(window, renderRoot, $"dock-{layout.Edge}-half.png");
            clock.Advance(90);
            transition.Tick(false, false, false);
            applyVisual.Invoke(window, null);
            check(window.Width == layout.Handle.Width && window.Height == layout.Handle.Height &&
                ((Image)window.FindName("SpriteImage")).Visibility == Visibility.Collapsed,
                $"{layout.Edge} collapsed window covers only handle");
            check((Point)type.GetProperty("PositionToPersist", flags)!.GetValue(window)! == layout.Expanded.TopLeft,
                $"{layout.Edge} settings retain expanded position");
            check(!(bool)type.GetProperty("CanPlayDockAnimation", flags)!.GetValue(window)!,
                $"{layout.Edge} reminder cannot animate collapsed pet");
            ((Border)window.FindName("DockHandle")).RaiseEvent(new System.Windows.Input.MouseEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
            check(transition.IsCollapsed && transition.TargetCollapsed,
                $"{layout.Edge} native mouse-enter cannot bypass hover guard");
            var session = (CompanionSession)type.GetField("_focusSession", flags)!.GetValue(window)!;
            session.StartOrResume();
            type.GetMethod("OnSessionCompleted", flags)!.Invoke(window, new object[] { SessionPhase.Focus });
            check(session.IsFocusing && transition.IsCollapsed && transition.TargetCollapsed,
                $"{layout.Edge} docking keeps timer running and reminders do not expand");
            if (renderRoot is not null) Render(window, renderRoot, $"dock-{layout.Edge}-handle.png");
            type.GetMethod("Window_MouseLeftButtonDown", flags)!.Invoke(window, new object[] { window,
                new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                    Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent } });
            check(window.Width == layout.Expanded.Width &&
                ((Image)window.FindName("SpriteImage")).Visibility == Visibility.Visible,
                $"{layout.Edge} handle click expands even with click-through enabled");
            type.GetMethod("CancelPointerInteraction", flags)!.Invoke(window, null);
            type.GetMethod("LeaveDock", flags)!.Invoke(window, null);
            check((bool)type.GetProperty("EffectiveClickThrough", flags)!.GetValue(window)! && !window.Topmost &&
                !(bool)type.GetProperty("IsEdgeDocked", flags)!.GetValue(window)!,
                $"{layout.Edge} undock restores click-through and topmost preferences");
        }
        finally
        {
            type.GetMethod("PrepareForApplicationShutdown", flags)!.Invoke(window, null);
            window.Close();
        }
    }

    private static void Render(MainWindow window, string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(window.Width, window.Height));
        root.Arrange(new Rect(0, 0, window.Width, window.Height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.Width), (int)Math.Ceiling(window.Height),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(int milliseconds) => _ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }
}
