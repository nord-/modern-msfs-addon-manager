using AddonManager.App.Startup;
using AddonManager.Infrastructure;

namespace AddonManager.App.Tests.Startup;

public class AppStartupTests
{
    private static string UniqueName() => "MmamTest" + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_data_dir_argument_picks_the_database()
    {
        Assert.Equal(Path.Combine(@"C:\scratch\data", "userdata.db"), AppStartup.DatabaseFile(["--data-dir", @"C:\scratch\data"]));
    }

    [Fact]
    public void Without_arguments_the_app_database_is_used()
    {
        Assert.Equal(AppPaths.DatabaseFile, AppStartup.DatabaseFile([]));
        Assert.Equal(AppPaths.DatabaseFile, AppStartup.DatabaseFile(["--data-dir"]));
    }

    [Fact]
    public void A_second_acquire_in_this_session_is_activated()
    {
        var name = UniqueName();
        using var activated = new ManualResetEventSlim();
        Assert.Null(AppStartup.AcquireOrSignal(name, activated.Set, out var first));
        using (first)
        {
            Assert.Equal(SecondStart.Activated, AppStartup.AcquireOrSignal(name, () => { }, out var second));
            Assert.Null(second);
            Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void A_global_mutex_without_an_event_here_is_another_session()
    {
        var name = UniqueName();
        using var mutex = new Mutex(false, @"Global\" + name, out var createdNew);
        Assert.True(createdNew);

        Assert.Equal(SecondStart.OtherSession, AppStartup.AcquireOrSignal(name, () => { }, out var guard));
        Assert.Null(guard);
    }

    [Fact]
    public void A_first_instance_that_exits_during_the_wait_lets_this_one_start()
    {
        var name = UniqueName();
        var mutex = new Mutex(false, @"Global\" + name, out _);

        // The first instance exits inside the retry wait, so the order does not depend on timing.
        var result = AppStartup.AcquireOrSignal(name, () => { }, out var guard, wait: mutex.Dispose);

        using (guard)
        {
            Assert.Null(result);
            Assert.NotNull(guard);
        }
    }
}
