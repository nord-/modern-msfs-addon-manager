namespace AddonManager.Core.Tests.Fakes;

/// <summary>A <see cref="TimeProvider"/> whose UTC time is set by the test.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
