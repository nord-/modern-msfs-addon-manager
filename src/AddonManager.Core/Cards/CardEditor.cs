using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Cards;

/// <summary>
/// Applies the user's card edits to <see cref="UserState"/> and saves once per call (spec 6.5 "User changes"). Each method opens a change scope first, so a locked file refuses it before anything changes. Every operation keeps
/// rule R2 (a Package is on at most one card; a card has a Package) and checks before it changes anything.
/// </summary>
public sealed class CardEditor(IUserStateStore store, TimeProvider clock)
{
    /// <summary>
    /// Moves the Packages of <paramref name="dragged"/> onto <paramref name="target"/> and removes the emptied card. The target
    /// keeps its tab, takes the dragged card's ICAO codes (never one it removed itself) and stores the name and image it shows now
    /// as the user's, so they don't change afterwards (a merge is not a rename).
    /// </summary>
    public void Merge(UserState state, Board board, long dragged, long target)
    {
        using var change = store.BeginChange();
        if (dragged == target)
        {
            throw new ArgumentException("A card cannot be merged into itself.", nameof(target));
        }

        var from = Require(state, dragged);
        var into = Require(state, target);

        if (from.Section != into.Section || from.Tab != into.Tab)
        {
            throw new InvalidOperationException("Cards on different tabs are merged by first moving one of them to the other's tab.");
        }

        if (board.Find(target) is { } shown)
        {
            into.Name ??= shown.Name;
            into.ImagePath ??= shown.ImagePath;
        }

        if (into.Section == Category.Scenery)
        {
            foreach (var code in from.Codes.Where(c => !into.RemovedCodes.Contains(c)))
            {
                into.AddCode(code);
            }

            foreach (var code in from.RemovedCodes.Where(c => !into.Codes.Contains(c)).Where(c => !into.RemovedCodes.Contains(c)))
            {
                into.RemovedCodes.Add(code);
            }

            into.RemovedCodes.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
        }

        foreach (var package in state.PackagesOn(dragged))
        {
            state.SetCard(package, target);
        }

        state.RemoveEmptyCards();
        store.Save(state);
    }

    public void MoveToTab(UserState state, long card, CardTab tab)
    {
        using var change = store.BeginChange();
        var record = Require(state, card);
        if (!CardTabs.IsValid(record.Section, tab))
        {
            throw new ArgumentException($"Tab {tab} does not belong to section {record.Section}.", nameof(tab));
        }

        record.Tab = tab;
        store.Save(state);
    }

    /// <summary>
    /// Moves a card to a section and its initial tab (spec 6.5). A card leaving Scenery loses its ICAO codes (rule R2); one entering
    /// it takes the ICAO codes of its Packages only when every Package shares the same non-empty set of them. A move to the card's own
    /// section does nothing. <paramref name="input"/> must hold the analysis for the current cards.
    /// </summary>
    public void MoveToSection(UserState state, BoardInput input, long card, Category section)
    {
        using var change = store.BeginChange();
        var record = Require(state, card);
        if (record.Section == section)
        {
            return;
        }

        var summaries = PackageCatalog.Build(state, input);
        var profiles = ProfileCatalog.Build(state, input);
        var keys = state.PackagesOn(card);

        // A GSX profile belongs to its own ICAO code, so a card of profiles gets it when it moves into Scenery (spec 6.5).
        var packages = ProfileCatalog.Resolve(keys, summaries, profiles);

        if (section != Category.Scenery)
        {
            record.Codes = [];
            record.RemovedCodes = [];
        }
        else
        {
            // Only a set every Package agrees on is the card's own; Packages without a summary agree with nothing.
            var shared = packages.Count == keys.Count && packages.Count > 0 ? packages[0].Codes : [];
            if (shared.Count > 0 && packages.All(p => p.Codes.ToHashSet().SetEquals(shared)))
            {
                foreach (var code in shared)
                {
                    record.AddCode(code);
                }
            }
        }

        record.Section = section;
        record.Tab = CardTabs.Initial(section, packages, record.Codes.Count, input.Analysis.Aircraft);
        store.Save(state);
    }

    /// <summary>Splits one Package off onto a new card of its own in the same section and tab. Returns the new card's id.</summary>
    public long MoveOut(UserState state, long card, PackageKey package)
    {
        using var change = store.BeginChange();
        var source = Require(state, card);
        if (package.Kind == PackageKind.GsxProfile)
        {
            throw new InvalidOperationException("A GSX profile cannot be moved out of its card.");
        }

        if (state.CardOf(package)?.Id != card)
        {
            throw new InvalidOperationException($"{package} is not on this card.");
        }

        if (state.PackagesOn(card).Count == 1)
        {
            throw new InvalidOperationException("The only Package of a card cannot be moved out.");
        }

        var created = state.NewCard(source.Section, source.Tab, [], clock.GetUtcNow());
        created.RemovedCodes = [.. source.RemovedCodes];
        state.SetCard(package, created.Id);
        store.Save(state);
        return created.Id;
    }

    public void Rename(UserState state, long card, string? name)
    {
        using var change = store.BeginChange();
        Require(state, card).Name = string.IsNullOrWhiteSpace(name) ? null : name;
        store.Save(state);
    }

    public void SetImage(UserState state, long card, string? path)
    {
        using var change = store.BeginChange();
        Require(state, card).ImagePath = string.IsNullOrWhiteSpace(path) ? null : path;
        store.Save(state);
    }

    /// <summary>
    /// Gives a Not shown Package a card of its own in <paramref name="section"/> (spec 6.4 "Show in"), like a new Package gets one: a
    /// Scenery card takes the Package's ICAO codes, and the card starts on its initial tab. Only a Package whose menu section is fixed
    /// can be shown (<see cref="NotShownPackage.CanShowIn"/>). <paramref name="input"/> must hold the analysis for the current cards.
    /// Returns the new card's id.
    /// </summary>
    public long ShowIn(UserState state, BoardInput input, PackageName package, Category section)
    {
        using var change = store.BeginChange();
        var key = PackageKey.Folder(package);
        if (!PackageCatalog.Build(state, input).TryGetValue(package, out var summary)
            || !summary.IsNotShown
            || summary.FixedSection is null
            || state.CardOf(key) is not null)
        {
            throw new InvalidOperationException($"{key} is not a Not shown Package that can be shown.");
        }

        var codes = section == Category.Scenery ? summary.Codes : [];
        var tab = CardTabs.Initial(section, [summary], codes.Count, input.Analysis.Aircraft);
        var card = state.NewCard(section, tab, codes, clock.GetUtcNow());
        state.SetCard(key, card.Id);
        store.Save(state);
        return card.Id;
    }

    /// <summary>
    /// Adds an ICAO code to a Scenery card (spec 6.5 "ICAO code field"). Any 3 to 5 letters or digits are accepted, stored upper-case.
    /// An ICAO code another Scenery card has is refused with the oldest such card's id and name. A card that already had an ICAO code keeps the
    /// name it shows now; adding one moves the card to Airports.
    /// </summary>
    public CodeOutcome AddCode(UserState state, Board board, long card, string text)
    {
        using var change = store.BeginChange();
        var record = RequireScenery(state, card);
        if (!AirportCode.TryParse(text, out var code))
        {
            return new CodeOutcome(CodeResult.Invalid);
        }

        if (record.Codes.Contains(code))
        {
            return new CodeOutcome(CodeResult.Added);
        }

        var holder = state.Cards.Where(c => c.Id != card && c.Section == Category.Scenery && c.Codes.Contains(code)).MinBy(c => c.Id);
        if (holder is not null)
        {
            return new CodeOutcome(CodeResult.Taken, holder.Id, board.Find(holder.Id)?.Name);
        }

        if (record.Codes.Count > 0)
        {
            PinName(record, board);
        }

        record.AddCode(code);
        record.Tab = CardTab.Airports;
        store.Save(state);
        return new CodeOutcome(CodeResult.Added);
    }

    /// <summary>
    /// Removes an ICAO code from a Scenery card and remembers the removal, so no Package of the card is linked to it again. The card
    /// keeps its tab and the name it shows now.
    /// </summary>
    public void RemoveCode(UserState state, Board board, long card, AirportCode code)
    {
        using var change = store.BeginChange();
        var record = RequireScenery(state, card);
        if (!record.Codes.Contains(code))
        {
            throw new InvalidOperationException($"The card has no ICAO code {code}.");
        }

        PinName(record, board);
        record.RemoveCode(code);
        store.Save(state);
    }

    private static void PinName(CardRecord record, Board board) => record.Name ??= board.Find(record.Id)?.Name;

    private static CardRecord RequireScenery(UserState state, long id)
    {
        var record = Require(state, id);
        return record.Section == Category.Scenery
            ? record
            : throw new InvalidOperationException("Only Scenery cards have ICAO codes.");
    }

    private static CardRecord Require(UserState state, long id)
        => state.FindCard(id) ?? throw new ArgumentException($"Unknown card: {id}", nameof(id));
}
