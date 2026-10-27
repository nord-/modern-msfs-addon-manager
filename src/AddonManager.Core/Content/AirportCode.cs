namespace AddonManager.Core.Content;

/// <summary>An Airport code: 3 to 5 ASCII letters or digits, stored upper-case and compared case-insensitively.</summary>
public readonly struct AirportCode : IEquatable<AirportCode>
{
    private const int MinLength = 3;
    private const int MaxLength = 5;

    private readonly string? _value;

    private AirportCode(string value) => _value = value;

    /// <summary>The upper-case code; empty for <c>default</c>.</summary>
    public string Value => _value ?? "";

    /// <summary>Parses a code, trimming surrounding whitespace. Returns false for anything that isn't 3 to 5 ASCII letters or digits.</summary>
    public static bool TryParse(string? text, out AirportCode code)
    {
        code = default;
        var trimmed = text?.Trim();
        if (trimmed is null || trimmed.Length < MinLength || trimmed.Length > MaxLength || !trimmed.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        code = new AirportCode(trimmed.ToUpperInvariant());
        return true;
    }

    public bool Equals(AirportCode other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is AirportCode other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AirportCode left, AirportCode right) => left.Equals(right);

    public static bool operator !=(AirportCode left, AirportCode right) => !left.Equals(right);
}
