# Plan 5b: turning cards on and off

Design for the second step of plan 5. Issue #22 held all of the rest of plan 5; on 2026-11-03 Rickard split it in three, so each part gives him something he can run:

- **5b (this):** the app changes Community. One switch per card, Remove, sim detection, the sync dialog with Stale re-pointing, and a banner line for Packages that are on but Not shown. After 5b Rickard can stop using MSFS Addons Linker.
- **5c (later):** editing cards: name, image, the ICAO code field, Move out, drag and drop (merge and move), the rest of the card dialog (tags, GSX section with the copy action, other cards at the same Airport, search links).
- **5d (later):** Settings: the Packages root, adding, changing and removing Libraries, the Not shown list, the Community list with removal of Broken links, URL templates, threshold, exclusion list and stop-list, and the ~1 s window target.

The full product is described in `docs/superpowers/specs/2026-10-25-mvp-design.md` (the spec); this document only says what 5b takes from it, where it deviates, and why. 5b builds on the plan 5a app shell (`docs/superpowers/specs/2026-10-30-plan-5a-app-shell-design.md` in git history, `ANALYS.md` plan 5a D1–D14).

## Why

In 5a the app only shows the collection and follows Community silently while Addons Linker changes it. 5b is the step where the app is trusted to change Community itself. Rickard switches straight over: once 5b works he stops using Addons Linker, so changes made by other tools while the app runs become rare (an Orbx Central update, an installer).

**Guiding constraint (Rickard, 2026-11-03): build only what is used now.** The app is usually open for a short while (open, turn a few cards on or off, close, start the sim), so 5b leaves out everything that only matters when the app runs for long next to the sim or other tools.

## Goals

1. **The card dialog** opens on a click on a card (an overlay; Esc, ✕ or a click outside closes it). It shows the card's name and ICAO codes as text, **one switch for the card**, and one row per Package: display name, folder name, path, Library, which copy is active when it is a Duplicate copy, and the reason when the Package is locked. Package rows have no switch of their own.
2. **The card's switch** turns every switchable Package of the card on or off (`CardSwitch.All`) through the apply path (`ChangeApplier.SetChoices`). Locked Packages are skipped, and the dialog says how many ("2 skipped (locked)"). A Mixed card shows the switch as off; one click turns the whole card on. There is no Apply button: the change is made at once and the wall shows the result straight away by re-reading Community, without a content rescan.
3. **Remove** on a Package row that is Not found, or held by no registered Library (`PackageRemover`), after a confirmation ("Remove `<name>` and forget it?").
4. **The sync dialog** (spec 7.4) replaces 5a's silent following (`CommunityFollower.Follow`). Every discovery (start, watcher, F5) calls `SyncCoordinator.Start` instead: with no rows, nothing changes; with rows, the sync dialog opens over the wall. On first run `Start` returns only the Stale rows (spec 7.1 step 5).
5. **Sim detection:** while MSFS runs, the card's switch and Remove are disabled with "Close MSFS to change add-ons".
6. **Banner:** a new line "N packages are on but not shown" for Packages that are on in Community and Not shown (the gap carried over from 5a, comment on #22).

## Deviations from the spec (Rickard, 2026-11-03)

Each is recorded in `ANALYS.md` with its reason.

- **No switch per Package** (spec 6.3, 10). The dialog has one switch, for the card. Turning single Packages of a card on and off, and a switch for all cards of a tab or the whole wall, are Later.
- **No choice of active copy** (spec 6.1, 10). The default rule of spec 6.1 always picks the active copy; the dialog only shows which one it is.
- **No Pending** (spec 6.8). Instead of recording changes while the sim runs and applying them when it exits, the app refuses them: `ChangeApplier` refuses every link change while the sim runs (a new rejection reason), so nothing new is ever added to the pending set. The app does not watch for the sim to exit. "Next sim start" can only show from data an older version left behind, and `SyncCoordinator.Start` still flushes such a set on the next start.
- **One sync dialog, one code path** (spec 7.4). There is no separate notice for changes made while the app runs and no "don't ask again this session": the watcher already starts a scan when Community changes, and that scan's discovery runs the same `Start` as at launch.
- **The sync dialog closes only with Apply** (spec 7.4 doesn't say). Its defaults are safe: Keep Community keeps what is already true, and a Moved row re-points to the Package's new location.
- **No sync dialog while the sim runs.** Moved rows and Restore app change links, which isn't allowed then. The banner says "Community changed outside the app. Close MSFS, then press F5 to review." The rows are recomputed on every discovery, so nothing is lost.
- **Sim process names** (spec 6.8). `SimProcessMonitor` takes a list of process names instead of one. The only known name is `FlightSimulator` (the Steam edition, `FlightSimulator.exe`). The Microsoft Store edition's process name is unknown: a forum thread (https://forums.flightsimulator.com/t/where-in-which-folder-is-the-flight-simulator-exe-file/572117) names only its launcher `gamelaunchhelper.exe`. It is an open question in `ANALYS.md`, added to the list when someone with that edition has checked.
- **No sim-machine check** (issue #22 step 4). The write path doesn't depend on what is in a folder: junction creation and removal are tested against temporary folders, the classification of Rickard's real Community was checked in 5a, and MSFS loading a junction is what Addons Linker relies on every day. The only thing that needs the sim machine is the process name, a one-line check.

## How it fits together

- **The write path lives in `AppSession`, under the same lock as the scans.** `AppSession` gets methods for the card's switch, Remove and resolving the sync dialog. The lock guarantees that a link change and a discovery never run at the same time.
- **Own changes need no suppression.** `ChangeApplier` updates the snapshot entries of the Packages it changes in the same write as the choice. When the watcher then fires and the next discovery compares Community with the snapshot (`Reconciler.Diff`), the app's own changes give no rows. Only a change made by someone else does.
- **The sim check** happens when the dialog opens, and again right before every link change, inside `ChangeApplier`. A dialog left open while MSFS starts doesn't update its switch, so the check right before the change is the one that counts: a click then is refused with the same text, and the switch flips back.
- **5a's guarantee is turned around.** 5a had a test proving the app's composition can't write to Community. 5b replaces it with a test proving that every Community write goes through `ChangeApplier` (and `PackageRemover`, which uses it).

## Errors and edge cases

- **A refused change** shows its reason on the Package's row. When no Package of the card changed, the switch flips back; when some did, the card shows Mixed. The reasons:
  - Name taken: "Name taken by `<entry>`" (spec 6.2).
  - Offline: "Library not found".
  - The sim runs: "Close MSFS to change add-ons".
- **A failed link change** (an IO error) shows its message in the dialog. Nothing is saved for that Package (spec 6.8, write order); Packages of the same card that did change are saved.
- **A locked `userdata.db`** refuses the change before any link is touched (already so in `ChangeApplier`), and the message is shown in the dialog.
- **Remove** while the sim runs is disabled; a Broken link that can't be removed leaves the Package as it was (already so in `PackageRemover`).

## Non-goals (5b)

- Switches per Package, a choice of active copy, switches for several cards at once.
- Pending, flushing on sim exit, and watching the sim process.
- Everything listed under 5c and 5d above; the map, store search, update notice and the release (plan 6).

## Constraints

- .NET 10, WPF + WPF-UI (ADR 0003), dark theme only, English UI only.
- New rules go to Core with tests; the WPF project only shows `Board` and calls services.
- MSFS 2024 stays out; sim-specific parts (the process names) stay in `AddonManager.Sim2020`.
- CRLF line endings; MIT; no AI attribution.

## Testing

- **Core:** `ChangeApplier` refuses every link change while the sim runs, and adds nothing to the pending set; the banner line "on but not shown"; a Mixed card's switch turns the card on.
- **App:** the card's switch calls the apply path and the wall shows the change; an own change gives no sync rows, also when the watcher fires after it; a change made by someone else opens the sync dialog; while the sim runs there is no dialog and the banner line shows instead; the turned-around composition test.
- All against fakes and temporary folders, never Rickard's real Community.

## How Rickard checks 5b

1. **On the dev machine** (about 5 minutes; Kjell asks right before starting any GUI): a generated test folder holds a Packages root (`Community`, `Official`) and a couple of Library Packages. The app runs with `--data-dir`, so the real `userdata.db` and Community are never touched. Turn a card on and off, change a link by hand in Explorer and see the sync dialog. Then start a renamed copy of a harmless exe (for example `notepad.exe` copied to `FlightSimulator.exe`): the switch is disabled with its reason, and works again once the copy is closed.
2. **On the sim machine** (about 30 seconds), with MSFS running: `Get-Process FlightSimulator` returns a row.

## Left to the builder

- How the dialog overlay and the sync dialog are built (WPF-UI controls, layout), within the BOB look of spec 10 and the dark theme.
- How the wall is refreshed after a change (re-read Community and rebuild the board from the last input, or another way that needs no content rescan).
- Whether the deferral code in `ChangeApplier` is deleted or left unused, as long as no change made by 5b ever becomes Pending.
- Which board `SyncCoordinator.Start` gets at a discovery before the first board exists (it only needs the Not found and Not shown names).
