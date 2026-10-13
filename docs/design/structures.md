# >>> STRUCTURES <<<

The hand-made pieces the world generator ([world.md](world.md)) places in each location. The generator makes terrain, roads and corridors; **buildings come from here**. The project owner builds them in the map editor. The generator picks them by footprint and theme.

Everything in this file is **Proposed** except the themes, which are **Decided**: 1 Village and Shore Resort, 2 Snow City, 2.1 Military Base, 3 Vast Underground Bunker-Laboratory.

## How a structure is built

- **One structure is one small grid**, saved as its own file under `Resources/Maps/_ERRORGATE/Structures/<theme>/<name>.yml`. The generator loads it with `TryLoadGrid` at a chosen spot (rotated in steps of 90 degrees), the same way the existing ruins and dungeon rooms load. The base is **not** a structure: it is a whole hand-built map.
- **Footprint classes** (width x height in tiles, **including** the outer wall; nothing outside the walls):

| Class | Size | Typical use |
|---|---|---|
| XS | 5 x 5 | well, kiosk, lighthouse, barricade |
| S | 8 x 6 | small house, shed, bungalow |
| M | 10 x 8 | shop, cafe, garage |
| L | 14 x 10 | church, school, warehouse, police |
| XL | 18 x 14 | hotel, hospital, apartment block, reactor hall |
| XXL | 24 x 18 | factory |
| XXXL | 40 x 30 | a block of rooms, a school, a long barracks |
| LANDMARK | bigger | kept in `Structures/review/` until we decide; the generator does not use them |

  Variants of one class may differ in shape (L-shapes are fine), but the structure must fit **inside its class rectangle**. The generator never overlaps two rectangles.
- **Upright grids.** The generator never turns a grid: a camera aligned with the grid must match the ground. When it turns a structure by a quarter turn, it turns the tiles and moves every entity to its new place, but only things whose use depends on the way they face turn with the building (seats and beds, wall mounts). Every other entity keeps the way it was built, so its sprite stays upright on screen (a fireplace, a table, a spawner). Build a structure facing south.
- **No apron.** The grid holds the building exactly, walls included, with no extra tile of floor around it. The generator keeps a gap of its own between structures and lays the roads to the doors.
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

## Harvested placeholders (Kuznetsk)

So there is something to see and improve on, the closed buildings of the old Kuznetsk map were cut out into the structure format; the cutting script was a throwaway and is not kept, the result is the files under `Resources/Maps/_ERRORGATE/Structures/`. A building is a group of rooms closed by walls: rooms joined by a door, or separated by a single wall, are one building (so a block of rooms is one structure, not many). The **fence around a building belongs to it** (a fence tile within five tiles of the walls goes to the nearest building, the house yards of the village and the wire fences of the city), so the footprint reaches the fence and nothing beyond it. Every mob, mob spawner and loose item is removed, and loot spawners are set to tier 1. Wood-floor buildings went to `village`, concrete ones to `city`; the small plain concrete ones and the schools (marked `also village`) are copied into `village` as well, with the snow floor replaced by dirt (snow does not lie in a village). The `LANDMARK` ones are more than 40 x 30 and sit in `Structures/review/` so the generator does not use them yet; see *How to review* below. Names are guesses from the furniture: **please confirm or rename them.**

| File | Floor | Class | Size | Floor tiles | Fence tiles | Entities | Guess | Notable |
|---|---|---|---|---|---|---|---|---|
| `city/kuz-concrete-XS-1.yml` (also village) | concrete | XS | 5x5 | 9 | 0 | 21 | workshop | MachineFrameDestroyedx3 LootSpawnerIndustrialTier1x2 MetalDoorx1 |
| `city/kuz-concrete-S-1.yml` (also village) | concrete | S | 5x8 | 15 | 0 | 38 | lockup | LootSpawnerIndustrialTier1x5 Rackx5 Grillex3 MetalDoorx3 |
| `city/kuz-concrete-S-2.yml` (also village) | concrete | S | 5x6 | 12 | 0 | 25 | lockup | Rackx3 Grillex2 LootSpawnerIndustrialTier1x2 MetalDoorx2 |
| `city/kuz-concrete-M-1.yml` (also village) | concrete | M | 8x10 | 48 | 0 | 44 | warehouse / store room | Rackx7 LootSpawnerIndustrialTier1x4 MetalDoorx4 LootSpawnerLivingTier1x1 |
| `city/kuz-concrete-M-2.yml` (also village) | concrete | M | 8x9 | 42 | 0 | 40 | warehouse / store room | Rackx5 MetalDoorx4 LootSpawnerIndustrialTier1x3 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-M-3.yml` (also village) | concrete | M | 7x9 | 35 | 0 | 34 | warehouse / store room | Rackx3 MetalDoorx3 LootSpawnerIndustrialTier1x2 LootSpawnerLivingTier1x1 |
| `city/kuz-concrete-M-4.yml` (also village) | concrete | M | 9x6 | 28 | 0 | 49 | shop / canteen / market | LootSpawnerKitchenTier1x8 Rackx7 Grillex4 TableCounterMetalx3 MetalDoorx3 Chairx1 |
| `city/kuz-concrete-L-1.yml` | concrete | L | 10x12 | 71 | 0 | 82 | shop / canteen / market | Rackx14 LootSpawnerKitchenTier1x10 TableCounterMetalx7 MetalDoorx5 GunSafex1 LootSpawnerSecurityTier1x1 |
| `city/kuz-concrete-L-2.yml` | concrete | L | 13x7 | 55 | 0 | 48 | warehouse / store room | Rackx7 LootSpawnerLivingTier1x3 MetalDoorx3 LootSpawnerIndustrialTier1x2 |
| `city/kuz-concrete-L-3.yml` | concrete | L | 13x7 | 55 | 0 | 45 | warehouse / store room | Rackx5 MetalDoorx3 LootSpawnerLivingTier1x2 LootSpawnerIndustrialTier1x2 |
| `city/kuz-concrete-L-4.yml` | concrete | L | 11x8 | 54 | 0 | 48 | warehouse / store room | Rackx7 MetalDoorx4 LootSpawnerLivingTier1x4 LootSpawnerIndustrialTier1x3 |
| `city/kuz-concrete-L-5.yml` | concrete | L | 11x9 | 51 | 0 | 92 | pharmacy / clinic | Rackx16 Grillex13 LootSpawnerMedicalTier1x10 MetalDoorx3 ChairGreyscalex2 TableReinforcedx2 |
| `city/kuz-concrete-L-6.yml` | concrete | L | 12x7 | 50 | 0 | 45 | warehouse / store room | Rackx6 MetalDoorx3 LootSpawnerIndustrialTier1x3 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-L-7.yml` | concrete | L | 12x7 | 50 | 0 | 46 | warehouse / store room | Rackx6 LootSpawnerIndustrialTier1x4 MetalDoorx3 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-L-8.yml` | concrete | L | 12x7 | 50 | 0 | 45 | warehouse / store room | Rackx6 LootSpawnerIndustrialTier1x3 MetalDoorx3 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-L-9.yml` | concrete | L | 11x7 | 45 | 0 | 46 | warehouse / store room | Rackx7 LootSpawnerIndustrialTier1x4 MetalDoorx3 LootSpawnerLivingTier1x3 |
| `city/kuz-concrete-L-10.yml` | concrete | L | 11x7 | 45 | 0 | 42 | warehouse / store room | Rackx5 MetalDoorx3 LootSpawnerLivingTier1x3 LootSpawnerIndustrialTier1x2 |
| `city/kuz-concrete-L-11.yml` | concrete | L | 11x7 | 45 | 0 | 44 | warehouse / store room | Rackx6 LootSpawnerIndustrialTier1x4 MetalDoorx3 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-L-12.yml` | concrete | L | 11x6 | 36 | 0 | 38 | warehouse / store room | MetalDoorx4 Rackx4 LootSpawnerIndustrialTier1x2 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-XL-1.yml` | concrete | XL | 14x14 | 42 | 10 | 70 | cell block | Rackx7 LootSpawnerIndustrialTier1x6 MachineFrameDestroyedx4 MetalDoorx3 LootSpawnerPrisonTier1x3 SignElectricalMedx1 |
| `city/kuz-concrete-XL-2.yml` | concrete | XL | 12x16 | 20 | 16 | 67 | warehouse / store room | Rackx5 Grillex4 LootSpawnerIndustrialTier1x3 Chairx2 LootSpawnerMilitaryTier1x1 Tablex1 |
| `city/kuz-concrete-XXL-1.yml` | concrete | XXL | 16x21 | 192 | 2 | 235 | police station / barracks | LootSpawnerSecurityTier1x19 TableWoodReinforcedx11 Rackx10 SteelBenchx8 ChairWoodx7 MetalDoorx7 |
| `city/kuz-concrete-XXL-2.yml` | concrete | XXL | 17x16 | 190 | 0 | 147 | shop / canteen / market | LootSpawnerKitchenTier1x19 TableCounterMetalx17 ClockworkGrillex13 Rackx8 TableWoodx7 MetalDoorx5 |
| `city/kuz-concrete-XXL-3.yml` | concrete | XXL | 10x24 | 148 | 0 | 136 | warehouse / store room | TableWoodx10 Grillex7 Rackx7 ClosetSteelBasex6 LootSpawnerIndustrialTier1x6 MetalDoorx4 |
| `city/kuz-concrete-XXL-4.yml` | concrete | XXL | 9x19 | 105 | 0 | 90 | warehouse / store room | Rackx14 MetalDoorx11 LootSpawnerIndustrialTier1x7 StairDarkx3 |
| `city/kuz-concrete-XXXL-1.yml` (also village) | concrete | XXXL | 30x28 | 346 | 0 | 503 | school | TableReinforcedx46 TableWoodx41 SteelBenchx30 LootSpawnerSchoolTier1x28 ChairWoodx27 Bookshelfx21 |
| `city/kuz-concrete-XXXL-2.yml` (also village) | concrete | XXXL | 38x11 | 297 | 0 | 247 | school | TableWoodx58 ChairWoodx39 LootSpawnerSchoolTier1x29 WoodDoorx4 |
| `city/kuz-concrete-XXXL-3.yml` | concrete | XXXL | 19x37 | 297 | 37 | 304 | store room | Rackx17 Tablex12 MetalDoorx10 ClosetSteelBasex10 AirlockFactoryEntrancex7 AtmosDeviceFanTinyInvisiblex7 |
| `review/city/kuz-concrete-LANDMARK-1.yml` (also village) | concrete | LANDMARK | 44x60 | 1600 | 0 | 1239 | store room | OreBoxx76 MetalDoorx51 Rackx36 Tablex35 MachineFrameDestroyedx32 TableWoodx31 |
| `review/city/kuz-concrete-LANDMARK-2.yml` | concrete | LANDMARK | 37x31 | 433 | 132 | 560 | police station / barracks | MetalDoorx25 LootSpawnerSecurityTier1x22 LootSpawnerPrisonTier1x19 LockerSyndicatex15 Bedx14 Tablex11 |
| `review/city/kuz-concrete-LANDMARK-3.yml` | concrete | LANDMARK | 47x15 | 430 | 0 | 471 | hut / house | Grillex55 LootSpawnerLivingTier1x52 Bedx28 TableWoodx28 WoodDoorx19 LootSpawnerKitchenTier1x9 |
| `city/kuz-other-XL-1.yml` (also village) | other | XL | 11x11 | 81 | 4 | 54 | hall (benches) | SteelBenchx7 LootSpawnerSchoolTier1x3 MetalDoorx1 WoodDoorx1 |
| `review/city/kuz-other-LANDMARK-1.yml` (also village) | other | LANDMARK | 72x36 | 1187 | 36 | 958 | workshop | OreBoxx97 ConveyorBeltx94 MachineFrameDestroyedx58 LootSpawnerIndustrialTier1x22 TableCounterMetalx16 LootSpawnerMilitaryTier1x15 |
| `village/kuz-wood-XL-1.yml` | wood | XL | 14x15 | 10 | 28 | 75 | cell block | WoodenBenchx4 FloraTreeSnowx3 WoodDoorx2 Fireplacex1 LootSpawnerPrisonTier1x1 |
| `village/kuz-wood-XL-2.yml` | wood | XL | 10x15 | 9 | 15 | 34 | cell block | WoodDoorx1 Fireplacex1 Bedx1 LootSpawnerPrisonTier1x1 |
| `village/kuz-wood-XL-3.yml` | wood | XL | 14x13 | 6 | 26 | 45 | warehouse / store room | LootSpawnerIndustrialTier1x2 Rackx2 FloraTreeSnowx1 WoodDoorx1 |
| `village/kuz-wood-XXL-1.yml` | wood | XXL | 16x20 | 83 | 13 | 87 | church | WoodenBenchx16 LootSpawnerChurchTier1x5 BarricadeBlockx3 Barricadex3 StairWoodx2 AltarConvertBurdenx1 |
| `village/kuz-wood-XXL-2.yml` | wood | XXL | 17x22 | 75 | 22 | 141 | pharmacy / clinic | Barricadex10 BarricadeBlockx8 TableWoodx5 WoodDoorx5 LootSpawnerMedicalTier1x4 Dresserx4 |
| `village/kuz-wood-XXL-3.yml` | wood | XXL | 19x15 | 55 | 33 | 107 | hut / house | TableWoodx9 LootSpawnerLivingTier1x4 LootSpawnerKitchenTier1x3 Bedx2 WoodDoorx2 FloraTreeSnowx1 |
| `village/kuz-wood-XXL-4.yml` | wood | XXL | 20x13 | 49 | 20 | 94 | house with a yard | LootSpawnerLivingTier1x5 FloraTreeSnowx4 TableWoodx4 WoodDoorx4 Dresserx3 Bedx2 |
| `village/kuz-wood-XXL-5.yml` | wood | XXL | 15x17 | 15 | 31 | 62 | pharmacy / clinic | OreBoxx4 LootSpawnerIndustrialTier1x2 Rackx2 LootSpawnerMedicalTier1x2 FloraTreeSnowx1 WoodDoorx1 |
| `village/kuz-wood-XXXL-1.yml` | wood | XXXL | 33x16 | 258 | 0 | 446 | shop / canteen / market | LootSpawnerLivingTier1x37 LootSpawnerKitchenTier1x25 ClockworkGrillex22 WoodDoorx22 Dresserx20 TableCounterWoodx17 |
| `village/kuz-wood-XXXL-2.yml` | wood | XXXL | 24x19 | 116 | 22 | 164 | building | Grillex13 CarpetBlackx8 WoodDoorx7 Bookshelfx4 LootSpawnerLivingTier1x3 ShuttersNormalx3 |
| `village/kuz-wood-XXXL-3.yml` | wood | XXXL | 26x20 | 93 | 48 | 164 | hut / house | TableWoodx8 WoodDoorx7 LootSpawnerLivingTier1x5 Rackx4 Bedx3 LootSpawnerKitchenTier1x3 |
| `village/kuz-wood-XXXL-4.yml` | wood | XXXL | 21x23 | 73 | 49 | 154 | workshop | LootSpawnerLivingTier1x7 WoodDoorx6 Dresserx4 TableWoodx4 LootSpawnerKitchenTier1x4 LootSpawnerIndustrialTier1x2 |
| `review/village/kuz-wood-LANDMARK-1.yml` | wood | LANDMARK | 19x67 | 653 | 0 | 1006 | shop / canteen / market | LootSpawnerLivingTier1x99 Grillex60 WoodDoorx58 TableCounterWoodx46 LootSpawnerKitchenTier1x41 Bedx26 |
| `review/village/kuz-wood-LANDMARK-2.yml` | wood | LANDMARK | 17x41 | 364 | 0 | 555 | shop / canteen / market | LootSpawnerKitchenTier1x41 ClockworkGrillex38 LootSpawnerLivingTier1x36 WoodDoorx26 TableCounterWoodx22 Dresserx18 |
| `review/village/kuz-wood-LANDMARK-3.yml` | wood | LANDMARK | 15x49 | 349 | 0 | 618 | shop / canteen / market | Grillex45 LootSpawnerKitchenTier1x41 WoodDoorx36 LootSpawnerLivingTier1x31 TableCounterWoodx24 MetalDoorx15 |
| `review/village/kuz-wood-LANDMARK-4.yml` | wood | LANDMARK | 45x16 | 334 | 0 | 616 | house | LootSpawnerLivingTier1x55 ClockworkGrillex42 WoodDoorx42 LootSpawnerKitchenTier1x27 Bedx24 Dresserx23 |

Not found by the cut: buildings with a gap in their walls, open yards and the warehouses with open fronts. They stay in the old map.

### How to review

In the running game, as an admin: `structureview <name>` (for example `structureview review/village/kuz-wood-LANDMARK-1`, or `structureview village/kuz-wood-XL-3`) builds a fresh map with that structure on it and moves you there. `structureview` without a name lists the files. Walk around, then use the `mapping` tools to cut a landmark into buildings: save each part with `savegrid <grid> <file>` and put it in the pool folder (`village` or `city`).

