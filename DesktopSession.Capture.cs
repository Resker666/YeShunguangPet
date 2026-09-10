using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace YeShunguangPet;

public sealed partial class DesktopSession
{
    private readonly CancellationTokenSource _captureLifetime = new();
    private readonly List<CapturePinWindow> _pins = new();
    private CaptureSelection? _selection;
    private bool _capturing;
    private int _pinSequence;
    public bool IsCapturing => _capturing;
    public int PinCount => _pins.Count;
    public IReadOnlyList<CapturePinWindow> Pins => _pins.AsReadOnly();

    public void StartCapture() => StartCaptureMode(CaptureMode.Region);
    public void StartCurrentScreenCapture() => StartCaptureMode(CaptureMode.CurrentScreen);
    public void StartAllScreensCapture() => StartCaptureMode(CaptureMode.AllScreens);
    private async void StartCaptureMode(CaptureMode mode)
    {
        try { await CaptureAsync(mode); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Error("Screen capture failed.", ex);
            if (!_disposed) System.Windows.MessageBox.Show(ex.Message, "无法截图", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    public async Task CaptureAsync(CaptureMode mode = CaptureMode.Region, Func<CaptureFrame>? readScreen = null,
        Func<CaptureSelection, CancellationToken, Task>? interact = null, Func<Point>? cursorPosition = null, Action<BitmapSource>? copy = null, Func<Window, string?>? savePath = null)
    {
        if (_disposed || _capturing) return;
        if (HasSettingsOpen || _shortcutDialog is not null || Application.Current?.Windows.Cast<Window>().Any(w => w.IsVisible && !NativeMethods.IsWindowInputEnabled(w)) == true)
            throw new InvalidOperationException("请先关闭当前对话框，再开始截图。");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var cursor = cursorPosition ?? ScreenCapture.CursorPosition;
        Point? origin = mode == CaptureMode.CurrentScreen ? cursor() : null;
        _capturing = true; Companion.SuppressCompletionAcknowledgement = true; _speech.Dismiss();
        var suspended = new List<(Window Window, bool Activate, EventHandler Closed)>();
        var closed = new HashSet<Window>(); CaptureResult? result = null; CapturePinWindow? stagedPin = null;
        try
        {
            var windows = CaptureWindows().Where(w => w.IsVisible && w.WindowState != WindowState.Minimized).Distinct().ToArray();
            foreach (var window in windows)
            {
                EventHandler onClosed = (_, _) => closed.Add(window); window.Closed += onClosed;
                if (window.ContextMenu is { } menu) menu.IsOpen = false;
                suspended.Add((window, window.ShowActivated, onClosed)); window.ShowActivated = false; window.Hide();
            }
            await Task.Delay(120, _captureLifetime.Token);
            var frame = (readScreen ?? ScreenCapture.ReadDesktop)();
            _selection = new CaptureSelection(frame, cursor, copy, savePath, image => stagedPin = PinCapture(image, show: false));
            if (interact is not null)
            {
                _selection.Prepare(mode, origin ?? new Point(frame.Bounds.X, frame.Bounds.Y));
                await interact(_selection, _captureLifetime.Token); result = await _selection.Result;
            }
            else result = await _selection.ShowAsync(_captureLifetime.Token, mode, origin);
        }
        finally
        {
            _selection?.Dispose(); _selection = null;
            foreach (var item in suspended)
            {
                item.Window.Closed -= item.Closed;
                if (!closed.Contains(item.Window) && !_disposed)
                {
                    try { item.Window.Show(); }
                    catch (Exception ex) { AppLogger.Error("Could not restore a window after capture.", ex); }
                    finally { item.Window.ShowActivated = item.Activate; }
                }
            }
            try { await Dispatcher.CurrentDispatcher.InvokeAsync(() => Companion.SuppressCompletionAcknowledgement = false, DispatcherPriority.ContextIdle); }
            finally { _capturing = false; }
        }
        if (!_disposed && result?.Output == CaptureOutput.Pin && stagedPin is not null) stagedPin.Show();
    }
    public CapturePinWindow PinCapture(BitmapSource image, bool show = true, Action<BitmapSource>? copy = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CaptureRaster.ValidateSize(image.PixelWidth, image.PixelHeight);
        if (_pins.Count >= 8 || _pins.Sum(p => (long)p.Snapshot.PixelWidth * p.Snapshot.PixelHeight) + (long)image.PixelWidth * image.PixelHeight > 48_000_000)
            throw new InvalidOperationException("贴图已达上限（8 张或合计 4800 万像素），请先关闭部分贴图。");
        if (!image.IsFrozen) image = CaptureRaster.Crop(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight));
        var pin = new CapturePinWindow(image, ++_pinSequence, copy);
        pin.Closed += (_, _) => { _pins.Remove(pin); Changed?.Invoke(); };
        _pins.Add(pin);
        try { if (show) pin.Show(); Changed?.Invoke(); return pin; }
        catch { _pins.Remove(pin); pin.Close(); throw; }
    }
    private IEnumerable<Window> CaptureWindows()
    {
        foreach (var window in Windows) yield return window;
        foreach (var window in Companion.CaptureWindows) yield return window;
        foreach (var pin in _pins) yield return pin;
        if (_manager is not null) yield return _manager;
        if (_diagnosticsWindow is not null) yield return _diagnosticsWindow;
    }
    public void PastePin()
    {
        var image = Clipboard.GetImage() ?? throw new InvalidOperationException("剪贴板中没有图片。");
        PinCapture(CaptureRaster.Crop(image, new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight)));
    }
    public void ShowPins() { foreach (var pin in _pins) { pin.Show(); NativeMethods.EnsureWindowInWorkArea(pin); } }
    public void HidePins() { foreach (var pin in _pins) pin.Hide(); }
    public void ClosePins() { foreach (var pin in _pins.ToArray()) pin.Close(); }
    private void DisposeCapture()
    {
        _captureLifetime.Cancel(); _selection?.Dispose(); ClosePins(); _captureLifetime.Dispose();
    }
}
