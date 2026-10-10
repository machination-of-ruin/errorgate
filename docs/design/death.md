# >>> DEATH <<<

## Decision

Respawn, lose gear. A dead player stays in the death void until they decide to respawn. They come back as a new character with nothing. Their body and everything on it stay where they died, lootable by anyone.

## Current implementation (port #2, commit `765da4a1ee`, later fixes)

- No ghosts. On death the player's mind moves to an empty entity on an empty map, so they cannot see, hear or read anything around the corpse.
- They return to the body if revived, or leave the void with the respawn action or the `/respawn` and `/rise` commands. Admins use `forcerespawn`.
- Anything that destroys the body (gibbing, explosions, chasms, admin delete) also ends in the void. Gibbing never drops organs, so a brain can never carry the mind; a brain that somehow receives a player's mind sends it to the void.
- "Succumb" and "say last words" kill the player and so end in the void too.
- The death message is a server chat message split into short lines (a wrapped large font overlaps later messages in a narrow chat).
- Code: `Content.Server/_ERRORGATE/DeathVoid/` (`DeathVoidSystem`, `SelfRespawnCommand`, `DeathVoidComponent`), client overlay in `Content.Client/_ERRORGATE/DeathVoid/`, prototype `Resources/Prototypes/_ERRORGATE/Mobs/death_void.yml`, strings `Resources/Locale/en-US/_ERRORGATE/death-void.ftl`, test `Content.IntegrationTests/Tests/_ERRORGATE/DeathVoidTest.cs`.

Bodies follow the 10-minute despawn timer in [survival-loop.md](survival-loop.md), so a body is not lootable forever.

## Life log (what the dead see)

With the death message, the player gets a short cold log of that character's life, with fixed wording and names of characters (not accounts). It records only what happened, never what to do, and never why a rule exists. Example:

```
>>> LOG OF IVAN PETROV <<<
ERRORS COUNTED: 23 MINUTES.
LAST HARMED BY BORIS: 20 DAMAGE.
IT DELETED: SEWER RAT.
IT SPOKE WITH BORIS: 2 LINES.
LAST WORDS RECORDED: "wait"
ERROR NOT CORRECTED.
```

- **Harm:** the last attacker is a player, a mob or object, a world fault ("A HEAT FAULT"), the character itself, or, when nobody dealt it (and no attack in the last minute), the environment: fire, cold, no air, blood loss, poison, radiation.
- **Deeds:** who the character hurt, and who they deleted (kills of players and mobs, credited to the last player who hurt the victim in the last minute).
- **Talk:** players within hearing range when the character spoke (voice 10 tiles, whisper 2), line count, and the last words. If the character never spoke but was spoken to, it names those who spoke.
- **Not yet:** talking to MACHINATION OF RUIN (no channel exists), more deeds (loot, building, food), a final line written by the model.
- Code: `Content.Server/_ERRORGATE/LifeLog/` (`LifeLogSystem`, `LifeRecord`), hook in `DeathVoidSystem.SpawnVoid`, strings `Resources/Locale/en-US/_ERRORGATE/life-log.ftl`, test `LifeLogTest`. The record belongs to the mind, so it survives a gibbed body.

## Respawn cooldown

CVar `errorgate.respawn_cooldown` (`ErrorgateCVars.RespawnCooldown`, seconds, server, default `0` = immediately). A player in the void sees the remaining time on the death overlay; the respawn action and the `/respawn` and `/rise` commands refuse until it has passed. `forcerespawn` (admin) ignores it. Covered by `DeathVoidTest.RespawnWaitsForTheCooldown`.

## Respawn location

Fixed spawn zones: a set of designated spawn areas (DayZ-style), not random spots. Maps currently spawn players through the `HumanError` job at spawn points (see [maps.md](maps.md)); designated zones come with the survival spawn rules.

## Open

- How a spawn zone is picked (random among all, nearest/farthest from the body, player choice).

- Whether revival should stay possible after the player has already respawned (currently respawning abandons the body).
