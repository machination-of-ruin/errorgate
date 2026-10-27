# >>> REPUTATION <<<

BUILT: standing, bands, storage, markers, commands, the kill matrix (its numbers can still change). Per account and per faction (see [factions.md](factions.md)). Range -1000 to +1000, **start -100, which is hostile to all**.

## >>> BUILT

- **Factions with a standing:** Military and Bandit. The Ruined have none (they hate everyone).
- **Bands (Decided):** below 0 Hostile, 0 to 399 Neutral, 400 and above Friendly. A faction's NPCs shoot Hostile players on sight and ignore Neutral and Friendly ones. Friendly will open the base, trade and spawning later.
- **Mechanism:** `Content.Server/_ERRORGATE/Reputation/ReputationSystem.cs`. A faction lists a marker faction as hostile (`HostileToMilitary`, `HostileToBandit`, `Factions/factions.yml`) instead of listing every Survivor; a player carries the marker exactly while Hostile with that faction, kept in sync on every change and when a player is attached to a body. No engine change, no per-NPC cost.
- **Storage (Decided):** table `faction_reputation` (player id, faction, value; unique per player and faction), migrations for Sqlite and Postgres. Loaded when the player connects, saved every minute, at disconnect and at shutdown. Guests are never saved (their standing lives for the session).
- **API:** `GetStanding`, `GetBand`, `Adjust`, `Set` (clamped, notifies the player). The kill matrix will call `Adjust`.
- **Character window:** the in-game character info window shows the standing with each faction (a `Reputation` heading and one line per faction).
- **Commands:** `reputation` (a player sees their own standing), `setreputation <player> <faction> <value> [add]` (admin, Fun flag).
- **Notifications:** one chat line on every change (`Military reputation -40.`), a second chat line when a band is crossed. CVar `errorgate.reputation.notifications` (server, default true) turns them off; the standing changes either way. All lines in `Locale/en-US/_ERRORGATE/reputation.ftl`.

## >>> KILL MATRIX (Built, numbers still tunable)

A player who deals the killing blow moves their standing. NPCs pay what the `ReputationReward` component on their prototype says (`Factions/military.yml`, `bandits.yml`, `MobSpawners/mobs.yml`); players pay the values in `ReputationKillSystem`. A mob pays once, even if revived. Kills by NPCs, fire, faults or hunger pay nothing.

| You killed | Military | Bandit |
|---|---|---|
| Soldier, medic, officer | -20 | +15 |
| Bandit | +15 | -15 |
| Bandit boss | +30 | -80 |
| Walker, runner (all Ruined mobs) | +5 | 0 |
| A player | -25 | +25 |

- **Per-victim rule:** killing the same player again within an hour pays nothing (no farming a friend or an alt).
- **Anti-farming (kills per faction, hourly cap) is off for now.**
- Hostile wildlife (xeno, zombie) pays nothing yet.

## >>> OPEN

- Every number of the kill matrix; the anti-farming rules; a loss floor (a deep negative standing is hard to climb out of).
- Whether the Ruined ever get a standing (draft: no).
- Not tested live: the DB round trip (save and load across a reconnect), the per-victim rule (needs two players), the chat lines and the character window in game.
