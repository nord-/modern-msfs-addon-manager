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
}
