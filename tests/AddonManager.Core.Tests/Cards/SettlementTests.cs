using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class SettlementTests
{
    private const string Addons = @"C:\Sim\Addons";

    private static World NewWorld(params Category[] sections)
    {
        var w = new World();
        w.Library(Addons, sections);
        return w;
    }

    private static PackageName Name(string n) => new(n);

    private static PackageKey Key(string n) => PackageKey.Folder(Name(n));

    private static HashSet<string> Paths(params string[] paths)
        => new(paths.Select(p => PathNormalizer.Normalize(p)), StringComparer.OrdinalIgnoreCase);

    private static PackageMemo MemoAt(string path)
        => new(path, PackageSource.Library, "Gone", "SCENERY", null, null, BglPlacement.Places, [], [], AircraftRole.None, []);

    [Fact]
    public void Settling_refuses_an_unfinished_scan()
    {
        var w = NewWorld();
        w.Add(Scenery("a"));

        Assert.Throws<ArgumentException>(() => Settlement.Apply(w.State, w.Input(scanComplete: false), World.Now));
        Assert.Empty(w.State.Cards);
    }

    [Fact]
    public void Settling_refuses_an_input_that_was_not_reanalysed()
    {
        var w = NewWorld();
        w.Add(Scenery("a", ["EHAM"]));
        var input = w.Input();
        var raw = new BoardInput(input.Index, input.Community, input.Contents, input.Analysis, true);

        Assert.Throws<ArgumentException>(() => Settlement.Apply(w.State, raw, World.Now));
        Assert.Empty(w.State.Cards);
        Assert.All(w.State.Packages.Values, p => Assert.Null(p.Memo));
    }

    [Fact]
    public void A_settled_read_fixes_the_section_and_it_never_changes()
    {
        var w = NewWorld();
        w.Add(Scenery("a", hasBgl: false));

        Settlement.Apply(w.State, w.Input(), World.Now);
        Assert.Equal(Category.Other, w.State.Packages[Name("a")].Section);

        // The next scan finds BGL files: the section stays Other.
        w.Reread(Scenery("a", ["EHAM"]));
        Settlement.Apply(w.State, w.Input(), World.Now);

        Assert.Equal(Category.Other, w.State.Packages[Name("a")].Section);
        Assert.Equal(Category.Other, PackageCatalog.Build(w.State, w.Input())[Name("a")].FixedSection);
        Assert.Equal(Category.Other, Assert.Single(w.State.Cards).Section);
    }

    [Fact]
    public void An_unsettled_package_gets_no_section_and_no_card()
    {
        var w = NewWorld();
        w.Add(Scenery("a", ["EHAM"]));
        w.Add(Scenery("b"));
        var input = w.Input() with { Unsettled = Paths(Path.Combine(Addons, "a")) };

        Settlement.Apply(w.State, input, World.Now);

        Assert.False(w.State.Packages.ContainsKey(Name("a")));
        Assert.Null(w.State.CardOf(Key("a")));
        Assert.NotNull(w.State.CardOf(Key("b")));
    }

    [Fact]
    public void A_package_with_a_read_error_stays_provisional()
    {
        var w = NewWorld();
        w.Add(Scenery("a"));
        w.Add(Scenery("b"));
        w.WithoutContent("a");

        // "a": its copy has no content. "b": it has a cached row, but its path is in Failed.
        var input = w.Input() with { Failed = Paths(Path.Combine(Addons, "b")) };
        Settlement.Apply(w.State, input, World.Now);

        Assert.Empty(w.State.Cards);
        Assert.All(["a", "b"], n =>
        {
            Assert.Null(w.State.Packages.GetValueOrDefault(Name(n))?.Section);
            Assert.Null(w.State.Packages.GetValueOrDefault(Name(n))?.Memo);
        });
        var board = BoardBuilder.Build(w.State, input);
        Assert.Equal(2, board.Cards.Count(c => c.ProvisionalPackage is not null));
    }

    [Fact]
    public void A_not_shown_package_read_with_an_error_gets_no_card_when_its_section_is_registered()
    {
        var w = NewWorld(Category.Scenery);
        w.Add(Scenery("a", ["EHAM"]));
        w.State.Packages[Name("a")] = new PackageState(false) { Section = Category.Scenery };
        var input = w.Input() with { Failed = Paths(Path.Combine(Addons, "a")) };

        Settlement.Apply(w.State, input, World.Now);

        Assert.Empty(w.State.Cards);
        Assert.Null(w.State.CardOf(Key("a")));
    }

    [Fact]
    public void Only_packages_in_a_library_registered_for_their_section_get_cards()
    {
        var w = NewWorld(Category.Other);
        w.Add(Scenery("a", ["EHAM"]));
        w.Add(Build("misc", "MISC"));

        Settlement.Apply(w.State, w.Input(), World.Now);

        Assert.Null(w.State.CardOf(Key("a")));
        Assert.NotNull(w.State.CardOf(Key("misc")));
        var row = Assert.Single(BoardBuilder.Build(w.State, w.Input()).NotShown);
        Assert.Equal((Name("a"), Category.Scenery, true), (row.Name, row.Section, row.CanShowIn));
    }

    [Fact]
    public void A_duplicate_copy_in_a_scenery_library_makes_the_package_eligible()
    {
        var w = NewWorld(Category.Other);
        var scenery = w.Library(@"C:\Sim\Scenery Library", Category.Scenery);
        w.Add(Scenery("dup", ["EHAM"]));
        w.Add(Scenery("dup", ["EHAM"]), library: scenery);

        Settlement.Apply(w.State, w.Input(), World.Now);

        Assert.NotNull(w.State.CardOf(Key("dup")));
    }

    [Fact]
    public void Fixed_and_external_packages_always_get_cards()
    {
        var w = NewWorld(Category.Other);
        w.Fixed(Scenery("fx", ["EHAM"]));
        w.External(Scenery("ex", ["EGLL"]), @"D:\Elsewhere\ex");

        Settlement.Apply(w.State, w.Input(), World.Now);

        Assert.NotNull(w.State.CardOf(Key("fx")));
        Assert.NotNull(w.State.CardOf(Key("ex")));
    }

    [Fact]
    public void Registering_a_section_later_groups_the_packages_it_makes_eligible_among_themselves()
    {
        var w = NewWorld(Category.Other);
        w.Add(Scenery("eham-a", ["EHAM"]));
        w.Add(Scenery("eham-b", ["EHAM"]));
        Settlement.Apply(w.State, w.Input(), World.Now);
        Assert.Empty(w.State.Cards);

        w.State.Libraries[0] = w.State.Libraries[0] with { Sections = [Category.Other, Category.Scenery] };
        Settlement.Apply(w.State, w.Input(), World.Now);

        var card = Assert.Single(w.State.Cards);
        Assert.Equal([Code("EHAM")], card.Codes);
        Assert.Equal([Key("eham-a"), Key("eham-b")], w.State.PackagesOn(card.Id));
    }

    [Fact]
    public void A_package_without_a_card_that_disappears_is_forgotten()
    {
        var w = NewWorld();
        w.State.Packages[Name("gone")] = new PackageState(true, Path.Combine(Addons, "gone")) { Memo = MemoAt(Path.Combine(Addons, "gone")) };

        var changed = Settlement.Apply(w.State, w.Input(), World.Now);

        Assert.True(changed);
        Assert.False(w.State.Packages.ContainsKey(Name("gone")));
    }

    [Fact]
    public void A_package_without_a_card_on_an_unreachable_library_is_kept()
    {
        var w = new World();
        var library = w.Library(@"D:\Unplugged");
        var path = @"D:\Unplugged\away";
        w.State.Packages[Name("away")] = new PackageState(true, path) { Memo = MemoAt(path) };
        w.Unreachable(library);

        Settlement.Apply(w.State, w.Input(), World.Now);

        Assert.True(w.State.Packages.ContainsKey(Name("away")));
        var card = Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards);
        Assert.Equal(Key("away"), card.ProvisionalPackage);
        Assert.Equal(Presence.LibraryNotFound, Assert.Single(card.Packages).Presence);
    }

    [Fact]
    public void A_package_without_a_card_under_a_folder_discovery_could_not_read_is_kept()
    {
        var w = NewWorld();
        var path = Path.Combine(Addons, "Locked", "x");
        w.State.Packages[Name("x")] = new PackageState(true, path) { Memo = MemoAt(path) };
        w.Inaccessible(Path.Combine(Addons, "Locked"));
        var input = w.Input();

        Settlement.Apply(w.State, input, World.Now);

        var kept = w.State.Packages[Name("x")];
        Assert.True(kept.On);
        Assert.Equal(path, kept.ActiveCopyPath);
        Assert.Single(BoardBuilder.Build(w.State, input).Cards);
    }

    [Fact]
    public void Memos_record_tags_type_designators_and_airport_positions()
    {
        var w = NewWorld();
        w.Add(Scenery("a", fullRecords: [Rec("EHAM", "Schiphol", 52.3, 4.76)]), folder: @"Europe\a");
        w.Add(Build("b", "MISC"), folder: @"Europe\b");
        w.Add(Aircraft("plane", [("Plane", null)]) with
        {
            AircraftConfigs = [new AircraftConfig("Plane", null, "A320"), new AircraftConfig("Plane2", null, "A20N"), new AircraftConfig("Plane3", null)],
        });

        Settlement.Apply(w.State, w.Input(), World.Now);

        var scenery = Assert.IsType<PackageMemo>(w.State.Packages[Name("a")].Memo);
        Assert.Equal(["Europe"], scenery.Tags);
        Assert.Equal(new AirportRecord(Code("EHAM"), "Schiphol", new GeoPoint(52.3, 4.76)), Assert.Single(scenery.Airports));
        Assert.Equal(["A20N", "A320"], Assert.IsType<PackageMemo>(w.State.Packages[Name("plane")].Memo).TypeDesignators);
        Assert.Empty(Assert.IsType<PackageMemo>(w.State.Packages[Name("b")].Memo).TypeDesignators);
    }

    [Fact]
    public void The_first_content_pass_marker_is_set()
    {
        var w = NewWorld();
        Assert.False(w.State.ContentPassCompleted);

        Assert.True(Settlement.Apply(w.State, w.Input(), World.Now));

        Assert.True(w.State.ContentPassCompleted);
        Assert.False(Settlement.Apply(w.State, w.Input(), World.Now));
    }
}
