# Modern MSFS Addon Manager — MVP design

Date: 2026-10-25, revised 2026-10-28 after the UI design session (menus, tabs, cards, tags) and 2026-10-29 for the card model (cards and Packages, Libraries per menu section, Not found, one SQLite file). Status: draft, revised after a builder check and two independent reviews; the card model went through five review rounds on 2026-10-29.

Vocabulary is defined in [GLOSSARY.md](../../../GLOSSARY.md) and used strictly below. Background and the full decision log are in [ANALYS.md](../../../ANALYS.md) (Swedish). Architectural decisions are in [docs/adr](../../adr).

## 1. Intent

Rickard manages a large MSFS 2020 setup (118 Community entries, about 250 Library Packages in four Libraries) with MSFS Addons Linker. He likes two things about it: turning content on and off quickly, and a map of installed airports plus the airports built into the sim. He dislikes its ugly, clunky interface, and misses a link to GSX Pro profiles and a way to find scenery he doesn't have.

This app replaces Addons Linker for that workflow. Scenery is the focus, aircraft come second, everything else is "Other" with plain on/off. It must start fast, never put the user's files at risk, and look as calm and clear as BOB Dispatcher (screenshots in `C:\Users\ricka\OneDrive\Bilder\bob\`).

**Guiding principle (2026-10-28):** the automation proposes, the user decides. Every automatic grouping, placement and Airport link can be overridden on the card, and an override always wins.

## 2. Goals

1. Turn content on and off per card or per single Package by linking Packages into Community with directory junctions.
2. Show the user's content as cards under Scenery, Aircraft and Other, each with its own tabs and its own Libraries.
3. Group the Packages of one Airport or one aircraft into one card automatically, and let the user merge and move cards by drag and drop and split them with Move out.
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
- **C9.** Package names, container names and ICAO codes are compared case-insensitively everywhere.
- **C10.** Only one instance of the app runs at a time (a named mutex). A second start brings the running window to the front and exits.

## 6. Domain rules

### 6.1 Packages and Libraries

- A Package is found by recursively walking a Library. A folder containing `manifest.json` is a Package; the walk does not descend into it. A GSX profile is also a Package (8).
- Package identity is its folder name (the name the sim uses). The junction in Community gets the same name.
- **Name held by a Community entry:** when a Fixed or External entry holds a Library Package's name (on Rickard's machine `neofly-neofly-cargo`, `-objects`, `-smokes` and `bagolu-c152-neoflybannertow` link into the unregistered `C:\Sim\Neofly\addons`), it is one Package whose active copy is the Community entry, locked. The Library copy is listed as an inactive copy ("held by an External entry") and can't be made active while the entry exists.
- A Package's display name is its manifest `title`, or its folder name when the title is empty. The folder name is shown as secondary text, because titles repeat (five Packages are titled "Simple Scenery" on Rickard's machine).
- **Libraries per menu section (2026-10-29):** each menu section (Scenery, Aircraft, Other) has its own Libraries: root folders the user registers for that section. A Library belongs to a menu section; it has no types of its own. The same folder may be registered for several sections (a user who keeps everything in `C:\Sim\Addons` registers it for all three). Every registered folder is always scanned completely; the sections it is registered for only decide whether its Packages get cards automatically (6.4).
- **Nested Libraries** (`C:\Sim\Addons` for Other and `C:\Sim\Addons\Scenery` for Scenery) are allowed. A Package inside several Libraries is found once. Content decides a Package's menu section, so a Scenery Package is never shown under Other because of the outer Library (Rickard, 2026-10-28: "paket av typen Scenery ska aldrig dyka upp (automatiskt) under Other, och paket av andra typer än Scenery ska inte synas under Scenery"). The Library shown in the dialog is the innermost one.
- The same name more than once across the Libraries (in two Libraries, or at two depths in one) is a **Duplicate copy**. One copy is the active copy: by default the one Community already points to, otherwise the first by the order its folder was first registered (for any menu section), then path; the user can change it in the card's dialog. Only the active copy can be linked. Changing the active copy of a linked Package re-points the link. A Package lies in a Library registered for its menu section when any of its copies does; its ICAO codes come from its active copy, and its menu section stays as first fixed (6.4). When the active copy's folder disappears while another copy exists, the default rule picks a new active copy (a link to the vanished copy is Stale, 6.2).
- **Registering a Library later** (Settings) adopts the Community state of its Packages like first run: on if linked, off if not, without a sync dialog except for Stale entries (7.1, step 5) and Packages linked to another copy than their stored active copy, which are offered as a re-point row. Their cards follow 6.5: the Packages it makes eligible are grouped among themselves first, like Packages first seen in one scan. Adoption sets the on/off choice from Community; all other user data of Packages that were known before (card, active copy, links) is kept.
- **Removing a Library** from a section, or altogether, never deletes anything, and its Packages stay on their cards with all their user data. Only when a folder is removed from every section do its Managed entries become External entries. A Package on a card that no registered Library holds any more is locked: linked ones are External, unlinked ones are off, and the dialog says "not in a registered Library" until the folder is registered again. Such a Package whose last known path no longer exists is Not found. A warning on those cards is Later (17).
- **Not found (2026-10-29):** a Package whose Library answers but whose folder is gone stays on its card with all its user data (choice, card, active copy, links and the display facts its card needs, 11) and is shown as **Not found**, a warning (6.7). The card stays where it is. This covers an uninstall and an update that renames the folder: after "OrbX EHAM 1.3" → "OrbX EHAM 1.5", the new Package joins the EHAM card by its ICAO code (6.5) and the old one is Not found on the same card. The user decides what is forgotten: only "Remove" in the card's dialog forgets a Not found Package (6.5). When a Package with the same name reappears, it is found again in its old place. A Fixed Package whose folder is gone, and an External Package whose link target is gone, are Not found too, since the app can't tell an unplugged drive outside the Libraries from a deleted folder. An External Package whose link was removed while its last known target still exists was turned off outside the app (Addons Linker, Orbx Central): it stays on its card, off and locked, with no warning. When a registered Library holds a copy of such a Package (6.1, Name held by a Community entry), that copy becomes its active copy instead, off and switchable again.
- **Links of Not found Packages:** a Community link whose target is a Not found Package is Broken (6.2) and shown in the banner. The startup self-check (6.8) skips Not found Packages, and a sync row for one offers only "keep Community" (7.4). Removing its Broken link doesn't change its stored choice.
- **Library not found:** when a Library's root doesn't answer at all (an unplugged drive, a network share that is down), nothing changes: its Packages keep their cards, choices and links, their switches are disabled with the reason shown, and the banner says "Library not found: E:\Addons". A read error during a scan, inside a Package or in any folder below a Library root that answers, makes the affected Packages unreadable (shown in the banner), never Not found. An unreadable Package keeps its card if it has one; one without a card stays provisional, and its menu section is fixed and its card made only by a scan that reads it without errors. A BGL that is encrypted and can't be decoded is not a read error.
- **Moved Packages:** a Broken link whose name matches a Package found in a registered Library is a stale Managed entry, not a plain Broken one. The sync dialog offers to re-point it to the new location (preselected) or to turn the Package off, which removes the stale link; both go through the apply path, and the row doesn't come back.

### 6.2 Community entries

Every entry in Community is classified on each reconcile:

| Entry | Rule | App may |
|---|---|---|
| Managed | Junction or symlink whose target is a Package in a registered Library, and whose name is that Package's name (a link to a Library Package under another name is External) | Remove; the app only ever creates junctions |
| Offline | Link whose target lies under a Library that doesn't answer (6.1, Library not found), or in a folder a scan could not read | Shown as offline, never offered for removal |
| External | Junction or symlink pointing outside every registered Library, or to a Library Package under another name | Show only |
| Fixed | Real folder with `manifest.json` | Show only |
| Ignored | Real folder without `manifest.json` (for example `flybywire-current-install-0`), or a link whose target isn't a Package. The sim ignores it | Show as neutral info |
| Broken | Link whose target doesn't exist and whose name matches no Package found in a Library (a Not found Package's link is Broken, 6.1) | Remove the link, after confirmation |
| Stale | Link whose target doesn't exist and whose name matches a Package found in a Library (6.1, Moved Packages) | Re-point or remove, via the sync dialog |
| Stray | Loose file | Show only, with a warning |

- Link targets are compared as resolved, normalised paths, never by entry name: strip the `\\?\` prefix, ignore a trailing backslash (Addons Linker adds one, Neofly doesn't), and resolve relative symlink targets against Community.
- Removing a link must remove only the link itself, never the target's contents.
- **Name taken:** the app never creates a link where any Community entry with that name already exists. When the holder is a Fixed or External entry, the Package is locked with that entry as its active copy (6.1, Name held by a Community entry). When the holder is not a Package (an Ignored or Stray entry), the Package can't be turned on, counts as off, and the app says which entry holds the name.

### 6.3 State and on/off

- **Only Packages have an on/off state.** It is the user's choice, stored by the app. A Package should be linked exactly when it is on.
- **Cards have no state of their own.** A card's state is computed from the Packages that count: **On** when all are on, **Off** when all are off, **Mixed** otherwise.
  - They count: Packages that lie in a registered Library, including Not found Packages and Packages in a Library not found (with their stored choice; the card says so), and a Package whose name an Ignored or Stray entry holds (as off, 6.2).
  - They don't count: Fixed and External Packages (including a Library Package whose name such an entry holds), GSX profiles, Packages that no registered Library holds any more (6.1) and built-in parts.
  - A card with no Package that counts has no state (it is **Locked**).
- On/off is applied in the card's dialog, never on the card wall: one control switches every switchable Package of the card, and each Package has its own switch.
- **Every Package on a card that lies in a registered Library can be switched** (2026-10-28: Managed elsewhere was dropped), unless a Fixed or External entry holds its name (6.1). A folder that another tool also links from can be registered and switched from both: Orbx Central downloads into `C:\Sim\Orbx` and makes its own junctions, and the app treats them as Managed entries; when Orbx Central changes a link, reconciliation picks it up (7.4). A folder the user doesn't register stays External, shown locked on the right card. "Switch all" skips locked Packages and says so. The switches of Not found Packages and of Packages in a Library not found are disabled, with the reason shown. A Package whose name an Ignored or Stray entry holds is skipped by "switch all" like a locked Package, and the dialog names the holder.
- **Name taken** (6.2) rejects a turn-on at apply time; the choice isn't stored.
- Fixed and External entries are locked Packages, shown with a lock. They join cards like any other Package (an External EHAM Package joins the EHAM card); a card made only of locked Packages is Locked.
- A GSX profile Package is locked: switching profiles on and off is Later (17).

### 6.4 Menus, tabs and visibility

- The app has three menu sections, **Scenery**, **Aircraft** and **Other**, each with its own tabs:

  ```
  Scenery                         Aircraft                         Other
  Airports | Landscapes | Assets  Aircraft | Instruments | Other   (no tabs)
  ```

- **A Package's menu section (its Category) comes from content:** manifest `content_type` `SCENERY` → Scenery, `AIRCRAFT`, `LIVERY` and `INSTRUMENTS` → Aircraft, everything else → Other; a GSX profile is Scenery. Exceptions: a `SCENERY` Package with no BGL files at all is Other (on Rickard's machine `fsdreamteam-gsx-pro`, the External entries `fsdreamteam-gsx-world-of-jetways` and `orbx-volanta-aerocaches-jw`, and eight Library Packages: `yara-cj-aerial`, `ywbl-cj-aerial`, `yhsm-txt-cj`, `yhsm-txt-e-cj`, `tsr-windy-things`, `tsr-windy-things-AUS`, `neofly-neofly-objects`, `neofly-neofly-smokes`; the first four are photo textures for an Airport: "Move to Scenery" in Not shown gives them a card there, and typing the ICAO code on it moves the card to Airports, 6.5); a Package where any `aircraft.cfg` declares `isAirTraffic=1` (for example `fsltl-traffic-base`, 2,551 such files, and `apronlife-jetway-helper`) is Other and isn't parsed for Liveries. An `AIRCRAFT` Package that belongs to no aircraft (6.6), such as `orbx-volanta-aerocaches`, stays in Aircraft on its Other tab.
- **A Package's menu section is fixed by a completed scan (2026-10-29):** it is set when a completed content scan has read the Package and never changes after that. Moving a card changes the card's menu section, not its Packages'.
- **Which Packages get cards automatically:** a Package gets a card (6.5) when it lies in a Library registered for its menu section, or when it is a Fixed or External entry or a GSX profile. Any other Package gets no card: it is listed under Settings → **Not shown** ("3 packages in Libraries not registered for their section"), with a shortcut to register its Library for that section and a "Move to Scenery / Aircraft / Other" action that gives it its own card in that section, with the ICAO codes and tab a new card would get (6.4, 6.5) (so `yara-cj-aerial` can be brought to Scenery without registering `C:\Sim\Addons` for Other). A Not shown Package can't be switched until it has a card; its on/off follows Community, and in the sync dialog its rows are grouped under "Not shown" and offer only "keep Community". A provisional or Not shown Package that disappears is forgotten: it has no card and no user changes to keep.
- **An existing card is always shown** in its menu section, wherever its Packages lie. Its dialog lists all its Packages, and they count for its state by the rules of 6.3.
- **Tab** (stored on the card; set by the rules below when the card is made and when it is moved to another menu section, otherwise changed only by the user, by dragging the card onto a tab header or by adding an ICAO code, 6.5):
  - Scenery: **Airports** when the card has an ICAO code or is a "(several)" card (6.5); otherwise **Assets** when it is an Asset pack; otherwise **Landscapes**. The user may also put a card without an ICAO code on Airports.
  - **Asset pack test (2026-10-28):** a Scenery card whose Packages have no BGL section with placed objects (`0x25`) or terrain (`0x65`): only model data (`0x2B` ModelData, `0x30`) or other non-placing sections. A card with a Package that has no BGL at all, or any encrypted BGL that can't be decoded (a card the user moved to Scenery, such as photo textures, or encrypted scenery), starts in Landscapes. On Rickard's machine this gives `esd-modellib-eolib`, `library1v14-cbj`, `library2v14-cbj`, `mikeaat-assetpack` and `us-firedeptpack`, plus three Community entries (Fixed or External) that start there locked: `navigraph-navdata`, `navigraph-navdata-base` and `sayintentions-fly-in-library`. Rickard, 2026-10-28: the app follows the manifest's `SCENERY`; the user drags them to Other if they want. `human-library-animated` places objects and starts in Landscapes; the user drags it to Assets.
  - Aircraft: **Instruments** when the `content_type` of the card's first Package (by name) is `INSTRUMENTS` (`pms50-instrument-gtn750-pro`, `sky4sim-os`); **Aircraft** when the card holds an aircraft (6.6); otherwise **Other**. (`navigraph-avionics-*` ship navigation data as `MISC` and belong to the Other menu section by content.)
- **Tags:** the folders between the Library root and a Package become the Package's tags, by these rules:
  - A Library root is not a tag. With nested Libraries, tags are taken below the innermost one.
  - A folder named `Community`, or named like a menu section (`Scenery`, `Aircraft`, `Aircrafts`, `Other`), gives no tag.
  - A folder that holds a single Package (directly or below) gives no tag.
  - Tool layouts collapse to the product: `Orbx Library\msfs\<product>\Community\<pkg>` gives no `Community` tag (rule above) and no product tag when the product holds one Package (single-Package rule).
  - A card's tags are the union of its Packages' tags. An Aircraft card also shows its aircraft's type designators as tags (6.6).

  Tags are shown on the card's dialog. Filtering and editing are Later (17).
- **Built-in content is shown only when the user has an add-on for it** (2026-10-28): a Package that belongs to the same Airport (Replaces built-in), a GSX profile for the Airport (8), or a Livery, Mod or attached Package (6.6) of the same aircraft that is not Sim-managed (a Library Package, or a Fixed or External entry). Sim-managed liveries and parts (127 `asobo-aircraft-*-livery-*` packages on Rickard's machine) never make a built-in aircraft visible; they are shown locked inside a card that exists for another reason. The built-in part is shown locked in the dialog (6.5). Otherwise built-in Airports appear only on the map, and built-in aircraft not at all.

### 6.5 Cards and Airports

- **A card is the app's group (2026-10-29).** It is stored with an id, a menu section, a tab, its ICAO codes (Scenery cards only: none, one or several) and the user's name and image when set. A Package belongs to at most one card, through a reference from the Package to its card; a Package without a card is provisional or Not shown (6.4). Scenery, Aircraft and Other cards are different kinds: only Scenery cards have ICAO codes, an Aircraft card is held together by its aircraft (6.6), and an Other card starts as one Package and gets more only by user action.
- **"(several)" card:** a Scenery card with more than one ICAO code.
- **Name and image** (computed unless overridden): a Scenery card with exactly one ICAO code that a record knows is named from it and that Airport's name ("EHAM Schiphol"); an Aircraft card takes the display name of its first providing Package by name, else the display name of its locked base, else the Container name, else the display name of its first Package by name; every other card, "(several)" cards included, takes the display name of its first Package by name ("YQ05 Mount Mulligan"). A card whose only Packages are GSX profiles, and that the one-ICAO rule doesn't name, is named by its ICAO codes. GSX profile Packages never give a card its name or image. The image is the thumbnail of the first Package by name that has one. When the user adds or removes an ICAO code on a card that already has one, its current name is stored as the user's, like after a merge, so "EHAM Schiphol" keeps its name when EHRD is added.
- **Built-in parts:** built-in content (Sim-managed packages) is never a Package of a card. On every analysis a built-in Airport is attached to each Scenery card that has its ICAO code, and a built-in aircraft to each Aircraft card whose Packages provide or repaint its Container (6.6). Built-in parts are shown locked in the dialog and are never stored.
- **When cards are made:** only from complete facts. On first run, after the content pass has finished for Community, Libraries and Sim-managed packages; later, for a new Package once a completed scan has read it. A card is never made from the fast discovery pass. **A Package being installed is left alone (Rickard, 2026-10-29):** a scan makes a card, and fixes the Package's menu section, only when nothing in the Package's folder has been written for a few seconds, judged by the newest write time in the folder, so a scan at startup, on F5 or after a watcher overflow is covered too; an archive can write `manifest.json` before its BGL files, and a scan in between would fix a Scenery Package as Other. A Package scanned earlier stays provisional until the next scan. Until then the card wall shows provisional cards, one per Package, that are not stored; each sits in the menu section of its **manifest section** (from manifest `content_type` alone) on Scenery → Landscapes, Aircraft → Other, or the Other section, and follows the rule of 6.4 for which Packages get cards, using its manifest section. A provisional card offers on/off only: drag and drop and the dialog's edits are disabled until the Package has its card, and the banner says "Grouping cards…" with the scan progress. If the first content pass is interrupted, making cards waits across restarts until it completes.
- **Initial cards:** Packages first seen in one completed scan are grouped among themselves first (a new airport shipped as scenery and lights gives one card), then joined to existing cards by the rule below. Scenery Packages with the same set of ICAO codes, one or several, share one card. Grouping only ever combines Packages of the same menu section, and a new card has that menu section. "First Package by name" in this spec means by folder name, case-insensitively (C9).
  - **Scenery:** Packages that belong to exactly one Airport, the same one, share one card with that ICAO code ("EHAM Schiphol" with `brickbybrickdesign-airport-eham-amsterdam`, `duijffmedia-airport-eham-amsterdam`, `duijffmedia-schiphol-amsterdam-mega-enhancement-lighting`). **A Package that belongs to several Airports** (counted distinct ICAO codes, not records: `tag21-airport-ybas-alice-springs` has two YBAS records and is a plain YBAS Package) gets a **"(several)" card** with all its ICAO codes, on Airports, named after the Package: `sltcreations-airport-yq05-mount-mulligan` gives "YQ05 Mount Mulligan" with YQ05 and YMUZ, and `asf-mission-demo` gives a card with FEFA and FEGK. No ICAO code is picked from the name. Every other Scenery Package gets its own card with no ICAO code.
  - **Aircraft:** one card per aircraft, by Container (6.6).
  - **Other:** one card per Package; never grouped automatically.
- **New Packages later** join an existing card in their menu section when one matches, otherwise they get a new card by the initial rules. For matching, a card's ICAO codes include the ones the user removed from it. A Scenery Package matches the card whose ICAO codes are exactly the Package's ICAO codes: an update with YQ05 and YMUZ joins the "(several)" card. A Package with no exact match joins the oldest card whose ICAO codes include all of the Package's ICAO codes, preferring cards that have them all over cards that cover some only through removals (Rickard, 2026-10-29): after the user has added EHRD to the EHAM card, "OrbX EHAM 1.5" still joins it, an update with EHAM and EHRD joins a card with EHAM, EHRD and EHLE, and a Package with only YQ05 joins the "(several)" card when no YQ05 card exists. A Package with an ICAO code that no card has gets a new card, even when another card has its other ICAO codes. A Scenery Package with no ICAO code matches no card. Only the ICAO code field keeps ICAO codes unique; automation may give two cards the same ICAO code (YQ05 on the "(several)" card and on a YQ05 card). An Aircraft Package matches by Container (6.6). When several cards match, the oldest wins. Automation never rearranges or merges existing cards.
- **User changes (all saved immediately, no save button):**
  - **Merge:** drag a card onto another card in the same menu section and on the same tab; this is the only way to merge. The dragged card's Packages move to the target, and the empty card is removed. The target keeps its name, image and tab and adds the dragged card's ICAO codes to its own, so a later Package for any of them joins the merged card: after merging "EHRD Rotterdam" into "EHAM Schiphol", a later EHRD Package joins it, and a duplicate EHAM card made by an update with EHAM and EHGG doesn't come back once it has been merged (Rickard, 2026-10-29; this replaces the earlier rule that dropped the dragged card's ICAO codes, decided before a card could have several). A merge stores the target's current name and image as the user's, so they don't change afterwards. Cards on different tabs are merged by first moving one of them to the other's tab.
  - **Move:** drag a card onto a tab header (same menu section) or onto a menu section in the left menu. A card moved to another menu section becomes that section's kind of card and lands on the tab the rules of 6.4 give there; its Packages keep their own menu section. A card moved out of Scenery loses its ICAO codes, and with them its Packages' manual links, and its removed ICAO codes. A card moved into Scenery gets ICAO codes as a new card would (Initial cards): the ICAO codes its Packages belong to when they all belong to the same Airports, otherwise none. Rickard's example (2026-10-29): an Other card with an EHAM Package, dragged to Scenery, lands on Airports as an EHAM card and can then be dragged onto the existing EHAM card.
  - **Move out:** on a Package row in the dialog. The Package gets a new card of its own in the same menu section and tab, with no ICAO code. "Move out" is not offered on a card's only Package, nor on a GSX profile Package.
  - **Remove:** on a Package row in the dialog that is Not found, or that is unlinked and held by no registered Library (including an External Package whose link was removed), and only there. It removes the Package's Broken link, if any, after confirmation and through the apply path, then forgets the Package and all its user data; a card left with no Packages is removed. While the sim runs, Remove is disabled with the reason shown, like removing a Broken link in Settings. Removing many Not found Packages at once is Later (17).
  - **Edit:** name and image (the thumbnail of one of the card's Packages, or a file the user picks; only its path is stored, the file is never copied or moved) in the dialog.
  - **ICAO code field** (Scenery cards only): lists the card's ICAO codes and lets the user add and remove them. Any 3–5 letters or digits are accepted (C3 of plan 2); known ICAO codes are suggested with their names while typing. A Package on a Scenery card is linked manually (rule 3 below) to every ICAO code of its card, so a merge, Move out and a Package joining later need no rule of their own. Adding an ICAO code moves the card to Airports. An ICAO code that another Scenery card already has is rejected, naming that card: typing never merges. An ICAO code no record knows gives no map marker. Removing an ICAO code removes the card's link to that Airport, automatic links included. The card remembers the removal, and it applies to every Package on the card, also those that join later or arrive by a merge: none of them belongs to that Airport while on the card. An update that still has that ICAO code joins the card instead of making a new one (New Packages later). The card stays on its tab. Adding an ICAO code to a card, with the field or by a merge, clears the card's removal of it; a merge doesn't add an ICAO code the target has removed, and adds the dragged card's removals to the target's, except ICAO codes the target has. A card made by Move out gets the old card's removals as its own.
- A Package belongs to an Airport if:
  1. one of its BGL files contains an airport record with that ICAO code, unless the Package is excluded (below), or
  2. rule 1 found no counted airport record in it, its menu section is Scenery (so a `SCENERY` Package with no BGL doesn't qualify), it isn't air traffic and isn't Sim-managed, and its folder name or manifest title contains, as a separate token, an ICAO code that some airport record knows (records of Packages excluded by pattern don't count; records excluded only by the threshold do), a token that is not on a stop-list of common words (`PACK`, `SAVE`, `SWIM`, `SANS` and similar). 4–5 character tokens may sit anywhere (`…-esgj`, `EHAM …`); 3-character tokens only count as the leading token (`u60-u60`, `c53-…`), because about 3,000 generic idents are 3 characters long, or
  3. it is on a Scenery card that has that ICAO code (a manual link, made with the ICAO code field).

  A GSX profile belongs to the Airport of the ICAO code in its file name (8) for making and joining cards and for the map. It is never Real scenery and never Replaces built-in, and removing that ICAO code from its card doesn't unlink it.

  Some real airport Packages have neither an airport record nor an ICAO code token (for example `005builds-lszrsceneryv11`, `grancanaria`); they land in Landscapes until the user types the ICAO code on their card.
- An Airport is identified by its ICAO code, or by its local ident when it has none (`OG20`, `MA44`, `U60`, `3U2`, `YX01`); both count as its ICAO code in this spec. Idents of 3–5 characters count, as in the sim; add-on airports use 5-character idents (`YALR1`, `YHAC1` from sltcreations, official `RJTIH`). Empty idents are ignored. Non-airports with airport records (`VOLC1`–`VOLC5` in `microsoft-airport-voloport`, `NEOF2` in `neofly-neofly-cargo`) are kept out by the exclusion list.
- Airport data (ICAO code, name, position) is read from airport records in BGL files (MSFS 2020: section type `0x03`, record id `0x0056`) of Library Packages, Community entries and Sim-managed packages.
- **Encrypted official airports:** some handcrafted official airports have no readable BGL (`asobo-airport-eddf-frankfurt`, `-egll-heathrow`, `-kden-denver`, `-lemd-adolfo-suarez`, `-omdb-dubai-intl`, `-heca-cairo`, `-fact-cape-town`). For Sim-managed airport packages where no airport record is found (no BGL at all, or BGLs without an airport record like `asobo-airport-lfcs-saucats`), the ICAO code comes from the package name (`*-airport-<code>-*`) and the name and position from the generic record for that ICAO code. This rule links only ICAO codes that are known from other records, so a Sim-managed name never creates a nameless Airport.
- **Excluded from rule 1:**
  - Packages with airport records for **more than 10 airports** (configurable). Verified: `fs-base-genericairports` 40,351, `fs-base-nav` 22,555, `navigraph-navdata` 15,629, `fsdreamteam-gsx-world-of-jetways-2020` 897, `microsoft-nordics2-point-of-interest` 87. The user can opt a Package back in; opting in overrides both the threshold and an exclusion pattern.
  - Packages on a configurable exclusion list. Defaults include `navigraph-navdata*`, `*-point-of-interest`, `fsdreamteam-gsx-world-of-jetways*`, `microsoft-airport-voloport` and `neofly-*`.
  - Records of a Package excluded by pattern are ignored everywhere: they create no Airports and are no known ICAO codes for the package-name rule. Records of a Package excluded only by the threshold (generic airports, navigation data) still supply known ICAO codes, names and positions.
  - An airport name that is a `TT:` localisation key (`TT:AIRPORTLR.LOWI.name`, only in Official Asobo packages) is resolved through `fs-base-genericairports\en-US.locPak` in the Official folder: exactly by key, else by the ICAO code in the key (`TT:AIRPORT.CYTZ.name` has no region part). A key that doesn't resolve, or a missing or unreadable locPak, counts as no name.
- **Real scenery** for an Airport: at least one non-excluded Library, Community or Sim-managed package belongs to it, or the user linked one manually. That includes `asobo-airport-*`, World Update partner airports and Marketplace airports. Airports known only from generic or navigation data have no real scenery.
- **Replaces built-in:** a Package that isn't Sim-managed, belonging to an Airport that a Sim-managed airport package also belongs to. (World Update partner airports such as `microsoft-airport-edny-friedrichshafen` have third-party creators but are Sim-managed, so the test is where the Package lives, not who made it.) A dependency on the official package is an extra signal. Inactive Duplicate copies are analysed and linked like active ones. This is information, not a warning.

### 6.6 Aircraft and Liveries

- Aircraft identity is the **Container** name (the folder under `SimObjects\Airplanes\` that holds an `aircraft.cfg`). Containers are indexed across Library Packages, Community entries and Sim-managed packages. Where `aircraft.cfg` is unreadable (encrypted official or Marketplace aircraft with `.fsarchive` files), container names come from paths in `layout.json`. Encrypted aircraft provide their containers in any source (Library, Community or Sim-managed), not only Sim-managed.
- **Type designator (2026-10-29):** `icao_type_designator` in the `[GENERAL]` section of `aircraft.cfg` (`C152`, `A20N`) is read and shown as a tag on the Aircraft card, one per distinct designator of its Packages. It never decides grouping. An `aircraft.cfg` without it, or an unreadable one, gives no tag.
- `base_container` is a relative path (`..\Aerosoft_CRJ_900`); its last segment names the container.
- A **Livery** Package is one where every `aircraft.cfg` has a `base_container` whose container isn't provided by the same Package. Most Liveries declare `content_type: AIRCRAFT`, so `content_type` alone can't tell. Livery detection runs only for the Aircraft menu section.
- **One card per aircraft, by Container:** every Package that provides the container, plus the Liveries whose `base_container` names it, form one card. They are Packages of that card and can be switched one by one in its dialog. Two aircraft of the same type from different makers (`Asobo_C152`, `Carenado_C152`) have different Containers and get two cards; the user merges them by drag and drop if they want. When two Library Packages provide the same container (`sal1800-cessna-170b-backcountry` and `sal1800-cessna-super-170b-OPTIONAL` both provide `Cessna-170B-Backcountry`), both join the same card; scan order never decides.
  - When a Sim-managed package, or a Fixed or External Package, also provides the container, it is shown as the card's locked base, and the Library Packages that provide the same container are labelled **mods** (`mge-asobo-aircraft-c152` provides `Asobo_C152`, like the official C152). A built-in aircraft with no Livery, Mod or attached Package gets no card (6.4). A Livery for a built-in aircraft gets a card for that Container, with the built-in aircraft as its locked base; later Liveries for it join that card.
  - A Package that provides several containers (`aerosoft-crj` provides `Aerosoft_CRJ_550`, `_700`, `_900` and `_1000`) gives one card; Liveries and attached Packages for any of them join it. A Livery that covers several aircraft never merges their cards: when cards are first made it joins the card of the first aircraft by name, and later the oldest matching card (6.5).
  - **Matching later Packages:** a new Package joins the card whose Packages provide or repaint one of its Containers, including Not found Packages, whose Containers are kept (11): "CRJ 1.1" joins the card "CRJ 1.0" left behind.
  - `INSTRUMENTS` Packages never take part in Livery or attachment matching; each gets its own card on the Instruments tab.
  - A Package without `aircraft.cfg` (`flybywire_A320Neo_Cabin`, `simflight-C152-interior-navtablet`) is matched, when its `content_type` is `AIRCRAFT` or `LIVERY`, by its `SimObjects\Airplanes\<folder>` names first, then by `dependencies` naming a known aircraft Package, otherwise it gets its own card on the Aircraft → Other tab. So does a Livery whose aircraft no Package or Sim-managed package provides.

### 6.7 Warnings

Only these are warnings:

- **Duplicate package**: the same Package name in Community and in `Official`.
- **Duplicate copy**: the same Package more than once across the Libraries. The active copy is chosen in the dialog.
- **Livery without aircraft**: a Livery is on while no provider of its base container is on or always loaded.
- **Not found** (2026-10-29): a Package whose Library answers but whose folder is gone (6.1). The dialog offers "Remove".
- **Broken** and **Stray** entries in Community (shown in the banner, 10).
- **Two GSX profiles** for one Airport (8): one warning for each ICAO code of the card whose status is Two profiles, also when the profiles sit on another card with that ICAO code (2026-10-30).

Several Packages at one Airport are shown neutrally ("3 packages"), never as a warning (ADR 0002). Ignored entries are neutral info. "Library not found" is shown in the banner, not on the cards.

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
3. **The user adds Libraries (2026-10-28, per menu section 2026-10-29).** The first-run screen starts with an empty list per menu section and "Add Library…": the user picks a folder for Scenery, Aircraft or Other, and may add the same folder to several. No Libraries are proposed. The existing links are used only to show coverage ("87 of 91 links covered", a total), so a forgotten folder is noticed. A counted junction is covered when its target lies under a registered Library. Counted are junctions whose target exists and holds a `manifest.json`, except targets under `…\FSDT Addon Manager\MSFS\` (FSDT's Addon Manager owns its junctions, and those entries stay External). Symlinks (made by installers such as SayIntentions), Broken links and FSDT's junctions are not counted; they are listed separately as "not counted (made by other tools)".
4. The screen explains that Packages in folders the user doesn't add stay External and are shown locked, and that a folder another tool also links from (such as Orbx Central's download folder) can be added and switched from both.
5. Adopt the current Community state: each Package is on if linked, off if not. No sync dialog on first run, with one exception: Stale entries (a link whose target is gone but whose Package is found in a Library) count as on and are offered for re-point in the sync dialog right after adoption (6.1, Moved Packages); on first run its rows are listed per Package, since no cards exist yet. The same holds when a Library is registered later.
6. Run discovery, then the content scan (7.3). On/off is available as soon as discovery is done.

### 7.2 Normal start

1. Show the main window with the last saved state and cached scan results immediately.
2. In the background, reconcile against Community (7.4).
3. Then run an incremental scan (7.3).
4. Update the UI as results come in. On/off works at all times; it only needs Community, Package paths and the stored choices.

Targets (to be measured): main window usable with cached data within about 1 second; Community reconcile under 1 second for about 150 entries.

### 7.3 Scanning

Two passes:

1. **Discovery** (fast, seconds): walk Libraries and Community, read `manifest.json`, find Packages, Duplicate copies and manifest sections. Enough for on/off.
2. **Content**: BGL airport records, `aircraft.cfg` (containers and type designators), `layout.json`, GSX profiles, thumbnails. Builds Airports and cards.

- Order on first scan: Community → Libraries → Sim-managed packages, with visible progress. The generic airports (about 40,000 in almost 5,000 BGL files) are read once and cached.
- Incremental by default: a Package is re-read only if its path, or the timestamp or size of `manifest.json` or `layout.json`, has changed. F5 forces a full rescan.
- Community, all Libraries and the GSX profile folder are watched for changes; affected Packages are rescanned in the background. Community is watched non-recursively; changes inside linked Packages are seen through the Library watchers. Watchers overflow during large installs (`C:\Sim\Addons` holds about 123,000 files), so an overflow triggers an incremental rescan.
- From Sim-managed packages, the app reads Scenery and aircraft containers. Their aircraft are never toggleable.

### 7.4 Reconciliation

- The app stores the **last accepted Community snapshot**: every entry with its resolved target. Reconciling compares that snapshot with the current Community, not with the user's choices.
- Changes to External, Fixed and Ignored entries are recorded silently and never open the dialog.
- If other differences exist, a sync dialog lists them, one row per Package, grouped by card. Each row chooses "keep Community" (preselected) or "restore app state"; one choice can be applied to all rows. A row for a Package whose name a Fixed or External entry now holds offers only Keep Community.
- "Keep Community" sets the Package's stored choice to match Community, and when another tool re-pointed the link to another Duplicate copy, it stores that copy as the active copy. "Restore app state" changes the link through the apply path (6.8). Either way, the snapshot is updated.
- If Community changes while the app runs and the change didn't come from the app (for example Addons Linker, which may run alongside), the user is told and offered the same sync dialog, with a way to not be asked again for the rest of the session. The app never silently overwrites such a change.

## 8. GSX

- Profiles are read from `%APPDATA%\Virtuali\GSX\MSFS`.
- **ICAO code from the file name:** the leading run of 3–4 letters or digits, followed by `-`, `_`, `.` or the end of the name (`epgd_LukSid.ini` → EPGD, `LPMA.ini` → LPMA). 5-character idents (`YALR1`) are not matched by file name. A 4-character ICAO code no record knows still gives a card, without a map marker, like a typed unknown ICAO code (6.5). A file that yields no ICAO code, or an unknown 3-character ICAO code (`gsx.ini`), gives no card and is listed in Settings as "GSX files not recognised". A `.py` alone never creates a card.
- **A GSX profile is a Package (2026-10-29):** each `.ini` or `.off` file in the GSX folder whose name yields an ICAO code is a Package of its own kind, identified by its file name without the extension, which is also its display name. Files that yield no ICAO code, or an unknown 3-character ICAO code, are not Packages; they are looked at again after every completed scan, since a later scan can make an ICAO code known. Renaming a profile between `.ini` and `.off` keeps the same Package. When both `lpma.ini` and `lpma.off` exist, the `.ini` is the Package and the `.off` is listed with it as a disabled variant. Like a Livery for a built-in aircraft, it gets a card when none exists: it is a Scenery Package with one ICAO code and joins a card by the rules of 6.5, otherwise it gets a new card with that ICAO code on Scenery → Airports. For a built-in Airport, that card shows the built-in Airport as its locked part (6.5). A deleted profile is Not found (6.1). A missing GSX folder is handled like a Library not found: nothing changes, and the banner says so; while it is missing, cards show no GSX mark and no Two profiles warning. The banner shows "GSX folder missing" only when the folder is missing and a profile Package is stored on a card (2026-10-30): a machine without GSX gets no banner. Profile Packages are locked (6.3).
- `.off` files are disabled profiles: shown as disabled, not counted.
- `.py` files are not Packages. They belong to the Airport by ICAO code, not by base name (`essa-orbx.py` belongs with `essa-bwib1r-noolvdgs.ini`), and are listed in the dialog of every card that has that ICAO code. A `.py` doesn't need an `.ini` with the same name. When an Airport has two profiles, its `.py` files are listed with the Airport, not paired with one profile.
- Status per Airport: **Installed** (at least one active `.ini` for the ICAO code; `.py` files alone don't count), **Missing**, **Two profiles** (two or more active `.ini` profiles in the GSX folder for one ICAO code; real cases today: LDSP, LFMN, LFPG). On a "(several)" card the mark reflects its first ICAO code by name; the dialog shows every ICAO code. A card without an ICAO code has no GSX mark.
- **Where it is shown (2026-10-28):** as a mark on the Airport card (✓ / missing / two) and in full in the card's dialog. There is no GSX table.
- **Profiles shipped with scenery:** some Packages carry a profile in a subfolder (`GSX\`, `GSX-PROFILE\`, `GSX-Profile-EPKK\`). They are recognised by `<code>….ini`/`.py` file names, not by the folder name (`aerosoft-crj\Data\GSX\` holds aircraft `gsx.cfg` files, not profiles). They are part of their scenery Package, not Packages of their own. They are shown in the dialog as "shipped with scenery, not installed" with an action to copy the profile into the GSX folder (it never overwrites; when a profile for the ICAO code is already installed, the action says that copying gives Two profiles and asks first), and they don't count for Two profiles. A shipped profile is listed only under the ICAO codes of its card (2026-10-30): a file whose ICAO code is not one of the card's codes (an aircraft Package's panel-state files such as `cold_and_dark.ini`, a stray `test.ini`) is not shown. Its `.py` files of the same ICAO code in the same folder belong to it. A shipped profile whose `.ini` or `.off` name is already in the GSX folder is marked "already in the GSX folder" and offers no copy; the copy is also refused when `<name>.ini` or `<name>.off` exists, so it never switches a profile the user disabled back on. GSX's own log (`Couatl.err`) has only ever shown it loading profiles from its own folder, which supports this; see 15.
- Missing profiles get a deep link to flightsim.to, filtered to GSX profiles for the ICAO code: `https://flightsim.to/others/gsx-pro/search/{code}` with the ICAO code in lower case (2026-10-30). The template is a constant until Settings gets the store templates of 9 (plan 6).

## 9. Finding scenery

For any Airport, the app offers deep links to search flightsim.to and SimMarket for its ICAO code. The links are URL templates in Settings, so more stores can be added by the user.

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
- **Card wall:** large cards with the image on top (the image of the first Package that has one, the user's choice, or a placeholder per type). A Scenery card with one ICAO code shows it in bold and the card's name below; a "(several)" card shows "(several)", its name and its ICAO codes; other cards show the card's name, and Aircraft cards their type designators. Below: package count, GSX mark (Airports) and warnings. Cards are ordered by ICAO code on Airports ("(several)" cards by their first ICAO code; cards without an ICAO code after them, by name) and by name elsewhere. **No switch on the wall.** A traffic light in the top right corner shows state: red Off, yellow Mixed, green On, grey with a lock when Locked. A Pending card also shows "Next sim start". Not found Packages, "(several)" and built-in parts are labelled.
- **Card dialog** (opens on click; Esc, ✕ or a click outside closes it): editable name and image, and on Scenery cards the ICAO code field (all saved immediately); one control to switch all switchable Packages; the Packages by folder name (C9), each with display name, folder name, path, version, Library, Duplicate copy choice and its own switch, plus "Move out", and "Remove" on a Not found Package; locked and built-in parts dimmed; the ICAO codes of its Packages (with Airport names), other cards at the same Airport, tags, Replaces built-in, GSX profiles (installed, shipped, missing with a search link) and deep links to find scenery.
- **Map** (Scenery → Airports, view switch `Cards | Map`): there is a marker for every ICAO code that a record knows and that a card has or that a Package belongs to (6.5, rules 1–3, the encrypted-official rule and GSX profiles), built-in ones included, whatever menu section their cards are in, placed at that record's position: an EHAM Package on a card the user moved to Other still has its marker, which opens that card. There is one marker per ICAO code. The cards at an ICAO code are the cards that have it plus the cards whose Packages belong to that Airport. The marker opens the oldest card that has the ICAO code as its only ICAO code; otherwise the first of the cards at the ICAO code by name, whose dialog lists the others. Its colour follows that card's light (green On, yellow Mixed, red Off, grey Locked). A built-in Airport with no card, and an Airport whose only Packages are Not shown (they have no card, 6.4), have a grey marker. An ICAO code no record knows gets no marker. "Show all airports" adds the generic ones, clustered when zoomed out. Airport search. Selecting an Airport opens its card's dialog, or shows ICAO code, name and search links when it has no card. When its Packages are all Not shown, it also says "not shown — register <Library> for <its menu section>".
- **Settings:** Packages root (the folder holding Community and Official; detected, overridable); Libraries per menu section (add/remove, link coverage); **Not shown** (6.4); **Community** (every entry with its classification, link target and warnings; remove Broken links with confirmation, disabled while the sim runs with the reason shown); search URL templates; airport threshold, exclusion list and token stop-list; version and update notice.
- **Banner** at the top of the window: Broken and Stray entries in Community (with a link to Settings → Community), Libraries not found, unreadable Packages, a missing GSX folder (8), pending changes while the sim runs, scan progress and "Grouping cards…" (6.5).
- **Overlays:** first-run setup (7.1) and the sync dialog (7.4).

Thumbnails come from the Package's own files (`ContentInfo\<any>\Thumbnail.jpg`; the subfolder name differs from the Package name), with a placeholder per type when none exists.

## 11. Storage

- **One SQLite file (2026-10-29):** `%LOCALAPPDATA%\<app>\userdata.db` holds both the user data and the scan cache, in separate tables. This replaces the 2026-10-28 decision to keep user data in JSON: cards and Packages are related by references that change with every merge and move, which is what a relational database is for, and the file can be opened in any SQLite editor.
- **User tables:**
  - the Libraries of each menu section;
  - cards: id, menu section, tab, the user's name and image path when set, created time (the oldest card wins, 6.5), and their ICAO codes;
  - Packages: kind (folder or GSX profile), name, a reference to the card (empty while provisional or Not shown), menu section (empty until a completed scan has read it, 6.4), on/off choice, active copy, and the display facts the card needs while the Package is Not found or its Library is not found: last known path, version, tags, display name, thumbnail path, type designators, Containers, and the ICAO codes of its Airports with their names and positions, which count as known records while the Package is Not found or its Library is not found. The facts are updated by every completed scan;
  - the ICAO codes the user removed from each card (Airport links are not stored per Package: manual links and removals follow the card, 6.5); the Packages root when the user picked or overrode it (7.1); whether the first content pass has completed (6.5); opted-in Packages; the pending set; the last accepted Community snapshot; search URL templates; threshold, exclusion list and stop-list (only the user's changes to the defaults).
- **What is stored versus computed:** a card and its Packages change only by user action and by new Packages joining it (6.5). A card's state, its built-in parts, its tags, Not found and "(several)" are computed on every analysis. Its name and image are computed unless the user set them.
- **Saving:** every change is saved at once, in one transaction.
- **Scan cache tables:** per Package the change marker and the facts read from its files (manifest data, airport records, BGL section types, `aircraft.cfg` facts including type designators, airplane folders, thumbnail path), and the GSX files shipped inside Packages. The GSX folder itself is not cached (2026-10-30): it holds about 40 small files, so it is read on every scan run and at startup. Links, Airports and aircraft groups are recomputed from these on every analysis.
- **Versions:** the file records one version for the user tables and one for the cache tables. A new cache version drops and rebuilds only the cache tables, as does a cache row that can't be read: deleting the cache only ever costs a rescan, and it never touches user data. A new user-table version is migrated forward in a transaction, after a copy of the file is taken with `VACUUM INTO`. A file whose user-table version is newer than the app's (an older release installed over a newer one) stops the app with an error naming the file; it is never migrated down or written to.
- **A damaged file:** the file is checked at startup. When the check fails, the file is first copied byte for byte next to it (`userdata.db.damaged-<time>`); then the cache tables are dropped and rebuilt and the check runs again, so damage confined to the cache usually costs only a rescan. If it still fails, or the file can't be opened, the app stops with an error naming the file and its copies. The app never deletes the file and never writes to it before that copy exists.
- **A locked file** (another program, such as an SQLite editor, holds a write lock) is waited on briefly, then reported: at startup the app stops with an error naming the file, and later a change is refused with the same message, before any link is touched. User data never falls back to memory.
- **First run** is decided from the user tables (no Libraries registered and no snapshot), not from whether the file exists.
- New Packages found after first run get a card by the rules in 6.5; the user's existing arrangement is never rearranged.

## 12. Structure (suggested)

The builder may change this, but the boundaries matter:

- **Core**: domain model and rules (Package choices and links, cards and their computed state, visibility, tabs and tags, Airport linking, warnings, apply path and pending set, reconciliation diff). No WPF, no file system.
- **Sim2020**: everything MSFS 2020 specific: locating the install, `UserCfg.opt`, folder layouts, manifest and `layout.json` reading, BGL airport records, `aircraft.cfg` and containers, sim process detection. Behind interfaces in Core (C6).
- **Infrastructure**: junctions (P/Invoke), file watching, SQLite storage (one file, 11), GSX folder access, GitHub release check, the single-instance mutex.
- **App**: WPF + WPF-UI views and view models, WebView2 map.

The reconcile, watcher, apply path, pending set and own-change suppression form one coupled subsystem; it should be planned as one unit with its state machine written down.

## 13. Testing

- Core rules are unit tested without touching disk: on/off per card and per Package, computed state (On/Off/Mixed/Locked), "switch all" skipping locked Packages, which Packages get cards and Not shown, initial cards and tabs, joining later Packages (exact ICAO codes, Containers, oldest card wins), the Asset pack test, tags and type designators, "(several)", merge/move/move out/remove and the ICAO code field, map markers, Airport linking (threshold, exclusions, stop-list, encrypted official airports), Livery and mod rules, built-in cards, warnings, pending set, reconciliation diff.
- Junction handling is tested against temporary folders, including guard tests that removing a link leaves the target untouched, that a failed creation leaves no empty folder, and that a taken name is never overwritten.
- BGL reading and GSX file-name parsing are tested with small fixtures, including the real-world names above. Official sim files are not committed to the repo.
- Guard tests for the state machine: a moved Package is re-pointed, a Not found Package keeps its user data and its place on its card, a Library not found changes nothing, a failed link change saves nothing, the self-check catches "on but not linked".
- **First-run fixture:** a sanitised copy of Rickard's Community (entry names, kinds, targets) and Library tree (folders and manifests, no content) is committed, so initial cards, tabs and tags are tested as a whole scenario.
- Storage is tested against a temporary SQLite file: user data survives a round trip, the card references hold, a new cache version leaves the user tables untouched, and a damaged file is neither deleted nor overwritten.
- A collision report (all Package name and title tokens against the generic idents) is part of the content-scan tests, so the token stop-list is checked against data.
- An opt-in integration test runs against the local MSFS install.

## 14. Left to the builder

- Exact visual design within section 10 and the BOB references.
- BGL parsing details (the format is documented; Little Navmap is a reference).
- Parsing details of GSX profiles (Dave's code is reference only).
- Map tile provider, as long as it needs no API key and has a dark style.
- How "update available" is shown.
- Logging and diagnostics.

## 15. Open investigation

- **Does GSX Pro load profiles shipped inside scenery Packages?** Evidence so far (GSX's own log) says no. If it turns out yes, shipped profiles count as active, and Two profiles may change for ESSA, ESGG, EDDF and EPKK on Rickard's machine. Note that a shipped and an installed profile can be variants of the same profile (ESSA: `essa-bwib1r.ini` and `essa-bwib1r-noolvdgs.ini`), which would make a plain Two profiles warning a false alarm.

## 16. Calls I made (please check)

- Spec written in English, like GLOSSARY.md and the ADRs.
- Moved Packages are re-pointed.
- `neofly-*` is on the default exclusion list (its cargo package carries a fake airport record).
- `SCENERY` Packages without any BGL go to Other.
- Marketplace and World Update partner airports count as real scenery.
- Changes to Community from other tools while the app runs offer the sync dialog, with "don't ask again this session".
- The pending set survives an app restart.
- Startup targets of about 1 second.
- Sync dialog rows are per Package, grouped by card.
- A card's menu section is its Packages' menu section when it is made; its menu section and tab change only by user action (6.4).
- Within one scan, Packages are grouped by identical sets of ICAO codes before they join existing cards, so a YQ05 Package and a YQ05+YMUZ Package found in the same scan get two cards, while found in separate scans they share one only when the YQ05+YMUZ Package came first; the user merges them (2026-10-29).
- A merge pins the target's name and image.
- Built-in parts are computed on every analysis, never stored.
- A GSX profile whose ICAO code no record knows gives a card without a map marker.
- Cards wait for a completed content scan; provisional per-Package cards, with on/off only, are shown until then.
- Stale links found at adoption are offered for re-point straight away.
- A moved Scenery Package with no BGL starts in Landscapes, not Assets.
- A Library Package whose name an External or Fixed entry holds is one Package, with the entry as its locked active copy.
- Managed elsewhere is dropped from the MVP (Rickard, 2026-10-28): every registered Package can be switched, including Packages in a folder another tool also links from (Orbx Central); unregistered folders stay External.
- A Library registered after first run adopts Community state without a sync dialog.
- Not shown Packages can be moved to a menu section from Settings, which gives them a card there.
- Folders named `Community` or like a menu section give no tag.
- Built-in parts, Fixed and External Packages, GSX profiles and Packages that no registered Library holds don't count towards a card's On/Off/Mixed state (6.3).
- When several cards match a new Package, the oldest card wins (2026-10-29).
- Adding an ICAO code on a Scenery card moves it to Airports; removing one leaves the card on its tab (2026-10-29).
- A "(several)" card is named after its Package's display name, like any card that isn't a one-Airport Scenery card (2026-10-29).
- A map marker opens the card that has the ICAO code as its only ICAO code, else the first card at the ICAO code by name (2026-10-29).
- The GSX mark on a "(several)" card reflects its first ICAO code by name (2026-10-29).
- A card moved out of Scenery also loses the manual Airport links that came from its ICAO codes (2026-10-29).
- A Livery covering several aircraft never merges their cards: the first aircraft by name when cards are first made, later the oldest matching card.
- GSX profile Packages are locked in the MVP, as switching profiles is Later (2026-10-29).
- Before a user-table migration, the file is copied with `VACUUM INTO`; a damaged file stops the app and is never deleted (2026-10-29).

## 17. Later (after the MVP)

Decided on 2026-10-28 to come after the MVP, in roughly this order of interest:

- **Filter and search** on the card wall, including filtering by tag.
- **Hide** and **Show hidden** for cards.
- **Delete from disk** for a card's Packages, as an explicit user action that never deletes through a junction.
- **Editing tags.**
- **Switching card layout** (large cards / compact cards).
- **Setting a map position** for an ICAO code that no record knows (preferably by clicking on the map).
- **Switching GSX profiles on and off** (`.ini` ↔ `.off`).
- **Installing GSX profiles by drag and drop**, asking before overwriting.
- **Removing Not found Packages in bulk.**
- **A warning on cards whose Packages lie in a Library removed from their menu section.**
- **Uses**: a card using another (automatic from manifest `dependencies` and set by hand), with the "Used card off" warning.
- **Undo** for merges, moves and edits.
- **Managed elsewhere** (a lock on Packages whose links another tool recreates), if a real need appears.
