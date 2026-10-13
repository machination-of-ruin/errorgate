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

  Variants of one class may differ in shape (L-shapes are fine), but the structure must fit **inside its class rectangle**. The generator never overlaps two rectangles.
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

So there is something to see and improve on, the closed buildings of the old Kuznetsk map were cut out into the structure format: the cutting script was a throwaway and is not kept, the result is the files under `Resources/Maps/_ERRORGATE/Structures/`. A building is a group of rooms closed by walls (joined by doors), at most 24 x 18, cut exactly at its outer wall; every mob, mob spawner and loose item is removed, and loot spawners are set to tier 1. Wood-floor buildings went to `village`, concrete ones to `city`; the plain small concrete ones (marked `also village`) are copied into `village` as well. Names are guesses from the furniture: **please confirm or rename them.**

| File | Floor | Class | Size | Floor tiles | Entities | Guess | Notable |
|---|---|---|---|---|---|---|---|
| `city/kuz-concrete-XS-1.yml` (also village) | concrete | XS | 5x5 | 9 | 21 | junk shack (machine frames) | MachineFrameDestroyedx3 LootSpawnerIndustrialTier1x2 MetalDoorx1 |
| `city/kuz-concrete-S-1.yml` (also village) | concrete | S | 8x6 | 24 | 38 | lockup (racks) | Rackx7 LootSpawnerIndustrialTier1x3 Grillex2 MetalDoorx1 |
| `city/kuz-concrete-S-2.yml` (also village) | concrete | S | 7x6 | 20 | 34 | lockup (racks) | Rackx5 Grillex4 LootSpawnerIndustrialTier1x3 MetalDoorx1 |
| `city/kuz-concrete-S-3.yml` (also village) | concrete | S | 5x8 | 15 | 45 | depot (rails, racks) | Railingx7 LootSpawnerIndustrialTier1x5 Rackx5 Grillex3 MetalDoorx3 |
| `city/kuz-concrete-S-4.yml` (also village) | concrete | S | 5x6 | 12 | 25 | lockup (racks) | Rackx3 Grillex2 LootSpawnerIndustrialTier1x2 MetalDoorx2 |
| `city/kuz-concrete-M-1.yml` | concrete | M | 8x10 | 48 | 44 | store room (racks) | Rackx7 LootSpawnerIndustrialTier1x4 MetalDoorx4 LootSpawnerLivingTier1x1 |
| `city/kuz-concrete-M-2.yml` | concrete | M | 8x9 | 42 | 40 | store room (racks) | Rackx5 MetalDoorx4 LootSpawnerIndustrialTier1x3 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-M-3.yml` (also village) | concrete | M | 9x6 | 28 | 53 | shop (counters, kitchen loot) | LootSpawnerKitchenTier1x8 Rackx7 TableCounterMetalx4 Grillex4 MetalDoorx3 Chairx1 |
| `city/kuz-concrete-L-1.yml` | concrete | L | 10x12 | 80 | 93 | school (tables, school loot) | TableWoodx14 ChairWoodx13 LootSpawnerSchoolTier1x8 TableReinforcedx7 Bookshelfx3 LootSpawnerKitchenTier1x3 |
| `city/kuz-concrete-L-2.yml` (also village) | concrete | L | 10x11 | 72 | 74 | school (tables, school loot) | TableWoodx16 ChairWoodx9 LootSpawnerSchoolTier1x8 WoodDoorx1 |
| `city/kuz-concrete-L-3.yml` | concrete | L | 10x12 | 71 | 82 | supermarket (racks, counters) | Rackx14 LootSpawnerKitchenTier1x10 TableCounterMetalx7 MetalDoorx5 GunSafex1 LootSpawnerSecurityTier1x1 |
| `city/kuz-concrete-L-4.yml` | concrete | L | 13x7 | 55 | 48 | warehouse (racks) | Rackx7 LootSpawnerLivingTier1x3 MetalDoorx3 LootSpawnerIndustrialTier1x2 |
| `city/kuz-concrete-L-5.yml` | concrete | L | 11x8 | 54 | 49 | warehouse (racks) | Rackx7 MetalDoorx4 LootSpawnerLivingTier1x4 LootSpawnerIndustrialTier1x3 FloraTreeSnowx1 |
| `city/kuz-concrete-L-6.yml` | concrete | L | 11x9 | 51 | 92 | pharmacy / clinic (racks, medical loot) | Rackx16 Grillex13 LootSpawnerMedicalTier1x10 MetalDoorx3 ChairGreyscalex2 TableReinforcedx2 |
| `city/kuz-concrete-L-7.yml` | concrete | L | 12x7 | 50 | 45 | warehouse row | Rackx6 MetalDoorx3 LootSpawnerIndustrialTier1x3 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-L-8.yml` | concrete | L | 12x7 | 50 | 46 | warehouse row | Rackx6 LootSpawnerIndustrialTier1x4 MetalDoorx3 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-L-9.yml` | concrete | L | 11x7 | 45 | 46 | warehouse row | Rackx7 LootSpawnerIndustrialTier1x4 MetalDoorx3 LootSpawnerLivingTier1x3 |
| `city/kuz-concrete-L-10.yml` | concrete | L | 11x7 | 45 | 42 | warehouse row | Rackx5 MetalDoorx3 LootSpawnerLivingTier1x3 LootSpawnerIndustrialTier1x2 |
| `city/kuz-concrete-L-11.yml` | concrete | L | 11x7 | 45 | 44 | warehouse row | Rackx6 LootSpawnerIndustrialTier1x4 MetalDoorx3 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-L-12.yml` | concrete | L | 9x9 | 42 | 62 | cell block (prison loot) | Rackx7 LootSpawnerIndustrialTier1x6 MachineFrameDestroyedx4 MetalDoorx3 LootSpawnerPrisonTier1x3 FenceMetalStraightx2 |
| `city/kuz-concrete-L-13.yml` | concrete | L | 11x6 | 36 | 38 | warehouse (racks) | MetalDoorx4 Rackx4 LootSpawnerIndustrialTier1x2 LootSpawnerLivingTier1x2 |
| `city/kuz-concrete-L-14.yml` | concrete | L | 11x7 | 34 | 84 | clinic ward (curtains, benches) | TableWoodx8 ChairWoodx6 HospitalCurtainsx6 SteelBenchx5 LootSpawnerSchoolTier1x4 LootSpawnerMedicalTier1x3 |
| `city/kuz-concrete-XL-1.yml` | concrete | XL | 11x12 | 90 | 86 | office (reinforced tables, bookshelves) | TableReinforcedx11 LootSpawnerIndustrialTier1x9 Rackx7 Bookshelfx4 TableWoodx3 LootSpawnerSchoolTier1x2 |
| `city/kuz-concrete-XL-2.yml` | concrete | XL | 11x11 | 81 | 77 | school hall (tables, chairs) | TableWoodx14 ChairWoodx12 LootSpawnerSchoolTier1x7 WoodDoorx1 |
| `city/kuz-concrete-XXL-1.yml` | concrete | XXL | 16x21 | 192 | 235 | police station / barracks (security loot) | LootSpawnerSecurityTier1x19 TableWoodReinforcedx11 Rackx10 SteelBenchx8 ChairWoodx7 MetalDoorx7 |
| `city/kuz-concrete-XXL-2.yml` | concrete | XXL | 17x16 | 190 | 147 | market / canteen (counters, kitchen loot) | LootSpawnerKitchenTier1x19 TableCounterMetalx17 ClockworkGrillex13 Rackx8 TableWoodx7 MetalDoorx5 |
| `city/kuz-concrete-XXL-3.yml` | concrete | XXL | 10x24 | 148 | 138 | workshop (closets, industrial loot) | TableWoodx10 Grillex7 Rackx7 ClosetSteelBasex6 LootSpawnerIndustrialTier1x6 MetalDoorx4 |
| `city/kuz-concrete-XXL-4.yml` | concrete | XXL | 9x19 | 105 | 94 | big warehouse (racks, stairs) | Rackx14 MetalDoorx11 LootSpawnerIndustrialTier1x8 StairDarkx3 OreBoxx1 |
| `city/kuz-other-L-1.yml` | other | L | 10x13 | 75 | 117 | school (tables, chairs) | TableWoodx15 ChairWoodx11 SteelBenchx8 LootSpawnerSchoolTier1x7 TableReinforcedx6 Tablex4 |
| `city/kuz-other-XL-1.yml` | other | XL | 11x11 | 81 | 67 | assembly hall (benches, fences) | SteelBenchx15 LootSpawnerSchoolTier1x5 FenceMetalCornerx4 MetalDoorx1 WoodDoorx1 |
| `village/kuz-wood-XS-1.yml` | wood | XS | 5x5 | 9 | 19 | hut (bed, fireplace) | WoodDoorx1 Fireplacex1 Bedx1 LootSpawnerPrisonTier1x1 |
| `village/kuz-wood-XS-2.yml` | wood | XS | 5x4 | 6 | 18 | shed (racks) | LootSpawnerIndustrialTier1x2 Rackx2 WoodDoorx1 |
| `village/kuz-wood-S-1.yml` | wood | S | 5x7 | 15 | 30 | workshop shed (ore boxes) | OreBoxx4 LootSpawnerIndustrialTier1x2 Rackx2 LootSpawnerMedicalTier1x2 WoodDoorx1 |
| `village/kuz-wood-S-2.yml` | wood | S | 4x8 | 10 | 28 | small hall (benches, fireplace) | WoodenBenchx4 WoodDoorx2 Fireplacex1 LootSpawnerPrisonTier1x1 |
| `village/kuz-wood-L-1.yml` | wood | L | 9x12 | 55 | 73 | farmhouse | TableWoodx9 LootSpawnerLivingTier1x4 LootSpawnerKitchenTier1x3 Bedx2 WoodDoorx2 Dresserx1 |
| `village/kuz-wood-L-2.yml` | wood | L | 10x10 | 49 | 69 | farmhouse | LootSpawnerLivingTier1x5 TableWoodx4 WoodDoorx4 Dresserx3 Bedx2 LootSpawnerKitchenTier1x2 |
| `village/kuz-wood-XL-1.yml` | wood | XL | 14x15 | 116 | 142 | big house (bookshelves) | Grillex13 CarpetBlackx8 WoodDoorx7 Bookshelfx4 LootSpawnerLivingTier1x3 ShuttersNormalx3 |
| `village/kuz-wood-XL-2.yml` | wood | XL | 16x12 | 93 | 123 | big house (beds) | TableWoodx8 WoodDoorx6 LootSpawnerLivingTier1x5 Rackx4 Bedx3 LootSpawnerKitchenTier1x3 |
| `village/kuz-wood-XL-3.yml` | wood | XL | 9x15 | 83 | 74 | church | WoodenBenchx16 LootSpawnerChurchTier1x5 BarricadeBlockx3 Barricadex3 StairWoodx2 AltarConvertBurdenx1 |
| `village/kuz-wood-XL-4.yml` | wood | XL | 13x12 | 75 | 119 | barricaded house | Barricadex10 BarricadeBlockx8 TableWoodx5 WoodDoorx5 LootSpawnerMedicalTier1x4 Dresserx4 |
| `village/kuz-wood-XL-5.yml` | wood | XL | 11x15 | 73 | 99 | big house | LootSpawnerLivingTier1x7 WoodDoorx5 Dresserx4 TableWoodx4 LootSpawnerKitchenTier1x4 LootSpawnerIndustrialTier1x2 |
| `village/kuz-wood-XXL-1.yml` | wood | XXL | 17x21 | 182 | 302 | long house / dormitory | LootSpawnerKitchenTier1x24 LootSpawnerLivingTier1x20 ClockworkGrillex19 WoodDoorx13 TableCounterWoodx11 Bedx10 |
| `village/kuz-wood-XXL-2.yml` | wood | XXL | 23x16 | 167 | 336 | long house / dormitory | LootSpawnerLivingTier1x32 ClockworkGrillex21 WoodDoorx21 LootSpawnerKitchenTier1x16 Bedx15 Dresserx13 |
| `village/kuz-wood-XXL-3.yml` | wood | XXL | 17x16 | 129 | 253 | long house / dormitory | LootSpawnerLivingTier1x23 LootSpawnerKitchenTier1x15 Dresserx11 Bedx11 ClockworkGrillex11 WoodDoorx11 |
| `village/kuz-wood-XXL-4.yml` | wood | XXL | 15x17 | 118 | 259 | canteen (counters, freezer lockers) | LootSpawnerKitchenTier1x28 TableCounterWoodx18 Grillex17 LootSpawnerLivingTier1x11 WoodDoorx11 LockerFreezerBasex8 |

Not found by the cut: buildings larger than 24 x 18 (the big apartment blocks), buildings with a gap in their walls, open yards and the warehouses with open fronts. They stay in the old map.
