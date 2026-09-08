using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using YeShunguangPet;
using Forms = System.Windows.Forms;

internal static class TrayMenuTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Action<bool, string> check, PetCatalog source, string folder)
    {
        Directory.CreateDirectory(folder);
        var config = DesktopConfiguration.Migrate(new PetSettings
        {
            Left = 40, Top = 80, Scale = 0.5, Topmost = false, ClickThrough = true,
            LookAtMouse = false, RandomIdleActions = false, DesktopRoaming = false
        });
        config.Appearance = new AppearanceOptions { Theme = "light", ReduceMotion = true };
        var catalog = new PetCatalog(source.BundledDirectory, Path.Combine(folder, "tray-test-users"));
        var saves = 0;
        using var desktop = new DesktopSession(config, catalog, _ => saves++, nativeIntegration: false);
        desktop.Start(showWindows: false);
        using var menu = new TrayMenu(desktop);
        Forms.ToolStripMenuItem Item(string name) => (Forms.ToolStripMenuItem)menu.Items.Find(name, false).Single();
        check(menu.Items.OfType<Forms.ToolStripMenuItem>().Select(i => i.Name).SequenceEqual(new[] { "manager", "focus", "showAll", "hideAll", "recallAll", "quiet", "exit" }), "tray restyle preserves all seven commands and ordering");
        check(!menu.ShowImageMargin && !menu.ShowCheckMargin && menu.AutoClose, "tray menu removes legacy gutters but preserves native dismissal");
        using (var nativeMenu = new Forms.ContextMenuStrip { DropShadowEnabled = true })
            check(menu.DropShadowEnabled == nativeMenu.DropShadowEnabled, "tray shadow follows the native system policy");
        check(Item("recallAll").ShortcutKeys == Forms.Keys.None && Item("recallAll").ShortcutKeyDisplayString == "Ctrl+Alt+Y", "tray shortcut display does not register another global hotkey");
        var first = desktop.Windows.Single();
        var work = Forms.Screen.PrimaryScreen!.WorkingArea;
        foreach (var theme in new[] { "light", "dark" })
        {
            desktop.UpdateAppearance(new AppearanceOptions { Theme = theme, ReduceMotion = true });
            menu.Show(new Point(work.Right - 2, work.Bottom - 2));
            Pump();
            check(menu.Visible && menu.Bounds.Left >= work.Left && menu.Bounds.Right <= work.Right && menu.Bounds.Top >= work.Top && menu.Bounds.Bottom <= work.Bottom,
                $"native {theme} tray popup remains inside monitor work area");
            check(menu.BackColor.R == (theme == "dark" ? 32 : 255), $"tray menu adopts {theme} appearance");
            check(menu.Region is not null && !menu.Region.IsVisible(0, 0) && menu.Region.IsVisible(menu.Width / 2, menu.Height / 2), $"{theme} tray outline has real rounded corners");
            var height = menu.Height;
            var width = menu.Width;
            Item("quiet").Select();
            Pump();
            Render(menu, folder, $"tray-{theme}-hover.png");
            check(menu.Height == height && menu.Width == width, "tray hover never changes menu geometry");
            var consumed = (bool)typeof(Forms.ToolStripDropDown).GetMethod("ProcessDialogKey", Private)!.Invoke(menu, new object[] { Forms.Keys.Escape })!;
            Pump();
            check(consumed && !menu.Visible, "Escape dismisses native tray menu without invoking a command");
            foreach (var dpi in new[] { 96, 144, 192 })
            {
                typeof(TrayMenu).GetMethod("ApplyMetrics", Private)!.Invoke(menu, new object[] { dpi });
                menu.PerformLayout();
                check(menu.Width == 288 * dpi / 96 && Item("quiet").Height == 36 * dpi / 96, $"tray dimensions scale consistently at {dpi} DPI");
                var rows = menu.Items.Cast<Forms.ToolStripItem>().ToArray();
                check(rows.Zip(rows.Skip(1), (a, b) => a.Bounds.Bottom <= b.Bounds.Top).All(x => x) && rows.All(i => i.Bounds.Right <= menu.Width && i.Bounds.Bottom <= menu.Height), $"tray rows do not overlap or clip at {dpi} DPI");
                Render(menu, folder, $"tray-{theme}-{dpi}dpi.png");
            }
        }
        menu.Show(new Point(work.Right - 2, work.Bottom - 2));
        desktop.UpdateAppearance(new AppearanceOptions { Theme = "light", ReduceMotion = true });
        Pump();
        check(menu.BackColor.R == 255, "already open tray menu follows theme changes");
        var before = saves;
        Item("quiet").PerformClick();
        check(desktop.Companion.Settings.DoNotDisturb && Item("quiet").Checked && saves == before + 1, "tray quiet command toggles and persists once");
        menu.Show(new Point(work.Right - 2, work.Bottom - 2));
        Pump();
        Render(menu, folder, "tray-light-checked.png");
        check(Item("quiet").Checked, "reopening tray menu retains actual quiet state");
        Item("quiet").PerformClick();
        check(!desktop.Companion.Settings.DoNotDisturb && !Item("quiet").Checked, "tray quiet command can turn off again");
        Item("hideAll").PerformClick();
        check(desktop.IsHidden(first) && !first.IsVisible, "tray hide-all command preserves instance and hides it");
        Item("showAll").PerformClick();
        Pump();
        check(!desktop.IsHidden(first) && first.IsVisible, "tray show-all command restores existing instance");
        Item("recallAll").PerformClick();
        check(!config.Pets.Single().ClickThrough && !desktop.IsHidden(first), "tray recall retains click-through recovery behavior");
        desktop.Remove(first);
        check(!Item("showAll").Enabled && !Item("hideAll").Enabled && !Item("recallAll").Enabled && Item("manager").Enabled, "zero-role tray state disables only unavailable role commands");
        var exitRequested = false;
        desktop.ExitRequested += () => exitRequested = true;
        Item("exit").PerformClick();
        check(exitRequested && desktop.Windows.Count == 0, "tray exit still requests orderly application shutdown");
        menu.Dispose();
        UiTheme.Apply(new AppearanceOptions());
        check(menu.IsDisposed, "tray disposal releases native control and theme subscription safely");
    }

    private static void Pump()
    {
        Forms.Application.DoEvents();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static void Render(TrayMenu menu, string folder, string name)
    {
        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
        bitmap.Save(Path.Combine(folder, name), ImageFormat.Png);
    }
}
