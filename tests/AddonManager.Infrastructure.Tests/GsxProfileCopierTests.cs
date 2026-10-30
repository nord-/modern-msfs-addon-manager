using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure.Tests;

public sealed class GsxProfileCopierTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _gsx;
    private readonly string _ini;
    private readonly string _script;

    public GsxProfileCopierTests()
    {
        _gsx = Directory.CreateDirectory(Path.Combine(_temp.Root, "gsx")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(_temp.Root, "lib", "pkg", "GSX")).FullName;
        _ini = Path.Combine(source, "essa-x.ini");
        _script = Path.Combine(source, "essa-x.py");
        File.WriteAllText(_ini, "ini");
        File.WriteAllText(_script, "py");
    }

    public void Dispose() => _temp.Dispose();

    private ShippedProfile Shipped() => new(Code("ESSA"), new PackageName("pkg"), _ini, [_script], false);

    private static AirportCode Code(string text)
    {
        Assert.True(AirportCode.TryParse(text, out var code));
        return code;
    }

    private string InGsx(string name) => Path.Combine(_gsx, name);

    [Fact]
    public void Copies_the_ini_and_its_scripts()
    {
        var result = GsxProfileCopier.Copy(Shipped(), _gsx, confirmed: false);

        Assert.Null(result.Refusal);
        Assert.False(result.NeedsConfirmation);
        Assert.Equal([InGsx("essa-x.ini"), InGsx("essa-x.py")], result.Copied);
        Assert.Equal("ini", File.ReadAllText(InGsx("essa-x.ini")));
        Assert.Equal("py", File.ReadAllText(InGsx("essa-x.py")));
    }

    [Fact]
    public void Never_overwrites_an_existing_file()
    {
        File.WriteAllText(InGsx("essa-x.py"), "mine");

        var result = GsxProfileCopier.Copy(Shipped(), _gsx, confirmed: false);

        Assert.Equal([InGsx("essa-x.ini")], result.Copied);
        Assert.Equal([InGsx("essa-x.py")], result.Skipped);
        Assert.Equal("mine", File.ReadAllText(InGsx("essa-x.py")));

        File.WriteAllText(InGsx("essa-x.ini"), "kept");
        var again = GsxProfileCopier.Copy(Shipped(), _gsx, confirmed: true);

        Assert.NotNull(again.Refusal);
        Assert.Empty(again.Copied);
        Assert.Equal("kept", File.ReadAllText(InGsx("essa-x.ini")));
    }

    [Fact]
    public void Does_not_copy_without_confirmation_when_a_profile_is_installed()
    {
        File.WriteAllText(InGsx("essa-a.ini"), "");

        var result = GsxProfileCopier.Copy(Shipped(), _gsx, confirmed: false);

        Assert.True(result.NeedsConfirmation);
        Assert.Contains("Two profiles for ESSA", result.Refusal);
        Assert.Empty(result.Copied);
        Assert.False(File.Exists(InGsx("essa-x.ini")));
    }

    [Fact]
    public void Copies_with_confirmation()
    {
        File.WriteAllText(InGsx("essa-a.ini"), "");

        var result = GsxProfileCopier.Copy(Shipped(), _gsx, confirmed: true);

        Assert.Null(result.Refusal);
        Assert.Equal(2, result.Copied.Count);
        Assert.True(File.Exists(InGsx("essa-x.ini")));
    }

    [Fact]
    public void A_disabled_profile_at_the_code_needs_no_confirmation()
    {
        File.WriteAllText(InGsx("essa-a.off"), "");

        var result = GsxProfileCopier.Copy(Shipped(), _gsx, confirmed: false);

        Assert.False(result.NeedsConfirmation);
        Assert.Equal(2, result.Copied.Count);
    }

    [Fact]
    public void Never_creates_the_gsx_folder()
    {
        var missing = Path.Combine(_temp.Root, "nope");

        var result = GsxProfileCopier.Copy(Shipped(), missing, confirmed: true);

        Assert.Equal("The GSX folder was not found", result.Refusal);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Leaves_the_source_untouched()
    {
        var written = File.GetLastWriteTimeUtc(_ini);

        GsxProfileCopier.Copy(Shipped(), _gsx, confirmed: false);

        Assert.Equal("ini", File.ReadAllText(_ini));
        Assert.Equal("py", File.ReadAllText(_script));
        Assert.Equal(written, File.GetLastWriteTimeUtc(_ini));
    }

    [Fact]
    public void A_failing_copy_is_refused_instead_of_thrown()
    {
        var result = GsxProfileCopier.Copy(Shipped() with { IniPath = Path.Combine(_temp.Root, "gone.ini") }, _gsx, confirmed: true);

        Assert.Contains("gone.ini", result.Refusal);
        Assert.Empty(result.Copied);
    }

    [Fact]
    public void A_copy_that_fails_on_a_script_keeps_what_was_copied()
    {
        var result = GsxProfileCopier.Copy(Shipped() with { ScriptPaths = [Path.Combine(_temp.Root, "gone.py")] }, _gsx, confirmed: true);

        Assert.Contains("gone.py", result.Refusal);
        Assert.Equal([InGsx("essa-x.ini")], result.Copied);
    }
}
