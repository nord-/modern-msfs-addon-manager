using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Gsx;

public sealed class GsxCopyTests
{
    private const string Ini = @"C:\Lib\pkg\GSX\epkk-x.ini";
    private const string Script = @"C:\Lib\pkg\GSX\epkk-x.py";

    private static ShippedProfile Shipped() => new(Code("EPKK"), new PackageName("pkg"), Ini, [Script], false);

    private static GsxFolderContent Folder(params string[] files) => GsxFolderContent.FromFiles(@"C:\GSX", files);

    [Fact]
    public void Ready_when_nothing_is_installed()
    {
        var check = GsxCopy.Check(Shipped(), Folder("lppt.ini"), 0);

        Assert.Equal(GsxCopyVerdict.Ready, check.Verdict);
        Assert.Equal([Ini, Script], check.Copy);
        Assert.Empty(check.Skip);
    }

    [Fact]
    public void Asks_to_confirm_when_a_profile_is_installed()
    {
        var check = GsxCopy.Check(Shipped(), Folder("epkk-a.ini"), 1);

        Assert.Equal(GsxCopyVerdict.ConfirmTwoProfiles, check.Verdict);
        Assert.Equal("Copying gives Two profiles for EPKK", check.Reason);
        Assert.Equal([Ini, Script], check.Copy);
    }

    [Fact]
    public void Refuses_when_the_ini_name_exists()
    {
        var check = GsxCopy.Check(Shipped(), Folder("EPKK-X.INI"), 1);

        Assert.Equal(GsxCopyVerdict.Refused, check.Verdict);
        Assert.Contains("epkk-x.ini", check.Reason);
        Assert.Empty(check.Copy);
    }

    [Fact]
    public void Refuses_when_a_disabled_profile_of_the_same_name_exists()
    {
        var check = GsxCopy.Check(Shipped(), Folder("epkk-x.off"), 0);

        Assert.Equal(GsxCopyVerdict.Refused, check.Verdict);
        Assert.Contains("epkk-x.off", check.Reason);
        Assert.Empty(check.Copy);
    }

    [Fact]
    public void Skips_a_script_whose_name_exists()
    {
        var check = GsxCopy.Check(Shipped(), Folder("EPKK-X.PY"), 0);

        Assert.Equal(GsxCopyVerdict.Ready, check.Verdict);
        Assert.Equal([Ini], check.Copy);
        Assert.Equal([Script], check.Skip);
    }

    [Fact]
    public void Refuses_when_the_gsx_folder_is_missing()
    {
        var check = GsxCopy.Check(Shipped(), GsxFolderContent.Missing, 0);

        Assert.Equal(GsxCopyVerdict.Refused, check.Verdict);
        Assert.Equal("The GSX folder was not found", check.Reason);
        Assert.Empty(check.Copy);
    }
}
