using AddonManager.Sim2020.Content;

namespace AddonManager.Sim2020.Tests.Content;

public sealed class LayoutReaderTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private string Pkg => _temp.Root;

    public void Dispose() => _temp.Dispose();

    private void WriteLayout(string json) => File.WriteAllText(Path.Combine(Pkg, "layout.json"), json);

    [Fact]
    public void Layout_paths_use_backslashes()
    {
        WriteLayout("""{"content":[{"path":"SimObjects/Airplanes/X/aircraft.cfg","size":1,"date":1}]}""");
        Assert.Equal([@"SimObjects\Airplanes\X\aircraft.cfg"], LayoutReader.Read(Pkg));
    }

    [Fact]
    public void Entries_outside_the_package_or_with_invalid_characters_are_dropped()
    {
        WriteLayout("""
            {"content":[
              {"path":"a\u0000.bgl"},
              {"path":"..\\x.bgl"},
              {"path":"scenery/../../x.bgl"},
              {"path":"C:\\x.bgl"},
              {"path":"\\x.bgl"},
              {"path":"a|b.bgl"},
              {"path":"scenery/ok.bgl"},
              {"path":"scenery/..ok.bgl"}
            ]}
            """);
        Assert.Equal([@"scenery\ok.bgl", @"scenery\..ok.bgl"], LayoutReader.Read(Pkg));
    }

    [Fact]
    public void Missing_or_invalid_layout_reads_null_and_falls_back_to_a_walk()
    {
        Directory.CreateDirectory(Path.Combine(Pkg, "scenery"));
        File.WriteAllText(Path.Combine(Pkg, "scenery", "a.bgl"), "x");
        Assert.Null(LayoutReader.Read(Pkg));
        Assert.Contains(@"scenery\a.bgl", LayoutReader.ListFiles(Pkg));

        WriteLayout("{ not json");
        Assert.Null(LayoutReader.Read(Pkg));
        Assert.Contains(@"scenery\a.bgl", LayoutReader.ListFiles(Pkg));
    }

    [Fact]
    public void Valid_json_without_a_content_array_reads_null()
    {
        WriteLayout("""{"other":1}""");
        Assert.Null(LayoutReader.Read(Pkg));
    }

    [Fact]
    public void Layout_open_for_writing_by_another_process_is_still_readable()
    {
        WriteLayout("""{"content":[{"path":"a.bgl"}]}""");
        using var _ = new FileStream(Path.Combine(Pkg, "layout.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        Assert.Equal(["a.bgl"], LayoutReader.Read(Pkg));
    }

    [Fact]
    public void Layout_that_cannot_be_opened_propagates()
    {
        WriteLayout("""{"content":[]}""");
        using var _ = new FileStream(Path.Combine(Pkg, "layout.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.ThrowsAny<IOException>(() => LayoutReader.Read(Pkg));
    }

    [Fact]
    public void ListFiles_throws_when_the_package_root_is_missing()
    {
        Assert.ThrowsAny<IOException>(() => LayoutReader.ListFiles(Path.Combine(Pkg, "nope")));
    }
}
