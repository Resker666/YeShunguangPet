using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

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
    private readonly FatalErrorCoordinator _fatalError = new();
    private volatile bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLogger.Initialize();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        System.Windows.Forms.Application.ThreadException += Forms_ThreadException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        AppLogger.Info($"Starting YeShunguangPet {GetApplicationVersion()}.");

        try
        {
            AppLogger.Info("Initializing single-instance controls.");
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

            AppLogger.Info("Loading desktop configuration.");
            var store = new DesktopSettingsStore();
            var configuration = store.Load();
            configuration.Companion.LaunchAtStartup = PetSettings.IsLaunchAtStartupEnabled();
            _desktop = new DesktopSession(configuration, new PetCatalog(), store.Save);
            _desktop.ExitRequested += () => Shutdown();
            _desktop.Start();
            AppLogger.Info($"Desktop session started: {_desktop.Windows.Count} role(s).");
        }
        catch (Exception ex)
        {
            HandleFatalError(ex, "无法启动桌面宠物");
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel) _desktop?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        try { _desktop?.Dispose(); }
        catch (Exception ex) { AppLogger.Error("Failed to close desktop session.", ex); }
        AppLogger.Info($"Application exiting with code {e.ApplicationExitCode}.");
        void Cleanup(Action action)
        {
            try { action(); } catch (Exception ex) { AppLogger.Error("Application shutdown cleanup failed.", ex); }
        }
        Cleanup(() => _stopActivationListener?.Set());
        var listenerStopped = _activationThread?.IsAlive != true || _activationThread.Join(millisecondsTimeout: 500);
        if (listenerStopped)
        {
            Cleanup(() => _stopActivationListener?.Dispose());
            Cleanup(() => _activationEvent?.Dispose());
        }

        if (_ownsSingleInstanceMutex)
        {
            Cleanup(() => _singleInstanceMutex?.ReleaseMutex());
        }

        Cleanup(() => _singleInstanceMutex?.Dispose());

        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        System.Windows.Forms.Application.ThreadException -= Forms_ThreadException;
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
        while (!_exiting && WaitHandle.WaitAny(handles) == 0)
        {
            if (_exiting || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(ShowExistingWindow));
        }
    }

    private void ShowExistingWindow()
    {
        if (_exiting || _fatalError.IsHandling) return;
        _desktop?.ShowAll();
        if (_desktop?.Windows.Count == 0) _desktop.OpenManager();
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        HandleFatalError(e.Exception, "叶瞬光运行错误");
    }

    private void Forms_ThreadException(object sender, ThreadExceptionEventArgs e)
    {
        if (_exiting || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) HandleFatalError(e.Exception, "叶瞬光运行错误");
        else Dispatcher.BeginInvoke(() => HandleFatalError(e.Exception, "叶瞬光运行错误"));
    }

    private void HandleFatalError(Exception exception, string title)
    {
        DiagnosticReport? report = null;
        _fatalError.Run(exception,
            stop: () =>
            {
                _exiting = true;
                try { _stopActivationListener?.Set(); } catch (Exception ex) { AppLogger.Error("Could not stop activation listener.", ex); }
                try { report = DiagnosticReport.Capture(_desktop, AppLogger.Capture(), exception, _fatalError.ErrorId); }
                catch (Exception ex) { AppLogger.Error("Could not capture pre-shutdown diagnostic state.", ex); }
                _desktop?.DisposeAfterFailure();
            },
            present: () =>
            {
                var logs = AppLogger.Capture();
                report ??= DiagnosticReport.Capture(null, logs, exception, _fatalError.ErrorId);
                var location = logs.ActivePath is null ? "日志文件未能写入，最近记录仍在内存中。" : "日志位置：" + logs.ActivePath;
                var reason = exception.Message.Length > 300 ? exception.Message[..300] + "…" : exception.Message;
                var answer = MessageBox.Show($"程序遇到错误，需要退出。\n原因：{reason}\n错误编号：{_fatalError.ErrorId}\n\n{location}\n\n是否保存脱敏诊断包？", title, MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (answer != MessageBoxResult.Yes) return;
                var picker = new SaveFileDialog { Title = "保存脱敏诊断包", Filter = "诊断包 (*.zip)|*.zip", DefaultExt = ".zip", AddExtension = true,
                    FileName = $"YeShunguangPet-error-{_fatalError.ErrorId}.zip", OverwritePrompt = true };
                if (picker.ShowDialog() != true) return;
                try { report.Export(picker.FileName, overwrite: true); }
                catch (Exception ex)
                {
                    AppLogger.Error("Could not export fatal error diagnostics.", ex);
                    MessageBox.Show("诊断包未保存：" + ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            },
            shutdown: () => Shutdown(-1),
            log: error => AppLogger.Error("Fatal error " + _fatalError.ErrorId + ".", error));
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
