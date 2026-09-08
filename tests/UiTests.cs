using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class UiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void RunLive(Action<bool, string> check, PetCatalog catalog, string renders)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var config = DesktopConfiguration.Migrate(new PetSettings { Topmost = false, LookAtMouse = false, RandomIdleActions = false });
        config.Appearance = new AppearanceOptions { Theme = "light" };
        using var desktop = new DesktopSession(config, catalog, _ => { }, nativeIntegration: false);
        desktop.Start(showWindows: false);
        desktop.Add(catalog.Scan().Pets.FirstOrDefault(p => p.Id == "robin")?.Id ?? PetPackage.DefaultId, show: false);
        desktop.Add(PetPackage.DefaultId, show: false);
        var manager = new PetManagerWindow(desktop);
        var failures = new List<Exception>();
        app.DispatcherUnhandledException += (_, e) => { failures.Add(e.Exception); e.Handled = true; };
        try
        {
            ShowOffscreen(manager);
            Await(Task.Delay(350));
            var timer = (DispatcherTimer)typeof(PetManagerWindow).GetField("_previewTimer", Private)!.GetValue(manager)!;
            check(manager.IsLoaded && timer.IsEnabled == UiTheme.MotionEnabled, "native control center loads and starts eligible previews");
            var list = (ListBox)manager.FindName("Instances");
            var card = list.Items[0];
            var previewProperty = card.GetType().GetProperty("Preview")!;
            var originalFrame = previewProperty.GetValue(card);
            var changed = !UiTheme.MotionEnabled;
            for (var i = 0; i < 12; i++) { Await(Task.Delay(90)); changed |= !ReferenceEquals(originalFrame, previewProperty.GetValue(card)); }
            check(changed, "native control center animation advances frozen frame references");
            manager.WindowState = WindowState.Minimized;
            Await(Task.Delay(60));
            check(!timer.IsEnabled, "minimized control center stops its animation timer");
            manager.WindowState = WindowState.Normal;
            Await(Task.Delay(80));
            check(timer.IsEnabled == UiTheme.MotionEnabled, "restoring control center resumes eligible previews");
            ((CheckBox)manager.FindName("TopmostCheck")).IsChecked = false;
            ((CheckBox)manager.FindName("EdgeCheck")).IsChecked = true;
            Await(Task.Delay(300));
            var toggle = (CheckBox)manager.FindName("EdgeCheck");
            var thumb = (FrameworkElement)toggle.Template.FindName("SwitchThumb", toggle);
            check(((TranslateTransform)thumb.RenderTransform).X == 14, "native checked switch thumb reaches the on position");
            Render(manager, renders, "native-control-light.png", 984, 701);
            manager.Height = 590;
            Await(Task.Delay(80));
            var scroll = (SmoothScrollViewer)manager.FindName("MainScroll");
            scroll.ScrollToBottom();
            Await(Task.Delay(80));
            check(scroll.VerticalOffset > 0, "compact native control center keeps quick controls reachable by scrolling");
            ((RadioButton)manager.FindName("SettingsNav")).IsChecked = true;
            Await(Task.Delay(180));
            check(scroll.VerticalOffset == 0, "navigation resets previous page scroll position");
            manager.Height = 740;
            Await(Task.Delay(80));
            check(!timer.IsEnabled, "settings page suspends invisible role previews");
            ((RadioButton)manager.FindName("DarkTheme")).IsChecked = true;
            ((CheckBox)manager.FindName("MotionCheck")).IsChecked = true;
            Await(Task.Delay(180));
            Render(manager, renders, "native-settings-dark.png", 984, 701);
            ((RadioButton)manager.FindName("RoleScope")).IsChecked = true;
            Await(Task.Delay(80));
            var selector = (ComboBox)manager.FindName("RoleChoice");
            selector.IsDropDownOpen = true;
            Await(Task.Delay(120));
            var popup = (Popup)selector.Template.FindName("PART_Popup", selector);
            check(popup.IsOpen && popup.Child is FrameworkElement { ActualHeight: > 20 }, "native dark dropdown opens with real role choices");
            check(((FrameworkElement)popup.Child).ActualWidth >= selector.ActualWidth - 1, "dropdown width tracks its selector");
            selector.SelectedIndex = 2;
            selector.IsDropDownOpen = false;
            check(list.SelectedIndex == 2, "native role dropdown changes current card selection");
            var slider = (Slider)manager.FindName("ScaleSlider");
            slider.Value = 130;
            Await(Task.Delay(300));
            check(config.Pets[2].Scale == 1.3 && config.Pets[1].Scale == 1, "native debounced slider persists only its selected role");
            var settings = new SettingsWindow(new PetSettings(), false, catalog, desktop.Windows.First().Package);
            try
            {
                ShowOffscreen(settings);
                Await(Task.Delay(100));
                check(settings.IsLoaded && ((SolidColorBrush)settings.Background).Color.R == 32, "new native settings window adopts active dark theme");
                var tabs = (TabControl)settings.FindName("SettingsTabs");
                tabs.SelectedIndex = 1;
                Await(Task.Delay(100));
                var checkBox = (CheckBox)settings.FindName("TopmostCheckBox");
                checkBox.ApplyTemplate();
                check(((TranslateTransform)((FrameworkElement)checkBox.Template.FindName("SwitchThumb", checkBox)).RenderTransform).X == 14, "initially checked native switch has correct thumb position");
                Render(settings, renders, "native-role-settings-dark.png", 844, 641);
            }
            finally { settings.Close(); }
            var focus = new FocusWindow(desktop.Companion.Session, desktop.Companion.Settings, desktop.Windows.First().Package, () => false);
            try
            {
                ShowOffscreen(focus);
                ((Button)focus.FindName("ToggleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Await(Task.Delay(100));
                check(desktop.Companion.Session.IsFocusing && !((DispatcherTimer)typeof(FocusWindow).GetField("_avatarTimer", Private)!.GetValue(focus)!).IsEnabled, "reduce-motion focus panel keeps real session running without avatar motion");
                focus.WindowState = WindowState.Minimized;
                check(desktop.Companion.Session.IsFocusing, "native focus minimization preserves shared session");
            }
            finally { focus.Close(); }
            check(desktop.Companion.Session.IsFocusing, "native focus close preserves shared session");
            var closeDialog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            var accepted = true;
            closeDialog.Tick += (_, _) =>
            {
                var dialog = app.Windows.OfType<ThemedWindow>().FirstOrDefault(w => w.Title == "UI confirmation test");
                if (dialog is null) return;
                closeDialog.Stop();
                dialog.DialogResult = accepted;
            };
            try
            {
                closeDialog.Start();
                check(AppDialog.Show(manager, "确认此操作？", "UI confirmation test", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK, "themed confirmation preserves accept result");
                accepted = false;
                closeDialog.Start();
                check(AppDialog.Show(manager, "确认此操作？", "UI confirmation test", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel, "themed confirmation preserves cancellation result");
            }
            finally { closeDialog.Stop(); }
            TrayMenuTests.Run(check, catalog, renders);
            check(failures.Count == 0, "native UI has no unhandled dispatcher errors: " + string.Join("; ", failures.Select(e => e.Message)));
        }
        finally { manager.Close(); desktop.Dispose(); app.Shutdown(); }
    }

    private static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = window.Top = -30000;
        window.ShowInTaskbar = window.ShowActivated = false;
        window.Show();
    }
    public static void Run(Action<bool, string> check, PetCatalog catalog, string root, string? renders)
    {
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try { Verify(check, catalog, root, renders); }
        finally { UiTheme.Apply(new AppearanceOptions()); SynchronizationContext.SetSynchronizationContext(oldContext); }
    }

    private static void Verify(Action<bool, string> check, PetCatalog catalog, string root, string? renders)
    {
        var legacy = JsonSerializer.Deserialize<DesktopConfiguration>("{\"SchemaVersion\":2,\"Pets\":[]}")!;
        legacy.Validate();
        check(legacy.Appearance.Theme == "system" && legacy.Appearance.Accent == "#A92C46" && !legacy.Appearance.ReduceMotion, "v2 configuration gains conservative appearance defaults");
        var options = new AppearanceOptions { Theme = "unknown", Accent = "url(invalid)" };
        options.Normalize();
        check(options.Theme == "system" && options.Accent == "#A92C46", "invalid appearance values normalize without injecting resources");
        options.Accent = "#24796e";
        options.Normalize();
        check(options.Accent == "#24796E", "accent normalization is case independent");
        var store = new DesktopSettingsStore(Path.Combine(root, "ui-state.json"), Path.Combine(root, "ui-legacy.json"));
        var config = DesktopConfiguration.Migrate(new PetSettings { Topmost = false, LookAtMouse = false, RandomIdleActions = false });
        config.Appearance = new AppearanceOptions { Theme = "light", ReduceMotion = true };
        var saves = 0;
        using var desktop = new DesktopSession(config, catalog, state => { saves++; store.Save(state); }, nativeIntegration: false);
        desktop.Start(showWindows: false);
        var first = desktop.Windows.Single();
        var second = desktop.Add(catalog.Scan().Pets.FirstOrDefault(p => p.Id == "robin")?.Id ?? PetPackage.DefaultId, show: false);
        desktop.Add(PetPackage.DefaultId, show: false);
        var manager = new PetManagerWindow(desktop);
        try
        {
            var list = (ListBox)manager.FindName("Instances");
            check(list.Items.Count == 3 && !((Button)manager.FindName("AddButton")).IsEnabled, "control center shows all three roles and enforces capacity");
            var collection = list.ItemsSource;
            var selected = list.SelectedItem;
            var before = saves;
            var slider = (Slider)manager.FindName("ScaleSlider");
            slider.Value = 140;
            slider.Value = 160;
            check(first.PetScale == 1.6 && second.PetScale == 1 && saves == before, "quick size is immediate, instance-local and debounced");
            Invoke(manager, "FlushSaves");
            check(saves == before + 1 && store.Load().Pets[0].Scale == 1.6, "multiple slider changes coalesce into one persisted update");
            check(ReferenceEquals(collection, list.ItemsSource) && ReferenceEquals(selected, list.SelectedItem), "control center preserves collection and selection identity after save");
            ((CheckBox)manager.FindName("EdgeCheck")).IsChecked = true;
            ((CheckBox)manager.FindName("ThroughCheck")).IsChecked = true;
            Invoke(manager, "FlushSaves");
            check(config.Pets[0].EdgeAutoHide && config.Pets[0].ClickThrough && !config.Pets[1].EdgeAutoHide && !config.Pets[1].ClickThrough, "quick switches persist independently for each role");
            ((RadioButton)manager.FindName("SettingsNav")).IsChecked = true;
            ((RadioButton)manager.FindName("RoleScope")).IsChecked = true;
            check(ReferenceEquals(((ContentControl)manager.FindName("SettingsRoleHost")).Content, manager.FindName("QuickPanel")), "role settings reuse the live quick controls without duplicated state");
            ((ComboBox)manager.FindName("RoleChoice")).SelectedIndex = 1;
            slider.Value = 80;
            Invoke(manager, "FlushSaves");
            check(second.PetScale == 0.8 && first.PetScale == 1.6 && list.SelectedIndex == 1, "role scope selector routes edits to selected instance");
            Invoke(desktop, "BeginSettings", first);
            check(!((FrameworkElement)manager.FindName("QuickPanel")).IsEnabled && !((FrameworkElement)manager.FindName("AppSettingsPanel")).IsEnabled, "open detail settings lock conflicting control center edits");
            Invoke(desktop, "EndSettings");
            check(((FrameworkElement)manager.FindName("QuickPanel")).IsEnabled, "closing detail settings unlocks quick controls");
            ((RadioButton)manager.FindName("AppScope")).IsChecked = true;
            ((RadioButton)manager.FindName("DarkTheme")).IsChecked = true;
            ((RadioButton)manager.FindName("GreenAccent")).IsChecked = true;
            check(UiTheme.IsDark && store.Load().Appearance.Theme == "dark" && store.Load().Appearance.Accent == "#24796E", "theme and accent apply immediately and roundtrip through existing store");
            check(!UiTheme.MotionEnabled && UiTheme.Current.ReduceMotion, "reduce-motion option disables decorative animation");
            var darkSurface = ((SolidColorBrush)manager.FindResource("SurfaceBrush")).Color;
            check(darkSurface.R < 60 && ((SolidColorBrush)manager.FindResource("TextBrush")).Color.R > 220, "dark theme supplies contrasting surface and text brushes");

            if (renders is not null)
            {
                Render(manager, renders, "control-settings-dark.png", 984, 701);
                ((RadioButton)manager.FindName("RolesNav")).IsChecked = true;
                Render(manager, renders, "control-roles-dark.png", 984, 701);
                ((RadioButton)manager.FindName("LightTheme")).IsChecked = true;
                Render(manager, renders, "control-roles-light.png", 984, 701);
                Render(manager, renders, "control-roles-compact.png", 764, 551);
                ((RadioButton)manager.FindName("SettingsNav")).IsChecked = true;
                Render(manager, renders, "control-settings-light.png", 984, 701);
                Render(manager, renders, "control-settings-compact.png", 764, 551);
            }
            var scan = Await(catalog.ScanAsync());
            check(scan.Pets.Count == catalog.Scan().Pets.Count && scan.Pets.All(p => p.Thumbnail?.IsFrozen == true), "background catalog scan returns a complete collection of frozen thumbnails");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var wasCancelled = false;
            try { Await(catalog.ScanAsync(cancelled.Token)); } catch (OperationCanceledException) { wasCancelled = true; }
            check(wasCancelled, "cancelled catalog scan cannot publish stale results");
            VerifyToolWindows(check, catalog, first.Package, renders);
            slider.Value = 120;
            manager.Close();
            check(store.Load().Pets[1].Scale == 1.2, "closing control center flushes pending size changes");
            Await((Task)Invoke(manager, "RefreshSkinsAsync")!);
            check(!((DispatcherTimer)typeof(PetManagerWindow).GetField("_previewTimer", Private)!.GetValue(manager)!).IsEnabled, "closed control center cancels scans and stops preview timer");
            desktop.Remove(desktop.Windows.Last());
            using var cancelledAdd = new CancellationTokenSource();
            cancelledAdd.Cancel();
            var addCancelled = false;
            try { Await(desktop.AddAsync(PetPackage.DefaultId, cancelledAdd.Token, show: false)); } catch (OperationCanceledException) { addCancelled = true; }
            check(addCancelled && desktop.Windows.Count == 2, "cancelled background add creates no partial role");
            var added = Await(desktop.AddAsync(PetPackage.DefaultId, show: false));
            check(added.Dispatcher.CheckAccess() && added.Package.SpriteSheet.IsFrozen && desktop.Windows.Count == 3, "background add creates its window back on the UI dispatcher");
        }
        finally { manager.Close(); }
        var failing = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()), catalog, _ => throw new IOException("simulated disk failure"), nativeIntegration: false);
        try
        {
            var previous = failing.Configuration.Appearance.Clone();
            try { failing.UpdateAppearance(new AppearanceOptions { Theme = "dark" }); } catch (IOException) { }
            check(failing.Configuration.Appearance.Theme == previous.Theme, "failed appearance save preserves prior configuration");
        }
        finally { failing.Dispose(); }
    }

    private static void VerifyToolWindows(Action<bool, string> check, PetCatalog catalog, PetPackage pet, string? renders)
    {
        var session = new CompanionSession();
        foreach (var theme in new[] { "light", "dark" })
        {
            UiTheme.Apply(new AppearanceOptions { Theme = theme, ReduceMotion = true });
            var settings = new SettingsWindow(new PetSettings(), false, catalog, pet);
            var editor = new PetEditorWindow(catalog, catalog.Scan().Pets.First(p => p.Id == pet.Manifest.Id));
            var backups = new PetBackupsWindow(catalog);
            var focus = new FocusWindow(session, new PetSettings(), pet, () => false);
            var picker = new PetPickerWindow(catalog.Scan().Pets);
            try
            {
                foreach (var window in new Window[] { settings, editor, backups, focus, picker })
                {
                    Layout(window, (int)window.Width - 16, (int)window.Height - 39);
                    check(((SolidColorBrush)window.FindResource("SurfaceBrush")).Color.R == (theme == "dark" ? 32 : 255), $"{window.GetType().Name} inherits {theme} theme");
                    if (renders is not null) Render(window, renders, $"{window.GetType().Name}-{theme}.png", (int)window.Width - 16, (int)window.Height - 39);
                }
                var input = (TextBox)editor.FindName("NameInput");
                input.ApplyTemplate();
                check(input.Template.FindName("InputBorder", input) is Border, $"editor text input uses shared {theme} template");
                var combo = (ComboBox)editor.FindName("ActionSelector");
                combo.ApplyTemplate();
                check(combo.Template.FindName("PART_Popup", combo) is Popup, $"editor action selector retains {theme} dropdown contract");
                var tabs = (TabControl)settings.FindName("SettingsTabs");
                tabs.SelectedIndex = 1;
                if (renders is not null) Render(settings, renders, $"settings-general-{theme}.png", 844, 641);
                check(((CheckBox)settings.FindName("TopmostCheckBox")).IsChecked == true, "restyled settings preserve option values");
                check(((Button)focus.FindName("ToggleButton")).ActualWidth == 136, "focus primary command has stable dimensions");
            }
            finally { picker.Close(); focus.Close(); backups.Close(); editor.Close(); settings.Close(); }
        }
    }

    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Await(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var deadline = Environment.TickCount64 + 15000;
        timer.Tick += (_, _) => { if (task.IsCompleted || Environment.TickCount64 >= deadline) frame.Continue = false; };
        timer.Start();
        try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (!task.IsCompleted) throw new TimeoutException("UI operation did not complete.");
        task.GetAwaiter().GetResult();
    }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.Result; }
    private static FrameworkElement Layout(Window window, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        return content;
    }
    private static void Render(Window window, string folder, string name, int width, int height)
    {
        var content = Layout(window, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
    }
}
