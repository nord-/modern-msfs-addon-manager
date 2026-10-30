using System.Text.Json.Serialization;
using AddonManager.Core.Content;

namespace AddonManager.Core.Packages;

/// <summary>
/// A registered Library with the menu sections it holds. Equality and hashing use the normalised path only, case-insensitively.
/// </summary>
/// <param name="Sections">The sections the Library holds; at least one (<see cref="AllSections"/> for all three).</param>
public sealed record LibraryRoot(string Path, IReadOnlyList<Category> Sections)
{
    public static IReadOnlyList<Category> AllSections { get; } = [Category.Scenery, Category.Aircraft, Category.Other];

    private readonly IReadOnlyList<Category> sections = Normalise(Sections);

    /// <summary>Sorted and distinct; never empty. The setter normalises and validates, so a <c>with</c> expression does too. Stored under the key "types" so existing user files keep their sections.</summary>
    [JsonPropertyName("types")]
    public IReadOnlyList<Category> Sections
    {
        get => sections;
        init => sections = Normalise(value);
    }

    private string Key => PathNormalizer.Normalize(Path);

    public bool IsRegisteredFor(Category section) => Sections.Contains(section);

    public bool Equals(LibraryRoot? other)
        => other is not null && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Key);

    private static IReadOnlyList<Category> Normalise(IReadOnlyList<Category>? value)
        => value is { Count: > 0 }
            ? value.Distinct().Order().ToList()
            : throw new ArgumentException("A Library needs at least one section.", nameof(Sections));
}
