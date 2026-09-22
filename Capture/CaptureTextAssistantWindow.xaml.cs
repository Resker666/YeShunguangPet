using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

public partial class CaptureTextAssistantWindow : ThemedWindow
{
    private readonly BitmapSource _image;
    private readonly IOcrService _ocr;
    private readonly ICaptureTextProcessor? _textProcessor;
    private CancellationTokenSource? _operation;
    private bool _closed;

    public CaptureTextAssistantWindow(BitmapSource image, IOcrService? ocr = null, ICaptureTextProcessor? textProcessor = null)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image)); _ocr = ocr ?? new WindowsOcrService(); _textProcessor = textProcessor;
        InitializeComponent();
        Loaded += async (_, _) => await RecognizeAsync();
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); };
    }

    private async void Recognize_Click(object sender, RoutedEventArgs e) => await RecognizeAsync();
    private async Task RecognizeAsync()
    {
        if (_operation is not null || _closed) return;
        using var cancellation = new CancellationTokenSource(); _operation = cancellation; SetBusy(true); StatusText.Text = "正在本地识别…";
        try
        {
            var result = await _ocr.RecognizeAsync(_image, cancellation.Token);
            if (_closed) return;
            SourceText.Text = result.Text; ResultText.Clear();
            StatusText.Text = result.Text.Length == 0 ? $"未识别到文字 · {result.Language}" : $"本地识别完成 · {result.Language}";
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "识别已停止。"; }
        catch (Exception ex) { if (!_closed) StatusText.Text = "识别失败：" + ex.Message; }
        finally { _operation = null; if (!_closed) SetBusy(false); }
    }

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunAiAsync();
    private async Task RunAiAsync()
    {
        if (_operation is not null || _closed) return;
        var source = SourceText.Text.Trim();
        if (source.Length == 0) { StatusText.Text = "没有可处理的文字。"; return; }
        if (source.Length > 12000) { StatusText.Text = "AI 处理文字最多 12000 个字符，请先精简原文。"; return; }
        using var cancellation = new CancellationTokenSource(); _operation = cancellation; SetBusy(true); ResultText.Clear();
        try
        {
            var processor = _textProcessor ?? throw new InvalidOperationException("请先在 AI 设置中启用“允许 AI 处理手动确认的 OCR 文字”。");
            var action = Enum.TryParse<CaptureTextAction>((ActionSelector.SelectedValue as string) ?? "Summary", out var parsed) ? parsed : CaptureTextAction.Summary;
            var request = new CaptureTextRequest(action, source, LanguageSelector.SelectedValue as string, RewriteSelector.SelectedValue as string);
            StatusText.Text = "正在处理…仅发送上方文字";
            var builder = new StringBuilder();
            await foreach (var delta in processor.ProcessAsync(request, cancellation.Token))
            {
                builder.Append(delta); if (builder.Length > 20000) throw new InvalidDataException("AI 结果过长。");
                ResultText.Text = builder.ToString(); ResultText.ScrollToEnd();
            }
            if (string.IsNullOrWhiteSpace(ResultText.Text)) throw new InvalidDataException("AI 返回内容为空。");
            StatusText.Text = "处理完成，结果未自动保存。";
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "处理已停止，结果未保存。"; }
        catch (Exception ex) { if (!_closed) StatusText.Text = "处理失败：" + ex.Message; }
        finally { _operation = null; if (!_closed) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        RecognizeButton.IsEnabled = RunButton.IsEnabled = !busy; SourceText.IsReadOnly = busy; StopButton.IsEnabled = busy;
        ActionSelector.IsEnabled = LanguageSelector.IsEnabled = RewriteSelector.IsEnabled = !busy;
    }
    private void Stop_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Action_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageSelector is null || RewriteSelector is null) return;
        var action = ActionSelector.SelectedValue as string;
        LanguageSelector.Visibility = action == "Translate" ? Visibility.Visible : Visibility.Collapsed;
        RewriteSelector.Visibility = action == "Rewrite" ? Visibility.Visible : Visibility.Collapsed;
    }
    private void CopySource_Click(object sender, RoutedEventArgs e) => Copy(SourceText.Text);
    private void CopyResult_Click(object sender, RoutedEventArgs e) => Copy(ResultText.Text);
    private void Copy(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try { Clipboard.SetText(text); StatusText.Text = "已复制。"; }
        catch (Exception ex) { StatusText.Text = "复制失败：" + ex.Message; }
    }
}
