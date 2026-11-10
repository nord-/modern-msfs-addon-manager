using System.Windows.Media;
using System.Windows.Media.Imaging;
using AddonManager.App.Thumbnails;

namespace AddonManager.App.Tests.Thumbnails;

public class ThumbnailCacheTests
{
    private static string WriteJpeg(TempFolder temp, string name = "Thumbnail.jpg")
    {
        var pixels = new byte[64 * 64 * 3];
        var source = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Rgb24, null, pixels, 64 * 3);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        var path = Path.Combine(temp.Root, name);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
        return path;
    }

    [Fact]
    public async Task A_jpeg_loads_frozen()
    {
        using var temp = new TempFolder();
        var image = await new ThumbnailCache().LoadAsync(WriteJpeg(temp));

        Assert.NotNull(image);
        Assert.True(image!.IsFrozen);
    }

    [Fact]
    public async Task A_missing_file_gives_null()
    {
        using var temp = new TempFolder();
        Assert.Null(await new ThumbnailCache().LoadAsync(Path.Combine(temp.Root, "none.jpg")));
        Assert.Null(await new ThumbnailCache().LoadAsync(null));
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_gives_null()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Root, "text.jpg");
        File.WriteAllText(path, "not an image");

        Assert.Null(await new ThumbnailCache().LoadAsync(path));
    }

    [Fact]
    public async Task A_locked_file_gives_null()
    {
        using var temp = new TempFolder();
        var path = WriteJpeg(temp);
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Null(await new ThumbnailCache().LoadAsync(path));
    }

    [Fact]
    public async Task The_same_path_is_loaded_once()
    {
        using var temp = new TempFolder();
        var path = WriteJpeg(temp);
        var cache = new ThumbnailCache();

        var first = await cache.LoadAsync(path);
        var second = await cache.LoadAsync(path.ToUpperInvariant());

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task A_cached_image_whose_file_is_gone_loads_as_null()
    {
        using var temp = new TempFolder();
        var path = WriteJpeg(temp);
        var cache = new ThumbnailCache();
        Assert.NotNull(await cache.LoadAsync(path));

        File.Delete(path);
        Assert.Null(await cache.LoadAsync(path));

        WriteJpeg(temp);
        Assert.NotNull(await cache.LoadAsync(path));
    }

    [Fact]
    public async Task A_file_that_failed_loads_once_it_is_readable()
    {
        using var temp = new TempFolder();
        var path = WriteJpeg(temp);
        var cache = new ThumbnailCache();

        ImageSource? whileLocked;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            whileLocked = await cache.LoadAsync(path);

        Assert.Null(whileLocked);
        Assert.NotNull(await cache.LoadAsync(path));
    }
}
