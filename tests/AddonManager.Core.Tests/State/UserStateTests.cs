using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.State;

public class UserStateTests
{
    [Fact]
    public void SetOn_keeps_the_other_fields_and_ignores_case()
    {
        var state = new UserState();
        state.Packages[new("X")] = new PackageState(On: false, ActiveCopyPath: @"C:\a\x");

        state.SetOn(new("x"), true);

        Assert.True(state.IsOn(new("X")));
        Assert.Equal(new PackageState(true, @"C:\a\x"), state.Packages[new("x")]);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static PackageKey Key(string name) => PackageKey.Folder(new PackageName(name));

    [Fact]
    public void Forget_removes_a_card_left_without_packages()
    {
        var state = new UserState();
        var two = state.NewCard(Category.Scenery, CardTab.Airports, [], Now);
        var one = state.NewCard(Category.Scenery, CardTab.Airports, [], Now);
        state.SetCard(Key("a"), two.Id);
        state.SetCard(Key("b"), two.Id);
        state.SetCard(Key("c"), one.Id);

        state.Forget(Key("A"));
        state.Forget(Key("c"));

        Assert.Equal([Key("b")], state.PackagesOn(two.Id));
        Assert.Same(two, Assert.Single(state.Cards));
        Assert.Null(state.CardOf(Key("c")));
        Assert.Same(two, state.CardOf(Key("B")));
        Assert.Same(two, state.FindCard(two.Id));
        Assert.Null(state.FindCard(one.Id));
    }

    [Fact]
    public void New_cards_get_increasing_ids_that_are_never_reused()
    {
        var state = new UserState();

        var first = state.NewCard(Category.Other, CardTab.Other, [], Now);
        var second = state.NewCard(Category.Other, CardTab.Other, [], Now);
        state.Cards.Remove(second);
        var third = state.NewCard(Category.Other, CardTab.Other, [], Now);

        Assert.Equal([1, 2, 3], [first.Id, second.Id, third.Id]);
        Assert.Equal(4, state.NextCardId);
        Assert.Equal(Now, first.Created);
    }

    [Fact]
    public void Only_a_scenery_card_gets_icao_codes_and_they_are_sorted()
    {
        var state = new UserState();
        AirportCode.TryParse("EHRD", out var ehrd);
        AirportCode.TryParse("EHAM", out var eham);

        var scenery = state.NewCard(Category.Scenery, CardTab.Airports, [ehrd, eham, ehrd], Now);
        var aircraft = state.NewCard(Category.Aircraft, CardTab.Other, [eham], Now);

        Assert.Equal([eham, ehrd], scenery.Codes);
        Assert.Empty(aircraft.Codes);
    }

    [Fact]
    public void SetCard_adds_an_off_state_for_a_package_without_one_and_keeps_the_choice_of_one_with()
    {
        var state = new UserState();
        var card = state.NewCard(Category.Other, CardTab.Other, [], Now);
        state.SetOn(new("b"), true);

        state.SetCard(Key("a"), card.Id);
        state.SetCard(Key("b"), card.Id);

        Assert.Equal(new PackageState(false) { CardId = card.Id }, state.Packages[new("a")]);
        Assert.True(state.IsOn(new("b")));
        Assert.Equal([Key("a"), Key("b")], state.PackagesOn(card.Id));
    }

    [Fact]
    public void RemoveEmptyCards_keeps_only_cards_with_a_package()
    {
        var state = new UserState();
        var used = state.NewCard(Category.Other, CardTab.Other, [], Now);
        state.NewCard(Category.Other, CardTab.Other, [], Now);
        state.SetCard(Key("a"), used.Id);

        state.RemoveEmptyCards();

        Assert.Same(used, Assert.Single(state.Cards));
    }

    [Fact]
    public void The_in_memory_store_returns_independent_copies()
    {
        var store = new Fakes.InMemoryUserStateStore();
        var state = new UserState();
        var card = state.NewCard(Category.Scenery, CardTab.Airports, [], Now);
        state.SetCard(Key("a"), card.Id);
        store.Save(state);

        var loaded = store.Load();
        loaded.Cards[0].Name = "changed";
        loaded.Cards[0].AddCode(Assert.IsType<AirportCode>(AirportCode.TryParse("EHAM", out var eham) ? eham : null));
        loaded.SetCard(Key("b"), card.Id);

        var again = store.Load();
        Assert.Null(again.Cards[0].Name);
        Assert.Empty(again.Cards[0].Codes);
        Assert.Equal([Key("a")], again.PackagesOn(card.Id));
        Assert.Equal(2, again.NextCardId);
    }

    [Fact]
    public void Forget_removes_the_choice_and_the_pending_mark()
    {
        var state = new UserState();
        state.SetOn(new("x"), true);
        state.Pending.Add(new("x"));

        state.Forget(Key("X"));

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
