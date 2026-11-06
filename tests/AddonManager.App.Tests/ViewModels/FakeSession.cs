using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Sync;
using AddonManager.Sim2020;

namespace AddonManager.App.Tests.ViewModels;

/// <summary>An <see cref="IAppSession"/> a test drives by hand: it sets <c>Current</c> and raises <c>Changed</c> itself, in any order.</summary>
internal sealed class FakeSession : IAppSession
{
    private SessionView current = new(SessionPhase.Starting, null, null, null);

    public SessionView Current
    {
        get
        {
            var view = current;
            Reads++;
            OnRead?.Invoke();
            return view;
        }
        set => current = value;
    }

    /// <summary>How many times <c>Current</c> was read.</summary>
    public int Reads { get; set; }

    /// <summary>Runs after <c>Current</c> was read, before the getter returns.</summary>
    public Action? OnRead { get; set; }

    public event Action<SessionView>? Changed;

    public UserState State { get; } = new();

    public SimPaths? Paths { get; set; }

    public string DatabaseFile { get; set; } = @"C:\Data\userdata.db";

    public List<bool> Rescans { get; } = [];

    public IReadOnlyList<CommunityEntryInfo> ReadCommunity() => [];

    public bool TryUsePackagesRoot(string root, out string? refusal)
    {
        refusal = null;
        return true;
    }

    public Task CompleteFirstRunAsync(IReadOnlyList<LibraryRoot> libraries) => Task.CompletedTask;

    public Task RescanAsync(bool full)
    {
        Rescans.Add(full);
        return Task.CompletedTask;
    }

    public bool IsSimRunning { get; set; }

    /// <summary>The choices of each <c>ResolveSyncAsync</c> call.</summary>
    public List<IReadOnlyDictionary<SyncKey, SyncChoice>> Resolved { get; } = [];

    /// <summary>What <c>ResolveSyncAsync</c> returns.</summary>
    public string? ResolveMessage { get; set; }

    public Task<string?> ResolveSyncAsync(IReadOnlyDictionary<SyncKey, SyncChoice> choices)
    {
        Resolved.Add(choices);
        return Task.FromResult(ResolveMessage);
    }

    public void Raise(SessionView view) => Changed?.Invoke(view);
}

/// <summary>Builders for cards and boards.</summary>
internal static class Boards
{
    public static AirportCode Code(string value) => AirportCode.TryParse(value, out var code) ? code : throw new ArgumentException(value);

    public static CardPackage Package(string name) => new(
        PackageKind.Folder, new PackageName(name), name, @"C:\Lib\" + name, null, null, PackageSource.Library, Presence.Found, LockReason.None,
        null, AircraftRole.None, [], [], [], true, true, null, [], false);

    public static Card Card(
        long? id,
        string name,
        Category section = Category.Scenery,
        CardTab tab = CardTab.Airports,
        string[]? codes = null,
        string? image = null,
        CardState state = CardState.On,
        bool pending = false,
        string[]? packages = null,
        string[]? designators = null,
        IReadOnlyList<BuiltInPart>? builtIns = null,
        IReadOnlyList<CardWarning>? warnings = null)
        => new(
            id,
            id is null ? new PackageKey(PackageKind.Folder, new PackageName(name)) : null,
            section,
            tab,
            name,
            image,
            [.. (codes ?? []).Select(Code)],
            [],
            null,
            [.. (packages ?? [name]).Select(Package)],
            [],
            designators ?? [],
            state,
            pending,
            builtIns ?? [],
            warnings ?? []);

    public static Banner EmptyBanner { get; } = new([], [], 0, 0, [], []);

    public static Board Board(params Card[] cards) => new(cards, [], EmptyBanner);

    public static Board Board(Banner banner, params Card[] cards) => new(cards, [], banner);

    public static SessionView Ready(Board board, ScanProgress? progress = null, string? error = null) => new(SessionPhase.Ready, board, progress, error);

    public static ClassifiedEntry Entry(string name, EntryClass entryClass)
        => new(new CommunityEntryInfo(name, EntryKind.Directory, null, false, false), entryClass, null);
}
