namespace AddonManager.Infrastructure.Tests;

public sealed class SingleInstanceTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public void The_first_acquire_wins_and_the_second_gets_null()
    {
        var name = NewName();

        using var first = SingleInstance.TryAcquire(name, () => { });
        using var second = SingleInstance.TryAcquire(name, () => { });

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void After_dispose_the_name_can_be_acquired_again()
    {
        var name = NewName();
        SingleInstance.TryAcquire(name, () => { })!.Dispose();

        using var again = SingleInstance.TryAcquire(name, () => { });

        Assert.NotNull(again);
    }

    [Fact]
    public async Task A_signal_reaches_the_running_instance()
    {
        var name = NewName();
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = SingleInstance.TryAcquire(name, () => activated.TrySetResult());

        Assert.True(SingleInstance.SignalRunningInstance(name));
        await activated.Task.WaitAsync(Bound);
    }

    [Fact]
    public void Signalling_without_a_running_instance_returns_false()
    {
        Assert.False(SingleInstance.SignalRunningInstance(NewName()));
    }

    private static string NewName() => $"{SingleInstance.AppName}.Test.{Guid.NewGuid():N}";
}
