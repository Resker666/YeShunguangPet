using System;
using System.Collections.Generic;
using System.Windows;

namespace YeShunguangPet;

public sealed partial class DesktopSession
{
    private readonly Dictionary<string, AiChatWindow> _chatWindows = new(StringComparer.Ordinal);

    public void OpenChat(PetPackage pet, Window owner, bool editProfile = false)
    {
        if (_disposed) return;
        if (_chatWindows.TryGetValue(pet.Manifest.Id, out var existing)) { existing.Activate(); return; }
        try
        {
            _speech.Dismiss();
            var dialog = new AiChatWindow(this, pet) { Owner = owner };
            if (editProfile) ((System.Windows.Controls.TabControl)dialog.FindName("ChatTabs")).SelectedIndex = 1;
            _chatWindows.Add(pet.Manifest.Id, dialog);
            try { dialog.ShowDialog(); }
            finally { _chatWindows.Remove(pet.Manifest.Id); }
        }
        catch (Exception ex) { AppDialog.Show(owner, "无法打开角色聊天：" + ex.Message, "角色聊天", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
