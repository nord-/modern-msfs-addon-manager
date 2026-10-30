using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Cards;

/// <summary>What the board is built from. <c>ScanComplete</c> is true only when <c>Contents</c> come from a completed content scan (rule A5).</summary>
public sealed record BoardInput(
    PackageIndex Index,
    IReadOnlyList<ClassifiedEntry> Community,
    IReadOnlyList<PackageContent> Contents,
    ContentAnalysis Analysis,
    bool ScanComplete)
{
    /// <summary>Normalised paths of Packages read while their folder was not quiet; they get no section and no card (rule R3).</summary>
    public IReadOnlySet<string> Unsettled { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Normalised paths of Packages the scan could not read without errors (from <c>ContentScanResult.FailedPaths</c>).</summary>
    public IReadOnlySet<string> Failed { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folders discovery could not read, from the index.</summary>
    public IReadOnlyList<string> Inaccessible => Index.Inaccessible;

    /// <summary>
    /// Normalised paths of Community folders and link targets outside every registered Library that exist on disk, so a Package last seen
    /// there is not gone. <c>null</c> until a run has completed (the startup board is built before any run); a completed run always sets it.
    /// </summary>
    public IReadOnlySet<string>? PresentOutsideLibraries { get; init; }

    /// <summary>The Airport links the Packages' own content gives them: no card overrides, filtered like <see cref="AirportCatalog.RealLinks"/>. What the memo's ICAO codes come from. Empty unless the input was built by <see cref="Reanalyze"/>; settle and build boards only from a re-analysed input.</summary>
    public IReadOnlyList<AirportLink> OwnLinks { get; init; } = [];

    /// <summary>True for an input built by <see cref="Reanalyze"/>; <see cref="Settlement.Apply"/> refuses any other.</summary>
    public bool Reanalyzed { get; private init; }

    /// <summary>The profile files found in the GSX folder (plan 4 reads them); <see cref="ProfileCatalog"/> decides which of them are Packages.</summary>
    public IReadOnlyList<GsxProfile> Profiles { get; init; } = [];

    /// <summary>False when the GSX folder does not exist: its profiles are Library not found and the banner says so (spec 8).</summary>
    public bool GsxFolderFound { get; init; } = true;

    /// <summary>True when the Package at <paramref name="path"/> was read completely and while quiet, so its facts may be stored (rule R3).</summary>
    public bool IsSettled(string path)
    {
        var normalized = PathNormalizer.Normalize(path);
        return !Unsettled.Contains(normalized) && !Failed.Contains(normalized);
    }

    public static BoardInput WithoutContent(PackageIndex index, IReadOnlyList<ClassifiedEntry> community)
        => new(index, community, [], ContentAnalyzer.Analyze([], new ContentSettings()), false);

    /// <summary>The same contents analysed with the content settings and the current cards, so the board always reflects them (<see cref="CardLinks"/>).</summary>
    public BoardInput Reanalyze(UserState state)
        => this with
        {
            Analysis = ContentAnalyzer.Analyze(Contents, state.Content, CardLinks.Overrides(state), CardLinks.Remembered(state, Contents)),
            OwnLinks = [.. AirportCatalog.RealLinks(AirportLinker.Link(Contents, state.Content, AirportLinkOverrides.None), state.Content).SelectMany(g => g)],
            Reanalyzed = true,
        };
}
