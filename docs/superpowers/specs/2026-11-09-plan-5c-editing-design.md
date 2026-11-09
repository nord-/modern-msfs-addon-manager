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

Out of scope: on/off for GSX profiles and copying a shipped profile into the GSX folder (a later decision, section 7); a switch per Package; Settings (5d); the map, store search links of spec 9, the update notice and the release (plan 6).

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

- **Name:** a text box. Saved on Enter or when focus leaves the box (`CardEditor.Rename`). An empty box clears the user's name, so the computed name returns. Esc closes the dialog as before; text not yet committed is dropped.
- **Image:** the button "Image ▾" opens a menu with one item per Package of the card that has a thumbnail (thumbnail and display name), then "Choose file…" (an open-file dialog for `.jpg`, `.jpeg`, `.png`; only the path is stored, the file is never copied or moved), then "Automatic" (clears the user's image). `CardEditor.SetImage`. A picked file that is gone later shows the placeholder of the card's type. Core: `CardPackage` gets the Package's `ThumbnailPath`, which the menu needs.
- **ICAO code field (Scenery cards only):** the card's ICAO codes as chips, each with ×, which runs `CardEditor.RemoveCode`. A text box after the chips suggests known ICAO codes that start with the typed text, with their Airport names, at most 8, from `ContentAnalysis.Airports`. Enter or a click on a suggestion runs `CardEditor.AddCode`. The outcome is shown under the field: `Invalid` → "Use 3–5 letters or digits."; `Taken` → "EHAM is already on EHAM Schiphol." (the code and the holder's name). An accepted code clears the text box.
- **Provisional cards:** the header is read-only (spec 6.5).
- **While MSFS runs:** every header edit works, since none of them touches Community.

## 4. Split and drag and drop

### 4.1 Split

- Every Package row gets a **Split** button with the Fluent icon `ArrowSplit20` before the text (WPF-UI `ui:SymbolIcon`, from microsoft/fluentui-system-icons; the app has no other icon set).
- Not shown on a card's only Package, on a GSX profile Package, or on a provisional card (the rules `CardEditor` already enforces).
- Split gives the Package a new card of its own in the same menu section and tab, with no ICAO code and the old card's removed ICAO codes (spec 6.5 "Move out"). The dialog stays on the old card; the new card shows on the wall at once.
- Core: `CardEditor.MoveOut` is renamed `Split`, so one name is used in the UI, the spec and the code. Split is the undo of a merge.

### 4.2 Drag and drop

WPF's built-in `DragDrop`; no new dependency.

1. A drag starts when the pointer moves more than the system drag distance with the button down on a card. A drag never opens the dialog; a plain click opens it as today.
2. **Drop on another card** on the wall → **Merge** (`CardEditor.Merge`): the wall shows one section and tab, so the target is always in the dragged card's section and tab. No confirmation; Split is the way back. A drop on the dragged card itself does nothing.
3. **Drop on a tab header** of the current menu section → move to that tab (`CardEditor.MoveToTab`). A drop on the card's own tab does nothing.
4. **Drop on Scenery, Aircraft or Other** in the left menu → move to that section (`CardEditor.MoveToSection`); the card lands on the tab spec 6.4 gives. A drop on the card's own section does nothing. Settings is not a drop target.
5. A valid target gets an accent border while the pointer is over it; everywhere else the pointer shows "no drop".
6. Provisional cards cannot be dragged and accept no drop.
7. Tabs do not open while hovering during a drag. Merging cards on two tabs takes two drags (move, then merge), as spec 6.5 says.

Merge and moves work while MSFS runs: they change only the app's data.

## 5. The rest of the card dialog

Below the Package rows, in this order. A part is shown only when it has content.

1. **Built-in parts:** listed under the Packages, dimmed, with the lock icon (`LockClosed16`): the display name and the package name with "(built-in)". Locked Package rows (spec 6.3) are dimmed too, besides the reason 5b already shows.
2. **Airports:** one row per ICAO code that the card or one of its Packages has, with the Airport's name ("EHAM Amsterdam Schiphol"); a code no record knows is shown without a name. On the row:
   - the label **Replaces built-in** when a Package of the card replaces the built-in Airport;
   - **other cards at the same Airport** (`Board.CardsAt`, without this card) as clickable names; a click switches the dialog to that card.
3. **Tags:** one comma-separated line, read-only. Tags come from the folder structure and aircraft type designators, so they are not edited in the dialog.

No new Core rules: `Card` already carries `BuiltIns`, `Tags`, `CardPackage.Codes` and `CardPackage.ReplacesBuiltIn`; Airport names come from `ContentAnalysis.Airports`.

## 6. The GSX section (display only)

Shown on Scenery cards with ICAO codes, from `Card.GsxAirports`. When the GSX folder is missing or cannot be read, the section is not shown (spec 8; the banner says why).

```
GSX
  EHAM-xyz                          ⚠  (yellow: two active for EHAM)
  EHAM-abc                          ⚠
  eham-old                             (dimmed: .off)
  EHAM-7g8  shipped with scenery       (dimmed, not counted)
  EHRD  🔍                             (no active profile for EHRD)
```

- One row per profile, its name only. No `.py` files and no file details.
- **Two active profiles:** more than one active `.ini` for one ICAO code puts a yellow `Warning` icon at each of those profiles, with the tooltip "Two GSX profiles for EHAM".
- **Disabled (`.off`)** and **shipped** profiles are dimmed and never counted. A shipped profile is labelled "shipped with scenery".
- **An ICAO code with no active profile:** the code and an icon-only search button (Fluent `Search20`) with the tooltip "Search flightsim.to for EHRD GSX profiles"; it opens `GsxAirport.SearchLink` in the browser. No text on the button.
- **Errors** are never shown in a dialog. An error lights the warning icon and its tooltip says what went wrong.

### 6.1 Shipped profiles are not active (spec 15 closed)

GSX Pro with the SU10 Navdata API option reads custom airport profiles only from `%APPDATA%\Virtuali\GSX\MSFS` and never reads Community. FSDT forum, Virtuali, quoting the GSX manual page 83: "It also means GSX Custom profiles will ONLY work from this folder: %APPDATA%\Virtuali\GSX\MSFS. Custom profiles which were previously distributed by a scenery developer in the scenery package itself won't be used anymore", and "you need to be sure you have only ONE .INI for a given ICAO". This matches `Couatl.err` on Rickard's machine and Dave's GSX Profile Manager, which reads only the GSX folder and installs downloads by copying them there. The app assumes the option is on. Shipped profiles stay as spec 8 has them: listed, not active, not counted for Two profiles.

## 7. Deviations from the MVP spec and issue #23

- **"Move out" is called "Split"** everywhere (UI, spec, `CardEditor.Split`). Rickard, 2026-11-09: rename it as the opposite of "Merge".
- **No copy of a shipped profile in 5c.** Issue #23 and spec 8 had a copy action with the Two profiles question. Rickard, 2026-11-09: there is only on/off for a profile, and on/off is not part of 5c. `GsxProfileCopier` stays unused until on/off for profiles is designed.
- **GSX section shows no file details** (`.py` files, disabled variants per file) and the search link is an icon only.
- **Two profiles is shown at each profile's name**, not as one line per Airport, in the dialog. The wall's mark and warning are unchanged.
- **Spec 15 is closed:** shipped profiles are not active (6.1).

## 8. Architecture

- **Session:** `IAppSession` gets one method per edit (rename, set image, add code, remove code, split, merge, move to tab, move to section), each through the existing `AppSession.Change(...)` path: run under the session lock, publish the board it leaves, return the message to show or `null`. `AddCode` returns its `CodeOutcome` so the dialog can word a rejection. The session also exposes the known Airports for suggestions.
- **Dialog:** the card dialog is split into four `UserControl`s with their view-model parts: header, Package rows, info, GSX. The dialog view model stays the owner of the card and of `Update(board)`.
- **Drag and drop:** `CardView` starts the drag with the card's `CardRef`; the card wall, the tab headers and the left menu handle `DragOver`/`Drop` and call the main view model, which calls the session.
- **Errors:** as in 5b, a change returns a message instead of throwing; the dialog shows it as `Message`. A locked user-data file is reported the same way.

## 9. Testing

- Core: the rename `MoveOut` → `Split` keeps its tests; `CardPackage.ThumbnailPath` gets a builder test.
- App: the view-model logic of all four dialog parts and of the drop targets (which drops are valid, which session call they make) is tested with a fake `IAppSession`, as 5b does.
- Drag and drop and the dialog's look are checked by hand on the development machine with `--data-dir` and the generated test folder (`tools/dev-check/New-TestPackages.ps1`).
