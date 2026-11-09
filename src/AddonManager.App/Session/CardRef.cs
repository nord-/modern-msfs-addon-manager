using AddonManager.Core.Cards;
using AddonManager.Core.Packages;

namespace AddonManager.App.Session;

/// <summary>
/// Names a card across boards: a stored card by its id, a provisional card by its Package. A provisional card becomes a stored card at the
/// next settled scan, so a reference taken from the old board still finds it on the new one.
/// </summary>
public sealed record CardRef(long? Id, PackageKey? Package)
{
    public static CardRef Of(Card card) => new(card.Id, card.ProvisionalPackage);

    /// <summary>The card on <paramref name="board"/> this reference names, or <c>null</c> when the card is gone.</summary>
    public Card? In(Board board) => Id is { } id ? board.Find(id) : Package is { } package ? board.CardOf(package) : null;
}
