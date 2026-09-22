using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace YeShunguangPet;

public sealed partial class DesktopSession
{
    private Drawing.Icon? _noticeIcon;
    private long _ignoreTrayDoubleClickUntil;
    private void UpdateCompletionTray()
    {
        if (_disposed || _tray is null) return;
        if (Companion.PendingCompletion is { } phase)
        {
            _noticeIcon ??= CompletionBadge.CreateTrayIcon(_icon ?? Drawing.SystemIcons.Application);
            _tray.Icon = _noticeIcon;
            _tray.Text = phase == SessionPhase.Focus ? "专注完成" : "休息完成";
        }
        else { _tray.Icon = _icon ?? Drawing.SystemIcons.Application; _tray.Text = "叶瞬光桌面宠物"; }
    }
    private void TrayClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Left || Companion.PendingCompletion is null || _capturing) return;
        _ignoreTrayDoubleClickUntil = Environment.TickCount64 + Forms.SystemInformation.DoubleClickTime + 100;
        OpenFocus();
    }
}
