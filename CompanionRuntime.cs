using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public sealed class CompanionRuntime : IDisposable
{
    private readonly CompanionService _service;
    public PetSettings Settings => _service.Settings;
    public CompanionSession Session => _service.Session;
    public StudyHistory History => _service.History;
    public FocusWindowOptions WindowOptions => _service.WindowOptions;
    public DateOnly Today => _service.Today;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private FocusWindow? _window;
    private StudyWindow? _studyWindow;
    private string? _avatarInstance;
    private Func<AiOptions>? _aiOptions;
    private bool _disposed;
    public bool IsQuiet => _service.IsQuiet;
    public event Action? Pulse { add => _service.Pulse += value; remove => _service.Pulse -= value; }
    public event Action<string, string>? Notification { add => _service.Notification += value; remove => _service.Notification -= value; }
    public event Action? BreakReminderDue { add => _service.BreakReminderDue += value; remove => _service.BreakReminderDue -= value; }
    public event Action? DurationsChanged { add => _service.DurationsChanged += value; remove => _service.DurationsChanged -= value; }
    public int FocusWindowCount => _window is null ? 0 : 1;
    public SessionPhase? PendingCompletion => _service.PendingCompletion;
    public event Action? CompletionNoticeChanged { add => _service.CompletionNoticeChanged += value; remove => _service.CompletionNoticeChanged -= value; }
    internal bool SuppressCompletionAcknowledgement { get; set; }
    public void AcknowledgeCompletion() { if (!SuppressCompletionAcknowledgement) _service.AcknowledgeCompletion(); }

    public CompanionRuntime(PetSettings settings, TimeProvider? clock = null, StudyHistory? history = null, Action<PetSettings>? persistDurations = null,
        FocusWindowOptions? windowOptions = null, Action<FocusWindowOptions>? persistWindow = null)
    {
        _service = new CompanionService(settings, clock, history, persistDurations, windowOptions, persistWindow);
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() { Configure(); _timer.Start(); }
    public void Configure() => _service.Configure();
    public void SetDurations(int focusMinutes, int breakMinutes) => _service.SetDurations(focusMinutes, breakMinutes);
    public void Tick() => _service.Tick();
    public void SaveWindowOptions(FocusWindowOptions options) => _service.SaveWindowOptions(options);
    internal void ConfigureAi(Func<AiOptions> options) => _aiOptions = options;
    internal AiOptions? AiConfiguration => _aiOptions?.Invoke();
    internal IAiProvider? CreateAiProvider() => _aiOptions is null ? null : AiProviderFactory.Create(_aiOptions(), new AiSecretStore());

    public void Open(PetPackage pet, string? instanceId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        AcknowledgeCompletion();
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
    internal IEnumerable<Window> CaptureWindows
    {
        get { if (_window is not null) yield return _window; if (_studyWindow is not null) yield return _studyWindow; }
    }
    internal void ToggleFromShortcut()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window is not null) { _window.ToggleFromShortcut(); return; }
        using var controller = new FocusPanelController(Session, Settings, SetDurations);
        if (!controller.Toggle()) throw new InvalidOperationException(controller.Error);
    }
    internal void ToggleMiniFromShortcut(bool ensureMini = false)
    {
        if (_disposed || _window is null) return;
        if (!_window.SetMiniMode(ensureMini || !_window.IsMiniMode)) throw new InvalidOperationException("无法切换面板，请检查分钟输入和窗口设置。");
        _window.Activate();
    }
    internal bool IsDisposed => _disposed;
    internal void FlushDurationEdits() => _window?.CommitPendingDurations();
    internal void FlushWindowPlacement() => _window?.SaveWindowPlacement();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _service.Dispose();
        _window?.Close();
        _studyWindow?.Close();
    }
}
