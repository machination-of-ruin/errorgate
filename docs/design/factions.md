# >>> FACTIONS <<<

Distinct factions with roaming NPC squads, bases and traders. What a player does in the world (kills, tasks) moves their **reputation**; reputation unlocks what the faction's NPCs let them do, and in the end **who they can spawn as**. That gives players a real reason to fight other factions or to help each other. Read [world.md](world.md) for the map side and [VIBE.md](VIBE.md) for the premise.

Sections marked **Decided** come from the project owner. **Proposed** is a default to confirm. **Later** is not designed yet.

## Principles (Decided)

- Playing without a faction, or fighting everyone, is a valid way to play. New players and hardcore players do not have to join anything.
- She (the AI GOD) never gives goals. The factions' **tasks** come from their own NPCs, so they are the world's goals, not hers. Factions have their own aims and the players roleplay them.
- The AI GOD arrives after this rework is fully implemented (see [ai-god.md](ai-god.md)).

## The three factions

| Faction | Style | First slice |
|---|---|---|
| **Military** | Tacticool soldiers with guns. | **Yes.** Base in location 2 (see [world.md](world.md)). |
| **Knights** | Melee fighters. Their goal is to purge the AI faults, so **mob kills count for them**. | Later |
| **Cultists** | Magic, tied to the AI GOD. | Later |

Only the Military is designed in detail now. Relations between the factions (who hates whom, how reputation moves across them) are **Later**.

## Reputation (Decided + Proposed)

- Stored **per account and per faction**. It survives death and changing characters. It is the one thing that does. (Proposed: it also survives the weekly wipe, with an admin command to reset it; to be confirmed.)
- **What changes it (Decided):** killing players, NPCs and mobs; doing tasks. How much each is worth is **Later** (numbers go in a table in this doc once decided).
- Reputation with the Military goes down when you kill their NPCs or members, and up when you do what they ask or kill what they hate. A player without a faction can still gain or lose it.
- **Anti-farming (Proposed):** diminishing returns on repeated kills, a cap per hour, decay over time. Guest accounts have no stable identity, so they get no persistent reputation.

### Milestones (Proposed)

| Milestone | What it allows |
|---|---|
| Hostile | Shot on sight, as before. |
| Neutral (default) | Shot on sight by the faction's NPCs. Cannot enter the base. |
| Friendly | Enter the base. Talk to NPCs, basic trade, take tasks from the base task giver. |
| Trusted | Better trade options. Take tasks from any NPC of the faction (Later). |
| Member | Spawn as a basic role of the faction at its base. |
| Veteran | Spawn as a high-tier role: better starting gear, limited slots. |

### What NPCs do with it (Decided)

- Players of other factions or without a faction are **shot on sight**, unless they meet the reputation needed for the base. A player who reaches the threshold becomes friendly to that faction's NPCs.
- **Guards detect a friendly player being attacked and punish the attacker.**
- Bases can be raided and every NPC can be killed.
- Technically (Proposed): NPC hostility already comes from faction membership lists and per-entity exceptions (`NpcFactionSystem`); reaching or losing a milestone adds or removes the player from the faction's friendly list. Guard punishment builds on `NPCRetaliationSystem`.

## Roles and spawning (Decided)

- The **faction base is a spawn option** for players who have the reputation. Everyone else spawns in location 1 at the spawn zones.
- A faction role spawns with a **basic kit**. **High-tier roles** give better starting gear and are **limited** (to prevent abuse and serve the lore), so slots per role are a server setting.
- This is an exception to [death.md](death.md): you still come back with nothing from a death, but a faction spawn brings the faction's basic kit.
- **Recognizable without gear:** a faction member must look like one. A forced look (skin color, markings) applies when spawning as that role. For now **the examine description says the faction**; markings come later. The look is **permanent for that life**: after a respawn and the lobby the player can choose anything.
- The species stays human ([humans-only.md](humans-only.md)): skin and markings only.

## Currency, trade and the stash (Decided)

- **Each faction has its own currency.** It is a **physical item**: it can be lost, dropped, looted from bodies, and does not survive the storm or the wipe.
- Players earn it with **tasks** and by **selling special valuable items**. They spend it with the **NPC trader in a faction base**, on faction-specific gear. Better offers need higher reputation.
- **Safe stash.** A player can put items and money in a **personal container in a friendly base**. It cannot be opened by raiders. It does not survive the wipe. Proposed build: the container lives in a hidden vault map and is opened from a terminal in the base through the storage UI. Spike: confirm that a storage UI can be opened on a remote container.

## Tasks (Decided)

Given by an NPC in the faction's base at first, later by any NPC of the faction. The set of task kinds (kill, fetch, deliver, escort) and their rewards is **Later**; the first slice has one kind.

## The Military (first slice)

- **Base:** a hand-built map branching off location 2. It never resets. Parts that the map needs: spawn points for the faction role, the trader's stand, the task giver's stand, guard posts, a stash terminal room, the transition to location 2, and a medical room (details are for the builder).
- **Squads:** roaming groups of soldiers in locations 1 to 3 and guards at the base. NPCs are only simulated in loaded maps.
- **Currency, trader stock and task kind:** Later (a first pass is enough for the slice).

### Soldiers and bandits (built)

- **Factions:** `Military` and `Bandit` (`Resources/Prototypes/_ERRORGATE/Factions`). Both are hostile to players (`Syndicate`), to each other, to walkers and runners (`SimpleHostile`), xenos and zombies; walkers and runners are hostile to both. Members of a faction are friendly to each other, so all bandits stand together, whatever their squad.
- **Mobs:** soldier (rifleman), medic, officer; bandit, bandit boss. Looks follow the Solar Alliance kits (`MilitaryRiflemanGear`, `MilitaryMedicGear`, `MilitaryOfficerGear`, `BanditGear`, `BanditBossGear`), PVP-only items replaced by repo ones. Each has a spawner (`MobSpawnerMilitary*`, `MobSpawnerBandit*`) in the spawn menu.
- **Squads:** `squads` on a `worldLocation`: a list of `members` (a spawner each) and a `count`. The generator puts each squad on a cluster of free tiles within 3 steps of its first tile, away from the spawn clearing and other squads. In use: the village has 2 bandit squads (3 bandits + boss), the snow city 1 bandit squad and 3 military squads (officer, 2 riflemen, medic).
- **Guns (Decided):** bandits get one gun at random: Makarov, IZh, sawn-off IZh or Simonov, no revolvers; the boss has the AK and a Stechkin. Armed NPCs use `ErrorgateHumanoidHostileCompound`: draw a gun from suit slot, holster or bag when an enemy is near and the hands are empty; when the gun has no live round (a revolver of spent casings counts) stand for a reload (2.2 s magazine, 3 s shells, 4 s cylinder) and load new ammunition out of nowhere. Bullets an NPC fires fly through its own side (`SharedProjectileSystem.PreventCollision`).
- **Gear:** they wear it for real, so a dead soldier is a source of weapons. A living NPC cannot be stripped (`SharedStrippableSystem.IsLivingNpc`: an NPC that is awake, i.e. alive and not downed); a dead or downed one can.

## Technical notes

- No reputation table exists in the database. It is a new table, modeled on `PlayTime` (account id, faction, value), with migrations for **both Postgres and SQLite**.
- The job system currently has one job (`HumanError`). Faction roles are new jobs with a reputation requirement and a slot limit.
- NPC squad behavior (formations, patrol routes, base defense) is new work on top of the HTN/NPC systems; many armed NPCs cost server time, so measure early.
- Traders and the reputation UI (dialogue, shop) have no existing equivalent to reuse.

## Open

- Reputation numbers: kill values, task values, thresholds, decay and caps.
- Whether reputation survives the wipe (Proposed: yes, with a reset command).
- Faction relations across the three factions.
- The Knights' and the Cultists' bases, trades, roles and how magic is built (the repo has leftover blood cult and wizard modules; psionics are off).
- Whether the AI GOD treats cultist players differently once she is implemented.
