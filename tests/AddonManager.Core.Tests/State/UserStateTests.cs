using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.State;

public class UserStateTests
{
    [Fact]
    public void Lock_is_inherited_from_the_active_copys_library_and_can_be_overridden()
    {
        var state = new UserState();
        var neofly = Copy("neofly-neofly-cargo", lib: new LibraryRoot(@"C:\Sim\Neofly\addons", ManagedElsewhere: true));
        var own = Copy("neofly-neofly-cargo", lib: new LibraryRoot(@"C:\Sim\Addons"));

        Assert.True(state.IsLocked(neofly));
        Assert.False(state.IsLocked(own));

        state.Packages[neofly.Name] = new PackageState(On: true, ManagedElsewhere: false);
        Assert.False(state.IsLocked(neofly));
    }

    [Fact]
    public void SetOn_keeps_the_other_fields_and_ignores_case()
    {
        var state = new UserState();
        state.Packages[new("X")] = new PackageState(On: false, ManagedElsewhere: true, ActiveCopyPath: @"C:\a\x");

        state.SetOn(new("x"), true);

        Assert.True(state.IsOn(new("X")));
        Assert.Equal(new PackageState(true, true, @"C:\a\x"), state.Packages[new("x")]);
    }

    [Fact]
    public void Forget_removes_the_choice_and_the_pending_mark()
    {
        var state = new UserState();
        state.SetOn(new("x"), true);
        state.Pending.Add(new("x"));

        state.Forget(new("X"));

        Assert.Empty(state.Packages);
        Assert.Empty(state.Pending);
    }

    [Fact]
    public void Snapshot_entries_keep_name_kind_target_and_whether_the_target_was_a_package()
    {
        var snapshot = SnapshotEntry.From([new CommunityEntryInfo("pkg", EntryKind.Junction, @"C:\lib\pkg", true, true)]);

        Assert.Equal([new SnapshotEntry("pkg", EntryKind.Junction, @"C:\lib\pkg", TargetWasPackage: true)], snapshot);
    }
}
