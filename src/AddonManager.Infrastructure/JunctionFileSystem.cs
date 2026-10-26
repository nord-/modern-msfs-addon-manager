using AddonManager.Core.Linking;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

/// <summary>Directory junctions via P/Invoke; no admin rights needed (spec C2).</summary>
/// <param name="setReparsePoint">Test seam that turns an empty folder into a junction; defaults to the Win32 call.</param>
public sealed class JunctionFileSystem(Func<string, string, bool>? setReparsePoint = null) : ILinkFileSystem
{
    private readonly Func<string, string, bool> setReparsePoint = setReparsePoint ?? NativeMethods.SetJunction;

    public void CreateJunction(string linkPath, string targetPath)
    {
        // Path.Exists is also true for a dangling junction, which FSCTL_SET_REPARSE_POINT would overwrite.
        if (Path.Exists(linkPath))
            throw new IOException($"'{linkPath}' already exists.");

        NativeMethods.CreateDirectory(linkPath);

        bool ok;
        try
        {
            ok = setReparsePoint(linkPath, targetPath);
        }
        catch (Exception ex)
        {
            Directory.Delete(linkPath, recursive: false);
            throw new IOException($"Could not link '{linkPath}' to '{targetPath}'.", ex);
        }

        if (!ok)
        {
            Directory.Delete(linkPath, recursive: false);
            throw new IOException($"Could not link '{linkPath}' to '{targetPath}'.");
        }
    }

    public void RemoveLink(string linkPath)
    {
        if (NativeMethods.GetReparseTag(linkPath) is not (NativeMethods.JunctionTag or NativeMethods.SymlinkTag))
            throw new InvalidOperationException($"'{linkPath}' is not a link; refusing to remove it.");

        Directory.Delete(linkPath, recursive: false);
    }

    /// <summary>The normalised target of a junction or symlink, or <c>null</c> when <paramref name="path"/> isn't a link.</summary>
    public static string? ReadLinkTarget(string path)
    {
        var target = new DirectoryInfo(path).LinkTarget;
        return target is null ? null : PathNormalizer.Normalize(target, Path.GetDirectoryName(Path.GetFullPath(path)));
    }
}
