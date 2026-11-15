# Plan 5d: changing Libraries in Settings

Design for the fourth step of plan 5 (issue #24). The full product is described in `docs/superpowers/specs/2026-10-25-mvp-design.md` (the spec); this document only says what 5d takes from it, where it deviates, and why. 5d builds on 5a (app shell, first run), 5b (switches, Community always wins) and 5c (card editing). An independent review on 2026-11-15 found two blockers and six important gaps in the first draft; this version is built around them.

## Why

After first run the Libraries can't be changed: Settings only lists them, and the first-run screen says "In this version Libraries can't be changed after you press Start." A Library that was forgotten, moved or added later (a new drive, a new download folder) needs a fresh `userdata.db` today. 5d lets Rickard add and remove Libraries per menu section from Settings.

**Guiding constraint (Rickard, 2026-11-03): build only what is used now.** Issue #24 listed six parts of Settings. On 2026-11-15 Rickard chose Libraries only for 5d. Most of the other parts already have their logic in Core and lack only a Settings view (see "Out of scope").

## The approach: save the list, then run

Every Add and Remove saves the list of Libraries and then starts an ordinary incremental run, exactly like F5. The run does the rest through the paths that already exist:

- its discovery walks the **new** list, so the `PackageIndex` covers every registered Library (an index with only the new folder would classify a link to a Duplicate copy in an older Library as External and turn that Package off);
- `SyncCoordinator.Start` adopts Community at that discovery (5b: Community always wins), which brings in the Packages of a new folder: linked ones on, unlinked ones off, a link to another copy than the stored active copy makes the linked copy active, and Stale entries of Packages that are on are re-pointed (or wait while MSFS runs, 5b D10);
- the run's completion places new Packages on cards (spec 6.5), turns Packages that no folder holds any more into locked ones, rebuilds the file watcher from the new list and refreshes the wall.

This replaces the first draft's idea of settling cards at once from "the last completed input": the session keeps no completed input while a run is going (every discovery replaces it with one that `Settlement.Apply` refuses), and a Remove cannot be shown correctly from the old index (its links would still classify as Managed). One path also survives a crash between saving the list and adopting, which two separate steps would not. The price is that the wall changes after the discovery, a few seconds on Rickard's machine, instead of at once.

## Goals

1. **Settings shows the Libraries per menu section, editable.** The read-only list on the Settings page is replaced by the three sections of the first-run screen: Scenery, Aircraft and Other, each with its folders, a Remove button per folder and "Add Library…". Below them, the link coverage is shown as on the first-run screen: "N of M links covered", the Not covered list and the Not counted list (spec 7.1 step 3, `LinkCoverage.Measure` over the registered Libraries). The Packages root and the database file stay on the page as read-only text, as in 5a.
2. **Changing a Library** means adding or removing the same folder under another section; there is no separate edit. The same folder may be listed under several sections (spec 6.1).
3. **Every Add and Remove is saved at once** (spec 11), with no Apply button, and the wall follows without the user pressing F5 (the run described above).
4. **Add** (`SyncCoordinator.RegisterLibrary` or a plain list edit; the implementation plan picks one, since the adoption is the discovery's job):
   - A folder is refused with a reason by the same rule as on first run (`FirstRunLibraries.Refusal`: it is, lies inside or holds Community or `<Packages root>\Official`). A folder already listed in that section is ignored, as on first run.
   - A new folder is added last, so the order that decides the default active copy of a Duplicate copy (spec 6.1) puts it after the older ones. A known folder added to another section keeps its position.
   - Adopting happens at the run's discovery (above), not in `RegisterLibrary`, so a new folder's Packages are adopted with an index that has them. `RegisterLibrary` with the old index would adopt nothing for them.
   - **One difference from spec 6.1 "Registering a Library later":** the adoption is `Start`'s, so a Not shown Package that is off keeps its Stale entry, as in 5b. Spec 6.1 is changed to say so.
5. **Remove** (`SyncCoordinator.UnregisterLibrary`): nothing is deleted, nothing in Community changes, and the Packages stay on their cards with all their user data (spec 6.1 "Removing a Library"). When the folder has no section left it is dropped from the list. **No confirmation:** removing is undone by adding the folder again (Kjell's call, in line with 5c: only merge asks). Effects, all computed by the run, that the dialog and tests must expect:
   - Its Managed entries become External entries, and a Package on a card that no registered Library holds any more is locked: linked ones External, unlinked ones off, "not in a registered Library" (spec 6.1). A Package whose last known path no longer exists is Not found.
   - A Package without a card that no registered Library holds is forgotten (it has no user data to keep, spec 6.4).
   - A linked Not shown Package of the folder becomes an External entry, and an External entry gets a card automatically (spec 6.4), so a Remove can create new locked cards.
   - A Duplicate copy whose active copy was in the folder: when a Fixed or External entry now holds its name it is "held by an External entry" (spec 6.1), otherwise the default rule picks another copy.
   - Removing a Library that is not found (an unplugged drive) turns its carded Packages Not found and forgets the rest.
6. **Nested Libraries** (spec 6.1) stay allowed.
   - Removing a folder that another registered Library still holds (an inner `C:\Sim\Addons\Scenery` under `C:\Sim\Addons`) changes nothing for its Packages: they belong to the outer Library, nothing becomes External or locked.
   - Adding a folder inside a registered one adds a new, last-ranked Library. The shown Library of a Package becomes the innermost one (spec 6.1). Adding a nested folder changes no stored choice and no stored active copy. The plan proves this with a test; if the test finds that the default active copy of a Duplicate copy can change, the plan keeps the stored active copy.
7. **Removing the last Library is allowed.** First run refuses an empty list, but the app is no longer in first run: a snapshot exists, so 5a D13 applies and the app starts as usual and shows only the cards for Community entries and GSX profiles. Settings shows the empty sections and the Add buttons.
8. **While MSFS runs** Add and Remove still work, since they change only the app's data. Re-pointing Stale entries waits as in 5b, with the existing banner line "Some links wait for MSFS to close. Close MSFS, then press F5."
9. **While a run is going** (a watcher run, F5, an earlier Add or Remove) an Add or Remove still works: the session saves the list and requests another run, and `ScanService` merges a queued request with the newest Libraries. Add and Remove are offered when the session is Scanning or Ready, the phases a run may start in.
10. **Errors.** A refused folder and a locked or failing `userdata.db` are shown as text below the sections, as on the first-run screen. A failed change **restores the list as it was** in memory and saves nothing, as `CompleteFirstRunAsync` does (it is not kept in memory with an "unsaved" mark as the card edits do, because the list decides what the next run walks). Failures of a re-point during the run are shown by the existing error line of 5b ("N links could not be repaired: …"), no new place.
11. **The Settings list is read from a snapshot.** `MainViewModel.RefreshSettings` reads `session.State.Libraries` outside the session's lock and relied on "Libraries never change after first run" (5a D6 and D11). 5d replaces that: the session publishes a snapshot of the Libraries (for example on `SessionView`), taken under the lock, and Settings reads that.
12. **The coverage is measured from the latest discovery's Community entries**, not from `ReadCommunity()`, which reads Community once and keeps it until the install changes (`AppSession.ReadCommunity`). So the figures follow a link another tool changes. On the first-run screen `ReadCommunity()` stays, since nothing has been discovered yet.
13. **The file watcher** is rebuilt by the run's completion from the new Libraries (5a D8). After a Remove the removed folder is no longer watched once that run completes, and after an Add the new folder is watched from then on.
14. **The first-run screen** loses its note "In this version Libraries can't be changed after you press Start." 5a D6 ("After Start nothing changes them") and D11 (Settings read-only) are replaced by this plan, and `ANALYS.md` says so.

## Reuse (Kjell's call)

The first-run screen and Settings show the same thing: Libraries per section with Add and Remove, refusals and the link coverage. The `LibrarySection` view is already a shared control; the coverage computation and the add/refuse logic of `FirstRunViewModel` are shared rather than copied. The difference is when changes take effect: on the first-run screen they are collected until Start, in Settings each one is saved at once.

## Vocabulary

The document uses the glossary's terms: Library, Package, Managed, External, Stale entry (a Community link whose target is gone while a Package of that name is found), Duplicate copy, Not shown, Not found.

## Out of scope

Moved to a new issue, recorded in `ANALYS.md` (Rickard, 2026-11-15: "Bara Libraries i 5d"). What exists today:

| Part | Exists | Missing |
|---|---|---|
| Not shown | `CardEditor.ShowIn`, `Board.NotShown`, the banner line for Not shown Packages that are on | The Settings list |
| Community | `CommunityClassifier`, `ChangeApplier.RemoveBrokenLink`, the banner line "Community: N Broken, N Stray" | The Settings list and removing a Broken link from it |
| Packages root | `TryUsePackagesRoot` (first run) | Changing it from Settings |
| Content settings | `ContentSettings` (threshold, exclusion list, stop-list), stored in `userdata.db` and used by the scan | A view to change them |
| ~1 s window target (spec 7.2) | Nothing | A measurement, then any fix |

Search URL templates (spec 9) depend on the store search links, which are plan 6.

## Testing

- Core's rules for registering and removing Libraries are already tested (`SyncCoordinatorTests`). Those tests build the index again after a change (`World.Input()`); they do not show what the app's session sees, so the new tests below run through the session.
- Session tests (with a fake scan service and a temporary `userdata.db`):
  - Add of a new folder saves the list, starts a run, and the run's discovery adopts the folder's Packages: linked on, unlinked off, a link to a Duplicate copy in an older Library keeps that Package on and its stored active copy.
  - Add of a known folder to a second section changes no stored choice and keeps the folder's position; the run gives the newly eligible Packages cards.
  - Remove from one of two sections keeps cards and user data; Remove of a folder's last section locks its Packages (External or off, "not in a registered Library"), keeps them on their cards, and a Package whose path is gone is Not found, not locked.
  - Remove of an inner folder that an outer Library holds changes nothing for its Packages; adding a nested folder changes no stored choice or active copy (a Duplicate copy case).
  - Removing the last Library gives a wall with only Community entries and GSX profiles.
  - Add and Remove during a running run request one more run with the newest list; Add and Remove work while the sim runs, and the Stale entry waits with the banner line.
  - A locked `userdata.db` and a failing save leave the in-memory list as it was and start no run.
  - The Settings snapshot follows every change; the coverage follows a Community change between two discoveries.
  - The watcher is rebuilt with the new folders after the run.
- Settings view model: refusals, a folder already in the section, the coverage text, Add and Remove disabled outside Scanning and Ready.
- A manual check on the development machine with `--data-dir` and the generated test folder (`tools/dev-check/New-TestPackages.ps1`): add a folder, see its cards appear; add it to a second section; remove it from one section and then from all, and see the cards stay, locked; add a nested folder and remove it again.

## Calls Kjell made (please check)

- Add and Remove save the list and start a run instead of settling cards at once, so the wall changes after the discovery (above).
- The coverage is shown in Settings too, not only on the first-run screen, so a forgotten folder is still noticed later.
- Remove asks no question.
- Add and Remove are allowed while MSFS runs and while a run is going.
- Removing the last Library is allowed.
- A failed save restores the list in memory, unlike the card edits.
- Nested folders stay allowed; a nested Add changes no stored active copy.
- The first-run screen and Settings share the Library section view, the add/refuse logic and the coverage computation.
