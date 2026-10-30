using AddonManager.Core.Gsx;

namespace AddonManager.Core.Tests.Gsx;

public sealed class GsxFolderContentTests
{
    private const string Folder = @"C:\Gsx";

    [Fact]
    public void Sorts_files_into_profiles_scripts_and_unrecognised()
    {
        var content = GsxFolderContent.FromFiles(Folder, ["LPMA.ini", "epgd_LukSid.ini", "essa-orbx.py", "YALR1.ini", "notes.txt"]);

        Assert.True(content.Found);
        Assert.Equal(["epgd_LukSid", "LPMA"], content.Profiles.Select(p => p.Name.Value));
        Assert.All(content.Profiles, p => Assert.False(p.Disabled));
        Assert.Equal("ESSA", Assert.Single(content.Scripts).Code.Value);
        Assert.Equal(["YALR1.ini"], content.Unrecognised);
        Assert.Equal(5, content.FileNames.Count);
        Assert.Contains("NOTES.TXT", content.FileNames);
    }

    [Fact]
    public void An_ini_and_an_off_of_one_name_are_one_profile()
    {
        var content = GsxFolderContent.FromFiles(Folder, ["lpma.ini", "LPMA.off"]);

        var profile = Assert.Single(content.Profiles);
        Assert.EndsWith("lpma.ini", profile.Path);
        Assert.False(profile.Disabled);
        Assert.EndsWith("LPMA.off", profile.OffVariantPath);
    }

    [Fact]
    public void An_off_alone_is_a_disabled_profile()
    {
        var content = GsxFolderContent.FromFiles(Folder, ["lppt.off"]);

        var profile = Assert.Single(content.Profiles);
        Assert.True(profile.Disabled);
        Assert.Null(profile.OffVariantPath);
    }
}
