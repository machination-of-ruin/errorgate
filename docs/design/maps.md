# >>> MAPS <<<

Keep this list current when a map is added, replaced or removed.

## Played maps

| Map | Prototype | File | Purpose |
|---|---|---|---|
| EDGE OF ENTROPY | `EdgeOfEntropy` | `Resources/Maps/edge_of_entropy/EdgeOfEntropy.yml` | Main world map of the original fork. In the default pool. |
| KUZNETSK | `Kuznetsk` | `Resources/Maps/edge_of_entropy/Kuznetsk.yml` | Second world map, imported from the separate `errorgate` repo. In the default pool (`DefaultMapPool` = EdgeOfEntropy + Kuznetsk). |
| EDGE OF ENTROPY (dev) | `EdgeOfEntropyDev` | `Resources/Maps/edge_of_entropy/TestMap2.yml` | Test world, default map of the development config (`Resources/ConfigPresets/Build/development.toml`). |

Test-only maps live in `Resources/Maps/Test` (`Empty`, `Dev`, `TestTeg`).

## Map design

- **EDGE OF ENTROPY:** a claustrophobic underground map with places that recall the real world and liminal spaces between them. Many deadly hostile mobs and close spawns. The aim is to force players to cooperate early to go deeper and explore. Playtests confirmed it: players who meet early have nothing to lose and are eager to cooperate in an unknown, dangerous environment.
- **KUZNETSK:** a larger open world with a day-night cycle and a constant snowstorm. A post-Soviet town with apartments and service buildings, and a factory with military loot and limited access. Mobs are slow and not very dangerous, which leaves players free to roam, fight each other and form gangs. Recent changes added anomalies to make travel between points of interest riskier.

## Spawning

Maps use the `TestStation` prototype with the single `HumanError` job (every other job is hidden). Players spawn at the map's spawn points. Designated spawn zones come with the survival spawn rules (see [death.md](death.md)).

## Cleanup status

Removed (maps and loaders): the station maps, shuttles (emergency, arrivals, cargo, mining, shuttle events), CentComm, salvage expeditions, ghost bar, `Misc`, `Nonstations`, and the `_White`, `_Shitmed` and `_Goobstation` maps (pocket dimension kept: it loads on demand). Loading is switched off with CVar defaults (`shuttle.arrivals`, `shuttle.grid_fill`, `shuttle.emergency`, `asteroid_field.enabled`), the ghost bar round-start load is disabled, and deleted map paths that prototypes or component defaults still name are pointed at `/Maps/Test/empty.yml`.

Kept for the planned planet surface (see [ROADMAP.md](ROADMAP.md): Lavaland-style terrain with ruins): `Resources/Maps/_Lavaland`, `Resources/Maps/Ruins`, `Resources/Maps/Dungeon` and the Lavaland planet, dungeon and shelter capsule systems.

## Notes

- Maps were authored against an older Sept 2024 content version. After porting, load them (integration test `PostMapInitTest`, narrow filters only, the full run needs a lot of RAM) and fix any renamed or removed entities.
- Obsolete or removed prototypes that a map still names are listed in the server log at map load.
