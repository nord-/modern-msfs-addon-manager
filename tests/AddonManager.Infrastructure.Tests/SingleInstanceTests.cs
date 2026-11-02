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

        Assert.Equal(SignalResult.Signalled, SingleInstance.TrySignalRunningInstance(name));
        await activated.Task.WaitAsync(Bound);
    }

    [Fact]
    public void Signalling_without_a_running_instance_returns_false()
    {
        Assert.False(SingleInstance.SignalRunningInstance(NewName()));
        Assert.Equal(SignalResult.NotRunning, SingleInstance.TrySignalRunningInstance(NewName()));
    }

    [Fact]
    public void The_mutex_lives_in_the_global_namespace()
    {
        var name = NewName();
        using var first = SingleInstance.TryAcquire(name, () => { });

        Assert.True(Mutex.TryOpenExisting(@"Global\" + name, out var mutex));
        mutex.Dispose();
    }

    [Fact]
    public void A_global_mutex_without_an_event_here_means_another_session()
    {
        var name = NewName();
        using var foreign = new Mutex(false, @"Global\" + name);

        Assert.Null(SingleInstance.TryAcquire(name, () => { }));
        Assert.False(SingleInstance.SignalRunningInstance(name));
    }

    private static string NewName() => $"{SingleInstance.AppName}.Test.{Guid.NewGuid():N}";
}
