using AddonManager.Core.Cards;
using AddonManager.Core.State;

namespace AddonManager.Core.Tests.Fakes;

/// <summary>Keeps a deep copy of the last saved state, like a file would. A change scope commits at its outermost dispose, like the SQLite store.</summary>
internal sealed class InMemoryUserStateStore : IUserStateStore
{
    private UserState saved = new();
    private UserState? pending;
    private int depth;

    public int SaveCount { get; private set; }

    /// <summary>How many times a state reached the "file": once per outermost scope that saved, or per Save outside any scope.</summary>
    public int CommitCount { get; private set; }

    /// <summary>When set, <see cref="BeginChange"/> throws it, like a locked file.</summary>
    public Exception? RefuseWith { get; set; }

    public UserState Load() => Copy(saved);

    public void Save(UserState state)
    {
        SaveCount++;
        if (depth == 0)
        {
            saved = Copy(state);
            CommitCount++;
            return;
        }

        pending = Copy(state);
    }

    public IDisposable BeginChange()
    {
        if (RefuseWith is { } refusal && depth == 0)
            throw refusal;

        depth++;
        return new Scope(this);
    }

    private void End()
    {
        if (--depth > 0)
            return;

        if (pending is { } state)
        {
            saved = state;
            CommitCount++;
        }

        pending = null;
    }

    private sealed class Scope(InMemoryUserStateStore owner) : IDisposable
    {
        private bool done;

        public void Dispose()
        {
            if (done)
                return;
            done = true;
            owner.End();
        }
    }

    // The records inside are immutable except the cards, which are cloned, and the lists of a memo, which are copied.
    private static UserState Copy(UserState s) => new()
    {
        Libraries = [.. s.Libraries],
        Packages = s.Packages.ToDictionary(kv => kv.Key, kv => kv.Value with { Memo = CopyMemo(kv.Value.Memo) }),
        Profiles = s.Profiles.ToDictionary(kv => kv.Key, kv => kv.Value with { Memo = CopyMemo(kv.Value.Memo) }),
        Snapshot = s.Snapshot?.ToList(),
        Pending = [.. s.Pending],
        Content = s.Content.Clone(),
        Cards = [.. s.Cards.Select(c => c.Clone())],
        NextCardId = s.NextCardId,
        ContentPassCompleted = s.ContentPassCompleted,
        PackagesRoot = s.PackagesRoot,
    };

    private static PackageMemo? CopyMemo(PackageMemo? memo) => memo is { } m
        ? m with { Codes = [.. m.Codes], Containers = [.. m.Containers], Airports = [.. m.Airports], Tags = [.. m.Tags], TypeDesignators = [.. m.TypeDesignators] }
        : null;
}
