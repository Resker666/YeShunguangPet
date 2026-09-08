using System;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public sealed class CompanionRuntime : IDisposable
{
    public PetSettings Settings { get; }
    public CompanionSession Session { get; }
    private readonly BreakReminder _reminder;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private FocusWindow? _window;
    private string? _avatarInstance;
    private bool _disposed;
    public bool IsQuiet => DesktopBehavior.IsQuiet(Settings, DateTime.Now);
    public event Action? Pulse;
    public event Action<string, string>? Notification;
    public event Action? BreakReminderDue;
    public int FocusWindowCount => _window is null ? 0 : 1;

    public CompanionRuntime(PetSettings settings, TimeProvider? clock = null)
    {
        Settings = settings;
        Session = new CompanionSession(clock);
        _reminder = new BreakReminder(clock);
        Session.Completed += OnCompleted;
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() { Configure(); _timer.Start(); }
    public void Configure()
    {
        Session.Configure(Settings.FocusMinutes, Settings.BreakMinutes);
        _reminder.Configure(Settings.BreakRemindersEnabled, Settings.BreakReminderMinutes);
    }

    public void Tick()
    {
        if (_disposed) return;
        Session.Tick();
        Pulse?.Invoke();
        if (_reminder.Poll(IsQuiet || !Settings.NotificationsEnabled || Session.Status == SessionStatus.Running))
        {
            Notification?.Invoke("休息一下", "站起来活动，喝口水，放松一下眼睛。");
            BreakReminderDue?.Invoke();
        }
    }

    private void OnCompleted(SessionPhase phase)
    {
        _reminder.Configure(Settings.BreakRemindersEnabled, Settings.BreakReminderMinutes);
        if (!Settings.NotificationsEnabled || IsQuiet) return;
        Notification?.Invoke(phase == SessionPhase.Focus ? "专注完成" : "休息结束", phase == SessionPhase.Focus
            ? $"完成一次专注，可以开始 {Settings.BreakMinutes} 分钟休息。" : "准备好了就开始下一次专注。");
    }

    public void Open(PetPackage pet, string? instanceId = null)
    {
        _avatarInstance = instanceId;
        if (_window is null)
        {
            _window = new FocusWindow(Session, Settings, pet, () => IsQuiet);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            _window.UpdatePet(pet);
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }
    }

    public void UpdateAvatar(string instanceId, PetPackage pet)
    {
        if (_avatarInstance == instanceId) _window?.UpdatePet(pet);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Session.Completed -= OnCompleted;
        _window?.Close();
        Pulse = null;
        Notification = null;
        BreakReminderDue = null;
    }
}
