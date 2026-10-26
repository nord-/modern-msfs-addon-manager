using AddonManager.Core.State;

namespace AddonManager.Core.Tests.Fakes;

/// <summary>Keeps a deep copy of the last saved state, like a file would.</summary>
internal sealed class InMemoryUserStateStore : IUserStateStore
{
    private UserState saved = new();

    public int SaveCount { get; private set; }

    public UserState Load() => Copy(saved);

    public void Save(UserState state)
    {
        saved = Copy(state);
        SaveCount++;
    }

    // The records inside are immutable, so copying the collections is a deep copy.
    private static UserState Copy(UserState s) => new()
    {
        Libraries = [.. s.Libraries],
        Packages = new(s.Packages),
        Snapshot = s.Snapshot?.ToList(),
        Pending = [.. s.Pending],
    };
}
