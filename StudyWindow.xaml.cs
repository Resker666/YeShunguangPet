using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace YeShunguangPet;

public partial class StudyWindow : ThemedWindow
{
    private readonly CompanionRuntime _runtime;
    private DateOnly _day;
    private bool _ready;
    public StudyWindow(CompanionRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();
        _ready = true;
        _runtime.History.Changed += Refresh;
        _runtime.Pulse += CheckDay;
        SizeChanged += (_, _) => WeekBars.Visibility = ActualHeight < 620 ? Visibility.Collapsed : Visibility.Visible;
        Closed += (_, _) => { _ready = false; _runtime.History.Changed -= Refresh; _runtime.Pulse -= CheckDay; };
        Refresh();
    }
    private void CheckDay() { if (_ready && _day != _runtime.Today) Refresh(); }
    private void Refresh()
    {
        if (!_ready) return;
        _day = _runtime.Today;
        var history = _runtime.History;
        var today = history.Totals(_day);
        var week = history.Week(_day);
        DateText.Text = _day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        TodayText.Text = $"{today.Minutes} 分钟";
        CountText.Text = $"{today.Sessions} 次";
        WeekText.Text = $"{week.Sum(d => d.Minutes)} 分钟";
        if (!GoalInput.IsKeyboardFocused) GoalInput.Text = history.DailyGoalMinutes.ToString(CultureInfo.InvariantCulture);
        GoalStatus.Text = today.Minutes >= history.DailyGoalMinutes ? $"今日目标已达成 · {today.Minutes}/{history.DailyGoalMinutes} 分钟" : $"{today.Minutes}/{history.DailyGoalMinutes} 分钟";
        GoalProgress.Value = Math.Min(1, (double)today.Minutes / history.DailyGoalMinutes);
        var maximum = Math.Max(1, week.Max(d => d.Minutes));
        WeekBars.ItemsSource = week.Select(d => new { d.Minutes, Label = d.Date.ToString("M/d", CultureInfo.InvariantCulture), Height = 76.0 * d.Minutes / maximum, Summary = $"{d.Date:yyyy-MM-dd} · {d.Minutes} 分钟 · {d.Sessions} 次" }).ToArray();
        var records = history.Records.Where(r => RangeChoice.SelectedIndex == 2 || r.Day >= (RangeChoice.SelectedIndex == 1 ? _day : _day.AddDays(-6)) && r.Day <= _day)
            .OrderByDescending(r => r.CompletedAt).Select(r => new { Date = r.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Time = r.CompletedAt.ToString("HH:mm", CultureInfo.InvariantCulture), Duration = $"{r.Minutes} 分钟" }).ToArray();
        RecordList.ItemsSource = records;
        EmptyText.Visibility = records.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.IsEnabled = history.LastError is not null || history.HasPendingChanges;
        StatusText.Text = (history.HasPendingChanges ? "有未保存记录。" : "") + history.LastError;
    }
    private void Range_Changed(object sender, SelectionChangedEventArgs e) => Refresh();
    private void SaveGoal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(GoalInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) throw new InvalidOperationException("每日目标需为 1-720 分钟。");
            _runtime.History.SetGoal(minutes);
            GoalInput.Text = _runtime.History.DailyGoalMinutes.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void Retry_Click(object sender, RoutedEventArgs e) { _runtime.History.RetrySave(); Refresh(); }
    private async void Summary_Click(object sender, RoutedEventArgs e)
    {
        var options = _runtime.AiConfiguration;
        if (options is null || !options.Enabled || !options.AllowStudySummaries)
        {
            StatusText.Text = "请先在 AI 设置中启用专注总结。"; return;
        }
        try
        {
            SummaryButton.IsEnabled = false; StatusText.Text = "正在生成总结…";
            var provider = _runtime.CreateAiProvider() ?? throw new InvalidOperationException("AI Provider 未启用。");
            var today = _runtime.History.Totals(_runtime.Today); var week = _runtime.History.Week(_runtime.Today);
            var prompt = new AiPrompt("你是离线专注助手。只返回 JSON：{\"text\":\"一段中文总结\",\"mood\":\"encourage\"}。不要给出医疗或职业诊断，不要输出 JSON 之外的内容。总结最多 600 个汉字。",
                $"日期：{_runtime.Today:yyyy-MM-dd}\n今日专注：{today.Minutes} 分钟，完成 {today.Sessions} 次\n近七天：{week.Sum(x => x.Minutes)} 分钟，完成 {week.Sum(x => x.Sessions)} 次\n请给出简短、具体、鼓励性的总结和一个明天可执行的小建议。");
            var suggestion = await new AiSuggestionService(provider).SuggestAsync(prompt, CancellationToken.None, 600);
            StatusText.Text = "AI 总结：" + suggestion.Text;
        }
        catch (Exception ex) { StatusText.Text = "AI 总结失败：" + ex.Message; }
        finally { SummaryButton.IsEnabled = true; }
    }
}
