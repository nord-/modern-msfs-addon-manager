using AddonManager.Cli;

if (args is ["status"])
    return StatusCommand.Run(Console.Out);

if (args is ["bgl-dump", ..var rest])
    return BglDumpCommand.Run(rest, Console.Out, Console.Error);

if (args is ["scan", ..var scanArgs])
    return ScanCommand.Run(scanArgs, Console.Out, Console.Error);

if (args is ["export-content", ..var exportArgs])
    return ExportContentCommand.Run(exportArgs, Console.Out, Console.Error);

Console.Error.WriteLine("Usage: AddonManager.Cli status | bgl-dump <file-or-package-folder> [--code <code>] [--out <file>] | scan [--full] [--token-links <file>] | export-content --out <file>");
return 2;
