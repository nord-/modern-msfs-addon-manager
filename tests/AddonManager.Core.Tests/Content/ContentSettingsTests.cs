using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Tests.Content;

public class ContentSettingsTests
{
    private static AirportCode Code(string text)
    {
        Assert.True(AirportCode.TryParse(text, out var code));
        return code;
    }

    [Theory]
    [InlineData("navigraph-navdata", true)]
    [InlineData("Navigraph-NavData-Base", true)]
    [InlineData("microsoft-nordics2-point-of-interest", true)]
    [InlineData("neofly-neofly-cargo", true)]
    [InlineData("microsoft-airport-voloport", true)]
    [InlineData("microsoft-airport-voloport-x", false)]
    [InlineData("brickbybrickdesign-airport-eham-amsterdam", false)]
    public void Default_exclusions(string name, bool excluded)
        => Assert.Equal(excluded, new ContentSettings().MatchesExclusion(new(name)));

    [Fact]
    public void Stop_words_ignore_case()
    {
        var s = new ContentSettings();
        Assert.True(s.IsStopWord("pack"));
        Assert.False(s.IsStopWord("EHAM"));
    }

    [Fact]
    public void Added_and_removed_entries_adjust_the_defaults()
    {
        var s = new ContentSettings();
        s.AddExclusionPattern("foo-*");
        s.RemoveExclusionPattern("neofly-*");
        Assert.True(s.MatchesExclusion(new("foo-bar")));
        Assert.False(s.MatchesExclusion(new("neofly-x")));

        s.AddExclusionPattern("NEOFLY-*");
        s.RemoveExclusionPattern("foo-*");
        Assert.True(s.MatchesExclusion(new("neofly-x")));
        Assert.False(s.MatchesExclusion(new("foo-bar")));
        Assert.Empty(s.AddedExclusionPatterns);
        Assert.Empty(s.RemovedExclusionPatterns);
    }

    [Fact]
    public void Pattern_changes_after_a_first_match_take_effect()
    {
        var s = new ContentSettings();
        Assert.False(s.MatchesExclusion(new("foo-bar")));
        s.AddExclusionPattern("foo-*");
        Assert.True(s.MatchesExclusion(new("foo-bar")));
        s.RemoveExclusionPattern("foo-*");
        Assert.False(s.MatchesExclusion(new("foo-bar")));
    }

    [Fact]
    public void Manual_link_undoes_a_removal_and_vice_versa()
    {
        var s = new ContentSettings();
        var p = new PackageName("p");
        s.RemoveLink(p, Code("LOWI"));
        Assert.Contains(Code("LOWI"), s.RemovedAirportLinks[p]);

        s.LinkManually(p, Code("lowi"));
        Assert.Contains(Code("LOWI"), s.ManualAirportLinks[p]);
        Assert.DoesNotContain(p, s.RemovedAirportLinks.Keys);

        s.RemoveLink(p, Code("LOWI"));
        Assert.Contains(Code("LOWI"), s.RemovedAirportLinks[p]);
        Assert.DoesNotContain(p, s.ManualAirportLinks.Keys);
    }

    [Fact]
    public void Clone_is_deep_and_keeps_the_comparer()
    {
        var s = new ContentSettings { AirportThreshold = 3 };
        s.AddStopWord("x1");
        s.OptedIn.Add(new("p"));
        s.LinkManually(new("p"), Code("LOWI"));

        var c = s.Clone();
        s.AddStopWord("later");
        s.OptedIn.Clear();
        s.RemoveLink(new("p"), Code("LOWI"));

        Assert.Equal(3, c.AirportThreshold);
        Assert.True(c.IsStopWord("X1"));
        Assert.False(c.IsStopWord("later"));
        Assert.Single(c.OptedIn);
        Assert.Single(c.ManualAirportLinks[new("p")]);
        c.AddStopWord("X1");
        Assert.Single(c.AddedStopWords);
    }

    [Fact]
    public void Forgetting_a_package_drops_its_content_settings()
    {
        var s = new UserState();
        s.Content.OptedIn.Add(new("p"));
        s.Content.LinkManually(new("p"), Code("LOWI"));
        s.Content.RemoveLink(new("p"), Code("EHAM"));
        s.Forget(new("p"));
        Assert.Empty(s.Content.OptedIn);
        Assert.Empty(s.Content.ManualAirportLinks);
        Assert.Empty(s.Content.RemovedAirportLinks);
    }

    [Fact]
    public void Fake_store_keeps_content_settings()
    {
        var fake = new Fakes.InMemoryUserStateStore();
        var s = new UserState();
        s.Content.OptedIn.Add(new("x"));
        fake.Save(s);
        s.Content.OptedIn.Clear();
        Assert.Single(fake.Load().Content.OptedIn);
    }
}
