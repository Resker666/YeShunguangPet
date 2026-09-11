using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class CaptureTests
{
    public static BitmapSource Fixture(int width = 600, int height = 360)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4; bytes[at] = (byte)(190 + y % 45); bytes[at + 1] = (byte)(180 + x % 65); bytes[at + 2] = (byte)(220 + y % 25); bytes[at + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4); image.Freeze(); return image;
    }
    public static byte[] Pixel(BitmapSource source, int x, int y)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var bytes = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0); return bytes;
    }
    private static bool Rejects(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } catch (InvalidOperationException) { return true; } }
    public static void Run(Action<bool, string> check, PetCatalog catalog, string temporary)
    {
        var image = Fixture(); var bounds = new CaptureRect(-400, -200, 600, 360);
        var selection = CaptureRect.Select(new Point(100, 80), new Point(-350, -100), bounds);
        check(selection == new CaptureRect(-350, -100, 450, 180), "capture selection supports reversed drags and negative monitor origins");
        check(CaptureRect.Select(new Point(-900, -900), new Point(900, 900), bounds) == bounds, "capture selection clamps to the frozen desktop bounds");
        check(CaptureRect.Select(new Point(-399.8, -199.8), new Point(-397.2, -196.2), bounds) == new CaptureRect(-400, -200, 3, 4), "fractional DPI coordinates round outward without dropping edge pixels");
        check(Rejects(() => CaptureRaster.ValidateSize(9000, 9000)) && Rejects(() => CaptureRaster.ValidateSize(0, 10)), "oversized and empty captures are rejected before allocating buffers");
        var frame = new CaptureFrame(image, bounds, new[] { bounds });
        var crop = frame.Crop(new CaptureRect(-300, -150, 120, 80));
        check(crop.IsFrozen && crop.PixelWidth == 120 && crop.PixelHeight == 80 && Pixel(crop, 5, 9).SequenceEqual(Pixel(image, 105, 59)), "selected physical coordinates produce detached original-size pixels");
        check(Rejects(() => frame.Crop(new CaptureRect(-500, -150, 50, 50))), "out-of-screen selection cannot read another region");
        BitmapSource? copy = null;
        using (var picker = new CaptureSelection(frame, copy: image => copy = image))
        {
            picker.Begin(new Point(-300, -150)); picker.End(new Point(-180, -70));
            check(!picker.Result.IsCompleted && picker.Selection == new CaptureRect(-300, -150, 120, 80), "mouse release retains the selected region for inline editing");
            picker.Complete(CaptureOutput.Copy);
            check(picker.Result.IsCompletedSuccessfully && picker.Result.Result?.Region == picker.Selection && copy?.PixelWidth == 120, "explicit completion copies the original-size region and finishes exactly once");
            picker.Cancel(); check(picker.Result.Result is not null, "late cancel cannot overwrite an accepted selection");
        }
        var document = new CaptureDocument(image);
        check(Pixel(document.Flatten(), 17, 29).SequenceEqual(Pixel(image, 17, 29)), "unannotated export preserves screenshot pixels");
        document.Add(new CaptureMark(CaptureTool.Mosaic, new Point(20, 30), new Point(180, 110), Colors.Red, 8));
        var masked = document.Flatten();
        check(!Pixel(masked, 80, 60).SequenceEqual(Pixel(image, 80, 60)) && Pixel(masked, 200, 150).SequenceEqual(Pixel(image, 200, 150)), "mosaic exports altered blocks while leaving other pixels intact");
        check(Pixel(image, 80, 60)[0] != 0, "mosaic annotation cannot change the immutable source screenshot");
        document.SetCrop(new Int32Rect(40, 40, 200, 150));
        var flattened = document.Flatten();
        check(flattened.PixelWidth == 200 && flattened.PixelHeight == 150 && !Pixel(flattened, 40, 20).SequenceEqual(Pixel(image, 80, 60)), "crop and mosaic are flattened together without hidden original layers");
        document.Undo(); check(document.Crop.Width == 600 && document.Marks.Count == 1, "undo crop restores geometry without losing annotations");
        document.Undo(); check(document.Marks.Count == 0 && Pixel(document.Flatten(), 80, 60).SequenceEqual(Pixel(image, 80, 60)), "undo mark restores the editable source");
        document.Redo(); check(document.Marks.Count == 1, "redo restores a complete annotation");
        document.Add(new CaptureMark(CaptureTool.Rectangle, new Point(200, 150), new Point(360, 260), Colors.Blue, 4));
        check(!document.CanRedo, "new annotation discards only the old redo branch");
        foreach (var tool in new[] { CaptureTool.Arrow, CaptureTool.Pen, CaptureTool.Text })
            document.Add(new CaptureMark(tool, new Point(250, 40), new Point(400, 120), Colors.DarkGreen, 5, "Capture fixture", 24, new[] { new Point(250, 40), new Point(300, 100), new Point(400, 120) }));
        check(document.Flatten().IsFrozen && document.Marks.Count == 5, "WPF shape, text and ink render into one frozen export");
        check(Rejects(() => new CaptureMark(CaptureTool.Text, new Point(), new Point(), Colors.Red, 2, new string('x', 501))), "annotation text length is bounded");
        check(Rejects(() => new CaptureMark(CaptureTool.Pen, new Point(), new Point(), Colors.Red, 2, points: Enumerable.Repeat(new Point(), 10001))), "pen sample count is bounded");
        var output = Path.Combine(temporary, "capture-export.png"); CaptureRaster.SavePng(document.Flatten(), output);
        using (var stream = File.OpenRead(output))
        {
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            check(decoder.Frames.Count == 1 && decoder.Frames[0].PixelWidth == 600 && !Pixel(decoder.Frames[0], 80, 60).SequenceEqual(Pixel(image, 80, 60)), "PNG export contains one flattened raster with effective mosaic");
        }
        check(Rejects(() => CaptureRaster.SavePng(image, Path.Combine(temporary, "not-png.jpg"))) && !Directory.GetFiles(temporary, "capture-export.png.*.tmp").Any(), "export enforces PNG and leaves no staging files");
        var history = new CaptureDocument(Fixture(32, 32));
        for (var i = 0; i < 120; i++) history.Add(new CaptureMark(CaptureTool.Rectangle, new Point(1, 1), new Point(10, 10), Colors.Red, 2));
        var undoCount = 0; while (history.CanUndo) { history.Undo(); undoCount++; }
        check(undoCount == 100, "annotation undo history is bounded to one hundred operations");
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()), catalog, _ => { }, false);
        desktop.Start(false);
        for (var i = 0; i < 8; i++) desktop.PinCapture(crop, show: false);
        check(Rejects(() => desktop.PinCapture(crop, show: false)) && desktop.PinCount == 8, "pin count is capped without discarding existing images");
        desktop.ClosePins(); check(desktop.PinCount == 0, "closing all pins releases the owned window list");
        VerifyRegionEditing(check, image);
        VerifyNotice(check);
    }
    private static void VerifyRegionEditing(Action<bool, string> check, BitmapSource image)
    {
        var bounds = new CaptureRect(-300, -200, 600, 360); var start = new CaptureRect(-100, -80, 160, 120);
        var region = new CaptureRegion(bounds); region.Set(start);
        check(region.Handles().Count == 8 && region.HitTest(new Point(start.Right, start.Y + 20), 2) == CaptureHit.East, "all eight handles and entire selection edges can be resized");
        foreach (var hit in Enum.GetValues<CaptureHit>().Where(h => h is not (CaptureHit.Outside or CaptureHit.Move)))
        {
            region.Set(start); var handle = region.Handles().Single(h => h.Hit == hit).Point;
            region.Begin(handle, hit); region.End(handle + new Vector(10, 10));
            check(region.Selection != start && region.Selection.HasArea, "resize handle changes geometry without inversion: " + hit);
        }
        region.Set(start); region.Begin(new Point(start.Right, start.Y), CaptureHit.East); region.End(new Point(-200, start.Y));
        check(region.Selection.Width == 1, "dragging past the opposite edge stops at one pixel");
        region.Set(start); region.Begin(new Point(), CaptureHit.Move); region.End(new Point(10000, 10000));
        check(region.Selection.Right == bounds.Right && region.Selection.Bottom == bounds.Bottom && region.Selection.Width == start.Width, "moving selection clamps to the desktop without resizing it");
        region.Set(start); region.Begin(new Point(), CaptureHit.Move); region.Update(new Point(15, 15)); region.CancelDrag();
        check(region.Selection == start, "canceling a drag restores the preceding selection");
        var monitor = new CaptureRect(-1000, 0, 1000, 700);
        foreach (var area in new[] { new CaptureRect(-900, 40, 300, 180), new CaptureRect(-400, 570, 390, 120), monitor })
        {
            var toolbar = CaptureRegion.PlaceToolbar(area, monitor, new Size(640, 100));
            check(monitor.ToRect().Contains(toolbar), "floating toolbar stays on its monitor near every screen edge");
        }
        var doc = new CaptureDocument(image); doc.SetSelection(new Int32Rect(100, 100, 100, 100), initial: true);
        doc.Add(new CaptureMark(CaptureTool.Mosaic, new Point(110, 110), new Point(130, 130), Colors.Black, 8));
        doc.SetSelection(new Int32Rect(50, 50, 250, 200));
        check(Pixel(doc.Flatten(), 10, 10).SequenceEqual(Pixel(image, 60, 60)) && !Pixel(doc.Flatten(), 70, 70).SequenceEqual(Pixel(image, 60, 60)), "expanded region preserves mosaic marks while annotations remain anchored to the desktop");
        doc.Undo(); check(doc.Crop.Width == 100 && doc.Marks.Count == 1, "selection adjustment is undoable without removing marks");
        var frame = new CaptureFrame(image, bounds, new[] { new CaptureRect(-300, -200, 300, 360), new CaptureRect(0, -200, 300, 360) });
        CaptureRect? currentAnchor = null;
        using var current = new CaptureSelection(frame, pinAt: (_, region) => currentAnchor = region); current.Prepare(CaptureMode.CurrentScreen, new Point(30, 0)); current.Complete(CaptureOutput.Pin);
        check(current.Result.Result?.Image?.PixelWidth == 300 && current.Selection == frame.Monitors[1] && currentAnchor == frame.Monitors[1], "current-screen mode selects exactly the cursor monitor and passes its anchor to pin output");
        using var all = new CaptureSelection(frame); all.Prepare(CaptureMode.AllScreens, new Point()); all.Complete(CaptureOutput.Pin);
        check(all.Selection == bounds && all.Result.Result?.Image?.PixelWidth == 600 && all.Result.Result.Image.PixelHeight == 360, "all-screens mode includes the complete virtual desktop bounds");
        using var blockedPin = new CaptureSelection(frame, pin: _ => throw new InvalidOperationException("pin limit"));
        blockedPin.Prepare(CaptureMode.AllScreens, new Point());
        check(!blockedPin.Complete(CaptureOutput.Pin) && !blockedPin.Result.IsCompleted && blockedPin.Error == "pin limit", "pin capacity failure preserves the editable inline session");
    }
    private static void VerifyNotice(Action<bool, string> check)
    {
        var clock = new SpeechStudyTests.ManualClock(); var settings = new PetSettings { FocusMinutes = 1, BreakMinutes = 1 };
        using var service = new CompanionService(settings, clock); service.Configure(); var changes = 0;
        service.CompletionNoticeChanged += () => changes++;
        service.Session.StartOrResume(); clock.Advance(60); service.Tick();
        check(service.PendingCompletion == SessionPhase.Focus && service.History.Records.Count == 1 && changes == 1, "focus completion creates one pending notice and one history record");
        service.Tick(); service.Tick(); check(changes == 1, "repeated ticks do not accumulate duplicate unread badges");
        service.AcknowledgeCompletion(); service.AcknowledgeCompletion();
        check(service.PendingCompletion is null && service.Session.Status == SessionStatus.Completed && changes == 2, "acknowledging a notice clears only the badge and never starts a new phase");
        service.Session.StartOrResume(); clock.Advance(60); service.Tick(); check(service.PendingCompletion == SessionPhase.Break, "break completion gets its own pending notice");
        service.Session.Reset(); check(service.PendingCompletion is null, "reset clears an old completion notice");
        settings.DoNotDisturb = true; service.Session.StartOrResume(); clock.Advance(60); service.Tick();
        check(service.PendingCompletion is null && service.History.Records.Count == 2, "quiet mode suppresses badge creation without losing focus history");
        settings.DoNotDisturb = false; service.Tick(); check(service.PendingCompletion is null, "leaving quiet mode does not replay an old completion");
        service.Session.Reset(); service.Session.StartOrResume(); clock.Advance(60); service.Tick(); settings.NotificationsEnabled = false; service.Configure();
        check(service.PendingCompletion is null, "disabling notifications clears an outstanding badge");
        check(CompletionBadge.CreateOverlay().IsFrozen, "taskbar overlay is a frozen image independent of any UI owner");
        var window = new Window();
        try { CompletionBadge.Apply(window, true); check(window.TaskbarItemInfo?.Overlay is not null, "pending notice installs a taskbar overlay"); CompletionBadge.Apply(window, false); check(window.TaskbarItemInfo?.Overlay is null, "acknowledgement removes the taskbar overlay"); }
        finally { window.Close(); }
    }
}
