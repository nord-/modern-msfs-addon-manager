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
    public static int Run(TextWriter output)
    {
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
        output.WriteLine($"User data:    {AppPaths.UserDataFile}");
        output.WriteLine();

        Core.State.UserState state;
        try
        {
            state = new JsonUserStateStore(AppPaths.UserDataFile).Load();
        }
        catch (UserStateCorruptException ex)
        {
            output.WriteLine($"User data is corrupt and was left untouched: {ex.Message}");
            return 1;
        }

        var entries = new CommunityReader(paths.CommunityPath).Read();

        // Without saved Libraries every junction would show as External, so use the proposal instead.
        IReadOnlyList<LibraryRoot> libraries = state.Libraries;
        var label = "Libraries:";
        if (libraries.Count == 0)
        {
            libraries = LibraryProposer.Propose(entries, new DiskFolderTree()).Select(p => new LibraryRoot(p.Path)).ToList();
            label = "Libraries (proposed):";
        }
        output.WriteLine(label);
        foreach (var library in libraries)
            output.WriteLine($"  {library.Path}{(library.ManagedElsewhere ? "  [managed elsewhere]" : "")}");
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
            var rows = Reconciler.Diff(state, classified, index);
            output.WriteLine($"Sync rows: {rows.Count}");
            foreach (var row in rows)
                output.WriteLine($"  {row.Package}: {row.Reason}{(row.NewTarget is null ? "" : " -> " + row.NewTarget)} (default {row.DefaultChoice})");
        }

        return 0;
    }
}
