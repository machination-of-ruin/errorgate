# >>> STRUCTURES <<<

The hand-made pieces the world generator ([world.md](world.md)) places in each location. The generator makes terrain, roads and corridors; **buildings come from here**. The project owner builds them in the map editor. The generator picks them by footprint and theme.

Everything in this file is **Proposed** except the themes, which are **Decided**: 1 Village and Shore Resort, 2 Snow City, 2.1 Military Base, 3 Vast Underground Bunker-Laboratory.

## How a structure is built

- **One structure is one small grid**, saved as its own file under `Resources/Maps/_ERRORGATE/Structures/<theme>/<name>.yml`. The generator loads it with `TryLoadGrid` at a chosen spot (rotated in steps of 90 degrees), the same way the existing ruins and dungeon rooms load. The base is **not** a structure: it is a whole hand-built map.
- **Footprint classes** (width x height in tiles, **including** the outer wall, not the apron):

| Class | Size | Typical use |
|---|---|---|
| XS | 5 x 5 | well, kiosk, lighthouse, barricade |
| S | 8 x 6 | small house, shed, bungalow |
| M | 10 x 8 | shop, cafe, garage |
| L | 14 x 10 | church, school, warehouse, police |
| XL | 18 x 14 | hotel, hospital, apartment block, reactor hall |
| XXL | 24 x 18 | factory |

  Variants of one class may differ in shape (L-shapes are fine), but the structure must fit **inside its class rectangle**. The generator never overlaps two rectangles.
- **Apron:** one tile of plain floor all around the rectangle (the builder draws it and the generator keeps it free), so a door never opens into a wall.
- **Doors on the edge** of the rectangle are the entrances. The generator joins roads and corridors to them. Mark at least one entrance per structure and put it in the **middle of a side** where you can. Structures are rotated by the generator, so do not depend on a compass direction.
- **No absolute positions.** The grid's bottom-left tile is (0, 0). Nothing outside the rectangle.
- **Do not place:** loot items, ground items, mobs, power, atmos devices. Only **markers** (below), furniture, walls, doors and decals. The generator and the loot manager fill the rest.
- **Light and air** follow the location (bunker-laboratory: dark, air filled by the generator). Do not rely on structure lights unless the theme asks for them.

## Markers (to be created)

| Marker | Meaning | Notes |
|---|---|---|
| `LootSpawner<Location>Tier1` | a loot spawner (existing prototypes) | Put the **tier 1** variant. The generator swaps it for the location's tier (2 or 3). Pick the **Location** (Living, Military, Security, Prison, Medical, Industrial, Kitchen, Church, School) that suits the room. |
| `StructureMobPoint` | one mob or NPC spawn | The generator picks from the location's roster. Optional tag for the kind (animal, soldier, boss) later. |
| `StructureNpcPoint` | a standing NPC (squad member, guard) | Later, with the squads. |
| `WorldTransition` | a transition to another location | Only in the gate structures. Has a link id set by the generator. |
| `StructureNoSpawn` | keep mobs out of this tile | Optional. Doorways, small rooms. |

The first three loot/mob markers already exist or are one small prototype each; `WorldTransition` is new code.

## Counts and variants

"Variants" is how many different designs to build. "Per location" is how many the generator places (the average of the range). A **first-slice minimum** is marked with `*`: enough to test the generator and the loot. Everything else can be added later without code changes.

### 1 Village and Shore Resort (tier 1, loot: Living, Kitchen, School, Church, a little Medical)

| Structure | Class | Variants | Per location | Loot location |
|---|---|---|---|---|
| Small house `*` | S | 6 (2*) | 8 to 10 | Living |
| Medium house `*` | M | 4 (2*) | 4 to 6 | Living |
| Farmstead (house and barn) | L | 2 | 1 to 2 | Living, Industrial |
| Barn, shed | S | 3 | 3 to 5 | Industrial |
| General store `*` | M | 2 (1*) | 1 to 2 | Kitchen, Living |
| Church `*` | L | 1 | 1 | Church |
| School `*` | L | 1 | 1 | School |
| Clinic | M | 1 | 1 | Medical |
| Well, fuel point, props | XS | 3 | 4 to 6 | none |
| Hotel `*` | XL | 2 (1*) | 1 | Living, Kitchen |
| Bungalow | S | 4 | 5 to 8 | Living |
| Beach bar, cafe | M | 2 | 1 to 2 | Kitchen |
| Boathouse and pier | L | 2 | 1 | Industrial |
| Lighthouse | XS | 1 | 1 | none |
| Lifeguard post, kiosk | XS | 3 | 3 to 4 | Kitchen |
| Campsite | S | 2 | 1 to 2 | Living |
| Spawn zone (open, markers) `*` | M | 2 (1*) | 1 to 2 | none |
| Gate: tunnel mouth to location 2 `*` | M | 2 (1*) | 1 | none |

About 45 designs in the full list, 14 in the minimum. A village and a shore half can be separated by the generator (a land half and a coast half).

### 2 Snow City (tier 2, loot: Industrial, Medical, Security, Prison, a little Military)

| Structure | Class | Variants | Per location | Loot location |
|---|---|---|---|---|
| Apartment block `*` | XL | 3 (1*) | 4 to 6 | Living |
| Row house | M | 3 | 4 to 6 | Living |
| Shop `*` | S/M | 4 (2*) | 6 to 8 | Kitchen, Living |
| Police station `*` | L | 1 | 1 | Security, Prison |
| Hospital `*` | XL | 1 | 1 | Medical |
| Warehouse `*` | L | 3 (1*) | 3 to 4 | Industrial |
| Garage, gas station | M | 2 | 2 to 3 | Industrial |
| Factory (limited access) | XXL | 1 | 1 | Industrial, Military |
| School | L | 1 | 1 | School |
| Church | L | 1 | 1 | Church |
| Checkpoint | S | 3 | 3 to 4 | Military, Security |
| Substation | S | 2 | 2 | Industrial |
| Barricades, wrecks | XS | 4 | 6 to 10 | none |
| Bus depot | L | 1 | 1 | Industrial |
| Gate: tunnels to locations 1 and 3 `*` | M | 2 (2*) | 2 | none |
| Gate: checkpoint to the Military base `*` | L | 1 | 1 | Military |

About 40 designs in the full list, 12 in the minimum.

### 2.1 Military Base (hand-built map, not a pool)

A whole map, built by hand, that never resets. It needs at least:

- A **gate** at the transition from location 2, a perimeter wall, and guard posts.
- The **spawn room** where players of the Military role appear.
- The **trader's stand**, the **task giver's stand**, and a **stash terminal room**.
- Barracks, a mess hall, a small armory (as props, no free loot), a medical room, a motor pool or yard, a command room.
- A **jail or holding room** is optional.
- All doors on the base that depend on reputation are normal doors for now; the generator of access rules comes with the base work.

The base must be raidable: walls and doors can be broken. The stash terminal's contents are **not** in the base (see [factions.md](factions.md)), so a raid cannot reach them.

### 3 Vast Underground Bunker-Laboratory (tier 3, loot: Military, Industrial, Medical, Security, a little Living)

Rooms in a sealed ring with one or two doors, joined by 3-wide corridors made by the generator, like the bunker ([bunker.md](bunker.md)). A structure here is a **room**. Use plain floor; the generator picks a floor type per kind if you do not.

| Structure (room) | Class | Variants | Per location | Loot location |
|---|---|---|---|---|
| Laboratory hall `*` | L | 3 (1*) | 3 to 4 | Industrial, Medical |
| Containment block | L | 2 | 1 to 2 | Security, Prison |
| Server room `*` | M | 2 (1*) | 2 | Industrial |
| Control room `*` | M | 2 (1*) | 1 to 2 | Security |
| Reactor hall (radioactive) | XL | 1 | 1 | Industrial |
| Medical lab `*` | M | 2 (1*) | 2 | Medical |
| Barracks `*` | L | 2 (1*) | 2 | Military |
| Armory `*` | M | 1 | 1 | Military |
| Ammo depot | M | 1 | 1 | Military |
| Canteen | M | 2 | 1 to 2 | Kitchen, Living |
| Generator room | M | 2 | 1 to 2 | Industrial |
| Archive | M | 2 | 1 to 2 | Security, Living |
| Storage `*` | M | 3 (1*) | 3 to 4 | Industrial |
| Cryo bay | L | 1 | 1 | Medical |
| Airlock vestibule | S | 2 | 1 to 2 | none |
| Junction (shaft) | XS | 3 | 4 to 6 | Industrial |
| Gate: elevator hall to location 2 `*` | M | 2 (1*) | 1 | none |

About 30 designs in the full list, 12 in the minimum. The bunker's radiation, the emergency lights and the rule "a boss walker in every military room" carry over as generator rules, not as things you build.

### Tunnels (small transition locations)

Tunnels are generated, not built, from short pieces. The pool is small:

| Piece | Class | Variants | Notes |
|---|---|---|---|
| Straight section | XS | 3 | 3 wide. Cover or debris variants. |
| Junction or bend | XS | 2 | |
| Collapse or flooded section | S | 2 | Slows you down, no free passage. |
| Rest stop | M | 2 | A place for an encounter. |
| Gate (each end) | S | 2 | Holds the transition. |

## Process

1. I make the **outline map**: one empty rectangle per structure class and variant in this list, labelled with the name, class and loot location, laid out in rows by theme. Each rectangle is drawn with floor tiles so you know the maximum size.
2. You build each structure inside its rectangle and save it as a grid file under the path above.
3. I import the files into the generator, run the seed probes and the loot checks, and report mismatches (bad footprints, missing entrances, wrong markers).

## Open

- Final names and exact shapes (L-shapes, courtyards) per structure.
- Whether the generator may rotate every structure or only some (shore buildings face the sea).
- Weather and light inside buildings.
- The marker for squads and patrol points (comes with the squads).
