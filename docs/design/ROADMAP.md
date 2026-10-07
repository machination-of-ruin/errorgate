# ERRORGATE: Design Overview

General notes only. Details will be added per feature in `docs/design/<feature>.md`.

## Vision

ERRORGATE is an immersive PvPvE survival experience set in a harsh world. There is no obligation to roleplay. Players are free to make allies or enemies and to enjoy fights with enhanced combat mechanics.

Inspirations: DayZ, Rust, Project Zomboid, Cataclysm: DDA.

## Direction

1. **Strip the space station.** Remove most station-specific content (departments, shuttles, round flow, roles that only make sense on a station) and reshape the game into a gritty multiplayer survival simulator.
2. **Enhance combat.** Deeper, more lethal combat mechanics to support PvP and PvE fights.
3. **AI "GOD".** An in-game gamerule with a system that tracks player behaviour and uses an LLM to formulate responses, set goals, and punish or reward players.

## Constraints

- **en-US only.** No localizations. Do not add or maintain `ru-RU` or `nl-NL` strings. This replaces the usual WWDP practice.
- Hard fork: upstream code is ported, not merged.

## Open questions

- Which of the many `_Fork` modules to keep, drop, or rework (Goobstation, DeltaV, EE, Impstation, Lavaland, NF, Shitmed, etc.).
- World and setting: map structure, persistence, round vs. persistent server.
- Core survival loop: needs, crafting, building, loot, death penalty.
- AI GOD: which LLM provider, how server-side calls are made, what player events are tracked, what actions it may take, rate and cost limits, moderation and safety.
