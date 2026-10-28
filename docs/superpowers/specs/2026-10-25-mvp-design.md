# Modern MSFS Addon Manager — MVP design

Date: 2026-10-25, revised 2026-10-28 after the UI design session (menus, tabs, cards, tags, Library types). Status: draft, revised after a builder check and two independent reviews.

Vocabulary is defined in [GLOSSARY.md](../../../GLOSSARY.md) and used strictly below. Background and the full decision log are in [ANALYS.md](../../../ANALYS.md) (Swedish). Architectural decisions are in [docs/adr](../../adr).

## 1. Intent

Rickard manages a large MSFS 2020 setup (118 Community entries, about 250 Library Packages in four Libraries) with MSFS Addons Linker. He likes two things about it: turning content on and off quickly, and a map of installed airports plus the airports built into the sim. He dislikes its ugly, clunky interface, and misses a link to GSX Pro profiles and a way to find scenery he doesn't have.

This app replaces Addons Linker for that workflow. Scenery is the focus, aircraft come second, everything else is "Other" with plain on/off. It must start fast, never put the user's files at risk, and look as calm and clear as BOB Dispatcher (screenshots in `C:\Users\ricka\OneDrive\Bilder\bob\`).

**Guiding principle (2026-10-28):** the automation proposes, the user decides. Every automatic grouping, placement and Airport link can be overridden on the card, and an override always wins.

## 2. Goals

1. Turn content on and off per Addon or per single Package by linking Packages into Community with directory junctions.
2. Show the user's Addons as cards under Scenery, Aircraft and Other, each with its own tabs, filtered by the Library types the user chose.
3. Group the Packages of one Airport into one Addon automatically, and let the user merge, split and move Addons by drag and drop.
4. Show Aircraft with their Liveries and Mods.
5. Show a map of Airports with real scenery, with an option to show all airports.
6. Show GSX Pro profile status on Airport cards.
7. Offer deep links to find scenery and GSX profiles the user doesn't have.
8. Start instantly from cached state; do all scanning in the background.
9. Take over an existing Addons Linker setup without manual work beyond adding the Libraries.

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
- Everything in section 17 (Later).

## 4. Anti-goals (failure even if it works)

- **Any user file moved, modified or deleted** without an explicit, specific user action. In particular, the app must never delete recursively through a junction or symlink.
- Community left in a broken state after the app is closed, crashes or is uninstalled.
- A UI that freezes during scanning, or a startup that waits for scanning.
- Changes to Sim-managed packages, Fixed entries or Managed elsewhere Packages.
- False alarms that train the user to ignore warnings (ADR 0002). Every warning rule below has been checked against Rickard's real install for this reason.
- A package that is on in Community but shown nowhere in the app without the user being told (6.4, "Not shown").

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
- **C10.** Only one instance of the app runs at a time (a named mutex). A second start brings the running window to the front and exits.

## 6. Domain rules

### 6.1 Packages and Libraries

- A Package is found by recursively walking a Library. A folder containing `manifest.json` is a Package; the walk does not descend into it.
- Package identity is its folder name (the name the sim uses). The junction in Community gets the same name.
- A Package's display name is its manifest `title`, or its folder name when the title is empty. The folder name is shown as secondary text, because titles repeat (five Packages are titled "Simple Scenery" on Rickard's machine).
- **Library types:** every Library has one or more of the types **Scenery**, **Aircraft** and **Other**, chosen by the user. Every Library is always scanned completely; its types only decide what is shown (6.4). The same folder may carry several types (a user who keeps everything in `C:\Sim\Addons` gives it all three).
- The same name more than once across the Libraries (in two Libraries, or at two depths in one) is a **Duplicate copy**. One copy is the active copy: by default the one Community already points to, otherwise the first by Library registration order, then path; the user can change it in the card's dialog. Only the active copy can be linked. Changing the active copy of a linked Package re-points the link.
- Unregistering a Library never deletes anything; its Managed entries become External entries.
- **Missing Packages:** all user data for a Package (choice, Addon membership, active copy, manual links, flags) is kept while the Package can't be found, for example when a Library drive is unplugged, a folder is renamed, or a tool deletes and re-creates a Package during an update. The Package is shown as missing in its Addon's dialog. When a Package with the same name reappears, it resumes its old place. Only an explicit user action forgets a missing Package.
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
- **Addons have no state of their own.** An Addon's state is computed from its Packages that the app may switch (not locked, not Sim-managed, not Fixed or External): **On** when all are on, **Off** when all are off, **Mixed** otherwise. An Addon with no switchable Package has no state (it is **Locked**).
- On/off is applied in the Addon's dialog, never on the card wall: one control switches every switchable Package of the Addon, and each Package has its own switch.
- **Managed elsewhere** is an attribute of a Library, inherited by every Package in it, including Packages that appear there later; it can be overridden per Package. A Package is locked when its active copy lies in such a Library (so a user's own copy of `neofly-neofly-cargo` in `C:\Sim\Addons` isn't locked by marking the Neofly Library). A locked Package's link is never created or removed by the app, by any action; "switch all" skips it and says so.
- **Name taken** (6.2) rejects a turn-on at apply time; the choice isn't stored.
- Fixed and External entries are Packages of read-only Addons, shown with a lock.

### 6.4 Menus, tabs and visibility

- The app has three menu sections, **Scenery**, **Aircraft** and **Other**, each with its own tabs:

  ```
  Scenery                         Aircraft                         Other
  Airports | Landscapes | Assets  Aircraft | Instruments | Other   (no tabs)
  ```

- **Category (the menu section) comes from content:** manifest `content_type` `SCENERY` → Scenery, `AIRCRAFT`, `LIVERY` and `INSTRUMENTS` → Aircraft, everything else → Other. Exceptions: a `SCENERY` Package with no BGL files at all (`fsdreamteam-gsx-pro`, `neofly-neofly-smokes`) is Other; a Package where any `aircraft.cfg` declares `isAirTraffic=1` (for example `fsltl-traffic-base`, 2,551 such files, and `apronlife-jetway-helper`) is Other and isn't parsed for Liveries. An `AIRCRAFT` Package that belongs to no aircraft (6.6), such as `orbx-volanta-aerocaches`, stays in Aircraft on its Other tab (this replaces plan 2's rule that made it Other). An Addon's Category is the Category of its first Package (by name) unless the user moved it.
- **Visibility:** a Library Package is shown in its menu section only when its Library carries that type. A Package whose Library lacks the type is listed under Settings → **Not shown** ("3 packages in Libraries not registered for their type"), with a shortcut to add the type to that Library. Packages outside every Library (Fixed and External entries) are always shown in their menu section, locked. An Addon the user moved to a menu section is shown there regardless of its Libraries' types.
- **Initial tab** (computed until the user moves the Addon):
  - Scenery: **Airports** when the Addon is anchored to an Airport or has an Airport code (6.5); otherwise **Assets** when it is an Asset pack; otherwise **Landscapes**.
  - **Asset pack test (2026-10-28):** a Scenery Addon whose Packages have no BGL section with placed objects (`0x25`) or terrain (`0x65`): only model data (`0x2B` ModelData, `0x30`), other non-placing sections, or no BGL at all (a card the user moved to Scenery). On Rickard's machine this gives `esd-modellib-eolib`, `library1v14-cbj`, `library2v14-cbj`, `mikeaat-assetpack` and `us-firedeptpack`. `human-library-animated` places objects and starts in Landscapes; the user drags it to Assets.
  - Aircraft: **Instruments** when the Package's `content_type` is `INSTRUMENTS` (`pms50-instrument-gtn750-pro`, `sky4sim-os`); **Aircraft** when it is part of an aircraft Addon (6.6); otherwise **Other**. (`navigraph-avionics-*` ship navigation data as `MISC` and belong to the Other menu section by content.)
- **Tags:** the folders between the Library root and a Package become the Package's tags, by these rules:
  - A Library root is not a tag.
  - A folder that holds a single Package (directly or below) gives no tag.
  - Tool layouts collapse to the product: `Orbx Library\msfs\<product>\Community\<pkg>` gives no `Community` tag and no product tag when the product holds one Package.
  - An Addon's tags are the union of its Packages' tags.

  Tags are shown on the card's dialog. Filtering and editing are Later (17).
- **Built-in content is shown only when the user has an add-on for it** (2026-10-28): a Package that belongs to the same Airport (Replaces built-in), a Livery or Mod of the same aircraft, or a GSX profile for the Airport. The built-in part is shown locked in the dialog. Otherwise built-in Airports appear only on the map, and built-in aircraft not at all.

### 6.5 Addons and Airports

- **An Addon is shown as a card.** Every Package belongs to exactly one Addon.
- **Initial Addons (first scan, and for new Packages later):**
  - All Packages belonging to the same Airport form **one Addon per Airport**, named from the Airport (for example "EHAM Schiphol" with `brickbybrickdesign-airport-eham-amsterdam`, `duijffmedia-airport-eham-amsterdam`, `duijffmedia-schiphol-amsterdam-mega-enhancement-lighting`). The Addon is **anchored** to that Airport.
  - **A Package that belongs to several Airports** (counted distinct codes, not records: `tag21-airport-ybas-alice-springs` has two YBAS records and is a plain YBAS Package): if exactly one of its codes appears as a separate token in its folder name or manifest title (the token rule of rule 2, stop-list included), it joins that Airport's Addon and the other codes are listed in the dialog (`sltcreations-airport-yq05-mount-mulligan` → YQ05, with YMUZ "Mount Mulligan Lodge" listed). Otherwise it gets **its own Addon with no Airport code**, shown as "(several)" on the card (`asf-mission-demo` with FEFA and FEGK); the dialog lists every code and the map shows the Package at each.
  - Aircraft Packages form one Addon per aircraft, anchored to its container (6.6).
  - Every other Package is its own Addon.
  - **New Packages later** join the Addon anchored to their Airport or container if one exists, otherwise they become a new Addon. They never rearrange existing Addons.
- **User changes (all saved immediately, no save button):**
  - **Merge:** drag a card onto another card. The target takes over the dragged Addon's Packages; the target's name, image, Airport code, menu section and tab stay. Any two cards can be merged, including two Airport cards, a card with only built-in or locked parts, and a GSX-only card. A merged Airport card shows the target's code; the other codes are listed in the dialog.
  - **Move:** drag a card onto a tab header (same menu section) or onto a menu section in the left menu. A card moved to another menu section lands on the tab the initial-tab rules give there.
  - **Split:** "Move out" on a Package row in the dialog gives that Package its own Addon by the initial rules.
  - **Edit:** name, image (the thumbnail of one of the Addon's Packages, or a file the user picks; only its path is stored, the file is never copied or moved) and **Airport code** in the dialog.
  - **Airport code field:** any 3–5 letters or digits are accepted (C3 of plan 2). Known codes are suggested with their names while typing. Setting a code links the Addon's Packages to that Airport manually (rule 3 below), moves the card to Airports (unless the user moved it elsewhere) and, when another Addon already has that code, merges this card into it. A code no record knows gives a card with that code and the user's name, and no map marker. Clearing the field removes the Addon's links to that code, automatic ones included.
  - Anchors survive rename and merge. When an Addon is split, the anchor stays with the card the dialog was opened on.
- A Package belongs to an Airport if:
  1. one of its BGL files contains an airport record with that code, unless the Package is excluded (below), or
  2. rule 1 found no counted airport record in it, it is a Scenery Package that isn't Sim-managed, and its folder name or manifest title contains the code as a separate token that is not on a stop-list of common words (`PACK`, `SAVE`, `SWIM`, `SANS` and similar). 4–5 character tokens may sit anywhere (`…-esgj`, `EHAM …`); 3-character tokens only count as the leading token (`u60-u60`, `c53-…`), because about 3,000 generic idents are 3 characters long, or
  3. the user linked it manually (the Airport code field).

  Some real airport Packages have neither an airport record nor a code token (for example `005builds-lszrsceneryv11`, `grancanaria`); they land in Landscapes until the user types the code on their card.
- An Airport is identified by its ICAO code, or by its local ident when it has none (`OG20`, `MA44`, `U60`, `3U2`, `YX01`). Idents of 3–5 characters count, as in the sim; add-on airports use 5-character idents (`YALR1`, `YHAC1` from sltcreations, official `RJTIH`). Empty idents are ignored. Non-airports with airport records (`VOLC1`–`VOLC5` in `microsoft-airport-voloport`, `NEOF2` in `neofly-neofly-cargo`) are kept out by the exclusion list.
- Airport data (code, name, position) is read from airport records in BGL files (MSFS 2020: section type `0x03`, record id `0x0056`) of Library Packages, Community entries and Sim-managed packages.
- **Encrypted official airports:** some handcrafted official airports have no readable BGL (`asobo-airport-eddf-frankfurt`, `-egll-heathrow`, `-kden-denver`, `-lemd-adolfo-suarez`, `-omdb-dubai-intl`, `-heca-cairo`, `-fact-cape-town`). For Sim-managed airport packages where no airport record is found (no BGL at all, or BGLs without an airport record like `asobo-airport-lfcs-saucats`), the code comes from the package name (`*-airport-<code>-*`) and the name and position from the generic record for that code. This rule links only codes that are known from other records, so a Sim-managed name never creates a nameless Airport.
- **Excluded from rule 1:**
  - Packages with airport records for **more than 10 airports** (configurable). Verified: `fs-base-genericairports` 40,351, `fs-base-nav` 22,555, `navigraph-navdata` 15,629, `fsdreamteam-gsx-world-of-jetways-2020` 897, `microsoft-nordics2-point-of-interest` 87. The user can opt a Package back in; opting in overrides both the threshold and an exclusion pattern.
  - Packages on a configurable exclusion list. Defaults include `navigraph-navdata*`, `*-point-of-interest`, `fsdreamteam-gsx-world-of-jetways*`, `microsoft-airport-voloport` and `neofly-*`.
  - Records of a Package excluded by pattern are ignored everywhere: they create no Airports and are no known codes for the package-name rule. Records of a Package excluded only by the threshold (generic airports, navigation data) still supply known codes, names and positions.
  - An airport name that is a `TT:` localisation key (`TT:AIRPORTLR.LOWI.name`, only in Official Asobo packages) is resolved through `fs-base-genericairports\en-US.locPak` in the Official folder: exactly by key, else by the airport code in the key (`TT:AIRPORT.CYTZ.name` has no region part). A key that doesn't resolve, or a missing or unreadable locPak, counts as no name.
- **Real scenery** for an Airport: at least one non-excluded Library, Community or Sim-managed package belongs to it, or the user linked one manually. That includes `asobo-airport-*`, World Update partner airports and Marketplace airports. Airports known only from generic or navigation data have no real scenery.
- **Replaces built-in:** a Package that isn't Sim-managed, belonging to an Airport that a Sim-managed airport package also belongs to. (World Update partner airports such as `microsoft-airport-edny-friedrichshafen` have third-party creators but are Sim-managed, so the test is where the Package lives, not who made it.) A dependency on the official package is an extra signal. Inactive Duplicate copies are analysed and linked like active ones. This is information, not a warning.

### 6.6 Aircraft and Liveries

- Aircraft identity is the **Container** name (the folder under `SimObjects\Airplanes\` that holds an `aircraft.cfg`). Containers are indexed across Library Packages, Community entries and Sim-managed packages. Where `aircraft.cfg` is unreadable (encrypted official or Marketplace aircraft with `.fsarchive` files), container names come from paths in `layout.json`. Encrypted aircraft provide their containers in any source (Library, Community or Sim-managed), not only Sim-managed.
- `base_container` is a relative path (`..\Aerosoft_CRJ_900`); its last segment names the container.
- A **Livery** Package is one where every `aircraft.cfg` has a `base_container` whose container isn't provided by the same Package. Most Liveries declare `content_type: AIRCRAFT`, so `content_type` alone can't tell. Livery detection runs only for the Aircraft Category.
- **One Addon per aircraft, keyed by container:** every Library Package that provides the container, plus the Liveries whose `base_container` names it, form one Addon. They are Packages of that Addon and can be switched one by one in its dialog. When two Library Packages provide the same container (`sal1800-cessna-170b-backcountry` and `sal1800-cessna-super-170b-OPTIONAL` both provide `Cessna-170B-Backcountry`), both join the same Addon; scan order never decides.
  - When a Sim-managed, Fixed or External Package also provides the container, it is shown as the Addon's locked base, and the Library Packages that provide the same container are labelled **mods** (`mge-asobo-aircraft-c152` provides `Asobo_C152`, like the official C152). A built-in aircraft with no Livery or Mod gets no card (6.4).
  - A Package that provides several containers joins the Addon of the alphabetically first one and is noted on the others.
  - A Package without `aircraft.cfg` (`flybywire_A320Neo_Cabin`, `simflight-C152-interior-navtablet`) is matched by its `SimObjects\Airplanes\<folder>` names first, then by `dependencies` naming a known aircraft Package, otherwise it is its own Addon on the Aircraft → Other tab.

### 6.7 Warnings

Only these are warnings:

- **Duplicate package**: the same Package name in Community and in `Official`.
- **Duplicate copy**: the same Package more than once across the Libraries. The active copy is chosen in the dialog.
- **Livery without aircraft**: a Livery is on while no provider of its base container is on or always loaded.
- **Broken** and **Stray** entries in Community (shown in the banner, 10).
- **Two GSX profiles** for one Airport (8).

Several Packages at one Airport are shown neutrally ("3 packages"), never as a warning (ADR 0002). Ignored entries are neutral info.

### 6.8 Applying changes and the sim

- **Every link change** (on/off in a dialog, "restore app state" in the sync dialog, removing a Broken link, changing an active copy) goes through one apply path.
- **Write order:** the link is changed first; only when that succeeds are the choice and the Community snapshot saved, in one write. A failure leaves the stored state as it was and is reported.
- **Startup self-check:** after reconciling, the app checks every Managed Package for "on ⇔ linked". Any mismatch (for example after a crash between link and save) becomes a row in the sync dialog, even when Community matches the snapshot.
- The app detects whether MSFS 2020 (`FlightSimulator.exe`) is running.
- While it runs, the apply path doesn't touch Community. It records which Packages are affected and shows them as **Pending** ("Next sim start" on the card); the card's light shows the chosen state. When the sim exits, the app re-derives the links of those Packages from the stored choices and applies them. Switching a Package on, off and on again while the sim runs therefore results in one correct change.
- The pending set is persisted. If the app is closed while the sim runs, it is applied on the next app start, after reconciliation, if the sim isn't running then.

## 7. Startup, scanning and reconciliation

### 7.1 First run

1. Locate `UserCfg.opt` (Steam: `%APPDATA%\Microsoft Flight Simulator\`; Microsoft Store: under `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_…\LocalCache\`), read `InstalledPackagesPath`, derive Community and Official. If not found, ask the user to pick the Community folder.
2. Classify Community and collect the targets of all existing links.
3. **The user adds Libraries (2026-10-28).** The first-run screen starts with an empty list and "Add Library…": the user picks a folder and its types (Scenery, Aircraft, Other). No Libraries are proposed. The existing junction targets are used only to show coverage ("87 of 95 links covered"), so a forgotten folder is noticed. A target under `…\FSDT Addon Manager\MSFS\` is not counted as uncovered: FSDT's Addon Manager owns its junctions, and those entries stay External.
4. For each Library the user can tick **Managed elsewhere**, with a plain explanation of the trade-off: the app won't switch those Packages, and switching them here would be undone by the tool if it relinks.
5. Adopt the current Community state: each Package is on if linked, off if not. No sync dialog on first run.
6. Run discovery, then the content scan (7.3). On/off is available as soon as discovery is done.

### 7.2 Normal start

1. Show the main window with the last saved state and cached scan results immediately.
2. In the background, reconcile against Community (7.4).
3. Then run an incremental scan (7.3).
4. Update the UI as results come in. On/off works at all times; it only needs Community, Package paths and the stored choices.

Targets (to be measured): main window usable with cached data within about 1 second; Community reconcile under 1 second for about 150 entries.

### 7.3 Scanning

Two passes:

1. **Discovery** (fast, seconds): walk Libraries and Community, read `manifest.json`, find Packages, Duplicate copies and Categories. Enough for on/off.
2. **Content**: BGL airport records, `aircraft.cfg` and containers, `layout.json`, GSX profiles, thumbnails. Builds Airports and Addons.

- Order on first scan: Community → Libraries → Sim-managed packages, with visible progress. The generic airports (about 40,000 in almost 5,000 BGL files) are read once and cached.
- Incremental by default: a Package is re-read only if its path, or the timestamp or size of `manifest.json` or `layout.json`, has changed. F5 forces a full rescan.
- Community and all Libraries are watched for changes; affected Packages are rescanned in the background. Community is watched non-recursively; changes inside linked Packages are seen through the Library watchers. Watchers overflow during large installs (`C:\Sim\Addons` holds about 123,000 files), so an overflow triggers an incremental rescan.
- From Sim-managed packages, the app reads Scenery and aircraft containers. Their aircraft are never toggleable.

### 7.4 Reconciliation

- The app stores the **last accepted Community snapshot**: every entry with its resolved target. Reconciling compares that snapshot with the current Community, not with the user's choices.
- Changes to External, Fixed and Ignored entries, and to Managed elsewhere Packages, are recorded silently and never open the dialog. For Managed elsewhere Packages, "recorded" means the stored choice follows Community.
- If other differences exist, a sync dialog lists them, one row per Package, grouped by Addon. Each row chooses "keep Community" (preselected) or "restore app state"; one choice can be applied to all rows.
- "Keep Community" sets the Package's stored choice to match Community. "Restore app state" changes the link through the apply path (6.8). Either way, the snapshot is updated.
- If Community changes while the app runs and the change didn't come from the app (for example Addons Linker, which may run alongside), the user is told and offered the same sync dialog, with a way to not be asked again for the rest of the session. The app never silently overwrites such a change.

## 8. GSX

- Profiles are read from `%APPDATA%\Virtuali\GSX\MSFS`.
- **Airport code from the file name:** the leading run of 3–4 letters or digits, followed by `-`, `_`, `.` or the end of the name (`epgd_LukSid.ini` → EPGD, `LPMA.ini` → LPMA). The code is checked against known Airports.
- `.off` files are disabled profiles: shown as disabled, not counted.
- `.py` files belong to the Airport by code, not by base name (`essa-orbx.py` belongs with `essa-bwib1r-noolvdgs.ini`). A `.py` doesn't need an `.ini` with the same name. When an Airport has two profiles, its `.py` files are listed with the Airport, not paired with one profile.
- Status per Airport: **Installed**, **Missing**, **Two profiles** (two or more active `.ini` profiles in the GSX folder for one code; real cases today: LDSP, LFMN, LFPG).
- **Where it is shown (2026-10-28):** as a mark on the Airport card (✓ / missing / two) and in full in the card's dialog. There is no GSX table. A GSX profile is an add-on: a profile for a built-in Airport gives that Airport a card (locked, no light), and a profile whose code matches no Airport with real scenery gives a **profile-only** card.
- **Profiles shipped with scenery:** some Packages carry a profile in a subfolder (`GSX\`, `GSX-PROFILE\`, `GSX-Profile-EPKK\`). They are recognised by `<code>….ini`/`.py` file names, not by the folder name (`aerosoft-crj\Data\GSX\` holds aircraft `gsx.cfg` files, not profiles). They are shown in the dialog as "shipped with scenery, not installed" with an action to copy the profile into the GSX folder, and they don't count for Two profiles. GSX's own log (`Couatl.err`) has only ever shown it loading profiles from its own folder, which supports this; see 15.
- Missing profiles get a deep link to flightsim.to, filtered to GSX profiles for the code.

## 9. Finding scenery

For any Airport, the app offers deep links to search flightsim.to and SimMarket for its code. The links are URL templates in Settings, so more stores can be added by the user.

## 10. Screens

Layout and feel follow BOB Dispatcher: dark, calm, airy, clear status colours. Mockups from the 2026-10-28 session: card dialog (B) and card style (A).

```
┌───────────┬──────────────────────────────────────────────────┐
│ Scenery   │ Airports | Landscapes | Assets     Cards | Map   │
│ Aircraft  │ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐              │
│ Other     │ │ img 🟢│ │ img 🟡│ │ img 🔴│ │ img 🔒│              │
│           │ │ EHAM │ │ ESSA │ │ LOWI │ │ LFCS │              │
│           │ │Schip.│ │Arlan.│ │Innsb.│ │Sauc. │              │
│           │ └──────┘ └──────┘ └──────┘ └──────┘              │
│ Settings  │                                                  │
└───────────┴──────────────────────────────────────────────────┘
```

- **Left menu:** Scenery, Aircraft, Other; Settings pinned to the bottom. Dropping a card on a menu section moves it there.
- **Tabs** per menu section as in 6.4. Dropping a card on a tab header moves it there.
- **Card wall:** large cards with the image on top (the image of the first Package that has one, the user's choice, or a placeholder per type). An Airport card shows its code in bold and the Airport name below; other cards show the Addon name. Below: package count, GSX mark (Airports) and warnings. **No switch on the wall.** A traffic light in the top right corner shows state: red Off, yellow Mixed, green On, grey with a lock when Locked. A Pending card also shows "Next sim start". Missing Packages, "(several)" and built-in parts are labelled.
- **Card dialog** (opens on click; Esc, ✕ or a click outside closes it): editable name, image and Airport code (saved immediately); one control to switch all switchable Packages; the Packages, each with display name, folder name, path, version, Library, Duplicate copy choice and its own switch, plus "Move out"; locked and built-in parts dimmed; Airport codes, tags, Replaces built-in, GSX profiles (installed, shipped, missing with a search link) and deep links to find scenery.
- **Map** (Scenery → Airports, view switch `Cards | Map`): Airports with real scenery as markers coloured by state (on / off / built-in). "Show all airports" adds the generic ones, clustered when zoomed out. Airport search. Selecting an Airport opens its card's dialog, or shows code, name and search links when it has no card.
- **Settings:** Community path (detected, overridable); Libraries (add/remove, types, Managed elsewhere, link coverage); **Not shown** (6.4); **Community** (every entry with its classification, link target and warnings; remove Broken links with confirmation); search URL templates; airport threshold, exclusion list and token stop-list; version and update notice.
- **Banner** at the top of the window: Broken and Stray entries in Community (with a link to Settings → Community), pending changes while the sim runs, scan progress.
- **Overlays:** first-run setup (7.1) and the sync dialog (7.4).

Thumbnails come from the Package's own files (`ContentInfo\<any>\Thumbnail.jpg`; the subfolder name differs from the Package name; 198 of 228 Library Packages in `C:\Sim\Addons` have one), with a placeholder per type when none exists.

## 11. Storage

- **User data** (`%APPDATA%\<app>\userdata.json`), JSON, small and human-readable, saved on every change (write to a temporary file, keep a `.bak`, then replace): Libraries with their types; each Package's on/off choice, Managed elsewhere flag and active copy; Addons (their Packages, anchor, and the user's overrides: name, image path, Airport code, menu section, tab); manual and removed Airport links; opted-in Packages; pending set; search URL templates; threshold, exclusion list and stop-list; the last accepted Community snapshot.
- **What is stored versus computed:** an Addon's membership and anchor are stored when it is created and change only by user action. Its name, image, Category, tab, tags and state are computed on every analysis unless the user overrode them, so a rescan that finds an airport record in an untouched Landscape card moves it to Airports.
- **Scan cache** (`%LOCALAPPDATA%\<app>\`), SQLite, disposable: per Package the change marker and the facts read from its files (manifest data, airport records, BGL section types, aircraft.cfg facts, airplane folders, thumbnail path), and GSX profiles (plan 4). Links, Airports and aircraft groups are recomputed from these on every analysis.
- Deleting the cache must only cost a rescan. User data never lives in the cache file, so a cache rebuild can never touch it (2026-10-28).
- New Packages found after first run get an initial Addon by the rules in 6.5; the user's existing arrangement is never rearranged.

## 12. Structure (suggested)

The builder may change this, but the boundaries matter:

- **Core**: domain model and rules (Package choices and links, Addons and their computed state, visibility, tabs and tags, Airport linking, warnings, apply path and pending set, reconciliation diff). No WPF, no file system.
- **Sim2020**: everything MSFS 2020 specific: locating the install, `UserCfg.opt`, folder layouts, manifest and `layout.json` reading, BGL airport records, `aircraft.cfg` and containers, sim process detection. Behind interfaces in Core (C6).
- **Infrastructure**: junctions (P/Invoke), file watching, JSON and SQLite storage, GSX folder access, GitHub release check, the single-instance mutex.
- **App**: WPF + WPF-UI views and view models, WebView2 map.

The reconcile, watcher, apply path, pending set and own-change suppression form one coupled subsystem; it should be planned as one unit with its state machine written down.

## 13. Testing

- Core rules are unit tested without touching disk: on/off per Addon and per Package, computed state (On/Off/Mixed/Locked), Managed elsewhere skipping, visibility and Not shown, initial Addons and tabs, the Asset pack test, tags, "(several)", merge/move/split and the Airport code field, Airport linking (threshold, exclusions, stop-list, encrypted official airports), Livery and mod rules, built-in cards, warnings, pending set, reconciliation diff.
- Junction handling is tested against temporary folders, including guard tests that removing a link leaves the target untouched, that a failed creation leaves no empty folder, and that a taken name is never overwritten.
- BGL reading and GSX file-name parsing are tested with small fixtures, including the real-world names above. Official sim files are not committed to the repo.
- Guard tests for the state machine: a moved Package is re-pointed, a missing Package keeps its user data and its place in its Addon, a failed link change saves nothing, the self-check catches "on but not linked".
- **First-run fixture:** a sanitised copy of Rickard's Community (entry names, kinds, targets) and Library tree (folders and manifests, no content) is committed, so initial Addons, tabs and tags are tested as a whole scenario.
- A collision report (all Package name and title tokens against the generic idents) is part of the content-scan tests, so the token stop-list is checked against data.
- An opt-in integration test runs against the local MSFS install.

## 14. Left to the builder

- Exact visual design within section 10 and the BOB references.
- BGL parsing details (the format is documented; Little Navmap is a reference).
- How GSX profiles inside scenery Packages are recognised (Dave's code is reference only).
- Map tile provider, as long as it needs no API key and has a dark style.
- How "update available" is shown.
- Logging and diagnostics.

## 15. Open investigation

- **Does GSX Pro load profiles shipped inside scenery Packages?** Evidence so far (GSX's own log) says no. If it turns out yes, shipped profiles count as active, and Two profiles may change for ESSA, ESGG, EDDF and EPKK on Rickard's machine. Note that a shipped and an installed profile can be variants of the same profile (ESSA: `essa-bwib1r.ini` and `essa-bwib1r-noolvdgs.ini`), which would make a plain Two profiles warning a false alarm.

## 16. Calls I made (please check)

- Spec written in English, like GLOSSARY.md and the ADRs.
- Addons are anchored to their Airport or container, so new Packages find the right Addon after renames and merges.
- Missing Packages keep all user data until the user forgets them; moved Packages are re-pointed.
- `neofly-*` is on the default exclusion list (its cargo package carries a fake airport record).
- `SCENERY` Packages without any BGL go to Other.
- Marketplace and World Update partner airports count as real scenery.
- Changes to Community from other tools while the app runs offer the sync dialog, with "don't ask again this session".
- The pending set survives an app restart.
- Startup targets of about 1 second.
- Sync dialog rows are per Package, grouped by Addon.
- An Addon's Category is that of its first Package by name unless moved; mixed-Category Addons arise only from user merges.
- A split keeps the anchor on the card whose dialog was open.
- Built-in parts and locked Packages don't count towards an Addon's On/Off/Mixed state.

## 17. Later (after the MVP)

Decided on 2026-10-28 to come after the MVP, in roughly this order of interest:

- **Filter and search** on the card wall, including filtering by tag.
- **Hide** and **Show hidden** for cards.
- **Delete from disk** for a card's Packages, as an explicit user action that never deletes through a junction.
- **Editing tags.**
- **Switching card layout** (large cards / compact cards).
- **Setting a map position** for an Airport code that no record knows (preferably by clicking on the map).
- **Switching GSX profiles on and off** (`.ini` ↔ `.off`).
- **Installing GSX profiles by drag and drop**, asking before overwriting.
- **Uses**: an Addon using another (automatic from manifest `dependencies` and set by hand), with the "Used Addon off" warning.
- **Undo** for merges, moves and edits.
