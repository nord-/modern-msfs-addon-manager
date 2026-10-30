using AddonManager.Cli;

if (args is ["status", ..var statusArgs])
    return StatusCommand.Run(statusArgs, Console.Out, Console.Error);

if (args is ["bgl-dump", ..var rest])
    return BglDumpCommand.Run(rest, Console.Out, Console.Error);

if (args is ["scan", ..var scanArgs])
    return ScanCommand.Run(scanArgs, Console.Out, Console.Error);

if (args is ["cards", ..var cardsArgs])
    return CardsCommand.Run(cardsArgs, Console.Out, Console.Error);

if (args is ["export-content", ..var exportArgs])
    return ExportContentCommand.Run(exportArgs, Console.Out, Console.Error);

Console.Error.WriteLine("Usage: AddonManager.Cli status [--library <path>[=sections]] | bgl-dump <file-or-package-folder> [--code <code>] [--out <file>] | scan [--full] [--token-links <file>] [--library <path>[=sections]] | cards [--full] [--packages] [--library <path>[=sections]] | export-content --out <file> [--library <path>[=sections]]");
return 2;
