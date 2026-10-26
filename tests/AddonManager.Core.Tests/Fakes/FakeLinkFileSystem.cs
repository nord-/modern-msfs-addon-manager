using AddonManager.Core.Linking;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Fakes;

internal enum FsCallKind { Create, Remove }

internal sealed record FsCall(FsCallKind Kind, string LinkPath, string? Target);

/// <summary>Records successful link changes, mutates a <see cref="FakeCommunity"/> and enforces the real guards.</summary>
internal sealed class FakeLinkFileSystem(FakeCommunity community) : ILinkFileSystem
{
    private readonly List<(string Name, string? Target, Exception? Error)> failures = [];

    public List<FsCall> Calls { get; } = [];

    /// <summary>
    /// Makes creating a link named <paramref name="name"/> fail (only to <paramref name="target"/>, when given),
    /// with <paramref name="error"/> or an <see cref="IOException"/>.
    /// </summary>
    public void FailOn(string name, string? target = null, Exception? error = null) => failures.Add((name, target, error));

    public void CreateJunction(string linkPath, string targetPath)
    {
        var name = Path.GetFileName(linkPath);
        var failure = failures.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)
                                                   && (f.Target is null || PathNormalizer.AreEqual(f.Target, targetPath)));
        if (failure.Name is not null)
            throw failure.Error ?? new IOException($"Simulated failure for '{name}'.");
        if (community.Has(name))
            throw new IOException($"'{name}' already exists.");

        community.AddJunction(name, targetPath);
        Calls.Add(new FsCall(FsCallKind.Create, linkPath, targetPath));
    }

    public void RemoveLink(string linkPath)
    {
        var name = Path.GetFileName(linkPath);
        if (community.Get(name) is not { IsLink: true })
            throw new InvalidOperationException($"'{name}' is not a link.");

        community.Remove(name);
        Calls.Add(new FsCall(FsCallKind.Remove, linkPath, null));
    }
}
