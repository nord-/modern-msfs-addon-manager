namespace AddonManager.Infrastructure;

/// <summary>
/// Signals that a rescan is needed when Community or a Library changes. Every event, and every watcher error
/// (buffer overflow), restarts one quiet-period timer, so a burst gives a single <see cref="RescanNeeded"/>.
/// A watcher that fails (for example an unplugged drive) stays stopped; the owner recreates the watcher
/// after each scan with the Libraries that are reachable then. Events run on a timer thread, so handlers must not
/// throw. <see cref="Dispose"/> waits for an in-flight raise to finish; after it returns no event is raised, so a handler
/// must not block on a thread that may be calling <see cref="Dispose"/> (marshal with <c>InvokeAsync</c>, not <c>Invoke</c>).
/// </summary>
public sealed class ContentWatcher(string communityPath, IReadOnlyList<string> libraryPaths, TimeSpan quietPeriod) : IDisposable
{
    private const int BufferSize = 65536;

    private readonly object _gate = new();
    private readonly object _raiseGate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _timer;
    private bool _communityEventSeen;
    private bool _pending;
    private long _dueAt;
    private bool _disposed;
    private int _eventCount;

    /// <summary>Raised once when the quiet period after the last event expires.</summary>
    public event Action? RescanNeeded;

    /// <summary>Raised right after <see cref="RescanNeeded"/> when a Community event was part of the burst.</summary>
    public event Action? CommunityChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null)
                return;
            _timer = new Timer(OnQuiet, null, Timeout.Infinite, Timeout.Infinite);

            AddWatcher(communityPath, isCommunity: true, recursive: false,
                NotifyFilters.DirectoryName | NotifyFilters.FileName);
            foreach (var library in libraryPaths)
                AddWatcher(library, isCommunity: false, recursive: true,
                    NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size);
        }
    }

    private void AddWatcher(string path, bool isCommunity, bool recursive, NotifyFilters filter)
    {
        if (!Directory.Exists(path))
            return;
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = filter,
                InternalBufferSize = BufferSize,
            };
            watcher.Created += (_, _) => OnChange(isCommunity);
            watcher.Changed += (_, _) => OnChange(isCommunity);
            watcher.Deleted += (_, _) => OnChange(isCommunity);
            watcher.Renamed += (_, _) => OnChange(isCommunity);
            watcher.Error += (_, _) => OnChange(isCommunity);
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            watcher?.Dispose();
        }
    }

    /// <summary>Test hook: number of events (changes and errors) the watcher has registered.</summary>
    internal int EventCount => Volatile.Read(ref _eventCount);

    /// <summary>Test hook: behaves like an <c>Error</c> from the Community or a Library watcher.</summary>
    internal void SimulateError(Exception error, bool fromCommunity) => OnChange(fromCommunity);

    private void OnChange(bool isCommunity)
    {
        lock (_gate)
        {
            if (_disposed || _timer is null)
                return;
            _eventCount++;
            _communityEventSeen |= isCommunity;
            _pending = true;
            _dueAt = Environment.TickCount64 + (long)quietPeriod.TotalMilliseconds;
            _timer.Change(quietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnQuiet(object? state)
    {
        lock (_raiseGate)
        {
            bool communityChanged;
            lock (_gate)
            {
                if (_disposed || !_pending)
                    return;

                // A callback that was already queued when an event re-armed the timer must not raise before the new quiet period is over.
                var remaining = _dueAt - Environment.TickCount64;
                if (remaining > 0)
                {
                    _timer!.Change(remaining, Timeout.Infinite);
                    return;
                }

                _pending = false;
                communityChanged = _communityEventSeen;
                _communityEventSeen = false;
            }

            RescanNeeded?.Invoke();
            if (!communityChanged)
                return;

            // A RescanNeeded handler may have disposed the watcher (the gates are re-entrant on this thread).
            lock (_gate)
            {
                if (_disposed)
                    return;
            }

            CommunityChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        lock (_raiseGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (var watcher in _watchers)
                    watcher.Dispose();
                _watchers.Clear();
                _timer?.Dispose();
            }
        }
    }
}
