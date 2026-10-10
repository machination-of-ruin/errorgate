# >>> SURVIVAL LOOP <<<

## Pillars

1. **Needs.** Hunger, thirst, temperature, sickness, injuries that matter.
2. **Crafting.** Weapons, tools, medicine and gear made from scavenged materials.
3. **Looting / scavenging.** Ruins and points of interest across the planet are the main source of gear and materials.
4. **Cooperation.** Players are pushed together by:
   - **Shared objectives:** world events and AI GOD goals that need several players to complete.
   - **Scarcity:** resources and zones too scarce or dangerous to handle solo, which encourages groups.

Not in scope for now: specialised skills that lock actions to certain players, and a trade/currency economy.

### Current state of needs

- Hunger is lethal: decay rate 0.1 (default 0.0167) and starving deals 1 Bloodloss per second (`Hunger` on `BaseMobHuman`). There is no CVar for it, it is set in the prototype.
- Food: craftable campfire (burns out, cooks meat), butchering humans with a knife, bone helmet from a skull only.
- Hazards: toxic water, deadly ammonia gas, spike traps.
- Light: lighters in the light slot, glowsticks in loot, broken floodlights are structures and not items.

## Building

- Full creative freedom, limited resources. Materials are the bottleneck, not build options.
- Bases are ruined structures that already exist in the world; players do not build a base from nothing.
- **Claiming is physical.** No claim mechanic: whoever occupies and defends a ruin owns it. Doors and locks only.
- **No offline protection.** A ruin can be raided at any time, owners online or not.
- **Group size is not enforced.** Ruins are meant for a couple of players; this is a design intent handled through ruin size and layout, not a hard limit.

## Loot

- Finite per wipe. The DayZ-style system is ported: `Content.Server/_ERRORGATE/LootManager/` (`LootManagerSystem`) with the global table `LootTableGlobal` and per-map tables (`Resources/Prototypes/_ERRORGATE/LootManager/`). Floor spawners and mob drops draw from the same shared pool (`MaxCount - Count` weights); an item deleted or despawned refunds its slot, the pool resets on round restart.
- Items lying on the ground despawn after 20 minutes (and refund), dead bodies are gibbed after 10 minutes (`Content.Server/_ERRORGATE/Despawn/`).
- Guns found in loot spawn with a random amount of ammo (`*RandomMag` prototypes).
- AI GOD events and rewards are an additional source of items.

## Open

- Which existing systems to reuse for needs (SS14 hunger/thirst/temperature, Shitmed for injuries) versus rewrite.
- Crafting system: SS14 construction graphs, a ported crafting module, or new.
- Item caps per table: the numbers in `globalloottable.yml` and `kuznetsk.yml` are a first pass and need tuning from play.
- What can be built inside a ruin, and which materials are scarce.
- Raiding tools: what can break walls and doors, and how long it takes.
