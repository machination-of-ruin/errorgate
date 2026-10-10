# >>> PROGRESSION <<<

## Decisions

- **Only knowledge survives death.** A respawned player has nothing: gear, keys and implants stay on the body. What carries over is what the player knows (routes, which doors open what, who holds what, where a stash is).
- **Players make their own persistence.** Nothing stops them from hiding excess loot in crates and drawers where others rarely look, or holding a ruin as a base. A stash is how a dead player gets back in: to recover the body or take revenge. No system is built for this, the world already allows it.
- **One round only.** Nothing carries between rounds (wipes).
- **Implants start with the SS14 internal implants.** They are invisible on the body, so the rule in [humans-only.md](humans-only.md) (no cyborg look) holds. Psionics and spells are possible later and must be added carefully so the game stays grounded; psionics are not implemented now (see [ROADMAP.md](ROADMAP.md)).

Progression is read against the premise in [VIBE.md](VIBE.md): she never guides, so nothing here comes with a quest, a marker or a "next step".

## Axes (Proposed)

Every upgrade has a cost, so no tier is strictly better. Inspiration: Stalker Anomaly (gear tiers climbing with zone danger, anomalies and artifacts), Caves of Qud (identity from tradeoffs), Cataclysm: DDA (bionics with a budget).

| Axis | Items | Cost that stops it being strictly better |
|---|---|---|
| Power | Weapons, guns | Better calibers are rarer. Ammo is the real gate, not the gun. |
| Defense | Armor | Heavier armor tires you faster and is louder. |
| Capacity | Inventory space | Bags must come off to open, satchels are always open but smaller. |
| Perception | Light | Lighter, then flashlight, then floodlight. A brighter light shows you to everyone. |
| Access | Keys and keycards from loot rooms | Unique items: only one exists, so the holder can be robbed. |
| Body | Internal implants, rejecting humanity | See below. |

## Location progression (Proposed)

- **Depth in EDGE OF ENTROPY.** Keycards from loot rooms open deeper levels, which are harder and pay better. Nobody tells players to go. The final bunker doors are the model (see [pressure.md](pressure.md)).
- **Kuznetsk.** The factory with limited access holds the military loot. Danger and loot climb together.
- **Anomalies.** Rare items behind hazards, sometimes with a drawback when carried.

## Current loot tiers (as implemented)

Floor loot comes from loot spawners (`Resources/Prototypes/_ERRORGATE/LootManager/`). Each spawner has a **location** (the kind of place) and a **tier** from 1 to 3. A spawner only draws from loot entries that list its location and tier, then the shared per-round cap (see [survival-loop.md](survival-loop.md)) decides what is still available. By default a spawner tries about 3 times an hour, in 5-minute checks, and does nothing if one of its table's items already lies on its tile.

Locations: Living, Military, Security, Prison, Medical, Industrial, Kitchen, Church, School.

| Location | Tier 1 | Tier 2 | Tier 3 |
|---|---|---|---|
| Living | 64 | 36 | 16 |
| Military | 28 | 46 | 56 |
| Security | 29 | 44 | 36 |
| Prison | 25 | 26 | 19 |
| Medical | 38 | 29 | 27 |
| Industrial | 57 | 34 | 25 |
| Kitchen | 11 | 6 | 8 |
| Church | 14 | 6 | 6 |
| School | 21 | 8 | 3 |

Numbers are loot entries available at that location and tier (298 entries in total, one entry can sit in several locations and tiers).

What the tiers mean in practice:

- **Tier 1:** everyday and weak. Makarov pistols, lighters, basic coats, holsters, bandoliers, food, bandages.
- **Tier 2:** useful. The AK rifle, floodlights, flak vests, combat boots, better tools and medicine.
- **Tier 3:** rare and strong. Bulletproof and plate carrier armor, combat gloves, the Hristov sniper rifle, C4, night vision goggles, the mercenary backpack and webbing, advanced surgical gear.
- **Military** is the strongest source (most Tier 2 and 3 entries). **Living** is mostly Tier 1 and is where the only key lives.
- **Keys:** the only key entry is `FactoryCard` (Tier 1, Living). Keycards for deeper levels are not in the loot system yet.

Caps: `LootTableGlobal` gives every item a maximum number per round, and `Kuznetsk` has its own smaller table. About 60 items in the global table have a cap of 1. Those show `>>> Unique <<<` on examine, so only one exists at a time.

## >>> Rejecting humanity <<<

The body axis. She wants HUMAN ERROR removed, so shedding human traits (hunger, pain, sleep) looks like progress to her, and it is a real advantage to a player. It never reaches the ERRORGATE, because that is unachievable. Starting with invisible internal implants, each with a cost (a budget, a power need, a side effect on the player), so a build is a choice and not a ladder.

Open: which implants exist in the repo and which fit the grounded feel; whether a rejecting build changes how she logs you.

## Where it interacts with existing rules

- **Body retrieval window.** Bodies are gibbed after 10 minutes ([survival-loop.md](survival-loop.md)). That is intended: the maps are small and the timer keeps server performance up. It also caps how long a stash run to recover a body can take.
- **Stashes are safe.** Only items lying directly on a grid count toward the 20-minute despawn. Anything carried or stored in a container is exempt, and the timer restarts if an item is dropped again (`DespawnItemSystem.UpdateTimer`, verified in code).
- **Finite loot.** The loot manager caps what exists per round, so progression is bounded by what spawns. Rare tiers need their own tables.
- **Corpses as loot.** Implants on a body are lootable, which ties progression to cannibalism and PvP.

## Open

- Which implants to enable, and what each costs.
- Whether unique keycards respawn after being lost or destroyed.
- How a heavy bag, armor or light affects stamina and noise, as numbers.
- How rare tiers are spread across EDGE OF ENTROPY and Kuznetsk, and whether depth alone gates them.
