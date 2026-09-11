using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class CaptureUiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Find<T>(Window window, string name) => (T)window.FindName(name);
    public static void RunLive(Action<bool, string> check, PetCatalog catalog, string renders)
    {
        var fixture = CaptureTests.Fixture();
        var clock = new SpeechStudyTests.ManualClock();
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings { Topmost = false, FocusMinutes = 1, BreakMinutes = 1 }), catalog, _ => { }, false, clock);
        desktop.Start(false);
        foreach (var theme in new[] { "light", "dark" })
        {
            desktop.UpdateAppearance(new AppearanceOptions { Theme = theme, Accent = "#24796E", ReduceMotion = true });
            BitmapSource? copied = null; var failCopy = false;
            var editor = new CaptureEditorWindow(fixture, image => desktop.PinCapture(image, show: false), image => { if (failCopy) throw new ExternalException("Clipboard busy"); copied = image; });
            try
            {
                Show(editor); var surface = editor.Surface;
                surface.Tool = CaptureTool.Arrow; surface.BeginAnnotation(new Point(80, 80)); surface.MoveAnnotation(new Point(150, 125)); surface.EndAnnotation(new Point(220, 160));
                check(editor.Document.Marks.Count == 1 && Find<Button>(editor, "UndoButton").IsEnabled, "canvas drag creates one undoable annotation");
                surface.Tool = CaptureTool.Rectangle; surface.BeginAnnotation(new Point(280, 80)); surface.EndAnnotation(new Point(500, 220));
                surface.Tool = CaptureTool.Pen; surface.BeginAnnotation(new Point(60, 220)); surface.MoveAnnotation(new Point(100, 280)); surface.EndAnnotation(new Point(220, 230));
                surface.Tool = CaptureTool.Text; surface.LabelText = "Capture fixture"; surface.BeginAnnotation(new Point(80, 30));
                surface.Tool = CaptureTool.Mosaic; surface.MosaicBlockSize = 8; surface.BeginAnnotation(new Point(300.7, 110.7)); surface.EndAnnotation(new Point(440.2, 155.2));
                check(!CaptureTests.Pixel(editor.Document.Flatten(), 320, 120).SequenceEqual(CaptureTests.Pixel(fixture, 320, 120)), "fractional mosaic boundaries preserve a rasterized block effect");
                foreach (var size in new[] { new Size(1000, 720), new Size(720, 480) })
                {
                    editor.Width = size.Width; editor.Height = size.Height; Wait(80);
                    foreach (var name in new[] { "CopyButton", "SaveButton", "PinButton", "UndoButton", "RedoButton", "LabelInput", "TextSize", "LineWidth", "MosaicStrength" })
                        check(Inside(Find<FrameworkElement>(editor, name), (FrameworkElement)editor.Content), $"capture {name} stays inside {size} {theme}");
                    check(surface.ActualWidth > 0 && editor.Document.Flatten().PixelWidth == 600, "editor zoom and resize never alter output dimensions");
                    Render(editor, renders, $"capture-editor-{size.Width}-{theme}.png");
                }
                var marks = editor.Document.Marks.Count; surface.Tool = CaptureTool.Crop; surface.BeginAnnotation(new Point(30, 20)); surface.EndAnnotation(new Point(540, 320));
                check(editor.Document.Crop == new Int32Rect(30, 20, 510, 300) && surface.Width == 510, "crop gesture updates the real editor canvas and export size");
                Find<Button>(editor, "UndoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(editor.Document.Crop.Width == 600 && editor.Document.Marks.Count == marks, "editor undo restores cropped image and annotations together");
                failCopy = true; check(!editor.CopyImage() && editor.IsVisible && copied is null, "busy clipboard preserves the editable screenshot and reports failure");
                failCopy = false; check(editor.CopyImage() && copied is { IsFrozen: true, PixelWidth: 600 }, "clipboard action receives only the flattened bitmap");
                check(editor.SaveImage(Path.Combine(renders, "capture-export-" + theme + ".png")), "save command writes the annotated PNG");
                check(editor.PinImage() && desktop.PinCount == 1, "pin command hands off the flattened result");
                var pin = desktop.Pins.Single(); Show(pin); pin.SetZoom(0.75); Wait(60);
                check(pin.Topmost && !pin.ShowInTaskbar && pin.Snapshot.PixelWidth == 600 && pin.Zoom <= 0.75, "pin is a bounded independent image window without a taskbar entry");
                Render(pin, renders, "capture-pin-" + theme + ".png");
                desktop.HidePins(); check(!pin.IsVisible && desktop.PinCount == 1, "hiding pins preserves their in-memory images");
                desktop.ShowPins(); Wait(40); check(pin.IsVisible, "hidden pins can be restored without reloading an image");
                desktop.ClosePins(); check(desktop.PinCount == 0, "closed pins are removed from the session");
            }
            finally { editor.Close(); desktop.ClosePins(); }
        }
        VerifySelection(check, fixture, renders);
        InlineCaptureUiTests.RunLive(check, catalog, renders);
        VerifyWorkflow(check, desktop, fixture, clock);
        VerifyLifetime(check, fixture);
        VerifyNativeCapture(check);
        using var tray = new TrayMenu(desktop);
        var item = (System.Windows.Forms.ToolStripMenuItem)tray.Items.Find("pins", false).Single();
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        tray.Show(new System.Drawing.Point(work.Right - 300, work.Top + 30)); item.ShowDropDown(); Wait(80);
        check(item.DropDown.Visible && item.DropDownItems.Count == 4 && item.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>().All(i => i.Bounds.Right <= item.DropDown.Width), "tray pin submenu exposes clipboard, show, hide and close commands without clipping");
        item.HideDropDown(); tray.Close();
    }
    private static void VerifySelection(Action<bool, string> check, BitmapSource fixture, string renders)
    {
        var frame = new CaptureFrame(fixture, new CaptureRect(-30000, -30000, 600, 360), new[] { new CaptureRect(-30000, -30000, 300, 360), new CaptureRect(-29700, -30000, 300, 360) });
        BitmapSource? copied = null;
        using var selection = new CaptureSelection(frame, () => new Point(-29900, -29920), image => copied = image);
        using var cancellation = new CancellationTokenSource();
        var count = Application.Current.Windows.Count;
        var task = selection.ShowAsync(cancellation.Token); Wait(60);
        selection.Begin(new Point(-29900, -29920)); selection.Move(new Point(-29500, -29720)); Wait(40);
        var windows = ((System.Collections.Generic.List<Window>)typeof(CaptureSelection).GetField("_windows", Private)!.GetValue(selection)!).ToArray();
        check(windows.Length == 2 && windows.All(w => !w.ShowInTaskbar && w.Topmost), "selection uses one borderless overlay per monitor");
        for (var i = 0; i < windows.Length; i++) Render(windows[i], renders, $"capture-selection-monitor-{i}.png");
        selection.End(new Point(-29500, -29720)); Wait(40);
        check(!task.IsCompleted && selection.Selection == new CaptureRect(-29900, -29920, 400, 200) && windows.All(w => w.IsVisible), "releasing cross-monitor selection keeps the original desktop overlays alive");
        var toolbar = (CaptureToolbarWindow)typeof(CaptureSelection).GetField("_toolbar", Private)!.GetValue(selection)!;
        check(toolbar.IsVisible && !toolbar.ShowInTaskbar && Find<FrameworkElement>(toolbar, "ToolOptions").Visibility == Visibility.Collapsed, "selected region opens only a compact contextual floating toolbar");
        Find<Button>(toolbar, "DoneButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(50);
        check(task.IsCompletedSuccessfully && task.Result?.Region == selection.Selection && copied?.PixelWidth == 400 && Application.Current.Windows.Count == count, "Done copies a flattened screenshot and removes every capture window");
        using var canceled = new CaptureSelection(frame, () => new Point(-29900, -29920)); var cancelTask = canceled.ShowAsync(cancellation.Token); cancellation.Cancel(); Wait(80);
        check(cancelTask.IsCompletedSuccessfully && cancelTask.Result is null && Application.Current.Windows.Count == count, "cancellation releases all selection windows and event subscriptions");
        canceled.ShowAsync(CancellationToken.None); check(Application.Current.Windows.Count == count, "completed selector cannot reopen orphaned windows");
    }
    private static void VerifyWorkflow(Action<bool, string> check, DesktopSession desktop, BitmapSource fixture, SpeechStudyTests.ManualClock clock)
    {
        var pet = desktop.Windows.First(); pet.ShowActivated = false; pet.Show(); Wait(50);
        var config = desktop.Configuration.Pets[0]; var wasHidden = config.Hidden;
        var foreign = new Window { Width = 120, Height = 80 }; Show(foreign);
        var frame = new CaptureFrame(fixture, new CaptureRect(0, 0, 600, 360), new[] { new CaptureRect(0, 0, 600, 360) });
        var captured = 0;
        var canceled = desktop.CaptureAsync(readScreen: () => { captured++; check(!pet.IsVisible && config.Hidden == wasHidden, "capture hides the visual without persisting a hidden-role preference"); return frame; }, interact: (capture, _) => { capture.Cancel(); return Task.CompletedTask; });
        Await(canceled); check(pet.IsVisible && !desktop.IsCapturing && captured == 1, "cancel restores previous visibility and leaves no editor");
        check(foreign.IsVisible, "temporary capture visibility stays within the owning desktop session"); foreign.Close();
        var failed = desktop.CaptureAsync(readScreen: () => throw new InvalidOperationException("simulated capture failure"));
        try { Await(failed); } catch (InvalidOperationException) { }
        check(pet.IsVisible && !desktop.IsCapturing && config.Hidden == wasHidden, "failed capture also restores windows and preferences");
        pet.Hide(); desktop.Companion.Session.Reset(); desktop.Companion.Session.StartOrResume();
        BitmapSource? copied = null;
        var accepted = desktop.CaptureAsync(readScreen: () => frame, copy: image => copied = image, interact: (capture, _) =>
        {
            clock.Advance(60); desktop.Companion.Tick();
            capture.Begin(new Point(30, 40)); capture.End(new Point(210, 140));
            var repeated = desktop.CaptureAsync(readScreen: () => { captured++; return frame; });
            check(repeated.IsCompletedSuccessfully && captured == 1, "repeated capture command preserves the active inline session");
            capture.Complete(CaptureOutput.Copy); return Task.CompletedTask;
        });
        Await(accepted);
        check(copied?.PixelWidth == 180 && !pet.IsVisible && !Application.Current.Windows.OfType<CaptureEditorWindow>().Any(), "inline completion restores the desktop without opening an independent image editor");
        var pinned = desktop.CaptureAsync(CaptureMode.AllScreens, readScreen: () => frame, interact: (capture, _) => { capture.Complete(CaptureOutput.Pin); return Task.CompletedTask; });
        Await(pinned); check(desktop.PinCount == 1 && desktop.Pins.Single().IsVisible && desktop.Pins.Single().Snapshot.PixelWidth == 600, "inline pin reserves capacity before exit and reveals the result only after restoring the desktop"); desktop.ClosePins();
        check(desktop.Companion.PendingCompletion == SessionPhase.Focus && desktop.Companion.Session.Status == SessionStatus.Completed, "completion during screenshot retains its unread notice without opening or restarting the timer");
        desktop.OpenFocus(); Wait(80);
        var focus = (FocusWindow)typeof(CompanionRuntime).GetField("_window", Private)!.GetValue(desktop.Companion)!;
        check(desktop.Companion.PendingCompletion is null && focus.TaskbarItemInfo?.Overlay is null && desktop.Companion.Session.Status == SessionStatus.Completed, "viewing completion clears the badge without starting the next phase");
        desktop.Companion.Session.StartOrResume(); clock.Advance(60); desktop.Companion.Tick(); Wait(60);
        check(desktop.Companion.PendingCompletion == SessionPhase.Break && focus.TaskbarItemInfo?.Overlay is not null, "existing timer window receives the pending completion overlay");
        desktop.Companion.AcknowledgeCompletion(); check(focus.TaskbarItemInfo?.Overlay is null, "acknowledgement clears the actual window overlay"); focus.Close();
        using var icon = CompletionBadge.CreateTrayIcon(System.Drawing.SystemIcons.Application);
        check(icon.Handle != IntPtr.Zero && icon.Width == 32, "tray fallback produces an independent native icon");
        var fakeTray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = false };
        typeof(DesktopSession).GetField("_tray", Private)!.SetValue(desktop, fakeTray);
        desktop.Companion.Session.Reset(); desktop.Companion.Session.StartOrResume(); clock.Advance(60); desktop.Companion.Tick();
        check(fakeTray.Text == "专注完成" && !ReferenceEquals(fakeTray.Icon, System.Drawing.SystemIcons.Application), "background completion updates the actual tray model even with no timer window");
        ShortcutTests.Call(desktop, "TrayClick", null!, new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 0, 0, 0));
        check(desktop.Companion.PendingCompletion is null && desktop.Companion.Session.Status == SessionStatus.Completed && fakeTray.Text == "叶瞬光桌面宠物", "tray click acknowledges the completion and restores its regular icon");
        ((FocusWindow)typeof(CompanionRuntime).GetField("_window", Private)!.GetValue(desktop.Companion)!).Close();
        var pin = desktop.PinCapture(fixture, show: false);
        var waiting = new TaskCompletionSource();
        var pending = desktop.CaptureAsync(readScreen: () => frame, interact: (_, token) => { token.Register(() => waiting.TrySetCanceled(token)); return waiting.Task; });
        Wait(160); desktop.Dispose();
        try { Await(pending); } catch (OperationCanceledException) { }
        check(!desktop.IsCapturing && desktop.PinCount == 0 && !pin.IsVisible, "shutdown cancels an in-flight selection without restoring or retaining pin windows");
    }
    private static void VerifyLifetime(Action<bool, string> check, BitmapSource fixture)
    {
        var references = CreateClosedWindows(fixture);
        Wait(100); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Wait(100); GC.Collect();
        check(references.All(r => !r.IsAlive), "closed capture editors and pins do not accumulate live window references");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateClosedWindows(BitmapSource fixture)
    {
        var references = new System.Collections.Generic.List<WeakReference>();
        for (var i = 0; i < 5; i++)
        {
            var editor = new CaptureEditorWindow(fixture, _ => { }, _ => { }); Show(editor); references.Add(new WeakReference(editor)); editor.Close();
            var pin = new CapturePinWindow(fixture, i, _ => { }); Show(pin); references.Add(new WeakReference(pin)); pin.Close();
        }
        return references.ToArray();
    }
    private static void VerifyNativeCapture(Action<bool, string> check)
    {
        if (!GetCursorPos(out _))
        {
            Console.WriteLine("SKIP: real screen capture requires access to the interactive desktop; synthetic capture and raster checks still run.");
            return;
        }
        // Only capture the interior of this owned, solid-color test window, never the user's desktop.
        var fixture = new Window { Width = 320, Height = 240, Left = 60, Top = 60, WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Background = new SolidColorBrush(Color.FromRgb(240, 30, 180)) };
        try
        {
            fixture.Show(); Wait(150);
            var native = typeof(MainWindow).Assembly.GetType("YeShunguangPet.NativeMethods")!;
            var args = new object[] { fixture, Rect.Empty }; native.GetMethod("TryGetWindowBounds", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
            var bounds = (Rect)args[1];
            var image = ScreenCapture.ReadRegion(new CaptureRect((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2), 8, 8));
            var pixel = CaptureTests.Pixel(image, 4, 4);
            check(image.IsFrozen && pixel[2] > 200 && pixel[1] < 80 && pixel[0] > 130, "real Windows capture reads the owned test window pixels correctly");
        }
        finally { fixture.Close(); }
    }
    private static void Show(Window window) { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = window.Top = -30000; window.ShowActivated = window.ShowInTaskbar = false; window.Show(); Wait(80); }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out CursorPoint point);
    private static void Wait(int ms) => FocusVisualGate.Pump(ms);
    private static void Await(Task task) { var until = Environment.TickCount64 + 10000; while (!task.IsCompleted && Environment.TickCount64 < until) Wait(20); if (!task.IsCompleted) throw new TimeoutException("Capture did not finish."); task.GetAwaiter().GetResult(); }
    private static bool Inside(FrameworkElement item, FrameworkElement root)
    {
        var rect = item.TransformToAncestor(root).TransformBounds(new Rect(item.RenderSize)); return rect.Left >= -1 && rect.Top >= -1 && rect.Right <= root.ActualWidth + 1 && rect.Bottom <= root.ActualHeight + 1;
    }
    private static void Render(Window window, string directory, string name)
    {
        var root = (FrameworkElement)window.Content; root.UpdateLayout();
        var width = (int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right); var height = (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom);
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen()) { dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height)); dc.DrawRectangle(new VisualBrush(root), null, new Rect(root.Margin.Left, root.Margin.Top, root.ActualWidth, root.ActualHeight)); }
        target.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target)); Directory.CreateDirectory(directory); using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }
}
