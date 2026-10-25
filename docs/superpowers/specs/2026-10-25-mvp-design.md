# Modern MSFS Addon Manager — MVP design

Date: 2026-10-25. Status: draft, revised after builder check and independent review.

Vocabulary is defined in [GLOSSARY.md](../../../GLOSSARY.md) and used strictly below. Background and the full decision log are in [ANALYS.md](../../../ANALYS.md) (Swedish). Architectural decisions are in [docs/adr](../../adr).

## 1. Intent

Rickard manages a large MSFS 2020 setup (about 120 Community entries, about 230 Library Packages in four Libraries) with MSFS Addons Linker. He likes two things about it: turning content on and off quickly, and a map of installed airports plus the airports built into the sim. He dislikes its ugly, clunky interface, and misses a link to GSX Pro profiles and a way to find scenery he doesn't have.

This app replaces Addons Linker for that workflow. Scenery is the focus, aircraft come second, everything else is "Other" with plain on/off. It must start fast, never put the user's files at risk, and look as calm and clear as BOB Dispatcher (screenshots in `C:\Users\ricka\OneDrive\Bilder\bob\`).

## 2. Goals

1. Turn Addons on and off by linking their Packages into Community with directory junctions.
2. Group Packages into Addons, including Packages shared by several Addons.
3. Show Scenery in two views: by Library folder, and by Airport (Airports and Landscape).
4. Show Aircraft with their Liveries underneath.
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

## 4. Anti-goals (failure even if it works)

- **Any user file moved, modified or deleted** without an explicit, specific user action. In particular, the app must never delete recursively through a junction or symlink.
- Community left in a broken state after the app is closed, crashes or is uninstalled.
- A UI that freezes during scanning, or a startup that waits for scanning.
- Changes to Sim-managed packages or Fixed entries.
- False alarms that train the user to ignore warnings (see ADR 0002). Every warning rule below has been checked against Rickard's real install for this reason.

## 5. Constraints

- **C1.** C# / .NET 10, WPF + WPF-UI, map in WebView2 + Leaflet with tiles that need no API key (ADR 0003). Easy move to .NET 11.
- **C2.** Windows only. Junctions, not symlinks, so no admin rights are needed. .NET has no managed API for creating junctions (`Directory.CreateSymbolicLink` makes symlinks), so creation needs P/Invoke (`FSCTL_SET_REPARSE_POINT`). Removal with non-recursive `Directory.Delete(path, false)` removes only the link.
- **C3.** Shipped as a framework-dependent single-file exe on GitHub Releases. If the .NET 10 Desktop Runtime is missing, the standard apphost dialog sends the user to Microsoft's download page.
- **C4.** MIT license. Dave's GSX Profile Manager (GPL-3.0) is reference only: no copied or translated code.
- **C5.** Files are never moved; enablement is junctions plus own state reconciled against Community (ADR 0001).
- **C6.** Sim-specific parts (locating the install, folder layouts, content formats, built-in airports, sim process detection) sit behind an abstraction so that MSFS 2024 can be added later.
- **C7.** MSFS 2020 loads Packages only from `Official\<store>` and Community, and only at sim startup. The real folders in the Packages root next to them (for example `orbx-airport-esgg-gothenburg`, `fsdreamteam-gsx-pro`) have no `manifest.json`; they are the sim's WASM cache and module storage, not Packages, and the app ignores them.
- **C8.** English UI, dark theme only.

## 6. Domain rules

### 6.1 Packages and Libraries

- A Package is found by recursively walking a Library. A folder containing `manifest.json` is a Package; the walk does not descend into it.
- Package identity is its folder name (the name the sim uses). The junction in Community gets the same name.
- The same name in two Libraries is a **Duplicate copy**. One copy is the active copy: by default the one Community already points to, otherwise the first found; the user can change it. Only the active copy can be linked.
- Unregistering a Library never deletes anything; its Managed entries become External entries.
- A Library's folders are shown as the folder view (6.4). They don't change how Packages behave.

### 6.2 Community entries

Every entry in Community is classified on each reconcile:

| Entry | Rule | App may |
|---|---|---|
| Managed | Junction whose target is a Package inside a registered Library | Create and remove |
| External | Junction or symlink pointing outside every registered Library | Show only |
| Fixed | Real folder with `manifest.json` | Show only |
| Ignored | Real folder or link target without `manifest.json` (for example `flybywire-current-install-0`). The sim ignores it | Show as neutral info |
| Broken | Link whose target doesn't exist | Remove the link, after confirmation |
| Stray | Loose file | Show only, with a warning |

- Link targets are compared as resolved, normalised paths (with or without trailing backslash; Addons Linker adds one, Neofly doesn't), never by entry name.
- Removing a link must remove only the link itself, never the target's contents.

### 6.3 Addons and on/off

- **Every Library Package always has its own Addon**, named from the manifest `title`. It is never absorbed.
- **Grouped Addons** can be added on top: a named collection of several Packages, for example an aircraft with its cabin mod, or an airport with the Asset pack it needs. A Package may belong to its own Addon and any number of grouped Addons.
- Grouped Addons come from suggestions based on manifest `creator`, shared name prefixes and `dependencies`. The user accepts, adjusts or rejects each one; nothing is grouped without acceptance. The user can also create, rename and edit grouped Addons manually.
- **An Addon's On/Off is the user's choice and is stored.** It is not derived from which Packages happen to be linked.
- **A Package should be linked exactly when at least one On Addon contains it.**
  - **Turn on:** link every Package of the Addon that isn't linked yet.
  - **Turn off:** unlink each Package of the Addon that no other On Addon contains.
  - Example: A = {X, P}, B = {Y, P}. Turning A on links X and P; B stays Off. Turning A off unlinks X and P, unless B is On, in which case P stays.
- A newly accepted grouped Addon starts On if all its Packages are linked, Off otherwise.
- An Addon that is Off but whose Packages are linked because another On Addon needs them shows "Off, loaded via <Addon>".
- **Partial** means Community differs from the user's choice for that Addon: an On Addon with a Package not linked, or an Off Addon with a Package linked that no On Addon needs. Clicking a Partial Addon turns it on.
- **Folder nodes and Airport nodes are not Addons.** Their toggle is a bulk action: on turns on every Addon below, off turns off every Addon below. Their displayed state is On, Off or Mixed, computed from what's below.
- **Managed elsewhere:** the user can mark an Addon as managed by another tool (FSDT Addon Manager recreates GSX's links at every start, Orbx Central does the same for its airports). Such an Addon is shown locked and never appears in the sync dialog. On first run, Packages in Libraries belonging to such tools (FSDT, Orbx) are proposed as Managed elsewhere.
- Addons contain only Library Packages. External and Fixed entries are shown read-only where they belong (with a lock), but can't be grouped or toggled.

### 6.4 Categories and views

- Category comes from manifest `content_type`: `SCENERY` → Scenery, `AIRCRAFT` and `LIVERY` → Aircraft, everything else → Other. Exception: a Package whose `aircraft.cfg` files declare `isAirTraffic=1` (for example `fsltl-traffic-base`, 2,551 such files) is Other and isn't parsed for Liveries. The user can override Category per Package.
- A grouped Addon's Category is that of its Packages. If they differ, the most prioritised wins (Scenery > Aircraft > Other) — *my call*.
- **Scenery has two views**, switched with one control. The toggles are the same in both.
  - **Folders**: the Library folder tree as it lies on disk, for example `Scenery → Europe → LFBZ Biarritz`. Folder nodes have a bulk toggle (6.3).
  - **Airports**: two branches.
    - **Airports**: each Airport with code and name, its Addons below ("3 packages"), and a bulk toggle.
    - **Landscape**: Scenery Addons that belong to no Airport.
- A Package belongs to an Airport if:
  1. one of its BGL files contains an airport record with that code, unless the Package is excluded (6.5), or
  2. no airport record was found in it by rule 1, and its folder name or manifest title contains the code as a separate token (`…-esgj`, `EHAM …`), and the token is not on a stop-list of common words (`PACK`, `SAVE`, `SWIM`, `SANS` and similar), or
  3. the user linked it manually.

  The user can remove automatic links. A Package can belong to several Airports. Some real airport Packages have neither an airport record nor a code token (for example `005builds-lszrsceneryv11`, `grancanaria`); they land in Landscape until linked manually, so "link to Airport" needs a search.
- **Aircraft**: each aircraft Addon with its Liveries underneath (6.6).
- **Other**: a flat list.
- Addons and Airports can be hidden. A "Show hidden" switch brings them back.

### 6.5 Airports

- An Airport is identified by its ICAO code, or by its local ident when it has none (`OG20`, `MA44`, `U60`, `3U2`, `YX01`). Airport records without any code (for example `microsoft-airport-voloport`) are ignored.
- Airport data (code, name, position) is read from airport records in BGL files (MSFS 2020: section type `0x03`, record id `0x0056`) of Library Packages, Community entries and Sim-managed packages.
- **Excluded from rule 1 in 6.4:**
  - Packages with airport records for **more than 10 airports** (configurable). Verified examples: `fs-base-genericairports` 40,351, `fs-base-nav` 22,555, `navigraph-navdata` 15,629, `fsdreamteam-gsx-world-of-jetways-2020` 897, `microsoft-nordics2-point-of-interest` 87. The user can opt a Package back in.
  - Packages on a configurable exclusion list. Defaults include `navigraph-navdata*`, `*-point-of-interest` and `fsdreamteam-gsx-world-of-jetways*`.
- **Real scenery** for an Airport means: at least one non-excluded Library or Community Package belongs to it, or a non-excluded Sim-managed package does. That includes `asobo-airport-*`, partner airports from World Updates and Marketplace airports — *my call*.
- **Replaces built-in:** a non-official Package with an airport record for an Airport that a handcrafted official package also has a record for. A dependency on the official package or overlapping relative paths in `layout.json` are extra signals. This is information, not a warning.

### 6.6 Aircraft and Liveries

- Aircraft identity is the **container name**: the folder under `SimObjects\Airplanes\` that holds an `aircraft.cfg`. It is not a Package name.
- A **Livery** Package is one whose `aircraft.cfg` files all have a `base_container` pointing to a container outside the Package. Most Liveries declare `content_type: AIRCRAFT`, not `LIVERY`, so `content_type` alone can't tell.
- A Livery belongs under the aircraft that provides its `base_container`.
- Container names are indexed across all Packages: Library, Community entries and Sim-managed. For encrypted Marketplace aircraft (only `.fsarchive` files), container names are read from paths in `layout.json`. Aircraft found outside the Libraries are shown as locked parents.
- **Mods** that provide a container with the same name as another Package (for example `mge-asobo-aircraft-c152` provides `Asobo_C152`, like the official C152) are shown under that aircraft as modifications, not as a separate aircraft.
- Aircraft Packages without any `aircraft.cfg` (for example `flybywire_A320Neo_Cabin`) are placed under the aircraft named in their `dependencies`, otherwise in Other.

### 6.7 Warnings

Only these are warnings:

- **Duplicate package**: the same Package name in Community and in `Official`.
- **Duplicate copy**: the same Package in several Libraries.
- **Livery without aircraft**: a Livery is on while its aircraft, a Library aircraft, is off. Liveries for always-loaded aircraft never warn.
- **Broken** and **Stray** entries in Community.
- **Two GSX profiles** for one Airport (8).

Several Packages at one Airport are shown neutrally ("3 packages"), never as a warning (ADR 0002). Ignored entries are neutral info.

### 6.8 Sim running

- The app detects whether MSFS 2020 is running.
- While it runs, every on/off change is queued and shown as pending. Queued changes are applied automatically when the sim exits.
- The queue is persisted. If the app is closed while the sim runs, the queue is applied on the next app start, if the sim isn't running then — *my call*.

## 7. Startup, scanning and reconciliation

### 7.1 First run

1. Locate `UserCfg.opt` (Steam: `%APPDATA%\Microsoft Flight Simulator\`; Microsoft Store: under `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_…\LocalCache\`), read `InstalledPackagesPath`, derive Community and Official. If not found, ask the user to pick the Community folder.
2. Classify Community and collect the targets of all existing links.
3. Propose Libraries as the common roots of those targets (on Rickard's machine: `C:\Spel\Addons`, `C:\Spel\Orbx Library\msfs`, `C:\Spel\FSDT Addon Manager\MSFS`, `C:\Spel\Neofly\addons`). Propose FSDT and Orbx Libraries as Managed elsewhere. The user confirms; more Libraries can be added later in Settings.
4. Adopt the current Community state: every Package's own Addon starts On if linked, Off if not. No sync dialog on first run.
5. Start the first scan (7.3).

### 7.2 Normal start

1. Show the main window with the last saved state and cached scan results immediately.
2. In the background, reconcile against Community (7.4).
3. Then run an incremental scan (7.3).
4. Update the UI as results come in. On/off works at all times; it only needs Community and Package paths.

Targets (*my call*, to be measured): main window usable with cached data within about 1 second; Community reconcile under 1 second for about 150 entries.

### 7.3 Scanning

- Order on first scan: Community → Libraries → Sim-managed packages, with visible progress. The first scan reads about 40,000 generic airports from almost 5,000 BGL files; it must be cached and never repeated unless forced.
- Incremental by default: a Package is re-read only if its path, or the timestamp or size of `manifest.json` or `layout.json`, has changed. F5 forces a full rescan.
- Community and all Libraries are watched for changes; affected Packages are rescanned in the background. Watchers will overflow during large installs (`C:\Spel\Addons` holds about 123,000 files), so an overflow triggers an incremental rescan.
- From Sim-managed packages, the app reads Scenery and aircraft container names. Their aircraft are never shown as toggleable.

### 7.4 Reconciliation

- The app stores the **last accepted Community snapshot**: every entry with its resolved target. Reconciling compares that snapshot with the current Community, not with the user's choices. Partial is computed from choices and links, and is never itself a difference.
- Changes to External, Fixed and Ignored entries, and to Addons marked Managed elsewhere, are recorded silently and never open the dialog.
- If other differences exist, a sync dialog lists them, one row per Package, grouped by Addon. Each row chooses "keep Community" (preselected) or "restore app state"; buttons apply one direction to all rows.
- "Keep Community" leaves the user's choices as they are and accepts the links, so an Addon may become Partial (6.3). "Restore app state" creates and removes links to match the choices. Either way, the snapshot is updated.
- If Community changes while the app runs and the change didn't come from the app, a banner offers the same sync dialog. The app never silently overwrites such a change — *my call*.

## 8. GSX

- Profiles are read from `%APPDATA%\Virtuali\GSX\MSFS`.
- **Airport code from the file name:** the leading run of 3–4 letters or digits, followed by `-`, `_`, `.` or the end of the name, case-insensitive (`epgd_LukSid.ini` → EPGD, `LPMA.ini` → LPMA). The code is checked against known Airports.
- `.off` files are disabled profiles: shown as disabled, not counted.
- `.py` files are linked to the profile by code, not by base name (`essa-orbx.py` belongs with `essa-bwib1r-noolvdgs.ini`). A `.py` doesn't need an `.ini` with the same name.
- Status per Airport: **Installed**, **Missing**, **Two profiles** (two or more active `.ini` profiles in the GSX folder for one code; real cases exist today: LDSP, LFMN, LFPG). Profiles with no matching real scenery appear as **Profile only**.
- **Profiles shipped with scenery:** some Packages carry a profile in a subfolder (`GSX\`, `GSX-PROFILE\`, `GSX-Profile-EPKK\`). Whether GSX Pro loads these by itself is **not known** (open investigation). Until it is settled, they are shown as "shipped with scenery, not installed" with an action to copy the profile into the GSX folder, and they don't count for Two profiles.
- Status is shown only for Airports with real scenery (plus Profile only rows). The table also shows whether the Airport's scenery is on.
- Dropping a profile file onto the app copies it into the GSX folder, asking before overwriting.
- Missing profiles get a deep link to flightsim.to, filtered to GSX profiles for the code.

## 9. Finding scenery

For any Airport, the app offers deep links to search flightsim.to and SimMarket for its code. The links are URL templates in Settings, so more stores can be added by the user.

## 10. Screens

Layout and feel follow BOB Dispatcher: dark, calm, airy, left navigation, clear status colours. Exact visuals are the builder's call.

- **Addons** (start view): Category tabs Scenery / Aircraft / Other. Scenery has the Folders / Airports switch (6.4). Search, filter (On / Off / Partial), Show hidden. Each Addon row: thumbnail, name, creator, status, warnings, toggle. A detail pane shows the Addon's Packages with paths, versions and Library, the Airports it belongs to, and actions: rename, edit grouped Addons, link/unlink Airport (with Airport search), override Category, mark Managed elsewhere, hide.
- **Suggestions**: proposed grouped Addons to accept, adjust or reject.
- **Map**: Airports with real scenery as markers coloured by state (on / off / built-in). "Show all airports" adds the generic ones, clustered when zoomed out. Airport search. Clicking an Airport opens a side panel: code, name, its Addons with toggles, GSX status, search links.
- **GSX profiles**: table of code, name, scenery state, profile status, actions (open folder, find profile, copy shipped profile). Filters by status. Drop target for profile files.
- **Community**: every entry with its classification, link target and warnings. Remove Broken links (with confirmation). Choose the active copy for Duplicate copies.
- **Settings**: Community path (detected, overridable), Libraries (add/remove, Managed elsewhere), search URL templates, airport threshold and exclusion list, token stop-list, version and update notice.
- **Overlays**: first-run setup, sync dialog, pending-changes banner while the sim runs, scan progress.

Thumbnails come from the Package's own files (`ContentInfo\…\Thumbnail.jpg`; 198 of 228 Library Packages have one), with a placeholder per type when none exists.

## 11. Storage

- **User data** (`%APPDATA%\<app>\`), JSON, small and human-readable: Libraries, Addons with their On/Off choice and membership, Managed elsewhere marks, manual Airport links and removed automatic links, opted-in Packages, Category overrides, hidden items, active copy choices, pending queue, search URL templates, threshold, exclusion list and stop-list, and the last accepted Community snapshot.
- **Scan cache** (`%LOCALAPPDATA%\<app>\`), SQLite, disposable: Packages with manifest data and change markers, Airports, Package–Airport links found by scanning, airport record counts, aircraft containers and Livery links, GSX profiles, thumbnails.
- Deleting the cache must only cost a rescan.

## 12. Structure (suggested)

The builder may change this, but the boundaries matter:

- **Core**: domain model and rules (Addons, On/Off and linking, Partial, bulk toggles, classification, views, Airport linking, warnings, reconciliation diff). No WPF, no file system.
- **Sim2020**: everything MSFS 2020 specific: locating the install, `UserCfg.opt`, folder layouts, manifest and `layout.json` reading, BGL airport records, `aircraft.cfg` and containers, sim process detection. Behind interfaces in Core (C6).
- **Infrastructure**: junctions (P/Invoke), file watching, JSON and SQLite storage, GSX folder access, GitHub release check.
- **App**: WPF + WPF-UI views and view models, WebView2 map.

## 13. Testing

- Core rules are unit tested without touching disk: linking with shared Packages, Partial and "loaded via", bulk toggles, classification, views and Airport linking (threshold, exclusions, stop-list), warnings, reconciliation diff.
- Junction handling is tested against temporary folders, including a guard test that removing a link leaves the target untouched.
- BGL reading and GSX file-name parsing are tested with small fixtures, including the real-world names above. Official sim files are not committed to the repo.
- An opt-in integration test runs against the local MSFS install.

## 14. Left to the builder

- Exact visual design, within section 10 and the BOB references.
- Grouping heuristics for suggestions, within the signals in 6.3.
- BGL parsing details (the format is documented; Little Navmap is a reference).
- How GSX profiles inside scenery Packages are recognised (Dave's code is reference only).
- Map tile provider, as long as it needs no API key and has a dark style.
- How "update available" is shown.
- Logging and diagnostics.

## 15. Open investigation

- **Does GSX Pro load profiles shipped inside scenery Packages?** If yes, they count as active and Two profiles may change for ESSA, ESGG, EDDF and EPKK on Rickard's machine. To be settled before or during implementation of section 8.

## 16. Calls I made (please check)

- Spec written in English, like GLOSSARY.md and the ADRs.
- Folder and Airport nodes are bulk toggles, not Addons. Folder grouping is the folder view rather than folder-based Addon suggestions.
- Addons contain only Library Packages; External and Fixed entries are read-only.
- Mixed-Category grouped Addons take the most prioritised Category.
- Marketplace and World Update partner airports count as real scenery.
- Aircraft containers are indexed across Sim-managed, External and Fixed content so Liveries always find their parent; such Liveries never warn.
- Changes to Community from other tools while the app runs open the sync dialog via a banner.
- Pending queue survives an app restart.
- Startup targets of about 1 second.
- Sync dialog rows are per Package, grouped by Addon.
