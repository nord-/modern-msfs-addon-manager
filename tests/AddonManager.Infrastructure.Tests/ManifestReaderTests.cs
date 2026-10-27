using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure.Tests;

public sealed class ManifestReaderTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private Manifest Read(string json)
    {
        var file = Path.Combine(temp.Root, "manifest.json");
        File.WriteAllText(file, json);
        return ManifestReader.Read(file);
    }

    [Fact]
    public void Reads_the_fields_the_app_uses()
    {
        var m = Read("""
            {
              "dependencies": [ { "name": "fs-base", "package_version": "0.1.0" }, { "name": "orbx-libs-global" } ],
              "content_type": "SCENERY",
              "title": "EPGD Gdańsk",
              "creator": "Drzewiecki Design",
              "package_version": "1.0.3",
              "minimum_game_version": "1.37.19"
            }
            """);

        Assert.Equal("EPGD Gdańsk", m.Title);
        Assert.Equal("Drzewiecki Design", m.Creator);
        Assert.Equal("SCENERY", m.ContentType);
        Assert.Equal("1.0.3", m.PackageVersion);
        Assert.Equal(["fs-base", "orbx-libs-global"], m.Dependencies);
    }

    [Fact]
    public void Missing_title_and_unexpected_types_are_tolerated()
    {
        var m = Read("""{ "creator": 42, "dependencies": "none" }""");

        Assert.Null(m.Title);
        Assert.Null(m.Creator);
        Assert.Empty(m.Dependencies);
    }

    [Fact]
    public void Invalid_json_and_missing_file_give_an_empty_manifest()
    {
        Assert.Same(Manifest.Empty, Read("{ not json"));
        Assert.Same(Manifest.Empty, ManifestReader.Read(Path.Combine(temp.Root, "none.json")));
    }

    [Fact]
    public void Manifest_can_be_read_while_another_process_holds_it_open_for_writing()
    {
        var manifest = Path.Combine(temp.Root, "manifest.json");
        File.WriteAllText(manifest, """{ "title": "Title" }""");
        using var writer = new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("Title", ManifestReader.Read(manifest).Title);
    }

    [Fact]
    public void A_utf8_bom_is_dropped()
    {
        var manifest = Path.Combine(temp.Root, "manifest.json");
        File.WriteAllText(manifest, """{ "title": "Bom" }""", new System.Text.UTF8Encoding(true));

        Assert.Equal("Bom", ManifestReader.Read(manifest).Title);
    }
}
