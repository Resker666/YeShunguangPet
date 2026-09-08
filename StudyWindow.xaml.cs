using System;
using System.Globalization;
using System.Linq;
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
}
