namespace AddonManager.Core.Community;

public interface ICommunitySource
{
    IReadOnlyList<CommunityEntryInfo> Read();
}
