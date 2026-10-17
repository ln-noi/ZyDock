using ZyDock.Core.Diagnostics;

namespace ZyDock.App.Interop;

/// <summary>
/// Single instance guard. The second launch signals the running dock to open its
/// settings window instead of starting a duplicate dock.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\ZyDock.SingleInstance.1";
    private const string EventName = @"Local\ZyDock.Activate.1";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private Thread? _listener;
    private volatile bool _stopping;

    public event Action? ActivationRequested;

    /// <summary>Returns false when another instance already owns the dock.</summary>
    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                // Hand the request over to the running instance and leave.
                try
                {
                    if (EventWaitHandle.TryOpenExisting(EventName, out var existing))
                    {
                        using (existing)
                        {
                            existing.Set();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("通知已运行的实例失败。", ex);
                }

                return false;
            }

            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _listener = new Thread(Listen)
            {
                IsBackground = true,
                Name = "ZyDock.ActivationListener",
            };
            _listener.Start();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("单实例初始化失败，将继续以普通方式启动。", ex);
            return true;
        }
    }

    private void Listen()
    {
        while (!_stopping)
        {
            try
            {
                if (_signal is null)
                {
                    return;
                }

                if (_signal.WaitOne(500))
                {
                    ActivationRequested?.Invoke();
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warn("单实例监听线程异常。", ex);
                return;
            }
        }
    }

    public void Dispose()
    {
        _stopping = true;
        try
        {
            _signal?.Set();
        }
        catch
        {
        }

        try
        {
            _listener?.Join(300);
        }
        catch
        {
        }

        _signal?.Dispose();
        _signal = null;

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
