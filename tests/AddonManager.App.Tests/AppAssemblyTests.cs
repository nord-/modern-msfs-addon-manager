using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace AddonManager.App.Tests;

/// <summary>Guards that the app assembly cannot change Community or the GSX folder.</summary>
public class AppAssemblyTests
{
    private static readonly string[] ForbiddenTypes =
    [
        "AddonManager.Infrastructure.GsxProfileCopier"
    ];

    private static readonly string[] LinkWriterTypes =
    [
        "AddonManager.Core.Linking.ILinkFileSystem",
        "AddonManager.Infrastructure.JunctionFileSystem"
    ];

    private static readonly string[] FileSystemTypes =
    [
        "System.IO.File", "System.IO.Directory", "System.IO.FileInfo", "System.IO.DirectoryInfo", "System.IO.FileSystemInfo"
    ];

    private static readonly string[] WritePrefixes = ["Write", "Append", "Create", "Move", "Copy", "Set"];

    private static readonly string[] WriteNames = ["Delete", "Replace", "Open", "OpenHandle", "OpenWrite"];

    [Fact]
    public void The_app_writes_community_only_through_the_apply_path()
    {
        var scan = ScanAssembly();

        Assert.Empty(ForbiddenTypes.Where(scan.Types.Contains).ToList());

        // The link writer is only ever constructed (the default factory); no member of it is called.
        Assert.DoesNotContain(scan.Members, m => LinkWriterTypes.Contains(m.Type) && m.Name != ".ctor");

        var writes = scan.Members.Where(m =>
            FileSystemTypes.Contains(m.Type)
            && (WriteNames.Contains(m.Name) || WritePrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal))));
        Assert.DoesNotContain(writes, _ => true);

        Assert.DoesNotContain(scan.Members, m => m.Type == "System.IO.RandomAccess" && m.Name.StartsWith("Write", StringComparison.Ordinal));

        // No StreamWriter constructor of any overload.
        Assert.DoesNotContain(scan.Members, m => m.Type == "System.IO.StreamWriter" && m.Name == ".ctor");

        Assert.Equal(0, scan.ModuleReferences);
    }

    [Fact]
    public void The_scan_of_the_assembly_finds_what_it_looks_for()
    {
        var scan = ScanAssembly();
        Assert.Contains("AddonManager.Infrastructure.ScanService", scan.Types);

        // A member-level control: a broken member collection would otherwise make the write-API assertions pass vacuously.
        Assert.Contains(scan.Members, m => m is ("System.IO.Directory", "Exists"));
    }

    private sealed record Scan(HashSet<string> Types, List<(string Type, string Name)> Members, int ModuleReferences);

    private static Scan ScanAssembly()
    {
        using var stream = File.OpenRead(typeof(AppServices).Assembly.Location);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        string TypeName(TypeReference t) =>
            md.GetString(t.Namespace) is { Length: > 0 } ns ? $"{ns}.{md.GetString(t.Name)}" : md.GetString(t.Name);

        var types = new HashSet<string>(md.TypeReferences.Select(h => TypeName(md.GetTypeReference(h))));
        var members = new List<(string Type, string Name)>();
        foreach (var h in md.MemberReferences)
        {
            var m = md.GetMemberReference(h);
            if (m.Parent.Kind == HandleKind.TypeReference)
                members.Add((TypeName(md.GetTypeReference((TypeReferenceHandle)m.Parent)), md.GetString(m.Name)));
        }

        return new Scan(types, members, md.GetTableRowCount(TableIndex.ModuleRef));
    }
}
