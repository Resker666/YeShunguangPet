using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YeShunguangPet;

internal static class EditorBackupTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Action<bool, string> check, PetCatalog existing, PetEntry source, string root, string? renders)
    {
        var catalog = new PetCatalog(existing.BundledDirectory, Path.Combine(root, "editor-users"));
        var doc = new PetEditorDocument(source.ManifestPath);
        var originalJson = File.ReadAllBytes(source.ManifestPath);
        var originalPng = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(source.ManifestPath)!, doc.Draft.SpriteSheet));
        check(!doc.IsDirty, "editor starts with clean independent draft");
        doc.Draft.Animations[PetState.Idle].DurationsMs[0] = 350;
        check(doc.IsDirty && doc.Preview().GetAnimation(PetState.Idle).DurationsMs[0] == 350, "editor preview uses draft timing");
        check(File.ReadAllBytes(source.ManifestPath).SequenceEqual(originalJson), "editing draft cannot change source JSON");
        doc.Draft.CellWidth = 32;
        Bad(() => doc.Preview(), check, "editor rejects dimensions mismatched with fixed PNG");
        doc.Reset();
        check(!doc.IsDirty && doc.Preview().GetAnimation(PetState.Idle).DurationsMs[0] == 200, "editor reset restores initial draft");
        doc.Draft.Animations.Remove(PetState.Idle);
        Bad(() => doc.Preview(), check, "editor cannot save without idle");
        doc.Reset();
        doc.Draft.Id = "../escape";
        Bad(() => doc.SaveCopy(catalog), check, "editor unsafe ID creates no skin");
        check(!Directory.Exists(catalog.UserDirectory), "invalid editor draft creates no directory");
        doc.Reset();
        doc.Draft.Animations[PetState.Idle].DurationsMs[0] = 350;
        var saved = doc.SaveCopy(catalog);
        check(!doc.IsDirty && PetPackage.Load(saved.ManifestPath).GetAnimation(PetState.Idle).DurationsMs[0] == 350, "editor saves edited skin copy");
        check(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(saved.ManifestPath)!, "spritesheet.png")).SequenceEqual(originalPng), "editor saved copy preserves PNG bytes");
        var second = doc.SaveCopy(catalog);
        check(second.Id != saved.Id && catalog.Scan().Pets.Count(p => !p.Bundled) == 2, "save copy allocates independent ID");

        var update = new PetEditorDocument(saved.ManifestPath);
        update.Draft.Name = "Edited skin";
        var updated = update.SaveUpdate(catalog, saved);
        var backups = catalog.ScanBackups();
        check(backups.Errors.Count == 0 && backups.Backups.Count == 1 && backups.Backups[0].CanRestore && !backups.Backups[0].Deleted,
            "editor update creates restorable history backup");
        var history = backups.Backups[0];
        check(history.SizeBytes == new FileInfo(Path.Combine(history.DirectoryPath, "pet.json")).Length + originalPng.Length,
            "backup size includes manifest and PNG");
        Bad(() => catalog.RestoreBackup(history), check, "history overwrite requires explicit confirmation");
        var restored = catalog.RestoreBackup(history, true);
        check(PetPackage.Load(restored.ManifestPath).Manifest.Name != "Edited skin" && Directory.Exists(history.DirectoryPath),
            "history restore keeps selected backup and replaces current skin");
        check(catalog.ScanBackups().Backups.Count == 2 && catalog.ScanBackups().Backups.Any(b => b.Name == "Edited skin"),
            "history restore backs up current version first");
        var backupCopy = catalog.RestoreBackupCopy(history);
        check(backupCopy.Id != restored.Id && File.Exists(restored.ManifestPath) && Directory.Exists(history.DirectoryPath),
            "restore as copy preserves both installed skin and original backup");
        var backupZip = Path.Combine(root, "backup-export.zip");
        catalog.ExportBackup(history, backupZip);
        check(File.Exists(backupZip), "backup can be exported independently");

        var zip = Path.Combine(root, "editor-export.zip");
        update.Export(zip);
        using (var archive = ZipFile.OpenRead(zip))
        {
            using var stream = archive.GetEntry(updated.Id + "/spritesheet.png")!.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            check(memory.ToArray().SequenceEqual(originalPng), "editor ZIP contains original PNG bytes");
        }
        var exportImported = new PetCatalog(existing.BundledDirectory, Path.Combine(root, "editor-export-users")).Import(zip);
        check(PetPackage.Load(exportImported.ManifestPath).Manifest.Name == "Edited skin", "editor export reimports with edited configuration");
        var bundled = existing.Scan().Pets.First(p => p.Bundled);
        var bundledDoc = new PetEditorDocument(bundled.ManifestPath);
        bundledDoc.Draft.Name = "Bundled custom copy";
        Bad(() => bundledDoc.SaveUpdate(catalog, bundled), check, "editor cannot overwrite bundled skin");
        var bundledCopy = bundledDoc.SaveCopy(catalog);
        check(bundledCopy.Id != bundled.Id && !bundledCopy.Bundled, "bundled skin editor saves independent user copy");

        catalog.Delete(restored, "another-active-skin");
        var deleted = catalog.ScanBackups().Backups.Single(b => b.Deleted);
        check(deleted.CanRestore && deleted.Thumbnail is not null, "deleted skin has restore preview");
        restored = catalog.RestoreBackup(deleted);
        check(File.Exists(restored.ManifestPath) && Directory.Exists(deleted.DirectoryPath), "deleted skin restores without removing backup");
        File.WriteAllText(restored.ManifestPath, "broken");
        Bad(() => catalog.RestoreBackup(history), check, "damaged installed directory also requires overwrite confirmation");
        restored = catalog.RestoreBackup(history, true);
        check(PetPackage.Load(restored.ManifestPath).Manifest.Id == saved.Id, "valid backup repairs damaged installed manifest");
        var healthyJson = File.ReadAllText(restored.ManifestPath);
        var otherIdentity = System.Text.Json.Nodes.JsonNode.Parse(healthyJson)!;
        otherIdentity["id"] = "unrelated-valid-character";
        File.WriteAllText(restored.ManifestPath, otherIdentity.ToJsonString());
        Bad(() => catalog.RestoreBackup(history, true), check, "recovery cannot overwrite another valid skin stored under mismatched directory name");
        File.WriteAllText(restored.ManifestPath, healthyJson);
        var damaged = catalog.ScanBackups().Backups.Single(b => !b.CanRestore);
        check(damaged.CanPurge && damaged.SizeBytes > 0, "damaged history remains visible and safely purgeable");
        Bad(() => catalog.RestoreBackup(damaged), check, "damaged backup cannot be restored");
        Bad(() => catalog.PurgeBackup(history with { DirectoryPath = Path.GetDirectoryName(source.ManifestPath)! }), check, "purge cannot target ordinary skin directory");
        Bad(() => catalog.PurgeBackup(history with { DirectoryPath = Path.GetDirectoryName(bundled.ManifestPath)! }), check, "purge cannot target bundled directory");
        Bad(() => catalog.PurgeBackup(history with { DirectoryPath = Path.Combine(root, Path.GetFileName(history.DirectoryPath)) }), check, "purge rejects sibling outside user root");
        Bad(() => catalog.RestoreBackup(history with { Id = "different-id" }), check, "restore rejects mismatched backup identity");
        var installedHash = File.ReadAllBytes(restored.ManifestPath);
        catalog.PurgeBackup(damaged);
        check(!Directory.Exists(damaged.DirectoryPath) && File.ReadAllBytes(restored.ManifestPath).SequenceEqual(installedHash),
            "purge deletes only selected backup, not installed skin");
        var unknown = Path.Combine(catalog.UserDirectory, ".backup-not-a-generated-directory");
        Directory.CreateDirectory(unknown);
        check(catalog.ScanBackups().Backups.All(b => b.DirectoryPath != unknown), "backup scanner ignores unrelated dot directories");
        Bad(() => catalog.PurgeBackup(history with { DirectoryPath = unknown }), check, "purge refuses unknown backup naming pattern");
        VerifyLinks(check, catalog, history, root);

        VerifyEditorWindow(check, catalog, bundled, renders);
        VerifyBackupsWindow(check, catalog, renders);
        var empty = new PetBackupsWindow(new PetCatalog(existing.BundledDirectory, Path.Combine(root, "empty-backups")));
        try
        {
            check(((TextBlock)empty.FindName("EmptyText")).Visibility == Visibility.Visible &&
                !((Button)empty.FindName("RestoreButton")).IsEnabled && !((Button)empty.FindName("PurgeButton")).IsEnabled,
                "empty backup window has disabled actions");
        }
        finally { empty.Close(); }
        check(File.ReadAllBytes(source.ManifestPath).SequenceEqual(originalJson) &&
            File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(source.ManifestPath)!, "spritesheet.png")).SequenceEqual(originalPng),
            "editor and backups leave original fixture unchanged");
    }

    private static void VerifyLinks(Action<bool, string> check, PetCatalog catalog, PetBackupEntry history, string root)
    {
        var outside = Path.Combine(root, "outside-protected");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        var link = Path.Combine(history.DirectoryPath, "linked-extra");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Junctions exercise the same reparse guard without requiring symbolic-link privilege.
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:PET_TEST_LINK -Target $env:PET_TEST_TARGET | Out-Null");
            start.Environment["PET_TEST_LINK"] = link;
            start.Environment["PET_TEST_TARGET"] = outside;
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(10000)) { process.Kill(); process.WaitForExit(); throw new InvalidOperationException("Junction setup timed out."); }
            if (process.ExitCode != 0) throw new InvalidOperationException("Junction setup failed: " + process.StandardError.ReadToEnd(), ex);
        }
        try
        {
            var scanned = catalog.ScanBackups().Backups.Single(b => b.DirectoryPath == history.DirectoryPath);
            check(!scanned.CanPurge && !scanned.CanRestore, "linked backup is blocked in management UI");
            Bad(() => catalog.PurgeBackup(history), check, "purge rechecks links instead of trusting prior scan");
            Bad(() => catalog.RestoreBackup(history, true), check, "restore rechecks linked backup tree");
            check(File.Exists(Path.Combine(outside, "keep.txt")), "outside files survive rejected purge");
        }
        finally { Directory.Delete(link); }
    }

    private static void VerifyEditorWindow(Action<bool, string> check, PetCatalog catalog, PetEntry bundled, string? renders)
    {
        var editor = new PetEditorWindow(catalog, bundled);
        var type = typeof(PetEditorWindow);
        try
        {
            var action = (ComboBox)editor.FindName("ActionSelector");
            var name = (TextBox)editor.FindName("NameInput");
            check(!((Button)editor.FindName("UpdateButton")).IsEnabled && ((Button)editor.FindName("CopyButton")).IsEnabled,
                "bundled editor offers copy but disables overwrite");
            check(((Image)editor.FindName("AnimationPreview")).Source is not null, "editor renders initial preview");
            var grid = (SpriteGridView)editor.FindName("SheetView");
            check(grid.HitCell(new Point(25, 25)) == new FrameLocation(0, 0) && grid.HitCell(new Point(0, 0)) is null,
                "sprite grid hit testing excludes coordinate gutter");
            action.SelectedIndex = (int)PetState.Waving;
            check(grid.SelectedCount == 4 && grid.SelectedRow == 3, "action selector highlights matching cells");
            var rows = (ObservableCollection<PetEditorWindow.DurationRow>)((DataGrid)editor.FindName("DurationGrid")).ItemsSource;
            rows[0].Text = "520";
            check(((TextBlock)editor.FindName("FrameStatus")).Text.Contains("520"), "per-frame duration edit updates preview timing");
            var count = (TextBox)editor.FindName("FrameCountInput");
            count.Text = "1";
            count.Text = "4";
            check(rows[0].Text == "520" && rows[3].Text == "280", "frame count changes preserve entered frame durations");
            rows[0].Text = "bad";
            check(!((Button)editor.FindName("CopyButton")).IsEnabled, "invalid frame duration disables saving");
            action.SelectedIndex = (int)PetState.Jumping;
            check(action.SelectedIndex == (int)PetState.Waving && rows[0].Text == "bad", "invalid action input not silently discarded on switch");
            rows[0].Text = "520";
            type.GetMethod("SelectCell", Private)!.Invoke(editor, new object[] { 4, 1 });
            check(((TextBox)editor.FindName("ActionRowInput")).Text == "4" && grid.SelectedColumn == 1, "clicking sheet changes action origin");
            var timer = (DispatcherTimer)type.GetField("_timer", Private)!.GetValue(editor)!;
            check(timer.Interval == TimeSpan.FromMilliseconds(520), "preview timer follows per-frame settings");
            var previousFrame = ((Image)editor.FindName("AnimationPreview")).Source;
            type.GetMethod("Preview_Tick", Private)!.Invoke(editor, new object?[] { null, EventArgs.Empty });
            check(!ReferenceEquals(previousFrame, ((Image)editor.FindName("AnimationPreview")).Source), "editor animation advances frames");
            type.GetField("_frame", Private)!.SetValue(editor, 3);
            type.GetMethod("Preview_Tick", Private)!.Invoke(editor, new object?[] { null, EventArgs.Empty });
            check(!(bool)type.GetField("_playing", Private)!.GetValue(editor)!, "finite preview finishes in paused state");
            ((Button)editor.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check((int)type.GetField("_frame", Private)!.GetValue(editor)! == 0 && timer.IsEnabled, "play restarts completed finite preview");
            ((CheckBox)editor.FindName("EnabledCheck")).IsChecked = false;
            ((CheckBox)editor.FindName("EnabledCheck")).IsChecked = true;
            check(((TextBox)editor.FindName("ActionRowInput")).Text == "4" && rows[0].Text == "520", "action toggle preserves draft settings");
            name.Text = "";
            check(!((Button)editor.FindName("ExportButton")).IsEnabled, "invalid editor name blocks export");
            name.Text = "配置预览";
            var tabs = (TabControl)editor.FindName("ModeTabs");
            tabs.SelectedIndex = 1;
            ((ComboBox)editor.FindName("DirectionSelector")).SelectedIndex = 8;
            type.GetMethod("SelectCell", Private)!.Invoke(editor, new object[] { 9, 2 });
            check(((TextBox)editor.FindName("LookRowInput")).Text == "9" && grid.SelectedCount == 1 && grid.SelectedColumn == 2,
                "look direction maps to selected grid cell");
            ((CheckBox)editor.FindName("LookEnabledCheck")).IsChecked = false;
            check(((Image)editor.FindName("AnimationPreview")).Source is null && grid.SelectedCount == 0, "disabled look directions show no stale preview");
            ((CheckBox)editor.FindName("LookEnabledCheck")).IsChecked = true;
            check(grid.SelectedRow == 9 && grid.SelectedColumn == 2, "look toggle preserves draft direction coordinates");
            tabs.SelectedIndex = 0;
            if (renders is not null)
            {
                Render(editor, renders, "editor-desktop.png", 1004, 721);
                Render(editor, renders, "editor-compact.png", 784, 521);
            }
            check(((Button)editor.FindName("CopyButton")).IsEnabled, "valid edits can be saved after errors corrected");
        }
        finally
        {
            type.GetField("_dirtyInputs", Private)!.SetValue(editor, false);
            editor.Close();
            check(!((DispatcherTimer)type.GetField("_timer", Private)!.GetValue(editor)!).IsEnabled, "closing editor stops preview timer");
        }
    }

    private static void VerifyBackupsWindow(Action<bool, string> check, PetCatalog catalog, string? renders)
    {
        var window = new PetBackupsWindow(catalog);
        try
        {
            var list = (ListView)window.FindName("BackupSelector");
            check(list.Items.Count == catalog.ScanBackups().Backups.Count && list.Items.Count > 0, "backup window lists history and deleted copies");
            var filters = (ComboBox)window.FindName("FilterSelector");
            filters.SelectedIndex = 2;
            check(list.Items.Cast<PetBackupEntry>().All(b => b.Deleted) && list.Items.Count > 0, "backup deleted filter works");
            filters.SelectedIndex = 1;
            check(list.Items.Cast<PetBackupEntry>().All(b => !b.Deleted), "backup history filter works");
            filters.SelectedIndex = 0;
            if (renders is not null) Render(window, renders, "backups.png", 884, 531);
        }
        finally { window.Close(); }
    }

    private static void Render(Window window, string directory, string name, int width, int height)
    {
        Directory.CreateDirectory(directory);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen()) context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }

    private static void Bad(Action action, Action<bool, string> check, string name)
    {
        try { action(); }
        catch (InvalidDataException) { check(true, name); return; }
        throw new InvalidOperationException("Unexpectedly accepted: " + name);
    }
}
