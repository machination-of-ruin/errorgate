# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ERRORGATE: a PvPvE survival game built on SS14, forked from WWDP (WWhiteDreamProject), itself a hard-fork of Einstein Engines / Space Station 14 (C#, RobustToolbox engine). It is a **hard fork**: code from other upstreams cannot be merged directly, only ported. The repo is an aggregation of many downstream forks' features (Goobstation, DeltaV, EE, Impstation, Corvax, NF, Shitmed, White, Lavaland, ...), each living in its own folder.

## >>> STYLE <<<

The `>>> STYLE <<<` is of utmost priority. User-facing project text (README, window title, rules, tips, server info, PR template, announcements) is written in the ERRORGATE voice: ALL-CAPS headings, `>>>` / `<<<` markers (e.g. `## \>>> LINKS` in markdown), terse and ominous, glitch / error / ruin / entropy themes. Match the tone of `README.md` and the title "MACHINATION OF RUIN >>> ERRORGATE <<< RISE ON THE EDGE OF ENTROPY". Do not flatten it into plain corporate prose; keep technical accuracy (build steps, versions) inside it.

## Notifications

Desktop and push notifications are exempt from `>>> STYLE <<<` and any persona.

- Plain, polite, professional language. No persona, slang or casual chat tone.
- Report status only: what finished, what failed, what needs a decision.
- Never phrase a notification as a command ("Review X", "Commit Y"). Use neutral wording instead: "Draft ready for review", "Changes are not yet committed".
- One short sentence. No emoji.

## Design

Start with `docs/design/VIBE.md`: the intended experience (pure-horror wasteland ravaged by a broken AI GOD) that every feature must serve. Vision, direction (strip the space station, enhanced combat, LLM-driven "AI GOD" gamerule) and open questions are in `docs/design/ROADMAP.md`. Read it, and any `docs/design/<feature>.md`, before working on a feature.

## Setup and commands

After cloning, initialise submodules (`RobustToolbox` is WWDP's fork on branch `hotfix`; `StyleSheetify`). Don't edit `RobustToolbox` as part of normal content work.

```bash
git submodule update --init --recursive
dotnet build -c Debug                      # whole solution (SpaceStation14.sln)
dotnet build Content.Server -c Debug       # one side only
dotnet run --project Content.Server        # then, in another shell:
dotnet run --project Content.Client
dotnet test Content.Tests/Content.Tests.csproj -c DebugOpt
dotnet test Content.Tests -c DebugOpt --filter "FullyQualifiedName~SomeTestName"   # single test
dotnet run --project Content.YAMLLinter -c DebugOpt                                # validates prototypes/YAML
dotnet test Content.IntegrationTests -c DebugOpt                                   # slow; filter it
```

`Scripts/sh/*.sh` and `Scripts/bat/*.bat` wrap these (`buildAllDebug`, `runQuickAll`/`runQuickServer`/`runQuickClient`, `runTests`, `runTestsIntegration`, `runTestsYAML`); test scripts write logs to `Scripts/logs/`. The README states .NET SDK 9.0.101, but newer SDKs (10.x) are in use locally. `.envrc` exists for Nix users.

After changing prototypes (`Resources/Prototypes/**`), run the YAML linter. CI (`.github/workflows`) also validates RSIs, map files, RGAs, and runs the YAML linter and tests.

## Architecture

- **Standard SS14 split:** `Content.Shared` (prediction, components, networked state, shared systems), `Content.Server`, `Content.Client`. Logic goes in `EntitySystem`s, state in components; anything networked or predicted belongs in Shared. `Content.Shared.Database` / `Content.Server.Database` hold DB models and EF migrations.
- **Fork-module folders:** each downstream feature set sits in an underscore-prefixed folder, repeated across layers, so changes for one feature span several roots. For example `_Goobstation` exists in `Content.{Server,Shared,Client}/_Goobstation`, `Resources/Prototypes/_Goobstation`, `Resources/Textures/_Goobstation`, and `Resources/Locale/<lang>/_Goobstation`. Prefer adding new work inside the matching `_<Fork>` folder and keep edits to non-prefixed upstream SS14 code minimal. When changing a feature, check all of those roots.
- `Content.Goobstation.Client` and `Content.Goobstation.Shared` are separate assemblies for ported Goobstation code (no Server counterpart there).
- **Resources:** prototypes (`Resources/Prototypes`), locale (Fluent `.ftl`), textures as `.rsi` directories, maps, and audio. ERRORGATE is **en-US only**: add new strings to `Resources/Locale/en-US` and do not add or maintain `ru-RU`/`nl-NL` entries.
- **Other projects:** `Content.Tests` (unit), `Content.IntegrationTests` (spins up real client/server), `Content.YAMLLinter`, `Content.MapRenderer`, `Content.Packaging`, `Content.Replay`, `Content.Benchmarks`. `bin/` holds build output.

## Contribution conventions

- Commit/PR titles use a bracketed tag, e.g. `[ADD] ...`, `[fix] ...`, `[Resprite] ...`, often bilingual (English / Russian), followed by the PR number.
- PRs (template in `.github/PULL_REQUEST_TEMPLATE.md`) carry a `:cl:` block with `add:`, `remove:`, `tweak:`, `fix:` entries. A bot generates "Automatic Changelog Update" commits into `Resources/Changelog/*.yml`, so don't hand-edit those changelog files.
- Contributions fall under the repo's ICLA (`LICENSE-ICLA-*.txt`); code is AGPLv3 / MIT (see `LICENSE-*`).
