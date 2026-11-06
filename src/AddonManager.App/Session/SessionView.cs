using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Sync;

namespace AddonManager.App.Session;

public enum SessionPhase { Starting, NeedsPackagesRoot, FirstRun, Scanning, Ready, Failed }

/// <summary>The rows the sync dialog offers and the board they were computed against (it names the cards the rows group under).</summary>
public sealed record SyncPrompt(IReadOnlyList<SyncRow> Rows, Board Board);

/// <summary>
/// What the window shows. <c>Board</c> is set from the first Ready on; <c>Progress</c> is set while a run reads content; <c>Error</c> is the
/// banner's error line (Ready) or the message of the error screen (Failed); <c>CanRetry</c> is true for a Failed that came from a run.
/// <c>Sync</c> is the open sync dialog; <c>SyncHeldBack</c> is true while rows or repairs wait for the sim to exit.
/// </summary>
public sealed record SessionView(
    SessionPhase Phase, Board? Board, ScanProgress? Progress, string? Error, bool CanRetry = false, SyncPrompt? Sync = null, bool SyncHeldBack = false);
