using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.Tests.Fakes;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class PackageRemoverTests
{
    private const string LibPath = @"C:\Sim\Addons";
    private const string SimMessage = "Remove is unavailable while the sim runs.";
    private const string NotRemovableMessage = "Only a Package that is Not found or held by no registered Library can be removed.";

    private static readonly PackageKey Eham = PackageKey.Folder(new PackageName("eham-x"));

    private readonly FakeCommunity community = new();
    private readonly FakeLinkFileSystem fs;
    private readonly InMemoryUserStateStore store = new();
    private readonly FakeSim sim = new();
    private readonly PackageRemover remover;
    private readonly World w = new();

    public PackageRemoverTests()
    {
        fs = new FakeLinkFileSystem(community);
        var applier = new ChangeApplier(fs, community, store, sim, @"C:\MSFS\Community");
        remover = new PackageRemover(applier, community, sim, store);
        w.Library(LibPath);
    }

    /// <summary>A placed, linked Package whose folder then vanishes: its link is Broken and the board shows it as Not found.</summary>
    private void NotFoundPackage()
    {
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Linked("eham-x");
        w.Place();
        w.Without("eham-x");
        community.AddJunction("eham-x", Path.Combine(LibPath, "eham-x"), targetExists: false);
    }

    private void Remove()
    {
        var input = w.Input();
        remover.Remove(w.State, BoardBuilder.Build(w.State, input), input.Index, Eham);
    }

    [Fact]
    public void Remove_forgets_a_not_found_package_and_removes_its_broken_link()
    {
        NotFoundPackage();
        w.State.Pending.Add(Eham.Name);

        Remove();

        var call = Assert.Single(fs.Calls);
        Assert.Equal(FsCallKind.Remove, call.Kind);
        Assert.Equal(@"C:\MSFS\Community\eham-x", call.LinkPath);
        Assert.False(community.Has("eham-x"));
        Assert.DoesNotContain(Eham.Name, w.State.Packages.Keys);
        Assert.DoesNotContain(Eham.Name, w.State.Pending);
        Assert.Empty(w.State.Cards);
        var saved = store.Load();
        Assert.Empty(saved.Packages);
        Assert.Empty(saved.Cards);
    }

    [Fact]
    public void Removing_a_broken_package_commits_the_link_removal_and_the_forgetting_once()
    {
        NotFoundPackage();

        Remove();

        // RemoveBrokenLink and Remove both save, inside one change.
        Assert.Equal(2, store.SaveCount);
        Assert.Equal(1, store.CommitCount);
    }

    [Fact]
    public void Remove_refused_by_a_locked_file_touches_no_link_and_forgets_nothing()
    {
        NotFoundPackage();
        store.RefuseWith = new IOException("locked");

        Assert.Throws<IOException>(Remove);

        Assert.Empty(fs.Calls);
        Assert.True(community.Has("eham-x"));
        Assert.Contains(Eham.Name, w.State.Packages.Keys);
    }

    [Fact]
    public void Remove_is_refused_while_the_sim_runs_and_changes_nothing()
    {
        NotFoundPackage();
        sim.IsRunning = true;

        var error = Assert.Throws<InvalidOperationException>(Remove);

        Assert.Equal(SimMessage, error.Message);
        Assert.Empty(fs.Calls);
        Assert.True(community.Has("eham-x"));
        Assert.Contains(Eham.Name, w.State.Packages.Keys);
        Assert.Single(w.State.Cards);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Remove_is_refused_for_a_found_package_in_a_registered_library()
    {
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Linked("eham-x");
        w.Place();

        var error = Assert.Throws<InvalidOperationException>(Remove);

        Assert.Equal(NotRemovableMessage, error.Message);
        Assert.Contains(Eham.Name, w.State.Packages.Keys);
        Assert.Single(w.State.Cards);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Remove_is_refused_for_a_package_the_board_does_not_show()
    {
        var error = Assert.Throws<InvalidOperationException>(Remove);

        Assert.Equal(NotRemovableMessage, error.Message);
    }

    [Fact]
    public void Remove_keeps_the_package_when_the_broken_link_cannot_be_removed()
    {
        NotFoundPackage();
        var input = w.Input();
        var board = BoardBuilder.Build(w.State, input);
        var failing = new PackageRemover(new ChangeApplier(new LockedFileSystem(), community, store, sim, @"C:\MSFS\Community"), community, sim, store);

        Assert.Throws<IOException>(() => failing.Remove(w.State, board, input.Index, Eham));

        Assert.True(community.Has("eham-x"));
        Assert.Contains(Eham.Name, w.State.Packages.Keys);
        Assert.Single(w.State.Cards);
        Assert.Equal(0, store.SaveCount);
    }

    private sealed class LockedFileSystem : ILinkFileSystem
    {
        public void CreateJunction(string linkPath, string targetPath) => throw new IOException("Locked.");

        public void RemoveLink(string linkPath) => throw new IOException("Locked.");
    }

    [Fact]
    public void Remove_forgets_an_unlinked_package_no_registered_library_holds()
    {
        w.External(Scenery("eham-x", ["EHAM"]), @"D:\Ext\eham-x");
        w.Place();
        w.RemoveEntry("eham-x");
        var input = w.Input();
        var package = Assert.Single(Assert.Single(BoardBuilder.Build(w.State, input).Cards).Packages);
        Assert.Equal(LockReason.NotInRegisteredLibrary, package.Lock);

        Remove();

        Assert.Empty(fs.Calls);
        Assert.DoesNotContain(Eham.Name, w.State.Packages.Keys);
        Assert.Empty(w.State.Cards);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void Remove_leaves_the_other_packages_of_a_card_and_the_card_alone()
    {
        NotFoundPackage();
        w.Add(Scenery("eham-y", ["EHAM"]));
        w.Place();
        var card = w.State.CardOf(Eham)!;
        w.State.SetCard(PackageKey.Folder(new PackageName("eham-y")), card.Id);
        w.State.RemoveEmptyCards();

        Remove();

        Assert.Same(card, Assert.Single(w.State.Cards));
        Assert.Equal([PackageKey.Folder(new PackageName("eham-y"))], w.State.PackagesOn(card.Id));
    }
}
