using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
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

    public bool IsSimRunning => false;

    public Task<string?> SwitchCardAsync(CardRef card) => Task.FromResult<string?>(null);

    public Task<string?> RemoveAsync(CardRef card, PackageKey package) => Task.FromResult<string?>(null);

    public IReadOnlyDictionary<AirportCode, Airport> Airports { get; } = new Dictionary<AirportCode, Airport>();

    public Task<string?> RenameAsync(CardRef card, string? name) => Task.FromResult<string?>(null);

    public Task<string?> SetImageAsync(CardRef card, string? path) => Task.FromResult<string?>(null);

    public Task<(CodeOutcome? Outcome, string? Message)> AddCodeAsync(CardRef card, string text) => Task.FromResult<(CodeOutcome?, string?)>((null, null));

    public Task<string?> RemoveCodeAsync(CardRef card, AirportCode code) => Task.FromResult<string?>(null);

    public Task<string?> SplitAsync(CardRef card, PackageKey package) => Task.FromResult<string?>(null);

    public Task<string?> MergeAsync(CardRef dragged, CardRef target) => Task.FromResult<string?>(null);

    public Task<string?> MoveToTabAsync(CardRef card, CardTab tab) => Task.FromResult<string?>(null);

    public Task<string?> MoveToSectionAsync(CardRef card, Category section) => Task.FromResult<string?>(null);

    public void Publish(SessionView view)
    {
        Current = view;
        Changed?.Invoke(view);
    }
}
