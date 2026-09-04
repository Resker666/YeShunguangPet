using System;
using System.Threading;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out _ownsSingleInstanceMutex);

        if (!_ownsSingleInstanceMutex)
        {
            _activationEvent.Set();
            Shutdown();
            return;
        }

        StartActivationListener();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stopActivationListener?.Set();
        _activationThread?.Join(millisecondsTimeout: 500);

        _stopActivationListener?.Dispose();
        _activationEvent?.Dispose();

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
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
        if (MainWindow is YeShunguangPet.MainWindow window)
        {
            window.ShowAndActivate();
        }
    }
}
