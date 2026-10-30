using AddonManager.Core.Community;
using AddonManager.Core.FirstRun;
using AddonManager.Core.Packages;
using AddonManager.Core.Sync;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;

namespace AddonManager.Cli;

/// <summary>Shows what the engine sees on this machine. Never creates, removes or saves anything.</summary>
internal static class StatusCommand
{
    public const string Usage = "Usage: AddonManager.Cli status [--library <path>[=sections]]";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!LibraryOptions.TryParse(args, out var libraryArgs, out var rest, out var libraryError))
        {
            error.WriteLine(libraryError);
            return 2;
        }

        if (rest.Length > 0)
        {
            error.WriteLine(Usage);
            return 2;
        }

        var paths = InstallLocator.Locate(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        if (paths is null)
        {
            output.WriteLine("No MSFS 2020 install found (no UserCfg.opt with InstalledPackagesPath).");
            return 1;
        }

        output.WriteLine($"UserCfg.opt:  {paths.UserCfgPath}");
        output.WriteLine($"Packages:     {paths.PackagesPath}");
        output.WriteLine($"Community:    {paths.CommunityPath}");
        output.WriteLine($"Official:     {paths.OfficialPath ?? "(not found)"}");
        output.WriteLine($"User data:    {AppPaths.DatabaseFile}");
        output.WriteLine();

        // A missing file means first run: status never creates it.
        Core.State.UserState state;
        try
        {
            // Opening an existing file CacheOnly may touch only the cache tables and side files, never the user tables.
            if (File.Exists(AppPaths.DatabaseFile))
            {
                using var db = UserDataDatabase.Open(AppPaths.DatabaseFile, UserDataAccess.CacheOnly);
                state = new SqliteUserStateStore(db).Load();
            }
            else
            {
                state = new Core.State.UserState();
            }
        }
        catch (UserDataException ex)
        {
            output.WriteLine($"User data cannot be used and was left untouched: {ex.Message}");
            return 1;
        }

        var entries = new CommunityReader(paths.CommunityPath).Read();

        // --library replaces the stored Libraries for this run; nothing is saved.
        IReadOnlyList<LibraryRoot> libraries = libraryArgs.Count > 0 ? libraryArgs : state.Libraries;
        if (libraries.Count == 0)
        {
            output.WriteLine(LibraryOptions.NoneRegistered);
        }
        else
        {
            output.WriteLine("Libraries:");
            foreach (var library in libraries)
                output.WriteLine($"  {library.Path}  [{string.Join(", ", library.Sections)}]");
        }
        output.WriteLine();

        var coverage = LinkCoverage.Measure(entries, libraries);
        output.WriteLine($"Links covered: {coverage.Covered} of {coverage.Counted}");
        output.WriteLine($"Not counted (made by other tools): {coverage.NotCounted.Count}");
        foreach (var entry in coverage.NotCounted)
            output.WriteLine($"  {entry.Name}");
        output.WriteLine();

        var discovery = LibraryScanner.Discover(libraries);
        var index = new PackageIndex(discovery.Packages, libraries, discovery.Unreachable);
        var classified = CommunityClassifier.Classify(entries, index);

        output.WriteLine($"Packages in Libraries: {discovery.Packages.Count}");
        output.WriteLine($"Community entries: {entries.Count}");
        foreach (var group in classified.GroupBy(e => e.Class).OrderBy(g => g.Key))
            output.WriteLine($"  {group.Key,-9} {group.Count()}");
        output.WriteLine();

        var duplicates = index.Names.Where(index.IsDuplicateCopy).OrderBy(n => n.Value, StringComparer.OrdinalIgnoreCase).ToList();
        output.WriteLine($"Duplicate copies: {duplicates.Count}");
        foreach (var name in duplicates)
        {
            var communityTarget = classified.FirstOrDefault(e => e.Name == name && e.Class == EntryClass.Managed)?.Entry.Target;
            var active = ActiveCopySelector.Select(name, index, communityTarget, state.Packages.GetValueOrDefault(name)?.ActiveCopyPath);
            output.WriteLine($"  {name}");
            foreach (var copy in index.CopiesOf(name))
                output.WriteLine($"    {(copy == active ? "*" : " ")} {copy.Path}");
        }
        output.WriteLine();

        output.WriteLine($"Unreachable Libraries: {discovery.Unreachable.Count}");
        foreach (var library in discovery.Unreachable)
            output.WriteLine($"  {library.Path}");

        foreach (var e in classified.Where(e => e.Class is EntryClass.Broken or EntryClass.Stray or EntryClass.Stale or EntryClass.Offline))
            output.WriteLine($"{e.Class}: {e.Entry.Name}{(e.Entry.Target is null ? "" : " -> " + e.Entry.Target)}");

        if (state.Snapshot is null)
        {
            output.WriteLine("No accepted snapshot yet (first run): Community would be adopted as is.");
        }
        else
        {
            // `status` has no content scan, so it knows no Not found or Not shown Packages.
            var rows = Reconciler.Diff(state, classified, index, new HashSet<PackageName>(), new HashSet<PackageName>());
            output.WriteLine($"Sync rows: {rows.Count}");
            foreach (var row in rows)
                output.WriteLine($"  {row.Package}: {row.Reason}{(row.NewTarget is null ? "" : " -> " + row.NewTarget)} (default {row.DefaultChoice})");
        }

        return 0;
    }
}
