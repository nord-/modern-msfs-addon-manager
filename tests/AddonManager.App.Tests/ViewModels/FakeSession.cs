using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
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

    /// <summary>The card of each <c>SwitchCardAsync</c> call.</summary>
    public List<CardRef> Switched { get; } = [];

    /// <summary>What <c>SwitchCardAsync</c> returns.</summary>
    public string? SwitchMessage { get; set; }

    /// <summary>Runs inside <c>SwitchCardAsync</c> before it returns; a test throws or changes <c>IsSimRunning</c> here.</summary>
    public Action<CardRef>? OnSwitch { get; set; }

    public Task<string?> SwitchCardAsync(CardRef card)
    {
        Switched.Add(card);
        OnSwitch?.Invoke(card);
        return Task.FromResult(SwitchMessage);
    }

    /// <summary>The card and Package of each <c>RemoveAsync</c> call.</summary>
    public List<(CardRef Card, PackageKey Package)> Removed { get; } = [];

    /// <summary>What <c>RemoveAsync</c> returns.</summary>
    public string? RemoveMessage { get; set; }

    public Task<string?> RemoveAsync(CardRef card, PackageKey package)
    {
        Removed.Add((card, package));
        return Task.FromResult(RemoveMessage);
    }

    public IReadOnlyDictionary<AirportCode, Airport> Airports { get; set; } = new Dictionary<AirportCode, Airport>();

    /// <summary>The card and name of each <c>RenameAsync</c> call.</summary>
    public List<(CardRef Card, string? Name)> Renamed { get; } = [];

    /// <summary>What <c>RenameAsync</c> returns.</summary>
    public string? RenameMessage { get; set; }

    public Task<string?> RenameAsync(CardRef card, string? name)
    {
        Renamed.Add((card, name));
        return Task.FromResult(RenameMessage);
    }

    /// <summary>The card and path of each <c>SetImageAsync</c> call.</summary>
    public List<(CardRef Card, string? Path)> ImagesSet { get; } = [];

    /// <summary>What <c>SetImageAsync</c> returns.</summary>
    public string? SetImageMessage { get; set; }

    public Task<string?> SetImageAsync(CardRef card, string? path)
    {
        ImagesSet.Add((card, path));
        return Task.FromResult(SetImageMessage);
    }

    /// <summary>The card and text of each <c>AddCodeAsync</c> call.</summary>
    public List<(CardRef Card, string Text)> CodesAdded { get; } = [];

    /// <summary>What <c>AddCodeAsync</c> returns.</summary>
    public (CodeOutcome? Outcome, string? Message) AddCodeResult { get; set; } = (new CodeOutcome(CodeResult.Added), null);

    public Task<(CodeOutcome? Outcome, string? Message)> AddCodeAsync(CardRef card, string text)
    {
        CodesAdded.Add((card, text));
        return Task.FromResult(AddCodeResult);
    }

    /// <summary>The card and ICAO code of each <c>RemoveCodeAsync</c> call.</summary>
    public List<(CardRef Card, AirportCode Code)> CodesRemoved { get; } = [];

    /// <summary>What <c>RemoveCodeAsync</c> returns.</summary>
    public string? RemoveCodeMessage { get; set; }

    public Task<string?> RemoveCodeAsync(CardRef card, AirportCode code)
    {
        CodesRemoved.Add((card, code));
        return Task.FromResult(RemoveCodeMessage);
    }

    /// <summary>The card and Package of each <c>SplitAsync</c> call.</summary>
    public List<(CardRef Card, PackageKey Package)> Splits { get; } = [];

    /// <summary>What <c>SplitAsync</c> returns.</summary>
    public string? SplitMessage { get; set; }

    public Task<string?> SplitAsync(CardRef card, PackageKey package)
    {
        Splits.Add((card, package));
        return Task.FromResult(SplitMessage);
    }

    /// <summary>The dragged and target card of each <c>MergeAsync</c> call.</summary>
    public List<(CardRef Dragged, CardRef Target)> Merges { get; } = [];

    /// <summary>What <c>MergeAsync</c> returns.</summary>
    public string? MergeMessage { get; set; }

    public Task<string?> MergeAsync(CardRef dragged, CardRef target)
    {
        Merges.Add((dragged, target));
        return Task.FromResult(MergeMessage);
    }

    /// <summary>The card and tab of each <c>MoveToTabAsync</c> call.</summary>
    public List<(CardRef Card, CardTab Tab)> TabMoves { get; } = [];

    /// <summary>What <c>MoveToTabAsync</c> returns.</summary>
    public string? MoveToTabMessage { get; set; }

    public Task<string?> MoveToTabAsync(CardRef card, CardTab tab)
    {
        TabMoves.Add((card, tab));
        return Task.FromResult(MoveToTabMessage);
    }

    /// <summary>The card and section of each <c>MoveToSectionAsync</c> call.</summary>
    public List<(CardRef Card, Category Section)> SectionMoves { get; } = [];

    /// <summary>What <c>MoveToSectionAsync</c> returns.</summary>
    public string? MoveToSectionMessage { get; set; }

    public Task<string?> MoveToSectionAsync(CardRef card, Category section)
    {
        SectionMoves.Add((card, section));
        return Task.FromResult(MoveToSectionMessage);
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
