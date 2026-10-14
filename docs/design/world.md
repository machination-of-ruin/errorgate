# >>> WORLD <<<

The world is a **fixed web of locations** joined by transitions. Each location is a finite map with a theme that regenerates completely on a timer. Players climb from safe-ish places to deadly ones, at their own pace. This replaces the hand-made maps (EDGE OF ENTROPY and Kuznetsk are retired). Read [VIBE.md](VIBE.md) first, then [factions.md](factions.md): the world and the factions are one rework.

Sections marked **Decided** come from the project owner. **Proposed** is a default to confirm. **Spike** means it must be proven in code before anything is built on it.

## Round and wipe (Decided)

- One wipe is **one week**. The server is not restarted during it (a long-running server is fine unless something leaks).
- **Nothing survives the storm nor the wipe:** items, money, characters, structures. The only things that carry over are described in [factions.md](factions.md) (reputation, pending confirmation).
- Population target: **10 to 40 players now, up to 80 later.** It is also playable solo or in a small group. The world is meant to feel **desolate, with rare encounters**; roaming NPC groups provide the encounters.

## The graph (Decided)

- The graph is **fixed** for the whole wipe. The **layout of every location is re-rolled** each time it resets.
- Transitions join locations. **A transition can be a small location itself** (a tunnel, a stairwell).
- First slice: **three locations in a line**, no branching, tiers 1, 2, 3, plus one branch off location 2 to the Military base (see below).

```
[1 Village + Shore Resort] -tunnel- [2 Snow City] -tunnel- [3 Bunker-Laboratory]
          tier 1                       tier 2  |               tier 3
                                               |
                                    [2.1 Military Base]
                               hand-built, never resets
```

| Location | Tier | Theme | Resets | Notes |
|---|---|---|---|---|
| 1 | 1 | Village and Shore Resort | yes | Where players without a faction spawn. Loot: Living, Kitchen, School, Church. |
| 2 | 2 | Snow City (like Kuznetsk) | yes | Loot: Industrial, Medical, Security, a little Military. Holds the transition to the base. |
| 2.1 | none | Military Base | **no** | Hand-built. Faction base, trader, task giver, safe stash terminal. Raidable. |
| 3 | 3 | Vast Underground Bunker-Laboratory | yes | Built from the generated-bunker approach. Military and Industrial tier 3 loot. |
| tunnels | 1-3 | transitions | yes | Small generated locations between the big ones. |

The location tier is the **loot tier**: floor spawners (`LootSpawner<Location>Tier<N>`) already exist. A location only places spawners of its tier (plus lower ones where the theme wants it). Caps in the loot manager become **per location** (today they are global per round).

## Reset: the storm (Decided + Proposed)

Each location resets on its own timer, like a blowout. Decided: players inside die, nothing inside survives, and the map is regenerated with a new layout. While it resets, travel to it is blocked and, if it has spawn zones, spawns are stopped. Resets never happen at the same time.

Proposed defaults (all server CVars):

| Step | Default | Effect |
|---|---|---|
| Interval | random 6 to 12 hours per location | Timers are picked so two locations never reset within an hour of each other. |
| Warning | 5 minutes | Entry through transitions is **blocked from the start**. Everyone inside gets a clear warning (sound, sky, text) and is meant to flee through a transition. |
| Storm | at the end of the warning | Everyone still inside is killed (gibbed, the life ends in the death void). Everything on the map is deleted with it. |
| Regeneration | up to 60 seconds | A new layout is built. Spawns and transitions reopen when it is done. |
| Admin | command `worldreset <location>` | Starts a reset at once (a normal warning, or `now` to skip it). |

The first location to open must be ready before any player spawns. The tunnels reset with the location they lead to.

### Players who log off (Proposed)

A disconnected body stays where it is for 3 minutes (vulnerable), then is taken out of the world. On reconnect the player reappears at the nearest transition of the same location. If that location reset meanwhile, the life is over and the player goes to the death void.

## Generation (Proposed)

- A **seeded generator in plain C#**, like [bunker.md](bunker.md): the same seed gives the same location, and a probe can check thousands of seeds for connectivity.
- **Streets first, then lots.** The generator lays the streets, then walks along both sides of every street and puts a lot at each stop, so buildings line streets and never float between them. The **buildings come from a pool of hand-made structures** (see [structures.md](structures.md)), picked by role.
  - **Village** (`style: village`): one main street across the map (as near the middle as the shore allows) and lanes branching off it, one side or both, 28 to 64 tiles long. Roads are three wide dirt.
  - **City** (`style: city`): a grid of three or four streets each way, the middle ones five wide (avenues), all with a sidewalk on both sides.
  - **Lots:** a lot faces the street: every structure is turned so that its **front door faces the street** (the front is the side of the structure with the most outer doors, south on a tie, measured when the pool is loaded). A path joins every front door to the street.
  - **Homestead** (village): a house plus up to two small buildings (a shed, a sauna, a workshop: the `outbuilding` role) inside a **fence** with a gate in line with the front door. The fence is built from the fence pieces of the location (`fenceStraight`, `fenceCorner`, `fenceGate`), each turned to connect: corners by quarter turns, straight pieces north to south or east to west, the gate across the line. A few industrial lots in the city get a wire fence the same way.
  - **Roles** (see `Resources/Prototypes/_ERRORGATE/World/structures.yml`): `house`, `outbuilding`, `civic` (church, school, clinic), `shop`, `industrial`. The main street of the village gets homesteads, civic buildings and shops; the lanes get homesteads and houses. The city avenues get shops, civic and industrial buildings; the other streets houses, shops and industry.
  - **Dressing** fills what is left: trees, rocks, grass and boulders stay clear of lots, streets and paths.
- Each location has: a theme, a structure pool, a loot pool, mob and NPC rosters, and weather/light settings.
- Every location must be built **off the main thread or sliced over ticks** if it is large. A rebuild must not hitch the server for seconds while players are elsewhere.
- Locations have a finite border (a wall of terrain, deep snow, a cliff, the sea).
- The bunker (`docs/design/bunker.md`) is adapted: location 3 reuses its seeded-layout, doors, airlock and radiation ideas, and the old hatch/key sequence either becomes a special tier 3 structure or is retired.

## Transitions (Proposed)

A transition is a pair of entities (one in each location) that move players, and what they pull, like the bunker hatch does. Mobs do not follow. Arrival spots are generated with the location, so a reset moves them. A transition is blocked while its destination is resetting or its source is in the warning.

## Dressing: what is in a location besides buildings (Proposed)

First pass, in the generator now: **trees** in clumps and alone (never closer than two tiles apart), and **rock formations**, indestructible blobs and ridges of 4 to 45 tiles that break up sight lines. Both stay clear of structures (two tiles) and roads (one tile). Counts and tree kinds are per location prototype (`rocks`, `treeClumps`, `treeSingles`, `trees`).

Also in now: **faults** (docs/design/anomalies.md) scattered along the routes between the loot (`faults`: 10 in the village, 22 in the city), the **air** outdoors is the map's (no grid atmosphere to fill or leak; a grid that grows gets one by itself, so the build takes it off), the **shore** follows the edge of the shape (a band by distance to the edge, tapering at its ends), **roadblocks** in the snow city (pairs of N14 metal barricades, sandbags, razorwire and tank traps in the middle of the five-wide avenues, never closing a road, away from the gates; `roadblocks`, `roadblockProtos`) and a **graveyard** in the village (clumps of grave crates), and a **shore** on one side of the map, with a wavy edge (village: sand, then shallow water that slows you down; snow city: ice), **bushes and grass** as decals on the village ground, **boulders** (`FloraRockSolid`) and **barricades**, a **permanent snowfall** over the snow city (`weather: SnowfallLight`, never ends), and a **day and night** that is the same in every location: every map has a `LightCycle` of 20 minutes with no random start, driven by the server clock, so the light in the village and the city always matches.

Ideas to add, in the order I would take them, each with a theme that shows where it fits:

| Idea | Where | What it does |
|---|---|---|
| Boulders and scree (destructible) | all | Small cover that a pickaxe can clear, unlike the formations. |
| Fences, hedges, low walls | village, city | Yards around houses, ways that funnel people to a gate. |
| Wrecks (cars, a bus, a boat on the shore) | all | Cover on roads, loot containers, a sign of what happened. |
| Ponds, shore, reeds | village | Water is slow terrain and a sight-line breaker of a different kind. |
| Snowdrifts, fallen trees | city | Partial cover, slow tiles. |
| Graveyard, burnt houses, a mass grave | village, city | Mood, hostile mobs, rare loot. |
| Power lines, poles, billboards | city | Landmarks that help people find their way without a map. |
| Roadblocks and checkpoints | city | A reason for squads to hold a place. |
| Lamp posts, fires, camps | all | Light in the dark, a place where people may meet. |
| Faults (see [anomalies.md](anomalies.md)) | all | Danger zones with a reward behind them. |

Open: should trees block bullets, how thick are the forests, are rock formations ever passable through a gap, and does every location need the same density?

## Running the world

Everything below is built; the world is off by default so the old maps play as before.

- **One command:** `Scripts/bat/runWorld.bat` (or `Scripts/sh/runWorld.sh`) builds, starts the server with `game.map=World` and `errorgate.world.enabled=true`, and starts the client; connect to localhost.
- **A round in the world:** set the CVars `game.map` to `World` and `errorgate.world.enabled` to `true` (the graph is `errorgate.world.graph`, `Default`). At the start of the round the web is built (village, tunnel, snow city, the military base, a tunnel, the laboratory), the ground of the village joins the station, and players spawn at the spawn zone of the village (eight spawn points in a clearing near the middle). When the round ends the world is forgotten.
- **The laboratory is entered like the bunker** (docs/design/bunker.md): the end of the tunnel that leads to it has the bunker hatch, a `BunkerKey` (in the loot tables, at most one per location) starts the timed airlock, the console inside cycles it. When either side is built again the hatch is joined to the new vestibule.
- **Gates:** `Village.east` and `TunnelVillageCity.west` lead to each other, and so on along the links in `locations.yml`. Every gate stands on a plaza of its own (a small grid: steel floor, hazard marks, two lamps, barricades along both flanks; a hand-built plaza can replace it; tunnel gates get marks and lamps). Using a gate moves you, and what you pull, to the other one. Mobs do not use them. A gate does not work while the location behind it has a storm coming.
- **The storm:** each location that resets (the village, the city, the laboratory; the tunnels follow the location they lead to; the base never resets) has a timer of 6 to 12 hours, picked so two locations are an hour apart if the range allows. `errorgate.world.warning_seconds` (300) before it hits, the whole location (and its tunnels) is told the exact time left (when the warning starts, then at 1:00, 0:30, 0:10 and 0:05), the warning sound (`errorgate.world.warning_sound`, `Misc/delta_alt.ogg`) plays with the first notice and the last, the ashfall thickens (light from the start, ashfall for the second half, heavy for the last five seconds) and entry is closed; when it hits, the map is deleted (everything and everyone in it) and built again with a new layout.
- **Admin commands:** `worldbuild [graph] [seed]` builds the whole web without a round, `worldreset <location> [now|seconds]` starts the storm now (with the full warning, after the given number of seconds, or at once), `worldstatus` lists the locations, their storms and the gates, `worldgo <location> <gate>` takes you to a gate, `worldgen`/`worldclear`/`worldinfo` work on one location, `structureview`, `structurecheck` and `structureoutline` are for the structure pool.
- **The Military base** is a placeholder walled compound until the hand-built map is saved as `/Maps/_ERRORGATE/Locations/MilitaryBase.yml` (put `WorldGate` entities in it with the gate name `gate`; people arrive on the tile in front of the gate).

## Technical limits and risks

- **Leaks over a week.** About 40 teardown and rebuild cycles per week. Every cycle must free entities, atmosphere, pathfinding data and maps. The first spike is a soak test that resets a location dozens of times in a row and watches memory, entity count and tick time.
- **Entity count.** The old Kuznetsk map is about 112,000 lines of YAML. A tier 2 city of that size means tens of thousands of entities. Budget the total over all loaded maps and measure early.
- **Which maps are loaded.** Start with everything loaded (three big locations, the base, a few tunnels). Unload only what proves too costly. Roaming NPC groups are only simulated in loaded maps.
- **Loot manager.** Per-location tables and caps. A reset must refund correctly (items on the map are deleted, so their slots return).
- **Dilution.** With 10 to 40 players spread over a web, most of the world is empty. That is wanted, but check that the funnel (spawn, transitions, the base) still produces meetings.
- **Content volume.** The generator is cheap, the structure pool is the real cost. See [structures.md](structures.md).

## Spike 1: build, tear down, soak (done)

`WorldSystem` (`Content.Server/_ERRORGATE/World/`) builds a location from a `worldLocation` prototype (`Resources/Prototypes/_ERRORGATE/World/locations.yml`): a map with air, gravity and light, a ground grid with terrain, 3-wide roads and an indestructible rock border, and one grid per structure placed by the seeded `LocationLayout` (random free spots with a gap, random quarter turns, roads joining the structures by the cheapest tree). Structure files are measured once. Admin commands: `worldgen <Village|SnowCity> [seed]`, `worldclear <location|all>`, `worldinfo`.

Measured with a throwaway soak test (80 build and teardown cycles per location, on the placeholder structures cut from Kuznetsk, no gameplay running):

| | Village (160 x 160) | Snow City (180 x 180) |
|---|---|---|
| Structures placed | 36 | 46 |
| Entities | 2,300 to 4,900 | 3,400 to 5,900 |
| Build time | 240 ms average, 1.2 s cold | 420 ms average, 1.05 s cold |
| Teardown | about 50 to 150 ms | about 70 to 160 ms |
| Entities left after teardown | 0 | 0 |
| Managed memory over 80 cycles | flat (1495 to 1499 MB) | flat (1495 to 1499 MB) |

Every ground tile picks its own variant (like the `variantize` command), so the terrain does not look like a pattern. The ground under a building is cut away, so a tile belongs to exactly one grid. Every grid has an atmosphere of its own and gets breathable air a few ticks after it is built (the map's air does not reach a grid without one); the tiles under walls have none, as usual. No structure leaves its location or overlaps another. A rebuild is fast enough to run on the game thread; it will be sliced once the real structures make locations heavier (the placeholders are far lighter than a finished tier 2 city). This is measured on empty locations: no NPC squads, no players, no atmos or lighting load yet.

## Order of work

1. **Spikes, one at a time:** (a) build, tear down and reset a location with timings and the soak test (done, see above); (b) the reputation table with migrations on both database kinds; (c) opening a storage UI on a remote container for the safe stash.
2. **First slice:** the three locations, tunnels and transitions, the storm timer and `worldreset`, loot by tier, spawn zones.
3. **The base:** the Military base map, one trader, one task giver, the stash terminal, reputation gating.
4. **Squads and roles.** Then more factions, locations and structures.

AI GOD work comes after this rework is fully implemented (see [ai-god.md](ai-god.md)).

## Open

- Border form of each location, and how big each one is (tiles, entities).
- Whether every tier has the same kind of transition or each pair has its own.
- Whether tunnels can have encounters, loot or NPCs.
- Weather and light per location (the snow city keeps a day-night cycle and a snowstorm).
- Whether the shore of location 1 can be crossed or is only a backdrop.
