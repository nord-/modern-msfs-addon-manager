using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class CardSwitchTests
{
    private static PackageName Name(string name) => new(name);

    private static Board Wall(World w, bool place = true)
    {
        if (place)
        {
            w.Place();
        }

        return BoardBuilder.Build(w.State, w.Input());
    }

    /// <summary>One card with a switchable Package, a locked Package and a Not found Package.</summary>
    private static Card MixedCard()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        w.Add(Scenery("pkg-a", ["EHAM"]));
        w.Add(Scenery("pkg-m", ["EHAM"]));
        w.Fixed(Scenery("pkg-fx", ["EHAM"]));
        w.Group(Category.Scenery, CardTab.Landscapes, "pkg-a", "pkg-m", "pkg-fx");
        Wall(w);

        w.Without("pkg-m");
        return Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards);
    }

    [Fact]
    public void Switch_all_sets_every_switchable_package_and_skips_the_rest()
    {
        var card = MixedCard();

        var plan = CardSwitch.All(card, true);

        Assert.Equal([new KeyValuePair<PackageName, bool>(Name("pkg-a"), true)], plan.Choices);
        Assert.Equal(["pkg-fx", "pkg-m"], plan.Skipped.Select(p => p.Name.Value));
        Assert.NotNull(plan.Skipped.Single(p => p.Name.Value == "pkg-fx").Holder);
    }

    [Fact]
    public void Switch_one_rejects_a_package_that_cannot_be_switched()
    {
        var card = MixedCard();

        var rejected = CardSwitch.One(card, PackageKey.Folder(Name("pkg-fx")), true);
        var accepted = CardSwitch.One(card, PackageKey.Folder(Name("pkg-a")), false);

        Assert.Empty(rejected.Choices);
        Assert.Equal("pkg-fx", Assert.Single(rejected.Skipped).Name.Value);
        Assert.Equal([new KeyValuePair<PackageName, bool>(Name("pkg-a"), false)], accepted.Choices);
        Assert.Empty(accepted.Skipped);
    }

    [Fact]
    public void A_provisional_card_switches_its_one_package()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        w.Add(Scenery("pkg-a", ["EHAM"]));
        var card = Assert.Single(Wall(w, place: false).Cards);

        Assert.Equal(PackageKey.Folder(Name("pkg-a")), card.ProvisionalPackage);
        Assert.Equal([new KeyValuePair<PackageName, bool>(Name("pkg-a"), true)], CardSwitch.All(card, true).Choices);
        Assert.Equal([new KeyValuePair<PackageName, bool>(Name("pkg-a"), true)], CardSwitch.One(card, PackageKey.Folder(Name("pkg-a")), true).Choices);
    }

    /// <summary>One card with the Packages <c>pkg-a</c> and <c>pkg-b</c>; <c>pkg-b</c> is Not found when <paramref name="bGone"/>.</summary>
    private static Card PairCard(bool aOn, bool bOn, bool bGone)
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        w.Add(Scenery("pkg-a", ["EHAM"]));
        w.Add(Scenery("pkg-b", ["EHAM"]));
        w.Group(Category.Scenery, CardTab.Landscapes, "pkg-a", "pkg-b");
        Wall(w);
        w.State.Packages[Name("pkg-a")] = w.State.Packages[Name("pkg-a")] with { On = aOn };
        w.State.Packages[Name("pkg-b")] = w.State.Packages[Name("pkg-b")] with { On = bOn };
        if (bGone)
        {
            w.Without("pkg-b");
        }

        return Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards);
    }

    [Fact]
    public void A_card_mixed_only_by_a_package_that_cannot_be_switched_shows_on()
    {
        var card = PairCard(aOn: true, bOn: false, bGone: true);

        Assert.True(CardSwitch.IsOn(card));
        var plan = CardSwitch.Toggle(card);
        Assert.Equal([new KeyValuePair<PackageName, bool>(Name("pkg-a"), false)], plan.Choices);
    }

    [Fact]
    public void A_mixed_card_of_switchable_packages_shows_off_and_turns_on()
    {
        var card = PairCard(aOn: true, bOn: false, bGone: false);

        Assert.False(CardSwitch.IsOn(card));
        var plan = CardSwitch.Toggle(card);
        Assert.Equal([new KeyValuePair<PackageName, bool>(Name("pkg-a"), true), new KeyValuePair<PackageName, bool>(Name("pkg-b"), true)], plan.Choices);
    }

    [Fact]
    public void A_card_without_switchable_packages_has_no_switch()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        w.Add(Scenery("pkg-a", ["EHAM"]));
        w.Group(Category.Scenery, CardTab.Landscapes, "pkg-a");
        Wall(w);
        w.Without("pkg-a");
        var card = Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards);

        Assert.False(CardSwitch.HasSwitchable(card));
        Assert.False(CardSwitch.IsOn(card));
    }
}
