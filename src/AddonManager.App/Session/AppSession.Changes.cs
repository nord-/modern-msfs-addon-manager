using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Infrastructure;

namespace AddonManager.App.Session;

public sealed partial class AppSession
{
    /// <summary>What an edit of a card returns when the card is no longer on the board, or is only provisional.</summary>
    public const string CardGoneText = "The card is no longer there.";

    public Task<string?> SwitchCardAsync(CardRef card) => Task.Run(() => Change(links =>
    {
        // The card the user clicked: the shown board, which may still list a copy that is gone (D2b).
        if (view.Board is not { } shown || card.In(shown) is not { } found)
            return null;

        var plan = CardSwitch.Toggle(found);
        return plan.Choices.Count == 0 ? null : links.Applier.SetChoices(State, latestDiscovery!.Index, plan.Choices);
    }));

    public Task<string?> RemoveAsync(CardRef card, PackageKey package) => Task.Run(() => Change(links =>
    {
        links.Remover.Remove(State, BuildBoard(latestInput!), latestDiscovery!.Index, package);
        return null;
    }));

    public Task<string?> RenameAsync(CardRef card, string? name)
        => Task.Run(() => Edit([card], (links, _, ids) => links.Editor.Rename(State, ids[0], name)));

    public Task<string?> SetImageAsync(CardRef card, string? path)
        => Task.Run(() => Edit([card], (links, _, ids) => links.Editor.SetImage(State, ids[0], path)));

    public Task<(CodeOutcome? Outcome, string? Message)> AddCodeAsync(CardRef card, string text)
        => Task.Run(() => Edit([card], (links, board, ids) => links.Editor.AddCode(State, board, ids[0], text)));

    public Task<string?> RemoveCodeAsync(CardRef card, AirportCode code)
        => Task.Run(() => Edit([card], (links, board, ids) => links.Editor.RemoveCode(State, board, ids[0], code)));

    public Task<string?> SplitAsync(CardRef card, PackageKey package)
        => Task.Run(() => Edit([card], (links, _, ids) => links.Editor.Split(State, ids[0], package)).Message);

    public Task<string?> MergeAsync(CardRef dragged, CardRef target)
        => Task.Run(() => Edit([dragged, target], (links, board, ids) => links.Editor.Merge(State, board, ids[0], ids[1])));

    public Task<string?> MoveToTabAsync(CardRef card, CardTab tab)
        => Task.Run(() => Edit([card], (links, _, ids) => links.Editor.MoveToTab(State, ids[0], tab)));

    public Task<string?> MoveToSectionAsync(CardRef card, Category section)
        => Task.Run(() => Edit([card], (links, _, ids) => links.Editor.MoveToSection(State, Reanalyzed(latestInput!), ids[0], section)));

    /// <summary>
    /// Runs one user change under the lock and publishes the board it leaves, from the latest input with Community read now (D3), so a run that
    /// is still going shows the change when it completes. Returns the change's value and the message to show (or <c>null</c>); nothing escapes.
    /// </summary>
    private (T? Value, string? Message) Change<T>(Func<AppLinks, (T Value, ApplyResult? Applied)> change)
    {
        SessionView? raise;
        T value;
        string? message = null;
        lock (gate)
        {
            if (disposed || services?.Links is not { } links || latestDiscovery is null || latestInput is null)
                return (default, null);

            ApplyResult? result;
            try
            {
                (value, result) = change(links);
            }
            catch (SimRunningException e)
            {
                return (default, e.Message);
            }
            catch (UserDataException e)
            {
                unsaved = true;
                return (default, e.Message);
            }
            catch (ArgumentException e)
            {
                return (default, e.ParamName is null ? e.Message : e.Message.Replace($" (Parameter '{e.ParamName}')", ""));
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return (default, e.Message);
            }

            if (result is { Failed.Count: > 0 })
                message = DescribeFailed(result.Failed);

            // The error line belongs to the last discovery or run (it recomputes it), so a card change leaves it as it is.
            raise = Publish(view.Board is null ? view : view with { Board = BuildBoard(latestInput) });
        }

        Raise(raise);
        return (value, message);
    }

    private string? Change(Func<AppLinks, ApplyResult?> change) => Change(links => (true, change(links))).Message;

    /// <summary>
    /// Runs a card edit through <see cref="Change{T}"/> on the stored cards the references name on the shown board; a reference to a card that
    /// is gone, or only provisional, refuses the edit with <see cref="CardGoneText"/> before anything is called.
    /// </summary>
    private (T? Value, string? Message) Edit<T>(IReadOnlyList<CardRef> cards, Func<AppLinks, Board, long[], T> edit)
        => Change(links =>
        {
            if (view.Board is not { } shown)
                return (default(T)!, (ApplyResult?)null);

            var ids = cards.Select(c => c.In(shown)?.Id ?? throw new CardGoneException()).ToArray();
            return (edit(links, shown, ids), null);
        });

    private string? Edit(IReadOnlyList<CardRef> cards, Action<AppLinks, Board, long[]> edit)
        => Edit(cards, (links, board, ids) =>
        {
            edit(links, board, ids);
            return true;
        }).Message;

    private static string DescribeFailed(IReadOnlyList<(PackageName Package, string Error)> failed)
        => failed.Count == 1
            ? $"Could not change {failed[0].Package.Value}: {failed[0].Error}"
            : $"{failed.Count} packages could not be changed: {failed[0].Error}";

    private sealed class CardGoneException() : InvalidOperationException(CardGoneText);
}
