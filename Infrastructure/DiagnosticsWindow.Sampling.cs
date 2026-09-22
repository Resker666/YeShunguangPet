using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class DiagnosticsWindow
{
    private RuntimeSamplingSession? _sampling;
    private readonly DispatcherTimer _samplingTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _hasSample;
    private RuntimeCapture? SamplingCapture => _hasSample ? _sampling?.Capture() : null;
    private void InitializeSampling()
    {
        if (_desktop is not null) _sampling = new RuntimeSamplingSession(_desktop);
        SampleButton.IsEnabled = _sampling is not null;
        _samplingTimer.Tick += (_, _) => RefreshSampling();
    }
    private void Sample_Click(object sender, RoutedEventArgs e)
    {
        if (_sampling is null) return;
        if (_sampling.IsRecording) { _sampling.Stop(); _samplingTimer.Stop(); }
        else { _sampling.Start(); _hasSample = true; _samplingTimer.Start(); }
        RefreshSampling();
        UpdateSamplingReport();
    }
    private void ClearSample_Click(object sender, RoutedEventArgs e)
    {
        _samplingTimer.Stop(); _sampling?.Dispose();
        _sampling = _desktop is null ? null : new RuntimeSamplingSession(_desktop);
        _hasSample = false;
        RuntimeRoles.ItemsSource = RuntimeEvents.ItemsSource = null;
        SampleStatus.Text = "未采样"; SampleText.Text = "开始采样"; SampleIcon.Text = "\uE768";
        ClearSampleButton.IsEnabled = false;
        UpdateSamplingReport();
    }
    private void UpdateSamplingReport()
    {
        if (_lastLogs is null) return;
        _report = DiagnosticReport.Capture(_desktop, _lastLogs, activity: SamplingCapture);
        InformationText.Text = _report.InformationJson;
    }
    private void RefreshSampling()
    {
        if (_closed || _sampling is null) return;
        var capture = _sampling.Capture();
        if (!capture.Recording) _samplingTimer.Stop();
        SampleText.Text = capture.Recording ? "停止采样" : "开始采样";
        SampleIcon.Text = capture.Recording ? "\uE71A" : "\uE768";
        ClearSampleButton.IsEnabled = _hasSample;
        SampleStatus.Text = $"{(capture.Recording ? "采样中" : "已停止")} · {capture.Seconds:0.0} 秒 · {capture.Transitions.Length} 条变化";
        if (WindowState == WindowState.Minimized) return;
        RuntimeRoles.ItemsSource = capture.Roles.Select(role => new
        {
            Number = role.Role, Name = _sampling.LocalRoleName(role.Role), State = ActivityName(role.Activity), Animation = SettingsWindow.ActionName(role.Animation),
            Frame = role.Frame + 1, Timers = TimerName(role.Timers), Calls = $"{role.FrameCallbacks} / {role.OtherCallbacks}",
            P95 = role.FrameLatenessP95Ms.ToString("0.00"), Decision = DecisionName(role.Decision), Rule = role.RuleIndex < 0 ? "-" : (role.RuleIndex + 1).ToString()
        }).ToArray();
        RuntimeEvents.ItemsSource = capture.Transitions.Reverse().Select(item => new
        {
            Time = item.Seconds.ToString("0.000"), Number = item.Role, State = ActivityName(item.Activity),
            Animation = SettingsWindow.ActionName(item.Animation), Timers = TimerName(item.Timers), Decision = DecisionName(item.Decision)
        }).ToArray();
    }
    private void CloseSampling()
    {
        _samplingTimer.Stop(); _sampling?.Dispose(); _sampling = null;
        RuntimeRoles.ItemsSource = RuntimeEvents.ItemsSource = null;
    }
    private static string TimerName(RuntimeTimers timers)
    {
        if (timers == RuntimeTimers.None) return "无";
        var names = new[] { (RuntimeTimers.Frame, "帧"), (RuntimeTimers.Ambient, "自动"), (RuntimeTimers.Roam, "移动"), (RuntimeTimers.Dock, "贴边"), (RuntimeTimers.Click, "点击") };
        return string.Join(" · ", names.Where(item => (timers & item.Item1) != 0).Select(item => item.Item2));
    }
    private static string DecisionName(RuleDecision decision) => decision switch
    {
        RuleDecision.Selected => "规则命中", RuleDecision.Legacy => "默认选择", RuleDecision.Cooldown => "冷却中",
        RuleDecision.Condition => "条件未满足", RuleDecision.Disabled => "未启用", _ => "-"
    };
    private static string ActivityName(PetActivity activity) => activity switch
    {
        PetActivity.Inactive => "未加载", PetActivity.Hidden => "隐藏", PetActivity.Exited => "已退出", PetActivity.Dragging => "拖动",
        PetActivity.Settings => "设置", PetActivity.Menu => "菜单", PetActivity.Pointer => "输入处理中", PetActivity.Docked => "贴边",
        PetActivity.Roaming => "走动", PetActivity.RoamPaused => "走动暂停", PetActivity.Gesture => "动作", PetActivity.Session => "专注联动",
        PetActivity.Looking => "注视", _ => "待机"
    };
}
