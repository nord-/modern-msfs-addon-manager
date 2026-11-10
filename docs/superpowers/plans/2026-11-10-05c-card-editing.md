# Plan 5c — Card Editing, Drag and Drop, GSX Section Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user rename a card, pick its image, edit its ICAO codes, split and merge cards, move a card to another tab or menu section by drag and drop, and see the card's built-in parts, Airports, tags and GSX profiles in the card dialog (issue #23).

**Architecture:** Core keeps all rules (`CardEditor`, `BoardBuilder`); two small Core changes (`Split` and two `CardPackage` fields). The session gets one method per edit, all through one generic `Change<T>` that resolves the card references on the shown board under the lock and turns every refusal into a message. The dialog view model is split into header, rows, info and GSX parts, each with its own `UserControl`; drag and drop is WPF `DragDrop` in the views with the drop rules in `MainViewModel`.

**Tech Stack:** .NET 10, WPF, WPF-UI 4.3.0 (`ui:SymbolIcon`, Fluent symbols), CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-09-plan-5c-editing-design.md` (read it; this plan does not repeat its behaviour rules). Review items carried from the spec loop: `.claude/spec-review-loop/23-plan-5c-editing/spec-summary.md`, section "Carried to the plan"; each is mapped to a task in the table at the end.

## Global Constraints

- English UI copy, exactly as below (en dash in "3–5", ellipsis character in "Choose file…", "▾" in "Image ▾"):
  - `Use 3–5 letters or digits.`
  - `{CODE} is already on {holder name}.` / `{CODE} is already on another card.` ({CODE} is the upper-case code)
  - `Merge {dragged name} into {target name}?`
  - `Move {card name} to {Scenery|Aircraft|Other}? It loses its ICAO codes.`
  - `The card is no longer there.`
  - `Two GSX profiles for {CODE}`
  - `Search flightsim.to for {CODE} GSX profiles`
  - Labels: `Split`, `Image ▾`, `Choose file…`, `Automatic`, `ICAO:`, `Replaces built-in`, `shipped with scenery`, `Not found`, `on {card name}`, `{package name} (built-in)`.
- Icons are WPF-UI Fluent symbols only: `ArrowSplit20`, `LockClosed16`, `Search20`, `Warning16`. There is no bare `Warning` symbol in WPF-UI 4.3.0. Warning yellow is `#FFB000` (the app's existing warning colour).
- No message box or popup for any error except the existing confirm prompt; errors are a `Message` line in the dialog, a banner line, or a warning icon with the error as tooltip.
- No new NuGet dependency.
- CRLF line endings, spaces, English code comments, conventional commits with `Refs #23`, no AI attribution trailer.
- Build: `dotnet build ModernMsfsAddonManager.slnx`; tests: `dotnet test ModernMsfsAddonManager.slnx --filter FullyQualifiedName~<Name>`.

## Review Focus

1. A scan removes the open card while the name box holds unsaved text: the dialog closes, nothing is saved, nothing throws (Task 6 test `A_card_gone_from_the_board_closes_the_dialog_and_drops_the_text`).
2. A drag whose dragged or target card a scan removed: the banner shows "The card is no longer there." and nothing changes (Task 4 `A_merge_with_a_card_that_is_gone_returns_the_gone_text`, Task 9 `A_refused_drop_shows_its_message_as_a_banner_line`).
3. A locked `userdata.db` when the dialog closes with a changed name: the dialog still closes and the banner shows the file's message (Task 6 `A_refused_name_save_on_close_still_closes_and_goes_to_the_banner`).
4. Typing a lower-case code that another card holds ("eham"): the message shows "EHAM is already on …" and the text stays (Task 7 `A_taken_code_names_the_holder_and_keeps_the_text`).
5. The browser cannot open the search link: a warning icon with the error appears after that search button and nothing throws (Task 6 `A_search_link_that_fails_lights_the_warning_on_that_code`).

## Execution tracks

- **Main track:** Task 1 → Task 4 → Task 6 → Task 7 → Task 8 → Task 9 → Task 10.
- **Side track A:** Task 2 → Task 5 (cherry-pick onto the main track before Task 6).
- **Side track B:** Task 3 (cherry-pick before Task 6).

---

### Task 1: Core — `MoveOut` becomes `Split`, guard counts non-profile Packages

**Files:**
- Modify: `src/AddonManager.Core/Cards/CardEditor.cs` (`MoveOut`, lines 121–144)
- Test: `tests/AddonManager.Core.Tests/Cards/CardEditorTests.cs`, `tests/AddonManager.Core.Tests/Cards/GsxProfileTests.cs`

**Interfaces:**
- Produces: `public long CardEditor.Split(UserState state, long card, PackageKey package)` (same contract as `MoveOut`, new guard).

- [ ] **Step 1: Rename in the tests and add the guard test**

Rename every `editor.MoveOut(` to `editor.Split(` and the test names `Move_out_…` to `Split_…`. Add to `GsxProfileTests`:

```csharp
[Fact]
public void Split_is_refused_when_the_only_other_package_is_a_profile()
{
    var w = NewWorld();
    w.Add(Scenery("eham-x", ["EHAM"]));
    w.Profile("eham", "EHAM");
    w.Place();
    var card = Assert.Single(w.State.Cards);
    var editor = new CardEditor(new InMemoryUserStateStore(), TimeProvider.System);

    Assert.Throws<InvalidOperationException>(() => editor.Split(w.State, card.Id, Folder("eham-x")));

    Assert.Equal([Profile("eham"), Folder("eham-x")], w.State.PackagesOn(card.Id));
}
```

- [ ] **Step 2: Run, expect a compile failure** — `dotnet test ModernMsfsAddonManager.slnx --filter FullyQualifiedName~CardEditorTests` fails: `Split` not defined.

- [ ] **Step 3: Rename `MoveOut` to `Split`** and change the "only Package" guard to `state.PackagesOn(card).Count(k => k.Kind != PackageKind.GsxProfile) < 2` with the message `"A card needs two Packages that are not GSX profiles to be split."`. Update the XML doc ("Splits one Package off …").

- [ ] **Step 4: Run `--filter FullyQualifiedName~Cards`**, expect PASS. `grep -rn MoveOut src tests` gives nothing.

- [ ] **Step 5: Commit** `refactor: rename MoveOut to Split and count only non-profile Packages` (body: `Refs #23`).

---

### Task 2: Core — `CardPackage` carries `ThumbnailPath` and `Airports`

**Files:**
- Modify: `src/AddonManager.Core/Cards/Board.cs` (`CardPackage`), `src/AddonManager.Core/Cards/BoardBuilder.cs` (`MakeCard`, line 71)
- Test: `tests/AddonManager.Core.Tests/Cards/BoardLayoutTests.cs`

**Interfaces:**
- Produces, as `init` properties beside `Disabled` (so no positional call site changes):
  - `public string? CardPackage.ThumbnailPath { get; init; }`
  - `public IReadOnlyList<AirportRecord> CardPackage.Airports { get; init; } = [];`

- [ ] **Step 1: Write the failing test** in `BoardLayoutTests`:

```csharp
[Fact]
public void A_card_package_carries_its_thumbnail_and_airport_records_also_when_not_found()
{
    var w = NewWorld(Category.Scenery);
    w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]) with { ThumbnailPath = @"C:\t.png" });
    var found = Assert.Single(Assert.Single(Wall(w).Cards).Packages);

    w.Without("eham-x");
    var gone = Assert.Single(Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards).Packages);

    Assert.Equal(@"C:\t.png", found.ThumbnailPath);
    Assert.Equal("Schiphol", Assert.Single(found.Airports).Name);
    Assert.Equal(@"C:\t.png", gone.ThumbnailPath);
    Assert.Equal("Schiphol", Assert.Single(gone.Airports).Name);
}
```

- [ ] **Step 2: Run `--filter FullyQualifiedName~BoardLayoutTests`**, expect a compile failure.
- [ ] **Step 3: Add the two properties; in `MakeCard` set them from `p.ThumbnailPath` and `p.Airports`** in the existing object initializer.
- [ ] **Step 4: Run `--filter FullyQualifiedName~Cards`**, expect PASS.
- [ ] **Step 5: Commit** `feat: card packages carry their thumbnail and airport records`.

---

### Task 3: `ThumbnailCache` forgets a file that is gone

**Files:**
- Modify: `src/AddonManager.App/Thumbnails/ThumbnailCache.cs`, `src/AddonManager.App/ViewModels/MainViewModel.cs` (`RebuildCards`, `LoadImageAsync`)
- Test: `tests/AddonManager.App.Tests/Thumbnails/ThumbnailCacheTests.cs`, `tests/AddonManager.App.Tests/ViewModels/MainViewModelTests.cs`

Carried item R3-10: a picked image deleted later must show the tab placeholder on the next board, not a cached picture. The cache alone is not enough: the wall asks again only while `vm.Image` is null, and a null result never clears it. So every new board asks again for every card with an `ImagePath` (a cache hit is cheap), and a null result sets `vm.Image = null` when the path is unchanged.

- [ ] **Step 1: Write the failing test** `A_cached_image_whose_file_is_gone_loads_as_null`: load a real PNG from a temp folder (use the existing test's PNG helper), delete the file, load again → `null`; recreate it → not null.
  Add to `MainViewModelTests`: `A_card_whose_image_loads_as_null_on_a_new_board_loses_its_image` (the fake loader returns an image, then `null`; after the second board `Cards[0].Image` is null).
- [ ] **Step 2: Run `--filter FullyQualifiedName~ThumbnailCacheTests|FullyQualifiedName~MainViewModelTests`**, expect FAIL.
- [ ] **Step 3: In `LoadAsync`, a cache hit whose `File.Exists(path)` is false removes the key and returns `null`.** Update the class doc. In `RebuildCards`, call `LoadImageAsync` for every card with an `ImagePath` on each board (drop the `vm.Image is null` condition); in `LoadImageAsync`, a null result is posted too and clears `vm.Image` while `vm.ImagePath == path`.
- [ ] **Step 4: Run the filter**, expect PASS.
- [ ] **Step 5: Commit** `fix: a thumbnail whose file is gone shows the placeholder`.

---

### Task 4: Session — one method per edit through `Change<T>`

**Files:**
- Modify: `src/AddonManager.App/AppServices.cs` (`AppLinks`, `UseSim`), `src/AddonManager.App/Session/IAppSession.cs`, `src/AddonManager.App/Session/AppSession.Changes.cs`, `src/AddonManager.App/Session/AppSession.cs` (`Airports` only)
- Test: `tests/AddonManager.App.Tests/Session/AppSessionTests.cs`; extend `tests/AddonManager.App.Tests/ViewModels/FakeSession.cs`; stub the new members in `tests/AddonManager.App.Tests/ViewModels/FirstRunFakeSession.cs` (return `null`, `(null, null)` or an empty dictionary)

**Interfaces:**
- Consumes: `CardEditor.Split` (Task 1).
- Produces on `IAppSession` (each runs on the thread pool, never throws, returns the message to show or `null`):
  - `IReadOnlyDictionary<AirportCode, Airport> Airports { get; }` — the Airports of the latest input, read **without** the session lock (the UI thread reads it on every keystroke, and the class promises the UI thread never waits on a run): a private `IReadOnlyDictionary<AirportCode, Airport> airports` field, written with `Volatile.Write` wherever `latestInput` is set and read with `Volatile.Read`; empty before the first discovery.
  - `Task<string?> RenameAsync(CardRef card, string? name)`
  - `Task<string?> SetImageAsync(CardRef card, string? path)`
  - `Task<(CodeOutcome? Outcome, string? Message)> AddCodeAsync(CardRef card, string text)`
  - `Task<string?> RemoveCodeAsync(CardRef card, AirportCode code)`
  - `Task<string?> SplitAsync(CardRef card, PackageKey package)`
  - `Task<string?> MergeAsync(CardRef dragged, CardRef target)`
  - `Task<string?> MoveToTabAsync(CardRef card, CardTab tab)`
  - `Task<string?> MoveToSectionAsync(CardRef card, Category section)`
  - `public const string AppSession.CardGoneText = "The card is no longer there.";`
- `AppLinks` gains a last member `CardEditor Editor`, built in `UseSim` as `new CardEditor(Store, TimeProvider.System)`.
- `FakeSession` gets, per method, a `List<…>` of the calls and a settable result (`RenameMessage`, `AddCodeResult`, …, same pattern as `Switched`/`SwitchMessage`), plus a settable `Airports` dictionary.

Carried items covered here: 8, 9, 13, R2-5, R3-3, R4-1, R5-2, R5-3.

- [ ] **Step 1: Write the failing session tests** (same machine helpers as the switch tests). `SimMachine` Packages have no BGL and no known code, so `machine.AddPackage("a")` and `machine.AddPackage("b")` give two code-less Scenery cards; a card with two Packages is made by `MergeAsync` first. `Edit` is the one path every method takes, so the locked-file and gone-card cases are a `[Theory]` over all eight methods (each case a lambda calling one method on a stored card):
  - `Rename_saves_the_name_and_the_wall_shows_it_at_once` — `RenameAsync(ref, "Home")` → `null`; `CardOf(...).Name == "Home"`; `Reload()` has the name.
  - `[Theory] A_locked_database_refuses_every_edit_and_changes_nothing` — `store.FailBeginCall = store.BeginCalls + 1`; the call → message contains `"userdata.db"`; `Reload()` equals the state before.
  - `[Theory] Every_edit_of_a_card_that_is_gone_returns_the_gone_text` — the card ref is `new CardRef(999, null)` → `AppSession.CardGoneText`.
  - `A_merge_with_a_card_that_is_gone_returns_the_gone_text` — target ref `new CardRef(999, null)` → `CardGoneText`, dragged card unchanged.
  - `Merging_a_card_into_itself_returns_a_message_and_throws_nothing` — `MergeAsync(a, a)` → non-null message (the `ArgumentException`'s).
  - `AddCode_returns_the_outcome_and_a_taken_code_names_the_holder` — first card gets `EHRD` → `(Added, null)`; second card `EHRD` → `Outcome.Result == Taken`, `HolderName` is the first card's shown name.
  - `Split_works_while_the_sim_runs` — merge card b into card a, then `ToggleSim.Running = true`; `SplitAsync(a, Pkg("b"))` → `null`, the board has two cards again.
  - `Moving_to_a_section_puts_the_card_on_its_initial_tab` — `MoveToSectionAsync(ref, Category.Other)` → card on Other/Other, no codes.
  - `MoveToTab_and_SetImage_and_RemoveCode_reach_the_editor` — one test, three calls, each observable on the next board.
  - `Airports_lists_the_known_airports_after_discovery`.
- [ ] **Step 2: Run `--filter FullyQualifiedName~AppSessionTests`**, expect compile failures.
- [ ] **Step 3: Generalise `Change`** in `AppSession.Changes.cs`:
  - `private (T? Value, string? Message) Change<T>(Func<AppLinks, (T Value, ApplyResult? Applied)> change)` — today's body; the early exit returns `(default, null)`; the catch list adds `ArgumentException`.
  - The existing `string? Change(Func<AppLinks, ApplyResult?>)` becomes a one-line wrapper, so `SwitchCardAsync` and `RemoveAsync` are unchanged.
  - `private (T? Value, string? Message) Edit<T>(IReadOnlyList<CardRef> cards, Func<AppLinks, Board, long[], T> edit)` — inside `Change<T>`: resolves every ref with `ref.In(view.Board)`; a missing board returns `(default, null)`; a ref that resolves to nothing or to a provisional card (`Id` null) throws a private `CardGoneException : InvalidOperationException` with `CardGoneText`, so the existing catch turns it into the message and nothing is called. Then calls `edit(links, shownBoard, ids)` with no `ApplyResult`.
- [ ] **Step 4: Implement the eight methods** as `Task.Run(() => Edit(...))` one-liners calling `links.Editor`: `Merge(State, board, ids[0], ids[1])`, `MoveToTab(State, ids[0], tab)`, `MoveToSection(State, Reanalyzed(latestInput!), ids[0], section)`, `Split(State, ids[0], package)`, `Rename`, `SetImage`, `AddCode(State, board, ids[0], text)` (returns the outcome as `Value`), `RemoveCode(State, board, ids[0], code)`. Add `Airports`. Extend `FakeSession`.
- [ ] **Step 5: Run `--filter FullyQualifiedName~AddonManager.App.Tests`**, expect PASS.
- [ ] **Step 6: Commit** `feat: session methods for every card edit`.

---

### Task 5: Info and GSX parts of the dialog (pure view models)

**Files:**
- Create: `src/AddonManager.App/ViewModels/CardInfoViewModel.cs`, `src/AddonManager.App/ViewModels/GsxSectionViewModel.cs`
- Test: `tests/AddonManager.App.Tests/ViewModels/CardInfoViewModelTests.cs`, `tests/AddonManager.App.Tests/ViewModels/GsxSectionViewModelTests.cs`

**Interfaces:**
- Consumes: `CardPackage.Airports` (Task 2); `Card`, `Board.CardsAt`, `Board.CardOf`, `GsxAirport`, `ShippedProfile`.
- Produces (immutable records, rebuilt on every `Update`):
  - `sealed record BuiltInRow(string DisplayName, string Package)` — `Package` is `"{name} (built-in)"`.
  - `sealed record OtherCard(string Name, CardRef Ref)`
  - `sealed record AirportRow(string Code, string? Name, bool ReplacesBuiltIn, IReadOnlyList<OtherCard> OtherCards)` — `Title` property: `"EHAM Amsterdam Schiphol"` or `"EHAM"`.
  - `sealed record CardInfoViewModel(IReadOnlyList<BuiltInRow> BuiltIns, IReadOnlyList<AirportRow> Airports, string? Tags)` with `static CardInfoViewModel Build(Card card, Board board, IReadOnlyDictionary<AirportCode, Airport> airports)`.
  - `sealed record GsxProfileRow(string Name, PackageKey? Key, bool Dimmed, string? Label, OtherCard? OnCard, string? TwoProfilesTip, bool CanRemove)` — implements `IRemovableRow` (below).
  - `sealed record GsxSearchRow(AirportCode Code, string Link, string Tip, string? Error)` — `Tip` is the search tooltip.
  - `sealed record GsxSectionViewModel(IReadOnlyList<object> Rows)` with `bool IsVisible` (rows not empty) and `static GsxSectionViewModel Build(Card card, Board board, IReadOnlyDictionary<AirportCode, string> searchErrors)`.
  - `interface IRemovableRow { PackageKey? Key { get; } string FolderName { get; } bool CanRemove { get; } }` (in `PackageRowViewModel.cs`; `PackageRowViewModel` implements it in Task 6 with a nullable `Key`; `PackageKey` is a `readonly record struct`, so the interface uses the nullable type). A profile row has `Key` only for the card's own profile Packages (`null` for rows on another card and shipped rows); `FolderName` is `Name`.

Rules (spec 5 and 6), each pinned by one test:
- Airport codes: the card's codes in order, then the Packages' other codes by ordinal value. Name: `airports[code].Name` when not blank, else the first non-blank name among the card's Packages' `Airports` records whose `Code` equals the row's code (Not found fallback). `ReplacesBuiltIn`: any Package's `ReplacesBuiltIn` contains the code. `OtherCards`: `board.CardsAt(code)` without this card (`CardRef.Of(c) != CardRef.Of(card)`).
- Tags: `string.Join(", ", card.Tags)`, `null` when empty. Built-ins: one row per `card.BuiltIns`.
- GSX rows per card code (order of `card.Codes`): the card's own profile Packages whose `Codes` contain the code (Found + not `Disabled` + in `GsxAirport.Active` with `Active.Count > 1` → `TwoProfilesTip = "Two GSX profiles for EHAM"`; `Disabled` → dimmed; Not found → dimmed, label `Not found`, `CanRemove = Removable`); then active profiles of `GsxAirport.Active` that sit on another card (`board.CardOf(new PackageKey(PackageKind.GsxProfile, name))`), with `OnCard` and the same two-profiles tip; then shipped profiles, named by `Path.GetFileNameWithoutExtension(IniPath)`, dimmed, label `shipped with scenery`, left out when `InGsxFolder` and a row of this code already has that name (case-insensitive); then, when `Active` is empty, one `GsxSearchRow` with `searchErrors.GetValueOrDefault(code)`. Profile Packages with none of the card's codes come last. With empty `GsxAirports`, only the card's own profile rows show.

- [ ] **Step 1: Write the failing tests**, one per rule above. Names: `Airport_names_come_from_the_analysis_then_from_remembered_records`, `A_code_only_a_package_has_gets_a_row_after_the_cards_codes`, `Replaces_built_in_and_other_cards_at_the_same_airport`, `Tags_are_one_line_and_built_ins_are_listed`, `Two_active_profiles_mark_each_row_also_on_another_card`, `Disabled_and_not_found_profiles_are_dimmed_and_not_found_keeps_remove`, `A_shipped_profile_is_hidden_only_when_its_installed_copy_has_a_row`, `A_code_without_an_active_profile_gets_a_search_row_with_its_error`, `Profiles_of_codes_the_card_lacks_come_last`, `Without_gsx_facts_only_the_cards_profiles_show`. Build cards with `Boards.Card(...) with { Packages = …, GsxAirports = … }`.
- [ ] **Step 2: Run `--filter FullyQualifiedName~CardInfoViewModelTests|FullyQualifiedName~GsxSectionViewModelTests`**, expect compile failures.
- [ ] **Step 3: Implement both `Build` methods.**
- [ ] **Step 4: Run the filter**, expect PASS.
- [ ] **Step 5: Commit** `feat: info and GSX parts of the card dialog`.

---

### Task 6: Dialog view model — services, closing, name, image, rows, Split, card switch

**Files:**
- Create: `src/AddonManager.App/ViewModels/DialogServices.cs`, `src/AddonManager.App/ViewModels/CardDialogViewModel.Header.cs` (name and image; Task 7 adds the ICAO part)
- Modify: `src/AddonManager.App/ViewModels/CardDialogViewModel.cs`, `PackageRowViewModel.cs`, `MainViewModel.cs`, `src/AddonManager.App/App.xaml.cs`
- Test: `tests/AddonManager.App.Tests/ViewModels/CardDialogViewModelTests.cs`, `MainViewModelTests.cs`

**Interfaces:**
- Consumes: Task 4 session methods; Task 5 builders; Task 3 is only behaviour.
- Produces:
  - `public sealed record DialogServices(Func<string, bool> Confirm, Func<string?, Task<ImageSource?>> LoadImage, Func<string?> PickImage, Action<string> OpenLink)`.
  - `MainViewModel(IAppSession session, Action<Action> post, string gsxFolder, DialogServices services)` (replaces the `loadImage, confirm` parameters; update the test factory in `MainViewModelTests` and `App.xaml.cs`).
  - `CardDialogViewModel(IAppSession session, Board board, CardRef card, DialogServices services, Action<string> notice, Action changeStarting)`; `notice` is `MainViewModel.ShowNotice`, `changeStarting` is `MainViewModel.ClearNotice`.
  - `MainViewModel`: `CloseCardCommand` (saves: ✕ and scrim), `DiscardCardCommand` (Esc), `void ShowNotice(string message)`. The notice is appended to `BannerLines` after `BannerText.Lines(...)`; it is cleared when the next user change starts and when `Apply` sees a new board. "A user change" is every command that calls the session: the dialog calls `changeStarting()` first in its switch, remove, rename, image, code, split and card-switch commands, and every drop calls `ClearNotice()` first (Task 9); a view without a new board (progress) keeps it.
  - `CardDialogViewModel`: `string NameText` (two-way), `bool CanEdit` (`card.Id is not null`), `Task<string?> CommitNameAsync()`, `void DiscardName()`, `void Detach()` (after it, `CommitNameAsync` does nothing), `ImageSource? Image`, `CardTab Tab` (placeholder), `IReadOnlyList<ImageChoice> ImageChoices`, `IAsyncRelayCommand<string?> SetImageCommand` (`null` = Automatic), `IAsyncRelayCommand ChooseImageCommand` (both can execute only when `CanEdit`), `IAsyncRelayCommand<IRemovableRow> RemoveCommand` (does nothing for a row without `Key`), `IAsyncRelayCommand<PackageRowViewModel> SplitCommand`, `IAsyncRelayCommand<CardRef> ShowCardCommand` (switch to another card), `IRelayCommand<GsxSearchRow> SearchCommand`, `CardInfoViewModel Info`, `GsxSectionViewModel Gsx`.
  - `sealed partial class ImageChoice(string displayName, string path) : ObservableObject` with `DisplayName`, `Path` and `[ObservableProperty] ImageSource? image` loaded through `LoadImage`.
  - `PackageRowViewModel` gains `bool Dimmed` (`Lock != LockReason.None`) and implements `IRemovableRow`; `Rows` lists only Packages with `Kind != PackageKind.GsxProfile`; `bool CanSplit` on the dialog = `CanEdit` and at least two such rows.

Name rules (carried R3-5): `bool NameFocused` is set by the view on focus in and out; `Update` refreshes `NameText` only while the box has no focus and the text equals the text last filled from the card, and losing focus commits and then refreshes; `CommitNameAsync` sends `RenameAsync(card, trimmed)` only when the trimmed text differs from that filled text (empty sends `null`), and on success the filled text becomes the new name. `CloseCard`: `await CommitNameAsync()`, `Detach()`, `CardDialog = null`, a message goes to `ShowNotice`. `DiscardCard`: `DiscardName()`, `Detach()`, `CardDialog = null`. `ShowCardCommand`: commit (message to `notice`), then show the target card from the dialog's last board and reset everything that belonged to the old card, whatever the commit returned: `NameText` and the filled text from the new card, `CodeText` empty, suggestions closed, `CodeMessage`, `Message` and the search errors cleared. A second `CommitNameAsync` while one is in flight awaits the first instead of sending again (✕ fires focus loss and then the close command). A board without the card closes the dialog and drops the text (existing `Update` returns `false`). Search (carried R2-9): `OpenLink(row.Link)` inside `try`; an exception stores its message under the row's code and rebuilds `Gsx`. Image (carried R4-9): `LoadImage(card.ImagePath)`; `null` leaves `Image` null so the view shows the tab placeholder.

- [ ] **Step 1: Write the failing tests:**
  - `Clicking_through_the_name_saves_nothing` — commit with untouched text → `Renamed` empty.
  - `A_changed_name_is_trimmed_and_saved_and_empty_clears_it` — `"  Home "` → `"Home"`; `""` → `null`.
  - `A_new_board_keeps_the_text_of_a_focused_name_box_and_refreshes_an_unfocused_one`.
  - `A_card_gone_from_the_board_closes_the_dialog_and_drops_the_text` (main VM: raise a board without the card → `CardDialog` null, `Renamed` empty).
  - `Escape_discards_the_name_and_closes` (`DiscardCardCommand`) and `Close_saves_the_name_first` (`CloseCardCommand`).
  - `A_refused_name_save_on_close_still_closes_and_goes_to_the_banner` — `RenameMessage = "userdata.db is locked"` → dialog null, `BannerLines` ends with it.
  - `The_notice_is_kept_by_progress_and_cleared_by_a_new_board`.
  - `Switching_to_another_card_saves_the_name_and_shows_that_card`.
  - `A_refused_save_on_switch_does_not_carry_the_text_to_the_other_card` — locked rename on card A, switch to B → `NameText` is B's name, `CodeText` empty, the refusal in the banner.
  - `Two_commits_at_once_send_one_rename` — the fake session holds `RenameAsync` open; two `CommitNameAsync` calls → one entry in `Renamed`.
  - `Every_session_command_clears_the_notice_first`.
  - `Provisional_cards_are_read_only` (`CanEdit` false, `CanSplit` false, `SetImageCommand` and `ChooseImageCommand` cannot execute; Task 7 adds the code commands to this test).
  - `Profiles_are_not_package_rows_and_split_needs_two_other_packages`.
  - `Split_calls_the_session_with_the_row` and `Locked_rows_are_dimmed`.
  - `Image_choices_list_packages_with_thumbnails_and_automatic_clears` and `Choose_file_sets_the_picked_path_and_cancel_does_nothing`.
  - `A_search_link_that_fails_lights_the_warning_on_that_code` and `The_search_error_is_kept_by_a_new_board_and_cleared_by_a_card_switch`.
- [ ] **Step 2: Run `--filter FullyQualifiedName~CardDialogViewModelTests|FullyQualifiedName~MainViewModelTests`**, expect compile failures.
- [ ] **Step 3: Implement**, including `App.xaml.cs`: `PickImage` is a `Microsoft.Win32.OpenFileDialog` with filter `Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png` returning the path or `null`; `OpenLink` is `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`; the confirm caption becomes `"Modern MSFS Addon Manager"` (it now asks about merges and moves too).
- [ ] **Step 4: Run the App tests**, expect PASS.
- [ ] **Step 5: Commit** `feat: card dialog edits the name and image and splits packages`.

---

### Task 7: Dialog view model — the ICAO code field

**Files:**
- Modify: `src/AddonManager.App/ViewModels/CardDialogViewModel.Header.cs`
- Test: `tests/AddonManager.App.Tests/ViewModels/CardDialogViewModelTests.cs`

**Interfaces:**
- Consumes: `IAppSession.Airports`, `AddCodeAsync`, `RemoveCodeAsync` (Task 4); `AirportSuggestions.Find`.
- Produces: `bool ShowCodes` (Scenery card), `IReadOnlyList<AirportCode> CodeChips`, `string CodeText` (two-way, on every keystroke), `IReadOnlyList<Airport> Suggestions`, `Airport? SelectedSuggestion`, `bool SuggestionsOpen`, `string? CodeMessage`, `void MoveSelection(int delta)` (clamped), `bool CloseSuggestions()` (true when it closed an open list, so the view marks Esc handled), `IAsyncRelayCommand AddCodeCommand`, `IAsyncRelayCommand<Airport> PickSuggestionCommand`, `IAsyncRelayCommand<AirportCode> RemoveCodeCommand`.

`AddCodeCommand`, `PickSuggestionCommand` and `RemoveCodeCommand` can execute only when `CanEdit`; on a provisional card the view shows the chips without × and no text box.

Rules: each `CodeText` change recomputes `Suggestions = AirportSuggestions.Find(session.Airports, CodeText)` (max 10), selects the first and opens the list when not empty. `AddCodeCommand` adds `SelectedSuggestion.Code.Value` while the list is open and something is selected, else `CodeText`. The result: `Added` → `CodeText = ""`, list closed, `CodeMessage = null`; `Invalid` → `CodeMessage = "Use 3–5 letters or digits."`; `Taken` → `"{CODE} is already on {HolderName}."` or `"… on another card."`; `(null, message)` → `Message = message`, text kept; `(null, null)` → nothing. Text is kept on every refusal.

- [ ] **Step 1: Write the failing tests:** `Typing_suggests_names_and_codes_and_selects_the_first`, `Up_and_down_move_the_selection_within_the_list`, `Enter_adds_the_selected_suggestion`, `Escape_closes_the_list_and_enter_then_adds_the_typed_text` ("YALR" with YALR1 known → `AddedCodes` has "YALR"), `An_invalid_code_shows_the_rule_and_keeps_the_text`, `A_taken_code_names_the_holder_and_keeps_the_text` (typed "eham" → "EHAM is already on EHAM Schiphol."), `A_taken_code_whose_holder_is_not_shown_says_another_card`, `A_session_refusal_shows_the_message`, `Removing_a_chip_calls_the_session`, `Only_scenery_cards_show_the_code_field`.
- [ ] **Step 2: Run the dialog tests**, expect FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the dialog tests**, expect PASS.
- [ ] **Step 5: Commit** `feat: ICAO code field with suggestions in the card dialog`.

---

### Task 8: Dialog views — four `UserControl`s and the layout

**Files:**
- Create: `src/AddonManager.App/Views/CardDialogHeader.xaml(.cs)`, `CardDialogPackages.xaml(.cs)`, `CardDialogInfo.xaml(.cs)`, `CardDialogGsx.xaml(.cs)`
- Modify: `src/AddonManager.App/Views/CardDialog.xaml(.cs)`, `src/AddonManager.App/MainWindow.xaml` (Esc → `DiscardCardCommand`; scrim → `CloseCardCommand`)

**Interfaces:** Consumes the view-model members of Tasks 5–7; produces no code interface.

Layout (carried R5-8): `CardDialog` keeps `Width="640" MaxHeight="620"`; the header is docked at the top and does not scroll; one `ScrollViewer` holds Packages, Info and GSX. ✕ runs `CloseCardCommand`.

- **Header** (spec 3 drawing): image 120×90 with the `TabSymbolConverter` placeholder when `Image` is null; `Image ▾` is a `ui:DropDownButton` (or a `Button` with a `ContextMenu`) listing `ImageChoices` (thumbnail + name), a separator, `Choose file…`, `Automatic`. The name `TextBox` binds `NameText` (`UpdateSourceTrigger=PropertyChanged`), Enter and `LostKeyboardFocus` call `CommitNameAsync` (the result goes to `Message`), `IsReadOnly = !CanEdit`; `Image ▾` is disabled when `!CanEdit`. `ICAO:` row: chips (`ItemsControl` of code + `×` button), then a `TextBox` bound to `CodeText` with a `Popup` list of suggestions ("EHGG  Groningen Eelde"); `PreviewKeyDown` handles Up/Down (`MoveSelection`), Enter (`AddCodeCommand`), Esc (`CloseSuggestions()`; mark handled only when it returns true). `CodeMessage` below in `#FFB000`. The switch, `SwitchReason` and `Message` stay as in 5b.
- **Packages:** the 5b row template plus `Opacity=0.5` when `Dimmed`, and a `Split` button (`ui:SymbolIcon ArrowSplit20` + text) visible when the dialog's `CanSplit`.
- **Info:** built-ins dimmed with `LockClosed16`; Airport rows: `Title`, `Replaces built-in`, other cards as hyperlink-style buttons running `ShowCardCommand`; the tags line. Each part collapses when empty.
- **GSX:** header `GSX`; `DataTemplate`s per row type: profile row (name, `Warning16` in `#FFB000` with `TwoProfilesTip` as tooltip, label, `on <card>` link, Remove), search row (code, icon-only `ui:Button` with `Search20` and `Tip` as tooltip, then `Warning16` with `Error` as tooltip when set). The section collapses when `Gsx.IsVisible` is false.

- [ ] **Step 1: Build the four controls and the new `CardDialog`.**
- [ ] **Step 2: `dotnet build ModernMsfsAddonManager.slnx`**, expect 0 errors and 0 new warnings. XAML errors show only at run time, so Step 3 is the load check.
- [ ] **Step 3: Run the app with `--data-dir` on the generated test folder** (`tools/dev-check/New-TestPackages.ps1`) and check: header fixed while the rest scrolls; rename by Enter, by clicking outside, Esc drops; suggestions with Up/Down/Enter/Esc; image menu; Split; GSX rows. Screenshot the dialog.
- [ ] **Step 4: Commit** `feat: card dialog views for header, packages, info and GSX`.

---

### Task 9: Drag and drop on the wall

**Files:**
- Modify: `src/AddonManager.App/ViewModels/CardViewModel.cs`, `MainViewModel.cs`, `src/AddonManager.App/Views/CardView.xaml(.cs)`, `src/AddonManager.App/MainWindow.xaml(.cs)`
- Test: `tests/AddonManager.App.Tests/ViewModels/MainViewModelTests.cs`, `CardViewModelTests.cs`

**Interfaces:**
- Consumes: `MergeAsync`, `MoveToTabAsync`, `MoveToSectionAsync` (Task 4); `ShowNotice`/`ClearNotice` (Task 6).
- Produces:
  - `CardViewModel`: `Category Section`, `bool CanDrag` (`Ref.Id is not null`), `bool HasCodes`.
  - `MainViewModel`: `bool CanDropOnCard(CardViewModel dragged, CardViewModel target)`, `bool CanDropOnTab(CardViewModel dragged, TabViewModel tab)`, `bool CanDropOnSection(CardViewModel dragged, MenuSection section)`, and `Task DropOnCardAsync(…)`, `Task DropOnTabAsync(…)`, `Task DropOnSectionAsync(…)` with the same parameters.

Rules: valid only when both cards are stored and different (card), the tab is another tab of the current section (tab), the section is Scenery, Aircraft or Other and not the card's own (section). Every drop first clears the notice, re-checks validity, then: card → confirm `Merge {dragged} into {target}?` → `MergeAsync`; tab → `MoveToTabAsync`; section → when `dragged.Section == Scenery && dragged.HasCodes` confirm `Move {name} to {Section}? It loses its ICAO codes.` → `MoveToSectionAsync`. No on a confirm calls nothing. A returned message goes to `ShowNotice`. The selected menu and tab never change.

Views (carried R4-11): `CardView` starts the drag in `MouseMove` when `pressed` and the move exceeds `SystemParameters.MinimumHorizontalDragDistance`/`MinimumVerticalDragDistance`: clear `pressed`, `ReleaseMouseCapture()`, and when `CanDrag` call `DragDrop.DoDragDrop(Frame, new DataObject(typeof(CardViewModel), vm), DragDropEffects.Move)`. `CardView`, each tab header item and the three menu items set `AllowDrop`; `DragOver` sets `Effects` from the matching `CanDrop…` and `Handled`; `DragEnter`/`DragLeave`/`Drop` set and clear an accent `BorderBrush` (a WPF-UI accent brush resource, 2 px). `Drop` calls the `Drop…Async` method through `Dispatcher.BeginInvoke`, so the confirm prompt shows after `DoDragDrop` returns.

- [ ] **Step 1: Write the failing tests:** `Only_stored_different_cards_accept_a_card_drop`, `Only_other_tabs_of_the_section_accept_a_drop`, `Only_another_content_section_accepts_a_drop` (Settings and the own section refused), `A_card_drop_asks_before_merging_and_no_merges_nothing`, `Moving_a_card_with_codes_out_of_scenery_asks_and_without_codes_does_not`, `A_drop_keeps_the_menu_and_tab`, `A_refused_drop_shows_its_message_as_a_banner_line`, `The_next_drop_clears_the_previous_notice`.
- [ ] **Step 2: Run `--filter FullyQualifiedName~MainViewModelTests|FullyQualifiedName~CardViewModelTests`**, expect FAIL.
- [ ] **Step 3: Implement the view-model part**, run the filter, expect PASS.
- [ ] **Step 4: Implement the view part**; `dotnet build` with 0 errors.
- [ ] **Step 5: Run the app with `--data-dir`** and check: a click still opens the dialog and a drag never does; merge with Yes/No; move to a tab; move to Aircraft/Other with and without codes; provisional cards neither drag nor accept; hovering a tab does not switch it; a "no drop" pointer outside targets.
- [ ] **Step 6: Commit** `feat: drag cards to merge them or move them to a tab or section`.

---

### Task 10: Documentation and the full check

**Files:**
- Modify: `README.md`, `CLAUDE.md` (project status: 5c implemented, 5d next), `ANALYS.md` (5c implemented, date), `docs/superpowers/specs/2026-10-25-mvp-design.md` (a short "changed by the 5c spec" note with the 5c section numbers in sections 2 (goal 3), 6.5 (Move out → Split, merge confirmation, the ICAO field), 8 (no copy action, shipped-profile label), 10 (card dialog list) and 15 (closed by 5c 6.1)), `GLOSSARY.md` (add **Split** if the glossary lists Merge)

- [ ] **Step 1: Update the docs** in one pass; English except `ANALYS.md`.
- [ ] **Step 2: Run the whole suite** `dotnet test ModernMsfsAddonManager.slnx`, expect all PASS, and `dotnet build` with no new warnings.
- [ ] **Step 3: Commit** `docs: record plan 5c in README, CLAUDE.md, the spec and the analysis`.
- [ ] **Step 4: Hand over the manual check to Rickard** (his part, about 15 minutes): the lists in Task 8 Step 3 and Task 9 Step 5 on his machine with `--data-dir`.

---

## Carried review items → tasks

| Item | Task |
|---|---|
| 8, R2-5, R4-1, R5-2 (refs resolved on the shown board, `ArgumentException` → message, gone → "The card is no longer there.") | 4 |
| 9, R3-3, R5-3 (`Change<T>`, `AddCodeAsync` signature, `CardEditor` in `AppLinks`) | 4 |
| 11 (dimmed `.off` rows from profile Packages with `Disabled`) | 5 |
| 13 (session tests: success, locked file, gone card; suggestions) | 4, 7 |
| R2-9 (`Process.Start` with `UseShellExecute`, exception lights the icon) | 6 |
| R3-1 (`CardPackage.Airports` + builder test) | 2 |
| R3-5 (explicit save/discard close intent, no save on unload) | 6 |
| R3-10 (`ThumbnailCache` and a deleted file) | 3 |
| R4-9 (dialog gets `loadImage`; null → tab placeholder) | 6, 8 |
| R4-10 (`Warning16`) | Global Constraints, 8 |
| R4-11 (release capture, cancel the click, confirm after `DoDragDrop`) | 9 |
| R5-8 (fixed header, one scroll area) | 8 |
