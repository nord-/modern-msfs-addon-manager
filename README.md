# Modern MSFS Addon Manager

A modern addon manager for Microsoft Flight Simulator 2020, built with .NET.

> **Status:** in development. The link engine and the content scan are done, as a headless engine with a command-line tool; the app itself isn't built yet. The design is in the [MVP spec](docs/superpowers/specs/2026-10-25-mvp-design.md), and [ANALYS.md](ANALYS.md) (Swedish) holds the analysis and decisions.

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
