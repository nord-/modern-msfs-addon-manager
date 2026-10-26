namespace AddonManager.Core.Packages;

/// <summary>Picks the one copy of a Package that may be linked (spec 6.1, rule E2).</summary>
public static class ActiveCopySelector
{
    /// <summary>Precedence: the user's chosen path, then the copy Community points to, then Library order and path.</summary>
    public static PackageCopy? Select(PackageName name, PackageIndex index, string? communityTarget, string? chosenPath)
    {
        var copies = index.CopiesOf(name);
        if (copies.Count == 0)
            return null;

        return Match(copies, chosenPath) ?? Match(copies, communityTarget) ?? copies[0];
    }

    private static PackageCopy? Match(IReadOnlyList<PackageCopy> copies, string? path)
        => path is null ? null : copies.FirstOrDefault(c => PathNormalizer.AreEqual(c.Path, path));
}
