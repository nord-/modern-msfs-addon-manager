using AddonManager.Core.Cards;
using AddonManager.Core.Content;

namespace AddonManager.Core.State;

/// <summary>The user's stored data for one Package.</summary>
/// <param name="On">The user's on/off choice.</param>
/// <param name="ActiveCopyPath">The copy to link (rule E2); <c>null</c> lets <c>ActiveCopySelector</c> decide.</param>
public sealed record PackageState(bool On, string? ActiveCopyPath = null)
{
    /// <summary>The card the Package is on; <c>null</c> while it has none (rule R2).</summary>
    public long? CardId { get; init; }

    /// <summary>The menu section the first settled read gave the Package; never changed afterwards (rule R4).</summary>
    public Category? Section { get; init; }

    /// <summary>The facts last seen, so a Not found or Library-not-found Package keeps its card (spec 11).</summary>
    public PackageMemo? Memo { get; init; }
}
