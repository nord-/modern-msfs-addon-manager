using System.Diagnostics;
using System.Text;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.FirstRun;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;
using AddonManager.Sim2020.Content;

namespace AddonManager.Cli;

/// <summary>The result of one real scan: the outcome plus the settings it ran with.</summary>
internal sealed record ScanRun(ScanOutcome Outcome, UserState State);

/// <summary>Runs a content scan against the real machine. Writes only the scan cache (and the explicit --token-links file).</summary>
internal static class ScanCommand
{
    public const string Usage = "Usage: AddonManager.Cli scan [--full] [--token-links <file>] [--library <path>[=sections]]";

    /// <summary>One collapsed Token link: a Package name and code with the source ("name" or "title") that produced it.</summary>
    internal sealed record TokenRow(string Package, AirportCode Code, string Source);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!LibraryOptions.TryParse(args, out var libraries, out var rest, out var libraryError))
        {
            error.WriteLine(libraryError);
            return 2;
        }

        var full = false;
        string? tokenLinks = null;
        for (var i = 0; i < rest.Length; i++)
        {
            switch (rest[i])
            {
                case "--full": full = true; break;
                case "--token-links" when i + 1 < rest.Length: tokenLinks = rest[++i]; break;
                default:
                    error.WriteLine(Usage);
                    return 2;
            }
        }

        var run = Execute(full, libraries, output, out var exitCode);
        if (run is null)
            return exitCode;

        Report(run, output);
        if (tokenLinks is not null)
        {
            var rows = TokenRows(run.Outcome.Content.Contents, run.State.Content);
            WriteText(tokenLinks, string.Concat(rows.Select(r => $"{r.Package}\t{r.Code}\t{r.Source}\t?\r\n")));
            output.WriteLine($"Wrote {rows.Count} Token links to {tokenLinks}");
        }

        return 0;
    }

    /// <summary>Locates the install, loads user data and runs the scan with the stored Libraries, or <paramref name="libraryArgs"/> when given. Null when it cannot run. <paramref name="listLibraries"/> false leaves the Libraries and cache listing to the caller.</summary>
    public static ScanRun? Execute(bool full, IReadOnlyList<LibraryRoot> libraryArgs, TextWriter output, out int exitCode, bool listLibraries = true)
    {
        exitCode = 1;
        var paths = InstallLocator.Locate(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        if (paths is null)
        {
            output.WriteLine("No MSFS 2020 install found (no UserCfg.opt with InstalledPackagesPath).");
            return null;
        }

        // One instance for the cache and the user data: a second connection would look like another program holding the lock.
        UserDataDatabase db;
        UserState state;
        try
        {
            db = UserDataDatabase.Open(AppPaths.DatabaseFile, UserDataAccess.CacheOnly);
            try
            {
                state = new SqliteUserStateStore(db).Load();
            }
            catch
            {
                db.Dispose();
                throw;
            }
        }
        catch (UserDataException ex)
        {
            output.WriteLine($"User data cannot be used and was left untouched: {ex.Message}");
            return null;
        }

        using var owned = db;

        // --library replaces the stored Libraries for this run; nothing is saved.
        if (libraryArgs.Count > 0)
            state.Libraries = [.. libraryArgs];
        if (state.Libraries.Count == 0)
        {
            output.WriteLine(LibraryOptions.NoneRegistered);
            return null;
        }

        if (listLibraries)
        {
            output.WriteLine("Libraries:");
            foreach (var library in state.Libraries)
                output.WriteLine($"  {library.Path}  [{string.Join(", ", library.Sections)}]");
            output.WriteLine($"Database: {AppPaths.DatabaseFile}");
            output.WriteLine();
        }

        var cache = new SqliteScanCache(db);
        var reader = new PackageContentReader(new AirportNames(paths.OfficialPath));
        using var service = new ScanService(reader, cache, paths.CommunityPath, new OfficialPackages(paths.OfficialPath));
        try
        {
            var outcome = service.RunAsync(state, full).GetAwaiter().GetResult();
            exitCode = 0;
            return new ScanRun(outcome, state);
        }
        catch (UserDataException ex)
        {
            // Another program took the file mid-run: say so and fail, without a stack trace.
            output.WriteLine($"The scan stopped and user data was left untouched: {ex.Message}");
            return null;
        }
    }

    /// <summary>Distinct (Package name, code) Token links from <see cref="AirportLinker.TokenCandidates"/>, name before title, sorted by Package then code.</summary>
    internal static IReadOnlyList<TokenRow> TokenRows(IReadOnlyList<PackageContent> contents, ContentSettings settings)
    {
        var known = AirportLinker.KnownCodes(contents, settings);
        var rows = new Dictionary<(string Package, AirportCode Code), TokenRow>(new RowKeyComparer());
        foreach (var content in contents)
        {
            if (content.Source == PackageSource.SimManaged)
                continue;
            foreach (var candidate in AirportLinker.TokenCandidates(content, settings, known).Where(c => c.Linked))
            {
                var key = (content.Name.Value, candidate.Code);
                // Candidates list name tokens first, so the first one seen for a key has source "name" when there is one; across Duplicate copies keep "name" over "title".
                if (!rows.TryGetValue(key, out var existing) || (existing.Source != "name" && candidate.Source == "name"))
                    rows[key] = new TokenRow(content.Name.Value, candidate.Code, candidate.Source);
            }
        }

        return rows.Values
            .OrderBy(r => r.Package, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Code.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The code with its name, or the code alone when the name is unknown.</summary>
    internal static string Label(AirportCode code, IReadOnlyDictionary<AirportCode, Airport> airports)
        => airports.TryGetValue(code, out var airport) && !string.IsNullOrWhiteSpace(airport.Name) ? $"{code}  {airport.Name}" : code.Value;

    /// <summary>Names the registered Libraries that can't be read (a mistyped path or an unplugged drive), so a wall of locked cards has its explanation.</summary>
    internal static void WriteUnreachable(PackageIndex index, TextWriter o)
    {
        o.WriteLine($"Unreachable Libraries: {index.Unreachable.Count}");
        foreach (var library in index.Unreachable)
            o.WriteLine($"  {library.Path}");
    }

    internal static void WriteText(string file, string text)
        => File.WriteAllText(file, text, new UTF8Encoding(false));

    private static void Report(ScanRun run, TextWriter o)
    {
        var (outcome, state) = run;
        var settings = state.Content;
        var contents = outcome.Content.Contents;
        var analysis = outcome.Analysis;
        var airports = analysis.Airports;
        var comparer = StringComparer.OrdinalIgnoreCase;

        o.WriteLine($"Elapsed: {outcome.Elapsed.TotalSeconds:F1} s");
        WriteUnreachable(outcome.Discovery.Index, o);
        o.WriteLine("Targets:");
        foreach (var group in outcome.Discovery.Targets.GroupBy(t => t.Source).OrderBy(g => g.Key))
            o.WriteLine($"  {group.Key,-10} {group.Count()}");
        o.WriteLine($"Read {outcome.Content.Read}, reused {outcome.Content.Reused}, failed {outcome.Content.FailedCount}");
        o.WriteLine($"Unreadable BGL files: {contents.Sum(c => c.UnreadableBglCount)} in {contents.Count(c => c.UnreadableBglCount > 0)} Packages");
        o.WriteLine();

        o.WriteLine("Packages per Category:");
        foreach (var group in analysis.Categories.GroupBy(kv => kv.Value).OrderBy(g => g.Key))
            o.WriteLine($"  {group.Key,-9} {group.Count()}");
        o.WriteLine();

        var real = airports.Values.Where(a => a.HasRealScenery).OrderBy(a => a.Code.Value, comparer).ToList();
        o.WriteLine($"Airports with real scenery: {real.Count}");
        foreach (var airport in real)
            o.WriteLine($"  {Label(airport.Code, airports)}");
        o.WriteLine();

        o.WriteLine("Links per reason:");
        foreach (var group in analysis.AirportLinks.GroupBy(l => l.Reason).OrderBy(g => g.Key))
            o.WriteLine($"  {group.Key,-12} {group.Count()}");
        o.WriteLine();

        var tokens = TokenRows(contents, settings);
        o.WriteLine($"Token links: {tokens.Count}");
        foreach (var row in tokens)
            o.WriteLine($"  {row.Package}  {Label(row.Code, airports)}  \"{row.Code}\"");
        o.WriteLine();

        // Core keeps every copy (C8); the report lists each Package once.
        var excluded = analysis.Excluded.DistinctBy(c => c.Name.Value, comparer).ToList();
        o.WriteLine($"Excluded Packages: {excluded.Count}");
        foreach (var c in excluded)
            o.WriteLine($"  {c.Name}  {c.UsableAirports().Select(a => a.Code).Distinct().Count()} codes");
        o.WriteLine();

        var groups = analysis.Aircraft.Groups.Values.OrderBy(g => g.Container, comparer).ToList();
        o.WriteLine($"Aircraft groups: {groups.Count}");
        o.WriteLine("  Mods:");
        foreach (var g in groups.Where(g => g.LockedBases.Count > 0))
            o.WriteLine($"    {g.Container}  providers {g.Providers.Count}, locked bases {g.LockedBases.Count}");
        o.WriteLine("  Liveries without provider:");
        foreach (var g in groups.Where(g => g.Providers.Count == 0 && g.LockedBases.Count == 0 && g.Liveries.Count > 0))
            o.WriteLine($"    {g.Container}  liveries {g.Liveries.Count}");
        o.WriteLine("  Attached Packages:");
        foreach (var g in groups.Where(g => g.Attached.Count > 0))
            o.WriteLine($"    {g.Container}  {string.Join(", ", g.Attached.Select(p => p.Name.Value))}");
        o.WriteLine();

        // Aircraft-type Packages only; every scenery Package has role None too.
        var noRole = contents
            .Where(c => CategoryRules.IsAircraftType(c.Manifest.ContentType) && analysis.Aircraft.RoleOf(c) == AircraftRole.None)
            .OrderBy(c => c.Name.Value, comparer).ThenBy(c => c.Path, comparer).ToList();
        o.WriteLine($"Aircraft-type Packages with role None: {noRole.Count}");
        foreach (var c in noRole)
            o.WriteLine($"  {c.Name}");
        o.WriteLine();

        var linkedPaths = analysis.AirportLinks.Select(l => l.Package.Path).ToHashSet(comparer);
        var suspects = contents
            .Where(c => c.Source == PackageSource.SimManaged
                && CategoryRules.Provisional(c.Manifest.ContentType, c.HasBgl) == Category.Scenery
                && !linkedPaths.Contains(c.Path)
                && (!c.HasBgl || c.UnreadableBglCount > 0))
            .OrderBy(c => c.Name.Value, comparer).ToList();
        o.WriteLine($"Sim-managed scenery without an Airport link and with missing or unreadable BGL: {suspects.Count}");
        foreach (var c in suspects)
            o.WriteLine($"  {c.Name}  hasBgl={c.HasBgl} unreadable={c.UnreadableBglCount}");
    }

    private sealed class RowKeyComparer : IEqualityComparer<(string Package, AirportCode Code)>
    {
        public bool Equals((string Package, AirportCode Code) x, (string Package, AirportCode Code) y)
            => string.Equals(x.Package, y.Package, StringComparison.OrdinalIgnoreCase) && x.Code.Equals(y.Code);

        public int GetHashCode((string Package, AirportCode Code) k)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Package), k.Code);
    }
}
