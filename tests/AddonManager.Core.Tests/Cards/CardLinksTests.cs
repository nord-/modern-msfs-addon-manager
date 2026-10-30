using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class CardLinksTests
{
    private static World NewWorld()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        return w;
    }

    private static (string, AirportLinkReason)[] LinksOf(BoardInput input, string package)
        => input.Analysis.AirportLinks
            .Where(l => l.Package.Name == new PackageName(package))
            .Select(l => (l.Code.Value, l.Reason))
            .ToArray();

    private static PackageMemo Memo(params AirportRecord[] airports)
        => new(@"C:\Sim\Addons\gone", PackageSource.Library, "Gone", "SCENERY", null, null, BglPlacement.Places, [], [], AircraftRole.None, [.. airports]);

    [Fact]
    public void Packages_on_a_scenery_card_are_linked_to_every_icao_code_of_the_card()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-a", ["EHAM"]));
        var card = w.Group(Category.Scenery, CardTab.Airports, "eham-a");
        card.AddCode(Code("EHAM"));
        card.AddCode(Code("EHRD"));

        var input = w.Input().Reanalyze(w.State);

        Assert.Equal([("EHAM", AirportLinkReason.Manual), ("EHRD", AirportLinkReason.Manual)], LinksOf(input, "eham-a"));
    }

    [Fact]
    public void A_removed_icao_code_unlinks_every_package_on_the_card()
    {
        var w = NewWorld();
        w.Add(Scenery("x", ["EHAM", "EHRD"]));
        w.Add(Scenery("y", ["EHRD"]));
        var card = w.Group(Category.Scenery, CardTab.Airports, "x", "y");
        card.RemoveCode(Code("EHRD"));

        var input = w.Input().Reanalyze(w.State);

        Assert.Equal([("EHAM", AirportLinkReason.Record)], LinksOf(input, "x"));
        Assert.Empty(LinksOf(input, "y"));
    }

    [Fact]
    public void Packages_on_aircraft_and_other_cards_get_no_overrides()
    {
        var w = NewWorld();
        w.Add(Aircraft("plane", [("P", null)]));
        w.Add(Build("tool", "MISC"));
        var aircraft = w.Group(Category.Aircraft, CardTab.Aircraft, "plane");
        var other = w.Group(Category.Other, CardTab.Other, "tool");

        // The invariant keeps ICAO codes off these cards; a hand-edited file might not.
        aircraft.Codes = [Code("EHAM")];
        other.RemovedCodes = [Code("EHRD")];

        var overrides = CardLinks.Overrides(w.State);

        Assert.Empty(overrides.Manual);
        Assert.Empty(overrides.Removed);
    }

    [Fact]
    public void Overrides_are_keyed_by_package_name_and_hold_the_card_codes()
    {
        var w = NewWorld();
        var card = w.Group(Category.Scenery, CardTab.Airports, "b", "a");
        card.AddCode(Code("EHAM"));
        card.RemoveCode(Code("EHRD"));
        w.Group(Category.Scenery, CardTab.Landscapes, "no-codes");

        var overrides = CardLinks.Overrides(w.State);

        Assert.Equal([new PackageName("a"), new PackageName("b")], overrides.Manual.Keys.OrderBy(k => k.Value).ToArray());
        Assert.Equal([Code("EHAM")], overrides.Manual[new PackageName("a")].ToArray());
        Assert.Equal([Code("EHRD")], overrides.Removed[new PackageName("b")].ToArray());
        Assert.DoesNotContain(new PackageName("no-codes"), overrides.Manual.Keys);
        Assert.DoesNotContain(new PackageName("no-codes"), overrides.Removed.Keys);
    }

    [Fact]
    public void Remembered_records_come_from_the_memo_of_carded_packages_without_content()
    {
        var w = NewWorld();
        w.Add(Scenery("present", ["EHAM"]));
        w.Group(Category.Scenery, CardTab.Airports, "gone", "present");
        w.State.SetCard(PackageKey.Folder(new PackageName("loose")), null);
        w.State.Packages[new PackageName("gone")] = w.State.Packages[new PackageName("gone")] with { Memo = Memo(Rec("YQ05", "Mount Mulligan", -16.8, 144.8), Rec("EHAM", "Old")) };
        w.State.Packages[new PackageName("present")] = w.State.Packages[new PackageName("present")] with { Memo = Memo(Rec("EHAM", "Schiphol")) };
        w.State.Packages[new PackageName("loose")] = w.State.Packages[new PackageName("loose")] with { Memo = Memo(Rec("ESSA", "Arlanda")) };

        var remembered = CardLinks.Remembered(w.State, w.Input().Contents);

        Assert.Equal([Rec("EHAM", "Old"), Rec("YQ05", "Mount Mulligan", -16.8, 144.8)], remembered);
    }
}
