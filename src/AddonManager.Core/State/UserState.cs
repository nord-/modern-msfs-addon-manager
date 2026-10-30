using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.State;

/// <summary>Everything the link engine stores for the user (spec 11).</summary>
public sealed class UserState
{
    public List<LibraryRoot> Libraries { get; set; } = [];

    /// <summary>Choices per Package, kept also while a Package can't be found (spec 6.1).</summary>
    public Dictionary<PackageName, PackageState> Packages { get; set; } = [];

    /// <summary>What is stored per GSX profile Package, by file name; a separate kind from <see cref="Packages"/>, so a folder and a profile of one name are two Packages (rule R1).</summary>
    public Dictionary<PackageName, PackageState> Profiles { get; set; } = [];

    /// <summary>The last accepted Community snapshot; <c>null</c> means never reconciled (first run).</summary>
    public List<SnapshotEntry>? Snapshot { get; set; }

    /// <summary>Packages whose link changes wait for the sim to exit (spec 6.8).</summary>
    public HashSet<PackageName> Pending { get; set; } = [];

    /// <summary>Content-scan settings (plan 2).</summary>
    public ContentSettings Content { get; set; } = new();

    /// <summary>Stored cards; a Package is on at most one, and a card has at least one Package (rule R2).</summary>
    public List<CardRecord> Cards { get; set; } = [];

    /// <summary>True once a completed content scan has been settled; plan 5 shows "Grouping cards…" until then.</summary>
    public bool ContentPassCompleted { get; set; }

    /// <summary>The folder the app keeps its own Packages in; <c>null</c> until the user chooses one.</summary>
    public string? PackagesRoot { get; set; }

    /// <summary>The id the next card gets; ids are never reused (rule R1).</summary>
    public long NextCardId { get; set; } = 1;

    /// <summary>Makes a card with the next id and stores it. ICAO codes are kept only on a Scenery card (rule R2); the caller puts Packages on it.</summary>
    public CardRecord NewCard(Category section, CardTab tab, IEnumerable<AirportCode> codes, DateTimeOffset now)
    {
        var card = new CardRecord
        {
            Id = NextCardId++,
            Created = now,
            Section = section,
            Tab = tab,
        };

        if (section == Category.Scenery)
        {
            foreach (var code in codes.Where(c => c != default))
            {
                card.AddCode(code);
            }
        }

        Cards.Add(card);
        return card;
    }

    public CardRecord? FindCard(long id) => Cards.FirstOrDefault(c => c.Id == id);

    /// <summary>The stored states of one kind of Package.</summary>
    private Dictionary<PackageName, PackageState> StatesOf(PackageKind kind) => kind == PackageKind.Folder ? Packages : Profiles;

    /// <summary>The card a Package is on.</summary>
    public CardRecord? CardOf(PackageKey package)
        => StatesOf(package.Kind).GetValueOrDefault(package.Name)?.CardId is { } id ? FindCard(id) : null;

    /// <summary>The Packages on a card, sorted by name, then kind.</summary>
    public IReadOnlyList<PackageKey> PackagesOn(long cardId)
        => [.. Packages
            .Where(p => p.Value.CardId == cardId)
            .Select(p => PackageKey.Folder(p.Key))
            .Concat(Profiles.Where(p => p.Value.CardId == cardId).Select(p => new PackageKey(PackageKind.GsxProfile, p.Key)))
            .OrderBy(k => k.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(k => k.Kind)];

    /// <summary>Puts a Package on a card, or takes it off with <c>null</c>; a Package without a stored state gets an Off one.</summary>
    public void SetCard(PackageKey package, long? cardId)
    {
        var states = StatesOf(package.Kind);
        var current = states.GetValueOrDefault(package.Name) ?? new PackageState(false);
        states[package.Name] = current with { CardId = cardId };
    }

    /// <summary>Drops every card that has no Package (rule R2).</summary>
    public void RemoveEmptyCards()
    {
        var used = Packages.Values.Concat(Profiles.Values).Select(p => p.CardId).OfType<long>().ToHashSet();
        Cards.RemoveAll(c => !used.Contains(c.Id));
    }

    public bool IsOn(PackageName name) => Packages.TryGetValue(name, out var s) && s.On;

    /// <summary>Stores a choice, keeping the other fields; <paramref name="activeCopyPath"/> replaces the stored copy when given.</summary>
    public void SetOn(PackageName name, bool on, string? activeCopyPath = null)
    {
        var current = Packages.GetValueOrDefault(name) ?? new PackageState(false);
        Packages[name] = current with { On = on, ActiveCopyPath = activeCopyPath ?? current.ActiveCopyPath };
    }

    /// <summary>Forgets everything stored for a Package, and the card it leaves empty. The pending mark and the link choices belong to folder Packages only.</summary>
    public void Forget(PackageKey package)
    {
        if (package.Kind == PackageKind.Folder)
        {
            Packages.Remove(package.Name);
            Pending.Remove(package.Name);
            Content.Forget(package.Name);
        }
        else
        {
            Profiles.Remove(package.Name);
        }

        RemoveEmptyCards();
    }
}
