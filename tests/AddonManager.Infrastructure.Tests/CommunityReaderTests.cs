using AddonManager.Core.Community;

namespace AddonManager.Infrastructure.Tests;

public sealed class CommunityReaderTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly string community;

    public CommunityReaderTests() => community = Directory.CreateDirectory(Path.Combine(temp.Root, "community")).FullName;

    public void Dispose() => temp.Dispose();

    private string C(string name) => Path.Combine(community, name);

    [Fact]
    public void Reads_each_kind_of_entry()
    {
        var pkg = temp.MakePackage(@"lib\pkg");
        new JunctionFileSystem().CreateJunction(C("pkg"), pkg);
        temp.MakePackage(@"community\fixed");
        Directory.CreateDirectory(C("ignored"));
        File.WriteAllText(C("UNINSTALL.exe"), "");

        var entries = new CommunityReader(community).Read().ToDictionary(e => e.Name);

        Assert.Equal(new CommunityEntryInfo("pkg", EntryKind.Junction, pkg, true, true), entries["pkg"]);
        Assert.Equal(EntryKind.Directory, entries["fixed"].Kind);
        Assert.True(entries["fixed"].HasManifest);
        Assert.Equal(C("fixed"), entries["fixed"].Path);
        Assert.False(entries["ignored"].HasManifest);
        Assert.Equal(EntryKind.File, entries["UNINSTALL.exe"].Kind);
    }

    [Fact]
    public void A_link_to_a_missing_target_reports_TargetExists_false()
    {
        var pkg = temp.MakePackage(@"lib\gone");
        new JunctionFileSystem().CreateJunction(C("gone"), pkg);
        Directory.Delete(pkg, true);

        var e = new CommunityReader(community).Read().Single();

        Assert.Equal(EntryKind.Junction, e.Kind);
        Assert.False(e.TargetExists);
        Assert.False(e.HasManifest);
    }

    [Fact]
    public void Hidden_entries_are_read_too()
    {
        var hidden = Directory.CreateDirectory(C("hidden"));
        hidden.Attributes |= FileAttributes.Hidden;

        Assert.Equal("hidden", new CommunityReader(community).Read().Single().Name);
    }
}
