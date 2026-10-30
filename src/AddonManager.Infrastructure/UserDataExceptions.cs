namespace AddonManager.Infrastructure;

/// <summary>A user data file that cannot be used. The message names the file and is fit to show.</summary>
public class UserDataException(string message, string filePath, Exception? inner = null) : IOException(message, inner)
{
    public string FilePath { get; } = filePath;
}

/// <summary>Another program holds the file's write lock.</summary>
public sealed class UserDataLockedException(string filePath)
    : UserDataException($"'{filePath}' is locked by another program. Close it and try again.", filePath);

/// <summary>The file is damaged or is not ours. Nothing was written to it; <see cref="Copies"/> holds the byte-for-byte copies.</summary>
public sealed class UserDataDamagedException(string filePath, IReadOnlyList<string> copies, string? detail = null)
    : UserDataException(MessageOf(filePath, copies, detail), filePath)
{
    public IReadOnlyList<string> Copies { get; } = copies;

    /// <summary>Names the copies only when there are any, so a row that cannot be read (nothing is copied) gives no empty "Copies:".</summary>
    private static string MessageOf(string filePath, IReadOnlyList<string> copies, string? detail)
    {
        var text = $"'{filePath}' is damaged.";
        if (copies.Count > 0)
            text += $" Copies: {string.Join(", ", copies)}";
        return detail is null ? text : $"{text} {detail}";
    }
}

/// <summary>The file was written by a newer version of the app.</summary>
public sealed class UserDataTooNewException(string filePath, int fileVersion)
    : UserDataException($"'{filePath}' was written by a newer version of the app (user data version {fileVersion}).", filePath);

/// <summary>The file has an older user data version and cache-only access may not migrate it.</summary>
public sealed class UserDataTooOldException(string filePath)
    : UserDataException($"Run the app once to update '{filePath}'.", filePath);

/// <summary>SQLite could not open or write the file for a reason that is neither a lock nor damage (read-only, disk full, I/O error).</summary>
public sealed class UserDataUnavailableException(string filePath, string detail, Exception inner)
    : UserDataException($"'{filePath}' cannot be used: {detail}", filePath, inner);
