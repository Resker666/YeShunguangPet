using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class ShortcutUiTests
{
    private static object? Call(object target, string method, params object[] args) => ShortcutTests.Call(target, method, args);
    private static T Find<T>(Window window, string name) => (T)window.FindName(name);
    private static ShortcutSettingsWindow.ShortcutRow[] Rows(ShortcutSettingsWindow window) => Find<ItemsControl>(window, "ShortcutRows").Items.Cast<ShortcutSettingsWindow.ShortcutRow>().ToArray();
    private static T RowControl<T>(ShortcutSettingsWindow window, int index, string name)
    {
        var items = Find<ItemsControl>(window, "ShortcutRows");
        var presenter = (ContentPresenter)items.ItemContainerGenerator.ContainerFromIndex(index);
        return (T)presenter.ContentTemplate.FindName(name, presenter);
    }
    private static FocusWindow? Panel(DesktopSession desktop) => (FocusWindow?)typeof(CompanionRuntime).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(desktop.Companion);
    public static void RunLive(Action<bool, string> check, PetCatalog catalog, string renders)
    {
        var native = new ShortcutTests.FakeRegistrar(); var writes = 0; var fail = false;
        var clock = new SpeechStudyTests.ManualClock();
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings { Topmost = false, RandomIdleActions = false, LookAtMouse = false }), catalog,
            _ => { if (fail) throw new IOException("模拟保存失败"); writes++; }, false, clock, shortcutRegistrar: native);
        desktop.Start(false);
        foreach (var theme in new[] { "light", "dark" })
        {
            desktop.UpdateAppearance(new AppearanceOptions { Theme = theme, Accent = "#24796E", ReduceMotion = true });
            desktop.UpdateShortcuts(new ShortcutOptions());
            var window = new ShortcutSettingsWindow(desktop);
            try
            {
                Show(window);
                check(window.FontFamily.Source == "Segoe UI, Microsoft YaHei UI" && Rows(window).Length == 4, "shortcut editor shares the application font and offers four actions");
                check(Find<TextBlock>(window, "StatusText").Text.Length == 0, "opening shortcut settings does not mark an untouched draft as changed");
                foreach (var size in new[] { new Size(580, 650), new Size(480, 460) })
                {
                    window.Width = size.Width; window.Height = size.Height; Wait(60);
                    var root = (FrameworkElement)window.Content;
                    foreach (var name in new[] { "DefaultsButton", "CancelButton", "SaveButton" })
                        check(Inside(Find<FrameworkElement>(window, name), root), $"shortcut command {name} fits at {size} {theme}");
                    var scroll = Find<ScrollViewer>(window, "ShortcutScroll");
                    check(scroll.ScrollableWidth == 0, "shortcut modifier and key fields never require horizontal scrolling");
                    if (size.Height == 650) check(scroll.ScrollableHeight == 0, "default shortcut window shows all four actions without scrolling");
                    scroll.ScrollToBottom(); Wait(50);
                    check(Inside(RowControl<CheckBox>(window, 3, "EnabledCheck"), scroll), "last shortcut can be reached in the compact editor");
                    scroll.ScrollToTop(); Wait(40);
                    Render(window, renders, $"shortcuts-{theme}-{size.Width}x{size.Height}.png");
                }
                var before = writes;
                RowControl<CheckBox>(window, 1, "EnabledCheck").IsChecked = true;
                RowControl<ComboBox>(window, 1, "KeyChoice").SelectedValue = 0x59u;
                check(!(bool)Call(window, "SaveOptions")! && writes == before && Find<TextBlock>(window, "StatusText").Text.Contains("重复"), "duplicate draft displays an error without saving or unregistering the old key");
                RowControl<ComboBox>(window, 1, "KeyChoice").SelectedValue = 0x46u;
                native.Blocked.Add(new(3, 0x46));
                check(!(bool)Call(window, "SaveOptions")! && writes == before && desktop.HotkeyRegistered, "occupied shortcut error leaves the previous recall key usable");
                native.Blocked.Clear(); fail = true;
                check(!(bool)Call(window, "SaveOptions")! && desktop.Configuration.Shortcuts.OpenFocus is null && native.Active.Count == 1, "disk-error feedback preserves old settings and native registrations");
                fail = false;
                check((bool)Call(window, "SaveOptions")! && desktop.Configuration.Shortcuts.OpenFocus == new ShortcutGesture(3, 0x46), "editing the actual combo controls persists a valid shortcut");
                Find<Button>(window, "DefaultsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!Rows(window)[1].Enabled && desktop.Configuration.Shortcuts.OpenFocus is not null, "restore-defaults is a draft until Save is pressed");
                check((bool)Call(window, "SaveOptions")! && desktop.Configuration.Shortcuts.Active().Count() == 1, "saving defaults restores only Ctrl+Alt+Y");
                var button = Find<Button>(window, "DefaultsButton");
                var oldSize = button.RenderSize; window.Activate(); button.Focus(); Wait(60);
                check(((Border)button.Template.FindName("FocusOutline", button)).IsVisible && button.RenderSize == oldSize, "shared keyboard focus outline is visible without resizing its button");
                Render(window, renders, $"shortcuts-keyboard-focus-{theme}.png");
                Rows(window)[0].Enabled = false;
            }
            finally { fail = false; window.Close(); }
            check(desktop.HotkeyRegistered && desktop.Configuration.Shortcuts.RecallAll is not null, "closing an edited shortcut window discards its unsaved draft");
        }
        VerifyCommands(check, desktop, native, clock);
        var options = new ShortcutOptions { RecallAll = new(7, 0x7A), OpenFocus = new(6, 0x46) };
        using var tray = new TrayMenu(desktop);
        desktop.UpdateShortcuts(options);
        check(((System.Windows.Forms.ToolStripMenuItem)tray.Items.Find("recallAll", false).Single()).ShortcutKeyDisplayString == "Ctrl+Alt+Shift+F11", "already-created tray reflects the saved shortcut hint");
        var pet = desktop.Windows.First(); Call(pet, "BuildWindowContextMenu"); Call(pet, "UpdateMenuChecks");
        check(PetContextMenu.Descendants(pet.ContextMenu).Single(x => Equals(x.Header, "专注计时")).InputGestureText == "Ctrl+Shift+F", "desktop menu uses the configured focus shortcut hint");
        desktop.UpdateShortcuts(new ShortcutOptions { RecallAll = null }); Call(pet, "UpdateMenuChecks");
        check(PetContextMenu.Descendants(pet.ContextMenu).Single(x => Equals(x.Header, "召回主屏幕")).InputGestureText.Length == 0, "disabled shortcuts disappear from menu hints without removing commands");
    }

    private static void VerifyCommands(Action<bool, string> check, DesktopSession desktop, ShortcutTests.FakeRegistrar native, SpeechStudyTests.ManualClock clock)
    {
        var options = new ShortcutOptions { ToggleFocus = new(3, 0x20), OpenFocus = new(3, 0x46), ToggleMini = new(3, 0x4D) };
        desktop.UpdateShortcuts(options);
        bool Queue(ShortcutGesture gesture)
        {
            var id = native.Active.Single(x => x.Value == gesture).Key;
            return (bool)Call(desktop, "QueueShortcut", id, new IntPtr(((long)gesture.Key << 16) | gesture.Modifiers))!;
        }
        check(Queue(options.ToggleFocus!), "registered native message is queued through the desktop dispatcher");
        options.ToggleFocus = new(6, 0x20); desktop.UpdateShortcuts(options); Wait(60);
        check(desktop.Companion.Session.Status == SessionStatus.Ready, "queued shortcut from an older configuration cannot execute after rebinding");
        Queue(options.ToggleFocus); Wait(60);
        check(desktop.Companion.Session.IsFocusing && desktop.Companion.FocusWindowCount == 0, "start shortcut operates the shared timer without opening a window");
        clock.Advance(10); Call(desktop, "ExecuteShortcut", ShortcutAction.ToggleFocus);
        var paused = desktop.Companion.Session.Remaining; clock.Advance(30);
        check(desktop.Companion.Session.Status == SessionStatus.Paused && desktop.Companion.Session.Remaining == paused, "pause shortcut preserves remaining time without a panel");
        Call(desktop, "ExecuteShortcut", ShortcutAction.ToggleMini); Wait(100);
        var focus = Panel(desktop)!;
        focus.Left = focus.Top = -30000;
        check(focus.WindowStyle == WindowStyle.None && desktop.Companion.Session.Remaining == paused, "mini shortcut opens one mini panel without restarting paused focus");
        Call(desktop, "ExecuteShortcut", ShortcutAction.ToggleMini); Wait(70);
        check(ReferenceEquals(focus, Panel(desktop)) && focus.WindowStyle == WindowStyle.SingleBorderWindow, "mini shortcut toggles the same existing window");
        desktop.Companion.Session.Reset(); focus.Controller.BeginMinuteEdit(); Find<TextBox>(focus, "MinutesInput").Text = "invalid";
        var rejected = false;
        try { Call(desktop, "ExecuteShortcut", ShortcutAction.ToggleFocus); } catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
        check(rejected && desktop.Companion.Session.Status == SessionStatus.Ready, "shortcut cannot bypass invalid minute input in the open timer");
        focus.Controller.CancelMinuteEdit();
        Call(desktop, "BeginSettings", desktop.Windows.First());
        Queue(options.ToggleFocus); Call(desktop, "EndSettings"); Wait(60);
        check(desktop.Companion.Session.Status == SessionStatus.Ready, "shortcut pressed during role settings stays suppressed after that dialog closes");
        Call(desktop, "ExecuteShortcut", ShortcutAction.ToggleMini); Wait(70); focus.Close();
        Call(desktop, "ExecuteShortcut", ShortcutAction.ToggleMini); Wait(70);
        check(Panel(desktop)!.WindowStyle == WindowStyle.None, "mini shortcut reopens directly in mini mode even when the saved mode was already mini");
        Panel(desktop)!.Close();
        var seen = false; Exception? failure = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<ShortcutSettingsWindow>().FirstOrDefault(x => x.IsVisible);
            if (dialog is null) return;
            timer.Stop(); seen = true;
            try
            {
                Queue(options.ToggleFocus);
                Call(desktop, "ExecuteShortcut", ShortcutAction.OpenFocus);
                check(desktop.Companion.FocusWindowCount == 0, "shortcut settings modal cannot trigger another panel");
            }
            catch (Exception ex) { failure = ex; }
            finally { dialog.DialogResult = false; }
        };
        timer.Start(); try { desktop.OpenShortcutSettings(); } finally { timer.Stop(); }
        if (failure is not null) throw failure;
        Wait(60); check(seen && desktop.Companion.Session.Status == SessionStatus.Ready, "modal cancellation does not replay suppressed hotkeys");
    }
    private static void Show(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = window.Top = -30000;
        window.ShowActivated = window.ShowInTaskbar = false; window.Show(); Wait(100);
    }
    private static bool Inside(FrameworkElement element, FrameworkElement root)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1;
    }
    private static void Wait(int ms) => FocusVisualGate.Pump(ms);
    private static void Render(Window window, string output, string name)
    {
        var root = (FrameworkElement)window.Content; root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right),
            (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(root.Margin.Left, root.Margin.Top, root.ActualWidth, root.ActualHeight));
        }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(output); using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
    }
}
