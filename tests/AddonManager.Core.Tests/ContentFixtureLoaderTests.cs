using AddonManager.Core.Gsx;

namespace AddonManager.Core.Tests;

public sealed class ContentFixtureLoaderTests
{
    private const string GsxFolder = @"C:\Users\user\AppData\Roaming\Virtuali\GSX\MSFS";

    [Fact]
    public void Loads_gsx_folder_and_shipped_files()
    {
        var gsx = ContentFixtureLoader.LoadGsx("gsx-sample");

        Assert.True(gsx.Found);
        Assert.Equal(["LPMA"], gsx.Profiles.Select(p => p.Name.Value));
        Assert.EndsWith("lpma.off", gsx.Profiles[0].OffVariantPath);
        Assert.StartsWith(GsxFolder, gsx.Profiles[0].Path);
        Assert.Equal(["essa-orbx.py"], gsx.Scripts.Select(s => Path.GetFileName(s.Path)));
        Assert.Equal(["YALR1.ini"], gsx.Unrecognised);
        Assert.Contains("gsx.cfg", gsx.FileNames);

        var content = Assert.Single(ContentFixtureLoader.Load("gsx-sample"));
        Assert.Equal([@"GSX-Profile-EPKK\epkk-x.ini", @"GSX-Profile-EPKK\epkk-x.py"], content.GsxFiles);
    }

    [Fact]
    public void A_fixture_without_gsx_folder_loads_as_missing()
    {
        Assert.Equal(GsxFolderContent.Missing, ContentFixtureLoader.LoadGsx("gsx-none"));
        Assert.Empty(Assert.Single(ContentFixtureLoader.Load("gsx-none")).GsxFiles);
    }
}
