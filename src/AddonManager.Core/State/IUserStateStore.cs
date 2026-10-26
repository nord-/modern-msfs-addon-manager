namespace AddonManager.Core.State;

public interface IUserStateStore
{
    UserState Load();

    void Save(UserState state);
}
