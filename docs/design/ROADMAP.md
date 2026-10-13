# ERRORGATE: Design Overview

General notes. Each feature has its own `docs/design/<feature>.md`; read it before working on that feature. Decisions below were made by the project owner. Anything listed under "Open" is undecided: ask, don't invent.

## Vision

**Read [VIBE.md](VIBE.md) first.** It defines the premise and the experience every feature serves: a pure-horror wasteland where a broken AI, MACHINATION OF RUIN, tries to rebuild civilization from the last human consciousnesses and cannot understand HUMAN ERROR. The ERRORGATE is an unachievable ideal. She never guides the players.

ERRORGATE is an immersive PvPvE survival experience set in a harsh world. There is no obligation to roleplay. Players are free to make allies or enemies and to enjoy fights with enhanced combat mechanics.

Inspirations: DayZ, Rust, Project Zomboid, Cataclysm: DDA.

## Direction

0. **World and factions rework (next).** A fixed web of procedurally generated locations that reset on a storm timer, and three factions with reputation, bases, traders and NPC squads. See [world.md](world.md), [factions.md](factions.md), [structures.md](structures.md). The AI GOD waits until this is implemented.
1. **Strip the space station.** Remove most station-specific content (departments, shuttles, round flow, roles that only make sense on a station) and reshape the game into a gritty multiplayer survival simulator.
2. **Enhance combat.** Deeper, more lethal combat mechanics to support PvP and PvE fights.
3. **AI "GOD".** An in-game gamerule with a system that tracks player behaviour and uses an LLM to formulate responses, build conditions, and punish or reward players. It never gives goals or orders.

## Decisions

| Area | Decision | Doc |
|---|---|---|
| Server model | One-week wipe, no restarts. Locations reset every 6 to 12 hours with a deadly storm. | [world.md](world.md) |
| Setting | A fixed web of finite, themed, procedurally generated locations with transitions (tier 1 village and shore, tier 2 snow city and a Military base, tier 3 bunker-laboratory). EDGE OF ENTROPY and Kuznetsk are retired. | [world.md](world.md), [maps.md](maps.md) |
| Factions | Three: Military, Knights, Cultists. Reputation per account, bases, traders, own currency, roles to spawn as. Military first. | [factions.md](factions.md) |
| Death | Dead players wait in the death void until they choose to respawn. Respawn as a new character with nothing; the body and gear stay where they died. Respawn cooldown is a server CVar, default 0. Respawn at fixed spawn zones. | [death.md](death.md) |
| Core loop | Needs, crafting, looting/scavenging, and cooperation forced by shared objectives and scarcity. | [survival-loop.md](survival-loop.md) |
| Building | Full creative freedom, limited resources. Bases are existing ruins, held physically, raidable anytime. | [survival-loop.md](survival-loop.md) |
| Loot | Finite per wipe, via the DayZ-style loot manager (ported). | [survival-loop.md](survival-loop.md) |
| Progression | Only knowledge survives death. Player-made stashes and bases. One round only. Internal implants first, psionics and spells later and carefully. | [progression.md](progression.md) |
| Pressure | She builds situations that need several people and logs the result. Never instructions. | [pressure.md](pressure.md) |
| World hazards | STALKER-style faults (heat, arc, collapse) in packs near points of interest, nearly invisible, warned by screen effects and sound. | [anomalies.md](anomalies.md) |
| Combat | Lethality, firearms depth, melee depth, dangerous PvE enemies. | [combat.md](combat.md) |
| AI GOD | (After the world and factions rework.) Both an event director for the world and a judge of individual players. Whitelisted actions (events, messages, items, bodies), loose strength limits. Pluggable LLM provider. | [ai-god.md](ai-god.md) |

## Implemented so far

Death void and respawn ([death.md](death.md)), loot manager and despawn ([survival-loop.md](survival-loop.md)), EdgeOfEntropy and Kuznetsk ([maps.md](maps.md)), human-only ([humans-only.md](humans-only.md)), hunger death, craftable campfire, distant gunfire ([combat.md](combat.md)), world anomalies ([anomalies.md](anomalies.md)).

## Constraints

- **en-US only.** No localizations. Do not add or maintain `ru-RU` or `nl-NL` strings. This replaces the usual WWDP practice.
- Hard fork: upstream code is ported, not merged.
- **Keep all `_Fork` modules for now.** Do not strip modules wholesale; remove or change only what breaks or conflicts with ERRORGATE, and note removals in the commit.
- **Psionics are not implemented (for now).** Things hidden because of that must be restored if they are: the "sacrifice Psionics" altar examine text (`SharedSacrificialAltarSystem.OnExamined`, marked `// ERRORGATE`).

## Open questions

- Whether reputation survives the weekly wipe (Proposed: yes, with a reset command).
- Per-feature open questions are listed in each feature doc.
