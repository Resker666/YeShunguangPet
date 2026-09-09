using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class EditorAdvancedTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Control<T>(Window w, string name) => (T)w.FindName(name);
    private static void Click(Window w, string name) => Control<ButtonBase>(w, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static ObservableCollection<PetEditorWindow.DurationRow> Rows(Window w) => (ObservableCollection<PetEditorWindow.DurationRow>)Control<DataGrid>(w, "DurationGrid").ItemsSource;

    public static void Run(Action<bool, string> check, PetCatalog existing, string root)
    {
        var clock = new QualityClock();
        var history = new EditorHistory(clock);
        history.Reset("initial"); history.Record("a", "name"); history.Record("ab", "name");
        check(history.Count == 2 && history.Undo() == "initial" && history.Redo() == "ab", "typing is coalesced into one history entry");
        clock.Advance(601); history.Record("abc", "name");
        check(history.Undo() == "ab", "separate typing gestures have separate checkpoints");
        history.Record("branch", "name");
        check(!history.CanRedo && history.Current == "branch", "editing after undo discards only the redo branch");
        for (var i = 0; i < 160; i++) history.Record("entry" + i);
        check(history.Count == 100 && history.Current == "entry159", "editor history is bounded at one hundred checkpoints");
        for (var i = 0; i < 100; i++) history.Record(i + new string('x', 32768));
        check(history.Count < 100 && history.CanUndo, "history memory budget evicts oldest checkpoints");
        var times = new[] { 100, 300, 200 };
        check(AnimationTimeline.FrameAt(times, 0) == 0 && AnimationTimeline.FrameAt(times, 99) == 0 &&
            AnimationTimeline.FrameAt(times, 100) == 1 && AnimationTimeline.FrameAt(times, 399) == 1 &&
            AnimationTimeline.FrameAt(times, 400) == 2 && AnimationTimeline.FrameAt(times, 600) == 2,
            "timeline follows individual frame durations and exact boundaries");
        check(AnimationTimeline.StartAt(times, 2) == 400 && AnimationTimeline.FrameAt(times, -1) == 0,
            "timeline clamps outside the animation range");

        var catalog = new PetCatalog(existing.BundledDirectory, Path.Combine(root, "advanced-users"));
        var source = existing.Scan().Pets.Single(p => p.Id == PetPackage.DefaultId);
        var entry = new PetEditorDocument(source.ManifestPath).SaveCopy(catalog);
        var pngPath = Path.Combine(Path.GetDirectoryName(entry.ManifestPath)!, "spritesheet.png");
        var original = File.ReadAllBytes(entry.ManifestPath); var png = File.ReadAllBytes(pngPath);
        var window = new PetEditorWindow(catalog, entry);
        try
        {
            var originalName = Control<TextBox>(window, "NameInput").Text;
            Control<ComboBox>(window, "ActionSelector").SelectedIndex = (int)PetState.Waving;
            check(!Control<Button>(window, "UndoButton").IsEnabled, "action navigation does not create an edit");
            Control<TextBox>(window, "NameInput").Text = "Editor test";
            Control<TextBox>(window, "NameInput").Text = "Editor test edited";
            Click(window, "UndoButton");
            check(Control<TextBox>(window, "NameInput").Text == originalName && (int)Control<ComboBox>(window, "ActionSelector").SelectedIndex == (int)PetState.Waving,
                "undo restores metadata without jumping to an unrelated action");
            Click(window, "RedoButton");
            check(Control<TextBox>(window, "NameInput").Text == "Editor test edited", "redo restores the complete coalesced edit");
            Click(window, "UndoButton");
            var oldTimes = Rows(window).Select(r => r.Text).ToArray();
            Rows(window)[0].Text = "700";
            Click(window, "UndoButton");
            check(Rows(window)[0].Text == oldTimes[0], "frame duration can be undone");
            Click(window, "RedoButton");
            var frame = Control<Image>(window, "AnimationPreview").Source;
            Rows(window)[0].Text = "bad";
            check(!Control<Button>(window, "CopyButton").IsEnabled && ReferenceEquals(frame, Control<Image>(window, "AnimationPreview").Source),
                "invalid draft keeps the last valid image and disables saving");
            Click(window, "UndoButton");
            check(Rows(window)[0].Text == "700" && Control<Button>(window, "CopyButton").IsEnabled, "undo recovers valid content from invalid raw input");
            Click(window, "RedoButton");
            check(Rows(window)[0].Text == "bad" && !Control<Button>(window, "CopyButton").IsEnabled, "redo can restore an invalid draft without crashing");
            Click(window, "UndoButton");
            Control<TextBox>(window, "BatchDurationInput").Text = "350";
            Click(window, "ApplyDurationButton");
            check(Rows(window).All(r => r.Text == "350"), "batch duration applies to every frame");
            Click(window, "UndoButton");
            check(Rows(window)[0].Text == "700" && Rows(window).Skip(1).Select(r => r.Text).SequenceEqual(oldTimes.Skip(1)), "one undo restores all frames from a batch edit");
            var grid = Control<DataGrid>(window, "DurationGrid");
            grid.SelectedItems.Clear(); grid.SelectedItems.Add(Rows(window)[1]); grid.SelectedItems.Add(Rows(window)[3]);
            Control<ComboBox>(window, "BatchScope").SelectedIndex = 1;
            Control<TextBox>(window, "BatchDurationInput").Text = "420";
            Click(window, "ApplyDurationButton");
            check(Rows(window)[1].Text == "420" && Rows(window)[3].Text == "420" && Rows(window)[0].Text == "700" && Rows(window)[2].Text == oldTimes[2],
                "selected-frame batch leaves other durations unchanged");
            Click(window, "UndoButton");
            Control<Slider>(window, "TimelineSlider").Value = 700;
            check(Control<TextBlock>(window, "FrameStatus").Text.StartsWith("帧 2/") && !(bool)typeof(PetEditorWindow).GetField("_playing", Private)!.GetValue(window)!,
                "timeline seek selects the duration boundary and pauses playback");
            Click(window, "NextFrameButton");
            check(Control<TextBlock>(window, "FrameStatus").Text.StartsWith("帧 3/"), "next-frame control advances one frame");
            Click(window, "PreviousFrameButton");
            check(Control<TextBlock>(window, "FrameStatus").Text.StartsWith("帧 2/"), "previous-frame control reverses one frame");
            var count = Control<TextBox>(window, "FrameCountInput"); count.Text = "1";
            check(!Control<Button>(window, "NextFrameButton").IsEnabled && !Control<Button>(window, "PreviousFrameButton").IsEnabled,
                "single-frame animations keep both step controls bounded");
            Click(window, "UndoButton");
            check(Rows(window).Count == 4 && Rows(window)[0].Text == "700", "undo restores frame count and all individual durations");
            var enabled = Control<CheckBox>(window, "EnabledCheck"); enabled.IsChecked = false;
            Click(window, "UndoButton");
            check(enabled.IsChecked == true && Rows(window)[0].Text == "700", "undo restores disabled action parameters");
            check(File.ReadAllBytes(entry.ManifestPath).SequenceEqual(original) && File.ReadAllBytes(pngPath).SequenceEqual(png), "history, timeline and preview never write source JSON or PNG");
        }
        finally { Close(window); }
        var document = new PetEditorDocument(entry.ManifestPath);
        document.Draft.Name = "Local draft";
        WriteName(entry.ManifestPath, "External writer");
        try { document.SaveUpdate(catalog, entry); check(false, "conflicting source update rejected"); }
        catch (InvalidDataException) { check(true, "conflicting source update rejected"); }
        check(PetPackage.Load(entry.ManifestPath).Manifest.Name == "External writer" && catalog.ScanBackups().Backups.Count == 0,
            "conflict detection preserves external files without creating a misleading backup");
        var copy = document.SaveCopy(catalog);
        check(PetPackage.Load(copy.ManifestPath).Manifest.Name == "Local draft", "conflicted draft remains exportable as an independent skin");
    }

    public static void RunLive(Action<bool, string> check, PetCatalog existing, string renders)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "YeShunguangPet-editor-live-" + Guid.NewGuid().ToString("N"));
        var catalog = new PetCatalog(existing.BundledDirectory, temporary);
        var entry = new PetEditorDocument(existing.Scan().Pets.Single(p => p.Id == PetPackage.DefaultId).ManifestPath).SaveCopy(catalog);
        var pngPath = Path.Combine(Path.GetDirectoryName(entry.ManifestPath)!, "spritesheet.png");
        var png = File.ReadAllBytes(pngPath);
        var window = new PetEditorWindow(catalog, entry) { ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
        try
        {
            window.Show(); Until(() => Control<TextBlock>(window, "SourceStatus").Text == "源文件已同步");
            var originalName = Control<TextBox>(window, "NameInput").Text;
            foreach (var theme in new[] { "light", "dark" })
            {
                UiTheme.Apply(new AppearanceOptions { Theme = theme, ReduceMotion = true, Accent = "#24796E" });
                foreach (var size in new[] { new Size(1020, 760), new Size(800, 560) })
                {
                    window.Width = size.Width; window.Height = size.Height; FocusVisualGate.Pump(80);
                    var scroll = Control<ScrollViewer>(window, "EditorControlsScroll"); scroll.ScrollToTop(); FocusVisualGate.Pump(20);
                    foreach (var name in new[] { "UndoButton", "RedoButton", "CopyButton", "UpdateButton", "AutoReloadCheck", "OpenSourceFolderButton", "ReloadSourceButton" })
                        check(Inside(Control<FrameworkElement>(window, name), (FrameworkElement)window.Content), "editor command stays inside client area: " + name + " " + size + " " + theme);
                    check(Control<Image>(window, "AnimationPreview").Source is not null && Control<Slider>(window, "TimelineSlider").ActualWidth > 180,
                        "native editor displays sprite and a usable timeline in " + theme);
                    Render(window, renders, $"editor-advanced-{size.Width}-{theme}.png");
                    scroll.ScrollToBottom(); FocusVisualGate.Pump(30);
                    check(Inside(Control<Button>(window, "ApplyDurationButton"), scroll), "batch editing is reachable by scrolling at " + size);
                }
            }
            var preview = Control<Image>(window, "AnimationPreview").Source;
            WriteName(entry.ManifestPath, "External revision one");
            Until(() => Control<TextBox>(window, "NameInput").Text == "External revision one");
            check(!Control<Button>(window, "UndoButton").IsEnabled && Control<Button>(window, "UpdateButton").IsEnabled, "clean source reload replaces preview and resets incompatible history");
            File.WriteAllText(entry.ManifestPath, "{");
            Until(() => Control<TextBlock>(window, "SourceStatus").Text.Contains("暂不可用"));
            check(Control<TextBox>(window, "NameInput").Text == "External revision one" && Control<Image>(window, "AnimationPreview").Source is not null && !Control<Button>(window, "UpdateButton").IsEnabled,
                "half-written manifest preserves the last valid preview");
            var valid = PetSourceSnapshot.Read(existing.Scan().Pets.Single(p => p.Id == PetPackage.DefaultId).ManifestPath);
            var json = JsonNode.Parse(valid.Json)!; json["id"] = entry.Id; json["name"] = "External revision two";
            File.WriteAllText(entry.ManifestPath, json.ToJsonString());
            Until(() => Control<TextBox>(window, "NameInput").Text == "External revision two");
            File.WriteAllBytes(pngPath, new byte[] { 1, 2, 3 });
            Until(() => Control<TextBlock>(window, "SourceStatus").Text.Contains("暂不可用"));
            check(Control<Image>(window, "AnimationPreview").Source is not null, "incomplete PNG does not clear the loaded sprite");
            File.WriteAllBytes(pngPath, png);
            Until(() => Control<TextBlock>(window, "SourceStatus").Text == "源文件已同步");
            Control<TextBox>(window, "NameInput").Text = "Unsaved local draft";
            WriteName(entry.ManifestPath, "External revision three");
            Until(() => Control<Grid>(window, "SourceConflictPanel").Visibility == Visibility.Visible);
            check(Control<TextBox>(window, "NameInput").Text == "Unsaved local draft" && !Control<Button>(window, "UpdateButton").IsEnabled,
                "external reload cannot overwrite unsaved local inputs");
            Click(window, "KeepDraftButton");
            check(Control<Button>(window, "CopyButton").IsEnabled && Control<Grid>(window, "SourceConflictPanel").Visibility == Visibility.Collapsed,
                "keeping a conflicted draft leaves copy and export available");
            Click(window, "ReloadSourceButton");
            Until(() => Control<Grid>(window, "SourceConflictPanel").Visibility == Visibility.Visible);
            Click(window, "LoadExternalButton");
            check(Control<TextBox>(window, "NameInput").Text == "External revision three" && !Control<Button>(window, "UndoButton").IsEnabled,
                "explicit source reload adopts external revision and clears old draft history");
            json = JsonNode.Parse(File.ReadAllText(entry.ManifestPath))!; json["id"] = "changed-identity";
            File.WriteAllText(entry.ManifestPath, json.ToJsonString());
            Until(() => Control<TextBlock>(window, "SourceStatus").Text.Contains("ID"));
            check(Control<TextBox>(window, "IdInput").Text == entry.Id, "hot reload rejects a changed skin identity");
            json["id"] = entry.Id; json["name"] = "External revision four"; File.WriteAllText(entry.ManifestPath, json.ToJsonString());
            Until(() => Control<TextBox>(window, "NameInput").Text == "External revision four");
            var folder = Path.GetDirectoryName(entry.ManifestPath)!; var old = folder + "-old";
            Directory.Move(folder, old); Directory.CreateDirectory(folder);
            File.WriteAllBytes(pngPath, png); json["name"] = "Atomic replacement"; File.WriteAllText(entry.ManifestPath, json.ToJsonString());
            Until(() => Control<TextBox>(window, "NameInput").Text == "Atomic replacement");
            check(true, "watching parent directory survives atomic skin-directory replacement");
            Control<CheckBox>(window, "AutoReloadCheck").IsChecked = false;
            WriteName(entry.ManifestPath, "Manual reload"); FocusVisualGate.Pump(800);
            check(Control<TextBox>(window, "NameInput").Text == "Atomic replacement", "disabled source watching does not replace preview");
            Click(window, "ReloadSourceButton");
            Until(() => Control<TextBox>(window, "NameInput").Text == "Manual reload");
            check(Control<CheckBox>(window, "AutoReloadCheck").IsChecked == false, "one-shot refresh preserves the automatic refresh preference");
            check(File.ReadAllBytes(pngPath).SequenceEqual(png), "hot reload does not alter source PNG pixels");
            Control<CheckBox>(window, "AutoReloadCheck").IsChecked = true;
            WriteName(entry.ManifestPath, "Pending at close");
        }
        finally
        {
            Close(window); FocusVisualGate.Pump(800);
            check(typeof(PetEditorWindow).GetField("_sourceWatcher", Private)!.GetValue(window) is null &&
                !((DispatcherTimer)typeof(PetEditorWindow).GetField("_timer", Private)!.GetValue(window)!).IsEnabled,
                "closing editor stops file watching and animation before pending updates can apply");
            check(Control<TextBox>(window, "NameInput").Text == "Manual reload", "pending source update cannot change a closed editor");
            UiTheme.Apply(new AppearanceOptions { Theme = "light", ReduceMotion = false });
            var references = CreateClosedEditors(catalog, entry);
            // Check the closed cohort while a replacement editor is active, as in repeated editing sessions.
            var cleanupHost = new PetEditorWindow(catalog, entry) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
            Control<CheckBox>(cleanupHost, "AutoReloadCheck").IsChecked = false;
            try
            {
                cleanupHost.Show(); FocusVisualGate.Pump(100);
                var settle = Stopwatch.StartNew();
                do
                {
                    FocusVisualGate.Pump(100); FocusControllerTests.Collect();
                } while (CountSurvivors(references) > 0 && settle.Elapsed < TimeSpan.FromSeconds(5));
                Console.WriteLine($"MEASURE: closed editor cohort survivors={CountSurvivors(references)}; settle={settle.Elapsed.TotalMilliseconds:F0}ms");
                check(CountSurvivors(references) == 0, "closed editor cohort is collectible while a replacement editor is active");
            }
            finally { Close(cleanupHost); }
            Directory.Delete(temporary, true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateClosedEditors(PetCatalog catalog, PetEntry entry)
    {
        var references = new WeakReference[10];
        for (var i = 0; i < references.Length; i++) references[i] = CreateClosedEditor(catalog, entry);
        return references;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CountSurvivors(WeakReference[] references)
    {
        var count = 0;
        foreach (var reference in references) if (reference.IsAlive) count++;
        return count;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateClosedEditor(PetCatalog catalog, PetEntry entry)
    {
        var window = new PetEditorWindow(catalog, entry) { ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
        window.Show(); FocusVisualGate.Pump(15);
        Close(window); return new WeakReference(window);
    }

    private static void WriteName(string path, string name) { var json = JsonNode.Parse(File.ReadAllText(path))!; json["name"] = name; File.WriteAllText(path, json.ToJsonString()); }
    private static void Until(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed.TotalSeconds > 12) throw new TimeoutException("Editor source update timed out"); FocusVisualGate.Pump(20); }
    }
    private static void Close(PetEditorWindow window)
    {
        typeof(PetEditorWindow).GetField("_dirtyInputs", Private)!.SetValue(window, false); window.Close();
    }
    private static bool Inside(FrameworkElement control, FrameworkElement root)
    {
        var box = control.TransformToAncestor(root).TransformBounds(new Rect(control.RenderSize));
        return box.Left >= -1 && box.Top >= -1 && box.Right <= root.ActualWidth + 1 && box.Bottom <= root.ActualHeight + 1;
    }
    private static void Render(Window window, string output, string name)
    {
        Directory.CreateDirectory(output);
        var root = (FrameworkElement)window.Content; root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
    }
}
