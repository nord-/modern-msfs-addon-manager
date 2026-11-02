using System.Collections.ObjectModel;
using AddonManager.App.Session;
using AddonManager.Core.Content;
using AddonManager.Core.FirstRun;
using AddonManager.Core.Packages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AddonManager.App.ViewModels;

/// <summary>The first-run screen: pick the Packages root when it is unknown, add Library folders per section, see the link coverage, press Start.</summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    public const string NotCoveredTitle = "Not covered (no Library holds them)";
    public const string NotCountedTitle = "Not counted (made by other tools)";

    private readonly IAppSession session;
    private readonly Action<Action> post;
    private readonly Func<string?> pickFolder;
    private readonly List<(string Path, Category Section)> additions = [];
    private int queued;

    [ObservableProperty]
    private bool needsPackagesRoot;

    [ObservableProperty]
    private string? rootRefusal;

    [ObservableProperty]
    private string? libraryRefusal;

    [ObservableProperty]
    private string? startError;

    [ObservableProperty]
    private string? coverage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotCovered))]
    private IReadOnlyList<string> notCovered = [];

    /// <summary>Whether the "Not covered" list has entries; a view binds its visibility to this, not to the list's Count.</summary>
    public bool HasNotCovered => NotCovered.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotCounted))]
    private IReadOnlyList<string> notCounted = [];

    /// <summary>Whether the "Not counted" list has entries; a view binds its visibility to this, not to the list's Count.</summary>
    public bool HasNotCounted => NotCounted.Count > 0;

    public FirstRunViewModel(IAppSession session, Action<Action> post, Func<string?> pickFolder)
    {
        this.session = session;
        this.post = post;
        this.pickFolder = pickFolder;

        Scenery.CollectionChanged += (_, _) => FoldersChanged();
        Aircraft.CollectionChanged += (_, _) => FoldersChanged();
        Other.CollectionChanged += (_, _) => FoldersChanged();

        session.Changed += OnSessionChanged;
        Apply(session.Current);
    }

    public ObservableCollection<string> Scenery { get; } = [];

    public ObservableCollection<string> Aircraft { get; } = [];

    public ObservableCollection<string> Other { get; } = [];

    public string Explanation => "Packages in folders you don't add stay External and are shown locked. A folder another tool also links from, such as Orbx Central's download folder, can be added and switched from both.";

    public string Note => "In this version Libraries can't be changed after you press Start.";

    [RelayCommand]
    private void PickPackagesRoot()
    {
        var folder = pickFolder();
        if (folder is null)
            return;

        RootRefusal = session.TryUsePackagesRoot(folder, out var refusal) ? null : refusal;
    }

    [RelayCommand]
    private void AddScenery() => Add(Scenery);

    [RelayCommand]
    private void AddAircraft() => Add(Aircraft);

    [RelayCommand]
    private void AddOther() => Add(Other);

    [RelayCommand]
    private void RemoveScenery(string folder) => Remove(Scenery, folder);

    [RelayCommand]
    private void RemoveAircraft(string folder) => Remove(Aircraft, folder);

    [RelayCommand]
    private void RemoveOther(string folder) => Remove(Other, folder);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => session.CompleteFirstRunAsync(Combined());

    private bool CanStart() => Scenery.Count + Aircraft.Count + Other.Count > 0;

    private void Add(ObservableCollection<string> section)
    {
        var category = SectionOf(section);
        var folder = pickFolder();
        if (folder is null || session.Paths is not { } paths)
            return;

        var refusal = FirstRunLibraries.Refusal(folder, paths.CommunityPath, paths.PackagesPath);
        LibraryRefusal = refusal;
        if (refusal is not null || section.Any(f => PathNormalizer.AreEqual(f, folder)))
            return;

        // The ordered list is appended first: the collection's change notification recomputes from it.
        additions.Add((folder, category));
        section.Add(folder);
    }

    private void Remove(ObservableCollection<string> section, string folder)
    {
        var existing = section.FirstOrDefault(f => PathNormalizer.AreEqual(f, folder));
        if (existing is null)
            return;

        var category = SectionOf(section);
        additions.RemoveAll(a => a.Section == category && PathNormalizer.AreEqual(a.Path, existing));
        section.Remove(existing);
    }

    private Category SectionOf(ObservableCollection<string> section)
        => ReferenceEquals(section, Scenery) ? Category.Scenery : ReferenceEquals(section, Aircraft) ? Category.Aircraft : Category.Other;

    private IReadOnlyList<LibraryRoot> Combined() => FirstRunLibraries.Combine(additions);

    private void FoldersChanged()
    {
        StartCommand.NotifyCanExecuteChanged();
        Recompute();
    }

    private void Recompute()
    {
        if (NeedsPackagesRoot || session.Current.Phase != SessionPhase.FirstRun)
        {
            Coverage = null;
            NotCovered = [];
            NotCounted = [];
            return;
        }

        var report = LinkCoverage.Measure(session.ReadCommunity(), Combined());
        Coverage = $"{report.Covered} of {report.Counted} links covered";
        NotCovered = [.. report.NotCovered.Select(e => $"{e.Name} ({e.Target})")];
        NotCounted = [.. report.NotCounted.Select(e => e.Name)];
    }

    private void OnSessionChanged(SessionView _)
    {
        // Reads session.Current on the UI thread, never the event's argument; the flag is cleared before the read so a later event posts again.
        if (Interlocked.Exchange(ref queued, 1) != 0)
            return;

        post(() =>
        {
            Interlocked.Exchange(ref queued, 0);
            Apply(session.Current);
        });
    }

    private void Apply(SessionView view)
    {
        NeedsPackagesRoot = view.Phase == SessionPhase.NeedsPackagesRoot;
        StartError = view.Error;
        Recompute();
    }
}
