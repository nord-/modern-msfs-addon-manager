using AddonManager.Core.Packages;

namespace AddonManager.Core.Cards;

/// <summary>The choices a switch sets, and the Packages it leaves alone (locked, Not found, Library not found or name taken) so the UI can say why.</summary>
public sealed record SwitchPlan(IReadOnlyDictionary<PackageName, bool> Choices, IReadOnlyList<CardPackage> Skipped);

/// <summary>Turns a switch on a card into choices; the UI passes <see cref="SwitchPlan.Choices"/> to <c>ChangeApplier.SetChoices</c>.</summary>
public static class CardSwitch
{
    /// <summary>Sets every switchable Package of the card.</summary>
    public static SwitchPlan All(Card card, bool on) => Plan(card.Packages, on);

    /// <summary>Whether the card has a Package the switch can set.</summary>
    public static bool HasSwitchable(Card card) => card.Packages.Any(p => p.CanSwitch);

    /// <summary>The switch position: on when the card has a switchable Package and every switchable Package is on; Packages it cannot set don't count.</summary>
    public static bool IsOn(Card card) => HasSwitchable(card) && card.Packages.Where(p => p.CanSwitch).All(p => p.On);

    /// <summary>The click on the switch: sets every switchable Package to the opposite of the switch position.</summary>
    public static SwitchPlan Toggle(Card card) => All(card, !IsOn(card));

    /// <summary>Sets one Package; if it cannot be switched, the plan holds it as skipped and no choice.</summary>
    public static SwitchPlan One(Card card, PackageKey package, bool on) => Plan(card.Packages.Where(p => p.Kind == package.Kind && p.Name == package.Name), on);

    private static SwitchPlan Plan(IEnumerable<CardPackage> packages, bool on)
    {
        var list = packages.ToList();
        return new SwitchPlan(
            list.Where(p => p.CanSwitch).ToDictionary(p => p.Name, _ => on),
            [.. list.Where(p => !p.CanSwitch).OrderBy(p => p.Name.Value, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase)]);
    }
}
