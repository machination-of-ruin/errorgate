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

The death message is one red block: the title, a technical log of the last 8 things that happened to the character, and the line "YOU FAILED TO ESCAPE THE MACHINATION OF RUIN. /RISE AND TRY AGAIN." at the bottom. The log is impersonal (no name of the dead, no "you", no "it"), counts time back from death in minutes, seconds and hundredths (the bars line up), has no header, and never says what to do or why a rule exists.

```
ERROR:
YOU ARE DEAD

T-23:10.04 |  SUBJECT INSTANTIATED
T-04:52.37 |  SPEECH      "stay back"
T-04:49.11 |  HEARD       URIST MCHANDS: "give me the rifle"
T-01:15.80 |  DAMAGE OUT  WALKER (601): 30
T-01:15.80 |  DELETED     WALKER (601)
T-00:41.02 |  DAMAGE IN   STARVATION: 12 x12
T-00:03.55 |  DAMAGE IN   COLLAPSE FAULT: 175 x3
T-00:00.00 |  SUBJECT TERMINATED

YOU FAILED TO ESCAPE THE MACHINATION OF RUIN. /RISE AND TRY AGAIN.
```

- **Entries:** speech and whispers (cut at 40 characters), speech heard from players in range (voice 10 tiles, whisper 2), harm taken, harm dealt to players and mobs, and kills. A kill is credited to the last player who hurt the victim in the last minute. The "SUBJECT INSTANTIATED" line only appears when the whole life fits in the log.
- **Harm sources:** the player, mob or object that dealt it, a world fault ("HEAT FAULT"), "SELF", or, when nobody dealt it, the environment (FIRE, COLD, NO AIR, BLOOD LOSS, POISON, RADIATION). Hits from the same source within 5 seconds merge into one line with the total and a count.
- **Minor damage:** damage nobody dealt (bleeding, no air, starvation) comes in tiny ticks. It is added up per cause over 30 seconds, whatever happened in between, and a cause that adds up to less than 5 does not appear at all. Starvation deals blood loss, so a starving character who takes exactly the damage hunger deals is logged as STARVATION (a wound of the very same size in the same moment would be called starvation too).
- **One message:** a mind is sent to the void once. A corpse that is gibbed or crushed afterwards (a collapse fault does this) does not send the death message again, and the dead do not see their own death emote ("seizes up...") because the void handles the death before the emote goes out.
- **Not yet:** talking to MACHINATION OF RUIN (no channel exists), more event types (loot, building, food), a final line written by the model.
- Code: `Content.Server/_ERRORGATE/LifeLog/` (`LifeLogSystem`, `LifeRecord`), the message is built in `DeathVoidSystem.SpawnVoid`, strings `Resources/Locale/en-US/_ERRORGATE/life-log.ftl`, test `LifeLogTest`. The record belongs to the mind, so it survives a gibbed body.

## Respawn cooldown

CVar `errorgate.respawn_cooldown` (`ErrorgateCVars.RespawnCooldown`, seconds, server, default `0` = immediately). A player in the void sees the remaining time on the death overlay; the respawn action and the `/respawn` and `/rise` commands refuse until it has passed. `forcerespawn` (admin) ignores it. Covered by `DeathVoidTest.RespawnWaitsForTheCooldown`.

## Respawn location

Fixed spawn zones: a set of designated spawn areas (DayZ-style), not random spots. Maps currently spawn players through the `HumanError` job at spawn points (see [maps.md](maps.md)); designated zones come with the survival spawn rules.

## Open

- How a spawn zone is picked (random among all, nearest/farthest from the body, player choice).

- Whether revival should stay possible after the player has already respawned (currently respawning abandons the body).
