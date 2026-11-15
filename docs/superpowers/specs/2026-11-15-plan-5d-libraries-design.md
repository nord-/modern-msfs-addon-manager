# Plan 5d: changing Libraries in Settings

Design for the fourth step of plan 5 (issue #24). The full product is described in `docs/superpowers/specs/2026-10-25-mvp-design.md` (the spec); this document only says what 5d takes from it, where it deviates, and why. 5d builds on 5a (app shell, first run), 5b (switches, Community always wins) and 5c (card editing). Three independent reviews on 2026-11-15 shaped this version: the first found that cards can't be settled at once (below), the second traced the run path through the code and found the gaps around nested Libraries and the moment between saving and the next discovery, and the third found that a run already going finishes with the old list.

## Why

After first run the Libraries can't be changed: Settings only lists them, and the first-run screen says "In this version Libraries can't be changed after you press Start." A Library that was forgotten, moved or added later (a new drive, a new download folder) needs a fresh `userdata.db` today. 5d lets Rickard add and remove Libraries per menu section from Settings.

**Guiding constraint (Rickard, 2026-11-03): build only what is used now.** Issue #24 listed six parts of Settings. On 2026-11-15 Rickard chose Libraries only for 5d. Most of the other parts already have their logic in Core and lack only a Settings view (see "Out of scope").

## The approach: save the list, then run

Every Add and Remove changes only the stored list of Libraries, saves it, and then starts an ordinary incremental run, the same kind the file watcher starts (F5 runs a full scan; this does not). The run does the rest through the paths that already exist:

- its discovery walks the **new** list, so the `PackageIndex` covers every registered Library (an index with only the new folder would classify a link to a Duplicate copy in an older Library as External and turn that Package off);
- `SyncCoordinator.Start` adopts Community at that discovery (5b: Community always wins). For a Package of a new folder: a linked one gets an Off-but-linked row that Community wins, so it is turned on with the linked copy as its active copy; an unlinked one gives no row and stays off; one linked to a Duplicate copy in an older Library is Managed and keeps its stored active copy; one with a Stale entry gets a Moved row and is turned on and re-pointed, unless it is Not shown (the Not shown set comes from the board built from the new discovery);
- the run's completion places new Packages on cards (spec 6.5), turns Packages that no folder holds any more into locked ones, rebuilds the file watcher from the new list (5a D8) and refreshes the wall.

**A discovery from an outdated list is discarded.** `ScanService` gives a queued run the newest list, but a run already going keeps the list it started with. Its discovery would let `Start` repair a link into a folder that was just removed, and its completion would publish a wall that mixes the saved list with an index of the old one. So the session compares the discovery's Library list with the saved list, exactly: the same paths, in the same order, with the same sections (`LibraryRoot.Equals` compares only the path, so the comparison has to include the sections). A discovery that differs runs no `Start` and no `Settlement`, publishes nothing and doesn't open the gate of goal 7; the run that the change requested follows and does the work.

Why not settle cards at once: the session keeps no completed input while a run is going (every discovery replaces it with one that `Settlement.Apply` refuses), and a Remove can't be shown correctly from the old index (its links would still classify as Managed). One path also survives a crash between saving the list and the run: adoption is a diff at `Start`, so the next start does it. The price is that the wall changes after the discovery, a few seconds on Rickard's machine, instead of at once.

## Goals

1. **Settings shows the Libraries per menu section, editable.** The read-only list on the Settings page is replaced by the three sections of the first-run screen: Scenery, Aircraft and Other, each with its folders, a Remove button per folder and "Add Library…". Below them, the link coverage is shown as on the first-run screen: "N of M links covered", the Not covered list and the Not counted list (spec 7.1 step 3, `LinkCoverage.Measure` over the registered Libraries). The Packages root and the database file stay on the page as read-only text, as in 5a.
2. **Changing a Library** means adding or removing the same folder under another section; there is no separate edit. The same folder may be listed under several sections (spec 6.1).
3. **Every Add and Remove is saved at once** (spec 11), with no Apply button, and the wall follows without the user pressing F5 (the run described above).
4. **Add** is a list-only edit: it adds the folder, or the section to a known folder, and saves. It adopts nothing, accepts no snapshot entries and repairs no link; all of that is the discovery's job.
   - A folder is refused with a reason by the same rule as on first run (`FirstRunLibraries.Refusal`: it is, lies inside or holds Community or `<Packages root>\Official`). It is also refused when it doesn't exist or can't be read when it is added ("The folder can't be read: <path>"), so a share that is down is never registered by accident. Both checks are made by the session, so the first-run screen gets the existence check too. A folder already listed in that section is ignored, as on first run.
   - A new folder is added last; a known folder added to another section keeps its position.
   - **`SyncCoordinator.RegisterLibrary` loses its adoption branch.** With the old index a nested folder already has indexed copies (through the outer Library), so its adoption would turn Stale entries on and re-point them, including those of Not shown Packages that are off, and it would change more than the list, so a failed save could not be rolled back by restoring the list. It becomes a list edit (or is replaced by one), `RegisterOutcome` goes away, and the remarks on `RegisterLibrary` and `UnregisterLibrary` that promise `Settlement.Apply` on the last completed input are corrected.
   - **One difference from spec 6.1 "Registering a Library later":** the adoption is `Start`'s, so a Not shown Package that is off keeps its Stale entry, as in 5b. The spec's 5d note says so.
5. **Remove** (`SyncCoordinator.UnregisterLibrary`, already a list edit): nothing is deleted, and Community changes only through the repair of Stale entries that already exists (below). When the folder has no section left it is dropped from the list. Effects, all computed by the run, that the dialog and tests must expect:
   - When no other registered Library holds the folder, its Managed entries become External entries, and a Package on a card is locked: linked ones External, unlinked ones off, "not in a registered Library" (spec 6.1). A Package whose last known path no longer exists is Not found.
   - A Package without a card that no registered Library holds is forgotten (`Settlement.Forget`; spec 6.4).
   - A linked Not shown Package of the folder becomes an External entry, and an External entry gets a card automatically (spec 6.4), so a Remove can create new locked cards.
   - A Duplicate copy whose active copy was in the folder: when the link to it is now an External entry, the Package is "held by an External entry" (spec 6.1); otherwise the default rule picks another copy.
   - Removing a Library that is not found (an unplugged drive): its carded Packages become Not found, the rest are forgotten, and its Offline links become Broken (shown in the banner). **Exception:** when another registered Library holds a copy of the Package, the link becomes a Stale entry instead, and the run re-points it to that copy (5b D10, the same rule as for a moved add-on). Accepted and stated here, with a test.
   - Removing one section of a folder that keeps others can leave Packages without a card eligible for none of its sections: they become Not shown. Cards stay where they are (spec 6.4).
   - **No confirmation** (Kjell's call, in line with 5c: only merge asks). Adding the folder back undoes almost everything, because Community wins and the next discovery takes the choices back in, with these limits: the folder comes back last in the order (see goal 6 for what the order decides), each section has to be added back separately, and Packages that had no card were forgotten and start again from Community.
6. **Nested Libraries** (spec 6.1) stay allowed, and the order that decides the default active copy no longer depends on nesting.
   - **Rule change in Core:** a copy is ranked by the earliest registered Library that holds it, not by the innermost one (`PackageIndex` today ranks by the innermost Library, which a nested Add ranks last). This is what spec 6.1 says ("the first by the order its folder was first registered"). The Library shown for a Package in the dialog stays the innermost one, and so do its tags (spec 6.4).
   - With that rule, adding a folder inside or around a registered Library changes no default active copy, and neither add nor remove changes a stored choice.
   - **Exception for Remove:** removing an inner folder can change a default active copy when a third Library holding a copy ranks between the inner and the outer one (registered after the inner, before the outer). The copy then ranks by the outer Library, after the third. Accepted and stated here, with a test; a chosen active copy never changes.
   - Removing a folder that another registered Library still holds (an inner `C:\Sim\Addons\Scenery` under `C:\Sim\Addons`) leaves its Packages registered through the outer Library: nothing becomes External or locked. Their sections shrink to the outer Library's sections, so Packages without a card can become Not shown (goal 5).
   - Adding a folder around a registered Library (an outer `C:\Sim\Addons` around `C:\Sim\Addons\Scenery`) is a new folder: its other Packages are adopted by the discovery as in goal 4; the Packages already known keep their choices and default active copies.
7. **Between saving the list and the next discovery** the board and the index still describe the old list. After an Add or Remove the session **does not republish the board**, and **every card change is refused** (the switch, Remove, rename, image, ICAO codes, Split, merge, move) with "Libraries are being updated. Wait for the scan to finish, or press F5." One rule for all of them, because each one republishes the board from the latest input, which still mixes the saved list with the old index; the window is a few seconds while Rickard is in Settings. It also stops a switch from creating a junction into a folder that was just removed.
   - **The gate opens** only when a discovery whose list is exactly the saved one (the comparison under "The approach") has run `Start` without an error and its board has been published. A discovery that is discarded, a `Start` that throws and a run that fails before its discovery all leave it closed; the text tells Rickard what opens it.
   - Add and Remove themselves are not gated (goal 10).
8. **Removing the last Library is allowed.** First run refuses an empty list, but the app is no longer in first run: a snapshot exists, so `StartupRules.IsFirstRun` is false (5a D13) and the app shows only the cards for Community entries and GSX profiles. Settings shows the empty sections and the Add buttons.
9. **While MSFS runs** Add and Remove still work, since they change only the app's data. Re-pointing Stale entries waits as in 5b, with the existing banner line "Some links wait for MSFS to close. Close MSFS, then press F5."
10. **While a run is going.** Settings is only shown in the Ready phase (`MainViewModel.ShowSettings`), and the phase stays Ready during later runs (watcher, F5, an earlier Add or Remove). An Add or Remove during such a run saves the list and requests another run; `ScanService` merges a queued request with the newest Libraries, and the discovery of the run already going is discarded when its list is outdated (see "The approach").
11. **Errors.** A refused folder and a locked or failing `userdata.db` are shown as text below the sections, as on the first-run screen. A failed change **restores the list as it was** in memory and starts no run, as `CompleteFirstRunAsync` does; since Add and Remove only edit the list, that is a full rollback. Failures of a re-point during the run are shown by the existing error line of 5b ("N links could not be repaired: …").
12. **Settings reads a snapshot from the session.** `MainViewModel.RefreshSettings` reads `session.State.Libraries` outside the session's lock and relied on "Libraries never change after first run" (5a D6 and D11). Instead the session publishes, under its lock, a snapshot with the Libraries and the coverage report, and Settings is refreshed whenever that snapshot changes, not only on a new board or on entering Ready (`MainViewModel.Apply` today).
   - `SessionView` gets `Libraries` and `Coverage`; the session publishes a new view after every Add and Remove and whenever the coverage changes.
   - `IAppSession` gets `AddLibraryAsync(path, section)` and `RemoveLibraryAsync(path, section)`. Each returns the message to show (a refusal or a failed save), or null when the change was saved.
13. **The coverage follows Community as far as the session reads it.** The session keeps the Community entries it last read and measures the coverage from them: the latest discovery's, refreshed after every change it makes itself (a card switch, a repair), the way it already re-reads Community for the board (`WithCurrentCommunity`). `ReadCommunity()`, which reads Community once and keeps it until the install changes, stays only on the first-run screen, where nothing has been discovered yet. A change in Community made by another tool shows at the next discovery (the watcher or F5), not before.
14. **The first-run screen** loses its note "In this version Libraries can't be changed after you press Start." 5a D6 ("After Start nothing changes them") and D11 (Settings read-only) are replaced by this plan, and `ANALYS.md` says so.

## Reuse (Kjell's call)

The first-run screen and Settings show the same thing: Libraries per section with Add and Remove, refusals and the link coverage. The `LibrarySection` view is already a shared control; the coverage computation and the add/refuse logic of `FirstRunViewModel` are shared rather than copied. The difference is when changes take effect: on the first-run screen they are collected until Start, in Settings each one is saved at once.

## Vocabulary

The document uses the glossary's terms: Library, Package, Managed, External, Stale entry (a Community link whose target is gone while a Package of that name is found), Duplicate copy, Not shown, Not found.

## Out of scope

Moved to issue #29 (plan 5e), recorded in `ANALYS.md` (Rickard, 2026-11-15: "Bara Libraries i 5d"). What exists today:

| Part | Exists | Missing |
|---|---|---|
| Not shown | `CardEditor.ShowIn`, `Board.NotShown`, the banner line for Not shown Packages that are on | The Settings list |
| Community | `CommunityClassifier`, `ChangeApplier.RemoveBrokenLink`, the banner line "Community: N Broken, N Stray" | The Settings list and removing a Broken link from it |
| Packages root | `TryUsePackagesRoot` (first run) | Changing it from Settings |
| Content settings | `ContentSettings` (threshold, exclusion list, stop-list), stored in `userdata.db` and used by the scan | A view to change them |
| ~1 s window target (spec 7.2) | Nothing | A measurement, then any fix |

Search URL templates (spec 9) depend on the store search links, which are plan 6.

## Testing

- **Core.**
  - `PackageIndex`: a copy is ranked by the earliest registered Library that holds it; adding an inner or an outer Library changes no Package's order of copies; removing an inner folder with a third Library ranked between the inner and the outer one moves the copy after the third (the exception of goal 6).
  - `PackageCatalog` and the board: the Library shown for a nested Package and its tags stay the innermost one's.
  - `RegisterLibrary` (or its replacement) only edits the list; the existing `SyncCoordinatorTests` that expect adoption from it are rewritten to run the adoption through `Start` with an index walked from the new list.
  - `Start` after a list change, with an index walked from the new list (the existing tests build the index again with `World.Input()`): a new folder's linked Package is turned on with the linked copy active, an unlinked one stays off, one linked to a Duplicate copy in an older Library keeps its stored active copy; a Stale entry of an eligible off Package is re-pointed and the Package turned on, while a Not shown off Package keeps its Stale entry (the deviation from spec 6.1, as a pair).
- **Session** (fake scan service, temporary `userdata.db`):
  - Add saves the list, changes nothing else in the user data and starts a run; the run's discovery adopts as above.
  - Add of a known folder to a second section keeps its position; the run gives the newly eligible Packages cards.
  - Remove from one of two sections keeps cards and user data, and Packages without a card that lose eligibility become Not shown; Remove of a folder's last section locks its carded Packages (External or off, "not in a registered Library"), a Package whose path is gone is Not found, Packages without a card are forgotten, and a linked Not shown Package gets a new locked card.
  - Remove of a Library that is not found: carded Packages Not found, the rest forgotten, its Offline links Broken; when another registered Library holds a copy, the link is re-pointed to it instead.
  - Remove of an inner folder held by an outer Library changes no choice, no active copy and no lock; Add of an inner and of an outer folder changes no stored choice or default active copy (a Duplicate copy case).
  - Removing the last Library gives a wall with only Community entries and GSX profiles.
  - Between an Add or Remove and the next discovery, every card change is refused with the "being updated" text and the board is not republished; after the discovery they work again. A `Start` that throws and a run that fails before its discovery leave the gate closed.
  - A run held open across a Remove: its discovery from the old list is discarded (no `Start`, no repair into the removed folder, nothing published), and the queued run's discovery does the work. The same across an Add followed at once by a Remove of the same folder: no link into that folder is adopted or repaired.
  - A crash between save and run: save, dispose the session before discovery, start again, and the start adopts the new folder.
  - Add and Remove during a running run request one more run with the newest list; Add and Remove work while the sim runs, and the Stale entry waits with the banner line.
  - A locked `userdata.db` and a failing save leave the in-memory list as it was and start no run; Add of a folder that doesn't exist is refused, in Settings and on the first-run screen.
  - The Settings snapshot follows every change; the coverage follows a card switch the app makes and a new discovery, and a Community change by another tool shows only at the next discovery.
  - The watcher is rebuilt with the new folders after the run.
- **Settings view model:** refusals, a folder already in the section, the coverage text, refresh when only the Libraries snapshot changed.
- **Manual check** on the development machine with `--data-dir` and the generated test folder (`tools/dev-check/New-TestPackages.ps1`): add a folder and see its cards appear; add it to a second section; remove it from one section and then from all, and see the cards stay, locked; add a nested folder and remove it again; try a card switch right after a Remove.

## Calls Kjell made (please check)

- Add and Remove save the list and start a watcher-style run instead of settling cards at once, so the wall changes after the discovery.
- Copies are ranked by the earliest registered Library that holds them (a Core rule change that follows spec 6.1's wording), so nesting never changes a default active copy.
- Until a discovery from exactly the saved list has been published, every card change is refused; a discovery from an outdated list is discarded.
- Removing a not-found Library re-points its links to a copy in another registered Library (5b D10), and removing an inner folder can change a default active copy when a third Library ranks between; both are accepted and tested.
- Add is a list-only edit, and `RegisterLibrary` loses its adoption.
- Add refuses a folder that doesn't exist or can't be read.
- The coverage is shown in Settings too, not only on the first-run screen, so a forgotten folder is still noticed later.
- Remove asks no question; its limits as an undo are stated in goal 5.
- Add and Remove are allowed while MSFS runs and while a run is going.
- Removing the last Library is allowed.
- A failed save restores the list in memory, unlike the card edits.
- The first-run screen and Settings share the Library section view, the add/refuse logic and the coverage computation.
