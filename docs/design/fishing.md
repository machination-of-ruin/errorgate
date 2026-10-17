# >>> FISHING <<<

**Status: proposal, not built. Nothing here is decided until it is approved.** Part of [survival-loop.md](survival-loop.md): a slow, safe-ish food source for the shore and the ponds, in a world where hunger is lethal (decay 0.1, starving deals Bloodloss).

## What is already in the repo

The Goobstation fishing system came with the fork and was never fitted to ERRORGATE:

- **Code:** `Content.Server/_Goobstation/Fishing/FishingSystem.cs`, components under `Content.Shared/_Goobstation/Fishing/` (`FishingRod`, `FishingSpot`), a client overlay.
- **Rods** (`_Goobstation/Entities/Objects/Tools/fishing_rods.yml`): `FishingRod`, `FishingRodGoon` (same rod, other sprite), `FishingRodMakeshift`, `FishingRodGolden`, plus `FishingLure`.
- **Spots:** `BaseFishingSpot` ("suspicious waves") and `FishingSpotWater`, and `FishingSpot` already sits on the tile entities of water, lava and liquid plasma (`Resources/Prototypes/Entities/Tiles/water.yml`, `lava.yml`, `liquid_plasma.yml`) and on the toilet.
- **Catch:** `WaterFishingLootTable` (`loot_tables.yml`), a timer of 25 s plus up to 15 s of variety per bite (`fishDefaultTimer`, `fishTimerVariety`). The fish (`fish.yml`) are a joke zoo: alien fish, nuke disk fish, Ian fish, a gib fish. Most of that must go.

**Not checked in game:** whether fishing works at all in the world today, and whether the world's water (the `FloorWaterEntityToxic` tile and the village ponds and shore) carries a spot.

## Proposal

1. **Where.** Shallow water of the village shore and the ponds (location 1). The snow city has ice only: no fishing. Toxic water is the exception, see 3.
2. **The catch is food, and small.** A short list of plain fish (a handful of types, raw, edible only when cooked at a campfire, a little food each). Raw fish is poor food and may make you sick. The joke fish, rare and legendary tables are removed from the water table, not just hidden.
3. **Toxic water.** A spot on toxic water gives fish that are poisoned: eating one hurts. It is a trap for people who do not read the water, and a reason to carry a Geiger counter or a filter. Nothing from toxic water is food.
4. **The rod.** Crafted, not found: a stick, string and a hook make `FishingRodMakeshift` (poor catch rate). The basic rod is rare loot (Living, Industrial; tier 1-2, small cap). The golden rod and the lure are removed. This follows the cost-per-upgrade rule in [progression.md](progression.md).
5. **Time and risk.** A bite takes 25 to 40 s (the existing timer): you stand at the water, which is slow terrain and in the open, so fishing costs attention and exposure, not only time. Mobs can find you there.
6. **Out of character.** No fish puns, no jokes, no named fish. Item texts stay plain, per [VIBE.md](VIBE.md).

## Open questions (need an answer before building)

- Is fishing wanted at all in this loop, next to hunting, butchering and the campfire?
- Should fish be the only reliable food, or a small extra?
- Crafted rod or found rod, or both (proposal: both)?
- Can a player be sick from raw fish, and does the world already have a sickness system to use for it?
- Poisoned fish from toxic water: a trap, or no fish at all?
- Names and descriptions of every fish and of the rod: player-facing, to be drafted and approved.

## If approved, the work

Check the system in game first; then cut the fish and tables down, add the rod to the loot and the crafting, place spots on the shore and pond tiles in the world generator, add a loading tip (to be approved), and write a test that a spot yields only food.
