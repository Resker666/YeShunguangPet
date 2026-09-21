using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class AiChatUiTests
{
    public static void Run(Action<bool, string> check, PetCatalog catalog, string root, string? renders)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var config = DesktopConfiguration.Migrate(new PetSettings());
        using var desktop = new DesktopSession(config, catalog, _ => { }, nativeIntegration: false);
        var pet = catalog.LoadPreferred(PetPackage.DefaultId, out _);
        var previous = UiTheme.Current;
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                UiTheme.Apply(new AppearanceOptions { Theme = theme });
                VerifySettings(check, desktop, root, renders, theme);
                VerifyTextAssistant(check, root, renders, theme);
                var provider = new ReplyProvider();
                var window = new AiChatWindow(desktop, pet, new AiChatStore(Path.Combine(root, "chat-ui-" + theme)), () => provider);
                try
                {
                    ((TextBox)window.FindName("BackgroundInput")).Text = "云岿山的小剑客，喜欢练剑。";
                    Invoke(window, "SaveProfile_Click", window, new RoutedEventArgs());
                    ((TextBox)window.FindName("MessageInput")).Text = "今天有点累，陪我聊聊吧。";
                    Await((Task)Invoke(window, "SendAsync")!);
                    check(((TextBox)window.FindName("MessageInput")).Text == "" && provider.Last?.System.Contains("喜欢练剑") == true,
                        "chat UI sends saved persona and clears input only on success " + theme);
                    check(((ItemsControl)window.FindName("MessagesList")).Items.Count == 2,
                        "chat UI displays a complete exchange " + theme);
                    provider.Fail = true;
                    ((TextBox)window.FindName("MessageInput")).Text = "这条保留用于重试";
                    Await((Task)Invoke(window, "SendAsync")!);
                    check(((TextBox)window.FindName("MessageInput")).Text == "这条保留用于重试" &&
                          ((ItemsControl)window.FindName("MessagesList")).Items.Count == 2 &&
                          ((Button)window.FindName("SendButton")).IsEnabled,
                        "failed chat restores send controls and retains draft without polluting history " + theme);
                    if (renders is not null)
                    {
                        Render(window, renders, "chat-" + theme + ".png");
                        ((TabControl)window.FindName("ChatTabs")).SelectedIndex = 1;
                        Render(window, renders, "persona-" + theme + ".png");
                    }
                }
                finally { window.Close(); }
            }
        }
        finally { UiTheme.Apply(previous); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void VerifySettings(Action<bool, string> check, DesktopSession desktop, string root, string? renders, string theme)
    {
        var window = new AiSettingsWindow(desktop);
        try
        {
            Show(window); var profiles = (ComboBox)window.FindName("ProfileSelector");
            check(profiles.Items.Count == 1 && ((CheckBox)window.FindName("CaptureCheck")).IsChecked == false,
                "AI settings migrate one default profile and keep screenshot text sharing disabled " + theme);
            ((CheckBox)window.FindName("EnabledCheck")).IsChecked = true;
            Invoke(window, "AddProfile_Click", window, new RoutedEventArgs());
            check(profiles.Items.Count == 2 && ((Button)window.FindName("DeleteProfileButton")).IsEnabled,
                "AI settings can add an independently keyed provider profile " + theme);
            if (renders is not null)
            {
                Render(window, renders, "ai-settings-profiles-" + theme + ".png", 612, 681);
                var scroll = (ScrollViewer)window.FindName("SettingsScroll"); scroll.ScrollToBottom(); Await(Task.Delay(60));
                check(((CheckBox)window.FindName("CaptureCheck")).ActualHeight > 0, "screenshot AI permission remains reachable in AI settings " + theme);
                Render(window, renders, "ai-settings-permissions-" + theme + ".png", 612, 681);
            }
        }
        finally { window.Close(); }
    }
    private static void VerifyTextAssistant(Action<bool, string> check, string root, string? renders, string theme)
    {
        var provider = new AssistantProvider(); var ocr = new FakeOcr();
        var window = new CaptureTextAssistantWindow(CaptureTests.Fixture(480, 180), ocr, () => provider);
        try
        {
            Show(window); Await(Task.Delay(80));
            check(((TextBox)window.FindName("SourceText")).Text == "识别后的本地文字" && ocr.Calls == 1,
                "capture text assistant performs OCR locally on open " + theme);
            Await((Task)Invoke(window, "RunAiAsync")!);
            check(((TextBox)window.FindName("ResultText")).Text == "总结完成" && provider.Last?.User == "识别后的本地文字",
                "capture text assistant sends only confirmed OCR text and assembles streaming output " + theme);
            var action = (ComboBox)window.FindName("ActionSelector"); action.SelectedIndex = 1;
            check(((ComboBox)window.FindName("LanguageSelector")).Visibility == Visibility.Visible &&
                  ((ComboBox)window.FindName("RewriteSelector")).Visibility == Visibility.Collapsed,
                "capture text assistant shows only parameters for the selected AI action " + theme);
            action.SelectedIndex = 0;
            if (renders is not null) Render(window, renders, "capture-text-assistant-" + theme + ".png", 732, 681);
        }
        finally { window.Close(); }
    }
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Await(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var deadline = Environment.TickCount64 + 10000;
        timer.Tick += (_, _) => { if (task.IsCompleted || Environment.TickCount64 >= deadline) frame.Continue = false; };
        timer.Start();
        try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (!task.IsCompleted) throw new TimeoutException("Chat UI test timed out");
        task.GetAwaiter().GetResult();
    }
    private static void Show(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = window.Top = -30000;
        window.ShowActivated = window.ShowInTaskbar = false; window.Show(); window.UpdateLayout();
    }
    private static void Render(Window window, string folder, string name, int width = 584, int height = 641)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        image.Render(background);
        image.Render(content);
        Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
    }
    private sealed class ReplyProvider : IAiProvider
    {
        public string Name => "offline-test";
        public AiPrompt? Last;
        public bool Fail;
        public Task<string> CompleteAsync(AiPrompt prompt, CancellationToken token = default)
        {
            Last = prompt;
            if (Fail) throw new InvalidOperationException("模拟连接失败");
            return Task.FromResult("那就先歇一会儿吧。我陪着你，等你恢复精神，我们再一起出发。今天有什么想和我说的吗？");
        }
    }
    private sealed class FakeOcr : IOcrService
    {
        public int Calls;
        public Task<OcrText> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new OcrText("识别后的本地文字", "测试语言")); }
    }
    private sealed class AssistantProvider : IStreamingAiProvider
    {
        public string Name => "assistant-test"; public AiPrompt? Last;
        public Task<string> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public async IAsyncEnumerable<string> StreamAsync(AiPrompt prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        { Last = prompt; yield return "总结"; await Task.Yield(); yield return "完成"; }
    }
}
