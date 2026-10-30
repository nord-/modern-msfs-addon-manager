using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.FirstRun;
using AddonManager.Core.Sync;

namespace AddonManager.Cli;

/// <summary>Prints the card wall for this machine after a completed content scan. Writes only the scan cache: no user data is saved.</summary>
internal static class CardsCommand
{
    public const string Usage = "Usage: AddonManager.Cli cards [--full] [--packages] [--library <path>[=sections]]"
        + " (prints the card wall; writes only the cache tables of userdata.db."
        + " The CLI never stores sections, so Packages without a stored section are re-read on every run.)";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!LibraryOptions.TryParse(args, out var libraries, out var rest, out var libraryError))
        {
            error.WriteLine(libraryError);
            return 2;
        }

        if (rest.Contains("--help"))
        {
            output.WriteLine(Usage);
            return 0;
        }

        var full = false;
        var packages = false;
        foreach (var arg in rest)
        {
            switch (arg)
            {
                case "--full": full = true; break;
                case "--packages": packages = true; break;
                default:
                    error.WriteLine(Usage);
                    return 2;
            }
        }

        var run = ScanCommand.Execute(full, libraries, output, out var exitCode, listLibraries: false);
        if (run is null)
            return exitCode;

        var (outcome, state) = run;
        var discovery = outcome.Discovery;

        output.WriteLine("Libraries:");
        foreach (var library in state.Libraries)
            output.WriteLine($"  {library.Path} [{string.Join(", ", library.Sections)}]");
        var coverage = LinkCoverage.Measure([.. discovery.Community.Select(e => e.Entry)], state.Libraries);
        output.WriteLine($"Links covered: {coverage.Covered} of {coverage.Counted}");

        // The scan has completed (rule A5). Everything below changes the in-memory state only; it is never saved.
        if (state.Snapshot is null)
            Reconciler.Adopt(state, discovery.Community, discovery.Index);
        var input = new BoardInput(discovery.Index, discovery.Community, outcome.Content.Contents, outcome.Analysis, ScanComplete: true)
        {
            Unsettled = outcome.Content.Unsettled,
            Failed = outcome.Content.FailedPaths,
            PresentOutsideLibraries = discovery.PresentOutsideLibraries,
        }.Reanalyze(state);
        Settlement.Apply(state, input, DateTimeOffset.UtcNow);

        // Placement changed the cards, so the board is built from an analysis of the current ones.
        var board = BoardBuilder.Build(state, input.Reanalyze(state));

        foreach (var section in Enum.GetValues<Category>())
        {
            foreach (var tab in CardTabs.Of(section))
            {
                var cards = board.On(section, tab).ToList();
                output.WriteLine($"{section} / {tab} ({cards.Count})");
                WriteCards(cards, packages, output);
            }
        }

        output.WriteLine($"Not shown ({board.NotShown.Count})");
        foreach (var p in board.NotShown)
            output.WriteLine($"  {p.Name}  {(p.Library is null ? "(no Library)" : $"{p.Library.Path} [{string.Join(", ", p.Library.Sections)}]")} -> {p.Section}");

        var banner = board.Banner;
        output.WriteLine($"Community: {banner.Broken.Count} Broken, {banner.Stray.Count} Stray; pending {banner.Pending}");
        foreach (var library in banner.LibrariesNotFound)
            output.WriteLine($"Library not found: {library.Path}");
        if (banner.Unreadable.Count > 0)
            output.WriteLine($"Unreadable ({banner.Unreadable.Count}): {string.Join(", ", banner.Unreadable)}");
        if (banner.Provisional > 0)
            output.WriteLine($"Grouping cards... {banner.Provisional} provisional");
        if (outcome.Content is { Unsettled.Count: > 0, SettleAt: { } settleAt })
            output.WriteLine($"Still being written: {outcome.Content.Unsettled.Count} (settles at {settleAt.ToLocalTime():HH:mm:ss})");
        return 0;
    }

    private static void WriteCards(IReadOnlyList<Card> cards, bool packages, TextWriter o)
    {
        // States and codes are padded to the longest in the list so the columns line up.
        var stateWidth = cards.Select(c => c.State.ToString().Length).DefaultIfEmpty(0).Max();
        var codeWidth = cards.Where(c => !c.Several).Select(c => c.Codes.FirstOrDefault().Value.Length).DefaultIfEmpty(0).Max();
        foreach (var card in cards)
        {
            var state = $"[{card.State.ToString().PadRight(stateWidth)}]";
            var count = $"({card.Packages.Count} package{(card.Packages.Count == 1 ? "" : "s")})";
            var head = card.Several
                ? $"(several) {card.Name} {count}  {string.Join(", ", card.Codes)}"
                : $"{card.Codes.FirstOrDefault().Value.PadRight(codeWidth)} {card.Name} {count}";
            var line = $"  {state} {head}".TrimEnd();
            if (card.Tags.Count > 0)
                line += $"  tags: {string.Join(", ", card.Tags)}";
            if (card.Pending)
                line += "  Next sim start";
            foreach (var warning in card.Warnings)
                line += $"  ! {WarningText(warning.Kind)}: {warning.Package}";
            o.WriteLine(line);

            if (!packages)
                continue;
            foreach (var p in card.Packages)
            {
                var library = p.Library is null ? "no Library" : p.Library.Path;
                var locked = p.Lock == LockReason.None ? "" : $", locked: {p.Lock}";
                o.WriteLine($"      {p.DisplayName} ({p.Name}) {library}, {p.Presence}, {p.Role}{locked}");
            }
        }
    }

    private static string WarningText(WarningKind kind) => kind switch
    {
        WarningKind.DuplicatePackage => "Duplicate Package",
        WarningKind.DuplicateCopy => "Duplicate copy",
        WarningKind.LiveryWithoutAircraft => "Livery without aircraft",
        _ => kind.ToString(),
    };
}
