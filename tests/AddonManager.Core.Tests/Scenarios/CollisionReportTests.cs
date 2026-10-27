using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using Xunit.Abstractions;

namespace AddonManager.Core.Tests.Scenarios;

/// <summary>The collision report (ANALYS "Collision report", spec 13): every name or title token that is a known code, against Rickard's reviewed list.</summary>
public class CollisionReportTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<PackageContent> Contents = ContentFixtureLoader.Load("rickard-content");

    private static readonly ContentSettings Settings = new();

    private static readonly ContentAnalysis A = ContentAnalyzer.Analyze(Contents, Settings);

    private static IEnumerable<(string Package, string Code, string Source, string Verdict)> Reviewed()
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reviewed-token-links.tsv"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split('\t'))
            .Select(f => (f[0], f[1].ToUpperInvariant(), f[2], f[3].Trim().ToLowerInvariant()));

    private static IEnumerable<(string Package, string Code)> ReviewedTokenLinks(params string[] verdicts)
        => Reviewed().Where(r => verdicts.Contains(r.Verdict)).Select(r => (r.Package, r.Code));

    private static IEnumerable<string> Report()
    {
        var known = AirportLinker.KnownCodes(Contents, Settings);
        foreach (var content in Contents.Where(c => c.Source != PackageSource.SimManaged).OrderBy(c => c.Name.Value, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var candidate in AirportLinker.TokenCandidates(content, Settings, known))
            {
                yield return $"{content.Name.Value}\t{candidate.Code.Value}\t{candidate.Source}\t{(candidate.Linked ? "linked" : "not linked: " + candidate.Reason)}\t{content.Source}";
            }
        }
    }

    [Fact]
    public void Reviewed_list_is_settled_and_consistent()
    {
        var rows = Reviewed().ToList();
        Assert.DoesNotContain(rows, r => r.Verdict == "?");
        Assert.All(rows, r => Assert.Contains(r.Verdict, new[] { "ok", "wrong", "stop" }));

        var stop = rows.Where(r => r.Verdict == "stop").Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(rows, r => r.Verdict != "stop" && stop.Contains(r.Code));

        // Every stop row's code is a stop word by default.
        Assert.All(stop, code => Assert.True(Settings.IsStopWord(code), code));
    }

    // Rickard's reviewed Token links: the ok and wrong rows of reviewed-token-links.tsv. A change in that file is a decision, not a fix.
    [Fact]
    public void Token_links_match_the_reviewed_list()
    {
        var links = A.AirportLinks
            .Where(l => l.Reason == AirportLinkReason.Token)
            .Select(l => (l.Package.Name.Value, l.Code.Value))
            .Distinct();

        foreach (var row in Report())
        {
            output.WriteLine(row);
        }

        foreach (var row in ReviewedTokenLinks("wrong"))
        {
            output.WriteLine($"known false positive: {row}");
        }

        Assert.Equal(ReviewedTokenLinks("ok", "wrong").Order(), links.Order());
    }
}
