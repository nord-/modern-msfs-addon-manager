using AddonManager.App.Session;
using AddonManager.App.ViewModels;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using static AddonManager.App.Tests.ViewModels.Boards;

namespace AddonManager.App.Tests.ViewModels;

public sealed class CardInfoViewModelTests
{
    private static readonly IReadOnlyDictionary<AirportCode, Airport> NoAirports = new Dictionary<AirportCode, Airport>();

    private static Airport AirportNamed(string code, string? name) => new(Code(code), name, null, true, false);

    private static CardPackage WithCodes(string name, params string[] codes) => Package(name) with { Codes = [.. codes.Select(Code)] };

    [Fact]
    public void Airport_names_come_from_the_analysis_then_from_remembered_records()
    {
        var gone = WithCodes("OrbX EHAM 1.3", "EHAM", "EHRD") with
        {
            Presence = Presence.NotFound,
            Airports = [new AirportRecord(Code("EHAM"), "Remembered Schiphol", null), new AirportRecord(Code("EHRD"), " ", null), new AirportRecord(Code("EHRD"), "Rotterdam The Hague", null)],
        };
        var card = Card(1, "Schiphol", codes: ["EHAM", "EHRD", "EHLE"]) with { Packages = [gone] };
        var airports = new Dictionary<AirportCode, Airport>
        {
            [Code("EHAM")] = AirportNamed("EHAM", "Amsterdam Schiphol"),
            [Code("EHRD")] = AirportNamed("EHRD", " "),
        };

        var vm = CardInfoViewModel.Build(card, Board(card), airports);

        Assert.Equal(["EHAM Amsterdam Schiphol", "EHRD Rotterdam The Hague", "EHLE"], vm.Airports.Select(a => a.Title));
        Assert.Null(vm.Airports[2].Name);
    }

    [Fact]
    public void A_code_only_a_package_has_gets_a_row_after_the_cards_codes()
    {
        var card = Card(1, "Schiphol", codes: ["EHRD", "EHAM"]) with { Packages = [WithCodes("a", "LPMA", "EHAM"), WithCodes("b", "EGLL")] };

        var vm = CardInfoViewModel.Build(card, Board(card), NoAirports);

        Assert.Equal(["EHRD", "EHAM", "EGLL", "LPMA"], vm.Airports.Select(a => a.Code));
    }

    [Fact]
    public void Replaces_built_in_and_other_cards_at_the_same_airport()
    {
        var card = Card(1, "Schiphol", codes: ["EHAM", "EHRD"]) with { Packages = [Package("a") with { ReplacesBuiltIn = [Code("EHAM")] }] };
        var other = Card(2, "Other EHAM", codes: ["EHAM"], packages: ["other"]);
        var provisional = Card(null, "loose", codes: [], packages: ["loose"]) with { Packages = [WithCodes("loose", "EHAM")] };

        var vm = CardInfoViewModel.Build(card, Board(card, other, provisional), NoAirports);

        var eham = vm.Airports[0];
        Assert.True(eham.ReplacesBuiltIn);
        Assert.Equal([new OtherCard("loose", CardRef.Of(provisional)), new OtherCard("Other EHAM", CardRef.Of(other))], eham.OtherCards);
        Assert.False(vm.Airports[1].ReplacesBuiltIn);
        Assert.Empty(vm.Airports[1].OtherCards);
    }

    [Fact]
    public void Tags_are_one_line_and_built_ins_are_listed()
    {
        BuiltInPart builtIn = new(new PackageName("asobo-airport-eham"), "Amsterdam Schiphol", Code("EHAM"), null, AircraftRole.None);
        var card = Card(1, "Schiphol", builtIns: [builtIn]) with { Tags = ["Netherlands", "Payware"] };
        var plain = Card(2, "Plain");

        var vm = CardInfoViewModel.Build(card, Board(card, plain), NoAirports);

        Assert.Equal("Netherlands, Payware", vm.Tags);
        Assert.Equal([new BuiltInRow("Amsterdam Schiphol", "asobo-airport-eham (built-in)")], vm.BuiltIns);
        Assert.Null(CardInfoViewModel.Build(plain, Board(card, plain), NoAirports).Tags);
    }
}
