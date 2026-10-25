# Modern MSFS Addon Manager — MVP design

Date: 2026-10-25. Status: draft for review.

Vocabulary is defined in [GLOSSARY.md](../../../GLOSSARY.md) and used strictly below. Background and the full decision log are in [ANALYS.md](../../../ANALYS.md) (Swedish). Architectural decisions are in [docs/adr](../../adr).

## 1. Intent

Rickard manages a large MSFS 2020 setup (about 120 Community entries, several Libraries) with MSFS Addons Linker. He likes two things about it: turning content on and off quickly, and a map of installed airports plus the airports built into the sim. He dislikes its ugly, clunky interface, and misses a link to GSX Pro profiles and a way to find scenery he doesn't have.

This app replaces Addons Linker for that workflow. Scenery is the focus, aircraft come second, everything else is "Other" with plain on/off. It must start fast, never put the user's files at risk, and look as calm and clear as BOB Dispatcher (screenshots in `C:\Users\ricka\OneDrive\Bilder\bob\`).

## 2. Goals

1. Turn Addons on and off by linking their Packages into Community with directory junctions.
2. Group Packages into Addons, including Packages shared by several Addons.
3. Show Scenery as a hierarchy: Airports with their Addons, and Landscape.
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
- False alarms that train the user to ignore warnings (see ADR 0002).

## 5. Constraints

- **C1.** C# / .NET 10, WPF + WPF-UI, map in WebView2 + Leaflet with tiles that need no API key (ADR 0003). Easy move to .NET 11.
- **C2.** Windows only. Junctions, not symlinks, so no admin rights are needed.
- **C3.** Shipped as a framework-dependent single-file exe on GitHub Releases. If the .NET 10 Desktop Runtime is missing, the standard apphost dialog sends the user to Microsoft's download page.
- **C4.** MIT license. Dave's GSX Profile Manager (GPL-3.0) is reference only: no copied or translated code.
- **C5.** Files are never moved; enablement is junctions plus own state reconciled against Community (ADR 0001).
- **C6.** Sim-specific parts (locating the install, folder layouts, content formats, built-in airports, sim process detection) sit behind an abstraction so that MSFS 2024 can be added later.
- **C7.** MSFS 2020 reads Community only at sim startup.
- **C8.** English UI, dark theme only.

## 6. Domain rules

### 6.1 Packages and Libraries

- A Package is found by recursively walking a Library. A folder containing `manifest.json` is a Package; the walk does not descend into it.
- Folder structure inside a Library doesn't affect how Packages behave, but folders are used as grouping suggestions (6.3).
- Package identity is its folder name (the name the sim uses). The junction in Community gets the same name.
- The same name in two Libraries is a **Duplicate copy**. One copy is the active copy: by default the one Community already points to, otherwise the first found; the user can change it. Only the active copy can be linked.
- Unregistering a Library never deletes anything; its Managed entries become External entries.

### 6.2 Community entries

Every entry in Community is classified on each reconcile:

| Entry | Rule | App may |
|---|---|---|
| Managed | Junction whose target is inside a registered Library | Create and remove |
| External | Junction or symlink pointing outside every registered Library | Show only |
| Fixed | Real folder | Show only |
| Broken | Link whose target doesn't exist | Remove the link, after confirmation |
| Stray | Loose file | Show only, with a warning |

Removing a link must remove only the link itself, never the target's contents.

### 6.3 Addons

- Every Library Package belongs to at least one Addon. A Package that belongs to no other Addon forms its own single-Package Addon, named from the manifest `title`. A Package that belongs to another Addon has no single-Package Addon.
- A Package may belong to several Addons (typically Asset packs), and Addons may nest (see suggestions below).
- On/off is always done on Addons, never on single Packages.
- **An Addon's On/Off is the user's choice and is stored.** It is not derived from which Packages happen to be linked.
- **A Package should be linked exactly when at least one On Addon contains it.**
  - **Turn on:** link every Package of the Addon that isn't linked yet.
  - **Turn off:** unlink each Package of the Addon that no other On Addon contains.
  - Example: A = {X, P}, B = {Y, P}. Turning A on links X and P. B stays Off. Turning A off unlinks X and P, unless B is On, in which case P stays.
- **Partial** means Community differs from the user's choice for that Addon: an On Addon with a Package not linked, or an Off Addon with a Package linked that no On Addon needs. It typically appears after choosing "keep Community" in the sync dialog. Clicking a Partial Addon turns it on.
- Addons contain only Library Packages. External and Fixed entries are shown read-only where they belong in the hierarchy (with a lock), but can't be grouped or toggled.
- **Suggestions:** the app proposes groupings from:
  - **Library folders**: every folder level that contains more than one Package is a suggestion, named after the folder. Levels nest: `Scenery\Europe` and `Scenery\Europe\SKIATHOS V1.2` are both suggestions, so "Europe" becomes an Addon that contains SKIATHOS's Packages too.
  - manifest `creator`, shared name prefixes, and `dependencies`.

  The user accepts, adjusts or rejects each suggestion; a bulk accept is offered for folder suggestions. Nothing is grouped without acceptance. The user can also create, rename and edit Addons manually.

### 6.4 Categories and hierarchy

- Category comes from manifest `content_type`: `SCENERY` → Scenery, `AIRCRAFT` and `LIVERY` → Aircraft, everything else → Other. The user can override it per Package.
- An Addon's Category is that of its Packages. If they differ, the most prioritised wins (Scenery > Aircraft > Other) — *builder's call if a better rule shows up*.
- **Scenery** contains two branches:
  - **Airports**: each Airport with code and name, its Addons below, a toggle per Addon and one for all.
  - **Landscape**: Scenery Addons that belong to no Airport.
- A Package belongs to an Airport if:
  1. one of its BGL files contains an airport record with that code, or
  2. its folder name or manifest title contains the code as a separate token (`…-eham-…`, `EHAM …`), or
  3. the user linked it manually.

  The user can remove automatic links. A Package can belong to several Airports.
- **Aircraft**: each aircraft Addon with its Liveries underneath. A Livery belongs to the aircraft named in `base_container` in its `aircraft.cfg`.
  - Aircraft identities are also read from Sim-managed packages and from External and Fixed entries, so that Liveries for default, Marketplace or otherwise installed aircraft find their parent. Such parents are shown as locked aircraft nodes — *my call*.
- **Other**: a flat list.
- Addons and Airports can be hidden. A "Show hidden" switch brings them back.

### 6.5 Airports

- An Airport is identified by its ICAO code, or by its local ident when it has none (`OG20`, `U60`, `MA33`).
- Airport data (code, name, position) is read from airport records in BGL files of: Library Packages, Community entries, and Sim-managed packages.
- **Real scenery** for an Airport means: at least one Library or Community Package belongs to it, or a Sim-managed package other than generic or navigation data does. That includes `asobo-airport-*`, partner airports from World Updates, and third-party Marketplace airports — *my call*. Airports known only from generic or navigation data (`fs-base-genericairports`, `fs-base-nav`) have no real scenery.
- Packages that carry navigation data for thousands of airports (such as `navigraph-navdata*`) must not become Airport scenery for every airport. A configurable exclusion list, with sensible defaults, keeps them out of rule 1 in 6.4 — *my call*. Object and region packs such as `a-puf-we-love-vfr-region*` contain no airports and are Landscape by the normal rules.
- **Replaces built-in:** a Package that belongs to an Airport with official real scenery, and either declares a dependency on the official package or overlaps it by relative file path in `layout.json`, is shown as "replaces built-in". This is information, not a warning.

### 6.6 Warnings

Only these are warnings:

- **Duplicate package**: the same Package name in more than one place the sim loads from (Community, Packages root, Official).
- **Duplicate copy**: the same Package in several Libraries.
- **Livery without aircraft**: a Livery is on while its aircraft, a Library aircraft, is off. Liveries for always-loaded aircraft never warn.
- **Broken** and **Stray** entries in Community.
- **Two GSX profiles** for one Airport.

Several Packages at one Airport are shown neutrally ("3 packages"), never as a warning (ADR 0002).

### 6.7 Sim running

- The app detects whether MSFS 2020 is running.
- While it runs, every on/off change is queued and shown as pending. Queued changes are applied automatically when the sim exits.
- The queue is persisted. If the app is closed while the sim runs, the queue is applied on the next app start, if the sim isn't running then — *my call*.

## 7. Startup, scanning and reconciliation

### 7.1 First run

1. Locate `UserCfg.opt` (Steam and Microsoft Store locations), read `InstalledPackagesPath`, derive Community, Official and the Packages root. If not found, ask the user to pick the Community folder.
2. Classify Community and collect the targets of all existing links.
3. Propose Libraries as the common roots of those targets (on Rickard's machine: `C:\Spel\Addons`, `C:\Spel\Orbx Library\msfs`, `C:\Spel\FSDT Addon Manager\MSFS`, `C:\Spel\Neofly\addons`). The user confirms; more can be added later in Settings.
4. Adopt the current Community state as the app's state. No sync dialog on first run.
5. Start the first scan (7.3).

### 7.2 Normal start

1. Show the main window with the last saved state and cached scan results immediately.
2. In the background, reconcile against Community (7.4).
3. Then run an incremental scan (7.3).
4. Update the UI as results come in. On/off works at all times; it only needs Community and Package paths.

Targets (*my call*, to be measured): main window usable with cached data within about 1 second; Community reconcile under 1 second for about 150 entries.

### 7.3 Scanning

- Order on first scan: Community → Libraries → Sim-managed packages, with visible progress.
- Incremental by default: a Package is re-read only if its path, or the timestamp or size of `manifest.json` or `layout.json`, has changed. F5 forces a full rescan.
- Community and all Libraries are watched for changes; affected Packages are rescanned in the background.
- Only Scenery is read from Sim-managed packages; their aircraft are ignored.

### 7.4 Reconciliation

- The app compares its saved state with Community.
- If they differ, a sync dialog lists the differences, one row per Package, grouped by Addon. Each row chooses "keep Community" (preselected) or "restore app state"; buttons apply one direction to all rows.
- "Keep Community" leaves the user's On/Off choices as they are and accepts the links, so an Addon may become Partial (6.3). "Restore app state" creates and removes links to match the choices.
- If Community changes while the app runs and the change didn't come from the app (another tool or an installer), a banner offers the same sync dialog. The app never silently overwrites such a change — *my call*.

## 8. GSX

- Profiles are read from `%APPDATA%\Virtuali\GSX\MSFS` (`.ini`, with an optional `.py` of the same base name) and from inside scenery Packages that ship their own.
- The Airport code is the file name prefix before the first `-`, case-insensitive.
- Status per Airport: **Installed**, **Missing**, **Two profiles**. Profiles with no matching real scenery appear as **Profile only**.
- Only profiles GSX would actually load count: those in the GSX folder, plus those inside scenery Packages that are linked. **Two profiles** means two or more such profiles for one code, wherever they are — *my call*.
- The table also shows whether the Airport's scenery is on, so an Installed profile for scenery that is off is visible.
- Status is shown only for Airports with real scenery (plus Profile only rows).
- Dropping a profile file onto the app copies it into the GSX folder, asking before overwriting.
- Missing profiles get a deep link to flightsim.to, filtered to GSX profiles for the code.

## 9. Finding scenery

For any Airport, the app offers deep links to search flightsim.to and SimMarket for its code. The links are URL templates in Settings, so more stores can be added by the user.

## 10. Screens

Layout and feel follow BOB Dispatcher: dark, calm, airy, left navigation, clear status colours. Exact visuals are the builder's call.

- **Addons** (start view): Category tabs Scenery / Aircraft / Other. Scenery shows the Airports and Landscape tree (6.4). Search, filter (On / Off / Partial), Show hidden. Each Addon row: thumbnail, name, creator, status, warnings, toggle. A detail pane shows the Addon's Packages with paths, versions and Library, the Airports it belongs to, and actions: rename, edit membership, link/unlink Airport, override Category, hide.
- **Suggestions**: proposed groupings to accept, adjust or reject.
- **Map**: Airports with real scenery as markers coloured by state (on / off / built-in). "Show all airports" adds the generic ones, clustered when zoomed out. Airport search. Clicking an Airport opens a side panel: code, name, its Addons with toggles, GSX status, search links.
- **GSX profiles**: table of code, name, scenery state, profile status, actions (open folder, find profile). Filters by status. Drop target for profile files.
- **Community**: every entry with its classification, link target and warnings. Remove Broken links (with confirmation). Choose the active copy for Duplicate copies.
- **Settings**: Community path (detected, overridable), Libraries (add/remove), search URL templates, navdata exclusion list, version and update notice.
- **Overlays**: first-run setup, sync dialog, pending-changes banner while the sim runs, scan progress.

Thumbnails come from the Package's own files (for example `ContentInfo\…\Thumbnail.jpg`), with a placeholder per type when none exists.

## 11. Storage

- **User data** (`%APPDATA%\<app>\`), JSON, small and human-readable: Libraries, Addons and membership, manual Airport links and removed automatic links, Category overrides, hidden items, active copy choices, pending queue, search URL templates, exclusion list, saved Community state for reconciliation.
- **Scan cache** (`%LOCALAPPDATA%\<app>\`), SQLite, disposable: Packages with manifest data and change markers, Airports, Package–Airport links found by scanning, Livery–aircraft links, GSX profiles, thumbnails.
- Deleting the cache must only cost a rescan.

## 12. Structure (suggested)

The builder may change this, but the boundaries matter:

- **Core**: domain model and rules (Addons, Partial state, reference counting, classification, hierarchy, warnings, reconciliation diff). No WPF, no file system.
- **Sim2020**: everything MSFS 2020 specific: locating the install, `UserCfg.opt`, folder layouts, manifest and `layout.json` reading, BGL airport records, `aircraft.cfg`, sim process detection. Behind interfaces in Core (C6).
- **Infrastructure**: junctions, file watching, JSON and SQLite storage, GSX folder access, GitHub release check.
- **App**: WPF + WPF-UI views and view models, WebView2 map.

## 13. Testing

- Core rules are unit tested without touching disk: toggle semantics with shared Packages, Partial state, classification, hierarchy and Airport linking, warnings, reconciliation diff.
- Junction handling is tested against temporary folders, including a guard test that removing a link leaves the target untouched.
- BGL reading is tested with small fixtures. Official sim files are not committed to the repo.
- An opt-in integration test runs against the local MSFS install.

## 14. Left to the builder

- Exact visual design, within section 10 and the BOB references.
- Grouping heuristics for suggestions, within the signals in 6.3.
- BGL parsing details (the format is documented; Little Navmap is a reference).
- How GSX profiles inside scenery Packages are recognised (Dave's code is reference only).
- Map tile provider, as long as it needs no API key and has a dark style.
- How "update available" is shown.
- Logging and diagnostics.

## 15. Calls I made (please check)

- Spec written in English, like GLOSSARY.md and the ADRs.
- Addons contain only Library Packages; External and Fixed entries are read-only.
- Mixed-Category Addons take the most prioritised Category.
- Navdata exclusion list keeps packages like `navigraph-navdata` from being Airport scenery everywhere.
- Marketplace and World Update partner airports count as real scenery.
- Aircraft identities are read from Sim-managed, External and Fixed content so Liveries always find their parent; such Liveries never warn.
- Changes to Community from other tools while the app runs open the sync dialog via a banner.
- Two GSX profiles counts only profiles GSX would load, including those in linked scenery.
- Folder suggestions are not auto-accepted, but can be accepted in bulk.
- Pending queue survives an app restart.
- Startup targets of about 1 second.
- Sync dialog rows are per Package, grouped by Addon.
