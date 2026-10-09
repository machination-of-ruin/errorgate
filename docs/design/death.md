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

## Respawn cooldown

CVar `errorgate.respawn_cooldown` (`ErrorgateCVars.RespawnCooldown`, seconds, server, default `0` = immediately). A player in the void sees the remaining time on the death overlay; the respawn action and the `/respawn` and `/rise` commands refuse until it has passed. `forcerespawn` (admin) ignores it. Covered by `DeathVoidTest.RespawnWaitsForTheCooldown`.

## Respawn location

Fixed spawn zones: a set of designated spawn areas (DayZ-style), not random spots. Maps currently spawn players through the `HumanError` job at spawn points (see [maps.md](maps.md)); designated zones come with the survival spawn rules.

## Open

- How a spawn zone is picked (random among all, nearest/farthest from the body, player choice).

- Whether revival should stay possible after the player has already respawned (currently respawning abandons the body).
