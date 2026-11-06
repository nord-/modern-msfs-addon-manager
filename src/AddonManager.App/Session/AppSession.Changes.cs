using AddonManager.Core.Cards;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Infrastructure;

namespace AddonManager.App.Session;

public sealed partial class AppSession
{
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

    /// <summary>
    /// Runs one user change under the lock and publishes the board it leaves, from the latest input with Community read now (D3), so a run that
    /// is still going shows the change when it completes. Returns the message to show, or <c>null</c>; nothing escapes.
    /// </summary>
    private string? Change(Func<AppLinks, ApplyResult?> change)
    {
        SessionView? raise;
        string? message = null;
        lock (gate)
        {
            if (disposed || services?.Links is not { } links || latestDiscovery is null || latestInput is null)
                return null;

            ApplyResult? result;
            try
            {
                result = change(links);
            }
            catch (SimRunningException e)
            {
                return e.Message;
            }
            catch (UserDataException e)
            {
                unsaved = true;
                return e.Message;
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return e.Message;
            }

            if (result is { Failed.Count: > 0 })
                message = DescribeFailed(result.Failed);

            // A user action ends the errors of the write path. Only a change that ran clears them: a refusal returns above, unpublished.
            writeError = null;
            resolveError = null;
            var next = view with { Error = ErrorLine };
            raise = Publish(view.Board is null ? next : next with { Board = BuildBoard(latestInput) });
        }

        Raise(raise);
        return message;
    }

    private static string DescribeFailed(IReadOnlyList<(PackageName Package, string Error)> failed)
        => failed.Count == 1
            ? $"Could not change {failed[0].Package.Value}: {failed[0].Error}"
            : $"{failed.Count} packages could not be changed: {failed[0].Error}";
}
