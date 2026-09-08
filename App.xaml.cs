using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace YeShunguangPet;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Resker666.YeShunguangPet.SingleInstance";
    private const string ActivationEventName = @"Local\Resker666.YeShunguangPet.Activate";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private ManualResetEvent? _stopActivationListener;
    private Thread? _activationThread;
    private bool _ownsSingleInstanceMutex;
    private DesktopSession? _desktop;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLogger.Initialize();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        AppLogger.Info($"Starting YeShunguangPet {GetApplicationVersion()}.");

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out _ownsSingleInstanceMutex);

        if (!_ownsSingleInstanceMutex)
        {
            AppLogger.Info("A second instance requested activation of the existing window.");
            _activationEvent.Set();
            Shutdown();
            return;
        }

        StartActivationListener();

        try
        {
            var store = new DesktopSettingsStore();
            var configuration = store.Load();
            configuration.Companion.LaunchAtStartup = PetSettings.IsLaunchAtStartupEnabled();
            _desktop = new DesktopSession(configuration, new PetCatalog(), store.Save);
            _desktop.ExitRequested += () => Shutdown();
            _desktop.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to start desktop session.", ex);
            MessageBox.Show(ex.Message, "无法启动桌面宠物", MessageBoxButton.OK, MessageBoxImage.Error);
            _desktop?.Dispose();
            Shutdown(-1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel) _desktop?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _desktop?.Dispose(); }
        catch (Exception ex) { AppLogger.Error("Failed to close desktop session.", ex); }
        AppLogger.Info($"Application exiting with code {e.ApplicationExitCode}.");
        _stopActivationListener?.Set();
        _activationThread?.Join(millisecondsTimeout: 500);

        _stopActivationListener?.Dispose();
        _activationEvent?.Dispose();

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();

        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        base.OnExit(e);
    }

    private void StartActivationListener()
    {
        _stopActivationListener = new ManualResetEvent(false);
        _activationThread = new Thread(ListenForActivation)
        {
            IsBackground = true,
            Name = "YeShunguangPet activation listener"
        };
        _activationThread.Start();
    }

    private void ListenForActivation()
    {
        if (_activationEvent is null || _stopActivationListener is null)
        {
            return;
        }

        var handles = new WaitHandle[] { _activationEvent, _stopActivationListener };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(ShowExistingWindow));
        }
    }

    private void ShowExistingWindow()
    {
        _desktop?.ShowAll();
        if (_desktop?.Windows.Count == 0) _desktop.OpenManager();
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "程序遇到错误并需要退出。详细信息已写入日志目录。",
            "叶瞬光运行错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        _desktop?.Dispose();
        Shutdown(-1);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled application exception.", e.ExceptionObject as Exception);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    private static string GetApplicationVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
    }
}
