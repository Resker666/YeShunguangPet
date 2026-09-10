using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace YeShunguangPet;

public sealed partial class DesktopSession : IDisposable
{
    private readonly Dictionary<string, MainWindow> _windows = new();
    private readonly Action<DesktopConfiguration> _save;
    private readonly bool _nativeIntegration;
    private Window? _hotkeyWindow;
    private HwndSource? _source;
    private WinForms.NotifyIcon? _tray;
    private TrayMenu? _trayMenu;
    private Drawing.Icon? _icon;
    private PetManagerWindow? _manager;
    private DiagnosticsWindow? _diagnosticsWindow;
    private MainWindow? _settingsOwner;
    private bool _disposed;
    private bool _started;
    private readonly DesktopSpeech _speech;
    public DesktopConfiguration Configuration { get; }
    public CompanionRuntime Companion { get; }
    public PetCatalog Catalog { get; }
    internal TimeProvider Clock { get; }
    public bool HotkeyRegistered => _shortcuts?.IsRegistered(ShortcutAction.RecallAll) == true;
    public IReadOnlyCollection<MainWindow> Windows => _windows.Values;
    public event Action? Changed;
    public event Action? ExitRequested;

    public DesktopSession(DesktopConfiguration configuration, PetCatalog catalog, Action<DesktopConfiguration> save, bool nativeIntegration = true, TimeProvider? clock = null, StudyHistory? history = null, IShortcutRegistrar? shortcutRegistrar = null)
    {
        configuration.Validate();
        Configuration = configuration;
        UiTheme.Apply(configuration.Appearance);
        Catalog = catalog;
        _save = save;
        _nativeIntegration = nativeIntegration;
        Clock = clock ?? TimeProvider.System;
        var globalSettings = new PetSettings();
        configuration.Companion.ApplyTo(globalSettings);
        Companion = new CompanionRuntime(globalSettings, clock, history, PersistCompanionDurations, configuration.FocusWindow, PersistFocusWindow);
        Companion.DurationsChanged += () =>
        {
            foreach (var window in Windows) window.ApplyGlobal(Configuration.Companion);
            Changed?.Invoke();
        };
        _speech = new DesktopSpeech(this, clock);
        Companion.Notification += ShowNotification;
        Catalog.IsPetInUse = id => _windows.Values.Any(w => w.Package.Manifest.Id == id);
        if (shortcutRegistrar is not null) { _shortcuts = new GlobalShortcuts(shortcutRegistrar); _shortcuts.Initialize(Configuration.Shortcuts); }
    }

    public void Start(bool showWindows = true)
    {
        if (_started || _disposed) return;
        _started = true;
        if (_nativeIntegration) CreateNativeControls();
        foreach (var instance in Configuration.Pets.ToArray()) CreateWindow(instance, showWindows);
        Companion.Start();
        Persist();
        if (_nativeIntegration && Windows.Count == 0) OpenManager();
    }

    private MainWindow CreateWindow(PetInstanceOptions instance, bool show, PetPackage? prepared = null)
    {
        string? warning = null;
        var package = prepared ?? Catalog.LoadPreferred(instance.PetId, out warning);
        instance.PetId = package.Manifest.Id;
        var window = new MainWindow(instance.ToSettings(Configuration.Companion), package, this, instance.InstanceId) { ShowActivated = false };
        _windows.Add(instance.InstanceId, window);
        if (show && !instance.Hidden) window.Show();
        if (warning is not null) AppLogger.Info(warning);
        return window;
    }

    public MainWindow Add(string petId, bool show = true)
    {
        CheckCanAdd();
        var entry = Catalog.Scan().Pets.FirstOrDefault(p => p.Id == petId) ?? throw new InvalidOperationException("皮肤不存在，请刷新列表。");
        return AddPrepared(PetPackage.Load(entry.ManifestPath), show);
    }

    public async Task<MainWindow> AddAsync(string petId, CancellationToken cancellationToken = default, bool show = true)
    {
        CheckCanAdd();
        var scan = await Catalog.ScanAsync(cancellationToken);
        var entry = scan.Pets.FirstOrDefault(p => p.Id == petId) ?? throw new InvalidOperationException("皮肤不存在，请刷新列表。");
        var package = await Task.Run(() => PetPackage.Load(entry.ManifestPath), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return AddPrepared(package, show);
    }

    private void CheckCanAdd()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DesktopSession));
        if (Configuration.Pets.Count >= DesktopConfiguration.MaximumPets) throw new InvalidOperationException("最多同时保留 3 个角色，请先关闭一个角色。");
        if (_settingsOwner is not null) throw new InvalidOperationException("请先关闭角色设置窗口。");
    }

    private MainWindow AddPrepared(PetPackage package, bool show)
    {
        CheckCanAdd();
        var instance = new PetInstanceOptions { PetId = package.Manifest.Id };
        Configuration.Pets.Add(instance);
        try
        {
            var window = CreateWindow(instance, show, package);
            Persist();
            Changed?.Invoke();
            return window;
        }
        catch
        {
            Configuration.Pets.Remove(instance);
            if (_windows.Remove(instance.InstanceId, out var failed)) { failed.PrepareForApplicationShutdown(); failed.Close(); }
            throw;
        }
    }

    public void Remove(MainWindow window)
    {
        if (_settingsOwner is not null) throw new InvalidOperationException("请先关闭角色设置窗口。");
        if (!_windows.Remove(window.InstanceId)) return;
        Configuration.Pets.RemoveAll(p => p.InstanceId == window.InstanceId);
        window.PrepareForApplicationShutdown();
        window.Close();
        Persist();
        Changed?.Invoke();
    }

    internal void Capture(MainWindow window)
    {
        if (_disposed) return;
        _speech.Dismiss(window);
        var instance = Configuration.Pets.FirstOrDefault(p => p.InstanceId == window.InstanceId);
        if (instance is null) return;
        instance.Capture(window.InstanceSettings);
        Persist();
        Changed?.Invoke();
    }

    internal void SetHidden(MainWindow window, bool hidden)
    {
        var instance = Configuration.Pets.FirstOrDefault(p => p.InstanceId == window.InstanceId);
        if (instance is not null) { instance.Hidden = hidden; Capture(window); }
    }

    public bool IsHidden(MainWindow window) => Configuration.Pets.FirstOrDefault(p => p.InstanceId == window.InstanceId)?.Hidden ?? true;
    public int Number(MainWindow window) => Configuration.Pets.FindIndex(p => p.InstanceId == window.InstanceId) + 1;
    internal double PlacementOffset(MainWindow window) => _windows.Values.TakeWhile(w => w != window).Sum(w => double.IsNaN(w.Width) ? 208 : w.Width + 16);

    internal void UpdateGlobal(PetSettings updated)
    {
        updated.Normalize();
        Configuration.Companion = CompanionOptions.From(updated);
        Configuration.Companion.ApplyTo(Companion.Settings);
        foreach (var window in Windows) window.ApplyGlobal(Configuration.Companion);
        Companion.Configure();
        Persist();
        Changed?.Invoke();
    }

    private void PersistCompanionDurations(PetSettings draft)
    {
        if (HasSettingsOpen) throw new InvalidOperationException("请先关闭角色设置窗口，再调整时长。");
        var previous = Configuration.Companion;
        Configuration.Companion = CompanionOptions.From(draft);
        try { Persist(); }
        catch { Configuration.Companion = previous; throw; }
    }

    private void PersistFocusWindow(FocusWindowOptions options)
    {
        var previous = Configuration.FocusWindow;
        Configuration.FocusWindow = options.Copy();
        try { Persist(); }
        catch { Configuration.FocusWindow = previous; throw; }
    }

    public void SetQuiet(bool quiet)
    {
        var settings = Companion.Settings.Clone();
        settings.DoNotDisturb = quiet;
        UpdateGlobal(settings);
    }

    public void UpdateAppearance(AppearanceOptions options)
    {
        var previous = Configuration.Appearance;
        Configuration.Appearance = options.Clone();
        Configuration.Appearance.Normalize();
        try { Persist(); }
        catch { Configuration.Appearance = previous; throw; }
        UiTheme.Apply(Configuration.Appearance);
        Changed?.Invoke();
    }

    internal bool HasSettingsOpen => _settingsOwner is not null;
    internal void Speak(MainWindow owner, SpeechEvent trigger) => _speech.Request(owner, trigger);
    internal void DismissSpeech(MainWindow? owner = null) => _speech.Dismiss(owner);
    public void UpdateSpeech(SpeechOptions options)
    {
        var previous = Configuration.Speech;
        var next = options.Clone();
        next.Normalize();
        Configuration.Speech = next;
        try { Persist(); } catch { Configuration.Speech = previous; throw; }
        _speech.Dismiss();
        Changed?.Invoke();
    }

    internal bool BeginSettings(MainWindow window)
    {
        if (_settingsOwner is not null) { _settingsOwner.ActivateSettings(); return false; }
        _settingsOwner = window;
        _speech.Dismiss();
        Changed?.Invoke();
        return true;
    }

    internal void EndSettings() { _settingsOwner = null; Changed?.Invoke(); }
    private void Persist() { if (!_disposed) _save(Configuration); }

    public void ShowAll() { foreach (var window in Windows.ToArray()) window.ShowAndActivate(); }
    public void HideAll() { foreach (var window in Windows.ToArray()) window.HideInstance(); }
    public void RecallAll() { foreach (var window in Windows.ToArray()) window.RecallToPrimaryScreen(); }
    public void ToggleAll() { if (Windows.Any(w => !IsHidden(w))) HideAll(); else ShowAll(); }

    public void OpenManager()
    {
        AppLogger.Info("Opening control center.");
        if (_manager is not null) { if (_manager.WindowState == WindowState.Minimized) _manager.WindowState = WindowState.Normal; _manager.Activate(); return; }
        _manager = new PetManagerWindow(this);
        _manager.Closed += (_, _) => _manager = null;
        _manager.Show();
    }

    public void OpenFocus()
    {
        AppLogger.Info("Opening focus panel.");
        var first = Windows.FirstOrDefault();
        var pet = first?.Package ?? Catalog.LoadPreferred(PetPackage.DefaultId, out _);
        Companion.Open(pet, first?.InstanceId);
    }

    public void OpenDiagnostics(Window? owner = null)
    {
        if (_disposed) return;
        if (_diagnosticsWindow is null)
        {
            _diagnosticsWindow = new DiagnosticsWindow(this) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
            _diagnosticsWindow.Closed += (_, _) => _diagnosticsWindow = null;
            _diagnosticsWindow.Show();
        }
        else
        {
            if (_diagnosticsWindow.WindowState == WindowState.Minimized) _diagnosticsWindow.WindowState = WindowState.Normal;
            _diagnosticsWindow.Activate();
        }
    }

    public void OpenSpeechSettings(PetPackage pet, Window? owner = null)
    {
        if (_disposed) return;
        _speech.Dismiss();
        var dialog = new SpeechSettingsWindow(this, pet);
        if (owner is not null) dialog.Owner = owner;
        dialog.ShowDialog();
    }

    public void RequestExit()
    {
        if (_settingsOwner is not null) { _settingsOwner.ActivateSettings(); return; }
        var exit = ExitRequested;
        try { Dispose(); }
        finally { exit?.Invoke(); }
    }

    private void CreateNativeControls()
    {
        _hotkeyWindow = new Window { ShowInTaskbar = false, Width = 1, Height = 1, WindowStyle = WindowStyle.None };
        var handle = new WindowInteropHelper(_hotkeyWindow).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(HotkeyHook);
        if (_shortcuts is null)
        {
            _shortcuts = new GlobalShortcuts(new NativeShortcutRegistrar(_hotkeyWindow));
            _shortcuts.Initialize(Configuration.Shortcuts);
            foreach (var action in Enum.GetValues<ShortcutAction>())
                if (_shortcuts.Failure(action) is { } failure) AppLogger.Info(failure);
        }
        _trayMenu = new TrayMenu(this);
        _icon = Environment.ProcessPath is { } path ? Drawing.Icon.ExtractAssociatedIcon(path) : null;
        _tray = new WinForms.NotifyIcon { Icon = _icon ?? Drawing.SystemIcons.Application, Text = "叶瞬光桌面宠物", ContextMenuStrip = _trayMenu, Visible = true };
        _tray.DoubleClick += (_, _) => ToggleAll();
        _tray.BalloonTipClicked += (_, _) => OpenFocus();
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey) handled = QueueShortcut(wParam.ToInt32(), lParam);
        return IntPtr.Zero;
    }

    private void ShowNotification(string title, string message) => _tray?.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.None);

    public void Dispose() => DisposeCore(saveConfiguration: true);
    internal void DisposeAfterFailure() => DisposeCore(saveConfiguration: false);

    private void DisposeCore(bool saveConfiguration)
    {
        if (_disposed) return;
        _disposed = true;
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception ex) { AppLogger.Error("Desktop shutdown cleanup failed.", ex); }
        }
        Cleanup(_speech.Dispose);
        foreach (var window in Windows) Cleanup(window.PrepareForApplicationShutdown);
        if (saveConfiguration)
            Cleanup(() =>
            {
                Companion.FlushDurationEdits();
                Companion.FlushWindowPlacement();
                Companion.History.Flush();
                foreach (var window in Windows)
                {
                    window.CapturePosition();
                    Configuration.Pets.First(p => p.InstanceId == window.InstanceId).Capture(window.InstanceSettings);
                }
                _save(Configuration);
            });
        Cleanup(Companion.Dispose);
        Cleanup(() => _shortcutDialog?.Close());
        Cleanup(() => _diagnosticsWindow?.Close());
        Cleanup(() => _manager?.Close());
        foreach (var window in Windows.ToArray()) Cleanup(window.Close);
        _windows.Clear();
        Catalog.IsPetInUse = null;
        Cleanup(() => _shortcuts?.Dispose());
        Cleanup(() => _source?.RemoveHook(HotkeyHook));
        Cleanup(() => _hotkeyWindow?.Close());
        Cleanup(() => _tray?.Dispose());
        Cleanup(() => _trayMenu?.Dispose());
        Cleanup(() => _icon?.Dispose());
        Changed = null;
        ExitRequested = null;
    }
}
