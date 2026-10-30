using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Tests.Fakes;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class GsxProfileTests
{
    private static World NewWorld()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        return w;
    }

    private static PackageKey Folder(string n) => PackageKey.Folder(new PackageName(n));

    private static PackageKey Profile(string n) => new(PackageKind.GsxProfile, new PackageName(n));

    private static Board Wall(World w, bool place = true)
    {
        if (place)
        {
            w.Place();
        }

        return BoardBuilder.Build(w.State, w.Input());
    }

    private static CardPackage ProfileRow(Board board, string name)
        => Assert.IsType<Card>(board.CardOf(Profile(name))).Packages.Single(p => p.Kind == PackageKind.GsxProfile && p.Name == new PackageName(name));

    [Fact]
    public void A_profile_for_an_airport_without_a_card_gets_an_airport_card()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA");

        var board = Wall(w);

        var record = Assert.Single(w.State.Cards);
        Assert.Equal((Category.Scenery, CardTab.Airports), (record.Section, record.Tab));
        Assert.Equal([Code("LPMA")], record.Codes);
        Assert.Equal([Profile("lpma")], w.State.PackagesOn(record.Id));
        var card = Assert.Single(board.Cards);
        Assert.Equal("LPMA", card.Name);
        Assert.Null(card.ImagePath);
        Assert.Equal(Presence.Found, ProfileRow(board, "lpma").Presence);
    }

    [Fact]
    public void A_profile_joins_the_card_with_its_icao_code()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Place();
        w.Profile("eham", "EHAM");

        w.Place();

        var record = Assert.Single(w.State.Cards);
        Assert.Equal([Profile("eham"), Folder("eham-x")], w.State.PackagesOn(record.Id));
        Assert.Equal([Code("EHAM")], record.Codes);
    }

    [Fact]
    public void A_profile_joins_a_several_card_by_inclusion()
    {
        var w = NewWorld();
        w.Add(Scenery("pair", ["EHAM", "EHRD"]));
        w.Place();
        w.Profile("ehrd", "EHRD");

        w.Place();

        var record = Assert.Single(w.State.Cards);
        Assert.Equal([Code("EHAM"), Code("EHRD")], record.Codes);
        Assert.Equal([Profile("ehrd"), Folder("pair")], w.State.PackagesOn(record.Id));
    }

    [Fact]
    public void A_profile_for_a_built_in_airport_shows_the_built_in_part()
    {
        var w = NewWorld();
        w.SimManaged(Scenery("asobo-airport-eham", fullRecords: [Rec("EHAM", "Schiphol")]));
        w.Profile("eham", "EHAM");

        var card = Assert.Single(Wall(w).Cards);

        Assert.Equal(Profile("eham"), new PackageKey(card.Packages[0].Kind, card.Packages[0].Name));
        Assert.Equal([new PackageName("asobo-airport-eham")], card.BuiltIns.Select(b => b.Name));
    }

    [Fact]
    public void A_profile_never_names_a_card_or_counts_toward_its_state()
    {
        var w = NewWorld();
        w.Add(Scenery("zz-eham", ["EHAM"], title: "Schiphol Pro"));
        w.Profile("aa-eham", "EHAM");
        w.Place();
        w.State.SetOn(new PackageName("zz-eham"), true);

        var card = Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards);

        Assert.Equal(["aa-eham", "zz-eham"], card.Packages.Select(p => p.Name.Value));
        Assert.Equal("Schiphol Pro", card.Name);
        Assert.Equal(CardState.On, card.State);
        var row = card.Packages[0];
        Assert.Equal((PackageKind.GsxProfile, LockReason.GsxProfile, false, false), (row.Kind, row.Lock, row.On, row.CanSwitch));
        Assert.Equal([Code("EHAM")], row.Codes);
    }

    [Fact]
    public void A_card_of_profiles_only_is_named_after_its_airport_when_known()
    {
        var w = NewWorld();
        w.SimManaged(Scenery("asobo-airport-eham", fullRecords: [Rec("EHAM", "Schiphol")]));
        w.Profile("eham", "EHAM");

        var card = Assert.Single(Wall(w).Cards);

        Assert.Equal("EHAM Schiphol", card.Name);
        Assert.Null(card.ImagePath);
        Assert.Equal(CardState.Locked, card.State);
    }

    [Fact]
    public void A_deleted_profile_is_not_found_and_removable()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA");
        w.Place();
        w.WithoutProfile("lpma");

        var changed = w.Place();
        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.False(changed);
        var row = ProfileRow(board, "lpma");
        Assert.Equal((Presence.NotFound, true, "lpma", @"C:\GSX\lpma.ini"), (row.Presence, row.Removable, row.DisplayName, row.Path));
        Assert.Equal([Code("LPMA")], row.Codes);
        Assert.Contains(w.State.Profiles, p => p.Key == new PackageName("lpma"));
    }

    [Fact]
    public void A_missing_gsx_folder_changes_nothing_and_is_in_the_banner()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA");
        w.Place();
        w.WithoutProfile("lpma");
        w.GsxFolderFound = false;

        var changed = w.Place();
        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.False(changed);
        Assert.Equal(Presence.LibraryNotFound, ProfileRow(board, "lpma").Presence);
        Assert.False(ProfileRow(board, "lpma").Removable);
        Assert.True(board.Banner.GsxFolderMissing);
        Assert.Single(w.State.Cards);
        Assert.Single(w.State.Profiles);
    }

    [Fact]
    public void A_found_gsx_folder_is_not_in_the_banner()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA");

        Assert.False(Wall(w).Banner.GsxFolderMissing);
    }

    [Fact]
    public void Move_out_is_refused_on_a_profile()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Profile("eham", "EHAM");
        w.Place();
        var card = Assert.Single(w.State.Cards);
        var editor = new CardEditor(new InMemoryUserStateStore(), TimeProvider.System);

        Assert.Throws<InvalidOperationException>(() => editor.MoveOut(w.State, card.Id, Profile("eham")));

        Assert.Equal([Profile("eham"), Folder("eham-x")], w.State.PackagesOn(card.Id));
    }

    [Fact]
    public void A_card_of_profiles_moved_out_of_scenery_is_named_by_their_icao_codes()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA");
        w.Profile("lppt", "LPPT");
        w.Place();
        var card = w.State.CardOf(Profile("lpma"))!;
        w.State.SetCard(Profile("lppt"), card.Id);
        card.AddCode(Code("LPPT"));
        w.State.RemoveEmptyCards();
        var editor = new CardEditor(new InMemoryUserStateStore(), TimeProvider.System);

        Assert.Equal("LPMA, LPPT", Assert.Single(Wall(w, place: false).Cards).Name);

        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Other);

        var moved = Assert.Single(Wall(w, place: false).Cards);
        Assert.Empty(card.Codes);
        Assert.Equal((Category.Other, "LPMA, LPPT"), (moved.Section, moved.Name));
    }

    [Fact]
    public void A_card_of_profiles_moved_back_into_scenery_gets_its_icao_code_on_airports()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA");
        w.Place();
        var card = w.State.CardOf(Profile("lpma"))!;
        var editor = new CardEditor(new InMemoryUserStateStore(), TimeProvider.System);
        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Other);

        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Scenery);

        Assert.Equal([Code("LPMA")], card.Codes);
        Assert.Equal((Category.Scenery, CardTab.Airports), (card.Section, card.Tab));
    }

    [Fact]
    public void A_profile_without_an_icao_code_is_no_package()
    {
        var w = NewWorld();
        w.ProfileWithoutCode("my-settings");

        var board = Wall(w);

        Assert.Empty(board.Cards);
        Assert.Empty(board.NotShown);
        Assert.Empty(w.State.Profiles);
    }

    [Fact]
    public void An_unknown_three_character_profile_gets_no_card_until_its_icao_code_is_known()
    {
        var w = NewWorld();
        w.Profile("gsx", "GSX");

        var board = Wall(w);

        Assert.Empty(board.Cards);
        Assert.Empty(board.NotShown);
        Assert.Empty(w.State.Cards);
        Assert.Empty(w.State.Profiles);

        w.SimManaged(Scenery("asobo-airport-gsx", fullRecords: [Rec("GSX", "Gsx Field")]));
        var later = Wall(w);

        Assert.Equal(Profile("gsx"), new PackageKey(Assert.Single(later.Cards).Packages[0].Kind, new PackageName("gsx")));
        Assert.Single(w.State.Profiles);
    }

    [Fact]
    public void A_carded_profile_stays_a_package_when_its_three_character_icao_code_stops_being_known()
    {
        var w = NewWorld();
        w.SimManaged(Scenery("asobo-airport-gsx", fullRecords: [Rec("GSX", "Gsx Field")]));
        w.Profile("gsx", "GSX");
        w.Place();
        w.Without("asobo-airport-gsx");

        var changed = w.Place();
        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.False(changed);
        Assert.Single(w.State.Profiles);
        Assert.Equal(Presence.Found, ProfileRow(board, "gsx").Presence);
        Assert.Single(board.Cards);
    }

    [Fact]
    public void A_folder_package_and_a_profile_may_share_a_name()
    {
        var w = NewWorld();
        w.Add(Scenery("LPMA", ["LPMA"]));
        w.Linked("LPMA");
        w.Profile("lpma", "LPMA");
        w.Place();
        w.WithoutProfile("lpma");
        var community = new FakeCommunity();
        community.AddJunction("LPMA", @"C:\Sim\Addons\LPMA");
        var fs = new FakeLinkFileSystem(community);
        var store = new InMemoryUserStateStore();
        var sim = new FakeSim();
        var remover = new PackageRemover(new ChangeApplier(fs, community, store, sim, @"C:\MSFS\Community"), community, sim, store);
        var input = w.Input();
        var card = Assert.Single(w.State.Cards);

        Assert.Equal([Folder("LPMA"), Profile("lpma")], w.State.PackagesOn(card.Id));

        remover.Remove(w.State, BoardBuilder.Build(w.State, input), input.Index, Profile("lpma"));

        Assert.Empty(w.State.Profiles);
        Assert.Contains(w.State.Packages, p => p.Key == new PackageName("LPMA"));
        Assert.Equal([Folder("LPMA")], w.State.PackagesOn(card.Id));
        Assert.Empty(fs.Calls);
        Assert.True(community.Has("LPMA"));
    }

    [Fact]
    public void A_new_scenery_package_and_a_new_profile_for_one_airport_share_one_card()
    {
        var w = NewWorld();
        w.Add(Scenery("lpma-x", ["LPMA"]));
        w.Profile("lpma-gsx", "LPMA");

        w.Place();

        var card = Assert.Single(w.State.Cards);
        Assert.Equal([Profile("lpma-gsx"), Folder("lpma-x")], w.State.PackagesOn(card.Id));
        Assert.Equal([Code("LPMA")], card.Codes);
    }

    [Fact]
    public void Settlement_places_profiles()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA", disabled: true);

        Assert.True(Settlement.Apply(w.State, w.Input(), World.Now));

        var state = w.State.Profiles[new PackageName("lpma")];
        Assert.Equal(Category.Scenery, state.Section);
        Assert.NotNull(state.CardId);
        Assert.Equal((@"C:\GSX\lpma.off", PackageSource.GsxFolder), (state.Memo!.Path, state.Memo.Source));
        Assert.Equal([Code("LPMA")], state.Memo.Codes);
        Assert.Equal("lpma", state.Memo.DisplayName);
        Assert.False(Settlement.Apply(w.State, w.Input(), World.Now));
    }

    [Fact]
    public void An_unplaced_profile_and_a_folder_package_of_one_name_are_two_provisional_cards()
    {
        var w = NewWorld();
        w.Add(Scenery("lpma", ["LPMA"]));
        w.Profile("lpma", "LPMA");

        var board = Wall(w, place: false);

        Assert.Equal(2, board.Cards.Count);
        Assert.Equal([Folder("lpma"), Profile("lpma")], board.Cards.Select(c => c.ProvisionalPackage!.Value).OrderBy(k => k.Kind));
        var profileCard = board.Cards.Single(c => c.ProvisionalPackage == Profile("lpma"));
        Assert.Equal((Category.Scenery, CardTab.Landscapes, "LPMA"), (profileCard.Section, profileCard.Tab, profileCard.Name));
        Assert.Empty(w.State.Cards);
        Assert.Empty(board.NotShown);
    }

    [Fact]
    public void A_disabled_profile_is_shown_as_disabled()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA", disabled: true);
        w.Profile("lppt", "LPPT");

        var board = Wall(w);

        Assert.True(ProfileRow(board, "lpma").Disabled);
        Assert.False(ProfileRow(board, "lppt").Disabled);
    }

    [Fact]
    public void Forgetting_a_profile_touches_only_the_profiles()
    {
        var state = new UserState();
        var name = new PackageName("lpma");
        state.SetOn(name, true);
        state.Pending.Add(name);
        state.Content.OptedIn.Add(name);
        var card = state.NewCard(Category.Scenery, CardTab.Airports, [], World.Now);
        state.SetCard(Profile("lpma"), card.Id);
        state.SetCard(Folder("lpma"), card.Id);

        state.Forget(Profile("lpma"));

        Assert.Empty(state.Profiles);
        Assert.True(state.IsOn(name));
        Assert.Contains(name, state.Pending);
        Assert.Contains(name, state.Content.OptedIn);
        Assert.Equal([Folder("lpma")], state.PackagesOn(card.Id));
        Assert.Single(state.Cards);
    }

    [Fact]
    public void Gsx_files_not_recognised_are_listed()
    {
        var w = NewWorld();
        w.Unrecognised("YALR1.ini");
        w.Profile("gsx", "GSX");
        w.Script("abc.py", "ABC");

        var board = Wall(w);

        Assert.Equal(["abc.py", "gsx.ini", "YALR1.ini"], board.GsxNotRecognised);
    }

    [Fact]
    public void A_known_three_character_profile_is_not_listed_as_not_recognised()
    {
        var w = NewWorld();
        w.SimManaged(Scenery("asobo-airport-gsx", fullRecords: [Rec("GSX", "Gsx Field")]));
        w.Profile("gsx", "GSX");
        w.Script("gsx-x.py", "GSX");

        Assert.Empty(Wall(w).GsxNotRecognised);
    }

    [Fact]
    public void A_script_alone_makes_no_card()
    {
        var w = NewWorld();
        w.Script("lpma-x.py", "LPMA");

        var board = Wall(w);

        Assert.Empty(board.Cards);
        Assert.Empty(w.State.Cards);
        Assert.Empty(board.GsxNotRecognised);
    }

    [Fact]
    public void No_banner_when_gsx_is_not_installed()
    {
        var w = NewWorld();
        w.GsxFolderFound = false;

        Assert.False(Wall(w).Banner.GsxFolderMissing);
    }

    [Fact]
    public void Removing_the_icao_code_from_a_card_does_not_unlink_its_profile()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA");
        w.Place();
        var card = Assert.Single(w.State.Cards);
        card.RemoveCode(Code("LPMA"));

        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.Equal(card.Id, Assert.Single(board.CardsAt(Code("LPMA"))).Id);
        Assert.Empty(CardLinks.Overrides(w.State).Manual);
    }
}
