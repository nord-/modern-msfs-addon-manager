# Plan 5d: changing Libraries in Settings

Design for the fourth step of plan 5 (issue #24). The full product is described in `docs/superpowers/specs/2026-10-25-mvp-design.md` (the spec); this document only says what 5d takes from it, where it deviates, and why. 5d builds on 5a (app shell, first run), 5b (switches, Community always wins) and 5c (card editing).

## Why

After first run the Libraries can't be changed: Settings only lists them, and the first-run screen says "In this version Libraries can't be changed after you press Start." A Library that was forgotten, moved or added later (a new drive, a new download folder) needs a fresh `userdata.db` today. 5d lets Rickard add and remove Libraries per menu section from Settings.

**Guiding constraint (Rickard, 2026-11-03): build only what is used now.** Issue #24 listed six parts of Settings. On 2026-11-15 Rickard chose Libraries only for 5d. Most of the other parts already have their logic in Core and lack only a Settings view (see "Out of scope").

## Goals

1. **Settings shows the Libraries per menu section, editable.** The read-only list on the Settings page is replaced by the three sections of the first-run screen: Scenery, Aircraft and Other, each with its folders, a Remove button per folder and "Add Library…". Below them, the link coverage is shown as on the first-run screen: "N of M links covered", the Not covered list and the Not counted list (spec 7.1 step 3, `LinkCoverage.Measure` over the registered Libraries). The Packages root and the database file stay on the page as read-only text, as in 5a.
2. **Changing a Library** means adding or removing the same folder under another section; there is no separate edit. The same folder may be listed under several sections (spec 6.1).
3. **Every Add and Remove is saved at once** (spec 11), with no Apply button. The wall follows without the user pressing F5.
4. **Add of a new folder** (one no section has):
   - The folder is refused with a reason by the same rule as on first run (`FirstRunLibraries.Refusal`: it is, lies inside or holds Community or `<Packages root>\Official`). A folder already listed in that section is ignored, as on first run.
   - Otherwise the folder is registered last for that section, and its Packages adopt the current Community state without asking (spec 6.1 "Registering a Library later", `SyncCoordinator.RegisterLibrary`): linked Packages are on, unlinked ones off, a link to another copy than the stored active copy makes the linked copy active, and Stale links are re-pointed (5b D10), or wait with the existing banner line while MSFS runs.
   - **The folder's Packages must be discovered before they are adopted.** The index of the last run holds no Packages from a folder that was not registered, so adopting with it would adopt nothing. How the folder is discovered before `RegisterLibrary` runs is left to the implementation plan.
   - Then a normal scan run places the new Packages on cards (spec 6.5: grouped among themselves first, then joining existing cards).
5. **Add of a known folder to another section** (the folder is already registered for some section): the section is added, the folder keeps its position (which decides the default active copy, spec 6.1), nothing is adopted and no stored choice changes. Packages that became eligible for a card in that section get one at once from the last completed analysis (`Settlement.Apply` on the last input, re-analysed, as the remark on `RegisterLibrary` describes), without waiting for a scan.
6. **Remove** of a folder from one section (`SyncCoordinator.UnregisterLibrary`): nothing is deleted, nothing in Community changes, and the Packages stay on their cards with all their user data (spec 6.1 "Removing a Library"). When the folder has no section left it is dropped from the list; its Managed entries become External and its Packages are locked with "not in a registered Library" (spec 6.1). The wall shows this at once, from the last completed input re-analysed with the new Libraries, without a scan. **No confirmation:** removing is undone by adding the folder again (Kjell's call, in line with 5c: only merge asks).
7. **While MSFS runs** Add and Remove still work, since they change only the app's data. Re-pointing Stale links waits as in 5b, with the existing banner line "Some links wait for MSFS to close. Close MSFS, then press F5." (Kjell's call.)
8. **Errors** (a refused folder, a locked `userdata.db`) are shown as text below the sections, as on the first-run screen. A failed change saves nothing and leaves the list as it was.
9. **The first-run screen** loses its note "In this version Libraries can't be changed after you press Start."

## Reuse (Kjell's call)

The first-run screen and Settings show the same thing: Libraries per section with Add and Remove, refusals and the link coverage. The `LibrarySection` view and the coverage computation are shared between the two views rather than copied. The difference is when changes take effect: on the first-run screen they are collected until Start, in Settings each one is saved at once.

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

- Core's rules for registering and removing Libraries are already tested (`SyncCoordinatorTests`).
- New tests for the session: Add of a new folder discovers its Packages before adopting them and then runs a scan; Add of a known folder to another section gives the newly eligible Packages cards without a scan; Remove keeps cards and user data and locks the Packages of a folder with no section left; a locked `userdata.db` saves nothing and leaves the list as it was; Add and Remove work while the sim runs.
- New tests for the Settings view model: refusals, a folder already in the section, the coverage text following the registered Libraries.
- A manual check on the development machine with `--data-dir` and the generated test folder (`tools/dev-check/New-TestPackages.ps1`): add a folder, see its cards appear; add it to a second section; remove it from one section and then from all, and see the cards stay, locked.

## Calls Kjell made (please check)

- The coverage is shown in Settings too, not only on the first-run screen, so a forgotten folder is still noticed later.
- Remove asks no question.
- Add and Remove are allowed while MSFS runs.
- The first-run screen and Settings share the Library section view and the coverage computation.
