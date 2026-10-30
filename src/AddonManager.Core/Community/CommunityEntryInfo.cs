namespace AddonManager.Core.Community;

public enum EntryKind { Junction, Symlink, Directory, File }

/// <summary>One top-level entry in Community as found on disk.</summary>
/// <param name="Target">Normalised link target, resolved against Community; <c>null</c> for non-links.</param>
/// <param name="TargetExists">Whether the link target exists (checked on the target, never on the link).</param>
/// <param name="Path">The entry's own full path in Community when known; a real folder's path is its only location when no content was read for it.</param>
/// <param name="HasManifest">Whether the folder itself (Directory) or the link target (links) holds a <c>manifest.json</c>.</param>
public sealed record CommunityEntryInfo(string Name, EntryKind Kind, string? Target, bool TargetExists, bool HasManifest, string? Path = null)
{
    public bool IsLink => Kind is EntryKind.Junction or EntryKind.Symlink;
}
