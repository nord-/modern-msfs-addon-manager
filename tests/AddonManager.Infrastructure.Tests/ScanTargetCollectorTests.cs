using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure.Tests;

public sealed class ScanTargetCollectorTests : IDisposable
{
    private sealed class FakeSimManaged(params string[] folders) : ISimManagedPackages
    {
        public IReadOnlyList<string> PackageFolders() => folders;
    }

    private readonly TempFolder temp = new();
    private readonly string community;
    private readonly LibraryRoot library;
    private readonly JunctionFileSystem links = new();

    public ScanTargetCollectorTests()
    {
        community = Directory.CreateDirectory(Path.Combine(temp.Root, "community")).FullName;
        library = new LibraryRoot(Directory.CreateDirectory(Path.Combine(temp.Root, "lib")).FullName);
    }

    public void Dispose() => temp.Dispose();

    private string C(string name) => Path.Combine(community, name);

    private PackageCopy LibraryCopy(string name)
    {
        var path = temp.MakePackage(Path.Combine("lib", name));
        return new PackageCopy(new PackageName(name), path, library, Manifest.Empty);
    }

    private IReadOnlyList<ClassifiedEntry> Classify(PackageIndex index)
        => CommunityClassifier.Classify(new CommunityReader(community).Read().ToList(), index);

    private IReadOnlyList<ScanTarget> Collect(PackageIndex index, ISimManagedPackages? simManaged = null)
        => ScanTargetCollector.Collect(Classify(index), community, index, simManaged ?? new FakeSimManaged());

    private PackageIndex Index(params PackageCopy[] copies) => new(copies, [library]);

    [Fact]
    public void Orders_community_then_libraries_then_sim_managed()
    {
        var managed = LibraryCopy("managed-pkg");
        var unlinked = LibraryCopy("unlinked-pkg");
        links.CreateJunction(C("managed-pkg"), managed.Path);
        temp.MakePackage(@"community\fixed-pkg");
        links.CreateJunction(C("external-pkg"), temp.MakePackage(@"elsewhere\external-pkg"));
        var asobo = temp.MakePackage(@"official\asobo-airport-x");

        var targets = Collect(Index(managed, unlinked), new FakeSimManaged(asobo));

        Assert.Equal(["fixed-pkg", "external-pkg", "managed-pkg", "unlinked-pkg", "asobo-airport-x"], targets.Select(t => t.Name.Value));
        Assert.Equal([PackageSource.Community, PackageSource.Community, PackageSource.Library, PackageSource.Library, PackageSource.SimManaged],
            targets.Select(t => t.Source));
    }

    [Fact]
    public void Community_groups_are_each_in_entry_name_order()
    {
        var mB = LibraryCopy("b-managed");
        var mA = LibraryCopy("a-managed");
        links.CreateJunction(C("b-managed"), mB.Path);
        links.CreateJunction(C("a-managed"), mA.Path);
        temp.MakePackage(@"community\z-fixed");
        temp.MakePackage(@"community\y-fixed");
        links.CreateJunction(C("z-ext"), temp.MakePackage(@"elsewhere\z-ext"));
        links.CreateJunction(C("y-ext"), temp.MakePackage(@"elsewhere\y-ext"));

        var targets = Collect(Index(mB, mA));

        Assert.Equal(["y-fixed", "z-fixed", "y-ext", "z-ext", "a-managed", "b-managed"], targets.Select(t => t.Name.Value));
    }

    [Fact]
    public void Managed_entry_is_scanned_once_at_its_library_path_as_library_source()
    {
        var managed = LibraryCopy("managed-pkg");
        links.CreateJunction(C("managed-pkg"), managed.Path);

        var target = Assert.Single(Collect(Index(managed)));

        Assert.Equal(PathNormalizer.Normalize(managed.Path), target.Path);
        Assert.Equal(PackageSource.Library, target.Source);
    }

    [Fact]
    public void External_link_into_a_library_copy_is_scanned_as_that_library_copy()
    {
        var copy = LibraryCopy("real-name");
        links.CreateJunction(C("other-name"), copy.Path);
        var index = Index(copy);
        Assert.Equal(EntryClass.External, Classify(index).Single().Class);

        var targets = Collect(index);

        var target = Assert.Single(targets, t => t.Path == PathNormalizer.Normalize(copy.Path) && t.Source == PackageSource.Library);
        Assert.Equal("real-name", target.Name.Value);
        Assert.Single(targets);
    }

    [Fact]
    public void External_target_is_named_after_the_entry()
    {
        var path = temp.MakePackage(@"elsewhere\folder-name");
        links.CreateJunction(C("orbx-link-name"), path);

        var target = Assert.Single(Collect(Index()));

        Assert.Equal("orbx-link-name", target.Name.Value);
        Assert.Equal(PathNormalizer.Normalize(path), target.Path);
        Assert.Equal(PackageSource.Community, target.Source);
    }

    [Fact]
    public void A_link_to_a_folder_without_a_manifest_is_ignored_and_gives_no_target()
    {
        var path = Directory.CreateDirectory(Path.Combine(temp.Root, "elsewhere", "no-manifest")).FullName;
        links.CreateJunction(C("no-manifest"), path);

        Assert.Equal(EntryClass.Ignored, Classify(Index()).Single().Class);
        Assert.Empty(Collect(Index()));
    }

    [Fact]
    public void Managed_copies_come_before_unlinked_library_copies()
    {
        var managed = LibraryCopy("z-managed");
        var unlinked = LibraryCopy("a-unlinked");
        links.CreateJunction(C("z-managed"), managed.Path);

        var targets = Collect(Index(managed, unlinked));

        Assert.Equal(["z-managed", "a-unlinked"], targets.Select(t => t.Name.Value));
    }

    [Fact]
    public void Managed_entries_are_in_entry_name_order_not_library_order()
    {
        var second = new LibraryRoot(Directory.CreateDirectory(Path.Combine(temp.Root, "lib2")).FullName);
        var b = LibraryCopy("b-managed");
        var a = new PackageCopy(new PackageName("a-managed"), temp.MakePackage(@"lib2\a-managed"), second, Manifest.Empty);
        links.CreateJunction(C("a-managed"), a.Path);
        links.CreateJunction(C("b-managed"), b.Path);
        var index = new PackageIndex([a, b], [library, second]);
        Assert.Equal([EntryClass.Managed, EntryClass.Managed], Classify(index).Select(e => e.Class));

        var targets = Collect(index);

        Assert.Equal(["a-managed", "b-managed"], targets.Select(t => t.Name.Value));
    }

    [Fact]
    public void Stale_broken_ignored_and_stray_entries_give_no_target_of_their_own()
    {
        var gone = temp.MakePackage(@"elsewhere\gone");
        links.CreateJunction(C("broken"), gone);
        Directory.Delete(gone, true);
        Directory.CreateDirectory(C("ignored"));
        File.WriteAllText(C("stray.txt"), "");
        var stale = LibraryCopy("stale-pkg");
        var oldTarget = Directory.CreateDirectory(Path.Combine(temp.Root, "old", "stale-pkg")).FullName;
        links.CreateJunction(C("stale-pkg"), oldTarget);
        Directory.Delete(oldTarget);
        var index = Index(stale);

        var classes = Classify(index).Select(e => e.Class).ToHashSet();
        Assert.Contains(EntryClass.Stale, classes);
        Assert.Contains(EntryClass.Broken, classes);
        Assert.Contains(EntryClass.Ignored, classes);
        Assert.Contains(EntryClass.Stray, classes);

        // Only the Library copy itself is scanned, as a Library copy; no entry adds a Community target.
        var target = Assert.Single(Collect(index));
        Assert.Equal(PackageSource.Library, target.Source);
        Assert.Equal(PathNormalizer.Normalize(stale.Path), target.Path);
    }

    [Fact]
    public void Inactive_duplicate_copies_are_scanned_in_library_order()
    {
        var second = new LibraryRoot(Directory.CreateDirectory(Path.Combine(temp.Root, "lib2")).FullName);
        var a = new PackageCopy(new PackageName("dup"), temp.MakePackage(@"lib2\dup"), second, Manifest.Empty);
        var b = new PackageCopy(new PackageName("dup"), temp.MakePackage(@"lib\dup"), library, Manifest.Empty);
        var index = new PackageIndex([a, b], [library, second]);

        var targets = Collect(index);

        Assert.Equal([PathNormalizer.Normalize(b.Path), PathNormalizer.Normalize(a.Path)], targets.Select(t => t.Path));
    }

    [Fact]
    public void Repeated_paths_are_dropped_and_the_first_wins()
    {
        var path = temp.MakePackage(@"official\shared");

        var target = Assert.Single(Collect(Index(), new FakeSimManaged(path, path.ToUpperInvariant(), path + @"\")));

        Assert.Equal(PathNormalizer.Normalize(path), target.Path);
    }

    [Fact]
    public void Fixed_entry_manifest_is_read_from_disk()
    {
        var path = temp.MakePackage(@"community\fixed");
        File.WriteAllText(Path.Combine(path, "manifest.json"), """{ "title": "Fixed Title" }""");

        Assert.Equal("Fixed Title", Assert.Single(Collect(Index())).Manifest.Title);
    }

    [Fact]
    public void Library_copy_reuses_its_manifest()
    {
        var manifest = new Manifest("From the index", null, null, null, []);
        var copy = new PackageCopy(new PackageName("lib-pkg"), temp.MakePackage(@"lib\lib-pkg"), library, manifest);

        Assert.Same(manifest, Assert.Single(Collect(Index(copy))).Manifest);
    }
}
