# ERRORGATE: Design Overview

General notes. Each feature has its own `docs/design/<feature>.md`; read it before working on that feature. Decisions below were made by the project owner. Anything listed under "Open" is undecided: ask, don't invent.

## Vision

**Read [VIBE.md](VIBE.md) first.** It defines the experience every feature serves: a pure-horror wasteland ravaged by a broken AI GOD that is still running.

ERRORGATE is an immersive PvPvE survival experience set in a harsh world. There is no obligation to roleplay. Players are free to make allies or enemies and to enjoy fights with enhanced combat mechanics.

Inspirations: DayZ, Rust, Project Zomboid, Cataclysm: DDA.

## Direction

1. **Strip the space station.** Remove most station-specific content (departments, shuttles, round flow, roles that only make sense on a station) and reshape the game into a gritty multiplayer survival simulator.
2. **Enhance combat.** Deeper, more lethal combat mechanics to support PvP and PvE fights.
3. **AI "GOD".** An in-game gamerule with a system that tracks player behaviour and uses an LLM to formulate responses, set goals, and punish or reward players.

## Decisions

| Area | Decision | Doc |
|---|---|---|
| Server model | Long rounds that end in a map wipe. Wipe length is open. | this file |
| Setting | A ruined planet surface (Lavaland-style terrain) with ruins of old stations and facilities. | [maps.md](maps.md) |
| Death | Dead players wait in the death void until they choose to respawn. Respawn as a new character with nothing; the body and gear stay where they died. Respawn cooldown is a server CVar, default 0. Respawn at fixed spawn zones. | [death.md](death.md) |
| Core loop | Needs, crafting, looting/scavenging, and cooperation forced by shared objectives and scarcity. | [survival-loop.md](survival-loop.md) |
| Building | Full creative freedom, limited resources. Bases are existing ruins, held physically, raidable anytime. | [survival-loop.md](survival-loop.md) |
| Loot | Finite per wipe, via an existing DayZ-style system (to be ported). | [survival-loop.md](survival-loop.md) |
| Combat | Lethality, firearms depth, melee depth, dangerous PvE enemies. | [combat.md](combat.md) |
| AI GOD | Both an event director for the world and a judge of individual players. Whitelisted actions (events, messages, items, bodies), loose strength limits. Pluggable LLM provider. | [ai-god.md](ai-god.md) |

## Constraints

- **en-US only.** No localizations. Do not add or maintain `ru-RU` or `nl-NL` strings. This replaces the usual WWDP practice.
- Hard fork: upstream code is ported, not merged.
- **Keep all `_Fork` modules for now.** Do not strip modules wholesale; remove or change only what breaks or conflicts with ERRORGATE, and note removals in the commit.

## Open questions

- Wipe length (hours, days, weekly) and whether world state must survive server restarts within one wipe.
- Per-feature open questions are listed in each feature doc.
