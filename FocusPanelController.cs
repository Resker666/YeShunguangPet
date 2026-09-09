using System;
using System.Collections.Generic;
using System.Globalization;

namespace YeShunguangPet;

public readonly record struct FocusPanelSnapshot(SessionPhase Phase, SessionStatus Status, int RemainingSeconds, double RemainingRatio, int CompletedSessions)
{
    public bool CanEdit => Status == SessionStatus.Ready;
    public bool CanSwitchPhase => Status is SessionStatus.Ready or SessionStatus.Completed;
    public bool CanReset => Status != SessionStatus.Ready;
    public bool CanSkip => Phase == SessionPhase.Break || Status == SessionStatus.Completed;
    public string TimeText => $"{RemainingSeconds / 60:00}:{RemainingSeconds % 60:00}";
    public string PhaseText => Status switch
    {
        SessionStatus.Ready => "准备" + PhaseName,
        SessionStatus.Paused => PhaseName + "已暂停",
        SessionStatus.Completed => PhaseName + "完成",
        _ => PhaseName + "中"
    };
    public string ToggleText => Status switch
    {
        SessionStatus.Running => "暂停",
        SessionStatus.Paused => "继续",
        SessionStatus.Completed when Phase == SessionPhase.Focus => "开始休息",
        SessionStatus.Ready when Phase == SessionPhase.Break => "开始休息",
        _ => "开始专注"
    };
    private string PhaseName => Phase == SessionPhase.Focus ? "专注" : "休息";
}

public sealed class FocusPanelController : IDisposable
{
    private static readonly IReadOnlyList<int> FocusPresets = Array.AsReadOnly(new[] { 15, 25, 45, 60 });
    private static readonly IReadOnlyList<int> BreakPresets = Array.AsReadOnly(new[] { 5, 10, 15, 20 });
    private readonly PetSettings _settings;
    private readonly Action<int, int> _save;
    private readonly Func<bool> _ownerDisposed;
    private readonly Action<Exception>? _reportFailure;
    public CompanionSession Session { get; }
    public bool IsDisposed { get; private set; }
    public bool CanEdit => !IsDisposed && !_ownerDisposed() && Session.Status == SessionStatus.Ready;
    public bool IsEditingMinutes { get; private set; }
    public bool HasInvalidInput { get; private set; }
    public int DraftMinutes { get; private set; }
    public string MinuteText { get; private set; } = string.Empty;
    public string Error { get; private set; } = string.Empty;
    public int MaximumMinutes => Session.Phase == SessionPhase.Focus ? 120 : 60;
    public int ConfiguredMinutes => Session.Phase == SessionPhase.Focus ? _settings.FocusMinutes : _settings.BreakMinutes;
    public IReadOnlyList<int> Presets => Session.Phase == SessionPhase.Focus ? FocusPresets : BreakPresets;
    public event Action? Changed;

    public FocusPanelController(CompanionSession session, PetSettings settings, Action<int, int>? save = null,
        Func<bool>? ownerDisposed = null, Action<Exception>? reportFailure = null)
    {
        Session = session;
        _settings = settings;
        _save = save ?? SaveInMemory;
        _ownerDisposed = ownerDisposed ?? (() => false);
        _reportFailure = reportFailure;
        Synchronize();
        Session.Changed += Synchronize;
    }

    public FocusPanelSnapshot Snapshot()
    {
        var remaining = Session.Remaining;
        return new(Session.Phase, Session.Status, (int)Math.Ceiling(remaining.TotalSeconds),
            Session.Duration.TotalSeconds > 0 ? Math.Clamp(remaining.TotalSeconds / Session.Duration.TotalSeconds, 0, 1) : 0,
            Session.CompletedFocusSessions);
    }

    public void BeginMinuteEdit()
    {
        if (!CanEdit) return;
        IsEditingMinutes = true;
    }

    public void EditMinuteText(string text)
    {
        if (!CanEdit || (!IsEditingMinutes && !HasInvalidInput)) return;
        MinuteText = text;
    }

    public bool EndMinuteEdit()
    {
        var result = CommitMinutes();
        IsEditingMinutes = false;
        return result;
    }

    public void CancelMinuteEdit()
    {
        if (!CanEdit) return;
        Synchronize();
    }

    public void PreviewDuration(int minutes)
    {
        if (!CanEdit) return;
        DraftMinutes = Math.Clamp(minutes, 1, MaximumMinutes);
        MinuteText = DraftMinutes.ToString(CultureInfo.InvariantCulture);
        IsEditingMinutes = HasInvalidInput = false;
        Error = string.Empty;
        Changed?.Invoke();
    }

    public bool SelectPreset(int minutes)
    {
        if (!CanEdit || minutes < 1 || minutes > MaximumMinutes) return false;
        PreviewDuration(minutes);
        return SaveDuration();
    }

    public bool CommitMinutes()
    {
        if (!CanEdit) return !IsDisposed && !_ownerDisposed();
        if (!IsEditingMinutes && !HasInvalidInput) return Error.Length == 0;
        if (!int.TryParse(MinuteText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes < 1 || minutes > MaximumMinutes)
        {
            HasInvalidInput = true;
            Error = $"请输入 1–{MaximumMinutes} 之间的整数分钟。";
            Changed?.Invoke();
            return false;
        }
        IsEditingMinutes = HasInvalidInput = false;
        DraftMinutes = minutes;
        return SaveDuration();
    }

    public bool SaveDuration()
    {
        if (!CanEdit) return !IsDisposed && !_ownerDisposed();
        try
        {
            _save(Session.Phase == SessionPhase.Focus ? DraftMinutes : _settings.FocusMinutes,
                Session.Phase == SessionPhase.Break ? DraftMinutes : _settings.BreakMinutes);
            Error = string.Empty;
            HasInvalidInput = false;
            MinuteText = DraftMinutes.ToString(CultureInfo.InvariantCulture);
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            DraftMinutes = ConfiguredMinutes;
            MinuteText = DraftMinutes.ToString(CultureInfo.InvariantCulture);
            IsEditingMinutes = HasInvalidInput = false;
            Error = ex is InvalidOperationException ? ex.Message : "时长未保存，已恢复原值。请检查配置目录权限后重试。";
            _reportFailure?.Invoke(ex);
            Changed?.Invoke();
            return false;
        }
    }

    public bool Toggle()
    {
        if (IsDisposed || _ownerDisposed() || (CanEdit && (!CommitMinutes() || !SaveDuration()))) return false;
        if (Session.Status == SessionStatus.Running) Session.Pause(); else Session.StartOrResume();
        return true;
    }

    public bool SelectPhase(SessionPhase phase)
    {
        if (IsDisposed || _ownerDisposed() || !Snapshot().CanSwitchPhase) return false;
        if (CanEdit && (!CommitMinutes() || !SaveDuration())) return false;
        Session.PreparePhase(phase);
        return true;
    }

    public void Reset() { if (!IsDisposed && !_ownerDisposed()) Session.Reset(); }
    public void SkipBreak() { if (!IsDisposed && !_ownerDisposed()) Session.SkipBreak(); }

    private void SaveInMemory(int focus, int rest)
    {
        _settings.FocusMinutes = focus; _settings.BreakMinutes = rest;
        Session.Configure(focus, rest);
    }
    private void Synchronize()
    {
        IsEditingMinutes = HasInvalidInput = false;
        DraftMinutes = ConfiguredMinutes;
        MinuteText = DraftMinutes.ToString(CultureInfo.InvariantCulture);
        Error = string.Empty;
        Changed?.Invoke();
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Session.Changed -= Synchronize;
        Changed = null;
    }
}
