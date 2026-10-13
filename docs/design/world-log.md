# >>> WORLD LOG <<<

Work log of the world and factions rework: what was done, in order, and the points to go back to. Newest at the bottom. Read [world.md](world.md) for the design.

## Checkpoints

| Tag | Commit | What it holds |
|---|---|---|
| `world-checkpoint-1` | `22955f437a` | Street-first layout (village main street and lanes, city grid with sidewalks), lots facing the street, generated fences, structure roles, `structurecheck`, structures re-cut without fences. The state the project owner last saw before the unattended night of 2026-10-14. |

To go back: `git checkout world-generator-spike && git reset --hard world-checkpoint-1` (this throws away later commits on the branch; make a branch of the current tip first if they should be kept). The tag is local; the commit is on `origin/world-generator-spike`.

## 2026-10-14, unattended night

The project owner went to sleep and asked for continuous improvement of map generation along the design docs. Rules I keep while alone: no merges, no pull requests, no force-pushes, no new player-facing text without marking it here for approval, every change built and probed before it is committed, one commit per step.

Player-facing text added during the night (names, descriptions, popups) is listed here for approval:

- (none yet)

### Done, in order

1. **Organic shapes** (`4f3701429d`): the playable ground is a blob with the same area as the nominal size (`shape: organic`, `shape: rect` for the old square), a box 1.5 times bigger holds it; shore, streets, lots, trees and the border all follow the edge of the shape. Back lanes in the village (loops, not only dead ends), trees along the village roads, street lights (`LightPostSmall`, already existing) along the city sidewalks.
2. **The web works** (this commit): `worldGraph` prototype (`Default`: Village - tunnel - Snow City), `WorldGate` entities (look like the bunker hatch, named *hatch* as the bunker's), gates placed at the ends of the main roads, a generated **tunnel** between the village and the city (`TunnelLayout`: winding three-wide corridor, rest stops, rubble that never closes it, crates, lights, four mob spawners), walking through gates (the user and what they pull), the **storm** (warning with a siren, entry blocked, the location and the tunnel that follows it are deleted and built again with a new layout, everyone inside is gone), staggered timers (6 to 12 hours, an hour apart if the range allows). Commands: `worldbuild [graph] [seed]`, `worldreset <location> [now]`, `worldstatus`, `worldgo <location> <gate>`. Verified with a throwaway engine test: the tunnel can be walked from one gate to the other without digging, a person walks village gate to tunnel to city, the storm deletes them and rebuilds with a new seed.

