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
- Changes to Sim-managed packages, Fixed entries or External entries.
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
- **Name held by a Community entry:** when a Fixed or External entry holds a Library Package's name (on Rickard's machine `neofly-neofly-cargo`, `-objects`, `-smokes` and `bagolu-c152-neoflybannertow` link into the unregistered `C:\Sim\Neofly\addons`), it is one Package whose active copy is the Community entry, locked. The Library copy is listed as an inactive copy ("held by an External entry") and can't be made active while the entry exists.
- A Package's display name is its manifest `title`, or its folder name when the title is empty. The folder name is shown as secondary text, because titles repeat (five Packages are titled "Simple Scenery" on Rickard's machine).
- **Library types:** every Library has one or more of the types **Scenery**, **Aircraft** and **Other**, chosen by the user. Every Library is always scanned completely; its types only decide what is shown (6.4). The same folder may carry several types (a user who keeps everything in `C:\Sim\Addons` gives it all three).
- **Nested Libraries** (`C:\Sim\Addons` as Other and `C:\Sim\Addons\Scenery` as Scenery) are allowed. A Package inside several Libraries is found once and has the types of all of them; since content decides the menu section and types only decide visibility, a Scenery Package is never shown under Other because of the outer Library (Rickard, 2026-10-28: "paket av typen Scenery ska aldrig dyka upp (automatiskt) under Other, och paket av andra typer än Scenery ska inte synas under Scenery"). The Library shown in the dialog is the innermost one.
- The same name more than once across the Libraries (in two Libraries, or at two depths in one) is a **Duplicate copy**. One copy is the active copy: by default the one Community already points to, otherwise the first by Library registration order, then path; the user can change it in the card's dialog. Only the active copy can be linked. Changing the active copy of a linked Package re-points the link.
- **Registering a Library later** (Settings) adopts the Community state of its Packages like first run: on if linked, off if not, without a sync dialog except for Stale entries (7.1, step 5). Their initial Addons follow 6.5. Adoption sets the on/off choice from Community; all other user data of Packages that were known before (Addon membership, active copy, links, flags) is kept.
- Unregistering a Library never deletes anything; its Managed entries become External entries. Its other Packages are not Missing: they leave the card wall and Not shown, and their user data is kept and revived when the Library is registered again.
- **Missing Packages:** all user data for a Package (choice, Addon membership, active copy, manual links, flags) is kept while the Package can't be found, for example when a Library drive is unplugged, a folder is renamed, or a tool deletes and re-creates a Package during an update. The Package is shown as missing in its Addon's dialog, and counts as lying in the Library it was last found in (its last known path is stored). When a Package with the same name reappears, it resumes its old place. Only an explicit user action forgets a missing Package. While a Package is missing, its Addon keeps the Category, tab, display name, thumbnail path and Airport links computed when the Package was last found; these are stored in user data with its last known path, so the card stays where it was.
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
| Stale | Link whose target doesn't exist and whose name matches a Library Package (6.1, Moved Packages) | Re-point or remove, via the sync dialog |
| Stray | Loose file | Show only, with a warning |

- Link targets are compared as resolved, normalised paths, never by entry name: strip the `\\?\` prefix, ignore a trailing backslash (Addons Linker adds one, Neofly doesn't), and resolve relative symlink targets against Community.
- Removing a link must remove only the link itself, never the target's contents.
- **Name taken:** the app never creates a link where any Community entry with that name already exists. When the holder is a Fixed or External entry, the Package is locked with that entry as its active copy (6.1, Name held by a Community entry). When the holder is not a Package (an Ignored or Stray entry), the Package can't be turned on, counts as off, and the app says which entry holds the name.

### 6.3 State and on/off

- **Only Packages have an on/off state.** It is the user's choice, stored by the app. A Package should be linked exactly when it is on.
- **Addons have no state of their own.** An Addon's state is computed from its Packages that the app may switch (not Sim-managed, not Fixed or External): **On** when all are on, **Off** when all are off, **Mixed** otherwise. An Addon with no switchable Package has no state (it is **Locked**). Offline and Missing Packages count with their stored choice, and the card says so; a Package whose name is taken (6.2) counts as off.
- On/off is applied in the Addon's dialog, never on the card wall: one control switches every switchable Package of the Addon, and each Package has its own switch.
- **Every Package in a registered Library can be switched** (2026-10-28: Managed elsewhere was dropped), unless a Fixed or External entry holds its name (6.1). A folder that another tool also links from can be registered and switched from both: Orbx Central downloads into `C:\Sim\Orbx` and makes its own junctions, and the app treats them as Managed entries; when Orbx Central changes a link, reconciliation picks it up (7.4). A folder the user doesn't register stays External, shown locked on the right card. "Switch all" skips locked Packages and says so. The switches of Missing and Offline Packages are disabled, with the reason shown. A Package whose name an Ignored or Stray entry holds is skipped by "switch all" like a locked Package, and the dialog names the holder.
- **Name taken** (6.2) rejects a turn-on at apply time; the choice isn't stored.
- Fixed and External entries are locked Packages, shown with a lock. They join Addons like any other Package (an External EHAM Package joins the EHAM Addon); an Addon made only of locked Packages is Locked.

### 6.4 Menus, tabs and visibility

- The app has three menu sections, **Scenery**, **Aircraft** and **Other**, each with its own tabs:

  ```
  Scenery                         Aircraft                         Other
  Airports | Landscapes | Assets  Aircraft | Instruments | Other   (no tabs)
  ```

- **Category (the menu section) comes from content:** manifest `content_type` `SCENERY` → Scenery, `AIRCRAFT`, `LIVERY` and `INSTRUMENTS` → Aircraft (`INSTRUMENTS` is new in this revision; plan 2 made it Other), everything else → Other. Exceptions: a `SCENERY` Package with no BGL files at all is Other (on Rickard's machine `fsdreamteam-gsx-pro`, the External entries `fsdreamteam-gsx-world-of-jetways` and `orbx-volanta-aerocaches-jw`, and eight Library Packages: `yara-cj-aerial`, `ywbl-cj-aerial`, `yhsm-txt-cj`, `yhsm-txt-e-cj`, `tsr-windy-things`, `tsr-windy-things-AUS`, `neofly-neofly-objects`, `neofly-neofly-smokes`; the first four are photo textures for an Airport, and typing the code on their card moves them to Scenery → Airports, 6.5); a Package where any `aircraft.cfg` declares `isAirTraffic=1` (for example `fsltl-traffic-base`, 2,551 such files, and `apronlife-jetway-helper`) is Other and isn't parsed for Liveries. An `AIRCRAFT` Package that belongs to no aircraft (6.6), such as `orbx-volanta-aerocaches`, stays in Aircraft on its Other tab (this replaces plan 2's rule that made it Other). An Addon's Category is the Category of its first Package (by name) unless the user moved it. This holds for Airport Addons too: a `MISC` Package with an airport record gives an Airport card in Other, and its map marker opens it there (this applies Rickard's 2026-10-28 rule that the app follows the manifest's type; the user can move the card).
- **Visibility is decided per Addon:** an Addon's card is shown in its menu section when the user moved it there, or when at least one of its Packages lies in a Library that carries that menu section's type, or when it has a Fixed or External Package, or when it is a computed card (built-in content or a GSX profile, 6.5). The dialog of a shown card always lists all its Packages, and they all count for its state. An Addon that fails the test is not shown; its Packages are listed under Settings → **Not shown** ("3 packages in Libraries not registered for their type"), with a shortcut to add the type to their Library and a "Move to Scenery / Aircraft / Other" action that records a user move (so `yara-cj-aerial` can be brought to Scenery without adding Other to the whole Library). Setting an Airport code on a card counts as a user move to Scenery.
- **Initial tab** (computed until the user moves the Addon):
  - Scenery: **Airports** when the Addon is anchored to an Airport, has an Airport code, is a "(several)" Addon, or is a detached card whose Package is linked to an Airport (6.5); otherwise **Assets** when it is an Asset pack; otherwise **Landscapes**.
  - **Asset pack test (2026-10-28):** a Scenery Addon whose Packages have no BGL section with placed objects (`0x25`) or terrain (`0x65`): only model data (`0x2B` ModelData, `0x30`) or other non-placing sections. A Package with no BGL at all, or with only unreadable BGLs (a card the user moved to Scenery, such as photo textures, or encrypted scenery), starts in Landscapes. On Rickard's machine this gives `esd-modellib-eolib`, `library1v14-cbj`, `library2v14-cbj`, `mikeaat-assetpack` and `us-firedeptpack`, plus three External entries that start there locked: `navigraph-navdata`, `navigraph-navdata-base` and `sayintentions-fly-in-library`. Rickard, 2026-10-28: the app follows the manifest's `SCENERY`; the user drags them to Other if they want. `human-library-animated` places objects and starts in Landscapes; the user drags it to Assets.
  - Aircraft: **Instruments** when the `content_type` of the Addon's first Package (by name) is `INSTRUMENTS` (`pms50-instrument-gtn750-pro`, `sky4sim-os`); **Aircraft** when it is part of an aircraft Addon (6.6); otherwise **Other**. (`navigraph-avionics-*` ship navigation data as `MISC` and belong to the Other menu section by content.)
- **Tags:** the folders between the Library root and a Package become the Package's tags, by these rules:
  - A Library root is not a tag. With nested Libraries, tags are taken below the innermost one.
  - A folder named `Community`, or named like a menu section (`Scenery`, `Aircraft`, `Aircrafts`, `Other`), gives no tag.
  - A folder that holds a single Package (directly or below) gives no tag.
  - Tool layouts collapse to the product: `Orbx Library\msfs\<product>\Community\<pkg>` gives no `Community` tag (rule above) and no product tag when the product holds one Package (single-Package rule).
  - An Addon's tags are the union of its Packages' tags.

  Tags are shown on the card's dialog. Filtering and editing are Later (17).
- **Built-in content is shown only when the user has an add-on for it** (2026-10-28): a Package that belongs to the same Airport (Replaces built-in), a Livery, Mod or attached Package (6.6) of the same aircraft that is not Sim-managed (a Library Package, or a Fixed or External entry), or a GSX profile for the Airport. Sim-managed liveries and parts (127 `asobo-aircraft-*-livery-*` packages on Rickard's machine) never make a built-in aircraft visible; they are shown locked inside a card that exists for another reason. The built-in part is shown locked in the dialog. Otherwise built-in Airports appear only on the map, and built-in aircraft not at all.

### 6.5 Addons and Airports

- **An Addon is shown as a card.** Every Package belongs to exactly one Addon.
- **Anchor:** an Addon's anchor is an ordered list of Airport codes or aircraft containers. For an Airport Addon the first code is its **primary code**, shown on the card; the others are listed in the dialog. "An Addon has code X" below always means X is in its anchor. **A code or container is in at most one anchor**; every rule below keeps it that way, so "the Addon that has X" is always unique.
- **Name and image** (computed unless overridden): an Airport Addon is named from its primary code and that Airport's name ("EHAM Schiphol"); an aircraft Addon takes the display name of its first providing Package by name, else the display name of its locked base, else the container name; every other Addon takes the display name of its first Package by name. The image is the thumbnail of the first Package by name that has one.
- **Computed parts and cards:** built-in content (Sim-managed packages) and GSX profiles are never Packages of an Addon. On every analysis each one is attached to the Addon whose anchor holds its Airport code or container; else to a shown card that lists the code (a "(several)" or detached card), where it appears in the dialog; else a GSX profile gives a **computed card** anchored to its code: a built-in Airport card when a Sim-managed package belongs to that Airport, otherwise a profile-only card (8). Computed cards are not stored; one disappears when the content that made it does.
- **Initial Addons (first scan, and for new Packages later):**
  - All Packages belonging to the same Airport form **one Addon per Airport**, named from the Airport (for example "EHAM Schiphol" with `brickbybrickdesign-airport-eham-amsterdam`, `duijffmedia-airport-eham-amsterdam`, `duijffmedia-schiphol-amsterdam-mega-enhancement-lighting`). The Addon is **anchored** to that Airport.
  - **A Package that belongs to several Airports** (counted distinct codes, not records: `tag21-airport-ybas-alice-springs` has two YBAS records and is a plain YBAS Package): if exactly one of its codes appears as a separate token in its folder name or manifest title (the token test of rule 2, stop-list and the 3-character rule included, but applied even though the Package has counted records), it joins that Airport's Addon and the other codes are listed in the dialog as **linked codes**, not anchor entries: they get map markers, but a later Package for one of them doesn't join this card, and they have no remove action (`sltcreations-airport-yq05-mount-mulligan` → YQ05, with YMUZ "Mount Mulligan Lodge" listed). Otherwise it gets **its own Addon with no anchor and no Airport code**, shown as "(several)" on the card and starting on Airports (`asf-mission-demo` with FEFA and FEGK); the dialog lists every code and the map shows the Package at each. Typing a code on it makes that code the anchor and primary code; the other codes stay listed.
  - Aircraft Packages form one Addon per aircraft, anchored to its container (6.6).
  - Every other Package is its own Addon.
  - **New Packages later** join the Addon whose anchor holds their Airport code or container if one exists, otherwise they become a new Addon, anchored by the initial rules. Packages first seen in the same analysis are grouped among themselves by the initial rules (a new airport shipped as scenery and lights gives one card) before they are joined to existing anchors. If the first content pass is interrupted, initial grouping stays pending across restarts until it completes. A new Package with several codes first goes through the token test above; if that names none of the held codes, it becomes a "(several)" card. They never rearrange existing Addons.
  - **When:** initial Addons are created only from complete facts: on first run after the content pass has finished for Community, Libraries and Sim-managed packages; later, for a new Package once its own facts are read. Until then the card wall shows provisional cards, one per Package, that are not stored; each sits in the menu section of its discovery Category (manifest `content_type`) on Scenery → Landscapes, Aircraft → Other, or the Other section; provisional cards follow the visibility rule of 6.4. A provisional card offers on/off only: drag and drop and the dialog's edits are disabled until the initial Addons exist, and the banner says "Grouping cards…" with the scan progress.
- **User changes (all saved immediately, no save button):**
  - **Merge:** drag a card onto another card. The target takes over the dragged Addon's Packages and adds the dragged anchor to its own; the target's name, image, primary code, menu section and tab stay. Any two cards can be merged, including two Airport cards, a card with only built-in or locked parts, and a computed card (which adds only its code). Merging an Airport card with an aircraft card is allowed: the anchor then holds both containers and codes. An **aircraft Addon** is one whose first anchor entry is a container; its codes are secondary codes with map markers, and it offers no code field. Dropping a card onto a computed card first makes the computed card a stored Addon anchored to its code. A merge records the target's current name, image, menu section and tab as user overrides, so they don't change afterwards. After merging "EHRD Rotterdam" into "EHAM Schiphol", a later EHRD Package joins the merged card. Cards on different tabs are merged by first moving one of them to the other's tab, or by typing the code.
  - **Move:** drag a card onto a tab header (same menu section) or onto a menu section in the left menu. A card moved to another menu section lands on the tab the initial-tab rules give there.
  - **Split:** "Move out" on a Package row in the dialog gives that Package its own Addon with no anchor, marked **detached**: no automatic rule ever joins it to another Addon again, and new Packages never join it. Its card and code field show the first Airport code its Package is linked to, without making it an anchor, and the map places it at each of its Package's codes, like a "(several)" card. The mark is cleared when the user merges the card or types a code on it (the typed code then follows the code-field rule). "Move out" is not offered on an Addon's only Package. A Livery moved out of an aircraft card lands on Aircraft → Other, since it is no longer part of an aircraft Addon. (Without the mark, the initial rules would put it straight back where it came from.)
  - **Edit:** name, image (the thumbnail of one of the Addon's Packages, or a file the user picks; only its path is stored, the file is never copied or moved) and **Airport code** in the dialog.
  - **Airport code field** (edits the primary code; offered on every card except aircraft Addons, which are anchored to containers. An **Airport Addon** is one whose first anchor entry is an Airport code): any 3–5 letters or digits are accepted (C3 of plan 2). Known codes are suggested with their names while typing. Changing the field replaces the primary code: the old code is removed as by clearing (below), then the new one is added; merging is the way to hold two codes. Setting a code links the Addon's Packages to that Airport manually (rule 3 below), puts it first in the anchor as primary code, moves the card to Scenery → Airports from any menu section (unless the user moved it after setting the code) and, when another Addon already has that code, merges this card into it. A code no record knows gives a card with that code and the user's name, and no map marker. Clearing the field removes that code from the anchor and the Addon's links to it, automatic ones included; the next code in the anchor becomes primary, or the card has no code.
  - **Secondary codes** listed in the dialog each have a remove action with the same effect as clearing the field for that code. This is how a mistaken merge is taken apart: remove the code, "Move out" its Packages, and type the code on the detached card to get a fresh anchored card.
  - Anchors survive rename and merge.
- A Package belongs to an Airport if:
  1. one of its BGL files contains an airport record with that code, unless the Package is excluded (below), or
  2. rule 1 found no counted airport record in it, it has Category Scenery (so a `SCENERY` Package with no BGL doesn't qualify), isn't air traffic and isn't Sim-managed, and its folder name or manifest title contains, as a separate token, a code that some airport record knows (records of Packages excluded by pattern don't count; records excluded only by the threshold do), a token that is not on a stop-list of common words (`PACK`, `SAVE`, `SWIM`, `SANS` and similar). 4–5 character tokens may sit anywhere (`…-esgj`, `EHAM …`); 3-character tokens only count as the leading token (`u60-u60`, `c53-…`), because about 3,000 generic idents are 3 characters long, or
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
  - When a Sim-managed, Fixed or External Package also provides the container, it is shown as the Addon's locked base, and the Library Packages that provide the same container are labelled **mods** (`mge-asobo-aircraft-c152` provides `Asobo_C152`, like the official C152). A built-in aircraft with no Livery, Mod or attached Package gets no card (6.4).
  - A Package that provides several containers (`aerosoft-crj` provides `Aerosoft_CRJ_550`, `_700`, `_900` and `_1000`) gives one Addon anchored to all of them, the first by name as primary; Liveries and attached Packages for any of them join that Addon.
  - `INSTRUMENTS` Packages never take part in Livery or attachment matching; each is its own Addon on the Instruments tab.
  - A Package without `aircraft.cfg` (`flybywire_A320Neo_Cabin`, `simflight-C152-interior-navtablet`) is matched, when its `content_type` is `AIRCRAFT` or `LIVERY`, by its `SimObjects\Airplanes\<folder>` names first, then by `dependencies` naming a known aircraft Package, otherwise it is its own Addon on the Aircraft → Other tab.

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

1. Locate `UserCfg.opt` (Steam: `%APPDATA%\Microsoft Flight Simulator\`; Microsoft Store: under `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_…\LocalCache\`), read `InstalledPackagesPath`, derive Community and Official. If not found, ask the user to pick the Packages root (the folder holding Community and Official), so Sim-managed packages are still read.
2. Classify Community and collect the targets of all existing links.
3. **The user adds Libraries (2026-10-28).** The first-run screen starts with an empty list and "Add Library…": the user picks a folder and its types (Scenery, Aircraft, Other). No Libraries are proposed. The existing links are used only to show coverage ("87 of 95 links covered", a total), so a forgotten folder is noticed. A counted junction is covered when its target lies under a registered Library. Counted are junctions whose target exists and holds a `manifest.json`, except targets under `…\FSDT Addon Manager\MSFS\` (FSDT's Addon Manager owns its junctions, and those entries stay External). Symlinks (made by installers such as SayIntentions) and Broken links are not counted; they are listed separately as "not counted (made by other tools)".
4. The screen explains that Packages in folders the user doesn't add stay External and are shown locked, and that a folder another tool also links from (such as Orbx Central's download folder) can be added and switched from both.
5. Adopt the current Community state: each Package is on if linked, off if not. No sync dialog on first run, with one exception: Stale entries (a link whose target is gone but whose Package is found in a Library) count as on and are offered for re-point in the sync dialog right after adoption (6.1, Moved Packages); on first run its rows are listed per Package, since no Addons exist yet. The same holds when a Library is registered later.
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
- Community, all Libraries and the GSX profile folder are watched for changes; affected Packages are rescanned in the background. Community is watched non-recursively; changes inside linked Packages are seen through the Library watchers. Watchers overflow during large installs (`C:\Sim\Addons` holds about 123,000 files), so an overflow triggers an incremental rescan.
- From Sim-managed packages, the app reads Scenery and aircraft containers. Their aircraft are never toggleable.

### 7.4 Reconciliation

- The app stores the **last accepted Community snapshot**: every entry with its resolved target. Reconciling compares that snapshot with the current Community, not with the user's choices.
- Changes to External, Fixed and Ignored entries are recorded silently and never open the dialog.
- If other differences exist, a sync dialog lists them, one row per Package, grouped by Addon. Each row chooses "keep Community" (preselected) or "restore app state"; one choice can be applied to all rows.
- "Keep Community" sets the Package's stored choice to match Community. "Restore app state" changes the link through the apply path (6.8). Either way, the snapshot is updated.
- If Community changes while the app runs and the change didn't come from the app (for example Addons Linker, which may run alongside), the user is told and offered the same sync dialog, with a way to not be asked again for the rest of the session. The app never silently overwrites such a change.

## 8. GSX

- Profiles are read from `%APPDATA%\Virtuali\GSX\MSFS`.
- **Airport code from the file name:** the leading run of 3–4 letters or digits, followed by `-`, `_`, `.` or the end of the name (`epgd_LukSid.ini` → EPGD, `LPMA.ini` → LPMA). 5-character idents (`YALR1`) are not matched by file name. A 4-character code no record knows still gives a profile-only card, without a map marker, like a typed unknown code (6.5). A file that yields no code, or an unknown 3-character code (`gsx.ini`), gives no card and is listed in Settings as "GSX files not recognised". A `.py` alone never creates a card.
- `.off` files are disabled profiles: shown as disabled, not counted.
- `.py` files belong to the Airport by code, not by base name (`essa-orbx.py` belongs with `essa-bwib1r-noolvdgs.ini`). A `.py` doesn't need an `.ini` with the same name. When an Airport has two profiles, its `.py` files are listed with the Airport, not paired with one profile.
- Status per Airport: **Installed** (at least one active `.ini` for the code; `.py` files alone don't count), **Missing**, **Two profiles** (two or more active `.ini` profiles in the GSX folder for one code; real cases today: LDSP, LFMN, LFPG). On a card with several codes the mark reflects the primary code (the first listed code for "(several)" and detached cards); the dialog shows every code.
- **Where it is shown (2026-10-28):** as a mark on the Airport card (✓ / missing / two) and in full in the card's dialog. There is no GSX table. A GSX profile is an add-on: a profile for a built-in Airport gives that Airport a computed card (grey light with a lock), and otherwise a profile attaches by the rule in 6.5 ("Computed parts and cards"), which also gives the **profile-only** card.
- **Profiles shipped with scenery:** some Packages carry a profile in a subfolder (`GSX\`, `GSX-PROFILE\`, `GSX-Profile-EPKK\`). They are recognised by `<code>….ini`/`.py` file names, not by the folder name (`aerosoft-crj\Data\GSX\` holds aircraft `gsx.cfg` files, not profiles). They are shown in the dialog as "shipped with scenery, not installed" with an action to copy the profile into the GSX folder (it never overwrites; when a profile for the code is already installed, the action says that copying gives Two profiles and asks first), and they don't count for Two profiles. GSX's own log (`Couatl.err`) has only ever shown it loading profiles from its own folder, which supports this; see 15.
- Missing profiles get a deep link to flightsim.to, filtered to GSX profiles for the code.

## 9. Finding scenery

For any Airport, the app offers deep links to search flightsim.to and SimMarket for its code. The links are URL templates in Settings, so more stores can be added by the user.

## 10. Screens

Layout and feel follow BOB Dispatcher: dark, calm, airy, clear status colours.

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
- **Card wall:** large cards with the image on top (the image of the first Package that has one, the user's choice, or a placeholder per type). An Airport card shows its code in bold and the Airport name below; other cards show the Addon name. Below: package count, GSX mark (Airports) and warnings. Cards are ordered by Airport code on Airports (detached cards by the code they show; cards without a code after them, by name) and by name elsewhere. **No switch on the wall.** A traffic light in the top right corner shows state: red Off, yellow Mixed, green On, grey with a lock when Locked. A Pending card also shows "Next sim start". Missing Packages, "(several)" and built-in parts are labelled.
- **Card dialog** (opens on click; Esc, ✕ or a click outside closes it): editable name, image and Airport code (saved immediately); one control to switch all switchable Packages; the Packages, each with display name, folder name, path, version, Library, Duplicate copy choice and its own switch, plus "Move out"; locked and built-in parts dimmed; Airport codes, other cards at the same Airport, tags, Replaces built-in, GSX profiles (installed, shipped, missing with a search link) and deep links to find scenery.
- **Map** (Scenery → Airports, view switch `Cards | Map`): Airports with real scenery as markers coloured like the card's light (green On, yellow Mixed, red Off, grey Locked or built-in). A card with several codes (merged, "(several)" or detached) has a marker at each known code, all opening the same dialog. There is one marker per code: when several cards claim a code, the marker opens the anchored card, whose dialog lists the others; without an anchored card, the first listing card by name decides colour and dialog. A marker whose cards are all Not shown is grey. A computed card for a known code without real scenery gets a grey marker; a code no record knows gets none. "Show all airports" adds the generic ones, clustered when zoomed out. Airport search. Selecting an Airport opens its card's dialog, or shows code, name and search links when it has no card. When its cards are all Not shown, it also says "not shown — add Scenery to <Library>".
- **Settings:** Community path (detected, overridable); Libraries (add/remove, types, link coverage); **Not shown** (6.4); **Community** (every entry with its classification, link target and warnings; remove Broken links with confirmation, disabled while the sim runs with the reason shown); search URL templates; airport threshold, exclusion list and token stop-list; version and update notice.
- **Banner** at the top of the window: Broken and Stray entries in Community (with a link to Settings → Community), pending changes while the sim runs, scan progress.
- **Overlays:** first-run setup (7.1) and the sync dialog (7.4).

Thumbnails come from the Package's own files (`ContentInfo\<any>\Thumbnail.jpg`; the subfolder name differs from the Package name), with a placeholder per type when none exists.

## 11. Storage

- **User data** (`%APPDATA%\<app>\userdata.json`), JSON, small and human-readable, saved on every change (write to a temporary file, keep a `.bak`, then replace): Libraries with their types; each Package's on/off choice, active copy, last known path and the facts its Addon needs while it is missing (Category, tab, display name, thumbnail path, Airport links; 6.1); Addons (their Packages, anchor, detached mark, and the user's overrides: name, image path, menu section, tab; a typed Airport code is stored as the anchor's primary code plus manual links); manual and removed Airport links; opted-in Packages; pending set; search URL templates; threshold, exclusion list and stop-list; the last accepted Community snapshot.
- **What is stored versus computed:** an Addon's Packages, anchor and detached mark are stored when it is created and change only by user action, with one automatic exception: a rescan may set the anchor of an untouched card (one the user has not moved, merged, renamed or given a code, not detached, not "(several)") whose anchor is **empty** and whose Packages are linked to **exactly one** distinct code that no anchor holds. The card then moves to the Airports tab if it is in Scenery; its menu section doesn't change. A card that already has an anchor, or whose Packages link to several codes, is never changed, and a rescan never removes a code. When another Addon already has the code, the card stays as it is and the user can merge it. Its name, image, Category, tab, tags and state are computed on every analysis unless the user overrode them. Built-in parts, GSX profiles and computed cards (6.5) are never stored.
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

- Core rules are unit tested without touching disk: on/off per Addon and per Package, computed state (On/Off/Mixed/Locked), "switch all" skipping locked Packages, visibility and Not shown, initial Addons and tabs, the Asset pack test, tags, "(several)", merge/move/split and the Airport code field, Airport linking (threshold, exclusions, stop-list, encrypted official airports), Livery and mod rules, built-in cards, warnings, pending set, reconciliation diff.
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
- An Addon's Category is that of its first Package by name unless moved.
- A Package moved out of an Addon is detached, so automation never puts it back; the card it left keeps its anchor.
- An anchor is a set of codes or containers, and a merge unions them, so one Airport keeps one card after merges.
- Built-in parts and GSX profiles are computed on every analysis, never stored as Addon members.
- Visibility is decided per Addon, not per Package.
- A GSX profile whose code no record knows gives a profile-only card without a map marker.
- A code is in at most one anchor; a rescan only anchors an untouched card with an empty anchor and exactly one unheld code.
- A Package providing several containers anchors one Addon to all of them, so multi-variant aircraft keep one card.
- Typing a code replaces the primary code; secondary codes have a remove action, which is how a mistaken merge is taken apart until Undo (17).
- Initial Addons wait for the complete content pass; provisional per-Package cards are shown until then.
- Stale links found at adoption are offered for re-point straight away.
- A moved Scenery Package with no BGL starts in Landscapes, not Assets.
- A Library Package whose name an External or Fixed entry holds is one Package, with the entry as its locked active copy.
- Provisional cards offer on/off only, so no arrangement is lost when the initial Addons arrive.
- A merge pins the target's name and image, not only its section and tab.
- The Airport code field is offered on every card except aircraft Addons.
- Unregistering a Library hides its unlinked Packages instead of leaving missing cards; re-registering revives them.
- A GSX file with no code or an unknown 3-character code gives no card.
- A Missing Package counts as lying in its last known Library, so its card doesn't vanish.
- GSX profiles attach by one rule: anchor, else a shown card listing the code, else a computed card.
- Managed elsewhere is dropped from the MVP (Rickard, 2026-10-28): every registered Package can be switched, including Packages in a folder another tool also links from (Orbx Central); unregistered folders stay External.
- A Library registered after first run adopts Community state without a sync dialog.
- Not shown Packages can be moved to a menu section from Settings; typing an Airport code counts as a move to Scenery.
- Folders named `Community` or like a menu section give no tag.
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
- **Managed elsewhere** (a lock on Packages whose links another tool recreates), if a real need appears.
