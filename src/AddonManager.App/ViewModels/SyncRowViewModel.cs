using AddonManager.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AddonManager.App.ViewModels;

/// <summary>One row of the sync dialog: what changed outside the app, and whether to keep it or restore the app's stored state.</summary>
public sealed partial class SyncRowViewModel : ObservableObject
{
    [ObservableProperty]
    private SyncChoice choice;

    public SyncRowViewModel(SyncRow row, string displayName)
    {
        Row = row;
        DisplayName = displayName;
        Choice = row.DefaultChoice;
        Change = row.Reason switch
        {
            SyncReason.LinkAppeared => "Linked outside the app",
            SyncReason.LinkDisappeared => "Link removed outside the app",
            SyncReason.LinkRetargeted => $"Link now points to {row.NewTarget}",
            SyncReason.Moved => $"Off in the app, but linked; moved to {row.NewTarget}",
            SyncReason.OnButNotLinked => "On in the app, but not linked",
            SyncReason.OffButLinked => "Off in the app, but linked",
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };
    }

    public SyncRow Row { get; }

    public string DisplayName { get; }

    public string Change { get; }

    /// <summary>False when only Keep Community is meaningful (the name is held by a link the app doesn't manage).</summary>
    public bool CanRestore => Row.Restorable;
}
