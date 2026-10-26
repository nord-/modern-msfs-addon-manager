# Content Scan Implementation Plan (plan 2 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A tested content pass that reads what every Package contains (BGL airport records, `aircraft.cfg`, `layout.json`, thumbnail path), caches it in SQLite, rescans incrementally and on file changes, and turns it into Categories, Package–Airport links, Airports and an aircraft index — the data plans 3–6 build on — plus CLI commands to check it against the real machine.

**Architecture:** Facts are read by sim-specific readers in `AddonManager.Sim2020` (BGL, `layout.json`, `aircraft.cfg`) behind `IPackageContentReader`; they are cached as one JSON document per Package in SQLite (`AddonManager.Infrastructure`); every derived result (Category, Airport links, Airports, aircraft roles) is recomputed in memory by pure rules in `AddonManager.Core.Content`. The scan is orchestrated by `ContentScanner` (Core, incremental by change marker) and `ScanService` (Infrastructure, discovery + content, single-flight), with `ContentWatcher` signalling when a rescan is needed.

**Tech Stack:** C# / .NET 10, xUnit, System.Text.Json, `Microsoft.Data.Sqlite` (raw SQL, no ORM), `FileSystemWatcher`.

**Spec:** `docs/superpowers/specs/2026-10-25-mvp-design.md` (sections 6.4–6.6, 7.3, 11 (cache), 13). Vocabulary: `GLOSSARY.md`. Decisions from the 2026-10-26 grilling: `ANALYS.md`, "Beslut för plan 2".

## Roadmap

| Plan | Delivers | Spec sections |
|---|---|---|
| 1. Link engine (done, PR #2) | Discovery, Community classification, junctions, user state, reconciliation, apply path and pending set, install location, Library proposal, `status` CLI | 5, 6.1–6.3, 6.8, 7.1, 7.4, 11 (user data) |
| **2. Content scan** (this) | BGL airport records, generic and encrypted airports, Airport linking rules, containers and Liveries, Categories, SQLite cache, two-pass and incremental scan, watchers | 6.4–6.6, 7.3, 11 (cache) |
| 3. Addons and Catalogs | Initial Addons and anchors, Catalog trees per tab, Asset pack test, Uses, warnings, computed Mixed, Category override per Addon | 6.3–6.7 |
| 4. GSX | Profile reading, status, shipped profiles, drag-and-drop install | 8 |
| 5. UI design + WPF app | Mockups, shell, tabs, properties, Community view, Settings, overlays; wires `ContentWatcher.RescanNeeded` → `ScanService` and F5 → full rescan; wires `ContentWatcher.CommunityChanged` → runtime reconcile with own-change suppression and the sync dialog ("don't ask again this session"); recreates the watcher after each scan with the reachable Libraries | 7.4 (runtime changes), 10, 12 |
| 6. Map, search, release | Map, deep links, update notice, single-file release (incl. SQLite native library self-extraction) | 9, 10, C3 |

## Global Constraints

- .NET 10 (`net10.0`; Windows-specific projects `net10.0-windows`); nothing that blocks a move to .NET 11.
- **Scanning is strictly read-only.** Files in Libraries, Community and the Official folder are opened with `FileAccess.Read` and `FileShare.ReadWrite | FileShare.Delete` (a `FileStream`, never `File.ReadAllText`/`ReadAllBytes`, which share only `Read` and can make an installer's write fail); nothing there is ever created, changed or deleted. The only files this plan writes are the scan cache and the CLI's explicit `--out` and `--token-links` files.
- Package names, container names and Airport codes are compared case-insensitively (`StringComparer.OrdinalIgnoreCase`).
- MIT license. Dave's GSX Profile Manager **and Little Navmap / atools** are GPL-3.0: reference only, no copied or translated code. The BGL format source is the documented format (FSDeveloper wiki, "BGL File Format"), confirmed against real files by `bgl-dump` (Task 4).
- No official sim files and no third-party BGL bytes are committed. Committed test data is synthetic (built in code) or facts (structure dumps, codes, names, positions).
- Sim-specific reading (BGL, `aircraft.cfg`, `layout.json`, Official folder layout) lives in `AddonManager.Sim2020` behind Core interfaces (spec C6).
- Scan cache: `%LOCALAPPDATA%\ModernMsfsAddonManager\scan-cache.db` (`AppPaths.CacheDirectory`). Disposable: deleting it only costs a rescan.
- CRLF line endings. Write any file containing backslashes with the Write/Edit tools, never a Bash heredoc (the Bash tool turns `\\` into `\`).
- The NuGet `.gitignore` rule `**/[Pp]ackages/*` hides folders named `Packages`; add an exception if a new file lands under one.
- Conventional commit messages (`feat:`, `test:`, `docs:`), no AI attribution.

## Review Focus

1. **Corrupt, truncated or encrypted BGL files** (wrong magic, offsets past the end, a record size below 6): the file counts as unreadable, the reader never throws, and the scan continues. Tests in Tasks 3 and 6.
2. **A Library on an unplugged drive during a content scan:** cached content under it is kept, so its Airport links and aircraft don't disappear while it is offline. Test in Task 15.
3. **A Package that vanishes or becomes unreadable mid-scan** (deleted by an installer between marker and read, access denied): no crash; a vanished Package leaves the cache, an unreadable one keeps its old cached content. Test in Task 15.
4. **A corrupt, foreign-version or locked cache file** (garbage bytes, an older `user_version`, a second app instance holding a write lock): a corrupt or old file is deleted and rebuilt; a locked one makes that cache instance work in memory for the rest of the session instead of failing. Tests in Task 14.
5. **A burst of file changes or a watcher overflow during a large install:** at most one scan runs at a time and a burst gives one rescan, never a pile of concurrent ones. Tests in Tasks 17 and 18.

## Content rules

These apply across Tasks 1–22. Each task restates the ones it implements.

- **C1 Read-only.** See Global Constraints.
- **C2 Readers never throw for bad content.** Unreadable content (a corrupt BGL, an invalid `aircraft.cfg` or `layout.json`) yields an "unreadable" or empty result. Two things propagate: cancellation, and an `IOException` or `UnauthorizedAccessException` when the Package itself can't be accessed (its folder, `manifest.json` or `layout.json`), so the scanner can keep the old content instead of caching an empty one (Tasks 8 and 15).
- **C3 Airport codes.** An Airport code is 3–5 ASCII letters or digits, stored upper-case. Anything else (empty, too short or long, other characters) is ignored wherever it is read.
- **C4 Counted records.** A Package's airport records count for rule 1 unless the Package is excluded: more distinct Airport codes than `AirportThreshold` (default 10), or its name matches an exclusion pattern. An opted-in Package is never excluded. Records of a Package excluded **by pattern** are ignored everywhere: they are no known codes and create no Airports (spec 6.5: voloport, neofly and points of interest are not airports). Records of a Package excluded **only by the threshold** (generic airports, navigation data) still supply known codes, names and positions.
- **C5 Identity.** Content is keyed by normalised path (`PathNormalizer.Normalize`); user data is keyed by `PackageName`, so a setting for a name applies to every content with that name (all Duplicate copies). `OptedIn` and `RemovedAirportLinks` apply to every source, Sim-managed included; `ManualAirportLinks` apply only to non-Sim-managed contents (Sim-managed Packages get their code from records or their name).
- **C6 Cache facts, derive the rest.** The cache stores what was read from disk. Categories, Airport links, Airports, Built-in replacements and the aircraft index are recomputed in memory on every analysis (spec 11 is updated in Task 14).
- **C7 Order never decides.** Every rule result is independent of the order Packages were scanned in. Every list a rule returns is sorted by Package name, then path (`OrdinalIgnoreCase`), then Airport code where it has one; a container's display name is its spelling in the first provider (else first Livery or Attached Package) in that order.
- **C8 Every copy is content.** Inactive Duplicate copies are scanned and analysed like active ones (they get links and aircraft roles); plan 3 shows and switches only the active copy. Tests that count Packages count distinct names.

## Tracks

Independent chains run as parallel tracks (side tracks reset onto the main-track commit they consume):

| Track | Tasks | Consumes |
|---|---|---|
| Main | 1 → 2 → 9 → 10 → 11 → 12 → 13 → 15 → 16 → 17 → 18 | — |
| BGL (side) | 3 → 7 → **4 (Rickard)** → 5 → 6 → 8 | Task 1 (start right after 1) |
| Cache (side) | 14 | Task 9 (`JsonConverters`) |
| Finish | merge BGL and Cache tracks → 19 → 20 → **21 (Rickard)** → 22 | all tracks |

Start the BGL track right after Task 1: Task 7 doesn't need the dumps, so it runs while Task 4 waits for Rickard at the sim machine. Task 4 gates Tasks 5, 6 and 8, and so the Finish track (Task 19 consumes `PackageContentReader` from Task 8 and `SqliteScanCache` from Task 14). The main track never waits for Task 4.

**Handover to the sim machine** (the dev machine has no MSFS; side-track commits live in local agent worktrees):
- Before Task 4: the controller pushes the BGL track head (after Tasks 3 and 7) to `origin/plan-2/bgl-track` and tells Rickard to run `git fetch` and `git switch plan-2/bgl-track` on the sim machine. Rickard commits the dumps there and pushes. The Task 5 agent runs `git fetch origin` and resets onto `origin/plan-2/bgl-track`.
- Before Task 21: the controller pushes the merged branch after Task 20 to `origin/docs/plan-2-content-scan` (or the implementation branch in use) and names it to Rickard; Rickard pushes his Task 21 commit; the Task 22 agent fetches and resets onto it.
- After the plan is done, `plan-2/bgl-track` is deleted on origin.

---

## File structure

```
src/
  AddonManager.Core/Content/
    AirportCode.cs, GeoPoint.cs, AirportRecord.cs, AircraftConfig.cs, PackageSource.cs,
    ChangeMarker.cs, PackageContent.cs, ScanTarget.cs, IPackageContentReader.cs, IScanCache.cs,
    Category.cs, CategoryRules.cs, ContentSettings.cs, AirportLinker.cs, AirportCatalog.cs,
    AircraftIndex.cs, ContentAnalyzer.cs, ContentScanner.cs
  AddonManager.Core/State/UserState.cs                     # + Content settings
  AddonManager.Sim2020/Bgl/
    BglFile.cs, AirportCodeCodec.cs, AirportRecordReader.cs, BglShape.cs, BglContentReader.cs
  AddonManager.Sim2020/Content/
    LayoutReader.cs, AircraftCfgReader.cs, PackageContentReader.cs
  AddonManager.Infrastructure/
    JsonConverters.cs, ContentJson.cs, SqliteScanCache.cs, ScanTargetCollector.cs,
    ScanService.cs, ContentWatcher.cs
  AddonManager.Cli/
    BglDumpCommand.cs, ScanCommand.cs, ExportContentCommand.cs
tests/
  AddonManager.Core.Tests/Content/…, Scenarios/ContentScenarioTests.cs, CollisionReportTests.cs,
    ContentFixtureLoader.cs, Fixtures/rickard-content.json
  AddonManager.Sim2020.Tests/Bgl/BglBuilder.cs, Bgl/…Tests.cs, Content/…Tests.cs,
    Integration/LocalInstallTests.cs, Fixtures/bgl-shapes/*.json
  AddonManager.Infrastructure.Tests/SqliteScanCacheTests.cs, ScanTargetCollectorTests.cs,
    ScanServiceTests.cs, ContentWatcherTests.cs
```

---

### Task 1: Content model and Airport codes

**Files:**
- Create: `src/AddonManager.Core/Content/AirportCode.cs`, `GeoPoint.cs`, `AirportRecord.cs`, `AircraftConfig.cs`, `PackageSource.cs`, `ChangeMarker.cs`, `PackageContent.cs`, `ScanTarget.cs`, `IPackageContentReader.cs`, `IScanCache.cs`
- Modify: `CLAUDE.md` (status "Plan 1 (link engine) in progress" → "Plan 2 (content scan) in progress"); `ANALYS.md`, section "Beslut för plan 2": add the approved "Calls I made" at the end of this plan, dated with the plan's approval date, in Swedish; spec `docs/superpowers/specs/2026-10-25-mvp-design.md` section 6.5 (rule 2 only for Packages that aren't Sim-managed; manual links only for Packages that aren't Sim-managed; opting in overrides both the threshold and a pattern; the overlapping `layout.json` signal is dropped; pattern-excluded records create no Airports) and 6.6 (encrypted aircraft in any source provide their containers)
- Test: `tests/AddonManager.Core.Tests/Content/AirportCodeTests.cs`

**Interfaces:**
- Consumes: `PackageName`, `Manifest`.
- Produces:
  - `public readonly struct AirportCode : IEquatable<AirportCode>` with `string Value` (upper-case), `static bool TryParse(string? text, out AirportCode code)` (C3: trims, 3–5 ASCII letters/digits), case-insensitive equality, `==`/`!=`, `ToString() => Value`. `default(AirportCode)` is safe to compare and hash (treat a `null` `Value` as `""`, as `PackageName` does).
  - `public readonly record struct GeoPoint(double Latitude, double Longitude);`
  - `public sealed record AirportRecord(AirportCode Code, string? Name, GeoPoint? Position);` — `Name`/`Position` are always set by the BGL reader; the content fixture may omit them (Task 21).
  - `public sealed record AircraftConfig(string Container, string? BaseContainer);` — `Container` is the folder holding the `aircraft.cfg`; `BaseContainer` is the last segment of `base_container`, or `null`.
  - `public enum PackageSource { Library, Community, SimManaged }` — `Community` means a Fixed or External entry.
  - `public sealed record ChangeMarker(long ManifestTicks, long ManifestSize, long? LayoutTicks, long? LayoutSize);`
  - `public sealed record ScanTarget(PackageName Name, string Path, PackageSource Source, Manifest Manifest);`
  - `public sealed record PackageContent(PackageName Name, string Path, PackageSource Source, ChangeMarker Marker, Manifest Manifest, bool HasBgl, IReadOnlyList<uint> BglSectionTypes, int UnreadableBglCount, IReadOnlyList<AirportRecord> Airports, bool IsAirTraffic, IReadOnlyList<AircraftConfig> AircraftConfigs, IReadOnlyList<string> AirplaneFolders, IReadOnlyList<string> EncryptedAirplaneFolders, string? ThumbnailPath);` — `Path` is normalised (C5); `BglSectionTypes` sorted and distinct; `AirplaneFolders` are the folder names under `SimObjects\Airplanes` seen in the Package; `EncryptedAirplaneFolders` is the subset that holds a `.fsarchive` file and no `aircraft.cfg` (encrypted aircraft, spec 6.6).
  - `public interface IPackageContentReader { ChangeMarker? ReadMarker(string packagePath); bool? HasBgl(string packagePath); PackageContent? Read(ScanTarget target, ChangeMarker marker); }` — `ReadMarker` is `null` when `manifest.json` is gone; `HasBgl` is `null` without a `layout.json`; `Read` is `null` when the folder vanished. `ReadMarker` and `Read` throw `IOException`/`UnauthorizedAccessException` when the Package can't be accessed (C2).
  - `public interface IScanCache { IReadOnlyList<PackageContent> LoadAll(); void Upsert(IReadOnlyList<PackageContent> contents); void Remove(IReadOnlyCollection<string> paths); }`

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("eham", "EHAM")] [InlineData(" U60 ", "U60")] [InlineData("YALR1", "YALR1")] [InlineData("3u2", "3U2")]
public void Parses_valid_codes_upper_case(string text, string expected)
{
    Assert.True(AirportCode.TryParse(text, out var code));
    Assert.Equal(expected, code.Value);
}

[Theory]
[InlineData(null)] [InlineData("")] [InlineData("AB")] [InlineData("ABCDEF")] [InlineData("EH-M")] [InlineData("ÅÄÖX")]
public void Rejects_invalid_codes(string? text) => Assert.False(AirportCode.TryParse(text, out _));

[Fact] public void Equality_ignores_case()
{
    AirportCode.TryParse("eham", out var a); AirportCode.TryParse("EHAM", out var b);
    Assert.Equal(a, b); Assert.Equal(a.GetHashCode(), b.GetHashCode());
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test ModernMsfsAddonManager.slnx --filter FullyQualifiedName~AirportCodeTests` → FAIL (type missing).
- [ ] **Step 3: Implement** the types above; records only, no logic beyond `AirportCode`. Make the `CLAUDE.md`, `ANALYS.md` and spec edits listed under Files.
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: add content model and airport codes"`

---

### Task 2: Categories

**Files:**
- Create: `src/AddonManager.Core/Content/Category.cs`, `CategoryRules.cs`
- Test: `tests/AddonManager.Core.Tests/Content/CategoryRulesTests.cs`

**Interfaces:**
- Consumes: nothing beyond Task 1.
- Produces:
  - `public enum Category { Scenery, Aircraft, Other }`
  - `public static class CategoryRules`
    - `Category Provisional(string? contentType, bool? hasBgl)` — for discovery, before content is read.
    - `Category Final(string? contentType, bool hasBgl, bool isAirTraffic, bool isAircraftMember)` — after the content pass; `isAircraftMember` is `AircraftRole != None` (Task 12).
    - `bool IsAircraftType(string? contentType)` — `AIRCRAFT` or `LIVERY`.

Rules (spec 6.4, ANALYS decision "Category"): `SCENERY` → Scenery, but Other when there is no BGL (`hasBgl == false`; `null` means unknown and keeps Scenery). `AIRCRAFT`/`LIVERY` → Aircraft, but in `Final` Other when `!isAircraftMember`. Everything else, including `null` and unknown values, → Other. In `Final`, `isAirTraffic` makes any Package Other, whatever its type (spec 6.4). `content_type` is compared case-insensitively.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("SCENERY", null, Category.Scenery)]
[InlineData("SCENERY", true, Category.Scenery)]
[InlineData("SCENERY", false, Category.Other)]    // fsdreamteam-gsx-pro, neofly-neofly-smokes
[InlineData("aircraft", null, Category.Aircraft)]
[InlineData("LIVERY", null, Category.Aircraft)]
[InlineData("MISC", null, Category.Other)]
[InlineData(null, null, Category.Other)]
public void Provisional(string? type, bool? hasBgl, Category expected) => Assert.Equal(expected, CategoryRules.Provisional(type, hasBgl));

[Theory]
[InlineData("AIRCRAFT", false, true, true, Category.Other)]   // fsltl-traffic-base: isAirTraffic=1
[InlineData("AIRCRAFT", false, false, false, Category.Other)] // no aircraft.cfg, matched to nothing
[InlineData("AIRCRAFT", false, false, true, Category.Aircraft)]
[InlineData("SCENERY", false, false, false, Category.Other)]
[InlineData("SCENERY", true, false, false, Category.Scenery)]
[InlineData("SCENERY", true, true, false, Category.Other)]       // scenery that ships AI aircraft
public void Final(string type, bool hasBgl, bool airTraffic, bool member, Category expected)
    => Assert.Equal(expected, CategoryRules.Final(type, hasBgl, airTraffic, member));
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement** the rules above.
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: derive package categories"`

---

### Task 3: BGL structure reader and `bgl-dump` (BGL track)

**Files:**
- Create: `src/AddonManager.Sim2020/Bgl/BglFile.cs`, `AirportCodeCodec.cs`, `BglShape.cs`, `src/AddonManager.Cli/BglDumpCommand.cs`, `tests/AddonManager.Sim2020.Tests/Bgl/BglBuilder.cs`
- Modify: `src/AddonManager.Cli/Program.cs` (route `bgl-dump`), `tests/AddonManager.Sim2020.Tests/AddonManager.Sim2020.Tests.csproj` (copy `Fixtures\**` to output, like Core.Tests)
- Test: `tests/AddonManager.Sim2020.Tests/Bgl/BglFileTests.cs`, `AirportCodeCodecTests.cs`

**Interfaces:**
- Consumes: nothing (format level only; `AirportCode` is not needed yet — the codec works on strings).
- Produces:
  - `public sealed record BglSubsection(int RecordCount, long DataOffset, int DataSize);`
  - `public sealed record BglSection(uint Type, int SubsectionEntrySize, IReadOnlyList<BglSubsection> Subsections);`
  - `public readonly record struct BglRecord(ushort Id, int Offset, int Size);` — `Offset` relative to the start of the buffer it was read from.
  - `public static class BglFile`
    - `IReadOnlyList<BglSection>? ReadSections(Stream stream)` — `null` when unreadable (C2).
    - `IEnumerable<BglRecord> Records(ReadOnlyMemory<byte> data)` — walks `u16 id, u32 size` records; stops (yields nothing more) at a size below 6 or past the end; `BglFile.IsConsistent(data)` returns false in that case.
    - `const uint AirportSectionType = 0x03; const ushort AirportRecordId = 0x0056; const ushort NameRecordId = 0x0019;`
    - `int? FindAirportFixedSize(ReadOnlySpan<byte> record)` — the fixed-size detection below; `BglShapes` (this task) and `AirportRecordReader` (Task 6) both use it.
  - `public static class AirportCodeCodec { string Decode(uint packed); uint Encode(string code); }` — `packed >> 5`, base 38: 0 = end, 2–11 = `0`–`9`, 12–37 = `A`–`Z`.
  - The JSON-serialisable structure below, used by `bgl-dump` and by Task 5:
    - `public sealed record BglShape(IReadOnlyList<BglSectionShape> Sections)` with `int AirportRecordCount()` (records with id 0x56 across all 0x03 sections);
    - `public sealed record BglSectionShape(string Type, int SubsectionEntrySize, IReadOnlyList<BglSubsectionShape> Subsections);`
    - `public sealed record BglSubsectionShape(int RecordCount, int DataSize, IReadOnlyList<BglRecordShape>? Records);`
    - `public sealed record BglRecordShape(string Id, int Size, int? FixedSize, string? Code, string? Name, double? Latitude, double? Longitude, IReadOnlyList<BglChildShape> Children);`
    - `public sealed record BglChildShape(string Id, int Size);`
    - `public static class BglShapes { BglShape? Describe(Stream bgl); }` — `null` when unreadable.
  - CLI: `AddonManager.Cli bgl-dump <file-or-package-folder> [--code <code>] [--out <file>]`.
  - Test helper `BglBuilder`: fluent `.Section(uint type, int entrySize = 16)`, `.Subsection(params byte[][] records)`, `byte[] Build()`; static `byte[] Record(ushort id, byte[] body)`, `byte[] Airport(string code, string? name, double lat, double lon, int fixedSize)`, `byte[] Broken(string kind)`. Task 5 extends it.

Format (FSDeveloper wiki "BGL File Format"; Task 4 confirms it on real files):
- Header, 0x38 bytes: `u32` magic `0x19920201` at 0x00; `u32` section count at 0x14.
- Section table at 0x38, 20 bytes per entry: `u32 type`, `u32 sizeFlag`, `u32 subsectionCount`, `u32 subsectionTableOffset`, `u32 subsectionTableSize`. Entry size of the subsection table = `((sizeFlag & 0x10000) | 0x40000) >> 0x0E` (16 or 20).
- Subsection entry: 16 bytes `u32 id, u32 recordCount, u32 dataOffset, u32 dataSize`; 20 bytes `u32 id, u32 id2, u32 recordCount, u32 dataOffset, u32 dataSize`.
- Unreadable (whole file, no partial results): short file, wrong magic, any table or data range outside the file.
- `ReadSections` reads only the header and tables from the stream, never whole files.

`BglShape` (JSON, camelCase; offsets omitted, sizes kept):

```json
{ "sections": [
  { "type": "0x3", "subsectionEntrySize": 16, "subsections": [
    { "recordCount": 1, "dataSize": 812, "records": [
      { "id": "0x56", "size": 806, "fixedSize": 68, "code": "EHAM", "name": "Amsterdam Schiphol",
        "latitude": 52.3086, "longitude": 4.7639,
        "children": [ { "id": "0x19", "size": 24 }, { "id": "0x4", "size": 52 } ] } ] } ] },
  { "type": "0x65", "subsectionEntrySize": 16, "subsections": [ { "recordCount": 3, "dataSize": 400 } ] } ] }
```

`records` is listed only for section type 0x03. Fixed-field offsets in an airport record (documented, confirmed in Task 4): longitude `u32` at 0x0C, latitude `u32` at 0x10, packed code `u32` at 0x28; `longitude = raw * 360.0 / (3 * 0x10000000) - 180.0`, `latitude = 90.0 - raw * 180.0 / (2 * 0x10000000)`. `fixedSize` is found, not assumed (`BglFile.FindAirportFixedSize`): the first of 0x44, 0x38, 0x3C, 0x48, 0x4C (the documented MSFS size first) from which the child records (`u16 id, u32 size`, size ≥ 6) end exactly at the record's end; `null` when none fits. `name` is the `0x0019` child's bytes as a null-trimmed string.

`bgl-dump` with a folder argument picks the first `.bgl` (ordinal path order, recursive) that has an airport section, or with `--code` the first whose airport records include that code; with a file it dumps that file. It prints the shape to stdout, or writes it with `--out`. Read-only (C1).

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory] [InlineData("EHAM")] [InlineData("YALR1")] [InlineData("U60")] [InlineData("3U2")] [InlineData("OG20")]
public void Codec_round_trips(string code) => Assert.Equal(code, AirportCodeCodec.Decode(AirportCodeCodec.Encode(code)));

[Fact] public void Reads_sections_and_subsections_with_both_entry_sizes()
{
    var bgl = new BglBuilder()
        .Section(0x03).Subsection(BglBuilder.Record(0x0056, new byte[60]))
        .Section(0x65, entrySize: 20).Subsection(BglBuilder.Record(0x0001, new byte[4]), BglBuilder.Record(0x0001, new byte[4]))
        .Build();
    var sections = BglFile.ReadSections(new MemoryStream(bgl))!;
    Assert.Equal([0x03u, 0x65u], sections.Select(s => s.Type));
    Assert.Equal(16, sections[0].SubsectionEntrySize); Assert.Equal(20, sections[1].SubsectionEntrySize);
    Assert.Equal(2, sections[1].Subsections.Single().RecordCount);
}

[Theory]
[InlineData("wrong-magic")] [InlineData("truncated-header")] [InlineData("table-past-end")] [InlineData("data-past-end")] [InlineData("random")]
public void Unreadable_files_return_null_and_never_throw(string kind)
    => Assert.Null(BglFile.ReadSections(new MemoryStream(BglBuilder.Broken(kind))));

[Fact] public void Record_walk_stops_at_an_impossible_size()
{
    var data = BglBuilder.Record(0x0056, new byte[10]).Concat(new byte[] { 0x19, 0x00, 0x02, 0, 0, 0 }).ToArray();
    Assert.Single(BglFile.Records(data));
    Assert.False(BglFile.IsConsistent(data));
}

[Fact] public void Shape_finds_the_fixed_size_and_the_name_child()
{
    var bgl = new BglBuilder().Section(0x03).Subsection(BglBuilder.Airport("EHAM", "Amsterdam Schiphol", 52.3086, 4.7639, fixedSize: 0x44)).Build();
    var record = BglShapes.Describe(new MemoryStream(bgl))!.Sections[0].Subsections[0].Records!.Single();
    Assert.Equal(0x44, record.FixedSize); Assert.Equal("EHAM", record.Code); Assert.Equal("Amsterdam Schiphol", record.Name);
    Assert.Equal(52.3086, record.Latitude!.Value, 3); Assert.Equal(4.7639, record.Longitude!.Value, 3);
}
```

`BglBuilder.Airport(code, name, lat, lon, fixedSize)` writes the record header, the fixed part with code/latitude/longitude at the offsets above (rest zero) and one `0x0019` name child. `BglBuilder.Broken(kind)` returns the five broken files named in the test.

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement** `BglFile`, `AirportCodeCodec`, `BglShapes` and `BglDumpCommand` (System.Text.Json, camelCase, hex strings for `type`/`id`, indented).
- [ ] **Step 4: Run to verify pass** → PASS. Also run `dotnet run --project src/AddonManager.Cli -- bgl-dump <a file built by the test>` once by hand; expected: the JSON shape above.
- [ ] **Step 5: Commit** — `git commit -m "feat: read BGL structure and add bgl-dump command"`

---

### Task 4: Rickard dumps real BGL structures (BGL track, manual)

**Owner:** Rickard, on the sim machine, from this branch. About 5 minutes. The dev machine has no MSFS install.

**Files:**
- Create: `tests/AddonManager.Sim2020.Tests/Fixtures/bgl-shapes/generic.json`, `eham-bbd.json`, `yalr1.json`, `asobo-airport.json`, `lfcs-saucats.json`

- [ ] **Step 1: Find the Official folder** — `dotnet run --project src/AddonManager.Cli -- status` prints `Official:`.
- [ ] **Step 2: Dump the five templates** (PowerShell, from the repo root):

```powershell
$o = '<Official path from step 1>'
$s = 'tests\AddonManager.Sim2020.Tests\Fixtures\bgl-shapes'
New-Item -ItemType Directory -Force $s | Out-Null
dotnet run --project src/AddonManager.Cli -- bgl-dump "$o\fs-base-genericairports" --out "$s\generic.json"
dotnet run --project src/AddonManager.Cli -- bgl-dump 'C:\Spel\Addons\Scenery\Europe\brickbybrickdesign-airport-eham-amsterdam' --code EHAM --out "$s\eham-bbd.json"
dotnet run --project src/AddonManager.Cli -- bgl-dump '<sltcreations package folder>' --code YALR1 --out "$s\yalr1.json"
dotnet run --project src/AddonManager.Cli -- bgl-dump "$o\<a readable asobo-airport-* package, e.g. asobo-airport-lowi-innsbruck>" --out "$s\asobo-airport.json"
dotnet run --project src/AddonManager.Cli -- bgl-dump "$o\asobo-airport-lfcs-saucats" --out "$s\lfcs-saucats.json"
```

(Adjust the EHAM and sltcreations paths to where the Packages really are; `status` and the plan-1 fixture list them.)

- [ ] **Step 3: Check the dumps.** Expected: `eham-bbd.json` has an airport record with `"code": "EHAM"`, a position within 0.05° of 52.31 N, 4.76 E, a non-null `fixedSize` and a `0x19` child; `yalr1.json` has `"code": "YALR1"`; `lfcs-saucats.json` has no 0x03 section (or one without 0x56 records). **Stop and report** if a code decodes wrongly, a position is off, `fixedSize` is null, or airport records use an id other than `0x56` — the plan's offsets or spec 6.5 would then need changing before Task 5.
- [ ] **Step 4: Commit and push** — `git commit -m "test: add BGL structure dumps from real files"`, then `git push` (to `plan-2/bgl-track`, see Handover).

---

### Task 5: BGL builder from real shapes (BGL track)

**Files:**
- Modify: `tests/AddonManager.Sim2020.Tests/Bgl/BglBuilder.cs`
- Test: `tests/AddonManager.Sim2020.Tests/Bgl/BglShapeFidelityTests.cs`

**Interfaces:**
- Consumes: `BglShape`, `BglShapes.Describe` (Task 3), the dumps (Task 4).
- Produces: `static byte[] BglBuilder.FromShape(BglShape shape)` and `static BglShape BglBuilder.LoadShape(string name)` (reads `Fixtures/bgl-shapes/<name>.json`). `FromShape` rebuilds every section, subsection and record with the dumped ids and sizes, writes code, position and name at the confirmed offsets and fills every other byte with zero.

ANALYS decision "BGL-fixtures": the synthetic files are modelled on real files, and this task proves it.

- [ ] **Step 1: Write the failing test**

```csharp
[Theory]
[InlineData("generic")] [InlineData("eham-bbd")] [InlineData("yalr1")] [InlineData("asobo-airport")] [InlineData("lfcs-saucats")]
public void Built_file_has_the_same_structure_as_the_real_one(string name)
{
    var shape = BglBuilder.LoadShape(name);
    var rebuilt = BglShapes.Describe(new MemoryStream(BglBuilder.FromShape(shape)));
    Assert.Equivalent(shape, rebuilt, strict: true);
}
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement** `FromShape` and `LoadShape`.
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "test: build synthetic BGL files from real structure dumps"`

---

### Task 6: Airport records from BGL files (BGL track)

**Files:**
- Create: `src/AddonManager.Sim2020/Bgl/AirportRecordReader.cs`, `BglContentReader.cs`
- Test: `tests/AddonManager.Sim2020.Tests/Bgl/BglContentReaderTests.cs`

**Interfaces:**
- Consumes: `BglFile`, `AirportCodeCodec`, `BglBuilder.FromShape`/`LoadShape` (Task 5), `AirportCode`, `AirportRecord`, `GeoPoint` (Task 1).
- Produces:
  - `public sealed record BglScan(bool Readable, IReadOnlyList<uint> SectionTypes, IReadOnlyList<AirportRecord> Airports);`
  - `public static class BglContentReader { BglScan Read(string bglPath); BglScan Read(Stream stream); }` — reads only the header, tables and the data of 0x03 subsections; never throws for bad content (C2); records with an invalid code are skipped (C3).
  - `AirportRecordReader` finds each record's fixed size with `BglFile.FindAirportFixedSize` (Task 3) and walks the children from there to find the `0x0019` name; when no size fits, the record keeps its code and position with a `null` name.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Reads_eham_from_its_real_shape()
{
    var scan = BglContentReader.Read(new MemoryStream(BglBuilder.FromShape(BglBuilder.LoadShape("eham-bbd"))));
    var eham = Assert.Single(scan.Airports, a => a.Code.Value == "EHAM");
    Assert.Equal(52.31, eham.Position!.Value.Latitude, 1); Assert.Equal(4.76, eham.Position!.Value.Longitude, 1);
    Assert.False(string.IsNullOrWhiteSpace(eham.Name));
    Assert.Contains(0x03u, scan.SectionTypes);
}

[Fact] public void Reads_five_character_codes() => Assert.Contains(Read("yalr1").Airports, a => a.Code.Value == "YALR1");

[Fact] public void A_bgl_without_airport_records_is_readable_and_empty()
{
    var scan = Read("lfcs-saucats");
    Assert.True(scan.Readable); Assert.Empty(scan.Airports);
}

[Fact] public void Generic_file_yields_every_valid_record_in_it()
    => Assert.Equal(AllRecords(BglBuilder.LoadShape("generic")).Count(r => AirportCode.TryParse(r.Code, out _)), Read("generic").Airports.Count);

[Fact] public void Empty_code_is_ignored() { /* BglBuilder.Airport("", …) */ Assert.Empty(scan.Airports); }
[Fact] public void Airport_without_name_child_has_null_name() { … Assert.Null(record.Name); }
[Theory] [InlineData("wrong-magic")] [InlineData("data-past-end")] [InlineData("random")]
public void Broken_files_are_unreadable(string kind) => Assert.False(BglContentReader.Read(new MemoryStream(BglBuilder.Broken(kind))).Readable);
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement** the two classes (`FileStream` per C1).
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: read airport records from BGL files"`

---

### Task 7: `layout.json` and `aircraft.cfg` (BGL track)

**Files:**
- Create: `src/AddonManager.Sim2020/Content/LayoutReader.cs`, `AircraftCfgReader.cs`, `tests/AddonManager.Sim2020.Tests/TempFolder.cs` (a copy of the one in Infrastructure.Tests; Tasks 8 and 20 reuse it)
- Test: `tests/AddonManager.Sim2020.Tests/Content/LayoutReaderTests.cs`, `AircraftCfgReaderTests.cs`

**Interfaces:**
- Consumes: `AircraftConfig` (Task 1).
- Produces:
  - `public static class LayoutReader { IReadOnlyList<string>? Read(string packagePath); IReadOnlyList<string> ListFiles(string packagePath); }` — `Read` returns the `content[].path` values with `\` separators, or `null` when `layout.json` is missing or invalid; `ListFiles` returns `Read(...)` or, when that is `null`, a recursive walk (reparse points skipped, like `LibraryScanner`) as relative paths.
  - `public sealed record CfgFacts(string? BaseContainer, bool IsAirTraffic);`
  - `public static class AircraftCfgReader { CfgFacts? Read(string cfgPath); string? LastSegment(string? baseContainer); }` — `null` when unreadable.

Rules: keys are case-insensitive, whitespace around `=` is ignored, `;` and `//` start comments, values may be quoted. `isAirTraffic = 1` anywhere in the file sets `IsAirTraffic`. `base_container` anywhere sets `BaseContainer` to its last path segment (`"..\Aerosoft_CRJ_900"` → `Aerosoft_CRJ_900`, `../x/` → `x`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Layout_paths_use_backslashes() { WriteLayout("""{"content":[{"path":"SimObjects/Airplanes/X/aircraft.cfg","size":1,"date":1}]}"""); Assert.Equal([@"SimObjects\Airplanes\X\aircraft.cfg"], LayoutReader.Read(pkg)); }
[Fact] public void Missing_or_invalid_layout_reads_null_and_falls_back_to_a_walk() { … Assert.Null(LayoutReader.Read(pkg)); Assert.Contains(@"scenery\a.bgl", LayoutReader.ListFiles(pkg)); }

[Theory]
[InlineData("[VARIATION]\nbase_container = \"..\\Aerosoft_CRJ_900\"", "Aerosoft_CRJ_900", false)]
[InlineData("[GENERAL]\nisAirTraffic=1 ; AI", null, true)]
[InlineData("[GENERAL]\nIsAirTraffic = 0", null, false)]
[InlineData("; base_container = \"..\\X\"", null, false)]
public void Reads_cfg_facts(string text, string? baseContainer, bool airTraffic)
{
    var facts = AircraftCfgReader.Read(Write("aircraft.cfg", text))!;
    Assert.Equal(baseContainer, facts.BaseContainer); Assert.Equal(airTraffic, facts.IsAirTraffic);
}

[Fact] public void Unreadable_cfg_is_null() => Assert.Null(AircraftCfgReader.Read(Path.Combine(dir, "missing.cfg")));
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: read layout.json and aircraft.cfg"`

---

### Task 8: Package content reader (BGL track)

**Files:**
- Create: `src/AddonManager.Sim2020/Content/PackageContentReader.cs`
- Test: `tests/AddonManager.Sim2020.Tests/Content/PackageContentReaderTests.cs`

**Interfaces:**
- Consumes: `IPackageContentReader`, `ScanTarget`, `PackageContent`, `ChangeMarker` (Task 1), `BglContentReader` (Task 6), `LayoutReader`, `AircraftCfgReader` (Task 7), `PathNormalizer`.
- Produces: `public sealed class PackageContentReader : IPackageContentReader`.

Behaviour:
- `ReadMarker`: `LastWriteTimeUtc.Ticks` and `Length` of `manifest.json` and (when present) `layout.json`; `null` when `manifest.json` is gone.
- `HasBgl`: any `.bgl` in `LayoutReader.Read`; `null` without a layout.
- `Read`: files from `LayoutReader.ListFiles`. `HasBgl` = any `.bgl`. Every `.bgl` goes through `BglContentReader`; unreadable ones increase `UnreadableBglCount`; section types and airport records are merged. `AirplaneFolders` = distinct `SimObjects\Airplanes\<folder>` names; `EncryptedAirplaneFolders` = those with a `.fsarchive` file and no `aircraft.cfg`. Each `SimObjects\Airplanes\<folder>\aircraft.cfg` is read in path order; the first one with `IsAirTraffic` stops cfg reading and sets `IsAirTraffic = true` with an empty `AircraftConfigs` (spec 6.4: not parsed for Liveries). `ThumbnailPath` = the absolute path of the first `ContentInfo\<any>\Thumbnail.jpg`. Returns `null` when the folder no longer exists. Paths normalised (C5).
- Access (C2): `ReadMarker` and `Read` let `IOException` and `UnauthorizedAccessException` propagate when the Package folder, `manifest.json` or `layout.json` can't be opened or listed, including the fallback walk failing at the Package root. Problems with single files inside the Package (a BGL or `aircraft.cfg` that can't be opened) don't throw: a BGL counts in `UnreadableBglCount`, a cfg is skipped.

- [ ] **Step 1: Write the failing tests** (a temp Package per test, BGLs from `BglBuilder.FromShape`)

```csharp
[Fact] public void Reads_airports_sections_and_thumbnail()
{
    var pkg = Package("eham-pkg", ("scenery\\eham.bgl", Bgl("eham-bbd")), ("ContentInfo\\x-y\\Thumbnail.jpg", [1]));
    var c = reader.Read(Target(pkg), reader.ReadMarker(pkg)!)!;
    Assert.True(c.HasBgl); Assert.Contains(c.Airports, a => a.Code.Value == "EHAM");
    Assert.EndsWith(@"ContentInfo\x-y\Thumbnail.jpg", c.ThumbnailPath);
}

[Fact] public void Counts_unreadable_bgls_and_keeps_going()
{ var pkg = Package("p", ("a.bgl", BglBuilder.Broken("random")), ("b.bgl", Bgl("yalr1"))); var c = Read(pkg); Assert.Equal(1, c.UnreadableBglCount); Assert.Single(c.Airports); }

[Fact] public void Air_traffic_stops_cfg_parsing()
{
    var pkg = Package("fsltl-traffic-base", (@"SimObjects\Airplanes\A\aircraft.cfg", Cfg("isAirTraffic=1")), (@"SimObjects\Airplanes\B\aircraft.cfg", Cfg("base_container=\"..\\X\"")));
    var c = Read(pkg); Assert.True(c.IsAirTraffic); Assert.Empty(c.AircraftConfigs);
}

[Fact] public void Containers_come_from_cfg_folders_and_airplane_folders_from_any_file()
{
    var pkg = Package("p", (@"SimObjects\Airplanes\Asobo_C152\aircraft.cfg", Cfg("")), (@"SimObjects\Airplanes\Enc\model.fsarchive", [1]));
    var c = Read(pkg);
    Assert.Equal([new AircraftConfig("Asobo_C152", null)], c.AircraftConfigs);
    Assert.Equal(["Asobo_C152", "Enc"], c.AirplaneFolders.Order());
    Assert.Equal(["Enc"], c.EncryptedAirplaneFolders);
}

[Fact] public void Inaccessible_package_folder_throws_instead_of_reading_empty()
{
    var pkg = Package("p", ("a.bgl", Bgl("yalr1")));
    var marker = reader.ReadMarker(pkg)!;
    using (DenyListing(pkg))   // ACL deny on list-folder for the current user, removed on dispose
        Assert.Throws<UnauthorizedAccessException>(() => reader.Read(Target(pkg), marker));
}

[Fact] public void Marker_changes_with_layout_and_is_null_without_manifest() { … }
[Fact] public void Vanished_folder_reads_null() { … Directory.Delete(pkg, true); Assert.Null(reader.Read(Target(pkg), marker)); }
[Fact] public void Reading_changes_nothing_on_disk() { var before = Snapshot(pkg); Read(pkg); Assert.Equal(before, Snapshot(pkg)); } // names, sizes, timestamps
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: read package content facts"`

---

### Task 9: Content settings in user data

**Files:**
- Create: `src/AddonManager.Core/Content/ContentSettings.cs`, `src/AddonManager.Infrastructure/JsonConverters.cs`
- Modify: `src/AddonManager.Core/State/UserState.cs` (add `ContentSettings Content { get; set; } = new();`), `src/AddonManager.Infrastructure/JsonUserStateStore.cs` (move `PackageNameConverter` to `JsonConverters.cs`, add `AirportCodeConverter`, also as dictionary key), `tests/AddonManager.Core.Tests/Fakes/InMemoryUserStateStore.cs` (`Copy` deep-copies `Content` with `ContentSettings.Clone()`)
- Test: `tests/AddonManager.Core.Tests/Content/ContentSettingsTests.cs`, extend `tests/AddonManager.Infrastructure.Tests/JsonUserStateStoreTests.cs`

**Interfaces:**
- Consumes: `PackageName`, `AirportCode`.
- Produces:
  - `public sealed class ContentSettings` with
    - `static readonly IReadOnlyList<string> DefaultExclusionPatterns = ["navigraph-navdata*", "*-point-of-interest", "fsdreamteam-gsx-world-of-jetways*", "microsoft-airport-voloport", "neofly-*"];`
    - `static readonly IReadOnlyList<string> DefaultStopList = ["PACK", "SAVE", "SWIM", "SANS"];` (extended in Task 22)
    - `int AirportThreshold { get; set; } = 10;`
    - Persisted user changes to the two lists: `HashSet<string> AddedExclusionPatterns`, `RemovedExclusionPatterns`, `AddedStopWords`, `RemovedStopWords`, all `{ get; set; } = new(StringComparer.OrdinalIgnoreCase)` with `[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]`, so a JSON load fills the case-insensitive set instead of replacing it with a case-sensitive one. `Clone()` keeps the comparer.
    - `IReadOnlyList<string> ExclusionPatterns` and `IReadOnlyList<string> StopList` (not persisted): defaults plus added, minus removed, sorted. So defaults added in a later release reach existing users, and a default the user removed stays removed.
    - `void AddExclusionPattern(string)`, `void RemoveExclusionPattern(string)`, `void AddStopWord(string)`, `void RemoveStopWord(string)` — adding a removed default un-removes it; removing an added entry un-adds it.
    - `HashSet<PackageName> OptedIn { get; set; } = [];`
    - `Dictionary<PackageName, HashSet<AirportCode>> ManualAirportLinks { get; set; } = [];`
    - `Dictionary<PackageName, HashSet<AirportCode>> RemovedAirportLinks { get; set; } = [];`
    - `bool MatchesExclusion(PackageName name)` — `*` wildcard only, whole name, case-insensitive.
    - `bool IsStopWord(string token)` — case-insensitive.
    - `void LinkManually(PackageName, AirportCode)` (also removes it from `RemovedAirportLinks`) and `void RemoveLink(PackageName, AirportCode)` (also removes it from `ManualAirportLinks`).
    - `ContentSettings Clone()` — a deep copy (Task 17 snapshots with it).
  - `internal static class JsonConverters` in Infrastructure: `PackageNameConverter`, `AirportCodeConverter` (Task 14 reuses them). Invalid Airport codes in user data are dropped on load, never an error (C3): `AirportCodeConverter` reads an invalid code as `default(AirportCode)`, and `JsonUserStateStore.Load` then removes `default` codes from `ManualAirportLinks` and `RemovedAirportLinks` and drops entries whose set ends up empty.

ANALYS decision "Användardata": Package-level inputs to Airport linking are stored here (plan 2); Category override per Addon is plan 3.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("navigraph-navdata", true)] [InlineData("Navigraph-NavData-Base", true)]
[InlineData("microsoft-nordics2-point-of-interest", true)] [InlineData("neofly-neofly-cargo", true)]
[InlineData("microsoft-airport-voloport", true)] [InlineData("microsoft-airport-voloport-x", false)]
[InlineData("brickbybrickdesign-airport-eham-amsterdam", false)]
public void Default_exclusions(string name, bool excluded) => Assert.Equal(excluded, new ContentSettings().MatchesExclusion(new(name)));

[Fact] public void Manual_link_undoes_a_removal_and_vice_versa() { … }

// JsonUserStateStoreTests
[Fact] public void Plan1_user_data_without_content_loads_defaults() { File.WriteAllText(file, """{"schemaVersion":1,"libraries":[],"packages":{}}"""); Assert.Equal(10, store.Load().Content.AirportThreshold); }
[Fact] public void Stop_list_changes_round_trip_on_top_of_the_defaults()
{
    var s = new UserState(); s.Content.AddStopWord("EXTRA"); s.Content.RemoveStopWord("pack"); store.Save(s);
    var list = store.Load().Content.StopList;
    Assert.Contains("EXTRA", list); Assert.DoesNotContain("PACK", list); Assert.Contains("SAVE", list);
    Assert.DoesNotContain("stopList", File.ReadAllText(file));   // only the changes are stored
}
[Fact] public void Re_adding_a_removed_default_after_a_reload_ignores_case()
{
    var s = new UserState(); s.Content.RemoveStopWord("pack"); store.Save(s);
    var loaded = store.Load(); loaded.Content.AddStopWord("PACK");
    Assert.Contains("PACK", loaded.Content.StopList); Assert.Empty(loaded.Content.RemovedStopWords);
}
[Fact] public void Airport_links_round_trip_case_insensitively() { … Assert.Contains(code("eham"), loaded.Content.ManualAirportLinks[new("PKG")]); }
[Fact] public void Invalid_codes_in_user_data_are_dropped() { … "manualAirportLinks":{"pkg":["EHAM","x"]} … Assert.Equal([code("EHAM")], loaded.Content.ManualAirportLinks[new("pkg")]); }

// InMemoryUserStateStore
[Fact] public void Fake_store_keeps_content_settings() { var s = new UserState(); s.Content.OptedIn.Add(new("x")); fake.Save(s); s.Content.OptedIn.Clear(); Assert.Single(fake.Load().Content.OptedIn); }
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.** The user-data `schemaVersion` stays 1 (the new property is optional).
- [ ] **Step 4: Run to verify pass** (the whole suite: the plan-1 store tests must still pass) → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: store content settings and airport link choices"`

---

### Task 10: Airport linking

**Files:**
- Create: `src/AddonManager.Core/Content/AirportLinker.cs`
- Test: `tests/AddonManager.Core.Tests/Content/AirportLinkerTests.cs` (+ a `ContentBuilder` test helper in `tests/AddonManager.Core.Tests/Content/` for `PackageContent` instances)

**Interfaces:**
- Consumes: `PackageContent`, `AirportCode`, `PackageSource` (Task 1), `CategoryRules` (Task 2), `ContentSettings` (Task 9).
- Produces:
  - `public enum AirportLinkReason { Record, Token, PackageName, Manual }`
  - `public sealed record AirportLink(PackageContent Package, AirportCode Code, AirportLinkReason Reason);`
  - `public static class AirportLinker`
    - `IReadOnlyList<AirportLink> Link(IReadOnlyList<PackageContent> contents, ContentSettings settings)` — one link per (Package, code), the first reason in the order Manual, Record, PackageName, Token; sorted as in C7.
    - `bool IsExcluded(PackageContent content, ContentSettings settings)` — C4.
    - `bool IsExcludedByPattern(PackageContent content, ContentSettings settings)` — matches an exclusion pattern and isn't opted in.
    - `IReadOnlySet<AirportCode> KnownCodes(IReadOnlyList<PackageContent> contents, ContentSettings settings)` — every code in any record, counted or not, except records of Packages excluded by pattern (C4).

Rules (spec 6.5, C4, C7):
1. **Record:** each distinct code in the Package's records, when the Package isn't excluded.
2. **Token:** for a non-Sim-managed Package with no counted record whose `CategoryRules.Provisional(contentType, hasBgl)` is Scenery: split its folder name and its manifest title on every character that isn't an ASCII letter or digit; a token links when it is a known code, isn't a stop word, and is 4–5 characters long anywhere, or 3 characters long as the **first** token of that string. *My call:* Sim-managed Packages don't use rule 2 (they use rule 3 below; their names, such as `asobo-landmarks-*`, aren't Airport names).
3. **PackageName (encrypted official airports):** a Sim-managed Package with no counted record whose name contains `-airport-<code>-` or ends in `-airport-<code>` links to that code (C3).
4. **Manual:** every code in `ManualAirportLinks[name]` for non-Sim-managed contents with that name (C5).
5. Links in `RemovedAirportLinks[name]` are dropped unless they are Manual, for every source (C5).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Records_link_their_codes()
    => Assert.Equal(["EHAM"], Codes(Link(Scenery("brickbybrickdesign-airport-eham-amsterdam", records: ["EHAM"]))));

[Fact] public void More_than_threshold_codes_exclude_the_package_unless_opted_in()
{
    var nav = Scenery("fs-base-nav", records: Enumerable.Range(0, 11).Select(i => $"K{i:D3}").ToArray(), source: PackageSource.SimManaged);
    Assert.Empty(Link(nav));
    settings.OptedIn.Add(nav.Name);
    Assert.Equal(11, Link(nav).Count);
}

[Fact] public void Exactly_threshold_codes_are_counted() { … 10 codes → 10 links }
[Fact] public void Voloport_and_neofly_records_are_excluded()
    => Assert.Empty(Link(Scenery("microsoft-airport-voloport", records: ["VOLC1", "VOLC2"]), Scenery("neofly-neofly-cargo", records: ["NEOF2"])));

[Theory]
[InlineData("u60-u60", null, "U60")]                     // 3 characters, leading
[InlineData("my-u60-scenery", null, null)]               // 3 characters, not leading
[InlineData("aerosoft-scenery-esgj", null, "ESGJ")]      // 4 characters anywhere
[InlineData("x-pack", "EHAM Pack", "EHAM")]              // title token; PACK is a stop word
[InlineData("ldsp-split", null, "LDSP")]
public void Tokens_link_only_known_codes(string name, string? title, string? expected)
{
    var known = Scenery("fs-base-genericairports", records: ["U60", "ESGJ", "EHAM", "LDSP", "PACK", "MY0"], source: PackageSource.SimManaged);
    var links = Link(known, Scenery(name, title: title, hasBgl: true)).Where(l => l.Package.Name.Value == name);
    Assert.Equal(expected is null ? [] : [expected], Codes(links));
}

[Fact] public void Token_rule_skips_packages_with_a_counted_record_and_non_scenery() { … }
[Fact] public void Token_rule_skips_sim_managed_packages() { … }
[Fact] public void Encrypted_official_airport_links_by_package_name()
{
    var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: PackageSource.SimManaged);
    var lfcs = Scenery("asobo-airport-lfcs-saucats", hasBgl: true, source: PackageSource.SimManaged); // BGL without airport record
    Assert.Equal([("EDDF", AirportLinkReason.PackageName), ("LFCS", AirportLinkReason.PackageName)], Pairs(Link(eddf, lfcs)));
}

[Fact] public void Pattern_excluded_records_are_not_known_codes()
{
    var volo = Scenery("microsoft-airport-voloport", records: ["VOLC1"], source: PackageSource.SimManaged);
    Assert.DoesNotContain(Code("VOLC1"), AirportLinker.KnownCodes([volo], settings));
    Assert.Empty(Link(volo, Scenery("volc1-vertiport", hasBgl: true)));   // no Token link to a non-airport
}

[Fact] public void Manual_links_win_over_removal_and_removal_drops_automatic_links() { … }
[Fact] public void Removal_applies_to_sim_managed_packages()
{
    var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: PackageSource.SimManaged);
    settings.RemoveLink(eddf.Name, Code("EDDF"));
    Assert.Empty(Link(eddf));
}
[Fact] public void Manual_links_ignore_sim_managed_packages() { … settings.LinkManually(simPkg.Name, Code("EHAM")) … Assert.Empty(Link(simPkg)); }
[Fact] public void Result_does_not_depend_on_input_order() { var a = Link(x, y, z); var b = Link(z, y, x); Assert.Equal(Set(a), Set(b)); }
[Fact] public void Real_cases_without_record_or_token_stay_unlinked()
    => Assert.Empty(Link(Scenery("005builds-lszrsceneryv11", hasBgl: true), Scenery("grancanaria", hasBgl: true)));
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement** the rules above.
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: link packages to airports"`

---

### Task 11: Airports, real scenery and Replaces built-in

**Files:**
- Create: `src/AddonManager.Core/Content/AirportCatalog.cs`
- Test: `tests/AddonManager.Core.Tests/Content/AirportCatalogTests.cs`

**Interfaces:**
- Consumes: `AirportLink`, `AirportLinker` (Task 10), `ContentSettings` (Task 9), `PackageContent`, `GeoPoint`.
- Produces:
  - `public sealed record Airport(AirportCode Code, string? Name, GeoPoint? Position, bool HasRealScenery, bool HasBuiltIn);`
  - `public sealed record BuiltInReplacement(PackageContent Package, AirportCode Code);`
  - `public static class AirportCatalog`
    - `IReadOnlyDictionary<AirportCode, Airport> Build(IReadOnlyList<PackageContent> contents, IReadOnlyList<AirportLink> links, ContentSettings settings)` — every known code (`AirportLinker.KnownCodes`) plus every linked code; records of Packages excluded by pattern are never used (C4).
    - `IReadOnlyList<BuiltInReplacement> ReplacesBuiltIn(IReadOnlyList<PackageContent> contents, IReadOnlyList<AirportLink> links)`

Rules (spec 6.5):
- **Name and position:** taken from the first record for the code with a non-empty name (position likewise, independently), searching first the linked Packages' records (Sim-managed first, then by Package name, then path), then all other usable records (same order). Encrypted official airports therefore get name and position from the generic record.
- `HasRealScenery`: at least one link (any reason). `HasBuiltIn`: at least one link from a Sim-managed Package.
- **Replaces built-in:** a link from a non-Sim-managed Package to an Airport with `HasBuiltIn`; also, as the extra signal, a non-Sim-managed Package whose manifest `dependencies` name a Sim-managed Package linked to that Airport. *My call:* the second extra signal in spec 6.5, overlapping `layout.json` paths, is left out: it would mean caching every file path of every Sim-managed airport Package, and the dependency signal plus own links cover the real cases. It's information, not a warning.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Encrypted_official_airport_takes_name_and_position_from_the_generic_record()
{
    var generic = Scenery("fs-base-genericairports", records: [Rec("EDDF", "Frankfurt Main", 50.03, 8.57), …10 more], source: SimManaged);
    var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: SimManaged);
    var airport = Build(generic, eddf)[Code("EDDF")];
    Assert.Equal("Frankfurt Main", airport.Name); Assert.Equal(50.03, airport.Position!.Value.Latitude, 2);
    Assert.True(airport.HasRealScenery); Assert.True(airport.HasBuiltIn);
}

[Fact] public void Generic_only_airport_has_no_real_scenery() => Assert.False(Build(generic)[Code("EDDF")].HasRealScenery);
[Fact] public void Pattern_excluded_records_create_no_airports()
    => Assert.False(Build(Scenery("microsoft-airport-voloport", records: ["VOLC1"], source: SimManaged)).ContainsKey(Code("VOLC1")));
[Fact] public void Linked_record_name_wins_over_generic() { … }
[Fact] public void World_update_partner_airport_is_built_in_because_it_is_sim_managed()
    => Assert.True(Build(Scenery("microsoft-airport-edny-friedrichshafen", records: ["EDNY"], creator: "Partner", source: SimManaged))[Code("EDNY")].HasBuiltIn);

[Fact] public void Library_package_at_a_built_in_airport_replaces_built_in() { … Assert.Equal([("lib-eddf", "EDDF")], Pairs(ReplacesBuiltIn(...))); }
[Fact] public void Dependency_on_the_official_package_is_an_extra_signal() { var patch = Scenery("eddf-fix", hasBgl: true, dependencies: ["asobo-airport-eddf-frankfurt"]); … Assert.Contains(("eddf-fix", "EDDF"), …); }
[Fact] public void Two_library_packages_without_official_scenery_replace_nothing() { … Assert.Empty(…); } // EHAM ×3 stays neutral (ADR 0002)
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: build airports with real scenery and built-in replacement"`

---

### Task 12: Aircraft index

**Files:**
- Create: `src/AddonManager.Core/Content/AircraftIndex.cs`
- Test: `tests/AddonManager.Core.Tests/Content/AircraftIndexTests.cs`

**Interfaces:**
- Consumes: `PackageContent`, `AircraftConfig`, `PackageSource` (Task 1), `CategoryRules.IsAircraftType` (Task 2).
- Produces:
  - `public enum AircraftRole { None, Provider, Mod, Livery, Attached, AirTraffic, LockedBase }`
  - `public sealed record ContainerGroup(string Container, IReadOnlyList<PackageContent> Providers, IReadOnlyList<PackageContent> LockedBases, IReadOnlyList<PackageContent> Liveries, IReadOnlyList<PackageContent> Attached);` — `Providers` are Library Packages (Mods when `LockedBases` isn't empty).
  - `public sealed class AircraftIndex` with `static AircraftIndex Build(IReadOnlyList<PackageContent> contents)`, `IReadOnlyDictionary<string, ContainerGroup> Groups` (case-insensitive keys), `AircraftRole RoleOf(PackageContent content)`, `IReadOnlyList<string> ContainersOf(PackageContent content)` (sorted; plan 3 takes the alphabetically first).

Rules (spec 6.6, C7) — computed in two passes, providers first, so scan order never decides:
- Only Packages with `IsAircraftType(contentType)` take part, plus Sim-managed Packages of any type. `IsAirTraffic` → role AirTraffic, nothing else.
- **Provided containers:** cfg folders without a base container, plus cfg folders whose base container is provided by the same Package, plus `EncryptedAirplaneFolders` for any source (spec 6.6: encrypted aircraft get their container from paths). For **Sim-managed** Packages also every other `AirplaneFolders` entry without a readable cfg (official aircraft whose files aren't `.fsarchive`).
- **Order (C7):** `Providers`, `LockedBases`, `Liveries` and `Attached` are sorted by Package name, then path; `Container` is spelled as in the first provider in that order (else the first Livery's base container, else the first Attached folder).
- **Locked bases:** Sim-managed and Community (Fixed/External) Packages that provide a container. Library Packages that provide it are Providers, with role **Mod** when the group has a locked base, else **Provider**.
- **Livery:** a Package with at least one cfg where every cfg's base container isn't provided by the same Package; it joins the group of each such base container (a group is created even when no provider is installed, so plan 3 can warn "Livery without aircraft").
- **Attached:** an aircraft-type Package with no cfg **that provides no container**: joins every group whose container is in its `AirplaneFolders`; if none, every group in which a Package named in its manifest `dependencies` is a Provider or LockedBase; otherwise role None (Category Other via Task 13).
- **`RoleOf` precedence** for a Package in several groups: AirTraffic, then Mod (a Mod in any group), then Provider, then LockedBase, then Livery, then Attached, else None.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Two_library_packages_providing_one_container_share_its_group_case_insensitively()
{
    var a = Aircraft("sal1800-cessna-170b-backcountry", cfgs: [("Cessna-170B-Backcountry", null)]);
    var b = Aircraft("sal1800-cessna-super-170b-OPTIONAL", cfgs: [("cessna-170b-backcountry", null)]);
    var group = AircraftIndex.Build([b, a]).Groups["CESSNA-170B-BACKCOUNTRY"];
    Assert.Equal([a, b], group.Providers); Assert.Equal("Cessna-170B-Backcountry", group.Container);
}

[Fact] public void Encrypted_library_aircraft_provides_its_container()
{
    var enc = Aircraft("thirdparty-aircraft-x", encryptedFolders: ["TP_X"]);   // .fsarchive, no cfg
    Assert.Equal(AircraftRole.Provider, AircraftIndex.Build([enc]).RoleOf(enc));
}

[Fact] public void Library_package_providing_an_official_container_is_a_mod()
{
    var official = Aircraft("asobo-aircraft-c152", airplaneFolders: ["Asobo_C152"], source: SimManaged); // encrypted, no cfg
    var mge = Aircraft("mge-asobo-aircraft-c152", cfgs: [("Asobo_C152", null)]);
    var index = AircraftIndex.Build([mge, official]);
    Assert.Equal(AircraftRole.Mod, index.RoleOf(mge)); Assert.Equal([official], index.Groups["Asobo_C152"].LockedBases);
}

[Fact] public void Livery_joins_its_base_containers_group_and_variants_are_not_liveries()
{
    var crj = Aircraft("aerosoft-crj", cfgs: [("Aerosoft_CRJ_900", null), ("Aerosoft_CRJ_900_Variant", "Aerosoft_CRJ_900")]);
    var sas = Aircraft("AS_CRJ_900_SAS_New_EIGEH", cfgs: [("SAS_EIGEH", "Aerosoft_CRJ_900")]);
    var index = AircraftIndex.Build([sas, crj]);
    Assert.Equal(AircraftRole.Livery, index.RoleOf(sas)); Assert.Equal(AircraftRole.Provider, index.RoleOf(crj));
    Assert.Equal([sas], index.Groups["Aerosoft_CRJ_900"].Liveries);
}

[Fact] public void Livery_without_installed_aircraft_still_gets_a_group() { … Assert.Empty(group.Providers); Assert.Single(group.Liveries); }
[Fact] public void Package_without_cfg_attaches_by_airplane_folder_then_by_dependency_else_none() { … } // flybywire_A320Neo_Cabin, simflight-C152-interior-navtablet
[Fact] public void Air_traffic_is_not_parsed_for_liveries() { … Assert.Equal(AircraftRole.AirTraffic, …); Assert.Empty(index.Groups); }
[Fact] public void Package_with_several_containers_is_in_each_group_and_lists_them_sorted() { … Assert.Equal(["A", "B"], index.ContainersOf(p)); }
[Fact] public void Result_does_not_depend_on_input_order() { … }
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement** the two passes above.
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: index aircraft containers, liveries and mods"`

---

### Task 13: Content analysis

**Files:**
- Create: `src/AddonManager.Core/Content/ContentAnalyzer.cs`
- Test: `tests/AddonManager.Core.Tests/Content/ContentAnalyzerTests.cs`

**Interfaces:**
- Consumes: `CategoryRules` (2), `ContentSettings` (9), `AirportLinker` (10), `AirportCatalog` (11), `AircraftIndex` (12).
- Produces:
  - `public sealed record ContentAnalysis(IReadOnlyDictionary<string, Category> Categories, IReadOnlyList<AirportLink> AirportLinks, IReadOnlyDictionary<AirportCode, Airport> Airports, IReadOnlyList<BuiltInReplacement> ReplacesBuiltIn, AircraftIndex Aircraft, IReadOnlyList<PackageContent> Excluded);` — `Categories` keyed by normalised path, case-insensitive.
  - `public static class ContentAnalyzer { ContentAnalysis Analyze(IReadOnlyList<PackageContent> contents, ContentSettings settings); }`

This is the single entry point plans 3–6 consume. `Categories[path] = CategoryRules.Final(contentType, HasBgl, IsAirTraffic, Aircraft.RoleOf(c) != None)`. `Excluded` = contents where `AirportLinker.IsExcluded` and they have records (for Settings and for "opt back in").

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Gsx_pro_without_bgl_is_other_and_air_traffic_is_other()
{
    var a = ContentAnalyzer.Analyze([Scenery("fsdreamteam-gsx-pro", hasBgl: false), AirTraffic("fsltl-traffic-base")], new());
    Assert.All(a.Categories.Values, c => Assert.Equal(Category.Other, c));
}

[Fact] public void Unmatched_aircraft_package_without_cfg_is_other() { … }
[Fact] public void Excluded_lists_packages_over_the_threshold_with_records() { … }
[Fact] public void Analysis_wires_links_airports_and_aircraft_together() { … Assert.True(a.Airports[Code("EHAM")].HasRealScenery); }
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: analyse scanned content in one pass"`

---

### Task 14: SQLite scan cache (Cache track)

**Files:**
- Create: `src/AddonManager.Infrastructure/ContentJson.cs`, `SqliteScanCache.cs`
- Modify: `src/AddonManager.Infrastructure/AddonManager.Infrastructure.csproj` (add `Microsoft.Data.Sqlite`, latest 10.0.x, exact version pinned), `docs/superpowers/specs/2026-10-25-mvp-design.md` section 11 (scan cache line → "SQLite, disposable: per Package the change marker and the facts read from its files (manifest data, airport records, BGL section types, aircraft.cfg facts, airplane folders, thumbnail path), and GSX profiles (plan 4). Links, Airports and aircraft groups are recomputed from these on every analysis.")
- Test: `tests/AddonManager.Infrastructure.Tests/SqliteScanCacheTests.cs`

**Interfaces:**
- Consumes: `IScanCache`, `PackageContent` (Task 1), `JsonConverters` (Task 9; the Cache track starts from the Task 9 commit).
- Produces:
  - `public sealed class SqliteScanCache(string filePath) : IScanCache, IDisposable` with `bool IsFallback { get; }`
  - `internal static class ContentJson { JsonSerializerOptions Options { get; } }`
  - `AppPaths.ScanCacheFile` = `Path.Combine(CacheDirectory, "scan-cache.db")`.

Schema (`PRAGMA user_version = SqliteScanCache.FormatVersion`, a `const int` starting at 1). **Bump `FormatVersion`** in the same commit whenever `PackageContent`'s shape changes or any reader's output for the same files changes (a fixed BGL or cfg rule, a new field in plan 4): old rows would otherwise be reused under unchanged markers. The bump rebuilds the cache, as for any other version.

```sql
CREATE TABLE package (
  path    TEXT PRIMARY KEY COLLATE NOCASE,
  content TEXT NOT NULL   -- PackageContent as JSON (ContentJson.Options)
);
```

Behaviour (ANALYS decision "SQLite", C6): raw ADO.NET; connection string with `Pooling=False` (so a corrupt file can be deleted) and `Default Timeout=1` (a second instance falls back after about a second, not 30); default rollback journal (no WAL). The connection opens lazily on first use, creating folder and file then (constructing never touches disk). `Upsert` and `Remove` run in one transaction each. Not thread-safe: one caller at a time (`ScanService` is its only user and runs single-flight). A `user_version` other than `FormatVersion`, or a `SqliteException` that says the file isn't a database, closes the connection, deletes `scan-cache.db` and `scan-cache.db-journal` and recreates it. A busy or locked database (`SQLITE_BUSY`/`SQLITE_LOCKED`, e.g. a second app instance) switches this instance to an in-memory fallback for the rest of its life, with `IsFallback = true`; the fallback starts with the rows this instance already loaded (empty if the first load hit the lock); it never throws for that.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Round_trips_content_including_airports_and_aircraft() { cache.Upsert([Sample()]); Assert.Equivalent(Sample(), new SqliteScanCache(file).LoadAll().Single()); }
[Fact] public void Upsert_replaces_by_path_case_insensitively() { … Assert.Single(cache.LoadAll()); }
[Fact] public void Remove_deletes_rows() { … }
[Fact] public void Constructing_creates_nothing() { _ = new SqliteScanCache(Path.Combine(dir, "sub", "c.db")); Assert.False(Directory.Exists(Path.Combine(dir, "sub"))); }
[Fact] public void Garbage_file_is_replaced_by_an_empty_cache() { File.WriteAllText(file, "not a database"); using var c = new SqliteScanCache(file); Assert.Empty(c.LoadAll()); c.Upsert([Sample()]); Assert.Single(c.LoadAll()); Assert.False(c.IsFallback); }
[Fact] public void Other_schema_version_is_rebuilt() { … PRAGMA user_version = 99 … Assert.Empty(…LoadAll()); }
[Fact] public void Locked_file_falls_back_to_memory_without_throwing()
{
    using var holder = OpenExclusive(file);   // BEGIN EXCLUSIVE on a second connection
    using var cache = new SqliteScanCache(file);
    cache.Upsert([Sample()]);
    Assert.True(cache.IsFallback); Assert.Single(cache.LoadAll());
}
```

Load speed with the real 40,000 generic records is measured in Task 20, not here (a timed unit test would be flaky on CI runners).

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: cache scanned content in SQLite"`

---

### Task 15: Incremental content scanner

**Files:**
- Create: `src/AddonManager.Core/Content/ContentScanner.cs`, `tests/AddonManager.Core.Tests/Fakes/FakeContentReader.cs`, `InMemoryScanCache.cs`
- Test: `tests/AddonManager.Core.Tests/Content/ContentScannerTests.cs`

**Interfaces:**
- Consumes: `IPackageContentReader`, `IScanCache`, `ScanTarget`, `PackageContent`, `ChangeMarker` (Task 1), `PathNormalizer`.
- Produces:
  - `public sealed record ScanProgress(int Done, int Total, string? Current);`
  - `public sealed record ContentScanResult(IReadOnlyList<PackageContent> Contents, int Read, int Reused, int Failed);`
  - `public sealed class ContentScanner(IPackageContentReader reader, IScanCache cache)` with `ContentScanResult Scan(IReadOnlyList<ScanTarget> targets, IReadOnlyList<PackageContent> cached, IReadOnlyList<string> keepUnder, bool full, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)` — `cached` is what the caller already loaded with `cache.LoadAll()` (so a run deserialises the cache once; tests pass `cache.LoadAll()`); the scanner only writes to `cache`.

Behaviour (spec 7.3):
- Targets are processed in the order given (the caller orders them Community → Libraries → Sim-managed).
- Per target: `ReadMarker`; `null` → the Package vanished: not returned, `Failed++`. If `!full` and the cache holds the same path with an equal marker → reuse (`Reused++`) as `cached with { Name = target.Name, Source = target.Source, Manifest = target.Manifest }`: the source changes without the marker when a Library is registered or removed. Otherwise `Read`; `null` → vanished as above. An `IOException` or `UnauthorizedAccessException` from `ReadMarker` or `Read` (C2) → `Failed++`, the cache row is left as it is, and the old cached content (if any) is returned with the target's `Source`, `Name` and `Manifest` as above.
- A vanished target's cache row is removed.
- Fresh reads are upserted in batches of 50 and at the end. On cancellation the batch read so far is upserted, then `OperationCanceledException` propagates.
- Cache rows whose path is neither a target nor under a `keepUnder` root (the unreachable Libraries) are removed; rows under `keepUnder` are kept **and returned**, so offline content keeps its links (Review Focus 2).
- Progress is reported after each target.

- [ ] **Step 1: Write the failing tests** (`Scan(targets, keepUnder, full)` is a test helper: `scanner.Scan(targets, cache.LoadAll(), keepUnder, full, progress: null, default)`)

```csharp
[Fact] public void Unchanged_marker_reuses_the_cache_and_changed_marker_rereads()
{
    Scan(targets, [], full: false);
    reader.Touch("b");                                        // new marker for b
    var r = Scan(targets, [], full: false);
    Assert.Equal(1, r.Read); Assert.Equal(targets.Count - 1, r.Reused);
}

[Fact] public void Full_rescan_rereads_everything() { … Assert.Equal(targets.Count, r.Read); }
[Fact] public void Reads_in_the_given_order() { …; Assert.Equal(["community-pkg", "lib-pkg", "sim-pkg"], reader.ReadOrder); }
[Fact] public void Vanished_package_is_dropped_from_cache_and_result() { … reader.Vanish("b"); … Assert.DoesNotContain(…); Assert.Equal(1, r.Failed); }
[Fact] public void Unreadable_package_keeps_its_old_content() { … reader.ThrowOn("b", new IOException()); … Assert.Contains(old, r.Contents); Assert.Equal(1, r.Failed); }
[Fact] public void Inaccessible_marker_keeps_the_old_content() { … reader.ThrowOnMarker("b", new UnauthorizedAccessException()); … Assert.Contains(r.Contents, c => c.Name.Value == "b"); }
[Fact] public void Reused_content_takes_the_targets_current_source()
{
    Scan([T("x", PackageSource.Community)], [], false);
    var r = Scan([T("x", PackageSource.Library)], [], false);   // same path and marker, Library now registered
    Assert.Equal(1, r.Reused); Assert.Equal(PackageSource.Library, r.Contents.Single().Source);
}
[Fact] public void Content_under_an_unreachable_library_is_kept_and_returned()
{
    Scan([T(@"E:\Addons\x"), T(@"C:\Spel\Addons\y")], [], false);
    var r = Scan([T(@"C:\Spel\Addons\y")], keepUnder: [@"E:\Addons"], full: false);
    Assert.Contains(r.Contents, c => c.Name.Value == "x");
}
[Fact] public void Removed_package_leaves_the_cache() { … Assert.DoesNotContain(cache.LoadAll(), …); }
[Fact] public void Cancellation_keeps_work_done_so_far() { … cts.Cancel() after 60 reads … Assert.Throws<OperationCanceledException>(…); Assert.True(cache.LoadAll().Count >= 50); }
[Fact] public void Reports_progress_per_target() { … Assert.Equal(targets.Count, progress.Last().Done); }
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: scan package content incrementally"`

---

### Task 16: Scan targets

**Files:**
- Create: `src/AddonManager.Infrastructure/ScanTargetCollector.cs`
- Modify: `src/AddonManager.Core/Packages/PackageIndex.cs` (`AllCopies`), `src/AddonManager.Infrastructure/ManifestReader.cs` (share mode)
- Test: `tests/AddonManager.Infrastructure.Tests/ScanTargetCollectorTests.cs`

**Interfaces:**
- Consumes: `ClassifiedEntry`, `EntryClass`, `PackageIndex`, `PackageCopy`, `ManifestReader`, `ScanTarget`, `PackageSource`, `PathNormalizer`.
- Produces:
  - `public static class ScanTargetCollector { IReadOnlyList<ScanTarget> Collect(IReadOnlyList<ClassifiedEntry> community, string communityPath, PackageIndex index, string? officialPath); }`
  - `PackageIndex.AllCopies` (`IReadOnlyList<PackageCopy>`): every copy, ordered by Library registration order (`Libraries`), then normalised path (`OrdinalIgnoreCase`).
  - `ManifestReader` opens `manifest.json` with a `FileStream` (`FileShare.ReadWrite | FileShare.Delete`) instead of `File.ReadAllText` (Global Constraints), read through a `StreamReader` so a UTF-8 BOM is still dropped; its plan-1 tests must still pass.

Order and sources (spec 7.3, decision: "Community first" means what the sim loads from Community):
1. **Community:** Fixed entries (`<communityPath>\<name>`, source Community), External entries whose target exists and has a manifest (the target path, source Community, `Name` = the entry name, the name the sim loads it under), and the copies Managed entries point to (source Library), in Community name order. An External entry whose target is a registered Library copy (`index.FindByPath(target) != null`, a link under another name) gives no Community target: that copy is scanned once, as a Library copy.
2. **Libraries:** every other copy in `index` (all copies, including inactive Duplicate copies, C8), in `PackageIndex.AllCopies` order.
3. **Sim-managed:** each direct subfolder of `officialPath` with a `manifest.json`, by name (source SimManaged). None when `officialPath` is `null` or missing.

Duplicates by normalised path are dropped (first wins). Manifests: Library copies reuse `PackageCopy.Manifest`; others are read with `ManifestReader`. Offline, Stale, Broken, Ignored and Stray entries give no target.

- [ ] **Step 1: Write the failing tests** (temp folders: Community with a Fixed folder, an External junction, a Managed junction; a Library; an Official folder)

```csharp
[Fact] public void Orders_community_then_libraries_then_sim_managed() { … Assert.Equal(["fixed-pkg", "external-pkg", "managed-pkg", "unlinked-pkg", "asobo-airport-x"], names); }
[Fact] public void Managed_entry_is_scanned_once_at_its_library_path_as_library_source() { … }
[Fact] public void External_link_into_a_library_copy_is_scanned_as_that_library_copy() { … Assert.Single(targets, t => t.Path == copyPath && t.Source == PackageSource.Library); }
[Fact] public void External_target_is_named_after_the_entry() { … Assert.Equal("orbx-link-name", target.Name.Value); }
[Fact] public void Manifest_can_be_read_while_another_process_holds_it_open_for_writing() { using var w = new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite); Assert.Equal("Title", ManifestReader.Read(manifest).Title); }
[Fact] public void Official_root_folders_without_manifest_are_not_targets() { … } // spec C7: WASM cache folders
[Fact] public void Missing_official_folder_gives_no_sim_managed_targets() { … }
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: collect scan targets in load order"`

---

### Task 17: Scan service

**Files:**
- Create: `src/AddonManager.Infrastructure/ScanService.cs`, `tests/AddonManager.Infrastructure.Tests/Fakes/GatedContentReader.cs` (an `IPackageContentReader` over temp folders with a `SemaphoreSlim? Gate` awaited in `ReadMarker`, `HasBglResult`, counters `MarkerCalls`/`ReadCalls` and `HasBglCallsFor(path)`), `tests/AddonManager.Infrastructure.Tests/Fakes/InMemoryScanCache.cs` (Infrastructure.Tests can't see Core.Tests' fakes)
- Test: `tests/AddonManager.Infrastructure.Tests/ScanServiceTests.cs` (temp folders)

**Interfaces:**
- Consumes: `LibraryScanner.Discover`, `CommunityReader`, `CommunityClassifier`, `PackageIndex`, `ScanTargetCollector` (16), `ContentScanner` (15), `ContentAnalyzer` (13), `CategoryRules.Provisional` (2), `UserState` (+ `Content`, Task 9).
- Produces:
  - `public sealed record DiscoveryOutcome(PackageIndex Index, IReadOnlyList<ClassifiedEntry> Community, IReadOnlyList<ScanTarget> Targets, IReadOnlyDictionary<string, Category> ProvisionalCategories);`
  - `public sealed record ScanOutcome(DiscoveryOutcome Discovery, ContentScanResult Content, ContentAnalysis Analysis, TimeSpan Elapsed);`
  - `public sealed class ScanService(IPackageContentReader reader, IScanCache cache, string communityPath, string? officialPath) : IDisposable` with `Task<ScanOutcome> RunAsync(UserState state, bool full, Action<DiscoveryOutcome>? onDiscovered, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)`. `Dispose` cancels the run in progress and drops the queued one (their tasks end cancelled).

Behaviour (spec 7.2–7.3, ANALYS decision "Category"):
- **Snapshot:** every `RunAsync` call snapshots its input synchronously, on the calling thread, before anything else: a copy of `state.Libraries` and `state.Content.Clone()` (Task 9). Runs only ever use snapshots, so the UI can change `state` at any time.
- Runs on the thread pool. A run loads the cache once (`cache.LoadAll()`). Pass 1: discover the snapshot's Libraries, read and classify Community, collect targets, compute provisional Categories for Library and Community targets only (`HasBgl` from the loaded content when present, else `reader.HasBgl`; Sim-managed Packages are never switched, so they aren't needed for on/off), call `onDiscovered`. Pass 2: `ContentScanner.Scan` with the loaded rows and `keepUnder` = the unreachable Libraries, then `ContentAnalyzer.Analyze` with the snapshot settings.
- **Single-flight** (Review Focus 5): while a run is in progress, further calls share one queued follow-up run and get its task; at most one run and one queued. The queued run uses `full` if any of its callers asked for it, and the snapshot, `onDiscovered` and `progress` of its **last** caller (the newest state wins).
- **Cancellation:** a caller's token cancels only that caller's wait (`task.WaitAsync(token)`); it never cancels a run other callers share. A run is cancelled only by `Dispose`. `RunAsync` is not an `async` method: with a token that can't be cancelled it returns the shared task itself, so coalesced callers get the same `Task` instance.
- Never saves `UserState` and never touches Community (C1).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Discovery_is_reported_before_content_is_read() { … Assert.True(discoveredAt < firstReadAt); }
[Fact] public async Task Provisional_category_uses_layout_bgl_presence() { reader.HasBglResult["gsx-pro"] = false; … Assert.Equal(Category.Other, d.ProvisionalCategories[path]); }
[Fact] public async Task Concurrent_requests_coalesce_into_one_follow_up_run()
{
    reader.Gate = new SemaphoreSlim(0);
    var first = service.RunAsync(state, false, null, null, default);
    var second = service.RunAsync(state, false, null, null, default);
    var third = service.RunAsync(state, true, null, null, default);
    reader.Gate.Release(int.MaxValue);
    await Task.WhenAll(first, second, third);
    Assert.Same(second, third);
    Assert.Equal(2 * targetCount, reader.MarkerCalls);   // two runs
    Assert.Equal(2 * targetCount, reader.ReadCalls);     // empty cache, then a full follow-up: nothing reused
}
[Fact] public async Task Follow_up_run_uses_the_last_callers_state() { … second call with state A, third with state B (an extra Library) … Assert.Contains(B's package, (await third).Content.Contents.Select(c => c.Name)); }
[Fact] public async Task Cancelling_one_caller_does_not_cancel_the_shared_run() { … cancel second's token … await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second2); Assert.NotNull(await third); }
[Fact] public async Task Changing_state_during_a_run_does_not_affect_it() { … while gated: state.Content.AddStopWord("X"); state.Libraries.Clear(); … release; the run still saw the original Library }
[Fact] public async Task Changing_state_after_queueing_does_not_affect_the_queued_run() { … first run gated; third = RunAsync(stateB); then stateB.Libraries.Clear() … release; (await third) still saw stateB's Library }
[Fact] public async Task Provisional_categories_skip_sim_managed_targets() { … Assert.Equal(0, reader.HasBglCallsFor(simPkg)); Assert.False(d.ProvisionalCategories.ContainsKey(simPath)); }
[Fact] public async Task Unreachable_library_content_is_kept() { … }
[Fact] public async Task User_state_is_not_saved_and_community_is_unchanged() { … }
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: run discovery and content scan as one service"`

---

### Task 18: Watchers

**Files:**
- Create: `src/AddonManager.Infrastructure/ContentWatcher.cs`
- Modify: `src/AddonManager.Infrastructure/AddonManager.Infrastructure.csproj` (`<InternalsVisibleTo Include="AddonManager.Infrastructure.Tests" />` for the error hook)
- Test: `tests/AddonManager.Infrastructure.Tests/ContentWatcherTests.cs`

**Interfaces:**
- Consumes: nothing from this plan.
- Produces: `public sealed class ContentWatcher(string communityPath, IReadOnlyList<string> libraryPaths, TimeSpan quietPeriod) : IDisposable` with `event Action? RescanNeeded`, `event Action? CommunityChanged` and `void Start()`. Plan 5 subscribes `RescanNeeded` to `ScanService.RunAsync(state, full: false, …)` and `CommunityChanged` to the runtime reconcile of spec 7.4 (with own-change suppression from spec 12; not this plan).

Behaviour (spec 7.3): Community is watched non-recursively (`DirectoryName | FileName`); each existing Library recursively (`DirectoryName | FileName | LastWrite | Size`), `InternalBufferSize = 65536`. Library paths that don't exist at `Start()` are skipped, and a watcher that fails (drive unplugged) stays stopped: plan 5 recreates the watcher after each scan with the reachable Libraries. Any event, and any `Error` (buffer overflow), restarts a quiet-period timer; `RescanNeeded` fires once when it expires, and `CommunityChanged` fires once with it when at least one of the events in that quiet period came from the Community watcher (an `Error` from the Community watcher counts). Changes inside linked Packages are seen through the Library watchers. Dispose stops everything; no event after Dispose.

- [ ] **Step 1: Write the failing tests** (temp folders, `quietPeriod` 200 ms, waits bounded at 5 s)

```csharp
[Fact] public async Task A_burst_of_changes_raises_one_rescan() { for (var i = 0; i < 50; i++) File.WriteAllText(Path.Combine(lib, $"p{i}", "manifest.json"), "{}"); await Task.Delay(1000); Assert.Equal(1, raised); }
[Fact] public async Task Community_is_watched_non_recursively() { … change inside a real Community subfolder … Assert.Equal(0, raised); … new top-level entry … Assert.Equal(1, raised); }
[Fact] public async Task Library_changes_at_any_depth_are_seen() { … }
[Fact] public async Task Community_changed_fires_only_for_community_events() { … library change → RescanNeeded 1, CommunityChanged 0; new Community entry → RescanNeeded 2, CommunityChanged 1 }
[Fact] public async Task Overflow_raises_a_rescan() { watcher.SimulateError(new InternalBufferOverflowException()); await WaitUntil(() => raised == 1, TimeSpan.FromSeconds(5)); } // internal test hook; fires after the quiet period
[Fact] public void Missing_library_is_skipped_without_throwing() { … }
[Fact] public async Task No_event_after_dispose() { … }
```

- [ ] **Step 2: Run to verify failure** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat: watch community and libraries for changes"`

---

### Task 19: CLI `scan` and `export-content`

**Files:**
- Create: `src/AddonManager.Cli/ScanCommand.cs`, `ExportContentCommand.cs`
- Modify: `src/AddonManager.Cli/Program.cs`, `CLAUDE.md` (add the two commands under Commands)

**Interfaces:**
- Consumes: `InstallLocator`, `JsonUserStateStore`, `AppPaths`, `LibraryProposer`, `DiskFolderTree` (as in `StatusCommand`), `PackageContentReader` (8), `SqliteScanCache` (14), `ScanService` (17).
- Produces:
  - `AddonManager.Cli scan [--full] [--token-links <file>]` — `--token-links` also writes every Token link as a tab-separated line `<package>`, `<code>`, `<token>`, `?` (four columns) for Rickard's review (Task 21). Runs `ScanService` with the real cache (`AppPaths.ScanCacheFile`). Uses saved Libraries, or the proposal when none are saved (like `status`). Writes only the cache (C1). Prints: elapsed time; targets per source; read/reused/failed; unreadable BGL count; Packages per Category; Airports with real scenery; links per reason; every Token link as `<package>  <code>  "<token>"`; excluded Packages with their code counts; aircraft groups with Mods, Liveries without provider and Attached Packages; Packages with role None; Sim-managed Packages with "airport" in their name and no Airport link.
  - `AddonManager.Cli export-content --out <file>` — runs the same scan, then writes the content fixture (format below). Read-only apart from `--out` and the cache.

Content fixture (`rickard-content.json`, UTF-8, CRLF, indented, user profile replaced with `%USERPROFILE%`):

```json
{ "exported": "2026-10-26",
  "packages": [
    { "name": "brickbybrickdesign-airport-eham-amsterdam", "path": "C:\\Spel\\Addons\\…", "source": "Library",
      "title": "…", "creator": "…", "contentType": "SCENERY", "dependencies": [],
      "hasBgl": true, "bglSectionTypes": [3, 101], "unreadableBgl": 0,
      "airports": [ { "code": "EHAM", "name": "Amsterdam Schiphol", "lat": 52.3086, "lon": 4.7639 } ],
      "isAirTraffic": false,
      "aircraft": [ { "container": "Aerosoft_CRJ_900", "baseContainer": null } ],
      "airplaneFolders": ["Aerosoft_CRJ_900"], "encryptedAirplaneFolders": [], "hasThumbnail": true } ] }
```

Mapping to `PackageContent` (Task 22's loader does the reverse): `name`, `path`, `source` → same; `title`, `creator`, `contentType`, `dependencies` (plus `packageVersion`) → `Manifest`; `hasBgl`, `bglSectionTypes`, `airports`, `isAirTraffic`, `airplaneFolders`, `encryptedAirplaneFolders` → same; `unreadableBgl` → `UnreadableBglCount`; `aircraft` → `AircraftConfigs`; `hasThumbnail` → `ThumbnailPath` = `"<path>\ContentInfo\<name>\Thumbnail.jpg"` when true, else `null`; `Marker` is a constant.

Excluded Packages (C4) export their records as `"codes": ["…", …]`, one compact string array on a single line, instead of `airports` — this is the committed list of generic codes from ANALYS decision "Collision report", roughly 0.6 MB in all. Codes linked by reason PackageName keep their full record in `airports` (the encrypted official airports need name and position). Everything else is exported in full. `ContentFixtureLoader` (Task 22) turns `codes` back into records without name or position.

- [ ] **Step 1: Implement** `ScanCommand` and `ExportContentCommand`; exit code 1 with a message when no install is found or user data is corrupt (as `status`).
- [ ] **Step 2: Smoke-test on the dev machine** — `dotnet run --project src/AddonManager.Cli -- scan`. Expected: "No MSFS 2020 install found …", exit code 1. (The real run is Task 21.)
- [ ] **Step 3: Commit** — `git commit -m "feat: add scan and export-content commands"`

---

### Task 20: Opt-in tests against the local install

**Files:**
- Create: `tests/AddonManager.Sim2020.Tests/Integration/SimFactAttribute.cs`, `LocalInstallTests.cs`
- Modify: `tests/AddonManager.Sim2020.Tests/AddonManager.Sim2020.Tests.csproj` (reference `AddonManager.Infrastructure`)

**Interfaces:**
- Consumes: `InstallLocator`, `PackageContentReader`, `SqliteScanCache`, `ScanService`, `JsonUserStateStore`, `LibraryProposer`, `TempFolder` in Sim2020.Tests (Task 7).
- Produces: `[SimFact]` — a `FactAttribute` that sets `Skip` unless the environment variable `MMAM_SIM_TESTS` is `1`. CI never sets it.

- [ ] **Step 1: Write the tests**

```csharp
[SimFact] public void Real_eham_package_reads_eham_near_schiphol() { … Assert.InRange(eham.Position!.Value.Latitude, 52.25, 52.36); }
[SimFact] public void Real_generic_airports_hold_about_forty_thousand_codes() { … Assert.InRange(count, 35_000, 45_000); }
[SimFact] public async Task Incremental_scan_without_changes_takes_under_two_seconds()
{
    var cacheFile = Path.Combine(temp.Root, "scan-cache.db");
    await Service(cacheFile).RunAsync(state, full: true, null, null, default);
    var second = await Service(cacheFile).RunAsync(state, full: false, null, null, default);   // fresh instance, as on app start
    output.WriteLine($"incremental: {second.Elapsed}");
    Assert.True(second.Elapsed < TimeSpan.FromSeconds(2));
}
```

Also `[SimFact] Cached_startup_load_and_analysis_takes_under_one_second`: after a full scan into the temp cache, a fresh `SqliteScanCache` times `LoadAll()` plus `ContentAnalyzer.Analyze` alone, writes the time to the output and asserts under 1 s (spec 7.2's "usable with cached data within about 1 second"; Task 21 stops and reports if it fails).

Uses a temp cache file, never `AppPaths.ScanCacheFile`, and saved Libraries or the proposal.

- [ ] **Step 2: Run on the dev machine** — `dotnet test … --filter FullyQualifiedName~LocalInstallTests` → all skipped.
- [ ] **Step 3: Commit** — `git commit -m "test: add opt-in tests against the local install"`

---

### Task 21: Rickard runs the scan and the export (manual)

**Owner:** Rickard, on the sim machine, from this branch, with all tracks merged. About 5 minutes plus scan time.

**Files:**
- Create: `tests/AddonManager.Core.Tests/Fixtures/rickard-content.json`, `tests/AddonManager.Core.Tests/Fixtures/reviewed-token-links.tsv`

- [ ] **Step 1: Full scan** — `dotnet run --project src/AddonManager.Cli -- scan --full --token-links tests/AddonManager.Core.Tests/Fixtures/reviewed-token-links.tsv`. Keep the output for the PR.
- [ ] **Step 2: Incremental scan** — run `scan` again. Expected: elapsed under 2 s, almost everything reused.
- [ ] **Step 3: Opt-in tests** — `$env:MMAM_SIM_TESTS = '1'; dotnet test ModernMsfsAddonManager.slnx --filter FullyQualifiedName~LocalInstallTests` → PASS.
- [ ] **Step 4: Export** — `dotnet run --project src/AddonManager.Cli -- export-content --out tests/AddonManager.Core.Tests/Fixtures/rickard-content.json`.
- [ ] **Step 5: Review the Token links** — in `reviewed-token-links.tsv`, replace every `?` with `ok` (the Package really belongs to that Airport) or `stop` (the token is a common word; Task 22 adds it to the stop-list). Rickard's verdict, not the implementer's: no `?` may remain. A token marked `stop` on one row and `ok` on another is a conflict (stop words are global): decide which wins before committing.
- [ ] **Step 6: Commit and push** — `git commit -m "test: add content fixture from the real install"`, then `git push`, so the Task 22 agent can fetch the fixture and the TSV (see Handover).

**Stop and report** if the unreadable BGL count is large, if a real case from spec 6.5/6.6 comes out differently from Task 22's expectations, if an aircraft-type Library or Community Package has role None (an aircraft the container rules miss), if `scan` lists a Sim-managed airport Package without a link (an airport that only a manual link could fix, which Sim-managed Packages don't get), or if step 2 is over 2 s.

---

### Task 22: Content scenario tests and collision report

**Files:**
- Create: `tests/AddonManager.Core.Tests/ContentFixtureLoader.cs`, `Scenarios/ContentScenarioTests.cs`, `Scenarios/CollisionReportTests.cs`
- Modify: `src/AddonManager.Core/Content/ContentSettings.cs` (`DefaultStopList` gets the token of every `stop` row in the reviewed TSV)

**Interfaces:**
- Consumes: the fixture and Rickard's reviewed `reviewed-token-links.tsv` (Task 21) — **stop and report** if either is missing, the TSV still has a `?`, or a `stop` row's token also appears in an `ok` row; `ContentAnalyzer` (13), `AirportLinker` (10), `ContentSettings` (9).
- Produces: `ContentFixtureLoader.Load(string name) → IReadOnlyList<PackageContent>` (expands `%USERPROFILE%` to `C:\Users\user`, like `FixtureLoader`; marker is a constant).

- [ ] **Step 1: Write the scenario tests** (expected values from spec 6.4–6.6; if the fixture disagrees, stop and report — don't change an expectation to make it pass)

```csharp
[Fact] public void Eham_has_its_three_library_packages()
    => Assert.Equal(["brickbybrickdesign-airport-eham-amsterdam", "duijffmedia-airport-eham-amsterdam", "duijffmedia-schiphol-amsterdam-mega-enhancement-lighting"],
        LinkedNames("EHAM", PackageSource.Library));   // distinct names, sorted (C8)

[Fact] public void Lszr_and_gran_canaria_have_no_airport() { Assert.Empty(LinksOf("005builds-lszrsceneryv11")); Assert.Empty(LinksOf("grancanaria")); }
[Fact] public void Voloport_and_neofly_are_kept_out()
{
    Assert.DoesNotContain(a.AirportLinks, l => l.Code.Value.StartsWith("VOLC") || l.Code.Value == "NEOF2");
    Assert.DoesNotContain(a.Airports.Keys, c => c.Value.StartsWith("VOLC") || c.Value == "NEOF2");
}
[Fact] public void Verified_large_packages_are_excluded()
    => Assert.Superset(new[] { "fs-base-genericairports", "fs-base-nav", "navigraph-navdata", "fsdreamteam-gsx-world-of-jetways-2020", "microsoft-nordics2-point-of-interest" }.ToHashSet(), Names(a.Excluded));
[Theory]
[InlineData("asobo-airport-eddf-frankfurt", "EDDF")] [InlineData("asobo-airport-egll-heathrow", "EGLL")] [InlineData("asobo-airport-kden-denver", "KDEN")]
[InlineData("asobo-airport-lemd-adolfo-suarez", "LEMD")] [InlineData("asobo-airport-omdb-dubai-intl", "OMDB")] [InlineData("asobo-airport-heca-cairo", "HECA")]
[InlineData("asobo-airport-fact-cape-town", "FACT")] [InlineData("asobo-airport-lfcs-saucats", "LFCS")]
public void Encrypted_official_airports_have_code_name_and_position(string package, string code)
{ Assert.Contains(code, LinkedCodes(package)); Assert.NotNull(a.Airports[Code(code)].Name); Assert.NotNull(a.Airports[Code(code)].Position); }
[Fact] public void Five_character_codes_link_by_record() { Assert.Contains(a.AirportLinks, l => l.Code.Value == "YALR1" && l.Reason == AirportLinkReason.Record); }
[Fact] public void Other_category_cases() { Assert.Equal(Category.Other, Cat("fsdreamteam-gsx-pro")); Assert.Equal(Category.Other, Cat("neofly-neofly-smokes")); Assert.Equal(Category.Other, Cat("fsltl-traffic-base")); }
[Fact] public void Sal1800_packages_share_one_group() => Assert.Equal(2, a.Aircraft.Groups["Cessna-170B-Backcountry"].Providers.Select(p => p.Name).Distinct().Count());   // C8: count names, not copies
[Fact] public void Mge_c152_is_a_mod_of_the_official_c152() => Assert.Equal(AircraftRole.Mod, Role("mge-asobo-aircraft-c152"));
[Fact] public void Packages_without_cfg_attach_to_their_aircraft() { Assert.Equal(AircraftRole.Attached, Role("flybywire_A320Neo_Cabin")); Assert.Equal(AircraftRole.Attached, Role("simflight-C152-interior-navtablet")); }
```

- [ ] **Step 2: Write the collision report** — ANALYS decision "Collision report" and spec 13:

```csharp
// Rickard's reviewed Token links (Task 21): the `ok` rows of reviewed-token-links.tsv. A change in that file is a decision, not a fix.
[Fact] public void Token_links_match_the_reviewed_list()
{
    var links = a.AirportLinks.Where(l => l.Reason == AirportLinkReason.Token).Select(l => (l.Package.Name.Value, l.Code.Value)).Distinct();
    foreach (var row in Report()) output.WriteLine(row);   // every name/title token that is a known code, with its rule outcome
    Assert.Equal(ReviewedTokenLinks("ok").Order(), links.Order());
}
```

`Report()` lists, for every Library and Community Package, each name or title token that is a known code (`AirportLinker.KnownCodes`), with whether it linked, and why not (stop word, not leading, counted record present, not Scenery). Every `stop` row's token is added to `ContentSettings.DefaultStopList` in this task; the test then passes only if those links are gone. The TSV is never edited to make the test pass.

- [ ] **Step 3: Run to verify** → PASS (after stop-list additions, if any). If an expectation in Step 1 fails, stop and report to Rickard.
- [ ] **Step 4: Commit** — `git commit -m "test: add content scenarios and collision report"`

---

## Calls I made (please check)

- Derived results (links, Airports, aircraft groups, record counts) are recomputed from cached facts, not cached (C6; spec 11 updated in Task 14).
- Rule 2 (name/title tokens) applies only to non-Sim-managed Packages; Sim-managed Packages without records use the package-name rule (Task 10).
- The `layout.json` overlap signal for Replaces built-in is left out; the dependency signal is kept (Task 11).
- The generic codes from ANALYS decision "Collision report" live inside the content fixture (as a compact `codes` array per excluded Package), not in a separate file (Task 19).
- Records of Packages excluded by pattern (voloport, neofly, points of interest) are ignored everywhere: no Airports on the map, no known codes for the token rule. Records excluded only by the threshold still supply codes, names and positions (C4).
- `OptedIn` and removed links apply to Sim-managed Packages too; manual links don't (C5).
- Inactive Duplicate copies are analysed like active ones; plan 3 shows only the active copy (C8).
- An encrypted aircraft (`.fsarchive`, no `aircraft.cfg`) provides its container in any source, not only Sim-managed (Task 12, spec 6.6).
- The exclusion list and stop-list are stored as the user's additions and removals on top of the built-in defaults, so new defaults in a later release still reach existing users (Task 9).
- A caller's cancellation only stops its own wait; a shared scan run is cancelled only when the service is disposed (Task 17).
- A Library on an unreachable drive keeps its cached content, so its Airport links don't disappear while it is offline (Task 15).
- A locked cache (second instance) makes that cache instance fall back to memory for the rest of the session instead of failing (Task 14).
- `isAirTraffic=1` makes a Package Other whatever its `content_type` (spec 6.4, Task 2).
- An External entry is scanned under its entry name, the name the sim loads it under; one that points into a registered Library copy is scanned once, as that Library copy (Task 16).
- `ContentWatcher` gets a separate `CommunityChanged` signal now; reacting to it (spec 7.4 runtime changes, own-change suppression from spec 12) is plan 5 (Task 18).
- A Library missing when the watcher starts isn't watched until plan 5 recreates the watcher after the next scan (Task 18).
- Sim-managed Packages get no manual links; `scan` lists Sim-managed airport Packages left without a link, so Task 21 checks this call against real data.
- Liveries whose aircraft isn't installed still get a group, so plan 3 can warn "Livery without aircraft" (Task 12).
- A Package that provides several containers is in every group; plan 3 picks the alphabetically first (spec 6.6).
- Plan 3 owns the Asset pack test; plan 2 provides `BglSectionTypes` (ANALYS decision "Asset pack-testet").
