using AddonManager.App.ViewModels;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;
using static AddonManager.App.Tests.ViewModels.Boards;

namespace AddonManager.App.Tests.ViewModels;

public sealed class CardViewModelTests
{
    [Fact]
    public void A_single_code_card_shows_its_code_and_name()
    {
        var vm = new CardViewModel(Card(1, "Arlanda", codes: ["ESSA"]), null, true);

        Assert.Equal("ESSA", vm.Code);
        Assert.Null(vm.CodesLine);
        Assert.Equal("Arlanda", vm.Name);
        Assert.Null(vm.Subtitle);
    }

    [Fact]
    public void A_several_card_shows_several_and_its_codes()
    {
        var vm = new CardViewModel(Card(1, "Stockholm", codes: ["ESSA", "ESSB"]), "Pkg", true);

        Assert.Equal("(several)", vm.Code);
        Assert.Equal("ESSA, ESSB", vm.CodesLine);
        Assert.Equal("Pkg", vm.Subtitle);
    }

    [Fact]
    public void Automation_name_is_the_code_and_name_or_just_the_name()
    {
        Assert.Equal("LOWI Innsbruck", new CardViewModel(Card(1, "Innsbruck", codes: ["LOWI"]), null, true).ToString());
        Assert.Equal("(several) Stockholm", new CardViewModel(Card(1, "Stockholm", codes: ["ESSA", "ESSB"]), null, true).ToString());
        Assert.Equal("Nothing", new CardViewModel(Card(1, "Nothing"), null, true).ToString());
    }

    [Fact]
    public void A_card_without_codes_has_no_code() => Assert.Null(new CardViewModel(Card(1, "Nothing"), null, true).Code);

    [Fact]
    public void An_aircraft_card_shows_its_type_designators()
    {
        var aircraft = new CardViewModel(Card(1, "A320", Category.Aircraft, CardTab.Aircraft, designators: ["A320", "A20N"]), null, false);
        var scenery = new CardViewModel(Card(2, "Arlanda", codes: ["ESSA"]), null, true);

        Assert.Equal("A320, A20N", aircraft.TypeDesignators);
        Assert.Null(scenery.TypeDesignators);
    }

    [Fact]
    public void The_gsx_mark_shows_only_when_asked()
    {
        var card = Card(1, "Arlanda", codes: ["ESSA"]) with { Gsx = GsxMark.Installed };

        Assert.Equal("GSX", new CardViewModel(card, null, showGsx: true).Gsx);
        Assert.Null(new CardViewModel(card with { Tab = CardTab.Landscapes }, null, showGsx: false).Gsx);
        Assert.Equal("GSX missing", CardText.GsxMark(GsxMark.Missing));
        Assert.Equal("GSX ×2", CardText.GsxMark(GsxMark.TwoProfiles));
        Assert.Null(CardText.GsxMark(GsxMark.None));
    }

    [Fact]
    public void Warnings_and_labels_use_the_fixed_copy()
    {
        var part = new BuiltInPart(new PackageName("official"), "Official", null, null, AircraftRole.None);
        var warnings = new List<CardWarning>
        {
            new(WarningKind.TwoGsxProfiles, new PackageName("essa-a.ini")) { Code = Code("ESSA") },
            new(WarningKind.NotFound, new PackageName("gone")),
            new(WarningKind.DuplicatePackage, new PackageName("dup")),
            new(WarningKind.DuplicateCopy, new PackageName("copy")),
            new(WarningKind.LiveryWithoutAircraft, new PackageName("livery")),
        };
        var vm = new CardViewModel(Card(1, "Arlanda", codes: ["ESSA"], pending: true, builtIns: [part], warnings: warnings), null, true);

        Assert.Equal(
            ["Two GSX profiles: ESSA", "Not found: gone", "Duplicate Package: dup", "Duplicate copy: copy", "Livery without aircraft: livery"],
            vm.Warnings);
        Assert.Equal(["Built-in", "Next sim start"], vm.Labels);
    }

    [Fact]
    public void Warnings_summary_is_the_warning_itself_or_a_count()
    {
        CardViewModel Of(params string[] packages) => new(
            Card(1, "Arlanda", codes: ["ESSA"], warnings: [.. packages.Select(p => new CardWarning(WarningKind.NotFound, new PackageName(p)))]), null, true);

        Assert.Equal("", Of().WarningsSummary);
        Assert.Equal("Not found: a", Of("a").WarningsSummary);
        Assert.Equal("3 warnings", Of("a", "b", "c").WarningsSummary);
    }

    [Fact]
    public void Package_count_is_singular_for_one()
    {
        Assert.Equal("1 package", CardText.PackageCount(1));
        Assert.Equal("2 packages", CardText.PackageCount(2));
        Assert.Equal("1 package", new CardViewModel(Card(1, "A"), null, false).PackageCount);
        Assert.Equal("2 packages", new CardViewModel(Card(1, "A", packages: ["a", "b"]), null, false).PackageCount);
    }

    [Fact]
    public void Package_count_includes_a_disabled_profile()
    {
        var card = Card(1, "A", packages: ["a", "b"]);
        card = card with { Packages = [card.Packages[0], card.Packages[1] with { Disabled = true }] };

        Assert.Equal("2 packages", new CardViewModel(card, null, false).PackageCount);
    }

    [Fact]
    public void Update_refreshes_every_property_and_raises_changes()
    {
        var vm = new CardViewModel(Card(1, "Arlanda", codes: ["ESSA"], image: "a.png"), null, true);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Update(Card(1, "Renamed", codes: ["ESSA"], image: "b.png", state: CardState.Off), "sub", true);

        Assert.Equal("Renamed", vm.Name);
        Assert.Equal("sub", vm.Subtitle);
        Assert.Equal("b.png", vm.ImagePath);
        Assert.Equal(CardState.Off, vm.Light);
        Assert.Contains(nameof(CardViewModel.Name), changed);
        Assert.Contains(nameof(CardViewModel.Light), changed);
        Assert.DoesNotContain(nameof(CardViewModel.Code), changed);
    }

    [Fact]
    public void Only_stored_cards_can_be_dragged_and_they_carry_their_section_and_codes()
    {
        var stored = new CardViewModel(Card(1, "Arlanda", codes: ["ESSA"]), null, true);
        var provisional = new CardViewModel(Card(null, "pkg", Category.Aircraft, CardTab.Aircraft), null, false);

        Assert.True(stored.CanDrag);
        Assert.True(stored.HasCodes);
        Assert.Equal(Category.Scenery, stored.Section);
        Assert.False(provisional.CanDrag);
        Assert.False(provisional.HasCodes);
        Assert.Equal(Category.Aircraft, provisional.Section);
    }
}
