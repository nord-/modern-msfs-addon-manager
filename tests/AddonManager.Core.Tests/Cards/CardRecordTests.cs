using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class CardRecordTests
{
    private static CardRecord NewCard() => new() { Id = 1, Created = World.Now, Section = Category.Scenery };

    [Fact]
    public void AddCode_keeps_codes_distinct_sorted_and_out_of_the_removed_ones()
    {
        var card = NewCard();
        card.RemoveCode(Code("EHRD"));

        card.AddCode(Code("EHRD"));
        card.AddCode(Code("EHAM"));
        card.AddCode(Code("eham"));

        Assert.Equal([Code("EHAM"), Code("EHRD")], card.Codes);
        Assert.Empty(card.RemovedCodes);
    }

    [Fact]
    public void RemoveCode_moves_a_code_to_the_removed_ones_in_order()
    {
        var card = NewCard();
        card.AddCode(Code("EHAM"));
        card.AddCode(Code("EHRD"));

        card.RemoveCode(Code("EHRD"));
        card.RemoveCode(Code("ESSA"));
        card.RemoveCode(Code("EHAM"));

        Assert.Empty(card.Codes);
        Assert.Equal([Code("EHAM"), Code("EHRD"), Code("ESSA")], card.RemovedCodes);
    }

    [Fact]
    public void MatchCodes_are_the_codes_and_the_removed_codes_sorted()
    {
        var card = NewCard();
        card.AddCode(Code("EHRD"));
        card.RemoveCode(Code("EHAM"));

        Assert.Equal([Code("EHAM"), Code("EHRD")], card.MatchCodes);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var card = NewCard();
        card.AddCode(Code("EHAM"));
        card.Name = "Mine";

        var copy = card.Clone();
        copy.AddCode(Code("EHRD"));
        copy.Name = "Other";

        Assert.Equal([Code("EHAM")], card.Codes);
        Assert.Equal("Mine", card.Name);
        Assert.Equal((card.Id, card.Created, card.Section), (copy.Id, copy.Created, copy.Section));
    }
}
