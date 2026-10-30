namespace AddonManager.Core.State;

public interface IUserStateStore
{
    UserState Load();

    void Save(UserState state);

    /// <summary>
    /// Takes the write lock now, so a locked file refuses the change before any link is touched; a <see cref="Save"/> inside commits within it.
    /// The scope is synchronous and thread-affine: never <c>await</c> inside it. A nested call on the same thread joins the outer scope, and
    /// disposing the outermost one commits when a Save ran and rolls back otherwise. A Save outside any scope commits by itself.
    /// </summary>
    IDisposable BeginChange();
}
