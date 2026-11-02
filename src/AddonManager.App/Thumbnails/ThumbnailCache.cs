using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AddonManager.Core.Packages;

namespace AddonManager.App.Thumbnails;

/// <summary>
/// Loads card thumbnails on a background thread, decoded to card width and frozen so the UI thread can use them. Loaded images are
/// kept by normalised path; a failure (missing, locked or undecodable file) is not kept, so the thumbnail is tried again on the next board.
/// </summary>
public sealed class ThumbnailCache
{
    private const int DecodeWidth = 320;

    private readonly ConcurrentDictionary<string, ImageSource> loaded = new(StringComparer.OrdinalIgnoreCase);

    public Task<ImageSource?> LoadAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Task.FromResult<ImageSource?>(null);

        var key = PathNormalizer.Normalize(path);
        if (loaded.TryGetValue(key, out var cached))
            return Task.FromResult<ImageSource?>(cached);

        return Task.Run(() =>
        {
            var image = Read(path);
            // Two loads of one path that overlap end on the same instance.
            return image is null ? null : loaded.GetOrAdd(key, image);
        });
    }

    private static ImageSource? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.DecodePixelWidth = DecodeWidth;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // Missing, locked or not an image: the card shows its placeholder and the next board tries again.
            return null;
        }
    }
}
