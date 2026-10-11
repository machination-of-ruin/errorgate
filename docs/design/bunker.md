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

A seeded generator in plain C# (`bunkergen [seed]` repeats a layout, the seed is in the log), not the SS14 dungeon pack system: it needed fixed room sizes and map-file templates, and the rules here (loot per room, a boss in every military room, barrels) are easier to keep in code.

- A grid of 5 by 4 cells of 20 tiles; the entrance cell (the first room, `Decon`) is right behind the vestibule. 16 to 20 rooms: Decon, Barracks x2, Mess, Control, Generator, Reactor, Medical, Storage x2, AmmoDepot, Armory, Archive and 3 to 7 small Shaft junction rooms. The reactor and the armory are always deep (the two last columns).
- **Doors, not openings.** Every room is sealed by its wall ring and has one `BunkerRoomDoor` (the unpowered, indestructible `AirlockTough` of the map, opened by hand) per link. The entrance room and the shafts are hubs, joined to each other by corridors; every other room is a dead end with one door (a quarter of them have a second one to another hub, never the armory or the reactor). Corridors are **three tiles wide**, routed around rooms (A*, few turns).
- Walls are indestructible: concrete, and **grey brick on the walls of some rooms only (never along corridors)**.
- **Each kind of room has a floor of its own** (white in medical, kitchen tiles in the mess, green circuit in control, blue circuit in the armory, and so on).
- **Interior:** columns in a mirrored pattern (a lattice, four near the corners, or two colonnades), rubble (`N14WallRockBasalt`, dug out with a pickaxe) in clumps in rooms and corridors, steel crates (cover), and the props that make the room what it is (beds, tables, generators, racks, cabinets); more props along the walls. Everything that blocks is placed only if every free tile of the room stays reachable from the doors; three tiles in front of every door stay free.
- **Reactor:** the gravity generator sprite (`BunkerReactor`) in the middle of its room, catwalks around it and from every door to it. It is radioactive (intensity 16, slope 2.5, deadly near it, mild at the walls). Barrels and the reactor carry a green point light.
- **Loot:** every room has 1 to 5 tier 3 loot spawners of fitting locations. They draw from the same loot manager as the surface.
- **Bosses:** every room with military loot (barracks, ammo depot, armory) has one `MobWalkerBoss`. Other mobs are walkers and runners only, in rooms and corridors. Walkers and runners are immune to radiation (`ErrorgateRadiationImmune`).
- **Radiation:** `BunkerRadiationBarrel` (intensity 12, slope 3: deadly without a suit for about four tiles around it) only along the corridors, at least 10 tiles apart, never within 13 tiles of the vestibule or 6 of a door. The radiation suit (`ClothingOuterSuitRad`, in the industrial and medical loot, tiers 2 and 3) takes it away. Rooms and the airlock are safe.
- **Cover in corridors:** steel crates at the corridor edges.
- **Light:** only always-on emergency lights in the corridors (`EmergencyLight`). Rooms stay dark. The map has no ambient light: pitch black.
- **Air:** every floor tile gets breathable air a moment after the build.
- **Consoles:** one in the vestibule, one just inside the bunker by the door: both run the same airlock.
- The surface entrance is never placed on or beside ice tiles, and unlocking it plays a sound at the entrance.

Admin commands: `bunkergen [seed]` builds a bunker now on any map (replacing this round's), `bunkerinfo` says where the hatch is. The key can be spawned from the entity menu (`BunkerKey`).

## Next

1. Playtest the layout and tune: room sizes, how much tunnel there is, barrel intensity and count, mob counts, loot density.
2. Dressing: decals, corpses and props that tell what the bunker was.
3. Sprites and sounds for the hatches, console and sirens.
