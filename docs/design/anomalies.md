# >>> ANOMALIES <<<

World faults: STALKER-style environmental hazards for KUZNETSK. They make travel between points of interest risky and give the wasteland something to fear that is not a player or a mob. In the fiction they are places where a rebuild failed (see [VIBE.md](VIBE.md)). Status: **finished for now** (playtested and tuned over several rounds).

## Design rules

- Lethal to the careless, avoidable with knowledge and preparation.
- **Damage radius <= screen effect radius <= about 8 tiles (the throwing range).** A player who does not rush can notice a fault, and test it with a thrown item, before it hurts.
- The faults themselves are nearly or fully invisible. The screen effects and ambient sound are the warning.
- A fault is **revealed** for 8 seconds when something is thrown into it or when it hurts someone (sprite, light and a short sound).
- Faults are not entities you can fight: no physics, no health, explosions skip them.

## The three faults

| Fault | Danger | Behaviour | Warning |
|---|---|---|---|
| Heat | 5 tiles | Fire inside the radius, 6 damage/s at the edge rising to 30 inside 2.5 tiles. Invisible until revealed (red sphere). Fireball sound on every damage tick. | Heat haze out to 8 tiles, `fireplace.ogg` hum (8 tiles). |
| Arc | 4 tiles | Fires at once at the first living thing or thrown object in reach (45 shock damage, 0.6 s stun, 2.5 s immunity so it never stunlocks), then recharges 5 s. A bolt only ever hits a target, nothing flashes at empty ground. | Screen static out to 6.5 tiles, `emf_buzz.ogg` hum. |
| Collapse | pull 5.5, crush 2 | Cycles off (about 7 s, invisible, harmless) and on (about 12 s). While on it waits behind a faint lens until a living thing or a thrown or moving object comes within 5.5 tiles, then it pulls, crushes and shows a wide lens. It holds on to whoever it caught until they are dead or gone. Mobs are dragged at 0.6 to 4.5 tiles/s (a walker cannot leave the inner 2 to 3 tiles, a sprinter can). | Desaturation out to 9 tiles, singularity hum. |

Damage lines in the combat log: "THE HEAT FAULT BURNS YOU!", "AN ARC FAULT TEARS THROUGH YOU!", "THE COLLAPSE FAULT CRUSHES YOU!".

## Placement (`AnomalyField` component)

Put the component on a grid (or a map) to place faults at map init. `spawnanomalies [grid|map uid] [count]` and `listanomalies` (grouped by pack, shows passability) are admin commands.

- `Count` is the total number of faults. Kuznetsk asks for 40.
- Faults come in **packs of one kind**, 1 to 5 each (`PackSizeMin`/`PackSizeMax`). Keep the maximum at 5: the singularity shader draws only five distortions at once, so a bigger pack of collapse faults leaves some active ones with a sprite and no lens.
- Spacing inside a pack uses the danger radii: danger zones never overlap, at least 4 tiles between centres, and a random extra gap (70% of the time 1.5 to 3 tiles, 30% 0 to 1) so most packs can be threaded and some cannot.
- Packs sit on tiles that can be **walked to from the spawn points** (tile exact flood fill, solid things stop it, doors do not), so nothing lands behind the mountain ring around the playable area.
- Only `FreeRadius` of clear floor is needed (Kuznetsk 1), so faults can stand in alleys and yards. A danger zone ignores walls and can reach into buildings.
- Pack centres: 25+ tiles from spawn points, 8 to 40 tiles from loot spawners, 30+ tiles from each other (15 on a second pass). Loot spawners are grouped into clusters and routes between clusters and from spawns are traced, and pack centres on those routes are tried first.

## Code map

- `Content.Shared/_ERRORGATE/Anomalies/`: `ErrorgateAnomalyComponent` (kind, radii, damage, reveal, on/off cycle), `SharedAnomalyDistortionSystem`.
- `Content.Server/_ERRORGATE/Anomalies/`: `AnomalyField*` (placement, commands), `ErrorgateAnomalySystem` (damage tick, reveal, cycle), `HeatFault*`, `ArcFault*`, `CollapseFault*`.
- `Content.Client/_ERRORGATE/Anomalies/`: haze, static and desaturation overlays, `AnomalyVisualSystem` (reveal, lens strength).
- `Resources/Prototypes/_ERRORGATE/Anomalies/`, `Resources/Textures/_ERRORGATE/Shaders/anomaly_static.swsl`, `Resources/Locale/en-US/_ERRORGATE/anomalies.ftl`.
- Tests: `AnomalyFieldTest`, `CombatLogsTest.WorldFaultsHaveTheirOwnLine`. The log of tuning rounds is in `docs/port/playtest-todo.md`.
- Upstream edits (marked `// ERRORGATE`): `SingularityDistortionComponent` access list, `FireVisualizerSystem` (no light spawn in `ComponentInit`).
