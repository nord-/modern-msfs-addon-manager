using AddonManager.Cli;

if (args is ["status"])
    return StatusCommand.Run(Console.Out);

Console.Error.WriteLine("Usage: AddonManager.Cli status");
return 2;
