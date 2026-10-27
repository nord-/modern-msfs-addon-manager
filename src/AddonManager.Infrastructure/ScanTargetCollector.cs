using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

/// <summary>Lists the Package folders to scan in the order the sim loads them: Community, Libraries, then Sim-managed.</summary>
public static class ScanTargetCollector
{
    public static IReadOnlyList<ScanTarget> Collect(
        IReadOnlyList<ClassifiedEntry> community, string communityPath, PackageIndex index, ISimManagedPackages simManaged)
    {
        var targets = new List<ScanTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(PackageName name, string path, PackageSource source, Manifest manifest)
        {
            var normalised = PathNormalizer.Normalize(path);
            if (seen.Add(normalised))
                targets.Add(new ScanTarget(name, normalised, source, manifest));
        }

        void AddFolder(PackageName name, string path, PackageSource source)
            => Add(name, path, source, ManifestReader.Read(Path.Combine(path, "manifest.json")));

        void AddCopy(PackageCopy copy) => Add(copy.Name, copy.Path, PackageSource.Library, copy.Manifest);

        IEnumerable<ClassifiedEntry> Group(EntryClass entryClass)
            => community.Where(e => e.Class == entryClass).OrderBy(e => e.Entry.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Group(EntryClass.Fixed))
            AddFolder(entry.Name, Path.Combine(communityPath, entry.Entry.Name), PackageSource.Community);

        foreach (var entry in Group(EntryClass.External))
        {
            // A link into a Library copy under another name is scanned once, as that copy.
            if (entry.Entry.Target is { } target && entry.Entry.HasManifest
                && index.FindByPath(target) is null)
                AddFolder(entry.Name, target, PackageSource.Community);
        }

        foreach (var entry in Group(EntryClass.Managed))
        {
            if (entry.Package is { } copy)
                AddCopy(copy);
        }

        foreach (var copy in index.AllCopies)
            AddCopy(copy);

        foreach (var folder in simManaged.PackageFolders())
            AddFolder(new PackageName(Path.GetFileName(PathNormalizer.Normalize(folder))), folder, PackageSource.SimManaged);

        return targets;
    }
}
