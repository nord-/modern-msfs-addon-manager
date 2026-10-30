using System.IO;

namespace AddonManager.Infrastructure.Tests;

public sealed class ContentWatcherTests : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private readonly TempFolder _temp = new();
    private readonly string _community;
    private readonly string _lib;
    private int _rescans;
    private int _communityChanges;

    public ContentWatcherTests()
    {
        _community = Directory.CreateDirectory(Path.Combine(_temp.Root, "Community")).FullName;
        _lib = Directory.CreateDirectory(Path.Combine(_temp.Root, "Lib")).FullName;
    }

    public void Dispose() => _temp.Dispose();

    private int Rescans => Volatile.Read(ref _rescans);
    private int CommunityChanges => Volatile.Read(ref _communityChanges);

    private ContentWatcher StartWatcher(TimeSpan? quiet = null, params string[] libraries)
    {
        var watcher = new ContentWatcher(_community, libraries.Length == 0 ? [_lib] : libraries, quiet ?? Quiet);
        watcher.RescanNeeded += () => Interlocked.Increment(ref _rescans);
        watcher.CommunityChanged += () => Interlocked.Increment(ref _communityChanges);
        watcher.Start();
        return watcher;
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met within the timeout.");
            await Task.Delay(25);
        }
    }

    private static Task PastQuietPeriod(TimeSpan? quiet = null) => Task.Delay((quiet ?? Quiet) * 4);

    [Fact]
    public async Task A_burst_of_changes_raises_one_rescan()
    {
        for (var i = 0; i < 50; i++)
            Directory.CreateDirectory(Path.Combine(_lib, $"p{i}"));
        var oneSecond = TimeSpan.FromSeconds(1);
        using var watcher = StartWatcher(oneSecond);

        for (var i = 0; i < 50; i++)
            File.WriteAllText(Path.Combine(_lib, $"p{i}", "manifest.json"), "{}");
        await WaitUntil(() => Rescans >= 1, Bound);
        await Task.Delay(oneSecond * 2);

        // A stall between two writes may split the burst; it must still be far fewer rescans than the 50 writes.
        Assert.InRange(Rescans, 1, 5);
    }

    [Fact]
    public async Task A_scheduled_rescan_is_raised_once_at_its_time()
    {
        using var watcher = StartWatcher();

        // A wide margin (500 ms ahead, checked after 50 ms) so a stalled runner cannot raise it before the check.
        watcher.ScheduleRescan(DateTimeOffset.UtcNow.AddMilliseconds(500));
        await Task.Delay(50);
        Assert.Equal(0, Rescans);

        await WaitUntil(() => Rescans == 1, TimeSpan.FromSeconds(1));
        await PastQuietPeriod();
        Assert.Equal(1, Rescans);
        Assert.Equal(0, CommunityChanges);
    }

    [Fact]
    public async Task A_scheduled_rescan_in_the_past_is_raised_right_away()
    {
        using var watcher = StartWatcher();

        watcher.ScheduleRescan(DateTimeOffset.UtcNow.AddMinutes(-1));

        await WaitUntil(() => Rescans == 1, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task An_event_burst_and_a_scheduled_rescan_raise_once()
    {
        // A long quiet period (1 s) leaves a wide window between the scheduled time and the burst's due time, so the check below
        // always runs and a stalled runner cannot slip past it.
        var quiet = TimeSpan.FromSeconds(1);
        using var watcher = StartWatcher(quiet);
        var scheduled = DateTimeOffset.UtcNow.AddMilliseconds(150);

        watcher.ScheduleRescan(scheduled);
        Directory.CreateDirectory(Path.Combine(_lib, "p0"));
        await Task.Delay(100);
        Directory.CreateDirectory(Path.Combine(_lib, "p1"));

        // The last event is due a quiet period after it, which is later than the scheduled time.
        // Wait until the scheduled time has passed, still well inside the quiet period: nothing may be raised yet.
        await Task.Delay(scheduled - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(200));
        Assert.Equal(0, Rescans);
        await WaitUntil(() => Rescans >= 1, Bound);
        await PastQuietPeriod(quiet);
        Assert.Equal(1, Rescans);
    }

    [Fact]
    public async Task Community_is_watched_non_recursively()
    {
        var inner = Directory.CreateDirectory(Path.Combine(_community, "real-folder")).FullName;
        using var watcher = StartWatcher();

        File.WriteAllText(Path.Combine(inner, "manifest.json"), "{}");
        Directory.CreateDirectory(Path.Combine(inner, "sub"));
        await PastQuietPeriod();
        Assert.Equal(0, Rescans);

        Directory.CreateDirectory(Path.Combine(_community, "new-entry"));
        await WaitUntil(() => Rescans == 1, Bound);
        await PastQuietPeriod();
        Assert.Equal(1, Rescans);
    }

    [Fact]
    public async Task Library_changes_at_any_depth_are_seen()
    {
        var deep = Directory.CreateDirectory(Path.Combine(_lib, "a", "b", "c")).FullName;
        using var watcher = StartWatcher();

        File.WriteAllText(Path.Combine(deep, "layout.json"), "{}");

        await WaitUntil(() => Rescans == 1, Bound);
    }

    [Fact]
    public async Task Community_changed_fires_only_for_community_events()
    {
        using var watcher = StartWatcher();

        Directory.CreateDirectory(Path.Combine(_lib, "pkg"));
        await WaitUntil(() => Rescans == 1, Bound);
        await PastQuietPeriod();
        Assert.Equal(1, Rescans);
        Assert.Equal(0, CommunityChanges);

        Directory.CreateDirectory(Path.Combine(_community, "link"));
        await WaitUntil(() => Rescans == 2, Bound);
        await PastQuietPeriod();
        Assert.Equal(2, Rescans);
        Assert.Equal(1, CommunityChanges);
    }

    [Fact]
    public async Task Overflow_raises_a_rescan()
    {
        using var watcher = StartWatcher();

        watcher.SimulateError(new InternalBufferOverflowException(), fromCommunity: false);

        await WaitUntil(() => Rescans == 1, Bound);
        await PastQuietPeriod();
        Assert.Equal(1, Rescans);
        Assert.Equal(0, CommunityChanges);
    }

    [Fact]
    public async Task A_community_error_counts_as_a_community_event()
    {
        using var watcher = StartWatcher();

        watcher.SimulateError(new InternalBufferOverflowException(), fromCommunity: true);

        await WaitUntil(() => CommunityChanges == 1, Bound);
        await PastQuietPeriod();
        Assert.Equal(1, Rescans);
        Assert.Equal(1, CommunityChanges);
    }

    [Fact]
    public async Task Missing_library_is_skipped_without_throwing()
    {
        var missing = Path.Combine(_temp.Root, "Unplugged", "Lib");

        using var watcher = StartWatcher(null, _lib, missing);

        Directory.CreateDirectory(Path.Combine(_lib, "pkg"));
        await WaitUntil(() => Rescans == 1, Bound);
    }

    [Fact]
    public async Task No_event_after_dispose()
    {
        var quiet = TimeSpan.FromSeconds(1);
        var watcher = StartWatcher(quiet);
        Directory.CreateDirectory(Path.Combine(_lib, "pending"));
        await WaitUntil(() => watcher.EventCount > 0, Bound);

        watcher.Dispose();
        Directory.CreateDirectory(Path.Combine(_community, "late"));
        await PastQuietPeriod(quiet);

        Assert.Equal(0, Rescans);
        Assert.Equal(0, CommunityChanges);
    }

    [Fact]
    public async Task Disposing_inside_RescanNeeded_suppresses_CommunityChanged()
    {
        ContentWatcher? watcher = null;
        watcher = new ContentWatcher(_community, [_lib], Quiet);
        watcher.RescanNeeded += () =>
        {
            Interlocked.Increment(ref _rescans);
            watcher.Dispose();
        };
        watcher.CommunityChanged += () => Interlocked.Increment(ref _communityChanges);
        watcher.Start();

        watcher.SimulateError(new InternalBufferOverflowException(), fromCommunity: true);

        await WaitUntil(() => Rescans == 1, Bound);
        await PastQuietPeriod();
        Assert.Equal(0, CommunityChanges);
    }
}
