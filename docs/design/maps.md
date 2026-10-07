# >>> MAPS <<<

Maps ported from the original ERRORGATE survival fork. Keep this list current when a map is added, replaced or removed.

| Map | Prototype | File | Purpose |
|---|---|---|---|
| EDGE OF ENTROPY (dev) | `EdgeOfEntropyDev` | `Resources/Maps/edge_of_entropy/TestMap2.yml` | Test world used as the default map of the development config (`Resources/ConfigPresets/Build/development.toml`). Test station, single `Passenger` job. A `SpawnPointPassenger` was added during the port because the map test requires a spawn point for every available job. |

## Notes

- The main world map `EdgeOfEntropy.yml` of the original fork is not ported yet; it comes with later PRs.
- The dev map still spawns players through the station/job flow (`TestStation`, `Passenger`). That flow is expected to change once the survival spawn rules are ported.
- Maps were authored against an older Sept 2024 content version. After porting, load them (integration test `PostMapInitTest`) and fix any renamed or removed entities.
