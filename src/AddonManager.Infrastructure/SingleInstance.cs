namespace AddonManager.Infrastructure;

/// <summary>
/// Lets only one instance of the app run per Windows session. The first process holds a named mutex and waits on a
/// named auto-reset event; a second process finds the mutex taken, sets the event and exits. Both kernel objects live
/// in the <c>Local\</c> namespace, so they are scoped to the current session and need no admin rights.
/// <see cref="Dispose"/> stops listening and releases both objects so the name can be acquired again.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string AppName = "ModernMsfsAddonManager";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly RegisteredWaitHandle _registration;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, RegisteredWaitHandle registration)
    {
        _mutex = mutex;
        _activate = activate;
        _registration = registration;
    }

    /// <summary>
    /// Returns the guard if no other instance holds <paramref name="name"/>, otherwise null. The callback runs on a
    /// thread-pool thread each time a second instance calls <see cref="SignalRunningInstance"/>.
    /// </summary>
    public static SingleInstance? TryAcquire(string name, Action onActivationRequested)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(onActivationRequested);

        // The mutex is only used for its existence, not for ownership: WaitOne would succeed re-entrantly on the same thread.
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: false, MutexName(name), out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated instance created it and this token may not open it: another instance runs.
            return null;
        }

        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        // Between the mutex above and this event a second process sees neither: TryAcquire gives null and SignalRunningInstance
        // gives false. The window is microseconds; a caller should treat (null, false) as "retry the signal once", not "nothing runs".
        var activate = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, EventName(name));
        var registration = ThreadPool.RegisterWaitForSingleObject(
            activate,
            (_, _) => onActivationRequested(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        return new SingleInstance(mutex, activate, registration);
    }

    /// <summary>
    /// Wakes the instance that holds <paramref name="name"/>. Returns false if no instance is running, or if it runs elevated and this
    /// token may not signal it.
    /// </summary>
    public static bool SignalRunningInstance(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        EventWaitHandle? activate;
        try
        {
            if (!EventWaitHandle.TryOpenExisting(EventName(name), out activate))
            {
                return false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        using (activate)
        {
            activate.Set();
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _registration.Unregister(waitObject: null);
        _activate.Dispose();
        _mutex.Dispose();
    }

    private static string MutexName(string name) => @"Local\" + name;

    private static string EventName(string name) => @"Local\" + name + ".Activate";
}
