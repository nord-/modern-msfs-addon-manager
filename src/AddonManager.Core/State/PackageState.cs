namespace AddonManager.Core.State;

/// <summary>The user's stored data for one Package.</summary>
/// <param name="On">The user's on/off choice.</param>
/// <param name="ManagedElsewhere">Per-Package override of the Library's Managed elsewhere flag; <c>null</c> inherits it.</param>
/// <param name="ActiveCopyPath">The copy to link (rule E2); <c>null</c> lets <c>ActiveCopySelector</c> decide.</param>
public sealed record PackageState(bool On, bool? ManagedElsewhere = null, string? ActiveCopyPath = null);
