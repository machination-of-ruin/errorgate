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
