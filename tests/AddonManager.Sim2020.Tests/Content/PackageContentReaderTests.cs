using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Sim2020.Content;
using AddonManager.Sim2020.Tests.Bgl;

namespace AddonManager.Sim2020.Tests.Content;

public sealed class PackageContentReaderTests : IDisposable
{
    private const string LocPak = """
        { "LocalisationPackage": { "Language": "en-US", "Strings": { "AIRPORTLR.LOWI.name": "Innsbruck" } } }
        """;

    private readonly TempFolder _temp = new();
    private readonly PackageContentReader _reader;

    public PackageContentReaderTests()
    {
        var official = Path.Combine(_temp.Root, "official", "fs-base-genericairports");
        Directory.CreateDirectory(official);
        File.WriteAllText(Path.Combine(official, "en-US.locPak"), LocPak, new UTF8Encoding(false));
        _reader = new PackageContentReader(new AirportNames(Path.Combine(_temp.Root, "official")));
    }

    public void Dispose() => _temp.Dispose();

    private static byte[] Bgl(string shape) => BglBuilder.FromShape(BglBuilder.LoadShape(shape));

    private static byte[] Cfg(string text) => Encoding.UTF8.GetBytes("[GENERAL]\r\n" + text + "\r\n");

    private static ScanTarget Target(string path) => new(new PackageName(Path.GetFileName(path)), path, PackageSource.Library, Manifest.Empty);

    private string Package(string name, params (string Path, byte[] Content)[] files)
    {
        var pkg = _temp.MakePackage(Path.Combine("library", name));
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(pkg, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
        }
        return pkg;
    }

    private static void WriteLayout(string pkg, params string[] paths) =>
        File.WriteAllText(Path.Combine(pkg, "layout.json"),
            "{\"content\":[" + string.Join(",", paths.Select(p => "{\"path\":\"" + p.Replace("\\", "/") + "\"}")) + "]}");

    [Fact]
    public void Newest_write_finds_the_newest_file_below_the_package()
    {
        var pkg = Package("nw1", ("scenery\\a.bgl", [1]), ("scenery\\deep\\b.bgl", [2]));
        MakeAllOld(pkg);
        var stamp = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(Path.Combine(pkg, "scenery", "deep", "b.bgl"), stamp);

        Assert.Equal(new DateTimeOffset(stamp, TimeSpan.Zero), _reader.NewestWrite(pkg));
    }

    private static readonly DateTime Old = new(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Pushes the creation and write time of the folder and everything below it into the past.</summary>
    private static void MakeAllOld(string pkg)
    {
        foreach (var file in Directory.EnumerateFiles(pkg, "*", SearchOption.AllDirectories))
        {
            File.SetCreationTimeUtc(file, Old);
            File.SetLastWriteTimeUtc(file, Old);
        }

        foreach (var dir in Directory.EnumerateDirectories(pkg, "*", SearchOption.AllDirectories).Append(pkg))
        {
            Directory.SetCreationTimeUtc(dir, Old);
            Directory.SetLastWriteTimeUtc(dir, Old);
        }
    }

    [Fact]
    public void Newest_write_counts_a_new_file_whose_write_time_was_restored_old()
    {
        var pkg = Package("nw2", ("scenery\\a.bgl", [1]));
        var file = Path.Combine(pkg, "scenery", "a.bgl");
        MakeAllOld(pkg);
        var created = DateTime.UtcNow.AddMinutes(-5);
        File.SetCreationTimeUtc(file, created);
        File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTimeOffset(created, TimeSpan.Zero), _reader.NewestWrite(pkg));
    }

    [Fact]
    public void Newest_write_ignores_write_times_in_the_future()
    {
        var pkg = Package("nw3", ("a.bgl", [1]));
        var file = Path.Combine(pkg, "a.bgl");
        var created = File.GetCreationTimeUtc(file);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(9));

        var newest = _reader.NewestWrite(pkg)!.Value;

        Assert.True(newest < DateTimeOffset.UtcNow.AddHours(1));
        Assert.True(newest >= new DateTimeOffset(created, TimeSpan.Zero));
    }

    [Fact]
    public void Newest_write_ignores_creation_times_in_the_future()
    {
        var pkg = Package("nw3b", ("a.bgl", [1]));
        var file = Path.Combine(pkg, "a.bgl");
        File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetCreationTimeUtc(file, DateTime.UtcNow.AddHours(9));

        var newest = _reader.NewestWrite(pkg)!.Value;

        Assert.True(newest < DateTimeOffset.UtcNow.AddHours(1));
    }

    [Fact]
    public void Newest_write_counts_a_time_slightly_ahead_as_just_written()
    {
        // A file server whose clock runs two minutes fast: the file was just written, not "in the future".
        var pkg = Package("nw3c", ("a.bgl", [1]));
        MakeAllOld(pkg);
        var before = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(Path.Combine(pkg, "a.bgl"), DateTime.UtcNow.AddMinutes(2));

        var newest = _reader.NewestWrite(pkg)!.Value;

        Assert.True(newest >= before);
        Assert.True(newest <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Newest_write_is_null_when_the_package_is_gone()
    {
        var pkg = Package("nw4", ("a.bgl", [1]));
        Directory.Delete(pkg, true);

        Assert.Null(_reader.NewestWrite(pkg));
    }

    private PackageContent Read(string pkg) => _reader.Read(Target(pkg), _reader.ReadMarker(pkg)!)!;

    [Fact]
    public void Reads_airports_sections_and_thumbnail()
    {
        var pkg = Package("eham-pkg", ("scenery\\eham.bgl", Bgl("eham-bbd")), ("ContentInfo\\x-y\\Thumbnail.jpg", [1]));
        var c = _reader.Read(Target(pkg), _reader.ReadMarker(pkg)!)!;
        Assert.True(c.HasBgl);
        Assert.Contains(c.Airports, a => a.Code.Value == "EHAM");
        Assert.Contains(0x03u, c.BglSectionTypes);
        Assert.EndsWith(@"ContentInfo\x-y\Thumbnail.jpg", c.ThumbnailPath);
        Assert.True(File.Exists(c.ThumbnailPath));
    }

    [Fact]
    public void Section_types_are_merged_sorted_and_distinct()
    {
        var one = new BglBuilder().Section(0x65).Subsection(BglBuilder.Record(0x01, [0])).Section(0x03).Subsection().Build();
        var two = new BglBuilder().Section(0x03).Subsection().Section(0x20).Subsection(BglBuilder.Record(0x01, [0])).Build();
        var c = Read(Package("p", ("a.bgl", one), ("b.bgl", two)));
        Assert.Equal([0x03u, 0x20u, 0x65u], c.BglSectionTypes);
    }

    [Fact]
    public void Counts_unreadable_bgls_and_keeps_going()
    {
        var c = Read(Package("p", ("a.bgl", BglBuilder.Broken("random")), ("b.bgl", Bgl("yalr1"))));
        Assert.Equal(1, c.UnreadableBglCount);
        Assert.Single(c.Airports);
    }

    [Fact]
    public void A_bgl_that_cannot_be_opened_counts_as_unreadable_and_the_scan_continues()
    {
        var pkg = Package("p", ("a.bgl", Bgl("eham-bbd")), ("b.bgl", Bgl("yalr1")));
        using var held = new FileStream(Path.Combine(pkg, "a.bgl"), FileMode.Open, FileAccess.Read, FileShare.None);
        var c = Read(pkg);
        Assert.Equal(1, c.UnreadableBglCount);
        Assert.Equal("YALR1", Assert.Single(c.Airports).Code.Value);
    }

    [Fact]
    public void Airport_names_are_resolved_through_the_locpak()
    {
        var bgl = new BglBuilder().Section(0x03)
            .Subsection(BglBuilder.Airport("LOWI", "TT:AIRPORTLR.LOWI.name", 47.26, 11.34, 0x44)).Build();
        var c = Read(Package("p", ("a.bgl", bgl)));
        Assert.Equal("Innsbruck", Assert.Single(c.Airports).Name);
    }

    [Fact]
    public void Path_is_normalised()
    {
        var pkg = Package("p", ("a.bgl", Bgl("yalr1")));
        var c = _reader.Read(Target(pkg + @"\..\p\"), _reader.ReadMarker(pkg)!)!;
        Assert.Equal(PathNormalizer.Normalize(pkg), c.Path);
    }

    [Fact]
    public void Air_traffic_stops_cfg_parsing()
    {
        var pkg = Package("fsltl-traffic-base",
            (@"SimObjects\Airplanes\A\aircraft.cfg", Cfg("isAirTraffic=1")),
            (@"SimObjects\Airplanes\B\aircraft.cfg", Cfg("base_container=\"..\\X\"")));
        var c = Read(pkg);
        Assert.True(c.IsAirTraffic);
        Assert.Empty(c.AircraftConfigs);
    }

    [Fact]
    public void Air_traffic_found_after_other_cfgs_discards_them()
    {
        var pkg = Package("p",
            (@"SimObjects\Airplanes\A\aircraft.cfg", Cfg("base_container=\"..\\X\"")),
            (@"SimObjects\Airplanes\B\aircraft.cfg", Cfg("isAirTraffic=1")));
        var c = Read(pkg);
        Assert.True(c.IsAirTraffic);
        Assert.Empty(c.AircraftConfigs);
    }

    [Fact]
    public void Containers_come_from_cfg_folders_and_airplane_folders_from_any_file()
    {
        var pkg = Package("p",
            (@"SimObjects\Airplanes\Asobo_C152\aircraft.cfg", Cfg("")),
            (@"SimObjects\Airplanes\Enc\cockpit.cfg", [1]),
            (@"SimObjects\jxgxgoqo.fsarchive", [1]));
        var c = Read(pkg);
        Assert.Equal([new AircraftConfig("Asobo_C152", null)], c.AircraftConfigs);
        Assert.Equal(["Asobo_C152", "Enc"], c.AirplaneFolders.Order());
        Assert.Equal(["Enc"], c.EncryptedAirplaneFolders);
    }

    [Fact]
    public void Folders_without_cfg_are_not_encrypted_when_the_package_has_no_archive()
    {
        var c = Read(Package("p", (@"SimObjects\Airplanes\Part_Common\model.bin", [1])));
        Assert.Equal(["Part_Common"], c.AirplaneFolders);
        Assert.Empty(c.EncryptedAirplaneFolders);
    }

    [Fact]
    public void Archive_in_the_package_root_marks_cfgless_folders_encrypted()
    {
        var c = Read(Package("p", (@"SimObjects\Airplanes\Enc\model.bin", [1]), ("abc.fsarchive", [1])));
        Assert.Equal(["Enc"], c.EncryptedAirplaneFolders);
    }

    [Fact]
    public void Inaccessible_package_folder_throws_instead_of_reading_empty()
    {
        var pkg = Package("p", ("a.bgl", Bgl("yalr1")));
        WriteLayout(pkg, "a.bgl");
        var marker = _reader.ReadMarker(pkg)!;
        using (DenyRead(pkg))
        {
            Assert.Throws<UnauthorizedAccessException>(() => _reader.Read(Target(pkg), marker));
        }
    }

    [Fact]
    public void Inaccessible_package_root_without_layout_throws_from_the_fallback_walk()
    {
        var pkg = Package("p", ("a.bgl", Bgl("yalr1")));
        var marker = _reader.ReadMarker(pkg)!;
        using (DenyRead(pkg))
        {
            Assert.Throws<UnauthorizedAccessException>(() => _reader.Read(Target(pkg), marker));
        }
    }

    [Fact]
    public void Inaccessible_manifest_throws_from_the_marker_instead_of_reading_as_vanished()
    {
        var pkg = Package("p");
        using (DenyRead(pkg, FileSystemRights.ListDirectory | FileSystemRights.ReadData | FileSystemRights.ReadAttributes))
        {
            Assert.Throws<UnauthorizedAccessException>(() => _reader.ReadMarker(pkg));
        }
    }

    [Fact]
    public void Unreadable_layout_throws_from_read_instead_of_falling_back_to_a_walk()
    {
        // A file ACL can't make the marker's metadata stat fail (the folder listing supplies it), so this covers the layout.json open.
        var pkg = Package("p", ("a.bgl", Bgl("yalr1")));
        WriteLayout(pkg, "a.bgl");
        var marker = _reader.ReadMarker(pkg)!;
        using (DenyRead(Path.Combine(pkg, "layout.json"), FileSystemRights.ReadData))
        {
            Assert.Throws<UnauthorizedAccessException>(() => _reader.Read(Target(pkg), marker));
        }
    }

    [Fact]
    public void Marker_changes_with_layout_and_is_null_without_manifest()
    {
        var pkg = Package("p");
        var bare = _reader.ReadMarker(pkg)!;
        Assert.Null(bare.LayoutTicks);
        Assert.Null(bare.LayoutSize);

        WriteLayout(pkg, "a.bgl");
        var withLayout = _reader.ReadMarker(pkg)!;
        Assert.NotNull(withLayout.LayoutSize);
        Assert.NotEqual(bare, withLayout);

        File.WriteAllText(Path.Combine(pkg, "manifest.json"), "{ \"a\": 1 }");
        Assert.NotEqual(withLayout.ManifestSize, _reader.ReadMarker(pkg)!.ManifestSize);

        File.Delete(Path.Combine(pkg, "manifest.json"));
        Assert.Null(_reader.ReadMarker(pkg));
    }

    [Fact]
    public void Has_bgl_follows_the_layout_and_is_null_without_one()
    {
        var pkg = Package("p");
        Assert.Null(_reader.HasBgl(pkg));
        WriteLayout(pkg, "readme.txt");
        Assert.False(_reader.HasBgl(pkg));
        WriteLayout(pkg, "readme.txt", "scenery\\A.BGL");
        Assert.True(_reader.HasBgl(pkg));
    }

    [Fact]
    public void Missing_parent_throws_instead_of_vanishing()
    {
        var pkg = Package("p");
        var marker = _reader.ReadMarker(pkg)!;
        Directory.Delete(Path.Combine(_temp.Root, "library"), true);
        Assert.Throws<DirectoryNotFoundException>(() => _reader.ReadMarker(pkg));
        Assert.Throws<DirectoryNotFoundException>(() => _reader.Read(Target(pkg), marker));
    }

    [Fact]
    public void Vanished_folder_reads_null()
    {
        var pkg = Package("p");
        var marker = _reader.ReadMarker(pkg)!;
        Directory.Delete(pkg, true);
        Assert.Null(_reader.ReadMarker(pkg));
        Assert.Null(_reader.Read(Target(pkg), marker));
    }

    [Fact]
    public void Reading_changes_nothing_on_disk()
    {
        var pkg = Package("p", ("a.bgl", Bgl("eham-bbd")), (@"SimObjects\Airplanes\A\aircraft.cfg", Cfg("")), ("ContentInfo\\x\\Thumbnail.jpg", [1]));
        var library = Path.Combine(_temp.Root, "library");
        var before = Snapshot(library);
        Read(pkg);
        Assert.Equal(before, Snapshot(library));
    }

    [Fact]
    public void The_package_reader_passes_the_type_designator_on()
    {
        var pkg = Package("p", (@"SimObjects\Airplanes\A\aircraft.cfg", Cfg("icao_type_designator = \"C152\"")));
        Assert.Equal([new AircraftConfig("A", null, "C152")], Read(pkg).AircraftConfigs);
    }

    [Fact]
    public void Lower_case_layout_paths_are_found()
    {
        var pkg = Package("livery", (@"SimObjects\Airplanes\X_Livery\aircraft.cfg", Cfg("base_container=\"..\\Y\"")));
        WriteLayout(pkg, @"simobjects\airplanes\X_Livery\aircraft.cfg");
        Assert.Equal([new AircraftConfig("X_Livery", "Y")], Read(pkg).AircraftConfigs);
    }

    private static List<string> Snapshot(string folder) =>
        [.. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            // Folder timestamps are skipped: NTFS refreshes a folder's cached times in its parent's listing lazily, even when nothing wrote.
            .Select(e => e is FileInfo f ? $"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}|{f.CreationTimeUtc.Ticks}" : e.FullName)
            .Order()];

    /// <summary>An inheritable deny of the given rights for the current user on a folder or file; removed on dispose.</summary>
    private static IDisposable DenyRead(string path, FileSystemRights rights = FileSystemRights.ListDirectory | FileSystemRights.ReadData)
    {
        var isFile = File.Exists(path);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, rights,
            isFile ? InheritanceFlags.None : InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny);

        void Change(Action<FileSystemSecurity> edit)
        {
            if (isFile)
            {
                var file = new FileInfo(path);
                var security = file.GetAccessControl();
                edit(security);
                file.SetAccessControl(security);
            }
            else
            {
                var folder = new DirectoryInfo(path);
                var security = folder.GetAccessControl();
                edit(security);
                folder.SetAccessControl(security);
            }
        }

        Change(security => security.AddAccessRule(rule));
        return new Undo(() => Change(security => security.RemoveAccessRule(rule)));
    }

    private sealed class Undo(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
