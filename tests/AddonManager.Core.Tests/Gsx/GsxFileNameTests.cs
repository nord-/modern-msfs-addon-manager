using AddonManager.Core.Content;
using AddonManager.Core.Gsx;

namespace AddonManager.Core.Tests.Gsx;

public sealed class GsxFileNameTests
{
    [Theory]
    [InlineData("epgd_LukSid.ini", GsxFileKind.Profile, "EPGD")]
    [InlineData("LPMA.ini", GsxFileKind.Profile, "LPMA")]
    [InlineData("eddf-virtualFRA_GSX_VDGS.ini", GsxFileKind.Profile, "EDDF")]
    [InlineData("EFHK-Epixeri.ini", GsxFileKind.Profile, "EFHK")]
    [InlineData("ESSA.INI", GsxFileKind.Profile, "ESSA")]
    [InlineData("lpma.off", GsxFileKind.Disabled, "LPMA")]
    [InlineData("essa-orbx.py", GsxFileKind.Script, "ESSA")]
    [InlineData("3U2-x.ini", GsxFileKind.Profile, "3U2")]
    [InlineData("gsx.ini", GsxFileKind.Profile, "GSX")]
    [InlineData("ldsp.v2.ini", GsxFileKind.Profile, "LDSP")]
    public void Yields_kind_and_icao_code(string file, GsxFileKind kind, string code)
    {
        Assert.True(GsxFileName.TryParse(file, out var actualKind, out var actualCode));
        Assert.Equal(kind, actualKind);
        Assert.Equal(code, actualCode.Value);
    }

    [Theory]
    [InlineData("YALR1.ini")] // 5 characters are not matched by file name
    [InlineData("panel.ini")] // leading run of 5
    [InlineData("ab.ini")] // too short
    [InlineData("eham+x.ini")] // '+' is no separator
    public void Yields_no_icao_code(string file)
    {
        Assert.True(GsxFileName.TryParse(file, out _, out var code));
        Assert.Equal(default(AirportCode), code);
    }

    [Theory]
    [InlineData("eham.ini.bak")]
    [InlineData("readme.txt")]
    [InlineData("gsx.cfg")]
    public void Ignores_other_extensions(string file)
        => Assert.False(GsxFileName.TryParse(file, out _, out _));
}
