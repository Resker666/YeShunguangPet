using System;
using System.Linq;
using System.Windows.Threading;

namespace YeShunguangPet;

internal sealed class DesktopSpeech : IDisposable
{
    private readonly DesktopSession _desktop;
    private readonly SpeechDirector _director;
    private readonly TimeProvider _clock;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private SpeechBubbleWindow? _window;
    private MainWindow? _owner;
    private long _shownAt;
    private bool _disposed;
    public DesktopSpeech(DesktopSession desktop, TimeProvider? clock)
    {
        _desktop = desktop;
        _clock = clock ?? TimeProvider.System;
        _director = new SpeechDirector(_clock);
        _timer.Tick += OnTick;
        _desktop.Companion.Session.Completed += OnCompleted;
    }
    public void Request(MainWindow owner, SpeechEvent trigger)
    {
        if (_disposed || !owner.CanShowSpeech || !_desktop.Windows.Contains(owner)) return;
        var text = _director.Select(_desktop.Configuration.Speech, owner.Package, trigger, _desktop.Companion.IsQuiet);
        if (text is null) return;
        Dismiss();
        try
        {
            _owner = owner;
            _window = new SpeechBubbleWindow(owner, text);
            if (!_window.ShowAtPet(owner)) { Dismiss(); return; }
            _shownAt = _clock.GetTimestamp();
            _timer.Start();
        }
        catch (Exception ex) { Dismiss(); AppLogger.Error("Could not show speech bubble.", ex); }
    }
    private void OnCompleted(SessionPhase phase)
    {
        if (!_desktop.Companion.Settings.NotificationsEnabled) return;
        var owner = _desktop.Windows.FirstOrDefault(w => w.InstanceId == _desktop.Companion.AvatarInstance && w.CanShowSpeech)
            ?? _desktop.Windows.FirstOrDefault(w => w.CanShowSpeech);
        if (owner is not null) Request(owner, phase == SessionPhase.Focus ? SpeechEvent.FocusCompleted : SpeechEvent.BreakCompleted);
    }
    private void OnTick(object? sender, EventArgs e)
    {
        if (_owner is null || !_owner.CanShowSpeech || !_desktop.Configuration.Speech.Enabled || _desktop.Companion.IsQuiet || _clock.GetElapsedTime(_shownAt).TotalSeconds >= 6) Dismiss();
    }
    public void Dismiss(MainWindow? owner = null)
    {
        if (owner is not null && owner != _owner) return;
        _timer.Stop();
        var window = _window;
        _window = null;
        _owner = null;
        try { window?.Close(); } catch (Exception ex) { AppLogger.Error("Could not close speech bubble.", ex); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _desktop.Companion.Session.Completed -= OnCompleted;
        _timer.Tick -= OnTick;
        Dismiss();
    }
}
