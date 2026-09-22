using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace YeShunguangPet;

public static class UiTheme
{
    private static readonly List<WeakReference<ThemedWindow>> Windows = new();
    private static readonly Uri StylesUri = new("/YeShunguangPet;component/Themes/Controls.xaml", UriKind.Relative);
    private static AppearanceOptions _current = new();
    private static bool _applicationStylesInstalled;
    private static Dispatcher? _dispatcher;
    internal static event Action? Changed;
    public static AppearanceOptions Current => _current.Clone();
    public static bool MotionEnabled => !_current.ReduceMotion && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    public static bool IsDark => _current.Theme == "dark" || (_current.Theme == "system" && !SystemUsesLightTheme());

    static UiTheme()
    {
        SystemEvents.UserPreferenceChanged += (_, _) => RefreshWindows();
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation)) RefreshWindows();
        };
    }

    public static void Apply(AppearanceOptions options)
    {
        _dispatcher ??= Dispatcher.CurrentDispatcher;
        _current = options.Clone();
        _current.Normalize();
        InstallApplicationResources();
        RefreshWindows();
    }

    internal static void Register(ThemedWindow window)
    {
        _dispatcher ??= window.Dispatcher;
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = StylesUri });
        Windows.Add(new WeakReference<ThemedWindow>(window));
        Update(window);
    }

    internal static void Unregister(ThemedWindow window) => Windows.RemoveAll(reference => !reference.TryGetTarget(out var target) || target == window);

    private static void InstallApplicationResources()
    {
        if (Application.Current is not { } app) return;
        if (!app.Dispatcher.CheckAccess()) { app.Dispatcher.BeginInvoke(InstallApplicationResources); return; }
        if (!_applicationStylesInstalled)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = StylesUri });
            _applicationStylesInstalled = true;
        }
        SetPalette(app.Resources);
    }

    private static void RefreshWindows()
    {
        var dispatcher = Application.Current?.Dispatcher ?? _dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(RefreshWindows); return; }
        InstallApplicationResources();
        foreach (var reference in Windows.ToArray())
            if (reference.TryGetTarget(out var window))
            {
                if (window.Dispatcher.CheckAccess()) Update(window);
                else window.Dispatcher.BeginInvoke(() => Update(window));
            }
        Windows.RemoveAll(reference => !reference.TryGetTarget(out _));
        Changed?.Invoke();
    }

    internal static void Update(ThemedWindow window)
    {
        SetPalette(window.Resources);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            var dark = IsDark ? 1 : 0;
            try { DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        window.OnThemeUpdated();
    }

    internal static void MatchTitleBarBackground(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || window.Background is not SolidColorBrush brush) return;
        var color = brush.Color.R | (brush.Color.G << 8) | (brush.Color.B << 16);
        if (SystemParameters.HighContrast) color = -1;
        try { DwmSetWindowAttribute(handle, 35, ref color, sizeof(int)); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    internal static void SetPalette(ResourceDictionary resources)
    {
        foreach (var (key, color) in GetColors()) { var brush = new SolidColorBrush(color); brush.Freeze(); resources[key] = brush; }
    }

    internal static IReadOnlyDictionary<string, Color> GetColors()
    {
        var dark = IsDark;
        var accent = (Color)ColorConverter.ConvertFromString(_current.Accent);
        var surface = ColorOf(dark ? "#202124" : "#FFFFFF");
        var colors = new Dictionary<string, Color>
        {
            ["SurfaceBrush"] = surface,
            ["SidebarBrush"] = ColorOf(dark ? "#191B1F" : "#F3F4F6"),
            ["SubtleBrush"] = ColorOf(dark ? "#292C31" : "#F6F7F9"),
            ["HoverBrush"] = ColorOf(dark ? "#34373D" : "#EEF0F3"),
            ["TextBrush"] = ColorOf(dark ? "#F2F3F5" : "#23252A"),
            ["SecondaryTextBrush"] = ColorOf(dark ? "#A6ADB8" : "#6C727C"),
            ["BorderBrush"] = ColorOf(dark ? "#383B42" : "#E7E9ED"),
            ["AccentBrush"] = accent,
            ["AccentTextBrush"] = dark ? Blend(accent, Colors.White, 0.45) : accent,
            ["AccentSoftBrush"] = Blend(surface, accent, dark ? 0.14 : 0.06),
            ["OnAccentBrush"] = Colors.White,
            ["SuccessBrush"] = ColorOf(dark ? "#7ED3AD" : "#287A5C"),
            ["DangerBrush"] = ColorOf(dark ? "#FF8193" : "#B2263E"),
            ["SwitchOffBrush"] = ColorOf(dark ? "#535861" : "#D9DDE3")
        };
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "SurfaceBrush", "SidebarBrush", "SubtleBrush", "AccentSoftBrush", "HoverBrush" }) colors[key] = SystemColors.WindowColor;
            foreach (var key in new[] { "TextBrush", "SecondaryTextBrush", "SuccessBrush", "DangerBrush", "AccentTextBrush" }) colors[key] = SystemColors.WindowTextColor;
            colors["BorderBrush"] = SystemColors.WindowTextColor;
            colors["AccentBrush"] = SystemColors.HighlightColor;
            colors["OnAccentBrush"] = SystemColors.HighlightTextColor;
        }
        return colors;
    }

    private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color Blend(Color first, Color second, double amount) => Color.FromRgb(
        (byte)(first.R + (second.R - first.R) * amount), (byte)(first.G + (second.G - first.G) * amount), (byte)(first.B + (second.B - first.B) * amount));
    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch { return true; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

public class ThemedWindow : Window
{
    public ThemedWindow()
    {
        UiTheme.Register(this);
        SetResourceReference(FontFamilyProperty, "AppFont");
        FontSize = 14;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SourceInitialized += (_, _) => UiTheme.Update(this);
    }

    internal virtual void OnThemeUpdated() { }
    protected override void OnClosed(EventArgs e) { UiTheme.Unregister(this); base.OnClosed(e); }
}
