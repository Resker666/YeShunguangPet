using System;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public sealed class CompanionRuntime : IDisposable
{
    public PetSettings Settings { get; }
    public CompanionSession Session { get; }
    public StudyHistory History { get; }
    private readonly TimeProvider _clock;
    private readonly Action<PetSettings>? _persistDurations;
    private readonly Action<FocusWindowOptions>? _persistWindow;
    private FocusWindowOptions _windowOptions;
    public FocusWindowOptions WindowOptions => _windowOptions.Copy();
    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
    private readonly BreakReminder _reminder;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private FocusWindow? _window;
    private StudyWindow? _studyWindow;
    private string? _avatarInstance;
    private bool _disposed;
    public bool IsQuiet => DesktopBehavior.IsQuiet(Settings, DateTime.Now);
    public event Action? Pulse;
    public event Action<string, string>? Notification;
    public event Action? BreakReminderDue;
    public event Action? DurationsChanged;
    public int FocusWindowCount => _window is null ? 0 : 1;

    public CompanionRuntime(PetSettings settings, TimeProvider? clock = null, StudyHistory? history = null, Action<PetSettings>? persistDurations = null,
        FocusWindowOptions? windowOptions = null, Action<FocusWindowOptions>? persistWindow = null)
    {
        Settings = settings;
        _clock = clock ?? TimeProvider.System;
        _persistDurations = persistDurations;
        _persistWindow = persistWindow;
        _windowOptions = windowOptions?.Copy() ?? new FocusWindowOptions();
        _windowOptions.Normalize();
        History = history ?? new StudyHistory();
        Session = new CompanionSession(clock);
        _reminder = new BreakReminder(clock);
        Session.Completed += OnCompleted;
        Session.FocusCompleted += RecordFocus;
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() { Configure(); _timer.Start(); }
    public void Configure()
    {
        Session.Configure(Settings.FocusMinutes, Settings.BreakMinutes);
        _reminder.Configure(Settings.BreakRemindersEnabled, Settings.BreakReminderMinutes);
    }

    public void SetDurations(int focusMinutes, int breakMinutes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Session.Status is SessionStatus.Running or SessionStatus.Paused)
            throw new InvalidOperationException("请先结束当前计时，再调整时长。");
        if (focusMinutes is < 1 or > 120 || breakMinutes is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(focusMinutes), "专注时长为 1–120 分钟，休息时长为 1–60 分钟。");
        if (Settings.FocusMinutes == focusMinutes && Settings.BreakMinutes == breakMinutes) return;
        var draft = Settings.Clone();
        draft.FocusMinutes = focusMinutes;
        draft.BreakMinutes = breakMinutes;
        // Persist the candidate before changing the shared timer or live preferences.
        _persistDurations?.Invoke(draft);
        Settings.FocusMinutes = focusMinutes;
        Settings.BreakMinutes = breakMinutes;
        Session.Configure(focusMinutes, breakMinutes);
        DurationsChanged?.Invoke();
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

    public void SaveWindowOptions(FocusWindowOptions options)
    {
        if (_disposed) return;
        var draft = options.Copy();
        draft.Normalize();
        if (draft == _windowOptions) return;
        _persistWindow?.Invoke(draft);
        _windowOptions = draft;
    }

    private void OnCompleted(SessionPhase phase)
    {
        _reminder.Configure(Settings.BreakRemindersEnabled, Settings.BreakReminderMinutes);
        if (!Settings.NotificationsEnabled || IsQuiet) return;
        Notification?.Invoke(phase == SessionPhase.Focus ? "专注完成" : "休息结束", phase == SessionPhase.Focus
            ? $"完成一次专注，可以开始 {Settings.BreakMinutes} 分钟休息。" : "准备好了就开始下一次专注。");
    }

    private void RecordFocus(FocusCompletion completion) => History.Record(completion);

    public void Open(PetPackage pet, string? instanceId = null)
    {
        _avatarInstance = instanceId;
        if (_window is null)
        {
            _window = new FocusWindow(Session, Settings, pet, () => IsQuiet, this, managePlacement: true);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            _window.UpdatePet(pet);
            if (_window.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(_window);
            _window.Activate();
        }
    }

    public void UpdateAvatar(string instanceId, PetPackage pet)
    {
        if (_avatarInstance == instanceId) _window?.UpdatePet(pet);
    }

    public void OpenHistory(Window? owner = null)
    {
        if (_disposed) return;
        if (_studyWindow is null)
        {
            _studyWindow = new StudyWindow(this);
            if (owner is not null) _studyWindow.Owner = owner;
            _studyWindow.Closed += (_, _) => _studyWindow = null;
            _studyWindow.Show();
        }
        else
        {
            if (_studyWindow.WindowState == WindowState.Minimized) _studyWindow.WindowState = WindowState.Normal;
            _studyWindow.Activate();
        }
    }

    internal string? AvatarInstance => _avatarInstance;
    internal bool IsDisposed => _disposed;
    internal void FlushDurationEdits() => _window?.CommitPendingDurations();
    internal void FlushWindowPlacement() => _window?.SaveWindowPlacement();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Session.Completed -= OnCompleted;
        Session.FocusCompleted -= RecordFocus;
        _window?.Close();
        _studyWindow?.Close();
        Pulse = null;
        Notification = null;
        BreakReminderDue = null;
        DurationsChanged = null;
    }
}
