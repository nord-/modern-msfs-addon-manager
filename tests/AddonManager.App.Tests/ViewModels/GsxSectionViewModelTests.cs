using AddonManager.App.Session;
using AddonManager.App.ViewModels;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;
using static AddonManager.App.Tests.ViewModels.Boards;

namespace AddonManager.App.Tests.ViewModels;

public sealed class GsxSectionViewModelTests
{
    private static readonly IReadOnlyDictionary<AirportCode, string> NoErrors = new Dictionary<AirportCode, string>();

    private static CardPackage Profile(string name, string code, Presence presence = Presence.Found, bool disabled = false, bool removable = false)
        => Package(name) with { Kind = PackageKind.GsxProfile, Codes = [Code(code)], Presence = presence, Removable = removable, Disabled = disabled };

    private static GsxAirport Gsx(string code, string[] active, params ShippedProfile[] shipped)
        => new(Code(code), GsxMark.None, [.. active.Select(a => new PackageName(a))], [], [], shipped, GsxStatus.SearchLink(Code(code)));

    private static ShippedProfile Shipped(string code, string file, bool inGsxFolder)
        => new(Code(code), new PackageName("scenery"), $@"C:\Lib\scenery\gsx\{file}.ini", [], inGsxFolder);

    private static Card CardWith(long id, string name, string[] codes, CardPackage[] packages, params GsxAirport[] gsx)
        => Card(id, name, codes: codes) with { Packages = packages, GsxAirports = gsx };

    private static List<GsxProfileRow> Profiles(GsxSectionViewModel vm) => [.. vm.Rows.OfType<GsxProfileRow>()];

    [Fact]
    public void Two_active_profiles_mark_each_row_also_on_another_card()
    {
        var other = CardWith(2, "Other EHAM", ["EHAM"], [Profile("EHAM-abc", "EHAM")]);
        var card = CardWith(1, "Schiphol", ["EHAM"], [Package("scenery"), Profile("EHAM-xyz", "EHAM")], Gsx("EHAM", ["EHAM-abc", "EHAM-xyz"]));

        var rows = Profiles(GsxSectionViewModel.Build(card, Board(card, other), NoErrors));

        Assert.Equal(["EHAM-xyz", "EHAM-abc"], rows.Select(r => r.Name));
        Assert.All(rows, r => Assert.Equal("Two GSX profiles for EHAM", r.TwoProfilesTip));
        Assert.Equal(new PackageKey(PackageKind.GsxProfile, new PackageName("EHAM-xyz")), rows[0].Key);
        Assert.Null(rows[0].OnCard);
        Assert.Null(rows[1].Key);
        Assert.Equal(new OtherCard("Other EHAM", CardRef.Of(other)), rows[1].OnCard);
        Assert.Equal("EHAM-abc", ((IRemovableRow)rows[1]).FolderName);
    }

    [Fact]
    public void Disabled_and_not_found_profiles_are_dimmed_and_not_found_keeps_remove()
    {
        var card = CardWith(
            1,
            "Schiphol",
            ["EHAM"],
            [Profile("EHAM-on", "EHAM"), Profile("eham-old", "EHAM", disabled: true), Profile("EHAM-gone", "EHAM", Presence.NotFound, removable: true)],
            Gsx("EHAM", ["EHAM-on"]));

        var rows = Profiles(GsxSectionViewModel.Build(card, Board(card), NoErrors));

        Assert.Equal(
            [("EHAM-on", false, null, false), ("eham-old", true, null, false), ("EHAM-gone", true, "Not found", true)],
            rows.Select(r => (r.Name, r.Dimmed, r.Label, r.CanRemove)));
        Assert.All(rows, r => Assert.Null(r.TwoProfilesTip));
    }

    [Fact]
    public void Profiles_that_are_not_found_for_other_reasons_are_dimmed_without_label()
    {
        var card = CardWith(
            1,
            "Schiphol",
            ["EHAM"],
            [Profile("EHAM-a", "EHAM", Presence.Unreadable, removable: true), Profile("EHAM-b", "EHAM", Presence.LibraryNotFound, removable: true)]);

        var rows = Profiles(GsxSectionViewModel.Build(card, Board(card), NoErrors));

        Assert.Equal([("EHAM-a", true, null, false), ("EHAM-b", true, null, false)], rows.Select(r => (r.Name, r.Dimmed, r.Label, r.CanRemove)));
    }

    [Fact]
    public void A_shipped_profile_is_hidden_only_when_its_installed_copy_has_a_row()
    {
        var card = CardWith(
            1,
            "Schiphol",
            ["EHAM"],
            [Profile("eham-fsdt", "EHAM")],
            Gsx("EHAM", ["eham-fsdt"], Shipped("EHAM", "EHAM-FSDT", inGsxFolder: true), Shipped("EHAM", "EHAM-gone", inGsxFolder: true), Shipped("EHAM", "EHAM-7g8", inGsxFolder: false)));

        var rows = Profiles(GsxSectionViewModel.Build(card, Board(card), NoErrors));

        Assert.Equal(
            [("eham-fsdt", false, null), ("EHAM-gone", true, "shipped with scenery"), ("EHAM-7g8", true, "shipped with scenery")],
            rows.Select(r => (r.Name, r.Dimmed, r.Label)));
        Assert.All(rows.Skip(1), r => Assert.Null(r.Key));
    }

    [Fact]
    public void A_code_without_an_active_profile_gets_a_search_row_with_its_error()
    {
        var card = CardWith(1, "Schiphol", ["EHAM", "EHRD"], [Profile("EHAM-x", "EHAM")], Gsx("EHAM", ["EHAM-x"]), Gsx("EHRD", []));
        var errors = new Dictionary<AirportCode, string> { [Code("EHRD")] = "No browser" };

        var vm = GsxSectionViewModel.Build(card, Board(card), errors);

        Assert.True(vm.IsVisible);
        Assert.IsType<GsxProfileRow>(vm.Rows[0]);
        var search = Assert.IsType<GsxSearchRow>(vm.Rows[1]);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(new GsxSearchRow(Code("EHRD"), "https://flightsim.to/others/gsx-pro/search/ehrd", "Search flightsim.to for EHRD GSX profiles", "No browser"), search);
    }

    [Fact]
    public void Profiles_of_codes_the_card_lacks_come_last()
    {
        var elsewhere = CardWith(2, "Madeira", ["LPMA"], [Profile("lpma-b", "LPMA")], Gsx("LPMA", ["lpma-a", "lpma-b"]));
        var card = CardWith(1, "Schiphol", ["EHAM"], [Profile("lpma-a", "LPMA"), Profile("EHAM-x", "EHAM")], Gsx("EHAM", ["EHAM-x"]));

        var rows = Profiles(GsxSectionViewModel.Build(card, Board(card, elsewhere), NoErrors));

        Assert.Equal([("EHAM-x", null), ("lpma-a", "Two GSX profiles for LPMA")], rows.Select(r => (r.Name, r.TwoProfilesTip)));
    }

    [Fact]
    public void Without_gsx_facts_only_the_cards_profiles_show()
    {
        var card = CardWith(1, "Schiphol", ["EHAM"], [Package("scenery"), Profile("EHAM-x", "EHAM"), Profile("EHAM-y", "EHAM")]);
        var empty = CardWith(2, "Plain", ["EHRD"], [Package("plain")]);

        var vm = GsxSectionViewModel.Build(card, Board(card, empty), NoErrors);

        Assert.Equal([("EHAM-x", null), ("EHAM-y", null)], vm.Rows.Cast<GsxProfileRow>().Select(r => (r.Name, r.TwoProfilesTip)));
        Assert.False(GsxSectionViewModel.Build(empty, Board(card, empty), NoErrors).IsVisible);
    }
}
