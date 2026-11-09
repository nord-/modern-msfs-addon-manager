# Modern MSFS Addon Manager

A modern addon manager for Microsoft Flight Simulator 2020, built with .NET.

> **Status:** in development. The link engine, the content scan and the card wall are done, as a headless engine with a command-line tool. The app turns cards on and off and removes Packages that are gone; changes made in Community outside the app are adopted silently, and nothing changes while MSFS runs. Card editing, GSX and settings come in the next versions. The design is in the [MVP spec](docs/superpowers/specs/2026-10-25-mvp-design.md), and [ANALYS.md](ANALYS.md) (Swedish) holds the analysis and decisions.

## Running the app

Run it with `dotnet run --project src/AddonManager.App`. On first start the app asks for the Libraries to add per menu section. Add `-- --data-dir <folder>` to keep the database in another folder (`<folder>\userdata.db`) instead of `%LOCALAPPDATA%\ModernMsfsAddonManager`, so a development run never touches your real file.

## Command-line tool

`src/AddonManager.Cli` is a developer tool for checking the engine against a real installation: `status`, `scan`, `cards` and `export-content`. Each command uses the Packages root you picked in the app when one is stored. `cards` follows Community in memory, so its lights match the app's, and it saves no user data.

## Goals

- Toggle addons on and off by linking packages into the Community folder (directory junctions). Your files stay where you put them.
- Addons shown as cards under Scenery, Aircraft and Other, grouped per airport and per aircraft, which you can merge, move and rename.
- Map of installed airport sceneries and the airports built into MSFS.
- GSX Pro profile status on each airport card: installed, missing or two profiles.
- Find scenery for airports you don't have yet (links to flightsim.to and other stores).
- A clean, modern user interface.

## Scope

- MSFS 2020: required.
- MSFS 2024: planned, not part of the first version.

## License

[MIT](LICENSE)
