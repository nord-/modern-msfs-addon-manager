# Plan 5c — editing cards, drag and drop, the GSX section

Issue #23. Builds on plan 5b (#22): the card dialog with the card's switch and Remove. Design pass with Rickard on 2026-11-09. The MVP spec (`2026-10-25-mvp-design.md`, sections 6.5, 8 and 10) holds except where this document says otherwise; every deviation is listed in section 7 and recorded in `ANALYS.md`.

## 1. Intent

Rickard switched from MSFS Addons Linker when 5b worked. 5c lets him fix what the automation got wrong and see what a card holds: rename a card, pick its image, give it the right ICAO codes, merge two cards, split one, move a card to another tab or menu section, and see the card's Airports, built-in parts and GSX profiles. Guiding rule from 5b still applies: build only what is used now.

## 2. Scope

In scope, all four parts of #23:

1. Editing the card dialog's header: name, image, ICAO code field.
2. Split on a Package row (spec 6.5 "Move out", renamed) and drag and drop on the card wall: merge, move to a tab, move to a menu section.
3. The rest of the card dialog: built-in parts, Airports with names, Replaces built-in, other cards at the same Airport, tags.
4. The GSX section, display only.

Out of scope: on/off for GSX profiles and copying a shipped profile into the GSX folder (a later decision, section 7); a switch per Package; choosing a Package's active copy (5b decided the default rule of MVP 6.1 always chooses; the dialog shows the active copy only); Settings (5d); the map, store search links of spec 9, the update notice and the release (plan 6).

## 3. The card dialog's header

Layout, always editable (no edit mode):

```
┌─────────────────────────────────────────────┐
│ ┌────────┐ [EHAM Schiphol_____________]  ✕ │
│ │  img   │ ICAO: [EHAM ×] [EHRD ×] [____]  │
│ │        │        EHGG  Groningen Eelde    │
│ └────────┘        EHGR  Gilze-Rijen        │
│ [Image ▾]   On ●━━                         │
├─────────────────────────────────────────────┤
│ Packages                                    │
├─────────────────────────────────────────────┤
│ Built-in · Airports · Tags                  │
├─────────────────────────────────────────────┤
│ GSX                                         │
└─────────────────────────────────────────────┘
```

- **Name:** a text box. The text is trimmed, then saved (`CardEditor.Rename`) on Enter, when focus leaves the box, when the dialog closes by ✕ or a click outside, and when the dialog switches to another card (sections 5 and 6); the save happens before the dialog closes or changes card. Nothing is saved unless the user changed the text since the box was last filled from the card, so clicking through the box never pins the computed name, even when a scan changes the computed name meanwhile. An empty box clears the user's name, so the computed name returns. Esc is the one exit that drops text not yet saved; it closes the dialog as before. A save that is refused when the dialog closes or switches card (a locked `userdata.db`) still closes or switches, and its message is shown as a banner line, like a refused drop (4.2).
- **A new board while typing:** the dialog refreshes on every board change (watcher, F5, the user's own edits). A text box that has focus keeps its text, unless the card is gone from the board: then the dialog closes and the unsaved text is dropped, since there is no card left to save it on; it is refreshed when it loses focus.
- **Image:** the button "Image ▾" opens a menu with one item per Package of the card that has a thumbnail (thumbnail and display name), then "Choose file…" (an open-file dialog for `.jpg`, `.jpeg`, `.png`; only the path is stored, the file is never copied or moved), then "Automatic" (clears the user's image). `CardEditor.SetImage`. A picked file that is gone later shows the placeholder of the card's tab, as on the wall. Core: `CardPackage` gets the Package's `ThumbnailPath`, which the menu needs.
- **ICAO code field (Scenery cards only):** the card's ICAO codes as chips, each with ×, which runs `CardEditor.RemoveCode`. A text box after the chips suggests known Airports with their names, from `ContentAnalysis.Airports` through the existing `AirportSuggestions.Find` (codes starting with the text first, then names containing it, at most 10), so typing "Schip" offers EHAM. The first suggestion is selected whenever the list opens or changes; Up and Down move the selection. Enter adds the selected suggestion while the list is open, so "Schip" + Enter adds EHAM. Esc closes the list before it closes the dialog, and Enter with the list closed (or with no suggestion) adds exactly the typed text, so "YALR" + Esc + Enter adds YALR even when YALR1 is known (Rickard, 2026-11-09). A click on a suggestion adds it. Adding runs `CardEditor.AddCode`, which also moves the card to the Airports tab (MVP spec 6.5); the dialog stays open on the card. The outcome is shown under the field: `Invalid` → "Use 3–5 letters or digits."; `Taken` → "EHAM is already on EHAM Schiphol." (the code and the holder's name), or "EHAM is already on another card." when the holder is not on the board. The text box is cleared only when the code is added; a refusal keeps the text.
- **Provisional cards:** the header is read-only (spec 6.5).
- **While MSFS runs:** every header edit works, since none of them touches Community.

## 4. Split and drag and drop

### 4.1 Split

- Every Package row gets a **Split** button with the Fluent icon `ArrowSplit20` before the text (WPF-UI `ui:SymbolIcon`, from microsoft/fluentui-system-icons; the app has no other icon set).
- Shown only when the card has at least two Packages that are not GSX profiles, and never on a provisional card. GSX profiles are not Package rows (section 6) and are never split. Core changes to match: `Split` refuses a card with fewer than two non-profile Packages (today it counts profiles too), so splitting `orbx-eham` off a card that holds only it and `EHAM-xyz.ini` cannot leave the scenery on a codeless card.
- Split gives the Package a new card of its own in the same menu section and tab, with no ICAO code and the old card's removed ICAO codes (spec 6.5 "Move out"). The dialog stays on the old card; the new card shows on the wall at once.
- Core: `CardEditor.MoveOut` is renamed `Split`, so one name is used in the UI, the spec and the code. Split moves a Package out after a merge, but does not take back the ICAO codes the merge added (4.2).

### 4.2 Drag and drop

WPF's built-in `DragDrop`; no new dependency.

1. A drag starts when the pointer moves more than the system drag distance with the button down on a card. A drag never opens the dialog; a plain click opens it as today.
2. **Drop on another card** on the wall → **Merge** (`CardEditor.Merge`) after a confirmation: "Merge EHRD Rotterdam into EHAM Schiphol?" with Yes and No, through the same confirm prompt as Remove in 5b. No changes nothing. The wall shows one section and tab, so the target is always in the dragged card's section and tab. A drop on the dragged card itself does nothing. The confirmation is there because a merge cannot be fully undone (Rickard, 2026-11-09): Split moves a Package back out, but the ICAO codes, removals and pinned name and image the merge gave the target stay and are fixed by hand.
3. **Drop on a tab header** of the current menu section → move to that tab (`CardEditor.MoveToTab`). A drop on the card's own tab does nothing.
4. **Drop on Scenery, Aircraft or Other** in the left menu → move to that section (`CardEditor.MoveToSection`); the card lands on the tab spec 6.4 gives. A drop on the card's own section does nothing. Moving a card that has ICAO codes out of Scenery asks first, for the same reason as a merge: "Move EHAM Schiphol to Other? It loses its ICAO codes." with Yes and No; moving it back does not restore its removed codes or codes added by hand. Settings is not a drop target.
5. A valid target gets an accent border while the pointer is over it; everywhere else the pointer shows "no drop".
6. Provisional cards cannot be dragged and accept no drop.
7. Tabs do not open while hovering during a drag. Merging cards on two tabs takes two drags (move, then merge), as spec 6.5 says.

Merge, moves and Split work while MSFS runs: they change only the app's data. After a drop on a tab header or a menu section the wall stays on the current section and tab; the card leaves the view.

A drop that is refused changes nothing and shows its message as a line in the banner: a locked `userdata.db` gives its usual message, and a dragged or target card that a scan removed meanwhile gives "The card is no longer there." The banner is where a locked file is already reported during a run (plan 5a D9). The line is cleared when the next user change starts or a new board is shown; scan progress updates keep it.

## 5. The rest of the card dialog

Below the Package rows, in this order. A part is shown only when it has content.

1. **Built-in parts:** listed under the Packages, dimmed, with the lock icon (`LockClosed16`): the display name and the package name with "(built-in)". Locked Package rows (spec 6.3) are dimmed too, besides the reason 5b already shows.
2. **Airports:** one row per ICAO code that the card or one of its Packages has, with the Airport's name ("EHAM Amsterdam Schiphol"); a code no record knows is shown without a name. Names are found as the card's own name is: from `ContentAnalysis.Airports`, falling back to the records remembered for a Not found Package, so a Not found "OrbX EHAM 1.3" still gives "EHAM Amsterdam Schiphol". On the row:
   - the label **Replaces built-in** when a Package of the card replaces the built-in Airport;
   - **other cards at the same Airport** (`Board.CardsAt`, without this card) as clickable names; a click switches the dialog to that card.
3. **Tags:** one comma-separated line, read-only. Tags come from the folder structure and aircraft type designators, so they are not edited in the dialog.


## 6. The GSX section (display only)

Shown when the card holds a GSX profile Package or has GSX facts (`Card.GsxAirports` is not empty), so a profile is never hidden: also on a provisional card, a card moved out of Scenery, or a card without ICAO codes. The parts that come from `Card.GsxAirports` (Two profiles, search icons, shipped profiles, profiles on other cards) appear only when it is not empty; it is empty when the card has no ICAO codes or the GSX folder is missing or cannot be read (spec 8; the banner says why). Rows are grouped per ICAO code in the order of the card's codes; profiles whose ICAO code the card does not have come last.

```
GSX
  EHAM-xyz                          ⚠  (yellow: two active for EHAM)
  EHAM-abc                          ⚠
  eham-old                             (dimmed: .off)
  EHAM-7g8  shipped with scenery       (dimmed, not counted)
  EHRD  🔍                             (no active profile for EHRD)
```

- **GSX profile Packages are listed only here**, not among the Package rows; the Packages part lists the card's other Packages. A profile that is Not found (its file was deleted) is dimmed with "Not found" and keeps its Remove button, as on a Package row in 5b.
- One row per profile Package of the card, its name only (the Package's display name, the file name without extension). No `.py` files and no file details.
- **Active profiles on another card:** an active profile for one of the card's ICAO codes that sits on another card (automation may give two cards the same code, MVP 6.5) also gets a row, with "on <card name>" as a clickable name that switches the dialog to that card, like the other cards of section 5. So the dialog always shows the profiles behind the wall's Two profiles warning.
- **Two active profiles:** more than one active `.ini` for one ICAO code (`GsxAirport.Active`, on any card) puts a yellow `Warning` icon at each of those profile rows, with the tooltip "Two GSX profiles for EHAM".
- **Disabled (`.off`)** profile Packages and **shipped** profiles are dimmed and never counted. A `.off` variant of an active profile (both `lpma.ini` and `lpma.off`) is not a row of its own.
- **A shipped profile** is a row named after its `.ini` file without extension, labelled "shipped with scenery". A shipped profile whose name is already in the GSX folder (`ShippedProfile.InGsxFolder`) is not shown when the installed profile has a row here (on this card, or active on another card); otherwise it is shown, so it never vanishes without trace.
- **An ICAO code with no active profile:** the code and an icon-only search button (Fluent `Search20`) with the tooltip "Search flightsim.to for EHRD GSX profiles"; it opens `GsxAirport.SearchLink` in the browser. No text on the button.
- **Errors** are never shown in a message box or popup (Rickard, 2026-11-09). The section only displays, so its one error is a search link the browser could not open: a yellow `Warning` icon then appears after that search button, with the error as its tooltip, until the dialog shows another card.

### 6.1 Shipped profiles are not active (spec 15 closed)

GSX Pro with the SU10 Navdata API option reads custom airport profiles only from `%APPDATA%\Virtuali\GSX\MSFS` and never reads Community. FSDT forum, Virtuali, quoting the GSX manual page 83: "It also means GSX Custom profiles will ONLY work from this folder: %APPDATA%\Virtuali\GSX\MSFS. Custom profiles which were previously distributed by a scenery developer in the scenery package itself won't be used anymore", and "you need to be sure you have only ONE .INI for a given ICAO". This matches `Couatl.err` on Rickard's machine and Dave's GSX Profile Manager, which reads only the GSX folder and installs downloads by copying them there. The app assumes the option is on. Shipped profiles stay as spec 8 has them: listed, not active, not counted for Two profiles.

## 7. Deviations from the MVP spec and issue #23

- **Merge asks for confirmation** (Rickard, 2026-11-09); MVP 6.5 merged on the drop. A move out of Scenery of a card with ICAO codes asks too, for the same reason.
- **"Move out" is called "Split"** everywhere (UI, spec, `CardEditor.Split`). Rickard, 2026-11-09: rename it as the opposite of "Merge".
- **No copy of a shipped profile in 5c.** Issue #23 and spec 8 had a copy action with the Two profiles question. Rickard, 2026-11-09: there is only on/off for a profile, and on/off is not part of 5c. `GsxProfileCopier` stays unused until on/off for profiles is designed.
- **GSX section shows no file details** (`.py` files, disabled variants per file) and the search link is an icon only.
- **Two profiles is shown at each profile's name**, not as one line per Airport, in the dialog. The wall's mark and warning are unchanged.
- **GSX profile Packages move from the Package rows to the GSX section** (5b lists them as locked Package rows).
- **Shipped profiles:** the label is "shipped with scenery" (spec 8: "shipped with scenery, not installed"), and one already in the GSX folder is not shown instead of being marked "already in the GSX folder".
- **Split needs two Packages that are not GSX profiles** (MVP 6.5 "Move out" counted every Package).
- **Suggestions match Airport names too** (the existing `AirportSuggestions.Find`), not only codes.
- **Spec 15 is closed:** shipped profiles are not active (6.1).

## 8. Architecture

- **Session:** `IAppSession` gets one method per edit (rename, set image, add code, remove code, split, merge, move to tab, move to section), each through the existing `AppSession.Change(...)` path: run under the session lock, publish the board it leaves, return the message to show or `null`. `AddCode` returns its `CodeOutcome` so the dialog can word a rejection. The session also exposes the known Airports for suggestions.
- **Dialog:** the card dialog is split into four `UserControl`s with their view-model parts: header, Package rows, info, GSX. The dialog view model stays the owner of the card and of `Update(board)`.
- **Drag and drop:** `CardView` starts the drag with the card's `CardRef`; the card wall, the tab headers and the left menu handle `DragOver`/`Drop` and call the main view model, which calls the session.
- **Errors:** as in 5b, a change returns a message instead of throwing. The dialog shows it as `Message`; a drop on the wall, and a name save refused when the dialog closes or switches card (section 3), shows it in the banner (4.2). A locked user-data file is reported the same way.

## 9. Testing

- Core: the rename `MoveOut` → `Split` keeps its tests, and the new guard (fewer than two non-profile Packages) gets one; `CardPackage.ThumbnailPath` gets a builder test.
- App: the view-model logic of all four dialog parts and of the drop targets (which drops are valid, which session call they make) is tested with a fake `IAppSession`, as 5b does.
- Drag and drop and the dialog's look are checked by hand on the development machine with `--data-dir` and the generated test folder (`tools/dev-check/New-TestPackages.ps1`).
