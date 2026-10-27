namespace AddonManager.Sim2020.Bgl;

/// <summary>The BGL airport ident packing: the code in base 38 shifted left five bits (0 = end, 2-11 = 0-9, 12-37 = A-Z).</summary>
public static class AirportCodeCodec
{
    public static string Decode(uint packed)
    {
        var value = packed >> 5;
        var chars = new Stack<char>();
        while (value > 0)
        {
            var digit = (int)(value % 38);
            value /= 38;
            chars.Push(digit switch
            {
                >= 2 and <= 11 => (char)('0' + digit - 2),
                >= 12 and <= 37 => (char)('A' + digit - 12),
                _ => '?',
            });
        }

        return new string([.. chars]);
    }

    public static uint Encode(string code)
    {
        uint value = 0;
        foreach (var c in code.ToUpperInvariant())
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0' + 2,
                >= 'A' and <= 'Z' => c - 'A' + 12,
                _ => throw new ArgumentException($"Unsupported character '{c}' in airport code.", nameof(code)),
            };
            value = value * 38 + (uint)digit;
        }

        return value << 5;
    }
}
