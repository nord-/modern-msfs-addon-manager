namespace AddonManager.Infrastructure;

/// <summary>The watcher seam: signals that a rescan is needed or that Community changed. <see cref="ContentWatcher"/> is the real one.</summary>
public interface IContentWatcher : IDisposable
{
    /// <summary>Raised once when the quiet period after the last event expires.</summary>
    event Action? RescanNeeded;

    /// <summary>Raised right after <see cref="RescanNeeded"/> when a Community event was part of the burst.</summary>
    event Action? CommunityChanged;

    void Start();

    /// <summary>True while an event (or a scheduled rescan) has not been raised yet; final once the watcher is disposed.</summary>
    bool HasPending { get; }

    /// <summary>Asks for one <see cref="RescanNeeded"/> at <paramref name="at"/>, or right away when it has passed.</summary>
    void ScheduleRescan(DateTimeOffset at);
}
