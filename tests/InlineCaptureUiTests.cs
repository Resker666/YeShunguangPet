using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class InlineCaptureUiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static T Find<T>(Window window, string name) => (T)window.FindName(name);
    private static void Wait(int ms = 60) => FocusVisualGate.Pump(ms);
    public static void RunLive(Action<bool, string> check, PetCatalog catalog, string renders)
    {
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()), catalog, _ => { }, false); desktop.Start(false);
        foreach (var theme in new[] { "light", "dark" })
        {
            desktop.UpdateAppearance(new AppearanceOptions { Theme = theme, Accent = "#24796E", ReduceMotion = true });
            var source = ManagerImage(desktop);
            var bounds = new CaptureRect(-30000, -30000, source.PixelWidth, source.PixelHeight);
            var frame = new CaptureFrame(source, bounds, new[] { bounds });
            BitmapSource? copied = null; var busy = false; string? savePath = null;
            using var capture = new CaptureSelection(frame, () => new Point(bounds.X + 100, bounds.Y + 100),
                image => { if (busy) throw new ExternalException("剪贴板被占用，请重试。"); copied = image; }, _ => savePath);
            var count = Application.Current.Windows.Count;
            capture.ShowAsync(CancellationToken.None);
            capture.Begin(new Point(bounds.X + 60, bounds.Y + 50)); capture.End(new Point(bounds.X + 940, bounds.Y + 550)); Wait();
            var toolbar = Field<CaptureToolbarWindow>(capture, "_toolbar");
            var overlays = Field<System.Collections.Generic.List<Window>>(capture, "_windows");
            check(!capture.Result.IsCompleted && Application.Current.Windows.Count == count + 2, "inline capture retains one desktop overlay and one floating toolbar, not an editor");
            check(toolbar.ActualHeight < 70 && Find<FrameworkElement>(toolbar, "ToolOptions").Visibility == Visibility.Collapsed, "selection mode keeps a single-row compact toolbar");
            AssertPlacement(check, capture, bounds);
            RenderScene(capture, bounds, renders, "inline-selection-" + theme + ".png");
            // Compare physical screen pixels throughout; visual offsets are DIPs at non-100% scaling.
            var commandY = Find<FrameworkElement>(toolbar, "CommandRow").PointToScreen(new Point()).Y;
            Find<RadioButton>(toolbar, "ArrowTool").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Wait();
            var expandedCommandY = Find<FrameworkElement>(toolbar, "CommandRow").PointToScreen(new Point()).Y;
            check(Math.Abs(commandY - expandedCommandY) <= 1, $"contextual options open without shifting the command row under the pointer: {commandY:0.##} → {expandedCommandY:0.##} screen pixels");
            check(capture.Tool == CaptureTool.Arrow && Find<FrameworkElement>(toolbar, "StrokeOptions").IsVisible && !Find<FrameworkElement>(toolbar, "FontOptions").IsVisible, "line tools expose only colors and stroke width");
            RenderScene(capture, bounds, renders, "inline-options-" + theme + ".png");
            capture.Begin(new Point(bounds.X + 190, bounds.Y + 250)); capture.End(new Point(bounds.X + 400, bounds.Y + 380));
            check(capture.Document.Marks.Count == 1, "in-place pointer gesture uses the shared annotation model");
            capture.SetTool(CaptureTool.Text); capture.Begin(new Point(bounds.X + 400, bounds.Y + 200)); Wait();
            var input = Field<TextBox>(capture, "_textInput");
            check(input.IsVisible && Find<FrameworkElement>(toolbar, "FontOptions").IsVisible && !Find<FrameworkElement>(toolbar, "StrokeOptions").IsVisible, "text starts at the image with contextual font controls");
            input.Text = "原地标注"; capture.CommitText();
            check(capture.Document.Marks.Any(m => m.Text == "原地标注") && overlays.Select(w => ((Canvas)((Grid)w.Content).Children[1]).Children.Count).Sum() == 0, "committing text removes the temporary input and preserves its raster annotation");
            capture.SetTool(CaptureTool.Ellipse); capture.Begin(new Point(bounds.X + 420, bounds.Y + 280)); capture.End(new Point(bounds.X + 700, bounds.Y + 430));
            capture.SetTool(CaptureTool.Mosaic); capture.MosaicBlockSize = 8; capture.Begin(new Point(bounds.X + 600, bounds.Y + 120)); capture.End(new Point(bounds.X + 780, bounds.Y + 165));
            check(Find<FrameworkElement>(toolbar, "ToolOptions").IsVisible && Find<FrameworkElement>(toolbar, "MosaicOptions").IsVisible && !Find<FrameworkElement>(toolbar, "StrokeOptions").IsVisible, "mosaic exposes only its strength control");
            capture.SetTool(null);
            var before = capture.Selection;
            capture.Begin(new Point(before.Right, before.Bottom)); capture.End(new Point(before.Right + 25, before.Bottom + 25));
            check(capture.Selection.Width == before.Width + 25 && capture.Document.Marks.Count == 4, "resize handle expands the live selection without scaling or dropping annotations");
            capture.Undo(); check(capture.Selection == before, "floating toolbar undo restores selection geometry");
            var center = new Point(before.X + before.Width / 2.0, before.Y + before.Height / 2.0);
            capture.Begin(center); capture.End(center + new Vector(10, 10));
            check(capture.Selection.X == before.X + 10 && capture.Selection.Width == before.Width, "dragging inside the selection moves its frame without resizing it");
            capture.Undo(); RenderScene(capture, bounds, renders, "inline-annotated-" + theme + ".png");
            busy = true;
            check(!capture.Complete(CaptureOutput.Copy) && !capture.Result.IsCompleted && overlays.All(w => w.IsVisible), "clipboard failure keeps the full in-place editing session recoverable");
            AssertPlacement(check, capture, bounds); busy = false;
            check(!capture.Complete(CaptureOutput.Save) && !capture.Result.IsCompleted, "canceling the save dialog does not cancel the screenshot");
            savePath = Path.Combine(renders, "inline-saved-" + theme + ".png");
            check(capture.Complete(CaptureOutput.Save) && File.Exists(savePath) && capture.Result.Result?.Output == CaptureOutput.Save, "saving exports the selected raster and exits the capture flow");
            Wait(); check(Application.Current.Windows.Count == count && copied is null, "save completion removes both windows without overwriting the clipboard");

            using var full = new CaptureSelection(frame, () => new Point(bounds.X + 100, bounds.Y + 100), image => copied = image);
            full.ShowAsync(CancellationToken.None, CaptureMode.AllScreens); Wait();
            check(full.Selection == bounds && full.Document.Crop.Width == source.PixelWidth, "all-screen entry selects every edge pixel before annotation");
            AssertPlacement(check, full, bounds); RenderScene(full, bounds, renders, "inline-fullscreen-" + theme + ".png");
            check(full.Complete(CaptureOutput.Copy) && copied?.PixelWidth == source.PixelWidth && copied.PixelHeight == source.PixelHeight, "full-screen copy keeps original dimensions without including toolbar UI");
            Wait(); check(Application.Current.Windows.Count == count, "full-screen completion leaves no capture window behind");
        }
        var options = new ShortcutOptions { CaptureCurrentScreen = new(3, 0x44), CaptureAllScreens = new(3, 0x41) }; options.Validate();
        check(options.Copy().CaptureAllScreens == options.CaptureAllScreens && new ShortcutOptions().CaptureAllScreens is null, "new full-screen shortcuts are independently configurable and default to disabled");
        var refs = ClosedSessions(); Wait(100); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Wait(100); GC.Collect();
        check(refs.All(r => !r.IsAlive), "closed inline sessions, overlays and floating toolbars are collectible");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference[] ClosedSessions()
    {
        var refs = new System.Collections.Generic.List<WeakReference>(); var image = CaptureTests.Fixture(600, 360); var bounds = new CaptureRect(-30000, -30000, 600, 360);
        for (var i = 0; i < 5; i++)
        {
            var session = new CaptureSelection(new CaptureFrame(image, bounds, new[] { bounds }), () => new Point(bounds.X + 20, bounds.Y + 20), _ => { });
            session.ShowAsync(CancellationToken.None, CaptureMode.AllScreens); Wait();
            refs.Add(new WeakReference(session)); refs.Add(new WeakReference(Field<CaptureToolbarWindow>(session, "_toolbar")));
            foreach (var window in Field<System.Collections.Generic.List<Window>>(session, "_windows")) refs.Add(new WeakReference(window));
            session.Dispose();
        }
        return refs.ToArray();
    }
    private static Rect NativeBounds(Window window)
    {
        var type = typeof(MainWindow).Assembly.GetType("YeShunguangPet.NativeMethods")!;
        var args = new object[] { window, Rect.Empty }; type.GetMethod("TryGetWindowBounds", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args); return (Rect)args[1];
    }
    private static void AssertPlacement(Action<bool, string> check, CaptureSelection capture, CaptureRect monitor)
    {
        Wait(); var toolbar = Field<CaptureToolbarWindow>(capture, "_toolbar"); var bounds = NativeBounds(toolbar);
        check(monitor.ToRect().Contains(bounds), $"floating toolbar stays inside monitor: {bounds}");
        foreach (var name in new[] { "DoneButton", "CancelButton", "SaveButton", "PinButton" })
        {
            var button = Find<FrameworkElement>(toolbar, name); var root = (FrameworkElement)toolbar.Content;
            var rect = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
            check(rect.Right <= root.ActualWidth + 1 && rect.Bottom <= root.ActualHeight + 1, "floating action stays reachable: " + name);
        }
    }
    private static BitmapSource ManagerImage(DesktopSession desktop)
    {
        var manager = new PetManagerWindow(desktop);
        try
        {
            var root = (FrameworkElement)manager.Content; root.Measure(new Size(1000, 700)); root.Arrange(new Rect(0, 0, 1000, 700)); root.UpdateLayout();
            var target = new RenderTargetBitmap(1000, 700, 96, 96, PixelFormats.Pbgra32); var background = new DrawingVisual();
            using (var dc = background.RenderOpen()) dc.DrawRectangle(manager.Background, null, new Rect(0, 0, 1000, 700));
            target.Render(background); target.Render(root); target.Freeze(); return target;
        }
        finally { manager.Close(); }
    }
    private static void RenderScene(CaptureSelection capture, CaptureRect monitor, string folder, string name)
    {
        var windows = Field<System.Collections.Generic.List<Window>>(capture, "_windows");
        var toolbar = Field<CaptureToolbarWindow>(capture, "_toolbar");
        var target = new RenderTargetBitmap(monitor.Width, monitor.Height, 96, 96, PixelFormats.Pbgra32); var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            foreach (var window in windows.Append<Window>(toolbar))
            {
                var bounds = NativeBounds(window); bounds.Offset(-monitor.X, -monitor.Y);
                dc.DrawRectangle(new VisualBrush((Visual)window.Content), null, bounds);
            }
        }
        target.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        Directory.CreateDirectory(folder); using var output = File.Create(Path.Combine(folder, name)); encoder.Save(output);
    }
}
