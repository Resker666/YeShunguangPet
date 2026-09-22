using System;

namespace YeShunguangPet;

public sealed class CompanionService : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly Action<PetSettings>? _persistDurations;
    private readonly Action<FocusWindowOptions>? _persistWindow;
    private readonly BreakReminder _reminder;
    private FocusWindowOptions _windowOptions;
    public PetSettings Settings { get; }
    public CompanionSession Session { get; }
    public StudyHistory History { get; }
    public bool IsDisposed { get; private set; }
    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
    public bool IsQuiet => DesktopBehavior.IsQuiet(Settings, _clock.GetLocalNow().DateTime);
    public FocusWindowOptions WindowOptions => _windowOptions.Copy();
    public event Action? Pulse;
    public event Action<string, string>? Notification;
    public event Action? BreakReminderDue;
    public event Action? DurationsChanged;
    public SessionPhase? PendingCompletion { get; private set; }
    public event Action? CompletionNoticeChanged;

    public CompanionService(PetSettings settings, TimeProvider? clock = null, StudyHistory? history = null,
        Action<PetSettings>? persistDurations = null, FocusWindowOptions? windowOptions = null, Action<FocusWindowOptions>? persistWindow = null)
    {
        Settings = settings;
        _clock = clock ?? TimeProvider.System;
        _persistDurations = persistDurations;
        _persistWindow = persistWindow;
        _windowOptions = windowOptions?.Copy() ?? new FocusWindowOptions();
        _windowOptions.Normalize();
        History = history ?? new StudyHistory();
        Session = new CompanionSession(_clock);
        _reminder = new BreakReminder(_clock);
        Session.Completed += OnCompleted;
        Session.FocusCompleted += RecordFocus;
        Session.Changed += OnSessionChanged;
    }

    public void Configure()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Session.Configure(Settings.FocusMinutes, Settings.BreakMinutes);
        _reminder.Configure(Settings.BreakRemindersEnabled, Settings.BreakReminderMinutes);
        RefreshCompletionNotice();
    }

    public void SetDurations(int focusMinutes, int breakMinutes)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
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

    public void SaveWindowOptions(FocusWindowOptions options)
    {
        if (IsDisposed) return;
        var draft = options.Copy();
        draft.Normalize();
        if (draft == _windowOptions) return;
        _persistWindow?.Invoke(draft);
        _windowOptions = draft;
    }

    public void Tick()
    {
        if (IsDisposed) return;
        Session.Tick();
        RefreshCompletionNotice();
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
        PendingCompletion = phase;
        CompletionNoticeChanged?.Invoke();
        Notification?.Invoke(phase == SessionPhase.Focus ? "专注完成" : "休息结束", phase == SessionPhase.Focus
            ? $"完成一次专注，可以开始 {Settings.BreakMinutes} 分钟休息。" : "准备好了就开始下一次专注。");
    }

    private void RecordFocus(FocusCompletion completion) => History.Record(completion);
    private void OnSessionChanged() { if (Session.Status != SessionStatus.Completed) AcknowledgeCompletion(); RefreshCompletionNotice(); }
    private void RefreshCompletionNotice() { if (IsQuiet || !Settings.NotificationsEnabled) AcknowledgeCompletion(); }
    public void AcknowledgeCompletion()
    {
        if (PendingCompletion is null) return;
        PendingCompletion = null; CompletionNoticeChanged?.Invoke();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Session.Completed -= OnCompleted;
        Session.FocusCompleted -= RecordFocus;
        Session.Changed -= OnSessionChanged;
        PendingCompletion = null; CompletionNoticeChanged = null;
        Pulse = null; Notification = null; BreakReminderDue = null; DurationsChanged = null;
    }
}
