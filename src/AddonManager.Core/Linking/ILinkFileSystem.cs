namespace AddonManager.Core.Linking;

/// <summary>Creates and removes links in Community. Implementations never delete through a link.</summary>
public interface ILinkFileSystem
{
    /// <summary>Creates a directory junction. Throws <see cref="IOException"/> when anything already holds the name.</summary>
    void CreateJunction(string linkPath, string targetPath);

    /// <summary>Removes a junction or symlink only. Throws <see cref="InvalidOperationException"/> for anything else.</summary>
    void RemoveLink(string linkPath);
}
