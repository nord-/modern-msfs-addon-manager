# Plan 5d: Libraries in Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Rickard add and remove Libraries per menu section from Settings after first run; each change saves the list and starts an ordinary run that brings the wall in line.

**Architecture:** Add and Remove are list-only edits in `SyncCoordinator`, saved by `AppSession`, which then requests an incremental run. The run's discovery adopts through `SyncCoordinator.Start` and its completion settles cards, as today. Two new session rules keep the gap safe: a discovery or completion whose Library list is not exactly the saved one is discarded, and every card change is refused until a discovery from the saved list has been published. Settings gets its own view model that reads the Libraries and the link coverage from `SessionView`.

**Tech Stack:** C# / .NET 10, WPF + WPF-UI, CommunityToolkit.Mvvm, xUnit, SQLite `userdata.db`.

**Spec:** `docs/superpowers/specs/2026-11-15-plan-5d-libraries-design.md` (read it with this plan; the goal numbers below are its goals).

## Global Constraints

- CRLF line endings in every file (`.gitattributes`: `* text=auto eol=crlf`).
- Commit messages carry no AI attribution (no `Co-Authored-By`, no "Generated with").
- Never move or delete the user's files; Remove changes only `userdata.db`.
- English UI text. Exact strings:
  - gate: `"Libraries are being updated. Wait for the scan to finish, or press F5."`
  - unreadable folder: `"The folder can't be read: <path>"` (the path as picked)
- Build: `dotnet build ModernMsfsAddonManager.slnx`; tests: `dotnet test ModernMsfsAddonManager.slnx [--filter FullyQualifiedName~<Name>]`.
- The branch is `plan-5d-settings`; spec, plan and code ship in one PR for issue #24.

## Review Focus

1. **A double-clicked Remove** (two `RemoveLibraryAsync` calls for the same folder and section at once): the second one changes nothing, returns null, and no error is shown. Test in Task 3.
2. **The same folder added with another case or a trailing backslash** (`C:\Lib\` after `c:\lib`) is the known folder: no second Library. Test in Task 3.
3. **A sibling folder with the same prefix** (`Lib` and `Lib2`): removing `Lib` leaves `Lib2`'s Packages Managed. Test in Task 3.
4. **The card dialog open across a Remove:** rename and the other edits return the gate text, the dialog stays open, and nothing throws. Test in Task 4.
5. **F5 pressed while the gate is closed:** the full run uses the saved list, and its discovery opens the gate. Test in Task 4.

---

### Task 1: Rank copies by the earliest registered Library that holds them

Spec goal 6, rule change in Core.

**Files:**
- Modify: `src/AddonManager.Core/Packages/PackageIndex.cs` (`LibraryRank`, its two callers)
- Test: `tests/AddonManager.Core.Tests/Packages/PackageIndexTests.cs`
- Test: `tests/AddonManager.Core.Tests/Cards/PackageCatalogTests.cs`

**Interfaces:**
- Produces: `PackageIndex.CopiesOf` and `AllCopies` ordered by the earliest registered Library whose path holds the copy (`PathNormalizer.IsUnder`), then by path. `PackageCopy.Library` stays the innermost Library.

- [ ] **Step 1: Write the failing tests** in `PackageIndexTests`.
  - Libraries: Outer `C:\Sim` (0), Lib `C:\Lib` (1), Inner `C:\Sim\Inner` (2); copies `x` at `C:\Sim\Inner\x` and `C:\Lib\x`.
  - The test changes each case's Library list and checks the order of `CopiesOf(x)`.

```csharp
[Fact]
public void A_copy_is_ranked_by_the_earliest_registered_library_that_holds_it()
// CopiesOf(x) paths == [@"C:\Sim\Inner\x", @"C:\Lib\x"]; the first copy's Library == Inner

[Fact]
public void Adding_an_inner_or_an_outer_library_keeps_the_order_of_copies()
// [Outer, Lib] then [Outer, Lib, Inner]: the same order both times
// [Inner, Lib] then [Inner, Lib, Outer]: the same order both times

[Fact]
public void Removing_an_inner_library_with_a_third_ranked_between_moves_the_copy_after_the_third()
// [Inner, Lib, Outer]: Inner\x first; [Lib, Outer]: Lib\x first
```

- [ ] **Step 2: Write the failing test** in `PackageCatalogTests`.
  - `The_shown_library_of_a_nested_package_is_the_innermost`: the Libraries are Outer `C:\Sim\Addons` (registered first) and Inner `C:\Sim\Addons\Scenery`; a Package in Inner.
  - The Library the catalog gives for the Package is Inner. The innermost-tags case is already covered by `TagsTests.Tags_are_taken_below_the_innermost_library`. If the catalog test already passes, keep it as a guard.
- [ ] **Step 3: Run the tests.**
  - Run: `dotnet test ModernMsfsAddonManager.slnx --filter "FullyQualifiedName~PackageIndexTests|FullyQualifiedName~PackageCatalogTests"`
  - Expected: the first and third index tests FAIL (the innermost rank puts `Lib\x` first).
- [ ] **Step 4: Implement.**
  - Replace `LibraryRank(LibraryRoot)` with `LibraryRank(PackageCopy copy)`. It returns the lowest `i` where `PathNormalizer.IsUnder(copy.Path, Libraries[i].Path)`, or `int.MaxValue` when no Library holds the copy.
  - Use it in `byName` and in `AllCopies`.
  - Update the comments ("ordered by Library registration order") to say "by the earliest registered Library that holds it".
- [ ] **Step 5: Run the whole suite.**
  - Run: `dotnet test ModernMsfsAddonManager.slnx`
  - Expected: all PASS. If an existing test asserted the innermost rank, check it against spec 6.1 and update it, saying why in the commit message.
- [ ] **Step 6: Commit** `feat: rank copies by the earliest registered Library holding them (plan 5d)`

---

### Task 2: RegisterLibrary becomes a list edit; adoption goes through Start

Spec goal 4 and "The approach".

**Files:**
- Modify: `src/AddonManager.Core/Sync/SyncCoordinator.cs`
- Test: `tests/AddonManager.Core.Tests/Sync/SyncCoordinatorTests.cs` (the register tests and the test extension at the bottom)

**Interfaces:**
- Produces:
  - `public void RegisterLibrary(UserState state, string path, Category section)`: a new path is added last with that section; a known path (`PathNormalizer.AreEqual`) gains the section in place; a section already registered changes nothing and saves nothing. It runs inside `store.BeginChange()` and calls `store.Save(state)` only when something changed. It uses no `PackageIndex` and no `community`.
  - `public void UnregisterLibrary(UserState state, string path, Category section)`: unchanged behaviour.
  - `RegisterOutcome` is deleted.
  - The test extension becomes `RegisterLibrary(this SyncCoordinator, UserState, LibraryRoot)` (no index), and it returns void.

- [ ] **Step 1: Rewrite the register tests** so adoption runs through `Start` with an index walked from the new list. Delete `RegisterLibrary_repairs_the_stale_links_it_adopts` and `..._while_the_sim_runs`; their cases move to the `Start` tests below. Keep the assertions of `Registering_a_library_keeps_other_user_data_of_known_packages`, with a `Start` after the register.

```csharp
[Fact]
public void Registering_a_library_only_edits_the_list()
// RegisterLibrary(state, LibB): LibB is last in state.Libraries and in store.Load().Libraries; state.Packages and state.Snapshot are unchanged; fs.Calls is empty

[Fact]
public void Registering_a_known_folder_for_another_section_keeps_its_position()

[Fact]
public void Registering_a_registered_section_saves_nothing()   // store save count is unchanged

[Fact]
public void Start_after_registering_turns_on_a_linked_package_of_the_new_folder_with_the_linked_copy_active()

[Fact]
public void Start_after_registering_leaves_an_unlinked_package_of_the_new_folder_off()

[Fact]
public void Start_after_registering_keeps_the_stored_active_copy_of_a_package_linked_to_a_duplicate_copy_in_an_older_library()

[Fact]
public void Start_after_registering_repoints_the_stale_entry_of_an_eligible_off_package_and_turns_it_on()

[Fact]
public void Start_after_registering_leaves_the_stale_entry_of_a_not_shown_off_package()
// The pair is the spec 6.1 deviation. The Not shown set comes from a board in which the Package is in Board.NotShown.
```

  `Registering_scenery_for_a_known_folder_then_settling_gives_one_eham_card` keeps its assertions, with the new signature.
- [ ] **Step 2: Run the tests.**
  - Run: `dotnet test ModernMsfsAddonManager.slnx --filter FullyQualifiedName~SyncCoordinatorTests`
  - Expected: compile error (the old signature), then FAIL on `Registering_a_library_only_edits_the_list`.
- [ ] **Step 3: Implement** the new `RegisterLibrary` and delete `RegisterOutcome`.
  - Rewrite the XML docs of `RegisterLibrary` and `UnregisterLibrary`. Drop every remark that promises adoption or `Settlement.Apply` on the last completed input. State that adoption, settling and repairs belong to the next run's discovery (`Start`) and completion.
- [ ] **Step 4: Run the tests.** Run the Step 2 filter, then the whole suite. Expected: PASS.
- [ ] **Step 5: Commit** `refactor: make RegisterLibrary a list edit; adoption runs through Start (plan 5d)`

---

### Task 3: Session Add and Remove (save the list, then run)

Spec goals 2–5, 8–11.

**Files:**
- Create: `src/AddonManager.App/Session/AppSession.Libraries.cs` (a partial of `AppSession`)
- Modify:
  - `src/AddonManager.App/Session/IAppSession.cs`
  - `src/AddonManager.App/Session/SessionView.cs`
  - `src/AddonManager.App/Session/AppSession.cs` (`Publish`, `CompleteFirstRunAsync` unchanged)
  - `src/AddonManager.Core/Packages/LibraryRoots.cs`
- Test:
  - `tests/AddonManager.App.Tests/Session/AppSessionLibrariesTests.cs` (new; copy the setup helpers it needs from `AppSessionTests`, or move them to a shared `SessionHarness` class when two copies would be needed)
  - `tests/AddonManager.Core.Tests/Packages/LibraryRootTests.cs`
  - `tests/AddonManager.App.Tests/ViewModels/FakeSession.cs` and `FirstRunFakeSession.cs` (stub the new members)

**Interfaces:**
- Consumes: `SyncCoordinator.RegisterLibrary(UserState, string, Category)` and `UnregisterLibrary(...)` (Task 2).
- Produces:
  - `SessionView`: `public IReadOnlyList<LibraryRoot> Libraries { get; init; } = [];` and `public CoverageReport? Coverage { get; init; }` (Coverage is filled in Task 5). `Publish` copies the session's current `librariesView` into every view it publishes. `librariesView` is a fresh list, refreshed only when the list changes (startup, first run, Add, Remove), so its reference changes exactly then.
  - `IAppSession`:
    - `string? LibraryRefusal(string folder)`: the `FirstRunLibraries.Refusal` rule against `Paths`, then the readability check (enumerate the folder's first entry; `IOException` or `UnauthorizedAccessException` gives `"The folder can't be read: <folder>"`). It returns null when the folder may be added.
    - `Task<string?> AddLibraryAsync(string path, Category section)`
    - `Task<string?> RemoveLibraryAsync(string path, Category section)`
  - `LibraryRoots.SameList(IReadOnlyList<LibraryRoot> a, IReadOnlyList<LibraryRoot> b)`: true when both have the same count and, at every position, `PathNormalizer.AreEqual` paths and equal `Sections` (`SequenceEqual`).
- Behaviour of Add and Remove:
  - They run off the UI thread.
  - Outside the Ready phase they return `"Libraries can be changed when the wall is shown."`. This is Kjell's call: Settings is only reachable in Ready.
  - Add calls `LibraryRefusal` first.
  - Under the lock each one copies the list, calls the coordinator, refreshes `librariesView` and publishes `view` (same Board).
  - On `UserDataException` they restore `State.Libraries` to the copy and return `e.Message`, and they start no run.
  - When the list changed, they request `RunAsync(false)` after the lock (not awaited), and they return null. A call that changes nothing returns null and starts no run.

- [ ] **Step 1: Write the failing tests.**
  - `LibraryRootTests`: `SameList_compares_paths_order_and_sections`. Cases: the same list is true; another order is false; the same path with other sections is false; another case or a trailing backslash is true.
  - `AppSessionLibrariesTests`. Wait for a run with `Until(() => session.HandledOutcomes > before)`.

```csharp
Add_saves_the_list_changes_nothing_else_and_its_run_adopts_the_new_folder
// first run with Lib; Lib2 holds a linked "b" and an unlinked "c"
// after Add, Reload() shows Lib2 last and otherwise equals the state from before the Add
// after the run: "b" is on and on a card, "c" is off and on a card (spec 6.5)

Add_of_a_known_folder_to_a_second_section_keeps_its_position_and_gives_the_newly_eligible_packages_cards

Add_of_the_same_folder_with_another_case_or_a_trailing_backslash_is_the_known_folder   // Review Focus 2

Add_refuses_a_folder_that_does_not_exist
// message == $"The folder can't be read: {path}"; the list is unchanged

Add_refuses_community_and_official_with_the_first_run_text

Remove_from_one_of_two_sections_keeps_cards_and_moves_unplaced_packages_to_not_shown

Remove_of_the_last_section_locks_carded_packages_forgets_uncarded_ones_and_gives_a_linked_not_shown_package_a_locked_card
// linked carded → External, unlinked carded → off; both locked "not in a registered Library"
// a carded Package whose folder is deleted → Not found

Remove_of_a_library_that_is_not_found_marks_carded_packages_not_found_and_its_links_broken
// rename the Library folder away before the Remove

Remove_of_a_not_found_library_repoints_a_link_to_a_copy_in_another_registered_library   // 5b D10

Remove_of_lib_leaves_the_packages_of_lib2_managed   // Review Focus 3

Remove_of_an_inner_folder_held_by_an_outer_library_changes_no_choice_no_active_copy_and_no_lock

Add_of_an_inner_and_of_an_outer_folder_changes_no_stored_choice_or_default_active_copy   // a Duplicate copy case

Removing_the_last_library_leaves_only_community_entries_and_gsx_profiles_on_the_wall

A_double_remove_changes_nothing_the_second_time   // Review Focus 1: two concurrent calls; both null; one Library fewer

Add_and_remove_work_while_the_sim_runs_and_the_stale_entry_waits_with_the_banner_line   // ToggleSim; view.LinksWaiting

A_locked_database_leaves_the_list_as_it_was_and_starts_no_run
// flakyStore with FailBeginCall; RequestedRuns is unchanged

A_failing_save_restores_the_list_in_memory   // ArmSaveFailure

The_watcher_is_rebuilt_with_the_new_folders_after_the_run   // FakeWatchers: the latest watcher's paths include Lib2

The_view_carries_the_libraries_after_every_change
```

- [ ] **Step 2: Run the tests.**
  - Run: `dotnet test ModernMsfsAddonManager.slnx --filter "FullyQualifiedName~AppSessionLibrariesTests|FullyQualifiedName~LibraryRootTests"`
  - Expected: compile errors, then FAILs.
- [ ] **Step 3: Implement.**
  - `LibraryRoots.SameList`, the `SessionView` properties, the `IAppSession` members and `AppSession.Libraries.cs`.
  - Refresh `librariesView` in `StartCoreAsync` (after open) and in `CompleteFirstRunAsync` (after a successful save).
  - Stub the new members in both fakes: `FakeSession` records the calls, and `FirstRunFakeSession.LibraryRefusal` returns `FirstRunLibraries.Refusal` against its `Paths`.
- [ ] **Step 4: Run the tests.** Run the Step 2 filter, then the whole suite. Expected: PASS. These tests start no overlapping runs (the watchers are fakes); runs that overlap are Task 4's.
- [ ] **Step 5: Commit** `feat: add and remove Libraries in the session, saved at once and followed by a run (plan 5d)`

---

### Task 4: Discard outdated discoveries and close the card-change gate

Spec "The approach" (outdated list) and goal 7.

**Files:**
- Modify:
  - `src/AddonManager.App/Session/AppSession.cs` (`OnDiscovered`, `Complete`)
  - `src/AddonManager.App/Session/AppSession.Changes.cs` (`Change<T>`)
  - `src/AddonManager.App/Session/AppSession.Libraries.cs`
  - `src/AddonManager.App/AppServices.cs`
- Create: `tests/AddonManager.App.Tests/Session/GatedCache.cs`
- Test: `tests/AddonManager.App.Tests/Session/AppSessionLibrariesTests.cs`

**Interfaces:**
- Consumes: `LibraryRoots.SameList` and the Add and Remove of Task 3.
- Produces:
  - `public const string LibrariesUpdatingText = "Libraries are being updated. Wait for the scan to finish, or press F5.";` on `AppSession`.
  - A private `bool librariesChanging`. Add and Remove set it when the list changed. It is cleared only where a board is published from a discovery whose list is `SameList` with `State.Libraries`:
    - in `OnDiscovered`, after `Start` returned and the view with the board was published (when `ShowsWallAtDiscovery`);
    - in `Complete`, after it published.
  - `OnDiscovered` returns right after the `disposed` check when `!SameList(discovery.Index.Libraries, State.Libraries)`. It sets no `latestDiscovery`, no watcher, no `Start` and no publish, so the old run's `Complete` is ignored by the existing `latestDiscovery` check.
  - `Complete` adds the same check for a run whose discovery matched but whose list changed before it completed. It does not count the outcome as handled, settles nothing and publishes nothing.
  - `Change<T>` returns `(default, LibrariesUpdatingText)` while `librariesChanging`, before it calls the change. This covers every card change, `Edit` included.
  - `AppServices.WrapCache`: `internal Func<IScanCache, IScanCache> WrapCache { get; set; } = c => c;`, applied to the `SqliteScanCache` in `UseSim`. This is a test seam for holding a run before its discovery.
  - `GatedCache(IScanCache inner)`: `Close()`, `Open()`, `int Waiting`. Its `LoadAll` waits on the gate (30 s timeout, like `GatedReader`), and the other members delegate.

- [ ] **Step 1: Write the failing tests.**

```csharp
Between_a_change_and_the_next_discovery_every_card_change_is_refused_and_the_board_is_not_republished
// GatedCache closed after the first run; Remove
// SwitchCardAsync, RemoveAsync, RenameAsync, SetImageAsync, AddCodeAsync, RemoveCodeAsync, SplitAsync, MergeAsync,
// MoveToTabAsync and MoveToSectionAsync all return LibrariesUpdatingText
// view.Board is the same reference as before the Remove
// Open the gate and wait for the run: SwitchCardAsync returns null

The_card_dialogs_edits_get_the_gate_text_and_nothing_throws   // Review Focus 4: RenameAsync on the open card's ref

A_run_held_open_across_a_remove_completes_without_settling_or_publishing_and_the_queued_run_does_the_work
// GatedReader: hold the run after its discovery; Remove Lib2, which holds linked "b"
// open: no repair into Lib2; the next board shows "b" locked External; HandledOutcomes grew by one, not two

A_discovery_from_an_outdated_list_is_discarded
// GatedCache: hold a watcher-started run before its discovery; Remove Lib2, whose "b" has a Stale entry with a copy only in Lib2
// open: the Stale link is not re-pointed into Lib2 (machine.ListCommunity unchanged by the old run)

An_add_followed_at_once_by_a_remove_of_the_same_folder_adopts_and_repairs_nothing_in_it

A_start_that_throws_leaves_the_gate_closed   // flakyStore fails the discovery's BeginChange; SwitchCardAsync still returns LibrariesUpdatingText

F5_during_the_gate_runs_with_the_saved_list_and_opens_it   // Review Focus 5: RescanAsync(true)

A_crash_between_save_and_run_is_adopted_at_the_next_start
// GatedCache closed; Add Lib2 (linked "b"); Dispose before discovery
// new session StartAsync: "b" is on and on a card

Add_and_remove_during_a_run_request_one_more_run_with_the_newest_list
```

- [ ] **Step 2: Run the tests.** Run: `dotnet test ModernMsfsAddonManager.slnx --filter FullyQualifiedName~AppSessionLibrariesTests`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement** the behaviour in the Interfaces block.
  - Extend the class remarks of `AppSession` with one sentence each on the discard rule and the gate.
- [ ] **Step 4: Run the tests.**
  - Run the Step 2 filter five times in a row (they are timing-sensitive), then the whole suite.
  - Expected: PASS every time.
- [ ] **Step 5: Commit** `feat: discard runs from an outdated Library list and refuse card changes until the saved list is discovered (plan 5d)`

---

### Task 5: Link coverage in the session view

Spec goals 12 and 13.

**Files:**
- Modify:
  - `src/AddonManager.App/Session/AppSession.cs` (`OnDiscovered`, `WithCurrentCommunity`, `Publish`)
  - `src/AddonManager.App/Session/AppSession.Libraries.cs`
- Test: `tests/AddonManager.App.Tests/Session/AppSessionLibrariesTests.cs`

**Interfaces:**
- Produces: `SessionView.Coverage = LinkCoverage.Measure(lastCommunity, State.Libraries)`. It is recomputed only when `lastCommunity` or the list changes, and the same instance is published otherwise.
  - `lastCommunity` is the Community entries the session last read: `discovery.Community.Select(c => c.Entry)` at an accepted discovery, and the entries `WithCurrentCommunity` reads.
  - `ReadCommunity()` (the cached read) stays for the first-run screen only.

- [ ] **Step 1: Write the failing tests.**

```csharp
The_coverage_follows_the_libraries_after_an_add_and_a_remove
// Lib2 holds linked "b": Covered goes 0 → 1 after Add and back after Remove

The_coverage_follows_a_card_switch_the_app_makes   // Counted grows when the switch creates a link

A_community_change_by_another_tool_shows_in_the_coverage_only_at_the_next_discovery
// machine.Link without a run: Coverage unchanged; after RescanAsync(false): changed
```

- [ ] **Step 2: Run the tests.** Use the Task 4 filter. Expected: FAIL (Coverage is null).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests.** Run the filter, then the whole suite. Expected: PASS.
- [ ] **Step 5: Commit** `feat: publish the link coverage with the session view (plan 5d)`

---

### Task 6: Settings view model and views; first-run screen shares them

Spec goals 1, 11, 12, 14 and "Reuse".

**Files:**
- Create:
  - `src/AddonManager.App/ViewModels/SettingsViewModel.cs`
  - `src/AddonManager.App/ViewModels/CoverageText.cs`
  - `src/AddonManager.App/Views/CoverageView.xaml(.cs)`
- Modify:
  - `src/AddonManager.App/ViewModels/FirstRunViewModel.cs`
  - `src/AddonManager.App/ViewModels/MainViewModel.cs` (remove `SettingsSection`, `SettingsLibraries`, `PackagesRoot`, `DatabaseFile` and `RefreshSettings`)
  - `src/AddonManager.App/Views/SettingsView.xaml(.cs)`
  - `src/AddonManager.App/Views/FirstRunView.xaml`
  - `src/AddonManager.App/Views/LibrarySection.xaml.cs` (the summary says "first-run screen and Settings")
  - `src/AddonManager.App/MainWindow.xaml(.cs)`
  - `src/AddonManager.App/App.xaml.cs`
- Test:
  - `tests/AddonManager.App.Tests/ViewModels/SettingsViewModelTests.cs` (new)
  - `FirstRunViewModelTests.cs`
  - `MainViewModelTests.cs` (move any Settings tests to the new file)

**Interfaces:**
- Consumes: `IAppSession.LibraryRefusal`, `AddLibraryAsync`, `RemoveLibraryAsync`, `SessionView.Libraries` and `SessionView.Coverage`.
- Produces:
  - `CoverageText`, a static class:
    - `NotCoveredTitle` and `NotCountedTitle` (moved from `FirstRunViewModel`, same text);
    - `static (string Summary, IReadOnlyList<string> NotCovered, IReadOnlyList<string> NotCounted) From(CoverageReport report)`, with the formats `FirstRunViewModel.Recompute` uses today (`"{Covered} of {Counted} links covered"`, `"{Name} ({Target})"`, `Name`).
  - Both view models expose the same coverage members: `string? Coverage`, `IReadOnlyList<string> NotCovered`, `bool HasNotCovered`, `IReadOnlyList<string> NotCounted`, `bool HasNotCounted`. `CoverageView` binds to these names on its DataContext.
  - `SettingsViewModel(IAppSession session, Action<Action> post, Func<string?> pickFolder)`:
    - `IReadOnlyList<string> Scenery`, `Aircraft` and `Other`;
    - commands `AddScenery`, `AddAircraft`, `AddOther`, `RemoveScenery(string)`, `RemoveAircraft(string)` and `RemoveOther(string)`;
    - `string? Message` (the refusal or failed save from the last command; cleared by the next command that succeeds);
    - `string? PackagesRoot` and `string DatabaseFile`, plus the coverage members.
    - It refreshes on `session.Changed` (posted, reading `session.Current`, coalesced the way `FirstRunViewModel.OnSessionChanged` does). It refreshes only when `Libraries` or `Coverage` is a new reference.
    - It never reads `session.State`.
  - `FirstRunViewModel.Add` uses `session.LibraryRefusal(folder)` instead of calling `FirstRunLibraries.Refusal`.
  - The `Note` property and its `TextBlock` are deleted (goal 14).

- [ ] **Step 1: Write the failing tests** (FakeSession).

```csharp
SettingsViewModelTests:
Sections_list_the_folders_registered_for_each_section_from_the_view
Add_passes_the_picked_folder_and_section_to_the_session
A_refusal_or_failed_save_is_shown_as_the_message_and_the_next_success_clears_it
A_cancelled_pick_calls_nothing
Remove_passes_the_folder_and_section
The_coverage_text_follows_the_view   // "1 of 2 links covered", NotCovered and NotCounted lines
Refreshes_when_only_the_libraries_changed   // same Board reference, new Libraries

FirstRunViewModelTests:
Add_shows_the_sessions_refusal_for_an_unreadable_folder
// update the title assertion to CoverageText.NotCoveredTitle
```

- [ ] **Step 2: Run the tests.** Run: `dotnet test ModernMsfsAddonManager.slnx --filter "FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~FirstRunViewModelTests|FullyQualifiedName~MainViewModelTests"`. Expected: compile errors, then FAILs.
- [ ] **Step 3: Implement the view models.**
- [ ] **Step 4: Implement the views.**
  - `CoverageView` holds the coverage block that is now in `FirstRunView.xaml` (summary, Not covered, Not counted), and both screens use it.
  - `SettingsView`:
    - the three `LibrarySection` controls bound to `SettingsViewModel`;
    - `Message` in `#FFB000`, below the sections;
    - `CoverageView`;
    - then the read-only Packages folder and Database lines;
    - and "More settings come in a later version."
  - `SettingsView` gets its own DataContext in code-behind, the way `FirstRunView` does in `MainWindow`.
  - `App.xaml.cs` creates the `SettingsViewModel` with `PickFolder`.
- [ ] **Step 5: Run the tests.** Run the Step 2 filter, then the whole suite and `dotnet build ModernMsfsAddonManager.slnx`. Expected: PASS, with no new warnings.
- [ ] **Step 6: Commit** `feat: edit Libraries and see the link coverage in Settings (plan 5d)`

---

### Task 7: Docs and the manual check

**Files:**
- Modify:
  - `CLAUDE.md` (project status: 5d is done; 5e (#29) is next)
  - `README.md` (Settings changes the Libraries)
  - `ANALYS.md` (the 5d section: implemented, with the date)
  - `GLOSSARY.md` (only if a term changed)

- [ ] **Step 1: Update the docs.** CRLF.
- [ ] **Step 2: Manual check (Rickard).** Do this on the development machine with `dotnet run --project src/AddonManager.App -- --data-dir <folder>` and the folders from `tools/dev-check/New-TestPackages.ps1`. Steps from the spec's Testing section:
  1. Add a folder and see its cards appear.
  2. Add it to a second section.
  3. Remove it from one section, then from all, and see the cards stay, locked.
  4. Add a nested folder and remove it again.
  5. Try a card switch right after a Remove: it gives the gate text.
  6. Delete a folder after picking it: the refusal is shown.
- [ ] **Step 3: Commit** `docs: record plan 5d in CLAUDE.md, README and the analysis`
