# Modern MSFS Addon Manager — MVP design

Date: 2026-10-25. Status: draft, revised after a builder check and two independent reviews.

Vocabulary is defined in [GLOSSARY.md](../../../GLOSSARY.md) and used strictly below. Background and the full decision log are in [ANALYS.md](../../../ANALYS.md) (Swedish). Architectural decisions are in [docs/adr](../../adr).

## 1. Intent

Rickard manages a large MSFS 2020 setup (118 Community entries, about 250 Library Packages in four Libraries) with MSFS Addons Linker. He likes two things about it: turning content on and off quickly, and a map of installed airports plus the airports built into the sim. He dislikes its ugly, clunky interface, and misses a link to GSX Pro profiles and a way to find scenery he doesn't have.

This app replaces Addons Linker for that workflow. Scenery is the focus, aircraft come second, everything else is "Other" with plain on/off. It must start fast, never put the user's files at risk, and look as calm and clear as BOB Dispatcher (screenshots in `C:\Users\ricka\OneDrive\Bilder\bob\`).

## 2. Goals

1. Turn content on and off at any level (Catalog, Addon, single Package) by linking Packages into Community with directory junctions.
2. Organise Addons in the user's own Catalogs, seeded from the Library folders.
3. Group the Packages of one Airport into one Addon automatically.
4. Show Aircraft with their Liveries.
5. Show a map of Airports with real scenery, with an option to show all airports.
6. Show GSX Pro profile status per Airport and install profiles by drag and drop.
7. Offer deep links to find scenery and GSX profiles the user doesn't have.
8. Start instantly from cached state; do all scanning in the background.
9. Take over an existing Addons Linker setup without manual work.

## 3. Non-goals (not in this MVP)

- MSFS 2024 (but see constraint C6).
- Presets.
- Per-flight activation (SimBrief, routes).
- Installing addons from archives.
- GSX profile analyser (stands and pushback paths on the map).
- Linking scenery to an Airport by geographic proximity.
- Store integrations beyond flightsim.to and SimMarket deep links.
- Localization (English only) and a light theme (dark only).
- Automatic updates.
- **Interaction design.** This spec defines states and rules. What a click, toggle or menu does where is decided when the UI is designed.

## 4. Anti-goals (failure even if it works)

- **Any user file moved, modified or deleted** without an explicit, specific user action. In particular, the app must never delete recursively through a junction or symlink.
- Community left in a broken state after the app is closed, crashes or is uninstalled.
- A UI that freezes during scanning, or a startup that waits for scanning.
- Changes to Sim-managed packages, Fixed entries or Managed elsewhere Packages.
- False alarms that train the user to ignore warnings (ADR 0002). Every warning rule below has been checked against Rickard's real install for this reason.

## 5. Constraints

- **C1.** C# / .NET 10, WPF + WPF-UI, map in WebView2 + Leaflet with tiles that need no API key (ADR 0003). Easy move to .NET 11.
- **C2.** Windows only. Junctions, not symlinks, so no admin rights are needed. .NET has no managed API for creating junctions (`Directory.CreateSymbolicLink` makes symlinks), so creation needs P/Invoke (`FSCTL_SET_REPARSE_POINT`). Creation must be atomic: if setting the reparse point fails, the empty folder is removed. Removal with non-recursive `Directory.Delete(path, false)` removes only the link.
- **C3.** Shipped as a framework-dependent single-file exe on GitHub Releases (with native libraries such as SQLite and WebView2 loader bundled for self-extraction). If the .NET 10 Desktop Runtime is missing, the standard apphost dialog sends the user to Microsoft's download page.
- **C4.** MIT license. Dave's GSX Profile Manager (GPL-3.0) is reference only: no copied or translated code.
- **C5.** Files are never moved; enablement is junctions plus own state reconciled against Community (ADR 0001).
- **C6.** Sim-specific parts (locating the install, folder layouts, content formats, built-in airports, sim process detection) sit behind an abstraction so that MSFS 2024 can be added later.
- **C7.** MSFS 2020 loads Packages only from `Official\<store>` and Community, and only at sim startup. The real folders in the Packages root next to them (for example `orbx-airport-esgg-gothenburg`, `fsdreamteam-gsx-pro`) have no `manifest.json`; they are the sim's WASM cache and module storage, not Packages, and the app ignores them.
- **C8.** English UI, dark theme only.
- **C9.** Package names, container names and Airport codes are compared case-insensitively everywhere.

## 6. Domain rules

### 6.1 Packages and Libraries

- A Package is found by recursively walking a Library. A folder containing `manifest.json` is a Package; the walk does not descend into it.
- Package identity is its folder name (the name the sim uses). The junction in Community gets the same name.
- A Package's display name is its manifest `title`, or its folder name when the title is empty. The folder name is shown as secondary text, because titles repeat (five Packages are titled "Simple Scenery" on Rickard's machine).
- The same name more than once across the Libraries (in two Libraries, or at two depths in one) is a **Duplicate copy**. One copy is the active copy: by default the one Community already points to, otherwise the first by Library registration order, then path; the user can change it. Only the active copy can be linked. Changing the active copy of a linked Package re-points the link.
- Unregistering a Library never deletes anything; its Managed entries become External entries.
- **Missing Packages:** all user data for a Package (choice, Addon membership, Catalog placement, active copy, manual links, flags) is kept while the Package can't be found, for example when a Library drive is unplugged, a folder is renamed, or a tool deletes and re-creates a Package during an update. The Package is shown as missing. When a Package with the same name reappears, it resumes its old place. Only an explicit user action forgets a missing Package.
- **Moved Packages:** a Broken link whose name matches a Package found in a registered Library is a stale Managed entry, not a plain Broken one. The sync dialog offers to re-point it to the new location (preselected) or to turn the Package off, which removes the stale link; both go through the apply path, and the row doesn't come back.

### 6.2 Community entries

Every entry in Community is classified on each reconcile:

| Entry | Rule | App may |
|---|---|---|
| Managed | Junction or symlink whose target is a Package in a registered Library, and whose name is that Package's name (a link to a Library Package under another name is External) | Remove; the app only ever creates junctions |
| Offline | Link whose target lies under an unreachable Library | Shown as offline, never offered for removal |
| External | Junction or symlink pointing outside every registered Library | Show only |
| Fixed | Real folder with `manifest.json` | Show only |
| Ignored | Real folder without `manifest.json` (for example `flybywire-current-install-0`), or a link whose target isn't a Package. The sim ignores it | Show as neutral info |
| Broken | Link whose target doesn't exist and whose name matches no Library Package | Remove the link, after confirmation |
| Stray | Loose file | Show only, with a warning |

- Link targets are compared as resolved, normalised paths, never by entry name: strip the `\\?\` prefix, ignore a trailing backslash (Addons Linker adds one, Neofly doesn't), and resolve relative symlink targets against Community.
- Removing a link must remove only the link itself, never the target's contents.
- **Name taken:** the app never creates a link where any Community entry with that name already exists. The Package then can't be turned on, and the app says which entry holds the name.

### 6.3 State and on/off

- **Only Packages have an on/off state.** It is the user's choice, stored by the app. A Package should be linked exactly when it is on.
- **Addons and Catalogs have no state of their own.** They show On, Off or **Mixed**, computed from the Packages below them.
- On/off can be applied at every level:
  - a **Package** (from its Addon's properties): that Package only;
  - an **Addon**: all its Packages;
  - a **Catalog**: all Packages of all Addons below it, at any depth.
- Turning an Addon or Catalog on or off never changes Addons it only **uses**. Used Addons are shown dimmed in the using Addon's properties and can't be switched from there.
- **Managed elsewhere** is an attribute of a Library, inherited by every Package in it, including Packages that appear there later; it can be overridden per Package. A Package is locked when its active copy lies in such a Library (so a user's own copy of `neofly-neofly-cargo` in `C:\Sim\Addons` isn't locked by marking the Neofly Library). A locked Package's link is never created or removed by the app, by any action at any level; bulk actions skip it and say so. Its Addon shows a lock.
- **Name taken** (6.2) rejects a turn-on at apply time; the choice isn't stored.
- Fixed and External entries appear as read-only Addons with a lock where they belong in the tree.

### 6.4 Categories, tabs and Catalogs

- Category comes from manifest `content_type`: `SCENERY` → Scenery, `AIRCRAFT` and `LIVERY` → Aircraft, everything else → Other. Exceptions: a `SCENERY` Package with no BGL files at all (`fsdreamteam-gsx-pro`, `neofly-neofly-smokes`) is Other; a Package where any `aircraft.cfg` declares `isAirTraffic=1` (for example `fsltl-traffic-base`, 2,551 such files) is Other and isn't parsed for Liveries. The user can override Category per Addon.
- Top level:

  ```
  Scenery              Aircraft             Other
  ├ Airports           aircraft Addons,     tools, modules,
  ├ Landscapes         Liveries as their    AI traffic …
  └ Other              Packages
    (Asset packs)
  ```

- Each tab (Scenery → Airports, Scenery → Landscapes, Scenery → Other, Aircraft, Other) has **its own Catalog tree**. Addons are the leaves.
- A Scenery Addon goes to:
  - **Airports** when its Packages belong to an Airport (6.5);
  - **Scenery → Other** when it is an Asset pack: used by another Addon, or containing only shared models and no placed scenery or airports — *builder's call on the exact content test; the user can move it*;
  - **Landscapes** otherwise.
- **Initial Catalogs (first run):** each tab's tree mirrors the Library folders that hold its Addons, for example `Scenery\Europe` → Catalog "Europe" in the Airports tab and, separately, in the Landscapes tab if it holds Landscape too. The mapping:
  - A Library root is not a Catalog. A top-level folder that only repeats the Category (`Scenery`, `Aircraft`) is skipped.
  - A folder that holds several Packages becomes a Catalog (`SKIATHOS V1.2`, `Mid Western Victoria`), not an Addon.
  - A chain of folders with a single child collapses into one level; a folder holding a single Package gives no Catalog.
  - Tool layouts collapse to the product: `Orbx Library\msfs\<product>\Community\<pkg>` gives at most one level, not a "Community" Catalog per product.
  - Folders with the same path from different Libraries merge into one Catalog.
  - An Addon whose Packages lie in different folders goes to the Catalog that holds most of them; on a tie, the Catalog of a linked Package, else alphabetically first — *my call*.

  Afterwards Catalogs are independent of the disk: the user creates, renames, nests and moves them freely.
- Addons and Catalogs can be hidden. A "Show hidden" switch brings them back.

### 6.5 Addons and Airports

- **Initial Addons (first run, and for new Packages later):**
  - All Packages belonging to the same Airport form **one Addon per Airport**, named from the Airport (for example "EHAM Schiphol" with `brickbybrickdesign-airport-eham-amsterdam`, `duijffmedia-airport-eham-amsterdam`, `duijffmedia-schiphol-amsterdam-mega-enhancement-lighting`). The Addon is **anchored** to that Airport. A Package that belongs to several Airports joins the Addon of the alphabetically first code and is noted on the others — *my call*.
  - Aircraft Packages form one Addon per aircraft, anchored to its container (6.6).
  - Every other Package is its own Addon.
  - The user can then merge, split, rename and move Addons freely, and the app keeps that. Anchors survive rename and merge; when splitting, the user decides which part keeps the anchor.
  - **New Packages later** join the Addon anchored to their Airport or container if one exists, otherwise they become a new Addon. They never rearrange existing Addons.
- A Package belongs to an Airport if:
  1. one of its BGL files contains an airport record with that code, unless the Package is excluded (below), or
  2. rule 1 found no counted airport record in it, it is a Scenery Package that isn't Sim-managed, and its folder name or manifest title contains the code as a separate token that is not on a stop-list of common words (`PACK`, `SAVE`, `SWIM`, `SANS` and similar). 4–5 character tokens may sit anywhere (`…-esgj`, `EHAM …`); 3-character tokens only count as the leading token (`u60-u60`, `c53-…`), because about 3,000 generic idents are 3 characters long, or
  3. the user linked it manually.

  The user can remove automatic links. Some real airport Packages have neither an airport record nor a code token (for example `005builds-lszrsceneryv11`, `grancanaria`); they land in Landscapes until linked manually, so linking needs an Airport search.
- An Airport is identified by its ICAO code, or by its local ident when it has none (`OG20`, `MA44`, `U60`, `3U2`, `YX01`). Idents of 3–5 characters count, as in the sim; add-on airports use 5-character idents (`YALR1`, `YHAC1` from sltcreations, official `RJTIH`). Empty idents are ignored. Non-airports with airport records (`VOLC1`–`VOLC5` in `microsoft-airport-voloport`, `NEOF2` in `neofly-neofly-cargo`) are kept out by the exclusion list.
- Airport data (code, name, position) is read from airport records in BGL files (MSFS 2020: section type `0x03`, record id `0x0056`) of Library Packages, Community entries and Sim-managed packages.
- **Encrypted official airports:** some handcrafted official airports have no readable BGL (`asobo-airport-eddf-frankfurt`, `-egll-heathrow`, `-kden-denver`, `-lemd-adolfo-suarez`, `-omdb-dubai-intl`, `-heca-cairo`, `-fact-cape-town`). For Sim-managed airport packages where no airport record is found (no BGL at all, or BGLs without an airport record like `asobo-airport-lfcs-saucats`), the code comes from the package name (`*-airport-<code>-*`) and the name and position from the generic record for that code. This rule links only codes that are known from other records, so a Sim-managed name never creates a nameless Airport.
- **Excluded from rule 1:**
  - Packages with airport records for **more than 10 airports** (configurable). Verified: `fs-base-genericairports` 40,351, `fs-base-nav` 22,555, `navigraph-navdata` 15,629, `fsdreamteam-gsx-world-of-jetways-2020` 897, `microsoft-nordics2-point-of-interest` 87. The user can opt a Package back in; opting in overrides both the threshold and an exclusion pattern.
  - Packages on a configurable exclusion list. Defaults include `navigraph-navdata*`, `*-point-of-interest`, `fsdreamteam-gsx-world-of-jetways*`, `microsoft-airport-voloport` and `neofly-*`.
  - Records of a Package excluded by pattern are ignored everywhere: they create no Airports and are no known codes for the package-name rule. Records of a Package excluded only by the threshold (generic airports, navigation data) still supply known codes, names and positions.
  - An airport name that is a `TT:` localisation key (`TT:AIRPORTLR.LOWI.name`, only in Official Asobo packages) is resolved through `fs-base-genericairports\en-US.locPak` in the Official folder: exactly by key, else by the airport code in the key (`TT:AIRPORT.CYTZ.name` has no region part). A key that doesn't resolve, or a missing or unreadable locPak, counts as no name.
- **Real scenery** for an Airport: at least one non-excluded Library, Community or Sim-managed package belongs to it, or the user linked one manually. That includes `asobo-airport-*`, World Update partner airports and Marketplace airports — *my call*. Airports known only from generic or navigation data have no real scenery.
- **Replaces built-in:** a Package that isn't Sim-managed, belonging to an Airport that a Sim-managed airport package also belongs to. (World Update partner airports such as `microsoft-airport-edny-friedrichshafen` have third-party creators but are Sim-managed, so the test is where the Package lives, not who made it.) A dependency on the official package is an extra signal. Inactive Duplicate copies are analysed and linked like active ones. This is information, not a warning.
- **Uses:** an Addon uses another when one of its Packages lists a Package of the other in manifest `dependencies`, or when the user sets it manually.

### 6.6 Aircraft and Liveries

- Aircraft identity is the **Container** name (the folder under `SimObjects\Airplanes\` that holds an `aircraft.cfg`). Containers are indexed across Library Packages, Community entries and Sim-managed packages. Where `aircraft.cfg` is unreadable (encrypted official or Marketplace aircraft with `.fsarchive` files), container names come from paths in `layout.json`. Encrypted aircraft provide their containers in any source (Library, Community or Sim-managed), not only Sim-managed.
- `base_container` is a relative path (`..\Aerosoft_CRJ_900`); its last segment names the container.
- A **Livery** Package is one where every `aircraft.cfg` has a `base_container` whose container isn't provided by the same Package. Most Liveries declare `content_type: AIRCRAFT`, so `content_type` alone can't tell. Livery detection runs only for the Aircraft Category.
- **One Addon per aircraft, keyed by container:** every Library Package that provides the container, plus the Liveries whose `base_container` names it, form one Addon. They are Packages of that Addon and can be switched one by one in its properties. When two Library Packages provide the same container (`sal1800-cessna-170b-backcountry` and `sal1800-cessna-super-170b-OPTIONAL` both provide `Cessna-170B-Backcountry`), both join the same Addon; scan order never decides.
  - When a Sim-managed, Fixed or External Package also provides the container, it is shown as the Addon's locked base, and the Library Packages that provide the same container are labelled **mods** (`mge-asobo-aircraft-c152` provides `Asobo_C152`, like the official C152).
  - A Package that provides several containers joins the Addon of the alphabetically first one and is noted on the others.
  - A Package without `aircraft.cfg` (`flybywire_A320Neo_Cabin`, `simflight-C152-interior-navtablet`) is matched by its `SimObjects\Airplanes\<folder>` names first, then by `dependencies` naming a known aircraft Package, otherwise it is its own Addon in Other.

### 6.7 Warnings

Only these are warnings:

- **Duplicate package**: the same Package name in Community and in `Official`.
- **Duplicate copy**: the same Package more than once across the Libraries.
- **Livery without aircraft**: a Livery is on while no provider of its base container is on or always loaded.
- **Used Addon off**: an Addon with any Package on uses an Addon that is entirely off.
- **Broken** and **Stray** entries in Community.
- **Two GSX profiles** for one Airport (8).

Several Packages at one Airport are shown neutrally ("3 packages"), never as a warning (ADR 0002). Ignored entries are neutral info.

### 6.8 Applying changes and the sim

- **Every link change** (on/off at any level, "restore app state" in the sync dialog, removing a Broken link, changing an active copy) goes through one apply path.
- **Write order:** the link is changed first; only when that succeeds are the choice and the Community snapshot saved, in one write. A failure leaves the stored state as it was and is reported.
- **Startup self-check:** after reconciling, the app checks every Managed Package for "on ⇔ linked". Any mismatch (for example after a crash between link and save) becomes a row in the sync dialog, even when Community matches the snapshot.
- The app detects whether MSFS 2020 (`FlightSimulator.exe`) is running.
- While it runs, the apply path doesn't touch Community. It records which Packages are affected and shows them as **Pending**; Pending takes precedence over other display states. When the sim exits, the app re-derives the links of those Packages from the stored choices and applies them. Switching a Package on, off and on again while the sim runs therefore results in one correct change.
- The pending set is persisted. If the app is closed while the sim runs, it is applied on the next app start, after reconciliation, if the sim isn't running then — *my call*.

## 7. Startup, scanning and reconciliation

### 7.1 First run

1. Locate `UserCfg.opt` (Steam: `%APPDATA%\Microsoft Flight Simulator\`; Microsoft Store: under `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_…\LocalCache\`), read `InstalledPackagesPath`, derive Community and Official. If not found, ask the user to pick the Community folder.
2. Classify Community and collect the targets of all existing links.
3. **Propose Libraries:**
   0. Only existing junction targets are used. Symlinks (made by other tools' installers, such as SayIntentions' three links into `%APPDATA%\SayIntentionsAI`) and missing targets are not; the user can add such folders by hand.
   1. Link targets under a well-known tool root (`…\Orbx Library\msfs\`, `…\Neofly\addons\`) propose that root directly. A target under `…\FSDT Addon Manager\MSFS\` proposes nothing at all (neither this step nor step 2): FSDT's Addon Manager owns its junctions and keeps a huge backup tree there, so those Community entries stay External.
   2. For the others, start at each target's parent folder and move upward while the next parent is not a drive root or tool root, is not itself a Package, and every one of its subfolders is a Package or has one beneath it. Subfolders don't need to be link targets: unlinked Packages count too. Merge identical results.

   On Rickard's machine this gives `C:\Sim\Addons` (it stops there because `C:\Sim` also holds folders without Packages, such as `Tool`), `C:\Sim\Orbx Library\msfs` and `C:\Sim\Neofly\addons`. The user can adjust, add and remove roots in the same dialog, and add more Libraries later in Settings.
4. **Propose Managed elsewhere** for Libraries owned by other tools (Orbx, Neofly; FSDT's folder is never proposed), unticked by default, with a plain explanation of the trade-off: the app won't switch those Packages, and switching them here would be undone by the tool if it relinks.
5. Adopt the current Community state: each Package is on if linked, off if not. No sync dialog on first run.
6. Run discovery, then the content scan (7.3). On/off is available as soon as discovery is done.

### 7.2 Normal start

1. Show the main window with the last saved state and cached scan results immediately.
2. In the background, reconcile against Community (7.4).
3. Then run an incremental scan (7.3).
4. Update the UI as results come in. On/off works at all times; it only needs Community, Package paths and the stored choices.

Targets (*my call*, to be measured): main window usable with cached data within about 1 second; Community reconcile under 1 second for about 150 entries.

### 7.3 Scanning

Two passes:

1. **Discovery** (fast, seconds): walk Libraries and Community, read `manifest.json`, find Packages, Duplicate copies and Categories. Enough for on/off.
2. **Content**: BGL airport records, `aircraft.cfg` and containers, `layout.json`, GSX profiles, thumbnails. Builds Airports, Addons and the tabs.

- Order on first scan: Community → Libraries → Sim-managed packages, with visible progress. The generic airports (about 40,000 in almost 5,000 BGL files) are read once and cached.
- Incremental by default: a Package is re-read only if its path, or the timestamp or size of `manifest.json` or `layout.json`, has changed. F5 forces a full rescan.
- Community and all Libraries are watched for changes; affected Packages are rescanned in the background. Community is watched non-recursively; changes inside linked Packages are seen through the Library watchers. Watchers overflow during large installs (`C:\Sim\Addons` holds about 123,000 files), so an overflow triggers an incremental rescan.
- From Sim-managed packages, the app reads Scenery and aircraft containers. Their aircraft are never toggleable.

### 7.4 Reconciliation

- The app stores the **last accepted Community snapshot**: every entry with its resolved target. Reconciling compares that snapshot with the current Community, not with the user's choices.
- Changes to External, Fixed and Ignored entries, and to Managed elsewhere Packages, are recorded silently and never open the dialog. For Managed elsewhere Packages, "recorded" means the stored choice follows Community.
- If other differences exist, a sync dialog lists them, one row per Package, grouped by Addon. Each row chooses "keep Community" (preselected) or "restore app state"; one choice can be applied to all rows.
- "Keep Community" sets the Package's stored choice to match Community. "Restore app state" changes the link through the apply path (6.8). Either way, the snapshot is updated.
- If Community changes while the app runs and the change didn't come from the app (for example Addons Linker, which may run alongside), the user is told and offered the same sync dialog, with a way to not be asked again for the rest of the session. The app never silently overwrites such a change — *my call*.

## 8. GSX

- Profiles are read from `%APPDATA%\Virtuali\GSX\MSFS`.
- **Airport code from the file name:** the leading run of 3–4 letters or digits, followed by `-`, `_`, `.` or the end of the name (`epgd_LukSid.ini` → EPGD, `LPMA.ini` → LPMA). The code is checked against known Airports.
- `.off` files are disabled profiles: shown as disabled, not counted.
- `.py` files belong to the Airport by code, not by base name (`essa-orbx.py` belongs with `essa-bwib1r-noolvdgs.ini`). A `.py` doesn't need an `.ini` with the same name. When an Airport has two profiles, its `.py` files are listed with the Airport, not paired with one profile.
- Status per Airport: **Installed**, **Missing**, **Two profiles** (two or more active `.ini` profiles in the GSX folder for one code; real cases today: LDSP, LFMN, LFPG). Profiles with no matching real scenery appear as **Profile only**.
- **Profiles shipped with scenery:** some Packages carry a profile in a subfolder (`GSX\`, `GSX-PROFILE\`, `GSX-Profile-EPKK\`). They are recognised by `<code>….ini`/`.py` file names, not by the folder name (`aerosoft-crj\Data\GSX\` holds aircraft `gsx.cfg` files, not profiles). They are shown as "shipped with scenery, not installed" with an action to copy the profile into the GSX folder, and they don't count for Two profiles. GSX's own log (`Couatl.err`) has only ever shown it loading profiles from its own folder, which supports this; see 15.
- Status is shown only for Airports with real scenery (plus Profile only rows), together with whether the Airport's scenery is on.
- Dropping a profile file onto the app copies it into the GSX folder, asking before overwriting.
- Missing profiles get a deep link to flightsim.to, filtered to GSX profiles for the code.

## 9. Finding scenery

For any Airport, the app offers deep links to search flightsim.to and SimMarket for its code. The links are URL templates in Settings, so more stores can be added by the user.

## 10. Screens

Layout and feel follow BOB Dispatcher: dark, calm, airy, left navigation, clear status colours. Exact visuals and interactions are decided in a later UI design step.

- **Addons** (start view): Scenery (Airports / Landscapes / Other), Aircraft and Other, each with its Catalog tree and Addons as leaves. Search, filter (On / Off / Mixed / Pending), Show hidden. Each Addon shows thumbnail, name, status, warnings and its on/off.
- **Addon properties**: its Packages (display name, folder name, path, version, Library, Duplicate copy choice, each with its own on/off), Airports it belongs to, Addons it uses (dimmed), and actions: rename, merge/split, move to Catalog, link/unlink Airport (with search), set Uses, override Category, Managed elsewhere, hide.
- **Map**: Airports with real scenery as markers coloured by state (on / off / built-in). "Show all airports" adds the generic ones, clustered when zoomed out. Airport search. Selecting an Airport shows code, name, its Addon with on/off, GSX status and search links.
- **GSX profiles**: table of code, name, scenery state, profile status, actions (open folder, find profile, copy shipped profile). Filters by status. Drop target for profile files.
- **Community**: every entry with its classification, link target and warnings. Remove Broken links (with confirmation).
- **Settings**: Community path (detected, overridable), Libraries (add/remove, Managed elsewhere), search URL templates, airport threshold, exclusion list and token stop-list, version and update notice.
- **Overlays**: first-run setup, sync dialog, pending changes while the sim runs, scan progress.

Thumbnails come from the Package's own files (`ContentInfo\<any>\Thumbnail.jpg`; the subfolder name differs from the Package name; 198 of 228 Library Packages in `C:\Sim\Addons` have one), with a placeholder per type when none exists.

## 11. Storage

- **User data** (`%APPDATA%\<app>\`), JSON, small and human-readable: Libraries; each Package's on/off choice, Managed elsewhere flag and active copy; Addons with their Packages, names and Uses; Catalog trees per tab with Addon placement; manual and removed Airport links; opted-in Packages; Category overrides; hidden items; pending set; search URL templates; threshold, exclusion list and stop-list; the last accepted Community snapshot.
- **Scan cache** (`%LOCALAPPDATA%\<app>\`), SQLite, disposable: per Package the change marker and the facts read from its files (manifest data, airport records, BGL section types, aircraft.cfg facts, airplane folders, thumbnail path), and GSX profiles (plan 4). Links, Airports and aircraft groups are recomputed from these on every analysis.
- Deleting the cache must only cost a rescan.
- New Packages found after first run get an initial Addon and Catalog placement by the rules in 6.4 and 6.5; the user's existing arrangement is never rearranged.

## 12. Structure (suggested)

The builder may change this, but the boundaries matter:

- **Core**: domain model and rules (Package choices and links, computed Addon and Catalog states, Uses, initial Addon and Catalog building, Airport linking, warnings, apply path and pending set, reconciliation diff). No WPF, no file system.
- **Sim2020**: everything MSFS 2020 specific: locating the install, `UserCfg.opt`, folder layouts, manifest and `layout.json` reading, BGL airport records, `aircraft.cfg` and containers, sim process detection. Behind interfaces in Core (C6).
- **Infrastructure**: junctions (P/Invoke), file watching, JSON and SQLite storage, GSX folder access, GitHub release check.
- **App**: WPF + WPF-UI views and view models, WebView2 map.

The reconcile, watcher, apply path, pending set and own-change suppression form one coupled subsystem; it should be planned as one unit with its state machine written down.

## 13. Testing

- Core rules are unit tested without touching disk: on/off at each level, computed Mixed, Uses and its warning, Managed elsewhere skipping, initial Addons and Catalogs, Airport linking (threshold, exclusions, stop-list, encrypted official airports), Livery and mod rules, warnings, pending set, reconciliation diff.
- Junction handling is tested against temporary folders, including guard tests that removing a link leaves the target untouched, that a failed creation leaves no empty folder, and that a taken name is never overwritten.
- BGL reading and GSX file-name parsing are tested with small fixtures, including the real-world names above. Official sim files are not committed to the repo.
- Guard tests for the state machine: a moved Package is re-pointed, a missing Package keeps its user data, a failed link change saves nothing, the self-check catches "on but not linked".
- **First-run fixture:** a sanitised copy of Rickard's Community (entry names, kinds, targets) and Library tree (folders and manifests, no content) is committed, so Library proposal, initial Addons and initial Catalogs are tested as a whole scenario.
- A collision report (all Package name and title tokens against the generic idents) is part of the content-scan tests, so the token stop-list is checked against data.
- An opt-in integration test runs against the local MSFS install.

## 14. Left to the builder

- Exact visual design and interactions, within section 10 and the BOB references (to be settled in a UI design step before building screens).
- BGL parsing details (the format is documented; Little Navmap is a reference).
- The content test for Asset packs (6.4).
- How GSX profiles inside scenery Packages are recognised (Dave's code is reference only).
- Map tile provider, as long as it needs no API key and has a dark style.
- How "update available" is shown.
- Logging and diagnostics.

## 15. Open investigation

- **Does GSX Pro load profiles shipped inside scenery Packages?** Evidence so far (GSX's own log) says no. If it turns out yes, shipped profiles count as active, and Two profiles may change for ESSA, ESGG, EDDF and EPKK on Rickard's machine. Note that a shipped and an installed profile can be variants of the same profile (ESSA: `essa-bwib1r.ini` and `essa-bwib1r-noolvdgs.ini`), which would make a plain Two profiles warning a false alarm.

## 16. Calls I made (please check)

- Spec written in English, like GLOSSARY.md and the ADRs.
- An Addon whose Packages lie in different folders is placed in the Catalog holding most of them.
- A Package belonging to several Airports, or providing several containers, joins the Addon of the alphabetically first one and is noted on the others.
- Addons are anchored to their Airport or container, so new Packages find the right Addon after renames and merges.
- Missing Packages keep all user data until the user forgets them; moved Packages are re-pointed.
- `neofly-*` is on the default exclusion list (its cargo package carries a fake airport record).
- `SCENERY` Packages without any BGL go to Other.
- Marketplace and World Update partner airports count as real scenery.
- Managed elsewhere is proposed for Orbx and Neofly Libraries, unticked by default.
- Changes to Community from other tools while the app runs offer the sync dialog, with "don't ask again this session".
- The pending set survives an app restart.
- Startup targets of about 1 second.
- Sync dialog rows are per Package, grouped by Addon.
