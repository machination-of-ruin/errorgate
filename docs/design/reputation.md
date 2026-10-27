# >>> REPUTATION <<<

CORE BUILT (standing, bands, storage, markers, commands). THE KILL MATRIX IS NOT: its numbers are proposals. Per account and per faction (see [factions.md](factions.md)). Range -1000 to +1000, **start 0, which is hostile to all**.

## >>> BUILT

- **Factions with a standing:** Military and Bandit. The Ruined have none (they hate everyone).
- **Bands (Decided):** below 100 Hostile, 100 to 399 Neutral, 400 and above Friendly. A faction's NPCs shoot Hostile players on sight and ignore Neutral and Friendly ones. Friendly will open the base, trade and spawning later.
- **Mechanism:** `Content.Server/_ERRORGATE/Reputation/ReputationSystem.cs`. A faction lists a marker faction as hostile (`HostileToMilitary`, `HostileToBandit`, `Factions/factions.yml`) instead of listing every Survivor; a player carries the marker exactly while Hostile with that faction, kept in sync on every change and when a player is attached to a body. No engine change, no per-NPC cost.
- **Storage (Decided):** table `faction_reputation` (player id, faction, value; unique per player and faction), migrations for Sqlite and Postgres. Loaded when the player connects, saved every minute, at disconnect and at shutdown. Guests are never saved (their standing lives for the session).
- **API:** `GetStanding`, `GetBand`, `Adjust`, `Set` (clamped, notifies the player). The kill matrix will call `Adjust`.
- **Commands:** `reputation` (a player sees their own standing), `setreputation <player> <faction> <value> [add]` (admin, Fun flag).
- **Notifications:** one chat line on every change (`Military reputation -40.`), a second chat line when a band is crossed. CVar `errorgate.reputation.notifications` (server, default true) turns them off; the standing changes either way. All lines in `Locale/en-US/_ERRORGATE/reputation.ftl`.

## >>> KILL MATRIX (Proposed)

Rows: who you killed. Columns: whose standing moves. Everything else: 0.

| You killed | Military | Bandit | Survivors (players) | Ruined |
|---|---|---|---|---|
| Soldier / medic / officer | -40 | +15 | 0 | +0 |
| Bandit / bandit boss | +15 | -40 | 0 | +0 |
| Bandit boss | +30 | -80 | 0 | 0 |
| Walker / runner | +2 | +2 | 0 | n/a |
| Hostile wildlife (xeno, zombie) | +2 | +2 | 0 | n/a |
| A player who was friendly to a faction (standing at least Friendly) | -60 with that faction | | | |
| Any other player | 0 | 0 | 0 | 0 |

Rules (Proposed): hits on a friendly player or a downed NPC give nothing; kills count only for the killer (or the last attacker, assist is Later); repeated kills of the same faction within 10 minutes give 50% each after the 5th (anti-farming); a cap of +/-200 per hour per faction.

## >>> OPEN

- Every number of the kill matrix, the cap and the anti-farming rules.
- Whether the Ruined ever get a standing (draft: no).
- Not tested live: the database round trip (save and load across a reconnect) and the notification texts in game.
