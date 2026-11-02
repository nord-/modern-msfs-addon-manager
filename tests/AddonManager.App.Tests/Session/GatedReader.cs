using AddonManager.Core.Content;
using AddonManager.Sim2020;
using AddonManager.Sim2020.Content;

namespace AddonManager.App.Tests.Session;

/// <summary>
/// The real reader, with <see cref="ReadMarker"/> held while the gate is closed. Every run calls <see cref="ReadMarker"/> for every target,
/// so closing the gate keeps any run in progress (gating <c>Read</c> alone would not block an incremental run over settled Packages).
/// </summary>
public sealed class GatedReader(string? officialPath) : IPackageContentReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly PackageContentReader inner = new(new AirportNames(officialPath));
    private readonly ManualResetEventSlim gate = new(initialState: true);
    private int waiting;

    /// <summary>How many <see cref="ReadMarker"/> calls are waiting on the closed gate right now.</summary>
    public int Waiting => Volatile.Read(ref waiting);

    public void Close() => gate.Reset();

    public void Open() => gate.Set();

    public ChangeMarker? ReadMarker(string packagePath)
    {
        Interlocked.Increment(ref waiting);
        try
        {
            if (!gate.Wait(Timeout))
                throw new TimeoutException("The test never opened the gate.");
        }
        finally
        {
            Interlocked.Decrement(ref waiting);
        }

        return inner.ReadMarker(packagePath);
    }

    public bool? HasBgl(string packagePath) => inner.HasBgl(packagePath);

    public PackageContent? Read(ScanTarget target, ChangeMarker marker) => inner.Read(target, marker);

    public DateTimeOffset? NewestWrite(string packagePath) => inner.NewestWrite(packagePath);
}
