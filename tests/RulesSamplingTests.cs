using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class RulesSamplingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly PetActivityContext Active = new(true, true, false, false, false, true, false, false, false);
    private static readonly PetBehaviorCapabilities Caps = new(true, true, true, true);
    private static T Control<T>(Window w, string name) => (T)w.FindName(name);
    private static void Click(Window w, string name) => Control<ButtonBase>(w, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);

    public static void Run(Action<bool, string> check, PetCatalog catalog, string root)
    {
        var entry = catalog.Scan().Pets.Single(p => p.Id == PetPackage.DefaultId);
        var original = PetSourceSnapshot.Read(entry.ManifestPath);
        var document = new PetEditorDocument(entry.ManifestPath);
        check(document.Draft.Behavior is null && document.Preview().RandomActions.SequenceEqual(new[] { PetState.Waving, PetState.Jumping }), "legacy skins retain their default random actions");
        var importedCatalog = new PetCatalog(catalog.BundledDirectory, Path.Combine(root, "rule-skins"));
        document.Draft.Behavior = new PetBehaviorRules { Rules = { new() { Action = PetState.Waving, Weight = 3 }, new() { Action = PetState.Jumping, Condition = BehaviorCondition.CursorFar, CooldownSeconds = 60 } } };
        var saved = document.SaveCopy(importedCatalog);
        var valid = File.ReadAllText(saved.ManifestPath);
        var loaded = PetPackage.Load(saved.ManifestPath);
        check(loaded.Manifest.Behavior!.Rules[0].Weight == 3 && loaded.Manifest.Behavior.Rules[1].Condition == BehaviorCondition.CursorFar, "behavior rules survive save and strict package loading");
        check(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(saved.ManifestPath)!, "spritesheet.png")).SequenceEqual(original.Png), "behavior configuration never changes sprite bytes");
        void Bad(Action<JsonNode> change, string message)
        {
            var node = JsonNode.Parse(valid)!; change(node); File.WriteAllText(saved.ManifestPath, node.ToJsonString());
            try { PetPackage.Load(saved.ManifestPath); check(false, message); } catch (InvalidDataException) { check(true, message); }
            finally { File.WriteAllText(saved.ManifestPath, valid); }
        }
        Bad(n => n["behavior"]!["schemaVersion"] = 99, "unknown rule schema is rejected");
        Bad(n => n["behavior"]!["rules"] = null, "null rule list is rejected");
        Bad(n => n["behavior"]!["rules"]![0] = null, "null rule is rejected");
        Bad(n => n["behavior"]!["rules"]![0]!["condition"] = "shell", "unknown condition is rejected");
        Bad(n => n["behavior"]!["rules"]![0]!["condition"] = 0, "numeric condition aliases are rejected");
        Bad(n => n["behavior"]!["rules"]![0]!["action"] = "running", "looping work states cannot be automated by rules");
        Bad(n => n["behavior"]!["rules"]![0]!["weight"] = 0, "zero weight is rejected");
        Bad(n => n["behavior"]!["rules"]![0]!["weight"] = 101, "oversized weight is rejected");
        Bad(n => n["behavior"]!["rules"]![0]!["cooldownSeconds"] = 4, "too-short cooldown is rejected");
        Bad(n => n["behavior"]!["rules"]![0]!["cooldownSeconds"] = 3601, "oversized cooldown is rejected");
        Bad(n => n["behavior"]!["rules"]![0]!["script"] = "anything", "rules cannot add executable script fields");
        Bad(n => n["behavior"]!["priority"] = 100, "rules cannot override system priority");
        Bad(n => n["behavior"]!["rules"]![1] = n["behavior"]!["rules"]![0]!.DeepClone(), "duplicate condition/action pairs are rejected");
        Bad(n => n["animations"]!.AsObject().Remove("waving"), "rules cannot reference missing animation states");
        Bad(n => n["animations"]!["waving"]!["durationsMs"] = JsonNode.Parse("[10000,10000,10000,10000]"), "long automatic animations are bounded at thirty seconds");
        Bad(n => { var rules = n["behavior"]!["rules"]!.AsArray(); while (rules.Count < 10) rules.Add(rules[0]!.DeepClone()); }, "rule count is bounded");
        VerifySelection(check);
        VerifyMeters(check);
        VerifyEditor(check, importedCatalog, saved);
        VerifySession(check, catalog);
    }

    private static void VerifySelection(Action<bool, string> check)
    {
        var clock = new QualityClock(); var behavior = new PetBehaviorController(clock, new Random(81));
        var rules = new PetBehaviorRules { Rules = { new() { Action = PetState.Waving, Weight = 3, CooldownSeconds = 5 }, new() { Action = PetState.Jumping, Weight = 1, CooldownSeconds = 5 } } };
        behavior.ConfigureRules(rules); rules.Rules[0].Weight = 0;
        var waving = 0;
        for (var i = 0; i < 1000; i++) { clock.Advance(5000); if (behavior.ChooseGesture(new[] { PetState.Waving, PetState.Jumping }) == PetState.Waving) waving++; }
        check(waving is > 680 and < 820, "weighted selection follows the copied 3:1 rule weights");
        rules = new PetBehaviorRules { Rules = { new() { Condition = BehaviorCondition.CursorNear, Action = PetState.Waving, CooldownSeconds = 60 } } };
        behavior.ConfigureRules(rules);
        var settings = new PetSettings { LookAtMouse = false, RandomIdleActions = true, IdleActionIntervalSeconds = 5 };
        behavior.EnsureSchedule(settings, Caps); clock.Advance(7000);
        check(behavior.ChooseAutomatic(Active, settings, Caps, false) == AutomaticPetAction.None && behavior.LastRuleDecision == RuleDecision.Condition,
            "unmatched pointer condition cannot start a gesture");
        check(behavior.ChooseAutomatic(Active, settings, Caps, true) == AutomaticPetAction.Gesture && behavior.ChooseGesture(new[] { PetState.Waving }, true) == PetState.Waving,
            "matching pointer condition selects the allowed action");
        behavior.ResetSchedule(settings, Caps); clock.Advance(7000);
        check(behavior.ChooseAutomatic(Active, settings, Caps, true) == AutomaticPetAction.None && behavior.LastRuleDecision == RuleDecision.Cooldown &&
            behavior.NextAmbientDelay(Active, settings, Caps)!.Value.TotalSeconds > 40, "cooldown survives schedule reset and does not cause rapid polling");
        behavior.ConfigureRules(rules); clock.Advance(1000);
        check(behavior.ChooseGesture(new[] { PetState.Waving }, true) == PetState.Idle, "unchanged rule reload preserves active cooldown");
        foreach (var context in new[] { Active with { Visible = false }, Active with { Quiet = true }, Active with { Focusing = true }, Active with { SettingsOpen = true },
            Active with { PointerBusy = true }, Active with { Docked = true } })
            check(behavior.ChooseAutomatic(context, settings, Caps, true) == AutomaticPetAction.None, "custom rules obey protected activity gates");
        behavior.BeginDrag(); check(behavior.ChooseAutomatic(Active, settings, Caps, true) == AutomaticPetAction.None, "drag priority blocks all custom rules"); behavior.EndDrag();
        behavior.BeginMenu(); check(behavior.ChooseAutomatic(Active, settings, Caps, true) == AutomaticPetAction.None, "menu priority blocks all custom rules"); behavior.EndMenu();
        behavior.ConfigureRules(new PetBehaviorRules());
        check(behavior.NextAmbientDelay(Active, settings, Caps) is null, "explicit empty rules disable automatic gestures without legacy fallback");
        behavior.ConfigureRules(null);
        check(behavior.ChooseGesture(new[] { PetState.Jumping }) == PetState.Jumping && behavior.LastRuleDecision == RuleDecision.Legacy, "removing optional behavior restores legacy selection");
    }

    private static void VerifyMeters(Action<bool, string> check)
    {
        var clock = new QualityClock(); var meter = new RuntimeRoleMeter(1, clock, clock.GetTimestamp());
        meter.ScheduleFrame(TimeSpan.FromMilliseconds(100)); clock.Advance(130); meter.Tick(RuntimeTick.Frame); meter.Tick(RuntimeTick.Ambient);
        check(meter.Snapshot().FrameCallbacks == 1 && meter.Snapshot().OtherCallbacks == 1 && meter.Snapshot().FrameLatenessP95Ms == 30, "runtime meter measures callback counts and scheduled-frame lateness");
        for (var i = 0; i < 400; i++) meter.Update(i % 2 == 0 ? PetActivity.Idle : PetActivity.Menu, PetState.Idle, 0, RuntimeTimers.Frame, RuleDecision.None, -1);
        check(meter.Transitions.Length == 128, "per-role transition storage is bounded");
        clock.Advance(300000); var before = meter.Snapshot(); meter.Tick(RuntimeTick.Frame); meter.Update(PetActivity.Dragging, PetState.Idle, 0, RuntimeTimers.None, RuleDecision.None, -1);
        check(meter.Snapshot() == before, "meters stop accepting data beyond the five-minute limit");
    }

    private static void VerifySession(Action<bool, string> check, PetCatalog catalog)
    {
        var clock = new QualityClock(); using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings()), catalog, _ => { }, false, clock);
        desktop.Start(false); desktop.Add(PetPackage.DefaultId, false);
        using var first = new RuntimeSamplingSession(desktop); using var second = new RuntimeSamplingSession(desktop);
        check(desktop.Windows.All(w => !w.IsRuntimeObserved), "diagnostic session is inert until explicitly started");
        first.Start(); second.Start(); first.Stop();
        check(desktop.Windows.All(w => w.IsRuntimeObserved), "independent observers do not stop one another");
        var capture = second.Capture();
        var logs = new ResilientLog(Array.Empty<string>()).Capture();
        var json = DiagnosticReport.Capture(desktop, logs, activity: capture).InformationJson;
        check(!json.Contains(desktop.Windows.First().Package.Manifest.Name) && !json.Contains(desktop.Windows.First().Package.Manifest.Id) &&
            !json.Contains("InstanceId") && !json.Contains("LeftPixels") && !json.Contains("CursorPosition"), "sampling export contains no role identity, source path or cursor coordinates");
        check(JsonDocument.Parse(json).RootElement.GetProperty("ActivitySampling").GetProperty("Roles").GetArrayLength() == 2, "sampling is included in the existing diagnostic JSON");
        var removed = desktop.Windows.First(); desktop.Remove(removed); desktop.Add(PetPackage.DefaultId, false);
        capture = second.Capture();
        check(capture.Roles.Select(r => r.Role).SequenceEqual(new[] { 2, 3 }) && !removed.IsRuntimeObserved, "sampling prunes removed roles and never reuses aliases");
        check(capture.Transitions.Any(item => item.Role == 1 && item.Activity == PetActivity.Exited), "removed role leaves an anonymous exit transition");
        clock.Advance(300000); capture = second.Capture();
        check(!capture.Recording && !second.IsRecording && desktop.Windows.All(w => !w.IsRuntimeObserved), "session timeout detaches all runtime observers");
    }

    private static void VerifyEditor(Action<bool, string> check, PetCatalog catalog, PetEntry entry)
    {
        var window = new PetEditorWindow(catalog, entry);
        try
        {
            Control<TabControl>(window, "ModeTabs").SelectedItem = window.FindName("BehaviorTab");
            var rows = (ObservableCollection<PetEditorWindow.BehaviorRuleRow>)Control<DataGrid>(window, "RuleGrid").ItemsSource;
            check(rows.Count == 2 && rows[0].Weight == "3", "editor loads behavior rules without dropping them");
            rows[0].Weight = "7"; Click(window, "UndoButton");
            check(rows[0].Weight == "3", "rule editing participates in undo history");
            Click(window, "RedoButton"); check(rows[0].Weight == "7", "rule editing participates in redo history");
            rows[0].Cooldown = "bad";
            check(!Control<Button>(window, "CopyButton").IsEnabled, "invalid rule input blocks package saving");
            Click(window, "UndoButton"); check(rows[0].Cooldown == "30" && Control<Button>(window, "CopyButton").IsEnabled, "undo restores valid rule input");
            Click(window, "AddRuleButton"); check(rows.Count == 3, "editor adds only distinct supported rule combinations");
            Click(window, "RemoveRuleButton"); check(rows.Count == 2, "editor removes the selected rule");
            Click(window, "UndoButton"); check(rows.Count == 3, "rule removal can be undone");
            Control<CheckBox>(window, "BehaviorEnabledCheck").IsChecked = false;
            check(Control<Button>(window, "CopyButton").IsEnabled, "legacy behavior can be restored without removing animations");
            Control<CheckBox>(window, "BehaviorEnabledCheck").IsChecked = true;
            check(rows.Count == 3, "disabling and enabling behavior preserves its draft rules");
            while (rows.Count > 0) { Control<DataGrid>(window, "RuleGrid").SelectedIndex = 0; Click(window, "RemoveRuleButton"); }
            Control<CheckBox>(window, "BehaviorEnabledCheck").IsChecked = false;
            Control<CheckBox>(window, "BehaviorEnabledCheck").IsChecked = true;
            check(rows.Count == 0, "explicit empty behavior remains empty after toggling");
        }
        finally { typeof(PetEditorWindow).GetField("_dirtyInputs", Private)!.SetValue(window, false); window.Close(); }
    }

    public static void RunLive(Action<bool, string> check, PetCatalog catalog, string output)
    {
        var clock = new QualityClock();
        using var desktop = new DesktopSession(DesktopConfiguration.Migrate(new PetSettings { Topmost = false, ClickThrough = true, RandomIdleActions = false, LookAtMouse = false }), catalog, _ => { }, false, clock);
        desktop.Start(false); desktop.Add(PetPackage.DefaultId, false);
        foreach (var role in desktop.Windows) { role.Opacity = 0; role.Show(); role.Left = role.Top = -30000; }
        var window = new DiagnosticsWindow(desktop, new ResilientLog(Array.Empty<string>()));
        try
        {
            ShowOffscreen(window);
            Control<TabControl>(window, "ReportTabs").SelectedItem = window.FindName("SamplingTab");
            check(desktop.Windows.All(w => !w.IsRuntimeObserved), "opening the activity tab does not start sampling");
            Click(window, "SampleButton");
            foreach (var role in desktop.Windows) Invoke(role, "PlayAnimation", PetState.Idle, true);
            clock.Advance(300);
            foreach (var role in desktop.Windows) Invoke(role, "FrameTimer_Tick", null, EventArgs.Empty);
            Invoke(window, "RefreshSampling");
            check(Control<DataGrid>(window, "RuntimeRoles").Items.Count == 2 && Control<DataGrid>(window, "RuntimeEvents").Items.Count > 0,
                "activity view displays subscribed role counters and transitions");
            foreach (var theme in new[] { "light", "dark" })
            {
                UiTheme.Apply(new AppearanceOptions { Theme = theme, ReduceMotion = true });
                foreach (var size in new[] { new Size(800, 640), new Size(510, 450) })
                {
                    window.Width = size.Width; window.Height = size.Height; FocusVisualGate.Pump(50);
                    foreach (var name in new[] { "SampleButton", "ExportButton", "ClearSampleButton" })
                        check(Inside(Control<FrameworkElement>(window, name), (FrameworkElement)window.Content), "sampling command remains inside the window: " + name + " " + size);
                    Render(window, output, $"runtime-sampling-{size.Width}-{theme}.png");
                }
            }
            Click(window, "SampleButton"); check(desktop.Windows.All(w => !w.IsRuntimeObserved), "stopping UI sampling releases all role subscriptions");
            Click(window, "ClearSampleButton"); check(Control<DataGrid>(window, "RuntimeRoles").Items.Count == 0, "clear removes previous sampling data");
            Click(window, "SampleButton"); clock.Advance(300000); Invoke(window, "RefreshSampling");
            check(Control<TextBlock>(window, "SampleText").Text == "开始采样" && desktop.Windows.All(w => !w.IsRuntimeObserved), "sampling UI stops automatically after five minutes");
            Click(window, "SampleButton");
        }
        finally { window.Close(); check(desktop.Windows.All(w => !w.IsRuntimeObserved), "closing diagnostics stops an active capture"); }
        var skin = catalog.Scan().Pets.Single(p => p.Id == PetPackage.DefaultId);
        var editor = new PetEditorWindow(catalog, skin);
        try
        {
            ShowOffscreen(editor); Control<TabControl>(editor, "ModeTabs").SelectedItem = editor.FindName("BehaviorTab");
            Control<CheckBox>(editor, "BehaviorEnabledCheck").IsChecked = true;
            foreach (var theme in new[] { "light", "dark" })
            {
                UiTheme.Apply(new AppearanceOptions { Theme = theme, ReduceMotion = true });
                editor.Width = 800; editor.Height = 560;
                Control<ScrollViewer>(editor, "EditorControlsScroll").ScrollToBottom(); FocusVisualGate.Pump(60);
                check(Inside(Control<Button>(editor, "AddRuleButton"), (FrameworkElement)editor.Content), "rule authoring stays reachable in compact " + theme);
                Render(editor, output, "behavior-rules-" + theme + ".png");
            }
        }
        finally { typeof(PetEditorWindow).GetField("_dirtyInputs", Private)!.SetValue(editor, false); editor.Close(); }
        var rulesFolder = Path.Combine(Path.GetTempPath(), "pet-live-rules-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rulesCatalog = new PetCatalog(catalog.BundledDirectory, rulesFolder);
            var document = new PetEditorDocument(skin.ManifestPath);
            document.Draft.Behavior = new PetBehaviorRules { Rules = { new() { Action = PetState.Failed, CooldownSeconds = 30 } } };
            var custom = document.SaveCopy(rulesCatalog);
            var options = DesktopConfiguration.Migrate(new PetSettings { SelectedPetId = custom.Id, Topmost = false, ClickThrough = true, LookAtMouse = false, RandomIdleActions = true });
            using var rulesDesktop = new DesktopSession(options, rulesCatalog, _ => { }, false, clock);
            rulesDesktop.Start(false); var role = rulesDesktop.Windows.Single(); role.Opacity = 0; ShowOffscreen(role);
            using var sampling = new RuntimeSamplingSession(rulesDesktop); sampling.Start();
            clock.Advance(60000); Invoke(role, "AmbientTimer_Tick", null, EventArgs.Empty);
            check(role.Behavior.Animation == PetState.Failed && role.Behavior.LastRuleIndex == 0, "desktop adopts the skin rule rather than the legacy gesture list");
            var capture = sampling.Capture();
            check(capture.Transitions.Any(item => item.Decision == RuleDecision.Selected && item.Animation == PetState.Failed), "rule selection appears in runtime sampling transitions");
            clock.Advance(30000); Invoke(role, "FrameTimer_Tick", null, EventArgs.Empty);
            Invoke(role, "BeginMenuInteraction"); clock.Advance(60000); Invoke(role, "AmbientTimer_Tick", null, EventArgs.Empty);
            check(role.ActivityPlan.Activity == PetActivity.Menu && role.Behavior.Animation == PetState.Idle, "native menu priority cannot be bypassed by a due custom rule");
            Invoke(role, "EndMenuInteraction");
        }
        finally { Directory.Delete(rulesFolder, true); }
    }
    private static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = window.Top = -30000;
        window.ShowActivated = window.ShowInTaskbar = false; window.Show(); FocusVisualGate.Pump(60);
    }
    private static bool Inside(FrameworkElement control, FrameworkElement root)
    {
        var box = control.TransformToAncestor(root).TransformBounds(new Rect(control.RenderSize));
        return box.Left >= -1 && box.Top >= -1 && box.Right <= root.ActualWidth + 1 && box.Bottom <= root.ActualHeight + 1;
    }
    private static void Render(Window window, string output, string name)
    {
        Directory.CreateDirectory(output); var root = (FrameworkElement)window.Content; root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            dc.DrawRectangle(window.Background, null, bounds);
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.Fill }, null, bounds);
        }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
    }
}
