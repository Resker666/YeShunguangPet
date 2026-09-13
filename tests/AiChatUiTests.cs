using System;
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
        var config = DesktopConfiguration.Migrate(new PetSettings());
        using var desktop = new DesktopSession(config, catalog, _ => { }, nativeIntegration: false);
        var pet = catalog.LoadPreferred(PetPackage.DefaultId, out _);
        var previous = UiTheme.Current;
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                UiTheme.Apply(new AppearanceOptions { Theme = theme });
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
        finally { UiTheme.Apply(previous); }
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
    private static void Render(Window window, string folder, string name)
    {
        const int width = 584, height = 641;
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
}
