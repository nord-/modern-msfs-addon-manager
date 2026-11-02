using AddonManager.Infrastructure;

namespace AddonManager.App.Tests.Session;

/// <summary>An <see cref="IContentWatcher"/> that records what the session did with it and lets a test raise <see cref="RescanNeeded"/>.</summary>
public sealed class FakeWatcher(IReadOnlyList<string> libraries) : IContentWatcher
{
    public IReadOnlyList<string> Libraries { get; } = libraries;

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public List<DateTimeOffset> Scheduled { get; } = [];

    /// <summary>Raised from <see cref="Dispose"/> before it returns, the way a raise in flight would be.</summary>
    public bool RaiseOnDispose { get; set; }

    /// <summary>What <see cref="HasPending"/> reports: an event whose quiet period had not expired yet.</summary>
    public bool Pending { get; set; }

    public bool HasPending => Pending;

    public event Action? RescanNeeded;

    public event Action? CommunityChanged
    {
        add { }
        remove { }
    }

    public void Start() => Started = true;

    public void ScheduleRescan(DateTimeOffset at) => Scheduled.Add(at);

    public void Raise() => RescanNeeded?.Invoke();

    public void Dispose()
    {
        Disposed = true;
        if (RaiseOnDispose)
            Raise();
    }
}

/// <summary>The factory a session's services use in tests; keeps every watcher it made, in order.</summary>
public sealed class FakeWatchers
{
    private readonly object gate = new();
    private readonly List<FakeWatcher> made = [];

    /// <summary>Set before the factory is first called: the first watcher raises from <c>Dispose</c>.</summary>
    public bool FirstRaisesOnDispose { get; set; }

    /// <summary>Set before the factory is first called: the first watcher still has an unraised event when it is replaced.</summary>
    public bool FirstIsPending { get; set; }

    public IReadOnlyList<FakeWatcher> All
    {
        get
        {
            lock (gate)
                return [.. made];
        }
    }

    public IContentWatcher Create(IReadOnlyList<string> libraries)
    {
        lock (gate)
        {
            var watcher = new FakeWatcher(libraries) { RaiseOnDispose = FirstRaisesOnDispose && made.Count == 0, Pending = FirstIsPending && made.Count == 0 };
            made.Add(watcher);
            return watcher;
        }
    }
}
