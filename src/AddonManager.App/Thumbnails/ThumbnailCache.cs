using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AddonManager.Core.Packages;

namespace AddonManager.App.Thumbnails;

/// <summary>
/// Loads card thumbnails on a background thread, decoded to card width and frozen so the UI thread can use them. Loaded images are
/// kept by normalised path; a failure (missing, locked or undecodable file) is not kept, so the thumbnail is tried again on the next board.
/// A kept image is checked against the disk on every load, off the UI thread, and forgotten when its file is gone.
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
        return Task.Run(() =>
        {
            if (loaded.TryGetValue(key, out var cached))
            {
                // Checked here, never on the UI thread: a sleeping network share can block File.Exists for seconds.
                if (File.Exists(path))
                    return cached;
                loaded.TryRemove(key, out _);
                return null;
            }

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
