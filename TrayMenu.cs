using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace YeShunguangPet;

public sealed class TrayMenu : ContextMenuStrip
{
    private readonly DesktopSession _desktop;
    private readonly SectionLabel _roles;
    private readonly ToolStripMenuItem _showAll, _hideAll, _recallAll, _quiet;
    private readonly ToolStripMenuItem _pinsMenu, _showPins, _hidePins, _closePins;
    private readonly ToolStripMenuItem _captureMenu;
    private Font? _menuFont, _detailFont, _glyphFont;
    private int _metricsDpi;
    private bool _ready;
    private Color _surface, _text, _secondary, _border, _hover;

    public TrayMenu(DesktopSession desktop)
    {
        _desktop = desktop;
        AutoSize = false;
        ShowImageMargin = ShowCheckMargin = false;
        ShowItemToolTips = false;
        DropShadowEnabled = true;
        Renderer = new MenuRenderer(this);
        AccessibleName = "桌面宠物托盘菜单";
        Items.Add(new SectionLabel("常用入口"));
        AddCommand("manager", "设置", desktop.OpenManager);
        AddCommand("focus", "专注计时", desktop.OpenFocus);
        _captureMenu = new ToolStripMenuItem("截图") { Name = "capture", AutoSize = false };
        Items.Add(_captureMenu); _captureMenu.DropDown.Renderer = new MenuRenderer(this);
        AddSubCommand(_captureMenu, "区域截图", desktop.StartCapture).Tag = ShortcutAction.Capture;
        AddSubCommand(_captureMenu, "当前屏幕", desktop.StartCurrentScreenCapture).Tag = ShortcutAction.CaptureCurrentScreen;
        AddSubCommand(_captureMenu, "全部屏幕", desktop.StartAllScreensCapture).Tag = ShortcutAction.CaptureAllScreens;
        _pinsMenu = new ToolStripMenuItem("贴图") { Name = "pins", AutoSize = false };
        Items.Add(_pinsMenu);
        _pinsMenu.DropDown.Renderer = new MenuRenderer(this);
        AddSubCommand(_pinsMenu, "从剪贴板贴图", desktop.PastePin);
        _showPins = AddSubCommand(_pinsMenu, "显示全部贴图", desktop.ShowPins);
        _hidePins = AddSubCommand(_pinsMenu, "隐藏全部贴图", desktop.HidePins);
        _closePins = AddSubCommand(_pinsMenu, "关闭全部贴图...", () => { if (MessageBox.Show("关闭全部贴图？未保存的贴图不会恢复。", "关闭贴图", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK) desktop.ClosePins(); });
        Items.Add(new ToolStripSeparator());
        _roles = new SectionLabel("桌面角色");
        Items.Add(_roles);
        _showAll = AddCommand("showAll", "显示全部", desktop.ShowAll);
        _hideAll = AddCommand("hideAll", "隐藏全部", desktop.HideAll);
        _recallAll = AddCommand("recallAll", "召回全部", desktop.RecallAll);
        _quiet = AddCommand("quiet", "勿扰模式", () => desktop.SetQuiet(!desktop.Companion.Settings.DoNotDisturb));
        Items.Add(new ToolStripSeparator());
        AddCommand("exit", "退出程序", desktop.RequestExit);
        _ready = true;
        ApplyMetrics(DeviceDpi);
        RefreshTheme();
        RefreshState();
        _desktop.Changed += RefreshState;
        UiTheme.Changed += RefreshTheme;
    }

    private ToolStripMenuItem AddCommand(string name, string caption, Action command)
    {
        var item = new ToolStripMenuItem(caption) { Name = name, AccessibleName = caption, AutoSize = false };
        item.Click += (_, _) =>
        {
            Close(ToolStripDropDownCloseReason.ItemClicked);
            try { command(); }
            catch (Exception ex)
            {
                AppLogger.Error("Tray command failed: " + name, ex);
                MessageBox.Show(ex.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        Items.Add(item);
        return item;
    }

    private ToolStripMenuItem AddSubCommand(ToolStripMenuItem parent, string caption, Action command)
    {
        var item = new ToolStripMenuItem(caption) { AutoSize = false };
        item.Click += (_, _) =>
        {
            parent.HideDropDown(); Close();
            try { command(); } catch (Exception ex) { MessageBox.Show(ex.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        parent.DropDownItems.Add(item); return item;
    }

    private void RefreshState()
    {
        if (IsDisposed) return;
        var count = _desktop.Windows.Count;
        _roles.Detail = $"{count} 个角色";
        _roles.AccessibleName = $"桌面角色，{count} 个角色";
        _showAll.Enabled = _hideAll.Enabled = _recallAll.Enabled = count > 0;
        _quiet.Checked = _desktop.Companion.Settings.DoNotDisturb;
        _recallAll.ShortcutKeyDisplayString = _desktop.ShortcutHint(ShortcutAction.RecallAll);
        ((ToolStripMenuItem)Items.Find("focus", false).Single()).ShortcutKeyDisplayString = _desktop.ShortcutHint(ShortcutAction.OpenFocus);
        foreach (ToolStripMenuItem item in _captureMenu.DropDownItems) if (item.Tag is ShortcutAction action) item.ShortcutKeyDisplayString = _desktop.ShortcutHint(action);
        _showPins.Enabled = _hidePins.Enabled = _closePins.Enabled = _desktop.PinCount > 0;
        _pinsMenu.Text = _desktop.PinCount == 0 ? "贴图" : $"贴图 ({_desktop.PinCount})";
        Invalidate();
    }

    private void RefreshTheme()
    {
        if (IsDisposed) return;
        var colors = UiTheme.GetColors();
        Color Read(string name) { var c = colors[name]; return Color.FromArgb(c.A, c.R, c.G, c.B); }
        _surface = Read("SurfaceBrush");
        _text = Read("TextBrush");
        _secondary = Read("SecondaryTextBrush");
        _border = Read("BorderBrush");
        _hover = Read("HoverBrush");
        BackColor = _surface;
        ForeColor = _text;
        foreach (var group in new[] { _captureMenu, _pinsMenu }) { group.DropDown.BackColor = _surface; group.DropDown.ForeColor = _text; }
        UpdateOutline();
        Invalidate();
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        ApplyMetrics(DeviceDpi);
        RefreshTheme();
        RefreshState();
        base.OnOpening(e);
    }

    protected override void RescaleConstantsForDpi(int oldDpi, int newDpi)
    {
        base.RescaleConstantsForDpi(oldDpi, newDpi);
        if (_ready) ApplyMetrics(newDpi);
    }

    private void ApplyMetrics(int dpi)
    {
        if (!_ready) return;
        SuspendLayout();
        try
        {
            if (_metricsDpi != dpi)
            {
                _metricsDpi = Math.Max(96, dpi);
                var oldMenuFont = _menuFont;
                _menuFont = new Font("Segoe UI", Px(14), FontStyle.Regular, GraphicsUnit.Pixel);
                Font = _menuFont;
                oldMenuFont?.Dispose();
                _detailFont?.Dispose();
                _detailFont = new Font("Segoe UI", Px(12), FontStyle.Regular, GraphicsUnit.Pixel);
                _glyphFont?.Dispose();
                _glyphFont = new Font("Segoe MDL2 Assets", Px(12), FontStyle.Regular, GraphicsUnit.Pixel);
            }
            Padding = new Padding(Px(6), Px(8), Px(6), Px(8));
            var width = Px(288);
            foreach (ToolStripItem item in Items)
            {
                item.AutoSize = false;
                item.Margin = Padding.Empty;
                item.Size = new Size(width - Padding.Horizontal, Px(item is SectionLabel ? 28 : item is ToolStripSeparator ? 12 : 36));
            }
            var contentHeight = Items.Cast<ToolStripItem>().Sum(item => item.Height);
            var minimumHeight = Padding.Vertical + contentHeight + Px(4);
            MinimumSize = new Size(width, minimumHeight);
            Size = new Size(width, minimumHeight);
            foreach (var group in new[] { _captureMenu, _pinsMenu })
            {
                group.DropDown.Padding = new Padding(Px(6)); group.DropDown.AutoSize = false;
                foreach (ToolStripItem child in group.DropDownItems) { child.Font = _menuFont; child.AutoSize = false; child.Margin = Padding.Empty; child.Size = new Size(width - Px(12), Px(36)); }
                group.DropDown.Size = new Size(width, Px(12 + 36 * group.DropDownItems.Count));
            }
        }
        finally { ResumeLayout(); }
        PerformLayout();
        var baseWidth = Px(288);
        var requiredBottom = Items.Cast<ToolStripItem>().Select(item => item.Bounds.Bottom).DefaultIfEmpty(Padding.Top).Max();
        var safeHeight = Math.Max(Size.Height, requiredBottom + Padding.Bottom + Px(2));
        if (Width != baseWidth || safeHeight != Height)
        {
            Size = new Size(baseWidth, safeHeight);
        }
        UpdateOutline();
    }

    private int Px(int value) => (int)Math.Round(value * Math.Max(96, _metricsDpi) / 96.0);
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (_ready) UpdateOutline(); }
    private void UpdateOutline()
    {
        if (Width <= 0 || Height <= 0) return;
        var old = Region;
        if (SystemInformation.HighContrast) Region = null;
        else
        {
            using var path = Rounded(new RectangleF(0, 0, Width, Height), Px(8));
            Region = new Region(path);
        }
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UiTheme.Changed -= RefreshTheme;
            _desktop.Changed -= RefreshState;
        }
        base.Dispose(disposing);
        if (disposing) { _menuFont?.Dispose(); _detailFont?.Dispose(); _glyphFont?.Dispose(); }
    }

    private static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var size = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (size <= 0) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.Left, rect.Top, size, size, 180, 90);
        path.AddArc(rect.Right - size, rect.Top, size, size, 270, 90);
        path.AddArc(rect.Right - size, rect.Bottom - size, size, size, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - size, size, size, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class SectionLabel : ToolStripLabel
    {
        public string Detail { get; set; } = string.Empty;
        public SectionLabel(string text) : base(text) { AccessibleRole = AccessibleRole.StaticText; }
    }

    private sealed class MenuRenderer(TrayMenu menu) : ToolStripProfessionalRenderer
    {
        private const TextFormatFlags TextFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(menu._surface);
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Rounded(new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), menu.Px(8));
            using var pen = new Pen(menu._border);
            if (SystemInformation.HighContrast) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            else e.Graphics.DrawPath(pen, path);
            e.Graphics.Restore(state);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var left = menu.Px(12) - e.Item.Bounds.Left;
            using var path = Rounded(new RectangleF(left, 0, (e.ToolStrip?.Width ?? menu.Width) - menu.Px(24), e.Item.Height), menu.Px(5));
            using var fill = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Highlight : menu._hover);
            e.Graphics.FillPath(fill, path);
            e.Graphics.Restore(state);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Native menu items and labels use different layout origins.
            var bounds = new Rectangle(menu.Px(20) - e.Item.Bounds.Left, 0, (e.ToolStrip?.Width ?? menu.Width) - menu.Px(40), e.Item.Height);
            if (e.Item is SectionLabel section)
            {
                var detailWidth = string.IsNullOrEmpty(section.Detail) ? 0 : TextRenderer.MeasureText(e.Graphics, section.Detail, menu._detailFont, Size.Empty, TextFlags).Width + menu.Px(12);
                TextRenderer.DrawText(e.Graphics, section.Text, menu._detailFont, bounds with { Width = bounds.Width - detailWidth }, menu._secondary, TextFlags);
                TextRenderer.DrawText(e.Graphics, section.Detail, menu._detailFont, bounds, menu._secondary, TextFlags | TextFormatFlags.Right);
                return;
            }
            if (e.Item is not ToolStripMenuItem item || e.Text != item.Text) return;
            var selectedHighContrast = item.Enabled && item.Selected && SystemInformation.HighContrast;
            var textColor = selectedHighContrast ? SystemColors.HighlightText : item.Enabled ? menu._text : menu._secondary;
            var detailColor = selectedHighContrast ? SystemColors.HighlightText : menu._secondary;
            var shortcut = item.ShortcutKeyDisplayString;
            var trailing = !string.IsNullOrEmpty(shortcut) ? TextRenderer.MeasureText(e.Graphics, shortcut, menu._detailFont, Size.Empty, TextFlags).Width + menu.Px(20) : item.Checked || item.HasDropDownItems ? menu.Px(26) : 0;
            TextRenderer.DrawText(e.Graphics, item.Text, menu._menuFont, bounds with { Width = bounds.Width - trailing }, textColor, TextFlags);
            if (!string.IsNullOrEmpty(shortcut)) TextRenderer.DrawText(e.Graphics, shortcut, menu._detailFont, bounds, detailColor, TextFlags | TextFormatFlags.Right);
            if (item.Checked) TextRenderer.DrawText(e.Graphics, "\uE73E", menu._glyphFont, bounds, textColor, TextFlags | TextFormatFlags.Right);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(menu._border);
            e.Graphics.DrawLine(pen, menu.Px(12) - e.Item.Bounds.Left, e.Item.Height / 2, menu.Width - menu.Px(12) - e.Item.Bounds.Left, e.Item.Height / 2);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e) { }
    }
}
