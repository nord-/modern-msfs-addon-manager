using AddonManager.App.Session;
using AddonManager.App.Tests.ViewModels;
using AddonManager.Core.Packages;

namespace AddonManager.App.Tests.Session;

public sealed class CardRefTests
{
    [Fact]
    public void A_provisional_card_is_found_by_its_package_after_it_is_stored()
    {
        var provisional = Boards.Card(null, "a");
        var reference = CardRef.Of(provisional);
        var stored = Boards.Card(7, "a");

        Assert.Same(provisional, reference.In(Boards.Board(provisional)));
        Assert.Same(stored, reference.In(Boards.Board(Boards.Card(8, "other"), stored)));
    }

    [Fact]
    public void A_stored_card_is_found_by_its_id()
    {
        var stored = Boards.Card(7, "a");
        var reference = CardRef.Of(stored);

        Assert.Equal(new CardRef(7, null), reference);
        Assert.Same(stored, reference.In(Boards.Board(Boards.Card(8, "b"), stored)));
        Assert.Null(reference.In(Boards.Board(Boards.Card(8, "b"))));
    }
}
