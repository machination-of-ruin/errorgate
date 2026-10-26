# >>> REPUTATION <<<

DRAFT. Numbers are proposals, nothing is built. Per account and per faction (see [factions.md](factions.md)). Range -1000 to +1000, start 0.

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

## >>> THRESHOLDS (Proposed)

| Standing | Name | Message |
|---|---|---|
| -1000 to -300 | Hostile | You are hostile with Bandits now. |
| -299 to 99 | Neutral | You are neutral with Bandits now. |
| 100 to 399 | Friendly | You are friendly with Bandits now. |
| 400 and above | Trusted | You are trusted by Bandits now. |

Note: Neutral is the default and is shot on sight by NPCs (see factions.md); Friendly stops that.

## >>> NOTIFICATIONS (Proposed)

- Popup to the killer on every change: "Military reputation -40." / "Bandit reputation +15." Chat line with the new total.
- A second popup when a threshold is crossed (messages above).
- CVar `errorgate.reputation.notifications` (server, default true) turns both off. The numbers still apply.

## >>> OPEN

- Every number above, the cap and the anti-farming rules.
- Whether the Ruined have a standing at all (draft: no).
- Storage: new table modeled on `PlayTime`, migrations for Postgres and SQLite, own branch off `master`.
