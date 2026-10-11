# >>> BUNKER <<<

A hidden Soviet-style nuclear bunker with tier 3 loot. It is a secret: nothing announces it. Players find the hatch by exploring and need one of the few keys. Decided with the project owner.

## Rules

- **Kuznetsk only.** Other maps get no bunker (`errorgate.bunker.maps`).
- **The surface map is not edited.** The hatch is spawned at run time at round start.
- **Hatch placement:** open ground (7x7 of free floor), reachable on foot from the spawn points, far from every point of interest (loot spawners, spawn points, mob spawners; 30 tiles, lowered step by step when the map has no such place). It is a point of interest on its own, outside buildings and remote.
- **Three keys** (`BunkerKey`, access tag `Bunker`), in the Kuznetsk loot table only, military tiers 1 and 2. The loot cap is 3: when a key despawns or is destroyed, `DespawnItemComponent` refunds the slot and the spawners put a new one somewhere else.
- **The hatch relocks.** The key unlocks it for `errorgate.bunker.hatch_open_seconds` (15 s), anyone can use it meanwhile, then it locks again. Using it takes the user and whatever they pull down.
- **One bunker per round, on a map of its own** (own grid, gravity, breathable air, dim light). It is deleted with the round.
- **The vestibule is a timed airlock between the maps** so nobody fights across them. Coming down the hatch or walking in from the bunker starts a cycle (`errorgate.bunker.cycle_seconds`, 15 s): both exits are locked, red lights turn and a siren sounds. Then the exit on the far side opens (the door into the bunker, or the ladder up). Mobs cannot follow through the hatch.
- No text anywhere: the hatch, the ladder and the key speak only through sounds and lights.

## Built (step 1)

Key, hatch, vestibule, a fixed small test bunker (vestibule, corridor, three rooms with tier 3 loot spawners). Code in `Content.Server/_ERRORGATE/Bunker/`, prototypes in `Resources/Prototypes/_ERRORGATE/Bunker/`.

Admin commands: `bunkergen` builds a bunker now on any map (replacing this round's), `bunkerinfo` says where the hatch is. The key can be spawned from the entity menu (`BunkerKey`).

## Next

1. Generated layout: SS14 dungeon generation with bunker room templates (15 to 25 rooms).
2. Radiation sources, existing dangerous mobs, tier 3 loot caps; radiation suits in the industrial loot areas.
3. Polish: sprites for the hatch and ladder, sound, lighting.
