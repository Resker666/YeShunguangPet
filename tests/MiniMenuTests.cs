using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class MiniMenuTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private)!.Invoke(owner, args);
    private static T Find<T>(Window window, string name) => (T)window.FindName(name);
    private static void Click(Window window, string name) => Find<ButtonBase>(window, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    public static void Run(Action<bool, string> check, PetCatalog catalog, string temporary)
    {
        var old = JsonSerializer.Deserialize<FocusWindowOptions>("{\"Width\":520,\"Height\":640,\"LeftPixels\":-1400,\"TopPixels\":20}")!;
        old.Normalize();
        check(!old.MiniMode && !old.MiniTopmost && old.MiniWidth == 320 && old.MiniHeight == 104 && old.Width == 520, "old full-only placement gets compact mini defaults without changing the full panel");
        var savedMini = JsonSerializer.Deserialize<FocusWindowOptions>("{\"MiniMode\":true,\"MiniTopmost\":true,\"MiniWidth\":320,\"MiniHeight\":128,\"MiniLeftPixels\":-1400,\"MiniTopPixels\":20}")!;
        var savedCopy = savedMini.Copy(); savedMini.Normalize();
        check(savedMini == savedCopy, "existing mini dimensions, negative position and pin are not overwritten by new defaults");
        var compactMini = savedMini with { MiniWidth = 280, MiniHeight = 104 };
        compactMini.Normalize();
        var compactRoundtrip = JsonSerializer.Deserialize<FocusWindowOptions>(JsonSerializer.Serialize(compactMini))!;
        compactRoundtrip.Normalize();
        check(compactRoundtrip == compactMini && compactRoundtrip.MiniHeight == 104, "new minimum mini height survives a settings roundtrip");
        var invalid = new FocusWindowOptions { MiniWidth = double.NaN, MiniHeight = 1, MiniLeftPixels = int.MaxValue, MiniTopPixels = 0 };
        invalid.Normalize();
        check(invalid.MiniWidth == 320 && invalid.MiniHeight == 104 && invalid.MiniLeftPixels is null, "invalid mini dimensions and coordinates normalize safely");
        invalid.MiniHeight = double.PositiveInfinity; invalid.Normalize();
        check(invalid.MiniHeight == 104, "nonfinite mini height falls back to the compact default");
        var config = DesktopConfiguration.Migrate(new PetSettings());
        config.FocusWindow = old;
        var store = new DesktopSettingsStore(Path.Combine(temporary, "mini-menu-desktop.json"));
        var fail = false;
        var clock = new SpeechStudyTests.ManualClock();
        using var desktop = new DesktopSession(config, catalog, c => { if (fail) throw new IOException("simulated mini-mode save failure"); store.Save(c); }, false, clock);
        desktop.Start(false);
        var pet = desktop.Windows.First();
        var window = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, pet.Package, () => false, desktop.Companion, managePlacement: true);
        try
        {
            new WindowInteropHelper(window).EnsureHandle();
            var fullSize = new Size(window.Width, window.Height);
            desktop.Companion.Session.StartOrResume(); clock.Advance(17);
            var remaining = desktop.Companion.Session.Remaining;
            check((bool)Call(window, "SetMiniMode", true)! && window.WindowStyle == WindowStyle.None && window.MinWidth == 280 && window.Width == 320 && window.MinHeight == 104 && window.Height == 104, "mini mode switches the same native window to a compact surface");
            check(desktop.Companion.Session.Remaining == remaining && desktop.Companion.Session.IsFocusing, "switching to mini mode does not restart active focus");
            check(store.Load().FocusWindow.MiniMode && store.Load().FocusWindow.Width == fullSize.Width, "mini mode persists without replacing full-window dimensions");
            Find<ToggleButton>(window, "MiniPinButton").IsChecked = true; Click(window, "MiniPinButton");
            check(window.Topmost && store.Load().FocusWindow.MiniTopmost, "mini pin persists and updates the native window");
            fail = true;
            check(!(bool)Call(window, "SetMiniMode", false)! && window.WindowStyle == WindowStyle.None && window.Topmost && store.Load().FocusWindow.MiniMode,
                "failed mode save leaves current mode and persisted state unchanged");
            check(Find<TextBlock>(window, "MiniTitleText").Text == "专注" && Find<TextBlock>(window, "MiniPhaseText").Text == "设置未保存" &&
                  Find<TextBlock>(window, "MiniPhaseText").ToolTip is string, "compact header preserves phase identity and exposes save failures");
            fail = false;
            check((bool)Call(window, "SetMiniMode", false)! && window.Width == fullSize.Width && window.Height == fullSize.Height && !window.Topmost,
                "expanding restores the full size without imposing mini topmost on the full panel");
            desktop.Companion.Session.Pause(); clock.Advance(90);
            var paused = desktop.Companion.Session.Remaining;
            for (var i = 0; i < 4; i++) { Call(window, "SetMiniMode", true); Call(window, "SetMiniMode", false); }
            check(desktop.Companion.Session.Remaining == paused && desktop.Companion.Session.Status == SessionStatus.Paused, "repeated mode switches preserve paused time");
            Call(window, "SetMiniMode", true);
        }
        finally { window.Close(); }
        check(desktop.Companion.Session.Status == SessionStatus.Paused, "closing mini mode leaves the shared timer alive");
        var reopened = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, pet.Package, () => false, desktop.Companion, managePlacement: true);
        try { check(reopened.WindowStyle == WindowStyle.None && reopened.Topmost && reopened.Width == 320 && reopened.Height == 104, "reopening restores compact mini dimensions and independent pin preference"); }
        finally { reopened.Close(); }
        Call(pet, "BuildWindowContextMenu"); Call(pet, "UpdateMenuChecks");
        var menu = pet.ContextMenu;
        var roots = menu.Items.OfType<MenuItem>().ToArray();
        var items = PetContextMenu.Descendants(menu).ToArray();
        check(roots.Any(i => Equals(i.Header, "专注计时")) && !roots.Any(i => Equals(i.Header, "学习陪伴")), "desktop menu offers focus timing without a study-only label");
        check(roots.Length <= 12 && roots.Any(i => Equals(i.Header, "聊天")) && roots.All(i => i.Tag is not PetState), "desktop root menu offers character chat and keeps animation choices in a submenu");
        check(items.Count(i => i.Tag is PetState) == 7 && items.Single(i => Equals(i.Header, "显示与行为")).Items.Count > 5, "all prior actions and display options remain available");
        check(roots.Any(i => Equals(i.Header, "查看角色介绍")) && roots.Any(i => Equals(i.Header, "关闭此角色...")), "read-only introduction and confirmed close are distinct root commands");
        check(items.Single(i => Equals(i.Header, "召回主屏幕")).InputGestureText == "Ctrl+Alt+Y", "grouping preserves the summon shortcut hint");
    }

    public static void RunLive(Action<bool, string> check, PetCatalog catalog, string renders)
    {
        var clock = new SpeechStudyTests.ManualClock();
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings { Topmost = false, LookAtMouse = false, RandomIdleActions = false }), catalog, _ => { }, false, clock);
        desktop.Start(false);
        desktop.Add(PetPackage.DefaultId, false);
        var pet = desktop.Windows.First();
        pet.ShowActivated = false;
        pet.Show(); Wait(100);
        foreach (var theme in new[] { "light", "dark" })
        {
            desktop.UpdateAppearance(new AppearanceOptions { Theme = theme, Accent = "#24796E", ReduceMotion = true });
            var focus = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, pet.Package, () => false, desktop.Companion);
            try
            {
                ShowOffscreen(focus); desktop.Companion.Session.Reset(); desktop.Companion.SetDurations(120, 60);
                var handle = new WindowInteropHelper(focus).Handle;
                Click(focus, "MiniModeButton"); Wait(120);
                check(new WindowInteropHelper(focus).Handle == handle && Find<Border>(focus, "MiniRoot").IsVisible, "mini mode reuses one native timer window in " + theme);
                check(Find<Image>(focus, "MiniPetImage").Source is BitmapSource { IsFrozen: true }, "mini mode retains the active character's real sprite");
                check(focus.FindName("MiniPetName") is null && focus.FindName("PetName") is null, "timer surfaces omit redundant character-name labels");
                var alternative = catalog.Scan().Pets.FirstOrDefault(p => p.Id != pet.Package.Manifest.Id);
                if (alternative is not null)
                {
                    var package = PetPackage.Load(alternative.ManifestPath);
                    focus.UpdatePet(package); Wait(40);
                    check(Equals(Find<Image>(focus, "MiniPetImage").ToolTip, package.Manifest.Name) &&
                          System.Windows.Automation.AutomationProperties.GetName(Find<Image>(focus, "MiniPetImage")) == package.Manifest.Name,
                        "changing the active skin updates mini avatar identity without adding visible text");
                    focus.UpdatePet(pet.Package); Wait(40);
                }
                foreach (var size in new[] { new Size(280, 104), new Size(320, 104), new Size(320, 128), new Size(480, 200) })
                {
                    focus.Width = size.Width; focus.Height = size.Height; Wait(70);
                    var time = Find<TextBlock>(focus, "MiniTimeText");
                    var label = $"{size.Width}x{size.Height}-{theme}";
                    check(IsInside(time, (FrameworkElement)focus.Content) && time.Text == "120:00" && TextFits(time), "maximum mini time is not clipped at " + label);
                    check(time.FontSize == 34 && time.FontWeight == FontWeights.Normal && time.FontFamily.Source == "Segoe UI" &&
                          time.Typography.NumeralAlignment == FontNumeralAlignment.Tabular && Find<Image>(focus, "MiniPetImage").ActualWidth == 20,
                        "mini timer uses readable regular tabular digits and a subtle header avatar at " + label);
                    foreach (var name in new[] { "MiniPinButton", "ExpandButton", "MiniCloseButton", "MiniToggleButton", "MiniResetButton", "MiniTitleText", "MiniPhaseText", "MiniProgress", "MiniPetImage" })
                        check(IsInside(Find<FrameworkElement>(focus, name), (FrameworkElement)focus.Content), name + " is visible at " + label);
                    var root = (FrameworkElement)focus.Content;
                    var title = Find<TextBlock>(focus, "MiniTitleText");
                    var phase = Find<TextBlock>(focus, "MiniPhaseText");
                    var avatar = Find<Image>(focus, "MiniPetImage");
                    check(title.FontSize == 15 && title.FontWeight == FontWeights.SemiBold && TextFits(title) &&
                          Math.Abs(Bounds(title, root).Left - Bounds(time, root).Left) < 1,
                        "semibold mini title and time share one left alignment at " + label);
                    check(Bounds(title, root).Right <= Bounds(avatar, root).Left && Bounds(avatar, root).Right <= Bounds(phase, root).Left &&
                          Bounds(avatar, root).Bottom <= Bounds(time, root).Top,
                        "mini avatar is beside the title without indenting or overlapping time at " + label);
                    check(TextFits(phase) && Bounds(phase, root).Right <= Bounds(Find<ToggleButton>(focus, "MiniPinButton"), root).Left &&
                          Bounds(phase, root).Bottom <= Bounds(time, root).Top &&
                          Bounds(time, root).Right <= Bounds(Find<Button>(focus, "MiniResetButton"), root).Left,
                        "mini status, time and controls do not overlap at " + label);
                    Render(root, focus.Background, renders, $"mini-{label}.png");
                }
                Click(focus, "MiniToggleButton"); clock.Advance(31); Wait(300);
                check(desktop.Companion.Session.IsFocusing && Find<TextBlock>(focus, "MiniTimeText").Text == "119:29", "mini start uses the shared duration and live remaining time");
                Click(focus, "MiniToggleButton");
                var remaining = desktop.Companion.Session.Remaining;
                Click(focus, "ExpandButton"); Wait(80);
                check(Find<Grid>(focus, "ContentRoot").IsVisible && desktop.Companion.Session.Status == SessionStatus.Paused && desktop.Companion.Session.Remaining == remaining,
                    "mini pause and expansion preserve one session");
                Click(focus, "MiniModeButton"); Wait(80);
                check(!(bool)Call(focus, "IsWindowDragTarget", Find<Button>(focus, "MiniToggleButton"))! && (bool)Call(focus, "IsWindowDragTarget", Find<TextBlock>(focus, "MiniTimeText"))!,
                    "mini commands never start a window drag, while its read-only time can move the window");
                check((bool)Call(focus, "IsWindowDragTarget", Find<TextBlock>(focus, "MiniTitleText"))! &&
                      (bool)Call(focus, "IsWindowDragTarget", Find<TextBlock>(focus, "MiniPhaseText"))! &&
                      (bool)Call(focus, "IsWindowDragTarget", Find<Image>(focus, "MiniPetImage"))!, "mini header and avatar remain draggable without the old name label");
                focus.Width = 280; focus.Height = 104;
                Click(focus, "MiniResetButton"); desktop.Companion.SetDurations(1, 1); Wait(60);
                foreach (var (stage, expectedTitle, expectedStatus) in new[] {
                    ("ready", "专注", "准备"), ("running", "专注", "进行中"), ("paused", "专注", "已暂停"),
                    ("completed", "专注", "已完成"), ("break-running", "休息", "进行中"), ("break-completed", "休息", "已完成") })
                {
                    if (stage is "running" or "paused" or "break-running") Click(focus, "MiniToggleButton");
                    if (stage == "completed") { Click(focus, "MiniToggleButton"); clock.Advance(60); desktop.Companion.Tick(); }
                    if (stage == "break-completed") { clock.Advance(60); desktop.Companion.Tick(); }
                    Wait(60);
                    var phase = Find<TextBlock>(focus, "MiniPhaseText");
                    check(Find<TextBlock>(focus, "MiniTitleText").Text == expectedTitle && phase.Text == expectedStatus && TextFits(phase) && TextFits(Find<TextBlock>(focus, "MiniTimeText")) &&
                          System.Windows.Automation.AutomationProperties.GetName(phase) == focus.Controller.Snapshot().PhaseText,
                        "minimal timer preserves readable " + stage + " state in " + theme);
                    check(Equals(Find<Button>(focus, "MiniToggleButton").ToolTip, focus.Controller.Snapshot().ToggleText), "mini action tooltip tracks " + stage);
                    Render((FrameworkElement)focus.Content, focus.Background, renders, $"mini-{stage}-{theme}.png");
                }
                Find<TextBlock>(focus, "WindowError").Text = "窗口设置未保存，请检查配置目录权限后重试。";
                Call(focus, "RefreshMiniStatus"); Wait(40);
                check(Find<TextBlock>(focus, "MiniPhaseText").Text == "设置未保存" && TextFits(Find<TextBlock>(focus, "MiniPhaseText")), "save-error status fits beside title and avatar in the minimum mini header");
                Render((FrameworkElement)focus.Content, focus.Background, renders, $"mini-error-{theme}.png");
                Find<TextBlock>(focus, "WindowError").Text = string.Empty; Call(focus, "RefreshMiniStatus");
            }
            finally { focus.Close(); }
            var menu = (PetContextMenu)pet.ContextMenu;
            menu.PlacementTarget = pet;
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = menu.VerticalOffset = 25;
            menu.IsOpen = true; Wait(100);
            check(menu.IsOpen && menu.ActualHeight < 500 && menu.ActualWidth <= 360, $"native grouped menu stays compact in {theme}: open={menu.IsOpen}, width={menu.ActualWidth}, height={menu.ActualHeight}");
            check(((SolidColorBrush)menu.FindResource("SurfaceBrush")).Color.R == (theme == "light" ? 255 : 32), "desktop menu uses the active " + theme + " palette");
            Render(menu, (Brush)menu.FindResource("SurfaceBrush"), renders, "pet-menu-" + theme + ".png");
            foreach (var label in new[] { "动作", "显示与行为" })
            {
                var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, label));
                item.IsSubmenuOpen = true; Wait(100);
                var popup = (Popup)item.Template.FindName("PART_Popup", item);
                check(popup.IsOpen && popup.Child is FrameworkElement { ActualWidth: > 100, ActualHeight: > 100 }, "native " + label + " submenu opens with real content");
                Render((FrameworkElement)popup.Child, (Brush)menu.FindResource("SurfaceBrush"), renders, "pet-submenu-" + (label == "动作" ? "actions-" : "display-") + theme + ".png");
                item.IsSubmenuOpen = false;
            }
            desktop.UpdateAppearance(new AppearanceOptions { Theme = theme == "light" ? "dark" : "light", ReduceMotion = true });
            check(((SolidColorBrush)menu.FindResource("SurfaceBrush")).Color.R == (theme == "light" ? 32 : 255), "an already open desktop menu follows theme changes");
            var first = menu.Items.OfType<MenuItem>().First();
            first.Focus();
            first.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(menu)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Wait(180);
            check(!menu.IsOpen, "Escape closes the themed desktop menu without invoking commands");
            check(!(bool)typeof(PetContextMenu).GetField("_listening", Private)!.GetValue(menu)!, "closed menu releases its theme subscription");
        }
        desktop.Companion.Session.Reset(); desktop.Companion.Session.StartOrResume();
        var before = desktop.Windows.Count;
        var exitRequests = 0; desktop.ExitRequested += () => exitRequests++;
        DismissDialog<PetAboutWindow>(() => Call(pet, "OpenAbout"), about =>
        {
            check(Find<TextBlock>(about, "CharacterName").Text == pet.Package.Manifest.Name && Find<Image>(about, "CharacterImage").Source is BitmapSource { IsFrozen: true }, "introduction shows the chosen character without modifying it");
            Render((FrameworkElement)about.Content, about.Background, renders, "character-introduction.png");
            return false;
        });
        check(desktop.Windows.Count == before && desktop.Companion.Session.IsFocusing && exitRequests == 0, "closing the introduction never closes a role or stops focus");
        DismissDialog<ThemedWindow>(() => Call(pet, "CloseInstance"), dialog => false);
        check(desktop.Windows.Count == before && desktop.Windows.Contains(pet), "canceling role closure preserves the chosen instance");
        DismissDialog<ThemedWindow>(() => Call(pet, "CloseInstance"), dialog => true);
        check(desktop.Windows.Count == before - 1 && !desktop.Windows.Contains(pet) && desktop.Companion.Session.IsFocusing && exitRequests == 0,
            "confirming role closure removes only that instance and preserves the app and timer");
        var last = desktop.Windows.Single();
        new WindowInteropHelper(last).EnsureHandle();
        DismissDialog<ThemedWindow>(() => Call(last, "CloseInstance"), dialog => true);
        check(desktop.Windows.Count == 0 && desktop.Companion.Session.IsFocusing && exitRequests == 0, "closing the last role keeps tray ownership and the shared focus service alive");
    }

    private static void DismissDialog<T>(Action open, Func<T, bool> inspect) where T : ThemedWindow
    {
        Exception? failure = null;
        var seen = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<T>().FirstOrDefault(w => w.IsVisible && (typeof(T) != typeof(ThemedWindow) || w.Title == "关闭角色"));
            if (dialog is null) return;
            timer.Stop(); seen = true;
            var accepted = false;
            try { accepted = inspect(dialog); } catch (Exception ex) { failure = ex; }
            finally { dialog.DialogResult = accepted; }
        };
        timer.Start(); try { open(); } finally { timer.Stop(); }
        if (failure is not null) throw failure;
        if (!seen) throw new InvalidOperationException("Expected dialog was not opened.");
    }
    private static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = window.Top = -30000;
        window.ShowInTaskbar = window.ShowActivated = false;
        window.Show(); Wait(100);
    }
    private static bool IsInside(FrameworkElement element, FrameworkElement root)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1;
    }
    private static Rect Bounds(FrameworkElement element, FrameworkElement root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static bool TextFits(TextBlock text)
    {
        var formatted = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentUICulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace <= text.ActualWidth + 1 && formatted.Height <= text.ActualHeight + 1;
    }
    private static void Wait(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
    private static void Render(FrameworkElement root, Brush background, string directory, string name)
    {
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right), (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        var fill = new DrawingVisual(); using (var dc = fill.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(fill); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(directory); using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
