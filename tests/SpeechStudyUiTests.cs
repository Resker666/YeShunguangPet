using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class SpeechStudyUiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(Action<bool, string> check, PetCatalog source, string renders)
    {
        var clock = new SpeechStudyTests.ManualClock();
        var history = new StudyHistory();
        for (var i = 0; i < 7; i++) history.Record(new FocusCompletion(Guid.NewGuid().ToString("N"), clock.GetUtcNow().AddDays(-i), (i % 3 + 1) * 1500));
        var config = DesktopConfiguration.Migrate(new PetSettings { Left = 80, Top = 220, Scale = 0.7, Topmost = false, ClickThrough = true, LookAtMouse = false, RandomIdleActions = false, FocusMinutes = 1, BreakMinutes = 1 });
        config.Speech.CooldownSeconds = 5;
        using var desktop = new DesktopSession(config, new PetCatalog(source.BundledDirectory, Path.Combine(Path.GetFullPath(renders), "speech-test-users")), _ => { }, false, clock, history);
        desktop.Start(false);
        var pet = desktop.Windows.Single();
        pet.Show();
        Wait(100);
        var native = typeof(MainWindow).Assembly.GetType("YeShunguangPet.NativeMethods")!;
        Rect Bounds(Window w)
        {
            var args = new object[] { w, Rect.Empty };
            native.GetMethod("TryGetWindowBounds", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
            return (Rect)args[1];
        }
        var speech = typeof(DesktopSession).GetField("_speech", Private)!.GetValue(desktop)!;
        SpeechBubbleWindow? Bubble() => (SpeechBubbleWindow?)speech.GetType().GetField("_window", Private)!.GetValue(speech);
        void Speak(SpeechEvent trigger) => typeof(DesktopSession).GetMethod("Speak", Private)!.Invoke(desktop, new object[] { pet, trigger });
        foreach (var theme in new[] { "light", "dark" })
        {
            desktop.UpdateAppearance(new AppearanceOptions { Theme = theme, ReduceMotion = true });
            clock.Advance(7);
            var size = new Size(pet.Width, pet.Height);
            var foreground = GetForegroundWindow();
            Speak(SpeechEvent.Click);
            Wait(100);
            var bubble = Bubble();
            check(bubble is { IsVisible: true } && bubble.Message.Length > 0, "native speech bubble displays a real offline line");
            check(new Size(pet.Width, pet.Height) == size && !Bounds(pet).IntersectsWith(Bounds(bubble!)), "speech overlay never resizes or obscures its pet");
            check(GetForegroundWindow() == foreground && !bubble!.ShowActivated && !bubble.IsHitTestVisible, "speech bubble does not steal focus or mouse input");
            var style = (IntPtr)native.GetMethod("GetWindowLongPtr", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { new WindowInteropHelper(bubble!).Handle, -20 })!;
            check((style.ToInt64() & 0x08000020L) == 0x08000020L, "native speech overlay has no-activate and click-through styles");
            Render(bubble!, renders, "speech-bubble-" + theme + ".png");
            pet.Left += 10;
            check(Bubble() is null, "moving the pet dismisses its speech immediately");
            var editor = new SpeechSettingsWindow(desktop, pet.Package);
            try
            {
                ShowOffscreen(editor);
                ((TextBox)editor.FindName("ClickInput")).Text = "个人点击台词";
                ((TextBox)editor.FindName("DragInput")).Text = "个人拖动台词";
                check((bool)typeof(SpeechSettingsWindow).GetMethod("SaveOptions", Private)!.Invoke(editor, null)! && config.Speech.Overrides[pet.Package.Manifest.Id].Click[0] == "个人点击台词", "speech editor saves per-skin personal lines without touching the manifest");
                ((TextBox)editor.FindName("ClickInput")).Text = new string('x', 81);
                check(!(bool)typeof(SpeechSettingsWindow).GetMethod("SaveOptions", Private)!.Invoke(editor, null)! && config.Speech.Overrides[pet.Package.Manifest.Id].Click[0] == "个人点击台词", "invalid speech draft cannot overwrite previously saved lines");
                ((TextBox)editor.FindName("ClickInput")).Text = "个人点击台词";
                typeof(SpeechSettingsWindow).GetMethod("SaveOptions", Private)!.Invoke(editor, null);
                Render(editor, renders, "speech-settings-" + theme + ".png");
            }
            finally { editor.Close(); }
            var stats = new StudyWindow(desktop.Companion);
            try
            {
                ShowOffscreen(stats);
                check(((ListView)stats.FindName("RecordList")).Items.Count == 7, "study window lists persisted completion snapshots");
                ((ComboBox)stats.FindName("RangeChoice")).SelectedIndex = 1;
                check(((ListView)stats.FindName("RecordList")).Items.Count == 1, "today filter uses recorded local completion dates");
                ((ComboBox)stats.FindName("RangeChoice")).SelectedIndex = 0;
                ((TextBox)stats.FindName("GoalInput")).Text = "90";
                ((Button)stats.FindName("SaveGoalButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(history.DailyGoalMinutes == 90, "study goal control persists a valid target");
                Render(stats, renders, "study-history-" + theme + ".png");
                stats.Width = 640;
                stats.Height = 550;
                Wait(100);
                Render(stats, renders, "study-history-compact-" + theme + ".png");
            }
            finally { stats.Close(); }
        }
        clock.Advance(7);
        Speak(SpeechEvent.Click);
        check(Bubble()?.Message == "个人点击台词", "runtime speech uses the saved personal skin override");
        desktop.SetQuiet(true);
        Wait(150);
        check(Bubble() is null, "quiet mode dismisses an existing speech bubble");
        desktop.SetQuiet(false);
        clock.Advance(7);
        pet.HideInstance();
        Speak(SpeechEvent.Click);
        check(Bubble() is null, "hidden roles cannot display speech bubbles");
        pet.ShowAndActivate();
        var previousCount = history.Records.Count;
        desktop.Companion.Session.StartOrResume();
        clock.Advance(60);
        desktop.Companion.Tick();
        Wait(80);
        check(history.Records.Count == previousCount + 1 && Bubble() is not null, "completed focus records history and emits one eligible companion bubble");
        clock.Advance(7);
        Wait(150);
        check(Bubble() is null, "speech bubble expires without a persistent animation timer");
        var focus = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, pet.Package, () => false, desktop.Companion);
        try { ShowOffscreen(focus); check(((TextBlock)focus.FindName("TodaySummary")).Text.Contains("26"), "focus panel exposes current-day durable study total"); Render(focus, renders, "focus-with-history.png"); }
        finally { focus.Close(); }
        desktop.Dispose();
        check(Bubble() is null, "desktop shutdown removes the speech overlay");
    }
    private static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = window.Top = -30000;
        window.ShowActivated = window.ShowInTaskbar = false;
        window.Show();
        Wait(100);
    }
    private static void Wait(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
    private static void Render(Window window, string folder, string name)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right), (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(background);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(folder, name));
        encoder.Save(output);
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
