namespace AddonManager.Infrastructure.Tests;

public sealed class GsxFolderReaderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Reads_the_files_of_the_folder()
    {
        File.WriteAllText(Path.Combine(_temp.Root, "LPMA.ini"), "");
        File.WriteAllText(Path.Combine(_temp.Root, "essa-orbx.py"), "");

        var content = GsxFolderReader.Read(_temp.Root);

        Assert.True(content.Found);
        Assert.Equal(Path.Combine(_temp.Root, "LPMA.ini"), Assert.Single(content.Profiles).Path);
        Assert.Equal("ESSA", Assert.Single(content.Scripts).Code.Value);
    }

    [Fact]
    public void A_missing_folder_is_not_found()
    {
        var content = GsxFolderReader.Read(Path.Combine(_temp.Root, "nope"));

        Assert.False(content.Found);
        Assert.Empty(content.Profiles);
        Assert.Empty(content.Scripts);
        Assert.Empty(content.Unrecognised);
    }

    [Fact]
    public void A_folder_that_cannot_be_listed_is_not_found()
    {
        Assert.False(GsxFolderReader.Read(_temp.Root, _ => throw new UnauthorizedAccessException()).Found);
        Assert.False(GsxFolderReader.Read(_temp.Root, _ => throw new IOException()).Found);
    }

    [Fact]
    public void Subfolders_are_not_read()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Root, "sub"));
        File.WriteAllText(Path.Combine(_temp.Root, "sub", "eham.ini"), "");

        var content = GsxFolderReader.Read(_temp.Root);

        Assert.True(content.Found);
        Assert.Empty(content.Profiles);
    }
}
