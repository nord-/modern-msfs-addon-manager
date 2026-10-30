using AddonManager.Core.Packages;

namespace AddonManager.Core.Community;

public enum EntryClass { Managed, Stale, Offline, External, Fixed, Ignored, Broken, Stray }

/// <param name="Package">The Library copy a Managed entry points to, or the copy a Stale entry should point to.</param>
public sealed record ClassifiedEntry(CommunityEntryInfo Entry, EntryClass Class, PackageCopy? Package)
{
    public PackageName Name => new(Entry.Name);
}

/// <summary>Classifies Community entries against the registered Libraries (spec 6.2, rules E1 and E6).</summary>
public static class CommunityClassifier
{
    public static IReadOnlyList<ClassifiedEntry> Classify(IReadOnlyList<CommunityEntryInfo> entries, PackageIndex index)
        => entries.Select(e => Classify(e, index)).ToList();

    private static ClassifiedEntry Classify(CommunityEntryInfo e, PackageIndex index)
    {
        switch (e.Kind)
        {
            case EntryKind.File:
                return new(e, EntryClass.Stray, null);
            case EntryKind.Directory:
                return new(e, e.HasManifest ? EntryClass.Fixed : EntryClass.Ignored, null);
        }

        if (e.Target is null || !IsDriveOrUncPath(e.Target))
            return new(e, EntryClass.External, null);

        var name = new PackageName(e.Name);

        // A folder discovery could not read says nothing about what is inside: the link is neither Broken nor External, and never offered for removal.
        if (index.IsInaccessible(e.Target))
            return new(e, EntryClass.Offline, null);

        if (e.TargetExists)
        {
            // E1: a link is Managed only when its name is the Package's own name.
            var copy = index.FindByPath(e.Target);
            if (copy is not null && copy.Name == name)
                return new(e, EntryClass.Managed, copy);
            return new(e, e.HasManifest ? EntryClass.External : EntryClass.Ignored, null);
        }

        if (index.IsUnreachable(e.Target))
            return new(e, EntryClass.Offline, null);

        var found = index.CopiesOf(name);
        return found.Count > 0 ? new(e, EntryClass.Stale, found[0]) : new(e, EntryClass.Broken, null);
    }

    private static bool IsDriveOrUncPath(string path)
        => (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
           || (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal));
}
