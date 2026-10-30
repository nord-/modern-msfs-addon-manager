using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Content;

public class AircraftIndexTests
{
    private const PackageSource SimManaged = PackageSource.SimManaged;

    [Fact]
    public void Two_library_packages_providing_one_container_share_its_group_case_insensitively()
    {
        var a = Aircraft("sal1800-cessna-170b-backcountry", cfgs: [("Cessna-170B-Backcountry", null)]);
        var b = Aircraft("sal1800-cessna-super-170b-OPTIONAL", cfgs: [("cessna-170b-backcountry", null)]);

        var group = AircraftIndex.Build([b, a]).Groups["CESSNA-170B-BACKCOUNTRY"];

        Assert.Equal([a, b], group.Providers);
        Assert.Equal("Cessna-170B-Backcountry", group.Container);
    }

    [Fact]
    public void Instruments_never_join_an_aircraft_group()
    {
        var official = Aircraft("asobo-aircraft-c172", airplaneFolders: ["Asobo_C172"], source: SimManaged);
        var instruments = Aircraft("pms50", contentType: "INSTRUMENTS", airplaneFolders: ["Asobo_C172"]);

        Assert.Equal(AircraftRole.None, AircraftIndex.Build([official, instruments]).RoleOf(instruments));
    }

    [Fact]
    public void Group_with_only_a_locked_base_is_spelled_as_the_locked_base()
    {
        var official = Aircraft("asobo-aircraft-c152", airplaneFolders: ["Asobo_C152"], source: SimManaged);
        var livery = Aircraft("c152-livery", cfgs: [("C152_Red", "asobo_c152")]);

        // The livery spells the base in lower case, so only the locked-base rule gives "Asobo_C152".
        Assert.Equal("Asobo_C152", AircraftIndex.Build([livery, official]).Groups["ASOBO_C152"].Container);
    }

    [Fact]
    public void Group_with_a_library_provider_is_spelled_as_the_provider_not_the_locked_base()
    {
        var official = Aircraft("asobo-aircraft-c152", airplaneFolders: ["Asobo_C152"], source: SimManaged);
        var mge = Aircraft("zz-mge-c152", cfgs: [("ASOBO_c152", null)]);

        Assert.Equal("ASOBO_c152", AircraftIndex.Build([official, mge]).Groups["asobo_c152"].Container);
    }

    [Fact]
    public void Group_with_only_a_livery_is_spelled_as_the_liveries_base_container()
    {
        var livery = Aircraft("c152-livery", cfgs: [("C152_Red", "Asobo_C152")]);

        Assert.Equal("Asobo_C152", AircraftIndex.Build([livery]).Groups["asobo_c152"].Container);
    }

    [Fact]
    public void A_sim_managed_provider_stays_a_locked_base_when_a_library_copy_has_its_name()
    {
        var official = Aircraft("asobo-aircraft-c152", airplaneFolders: ["Asobo_C152"], source: SimManaged);
        var copy = Aircraft("asobo-aircraft-c152", cfgs: [("Asobo_C152", null)]) with { Path = @"C:\Lib\asobo-aircraft-c152" };
        var mge = Aircraft("mge-asobo-aircraft-c152", cfgs: [("Asobo_C152", null)]);

        var index = AircraftIndex.Build([official, copy, mge]);

        Assert.Equal(AircraftRole.LockedBase, index.RoleOf(official));
        Assert.Equal(AircraftRole.Mod, index.RoleOf(copy));
        Assert.Equal(AircraftRole.Mod, index.RoleOf(mge));
    }

    [Fact]
    public void Livery_whose_cfgs_chain_through_each_other_joins_only_the_foreign_container()
    {
        var livery = Aircraft("c152-livery", cfgs: [("C152_Red", "Asobo_C152"), ("C152_RedFloat", "C152_Red")]);

        var index = AircraftIndex.Build([livery]);

        Assert.Equal(["Asobo_C152"], index.Groups.Keys);
        Assert.Equal(["Asobo_C152"], index.ContainersOf(livery));
    }

    [Fact]
    public void Encrypted_library_aircraft_provides_its_container()
    {
        var enc = Aircraft("thirdparty-aircraft-x", encryptedFolders: ["TP_X"]);

        var index = AircraftIndex.Build([enc]);

        Assert.Equal(AircraftRole.Provider, index.RoleOf(enc));
        Assert.Equal(["TP_X"], index.ContainersOf(enc));
    }

    [Fact]
    public void Library_airplane_folder_without_cfg_or_archive_provides_nothing()
    {
        var folderOnly = Aircraft("thirdparty-aircraft-y", airplaneFolders: ["TP_Y"]);

        var index = AircraftIndex.Build([folderOnly]);

        Assert.Empty(index.Groups);
        Assert.Equal(AircraftRole.None, index.RoleOf(folderOnly));
    }

    [Fact]
    public void Library_package_providing_an_official_container_is_a_mod()
    {
        var official = Aircraft("asobo-aircraft-c152", airplaneFolders: ["Asobo_C152"], source: SimManaged);
        var mge = Aircraft("mge-asobo-aircraft-c152", cfgs: [("Asobo_C152", null)]);

        var index = AircraftIndex.Build([mge, official]);

        Assert.Equal(AircraftRole.Mod, index.RoleOf(mge));
        Assert.Equal(AircraftRole.LockedBase, index.RoleOf(official));
        Assert.Equal([official], index.Groups["Asobo_C152"].LockedBases);
        Assert.Equal([mge], index.Groups["Asobo_C152"].Providers);
    }

    [Fact]
    public void A_package_is_never_a_mod_of_its_own_external_copy()
    {
        // The same Package as a Library copy and as an External Community copy (scanned under its entry name).
        var library = Aircraft("bagolu-c152-neoflybannertow", cfgs: [("Bagolu_C152", null)]);
        var external = Aircraft("bagolu-c152-neoflybannertow", cfgs: [("Bagolu_C152", null)], source: PackageSource.Community)
            with { Path = @"C:\Spel\Neofly\addons\bagolu-c152-neoflybannertow" };

        var index = AircraftIndex.Build([external, library]);
        var group = index.Groups["Bagolu_C152"];

        Assert.Empty(group.LockedBases);
        Assert.Equal([library, external], group.Providers);
        Assert.Single(group.Providers.Select(p => p.Name.Value).Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(AircraftRole.Provider, index.RoleOf(library));
        Assert.Equal(AircraftRole.Provider, index.RoleOf(external));
    }

    [Fact]
    public void A_same_named_copy_does_not_stop_a_different_community_package_being_a_locked_base()
    {
        var library = Aircraft("lib-aircraft", cfgs: [("Fixed_A", null)]);
        var libraryCopy = Aircraft("lib-aircraft", cfgs: [("Fixed_A", null)], source: PackageSource.Community)
            with { Path = @"C:\Elsewhere\lib-aircraft" };
        var other = Aircraft("fixed-aircraft", cfgs: [("Fixed_A", null)], source: PackageSource.Community);

        var index = AircraftIndex.Build([library, libraryCopy, other]);

        Assert.Equal([other], index.Groups["Fixed_A"].LockedBases);
        Assert.Equal([libraryCopy, library], index.Groups["Fixed_A"].Providers);
        Assert.Equal(AircraftRole.Mod, index.RoleOf(library));
    }

    [Fact]
    public void Community_package_providing_a_container_is_a_locked_base()
    {
        var fixedAircraft = Aircraft("fixed-aircraft", cfgs: [("Fixed_A", null)], source: PackageSource.Community);
        var lib = Aircraft("lib-aircraft", cfgs: [("Fixed_A", null)]);

        var index = AircraftIndex.Build([lib, fixedAircraft]);

        Assert.Equal([fixedAircraft], index.Groups["Fixed_A"].LockedBases);
        Assert.Equal(AircraftRole.Mod, index.RoleOf(lib));
    }

    [Fact]
    public void Sim_managed_package_provides_its_cfg_container_and_its_cfg_less_folders()
    {
        var official = Aircraft("asobo-aircraft-x", cfgs: [("Asobo_X", null)], airplaneFolders: ["Asobo_X", "Asobo_Y"], source: SimManaged);

        Assert.Equal(["Asobo_X", "Asobo_Y"], AircraftIndex.Build([official]).ContainersOf(official));
    }

    [Fact]
    public void Livery_joins_its_base_containers_group_and_variants_are_not_liveries()
    {
        var crj = Aircraft("aerosoft-crj", cfgs: [("Aerosoft_CRJ_900", null), ("Aerosoft_CRJ_900_Variant", "Aerosoft_CRJ_900")]);
        var sas = Aircraft("AS_CRJ_900_SAS_New_EIGEH", cfgs: [("SAS_EIGEH", "Aerosoft_CRJ_900")]);

        var index = AircraftIndex.Build([sas, crj]);

        Assert.Equal(AircraftRole.Livery, index.RoleOf(sas));
        Assert.Equal(AircraftRole.Provider, index.RoleOf(crj));
        Assert.Equal([sas], index.Groups["Aerosoft_CRJ_900"].Liveries);
        Assert.Equal([crj], index.Groups["Aerosoft_CRJ_900"].Providers);
    }

    [Fact]
    public void Variants_including_variants_of_variants_are_provided_by_the_same_package()
    {
        var crj = Aircraft("aerosoft-crj", cfgs: [("Aerosoft_CRJ_900_V2", "Aerosoft_CRJ_900_Variant"), ("Aerosoft_CRJ_900_Variant", "Aerosoft_CRJ_900"), ("Aerosoft_CRJ_900", null)]);

        var index = AircraftIndex.Build([crj]);

        Assert.Equal(["Aerosoft_CRJ_900", "Aerosoft_CRJ_900_V2", "Aerosoft_CRJ_900_Variant"], index.ContainersOf(crj));
        Assert.Equal([crj], index.Groups["Aerosoft_CRJ_900_Variant"].Providers);
        Assert.Equal([crj], index.Groups["Aerosoft_CRJ_900_V2"].Providers);
    }

    [Fact]
    public void Variant_of_an_encrypted_folder_is_provided_by_the_same_package()
    {
        var p = Aircraft("thirdparty-aircraft-x", encryptedFolders: ["TP_X"], cfgs: [("TP_X_Variant", "TP_X")]);
        var livery = Aircraft("tp-x-livery", cfgs: [("Red", "TP_X_Variant")]);

        var index = AircraftIndex.Build([livery, p]);

        Assert.Equal(["TP_X", "TP_X_Variant"], index.ContainersOf(p));
        Assert.Equal(AircraftRole.Provider, index.RoleOf(p));
        Assert.Equal([p], index.Groups["TP_X_Variant"].Providers);
    }

    [Fact]
    public void Variant_of_a_cfg_less_sim_managed_folder_is_provided_by_the_same_package()
    {
        var official = Aircraft("asobo-aircraft-x", cfgs: [("Asobo_X_Variant", "Asobo_X")], airplaneFolders: ["Asobo_X"], source: SimManaged);

        Assert.Equal(["Asobo_X", "Asobo_X_Variant"], AircraftIndex.Build([official]).ContainersOf(official));
    }

    [Fact]
    public void Package_mixing_own_and_foreign_base_containers_is_not_a_livery()
    {
        var mixed = Aircraft("mixed", cfgs: [("Own", null), ("Other_Livery", "Foreign")]);

        var index = AircraftIndex.Build([mixed]);

        Assert.Equal(AircraftRole.Provider, index.RoleOf(mixed));
        Assert.False(index.Groups.ContainsKey("Foreign"));
    }

    [Fact]
    public void Livery_without_installed_aircraft_still_gets_a_group()
    {
        var livery = Aircraft("orphan-livery", cfgs: [("Red", "Missing_Base")], contentType: "LIVERY");

        var group = AircraftIndex.Build([livery]).Groups["missing_base"];

        Assert.Empty(group.Providers);
        Assert.Empty(group.LockedBases);
        Assert.Single(group.Liveries);
    }

    [Fact]
    public void Package_without_cfg_attaches_by_airplane_folder_then_by_dependency_else_none()
    {
        var fbw = Aircraft("flybywire-aircraft-a320-neo", cfgs: [("FlyByWire_A320_NEO", null)]);
        var official = Aircraft("asobo-aircraft-c152", airplaneFolders: ["Asobo_C152"], source: SimManaged);

        // Folder wins over dependency: the dependency points at the C152 group, the folder at the A320 group.
        var cabin = Aircraft("flybywire_A320Neo_Cabin", airplaneFolders: ["flybywire_a320_neo"], dependencies: ["asobo-aircraft-c152"]);
        var navtablet = Aircraft("simflight-C152-interior-navtablet", dependencies: ["ASOBO-aircraft-c152"]);
        var stray = Aircraft("stray-aircraft-addon", dependencies: ["not-installed"]);

        var index = AircraftIndex.Build([stray, navtablet, cabin, official, fbw]);

        Assert.Equal(AircraftRole.Attached, index.RoleOf(cabin));
        Assert.Equal([cabin], index.Groups["FlyByWire_A320_NEO"].Attached);
        Assert.Equal(AircraftRole.Attached, index.RoleOf(navtablet));
        Assert.Equal([navtablet], index.Groups["Asobo_C152"].Attached);
        Assert.Equal(AircraftRole.None, index.RoleOf(stray));
        Assert.Empty(index.ContainersOf(stray));
    }

    [Fact]
    public void Non_aircraft_package_is_not_attached()
    {
        var fbw = Aircraft("flybywire-aircraft-a320-neo", cfgs: [("FlyByWire_A320_NEO", null)]);
        var scenery = Aircraft("some-scenery", airplaneFolders: ["FlyByWire_A320_NEO"], contentType: "SCENERY");

        Assert.Equal(AircraftRole.None, AircraftIndex.Build([fbw, scenery]).RoleOf(scenery));
    }

    [Fact]
    public void Air_traffic_is_not_parsed_for_liveries()
    {
        var traffic = Aircraft("traffic-pack", cfgs: [("Boeing_Red", "Boeing_Base")], isAirTraffic: true);

        var index = AircraftIndex.Build([traffic]);

        Assert.Equal(AircraftRole.AirTraffic, index.RoleOf(traffic));
        Assert.Empty(index.Groups);
    }

    [Fact]
    public void Package_with_several_containers_is_in_each_group_and_lists_them_sorted()
    {
        var p = Aircraft("multi", cfgs: [("B_One", null), ("a_two", null)]);

        var index = AircraftIndex.Build([p]);

        Assert.Equal(["a_two", "B_One"], index.ContainersOf(p));
        Assert.Equal([p], index.Groups["A_TWO"].Providers);
        Assert.Equal([p], index.Groups["b_one"].Providers);
    }

    [Fact]
    public void Mod_role_wins_for_a_package_that_provides_one_group_and_modifies_another()
    {
        var official = Aircraft("asobo-aircraft-c152", airplaneFolders: ["Asobo_C152"], source: SimManaged);
        var both = Aircraft("both", cfgs: [("Own", null), ("Asobo_C152", null)]);

        var index = AircraftIndex.Build([both, official]);

        Assert.Equal(AircraftRole.Mod, index.RoleOf(both));
        Assert.Equal(AircraftRole.Provider, AircraftIndex.Build([both]).RoleOf(both));
    }

    [Fact]
    public void Result_does_not_depend_on_input_order()
    {
        PackageContent[] all =
        [
            Aircraft("asobo-aircraft-c152", airplaneFolders: ["Asobo_C152"], source: SimManaged),
            Aircraft("mge-c152", cfgs: [("asobo_c152", null)]),
            Aircraft("a-c152-mod", cfgs: [("ASOBO_C152", null)]),
            Aircraft("c152-livery", cfgs: [("Red", "Asobo_C152")]),
            Aircraft("aerosoft-crj", cfgs: [("Aerosoft_CRJ_900", null), ("Aerosoft_CRJ_900_Variant", "Aerosoft_CRJ_900")]),
            Aircraft("crj-sas", cfgs: [("SAS", "aerosoft_crj_900")]),
            Aircraft("crj-cabin", airplaneFolders: ["Aerosoft_CRJ_900"]),
            Aircraft("c152-nav", dependencies: ["mge-c152"]),
            Aircraft("orphan-livery", cfgs: [("X", "Gone")]),
            Aircraft("traffic", isAirTraffic: true),
        ];

        var expected = Describe(AircraftIndex.Build(all), all);
        PackageContent[][] permutations =
        [
            [.. all.Reverse()],
            [.. all.Skip(4), .. all.Take(4)],
            [.. all.OrderBy(c => c.Name.Value.Length).ThenBy(c => c.Name.Value)],
        ];

        foreach (var permutation in permutations)
        {
            Assert.Equal(expected, Describe(AircraftIndex.Build(permutation), all));
        }
    }

    private static string Describe(AircraftIndex index, PackageContent[] all)
    {
        static string Names(IEnumerable<PackageContent> list) => string.Join(",", list.Select(c => c.Name.Value));

        var groups = index.Groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g =>
            $"{g.Key}={g.Value.Container}|P:{Names(g.Value.Providers)}|B:{Names(g.Value.LockedBases)}|L:{Names(g.Value.Liveries)}|A:{Names(g.Value.Attached)}");
        var roles = all.OrderBy(c => c.Name.Value).Select(c => $"{c.Name.Value}:{index.RoleOf(c)}:{string.Join("/", index.ContainersOf(c))}");
        return string.Join("\n", groups.Concat(roles));
    }
}
