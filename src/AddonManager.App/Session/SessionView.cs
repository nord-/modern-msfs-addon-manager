using AddonManager.Core.Cards;
using AddonManager.Core.Content;

namespace AddonManager.App.Session;

public enum SessionPhase { Starting, NeedsPackagesRoot, FirstRun, Scanning, Ready, Failed }

/// <summary>
/// What the window shows. <c>Board</c> is set from the first Ready on; <c>Progress</c> is set while a run reads content; <c>Error</c> is the
/// banner's error line (Ready) or the message of the error screen (Failed); <c>CanRetry</c> is true for a Failed that came from a run.
/// </summary>
public sealed record SessionView(SessionPhase Phase, Board? Board, ScanProgress? Progress, string? Error, bool CanRetry = false);
