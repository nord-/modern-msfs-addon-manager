using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Cards;

public class CardTitlesTests
{
    [Fact]
    public void Cards_with_the_same_title_show_their_package_name()
    {
        Card[] cards =
        [
            MakeCard("My Company Aerial", "MyCo-Aerial-North"),
            MakeCard("My Company Aerial", "MyCo-Aerial-South"),
        ];

        Assert.Equal(["MyCo-Aerial-North", "MyCo-Aerial-South"], CardTitles.Subtitles(cards));
    }

    [Fact]
    public void A_unique_title_has_no_subtitle()
    {
        Card[] cards =
        [
            MakeCard("My Company Aerial", "MyCo-Aerial-North"),
            MakeCard("Another Aerial", "Other-Aerial"),
        ];

        Assert.Equal([null, null], CardTitles.Subtitles(cards));
    }

    [Fact]
    public void Same_name_with_other_icao_codes_is_no_clash()
    {
        Card[] cards =
        [
            MakeCard("Schiphol", "Schiphol-A", "EHAM"),
            MakeCard("Schiphol", "Schiphol-B", "EHRD"),
        ];

        Assert.Equal([null, null], CardTitles.Subtitles(cards));
    }

    [Fact]
    public void Name_case_does_not_matter()
    {
        Card[] cards =
        [
            MakeCard("Windy Things Library", "Windy-Library"),
            MakeCard("windy things library", "Windy-Library-Copy"),
        ];

        Assert.Equal(["Windy-Library", "Windy-Library-Copy"], CardTitles.Subtitles(cards));
    }

    private static Card MakeCard(string name, string package, params string[] codes)
    {
        var airports = codes.Select(text => AirportCode.TryParse(text, out var code) ? code : default).ToList();
        var cardPackage = new CardPackage(
            PackageKind.Folder,
            new PackageName(package),
            package,
            @"C:\Sim\Addons\" + package,
            null,
            null,
            PackageSource.Library,
            Presence.Found,
            LockReason.None,
            null,
            AircraftRole.None,
            [],
            airports,
            [],
            false,
            true,
            null,
            [],
            true);

        return new Card(
            null,
            null,
            Category.Scenery,
            CardTab.Airports,
            name,
            null,
            airports,
            [],
            null,
            [cardPackage],
            [],
            [],
            CardState.Off,
            false,
            [],
            []);
    }
}
