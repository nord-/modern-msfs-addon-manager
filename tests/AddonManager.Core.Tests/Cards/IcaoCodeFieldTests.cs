using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.Tests.Fakes;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class IcaoCodeFieldTests
{
    private static PackageKey Key(string n) => PackageKey.Folder(new PackageName(n));

    private static World NewWorld()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        return w;
    }

    private static Board Wall(World w) => BoardBuilder.Build(w.State, w.Input());

    private static CardRecord CardOf(World w, string package) => Assert.IsType<CardRecord>(w.State.CardOf(Key(package)));

    private static (CardEditor Editor, InMemoryUserStateStore Store) NewEditor()
    {
        var store = new InMemoryUserStateStore();
        return (new CardEditor(store, TimeProvider.System), store);
    }

    /// <summary>EHAM Schiphol (card with an ICAO code), an ESSA card and a code-less card.</summary>
    private static World Scene()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"], title: "EHAM Schiphol"));
        w.Add(Scenery("essa-x", ["ESSA"], title: "Arlanda"));
        w.Add(Scenery("plain-z", []));
        w.Place();
        return w;
    }

    [Fact]
    public void Adding_an_icao_code_moves_the_card_to_airports()
    {
        var w = Scene();
        var card = CardOf(w, "plain-z");
        card.Tab = CardTab.Other;
        var (editor, store) = NewEditor();

        var outcome = editor.AddCode(w.State, Wall(w), card.Id, "ehrd");

        Assert.Equal(CodeResult.Added, outcome.Result);
        Assert.Equal([Code("EHRD")], card.Codes);
        Assert.Equal(CardTab.Airports, card.Tab);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void An_icao_code_another_card_has_is_rejected_naming_that_card()
    {
        var w = Scene();
        var board = Wall(w);
        var holder = CardOf(w, "eham-x");
        var (editor, store) = NewEditor();

        var outcome = editor.AddCode(w.State, board, CardOf(w, "essa-x").Id, "EHAM");

        Assert.Equal(CodeResult.Taken, outcome.Result);
        Assert.Equal(holder.Id, outcome.Holder);
        Assert.Equal("EHAM Schiphol", outcome.HolderName);
        Assert.Equal([Code("ESSA")], CardOf(w, "essa-x").Codes);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Any_three_to_five_letters_or_digits_are_accepted()
    {
        var w = Scene();
        var card = CardOf(w, "plain-z");
        var (editor, _) = NewEditor();

        var outcome = editor.AddCode(w.State, Wall(w), card.Id, "zz9");

        Assert.Equal(CodeResult.Added, outcome.Result);
        Assert.Equal([Code("ZZ9")], card.Codes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("abcdef")]
    [InlineData("ab-c")]
    public void Text_that_is_no_icao_code_is_invalid(string text)
    {
        var w = Scene();
        var card = CardOf(w, "plain-z");
        var (editor, store) = NewEditor();

        var outcome = editor.AddCode(w.State, Wall(w), card.Id, text);

        Assert.Equal(CodeResult.Invalid, outcome.Result);
        Assert.Empty(card.Codes);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Adding_an_icao_code_to_a_card_that_has_one_keeps_its_name()
    {
        var w = Scene();
        var card = CardOf(w, "eham-x");
        var (editor, _) = NewEditor();

        editor.AddCode(w.State, Wall(w), card.Id, "EHRD");

        Assert.Equal([Code("EHAM"), Code("EHRD")], card.Codes);
        Assert.Equal("EHAM Schiphol", card.Name);
    }

    [Fact]
    public void Adding_an_icao_code_to_a_card_without_one_leaves_its_name_to_the_board()
    {
        var w = Scene();
        var card = CardOf(w, "plain-z");
        var (editor, _) = NewEditor();

        editor.AddCode(w.State, Wall(w), card.Id, "EHRD");

        Assert.Null(card.Name);
    }

    [Fact]
    public void An_icao_code_already_on_the_card_is_added_without_saving()
    {
        var w = Scene();
        var (editor, store) = NewEditor();

        var outcome = editor.AddCode(w.State, Wall(w), CardOf(w, "eham-x").Id, "eham");

        Assert.Equal(CodeResult.Added, outcome.Result);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Adding_an_icao_code_clears_its_removal()
    {
        var w = Scene();
        var card = CardOf(w, "eham-x");
        card.RemoveCode(Code("EHRD"));
        var (editor, _) = NewEditor();

        editor.AddCode(w.State, Wall(w), card.Id, "EHRD");

        Assert.Empty(card.RemovedCodes);
        Assert.Contains(Code("EHRD"), card.Codes);
    }

    [Fact]
    public void Removing_an_icao_code_unlinks_every_package_and_is_remembered()
    {
        var w = Scene();
        var card = CardOf(w, "eham-x");
        var (editor, store) = NewEditor();
        editor.AddCode(w.State, Wall(w), card.Id, "EHRD");
        var saves = store.SaveCount;

        editor.RemoveCode(w.State, Wall(w), card.Id, Code("EHRD"));

        var summaries = PackageCatalog.Build(w.State, w.Input());
        Assert.All(w.State.PackagesOn(card.Id), k => Assert.DoesNotContain(Code("EHRD"), summaries[k.Name].Codes));
        Assert.Equal([Code("EHRD")], card.RemovedCodes);
        Assert.Equal([Code("EHAM")], card.Codes);
        Assert.Equal(saves + 1, store.SaveCount);
    }

    [Fact]
    public void Removing_an_icao_code_keeps_the_tab_and_the_name()
    {
        var w = Scene();
        var card = CardOf(w, "eham-x");
        card.Tab = CardTab.Other;
        var shown = Assert.IsType<Card>(Wall(w).Find(card.Id)).Name;
        var (editor, _) = NewEditor();

        editor.RemoveCode(w.State, Wall(w), card.Id, Code("EHAM"));

        Assert.Equal(CardTab.Other, card.Tab);
        Assert.Equal(shown, card.Name);
    }

    [Fact]
    public void Removing_an_icao_code_the_card_lacks_is_refused()
    {
        var w = Scene();
        var (editor, store) = NewEditor();

        Assert.Throws<InvalidOperationException>(() => editor.RemoveCode(w.State, Wall(w), CardOf(w, "eham-x").Id, Code("EHRD")));
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void A_later_package_with_a_removed_icao_code_joins_the_card()
    {
        var w = Scene();
        var card = CardOf(w, "eham-x");
        var (editor, _) = NewEditor();
        editor.AddCode(w.State, Wall(w), card.Id, "EHRD");
        editor.RemoveCode(w.State, Wall(w), card.Id, Code("EHRD"));
        w.Add(Scenery("ehrd-y", ["EHRD"]));

        w.Place();

        Assert.Equal(card.Id, CardOf(w, "ehrd-y").Id);
    }

    [Fact]
    public void Aircraft_and_other_cards_take_no_icao_code()
    {
        var w = Scene();
        var card = CardOf(w, "plain-z");
        card.Section = Category.Aircraft;
        card.Tab = CardTab.Other;
        var (editor, store) = NewEditor();

        Assert.Throws<InvalidOperationException>(() => editor.AddCode(w.State, Wall(w), card.Id, "EHRD"));
        Assert.Throws<InvalidOperationException>(() => editor.RemoveCode(w.State, Wall(w), card.Id, Code("EHRD")));
        Assert.Equal(0, store.SaveCount);
    }
}
