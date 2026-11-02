using AddonManager.App.Session;
using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Sim2020;

namespace AddonManager.App.Tests.ViewModels;

internal sealed class FirstRunFakeSession : IAppSession
{
    public SessionView Current { get; set; } = new(SessionPhase.FirstRun, null, null, null);

    public event Action<SessionView>? Changed;

    public UserState State { get; } = new();

    public SimPaths? Paths { get; set; } = new(null, @"C:\Sim\Packages", @"C:\Sim\Packages\Community", @"C:\Sim\Packages\Official");

    public string DatabaseFile => "";

    public List<CommunityEntryInfo> Community { get; } = [];

    public int ReadCommunityCalls { get; private set; }

    public string? RootRefusal { get; set; }

    public List<string> RootsTried { get; } = [];

    public IReadOnlyList<LibraryRoot>? Completed { get; private set; }

    public IReadOnlyList<CommunityEntryInfo> ReadCommunity()
    {
        ReadCommunityCalls++;
        return Community;
    }

    public bool TryUsePackagesRoot(string root, out string? refusal)
    {
        RootsTried.Add(root);
        refusal = RootRefusal;
        return refusal is null;
    }

    public Task CompleteFirstRunAsync(IReadOnlyList<LibraryRoot> libraries)
    {
        Completed = libraries;
        return Task.CompletedTask;
    }

    public Task RescanAsync(bool full) => Task.CompletedTask;

    public void Publish(SessionView view)
    {
        Current = view;
        Changed?.Invoke(view);
    }
}
