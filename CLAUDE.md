# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

Plans 1 (link engine), 2 (content scan), 3 (the card wall) and 3b (card model) are done; their sim-machine runs were the handover issues #15 and #17, and plan 4 (GSX) is next — see `docs/superpowers/plans/`. Spec: `docs/superpowers/specs/2026-10-25-mvp-design.md`. `ANALYS.md` (Swedish) is the source of truth for background, decisions and open questions; read it before proposing designs. When a decision is made, record it there (with date).

## Commands

- Build: `dotnet build ModernMsfsAddonManager.slnx`
- Test all: `dotnet test ModernMsfsAddonManager.slnx`
- Test subset: `dotnet test ModernMsfsAddonManager.slnx --filter FullyQualifiedName~<Name>`
- Status against the real machine (it never creates `userdata.db` and never writes user rows: a missing file means no Libraries; opening an existing file may touch its cache tables and WAL side files, and resets the cache tables when their version differs): `dotnet run --project src/AddonManager.Cli -- status [--library <path>[=sections]]`
- Content scan against the real machine (writes only the cache tables of `%LOCALAPPDATA%\ModernMsfsAddonManager\userdata.db` and `--token-links`; on a machine without the file it creates it with an empty user schema, but never writes user rows): `dotnet run --project src/AddonManager.Cli -- scan [--full] [--token-links <file>] [--library <path>[=sections]]`
- Card wall against the real machine (`scan` and `cards` write only the cache tables of `%LOCALAPPDATA%\ModernMsfsAddonManager\userdata.db`, and on a machine without the file create it with an empty user schema, but never write user rows; user data is never saved, so the CLI never stores sections and Packages without a stored section are re-read on every run): `dotnet run --project src/AddonManager.Cli -- cards [--full] [--packages] [--library <path>[=sections]]`
- Export the content fixture (writes only the cache tables, creating the file with an empty user schema when missing, and `--out`): `dotnet run --project src/AddonManager.Cli -- export-content --out <file> [--library <path>[=sections]]`

## What we're building

A modern addon manager for **MSFS 2020** (MSFS 2024 is out of MVP scope), replacing MSFS Addons Linker / BOB Dispatcher for Rickard's own use. Repo: https://github.com/nord-/modern-msfs-addon-manager (public, MIT).

Priority of addon types: 1) **Scenery** (airports, map, GSX profiles), 2) **Aircraft**, 3) everything else lumped as "Övrigt" with plain on/off only.

## Decided constraints

- **.NET 10** minimum; keep it easy to move to .NET 11.
- **License: MIT.** Dave's GSX Profile Manager (GPL-3.0) may be read as reference only — never copy or translate its code into C#. Ideas, file formats and paths are fine to reuse.
- **Never move the user's files implicitly.** Addons stay where the user keeps them (e.g. `C:\Sim\Addons\…`); enable/disable = create/remove a **directory junction** in Community (no admin needed, unlike symlinks). Uninstalling or switching tools must never leave Community broken.
- Community will contain a mix the app must classify and respect: our junctions, foreign junctions (Addons Linker, Orbx, FSDT, Neofly), symlinks (SayIntentions), real folders, broken links, stray files. Existing Addons Linker junctions must migrate painlessly.
- MSFS reads Community only at startup: detect a running sim, show "takes effect on next start", and don't remove links while it runs.
- Keep sim-specific parts (paths, content formats, built-in airports) behind replaceable abstractions so MSFS 2024 can be added later.
- A Package belongs to an Airport via an **airport record in its BGL files**, the Airport code as a separate token in its name/title, or a manual link — never from loose name heuristics. See the design decisions in `ANALYS.md`. Scan GSX profiles both in `%APPDATA%\Virtuali\GSX\MSFS\*.ini` (Airport code = leading 3–4 letters/digits followed by `-`, `_`, `.` or end of name, case-insensitive; see spec section 8) and inside scenery folders.
- Map tiles must work without an API key. Finding uninstalled scenery = deep links to flightsim.to/SimMarket (no download APIs exist; no scraping).

## Tech (decided, see `docs/adr/0003-wpf-with-webview2-leaflet-map.md`)

C# / WPF + WPF-UI. Map via WebView2 + Leaflet with tiles that need no API key. SimConnect managed wrapper when needed. Dark theme only, English UI only (no localization). Shipped as a framework-dependent single-file exe on GitHub Releases.

## Repo conventions

- Line endings are CRLF (`.gitattributes`: `* text=auto eol=crlf`).
- `ANALYS.md` is Swedish; `README.md` is English.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `nord-/modern-msfs-addon-manager` via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` and `docs/adr/` at the repo root (created lazily). See `docs/agents/domain.md`.
