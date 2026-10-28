# Never move user files: junctions plus own state reconciled against Community

Packages stay where the user put them in their Libraries. Turning a Package on or off only creates or removes directory junctions in Community. The app keeps its own state (on/off choices, Addons with their anchors, manual Airport links, card overrides) but reconciles it against Community on every start. If they differ, it warns and lets the user sync in either direction, with "keep Community" preselected.

## Considered Options

- **Move Packages into an app-owned library** (as BOB Dispatcher does). Rejected: uninstalling the app or switching tools leaves Community pointing at nothing, and users believe their files are damaged.
- **Community as the only source of truth, no own state.** Rejected: Addons and the user's merges, moves and edits of their cards, manual Airport links and the user's on/off choices while the sim runs can't be derived from the folder alone.
- **Own state only, never reconciled.** Rejected: other tools (Addons Linker, installers) change Community, and the app would silently drift from what the sim actually loads.

## Consequences

- The app can run alongside MSFS Addons Linker during migration, and existing junctions are adopted as they are.
- Removing the app never breaks Community.
- Junctions don't need admin rights, unlike symbolic links.
