using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Sim2020;

namespace AddonManager.App.Session;

/// <summary>The window's view of the app session. <see cref="Changed"/> is raised on whatever thread finished the work, never under the session lock.</summary>
public interface IAppSession
{
    SessionView Current { get; }

    event Action<SessionView>? Changed;

    UserState State { get; }

    SimPaths? Paths { get; }

    string DatabaseFile { get; }

    /// <summary>Community as it is now, read once; empty before the install is known or when it cannot be read.</summary>
    IReadOnlyList<CommunityEntryInfo> ReadCommunity();

    bool TryUsePackagesRoot(string root, out string? refusal);

    Task CompleteFirstRunAsync(IReadOnlyList<LibraryRoot> libraries);

    Task RescanAsync(bool full);

    /// <summary>Whether MSFS runs right now; false before the install is known.</summary>
    bool IsSimRunning { get; }

    /// <summary>
    /// The card's switch: sets every switchable Package of the card to the opposite of the switch position, and the wall shows it at once.
    /// Returns the message to show, or <c>null</c> on success (also when the click changes nothing); nothing is thrown.
    /// </summary>
    Task<string?> SwitchCardAsync(CardRef card);

    /// <summary>Removes a Not found Package (and the card it leaves empty) from the app's data. Returns the message to show, or <c>null</c> on success.</summary>
    Task<string?> RemoveAsync(CardRef card, PackageKey package);

    /// <summary>The Airports of the latest board's analysis, for ICAO code suggestions; empty before the first board. Never waits on a run.</summary>
    IReadOnlyDictionary<AirportCode, Airport> Airports { get; }

    // The card edits below run off the UI thread, throw nothing and return the message to show, or null on success. A card that is gone
    // from the board, or is only provisional, gives AppSession.CardGoneText; a locked user-data file gives its usual message.

    /// <summary>Sets the user's name of the card; an empty name brings the computed name back.</summary>
    Task<string?> RenameAsync(CardRef card, string? name);

    /// <summary>Sets the user's image of the card (only the path is stored); <c>null</c> brings the automatic image back.</summary>
    Task<string?> SetImageAsync(CardRef card, string? path);

    /// <summary>Adds an ICAO code to a Scenery card. The outcome says whether it was added, invalid or taken (and by which card); it is <c>null</c> when the edit did not run.</summary>
    Task<(CodeOutcome? Outcome, string? Message)> AddCodeAsync(CardRef card, string text);

    /// <summary>Removes an ICAO code from a Scenery card and remembers the removal.</summary>
    Task<string?> RemoveCodeAsync(CardRef card, AirportCode code);

    /// <summary>Splits one Package off onto a new card of its own; also while MSFS runs, since no link changes.</summary>
    Task<string?> SplitAsync(CardRef card, PackageKey package);

    /// <summary>Moves the Packages of <paramref name="dragged"/> onto <paramref name="target"/> and removes the emptied card.</summary>
    Task<string?> MergeAsync(CardRef dragged, CardRef target);

    Task<string?> MoveToTabAsync(CardRef card, CardTab tab);

    /// <summary>Moves the card to a menu section and that section's initial tab.</summary>
    Task<string?> MoveToSectionAsync(CardRef card, Category section);
}
