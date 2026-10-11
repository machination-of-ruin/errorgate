# >>> BUNKER <<<

A hidden Soviet-style nuclear bunker with tier 3 loot. It is a secret: nothing announces it. Players find the hatch by exploring and need one of the few keys. Decided with the project owner.

## Rules

- **Kuznetsk only.** Other maps get no bunker (`errorgate.bunker.maps`).
- **The surface map is not edited.** The hatch is spawned at run time at round start.
- **Hatch placement:** open ground (7x7 of free floor), reachable on foot from the spawn points, far from every point of interest (loot spawners, spawn points, mob spawners; 30 tiles, lowered step by step when the map has no such place). It is a point of interest on its own, outside buildings and remote.
- **Three keys** (`BunkerKey`, access tag `Bunker`), in the Kuznetsk loot table only, military tiers 1 and 2. The loot cap is 3: when a key despawns or is destroyed, `DespawnItemComponent` refunds the slot and the spawners put a new one somewhere else.
- **The sequence (the vestibule controller is its state machine):**
  1. The key is used on the surface hatch: a siren sounds outside (`errorgate.bunker.cycle_seconds`, 15 s) and every door is locked.
  2. The surface hatch and the hatch up (west wall of the vestibule) are open, red lights turn blue: people pass freely, with whatever they pull. They lock again by themselves after `errorgate.bunker.outer_open_seconds` (90 s) if nobody presses the console.
  3. Someone presses the console in the middle of the vestibule (by the north wall): the siren sounds inside and every door is locked.
  4. The door into the bunker is open, the hatches are shut.
  5. The console from inside, or the key outside, starts the cycle again: the console leads back to step 2, the key to step 1 and then 2. If everything timed out the console also leads to step 2, so nobody is trapped inside.
- **The bunker door always shuts before it is bolted, never closes by itself, and has no safety:** it crushes what stands in the way (living people), removes bodies and loose things left in the doorway, and is replaced by a shut door if it still does not shut.
- **Hatch look:** the door sprite is always shut, red lights while locked, blue lights while usable. Mobs cannot follow through the hatch.
- One bunker per round, on a map of its own (own grid, gravity, breathable air, dim light), deleted with the round. Walls are indestructible: dark concrete (`BunkerWallConcrete`) for rooms, grey brick for technical tunnels.
- Text: only the descriptions of the hatches, two short popups on the locked hatches and the name and description of the key (approved by the project owner). The console and the rest speak through sounds and lights.

## Built (step 1)

Key, hatch, vestibule, a fixed small test bunker (vestibule, corridor, three rooms with tier 3 loot spawners). Code in `Content.Server/_ERRORGATE/Bunker/`, prototypes in `Resources/Prototypes/_ERRORGATE/Bunker/`.

Admin commands: `bunkergen` builds a bunker now on any map (replacing this round's), `bunkerinfo` says where the hatch is. The key can be spawned from the entity menu (`BunkerKey`).

## Next

1. Generated layout: SS14 dungeon generation with bunker room templates (15 to 25 rooms).
2. Radiation sources, existing dangerous mobs, tier 3 loot caps; radiation suits in the industrial loot areas.
3. Polish: sprites for the hatch and ladder, sound, lighting.
