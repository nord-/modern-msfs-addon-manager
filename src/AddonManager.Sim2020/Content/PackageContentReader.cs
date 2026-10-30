using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;
using AddonManager.Sim2020.Bgl;

namespace AddonManager.Sim2020.Content;

/// <summary>
/// Reads the facts of one Package folder: its change marker, BGL airport records, aircraft containers and thumbnail. Read-only.
/// Bad content gives an empty result; cancellation and an <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
/// when the Package itself can't be accessed propagate, so the scanner can keep the old content.
/// </summary>
public sealed class PackageContentReader(AirportNames airportNames) : IPackageContentReader
{
    private const string ManifestFile = "manifest.json";
    private const string LayoutFile = "layout.json";
    private const string AirplanesPrefix = @"SimObjects\Airplanes\";

    public ChangeMarker? ReadMarker(string packagePath)
    {
        if (!PackageExists(packagePath))
            return null;

        // Only a missing file counts as missing: an access error must propagate, or the scanner would drop cached content as vanished.
        if (Stat(Path.Combine(packagePath, ManifestFile)) is not { } manifest)
        {
            PackageExists(packagePath);
            return null;
        }

        var layout = Stat(Path.Combine(packagePath, LayoutFile));
        return new ChangeMarker(manifest.Ticks, manifest.Size, layout?.Ticks, layout?.Size);
    }

    public bool? HasBgl(string packagePath) => LayoutReader.Read(packagePath)?.Any(IsBgl);

    public PackageContent? Read(ScanTarget target, ChangeMarker marker)
    {
        if (!PackageExists(target.Path))
            return null;

        List<string> files;
        List<string> gsxFiles;
        try
        {
            files = [.. LayoutReader.ListFiles(target.Path).Order(StringComparer.OrdinalIgnoreCase)];
            // Orbx ships profiles (GSX-Profile-EPKK\, GSX-PROFILE\) that its layout.json does not list, so the disk is walked for them too.
            gsxFiles = [.. files.Concat(LayoutReader.ListDiskFiles(target.Path)).Where(IsShippedGsxFile).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (DirectoryNotFoundException)
        {
            // Vanished between the existence check and the listing: null when the folder is gone, an error when it is unreachable.
            if (PackageExists(target.Path))
                throw;
            return null;
        }

        var bgls = ReadBgls(target.Path, files);
        var airplanes = ReadAirplanes(target.Path, files);

        return new PackageContent(
            target.Name,
            PathNormalizer.Normalize(target.Path),
            target.Source,
            marker,
            target.Manifest,
            files.Any(IsBgl),
            bgls.SectionTypes,
            bgls.Unreadable,
            bgls.Airports,
            airplanes.IsAirTraffic,
            airplanes.Configs,
            airplanes.Folders,
            airplanes.EncryptedFolders,
            ThumbnailOf(target.Path, files))
        {
            GsxFiles = gsxFiles,
        };
    }

    /// <summary>A <c>.ini</c> or <c>.py</c> whose name yields an ICAO code (<c>.off</c> files are not shipped profiles), matched on the file's own name.</summary>
    private static bool IsShippedGsxFile(string file)
        => GsxFileName.TryParse(Path.GetFileName(file), out var kind, out var code) && kind != GsxFileKind.Disabled && code != default;

    public DateTimeOffset? NewestWrite(string packagePath)
    {
        if (!PackageExists(packagePath))
            return null;

        var now = DateTime.UtcNow;
        var root = new DirectoryInfo(packagePath);
        var newest = Later(root, now);
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false };
            foreach (var entry in root.EnumerateFileSystemInfos("*", options))
            {
                var time = Later(entry, now);
                if (time > newest)
                    newest = time;
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Vanished while listing: null when the folder is gone, an error when it is unreachable.
            if (PackageExists(packagePath))
                throw;
            return null;
        }

        return new DateTimeOffset(newest, TimeSpan.Zero);
    }

    /// <summary>How far ahead of now a time may be and still count as "just written": a file server whose clock runs a little fast.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    // Extractors restore old write times but the creation time is new. A time slightly ahead of now (a fast file-server clock) counts as
    // now; one far ahead (a zip's local time from a zone ahead, a clock correction) is ignored.
    private static DateTime Later(FileSystemInfo entry, DateTime now)
    {
        var created = entry.CreationTimeUtc;
        var written = entry.LastWriteTimeUtc;
        return new[] { created, written }
            .Where(t => t <= now + ClockSkew)
            .Select(t => t > now ? now : t)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
    }

    /// <summary>False (vanished) when the folder is gone but its parent is there; throws when the parent is gone too, because then the folder is unreachable, not deleted.</summary>
    private static bool PackageExists(string packagePath)
    {
        if (Directory.Exists(packagePath))
            return true;

        var parent = Path.GetDirectoryName(PathNormalizer.Normalize(packagePath));
        if (parent is null || !Directory.Exists(parent))
            throw new DirectoryNotFoundException($"The parent of '{packagePath}' does not exist.");
        return false;
    }

    private static (long Ticks, long Size)? Stat(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var size = info.Length;
            return (info.LastWriteTimeUtc.Ticks, size);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool IsBgl(string file) => file.EndsWith(".bgl", StringComparison.OrdinalIgnoreCase);

    private (List<uint> SectionTypes, int Unreadable, List<AirportRecord> Airports) ReadBgls(string packagePath, List<string> files)
    {
        var sectionTypes = new SortedSet<uint>();
        var airports = new List<AirportRecord>();
        var unreadable = 0;
        foreach (var file in files.Where(IsBgl))
        {
            BglScan scan;
            try
            {
                scan = BglContentReader.Read(Path.Combine(packagePath, file));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                unreadable++;
                continue;
            }

            if (!scan.Readable)
            {
                unreadable++;
                continue;
            }

            sectionTypes.UnionWith(scan.SectionTypes);
            airports.AddRange(scan.Airports.Select(a => a with { Name = airportNames.Resolve(a.Name, a.Code) }));
        }

        return ([.. sectionTypes], unreadable, airports);
    }

    private static (bool IsAirTraffic, List<AircraftConfig> Configs, List<string> Folders, List<string> EncryptedFolders) ReadAirplanes(
        string packagePath, List<string> files)
    {
        var folders = new List<string>();
        var cfgFolders = new List<(string Folder, string File)>();
        foreach (var file in files.Where(f => f.StartsWith(AirplanesPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var parts = file[AirplanesPrefix.Length..].Split('\\');
            if (parts.Length < 2)
                continue;
            if (!folders.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                folders.Add(parts[0]);
            if (parts.Length == 2 && parts[1].Equals("aircraft.cfg", StringComparison.OrdinalIgnoreCase))
                cfgFolders.Add((parts[0], file));
        }

        var configs = new List<AircraftConfig>();
        var airTraffic = false;
        foreach (var (folder, file) in cfgFolders)
        {
            if (AircraftCfgReader.Read(Path.Combine(packagePath, file)) is not { } facts)
                continue;
            if (facts.IsAirTraffic)
            {
                airTraffic = true;
                configs.Clear();
                break;
            }

            configs.Add(new AircraftConfig(folder, facts.BaseContainer, facts.TypeDesignator));
        }

        var encrypted = files.Any(f => f.EndsWith(".fsarchive", StringComparison.OrdinalIgnoreCase))
            ? folders.Where(f => !cfgFolders.Any(c => c.Folder.Equals(f, StringComparison.OrdinalIgnoreCase))).ToList()
            : [];
        return (airTraffic, configs, folders, encrypted);
    }

    private static string? ThumbnailOf(string packagePath, List<string> files)
    {
        var thumbnail = files.FirstOrDefault(f =>
        {
            var parts = f.Split('\\');
            return parts.Length == 3
                && parts[0].Equals("ContentInfo", StringComparison.OrdinalIgnoreCase)
                && parts[2].Equals("Thumbnail.jpg", StringComparison.OrdinalIgnoreCase);
        });
        return thumbnail is null ? null : Path.Combine(PathNormalizer.Normalize(packagePath), thumbnail);
    }
}
