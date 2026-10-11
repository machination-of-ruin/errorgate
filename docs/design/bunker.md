# >>> BUNKER <<<

A hidden Soviet-style nuclear bunker with tier 3 loot. It is a secret: nothing announces it. Players find the hatch by exploring and need one of the few keys. Decided with the project owner.

## Rules

- **Kuznetsk only.** Other maps get no bunker (`errorgate.bunker.maps`).
- **The surface map is not edited.** The hatch is spawned at run time at round start.
- **Hatch placement:** open ground (7x7 of free floor), reachable on foot from the spawn points, far from every point of interest (loot spawners, spawn points, mob spawners; 30 tiles, lowered step by step when the map has no such place). It is a point of interest on its own, outside buildings and remote.
- **Three keys** (`BunkerKey`, access tag `Bunker`), in the Kuznetsk loot table only, military tiers 1 and 2. The loot cap is 3: when a key despawns or is destroyed, `DespawnItemComponent` refunds the slot and the spawners put a new one somewhere else.
- **The sequence (the vestibule controller is its state machine):**
  1. The key is used on the surface entrance: a siren sounds outside (`errorgate.bunker.cycle_seconds`, 15 s) and every door is locked.
  2. The surface entrance and the hatch up (west wall of the vestibule) are open, red lights turn blue: people pass freely, with whatever they pull. They lock again by themselves after `errorgate.bunker.outer_open_seconds` (90 s) if nobody presses the console.
  3. Someone presses the console in the middle of the vestibule (by the north wall): the siren sounds inside and every door is locked.
  4. The door into the bunker is open, the hatches are shut.
  5. The console from inside, or the key outside, starts the cycle again: the console leads back to step 2, the key to step 1 and then 2. If everything timed out the console also leads to step 2, so nobody is trapped inside.
- **The bunker door is a normal airlock** (safety on, it never closes on anyone, no damage) that never closes by itself. Only the state change that locks it closes it: whoever stands in the doorway is gibbed on the spot, loose things are removed, it shuts once and is bolted. It is replaced by a shut door if it still does not shut.
- **Hatch look:** the door sprite is always shut, red lights while locked, blue lights while usable. Mobs cannot follow through the hatch.
- One bunker per round, on a map of its own (own grid, gravity, breathable air, dim light), deleted with the round. Walls are indestructible: dark concrete (`BunkerWallConcrete`) for rooms, grey brick for technical tunnels.
- Text: only the descriptions of the hatches, two short popups on the locked hatches and the name and description of the key (approved by the project owner). The console and the rest speak through sounds and lights.

## Layout (generated, `BunkerLayout`)

A seeded generator in plain C# (`bunkergen [seed]` repeats a layout, the seed is in the log), not the SS14 dungeon pack system: it needed fixed room sizes and map-file templates, and the rules here (loot per room, a boss in every military room, barrels, lights) are easier to keep in code.

- A grid of 5 by 4 cells of 20 tiles; the entrance cell (the first room, `Decon`) is right behind the vestibule. 16 to 20 rooms: Decon, Barracks x2, Mess, Control, Generator, Reactor, Medical, Storage x2, AmmoDepot, Armory, Archive and 3 to 7 small Shaft junction rooms. The reactor and the armory are always deep (the two last columns).
- Rooms are joined by tunnels **three tiles wide** (cheapest way to join all of them, plus some loops). All walls are indestructible: concrete, and **grey brick on the walls of some rooms only (never along tunnels)**.
- **Loot:** every room has 2 to 5 tier 3 loot spawners of fitting locations (armory 4 military plus 1 security, medical 3 medical, generator 3 industrial, and so on; shafts have 1). They draw from the same loot manager as the surface.
- **Bosses:** every room with military loot (barracks, ammo depot, armory) has one `MobWalkerBoss`: the second military tier armored walker scaled up (300 health, double damage, larger) that always drops tier 3 military loot. Other mobs are walkers and runners only, in rooms and tunnels.
- **Radiation:** `BunkerRadiationBarrel` (a child of `YellowBarrelFull`, intensity 20) in every room and about every 6 tiles along the tunnels: without a suit a person dies in seconds anywhere. The radiation suit (`ClothingOuterSuitRad`, already in the industrial and medical loot, tiers 2 and 3) takes it away.
- **Light:** only always-on emergency lights (`EmergencyLight`, like on EdgeOfEntropy), facing away from the wall they hang on. The map has a dim ambient light and nothing else.
- **Air:** every floor tile gets breathable air a moment after the build.
- The surface entrance is never placed on or beside ice tiles.

Admin commands: `bunkergen [seed]` builds a bunker now on any map (replacing this round's), `bunkerinfo` says where the hatch is. The key can be spawned from the entity menu (`BunkerKey`).

## Next

1. Playtest the layout and tune: room sizes, how much tunnel there is, barrel intensity and count, mob counts, loot density.
2. Dressing: decals, corpses and props that tell what the bunker was; doors on some rooms.
3. Sprites and sounds for the hatches, console and sirens.
