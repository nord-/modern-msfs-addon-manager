using AddonManager.Infrastructure;

namespace AddonManager.App.Startup;

/// <summary>What a second start found: a running instance in this session was signalled, the app runs in another Windows session, or the instance here runs elevated and cannot be signalled.</summary>
public enum SecondStart { Activated, OtherSession, Elevated }

/// <summary>Startup decisions that need no window: which database to open and whether this start may run.</summary>
public static class AppStartup
{
    private const string DataDirOption = "--data-dir";
    private const int RetryDelayMilliseconds = 250;

    /// <summary><c>--data-dir &lt;folder&gt;</c> gives <c>&lt;folder&gt;\userdata.db</c>; otherwise the app's own database (D10).</summary>
    public static string DatabaseFile(string[] args)
    {
        var at = Array.FindIndex(args, a => string.Equals(a, DataDirOption, StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length
            ? Path.Combine(args[at + 1], Path.GetFileName(AppPaths.DatabaseFile))
            : AppPaths.DatabaseFile;
    }

    /// <summary>
    /// Null when this process is the first instance (<paramref name="guard"/> is then set). Otherwise the running instance is signalled;
    /// when none answers, one retry follows 250 ms later, so a first instance that exited meanwhile lets this one start (D4).
    /// <paramref name="wait"/> replaces that pause in tests.
    /// </summary>
    public static SecondStart? AcquireOrSignal(string name, Action onActivation, out SingleInstance? guard, Action? wait = null)
    {
        guard = SingleInstance.TryAcquire(name, onActivation);
        if (guard is not null)
            return null;
        var first = SingleInstance.TrySignalRunningInstance(name);
        if (first == SignalResult.Signalled)
            return SecondStart.Activated;

        (wait ?? (() => Thread.Sleep(RetryDelayMilliseconds)))();

        guard = SingleInstance.TryAcquire(name, onActivation);
        if (guard is not null)
            return null;
        var second = SingleInstance.TrySignalRunningInstance(name);
        return second switch
        {
            SignalResult.Signalled => SecondStart.Activated,
            // Access denied on either try means the instance is in this session but elevated.
            _ when first == SignalResult.AccessDenied || second == SignalResult.AccessDenied => SecondStart.Elevated,
            _ => SecondStart.OtherSession,
        };
    }
}
