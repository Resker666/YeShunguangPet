using System;
using System.Threading;

namespace YeShunguangPet;

public sealed class FatalErrorCoordinator
{
    private int _started;
    public bool IsHandling => Volatile.Read(ref _started) != 0;
    public string ErrorId { get; } = Guid.NewGuid().ToString("N")[..12];

    public bool Run(Exception exception, Action stop, Action present, Action shutdown, Action<Exception> log)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return false;
        void Record(Exception error) { try { log(error); } catch { } }
        Record(exception);
        try
        {
            try { stop(); } catch (Exception ex) { Record(ex); }
            try { present(); } catch (Exception ex) { Record(ex); }
        }
        finally { try { shutdown(); } catch (Exception ex) { Record(ex); } }
        return true;
    }
}
