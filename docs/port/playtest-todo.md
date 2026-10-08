# >>> PLAYTEST TODO <<<

Found while playing the `port/beyond` build. One line each, newest last.

- [ ] Loading screen tips (the station tips dataset, `Resources/Locale/en-US/tips.ftl`, `Datasets/tips.yml`) need to be rewritten in the ERRORGATE voice or removed.
- [ ] Rules and playtesting tips lose their line breaks (`ServerInfo/Guidebook/ServerRules/ErrorgateRules.xml` is plain text inside `<Document>`, the guidebook parser needs `\n` or blank lines).

## Window, lobby, menus

- [ ] OS window title adds "myserver". It must always read ERRORGATE.
- [ ] Guidebook goes (button, hotkey, F1 help links).
- [ ] Lobby text "Hi and welcome to Space Station 14" becomes "welcome to ERRORGATE".
- [ ] Lobby music is vanilla, not ERRORGATE. Only ERRORGATE tracks should play (check the lobby sound collection and the `Audio/Lobby` tracks).
- [ ] "Have a happy lesbian day": holiday greetings are on. Disable the server holiday cvar.
- [ ] Crew manifest lobby button is not needed, remove it.

## Character setup

- [ ] Remove the animal voices (bark voice list: humans' voices only).
- [ ] Remove traits (tab and any data).
- [ ] Remove the custom fluff / flavor description.
- [ ] Remove pronouns: default to the ones that go with the chosen sex.
- [ ] Players must not be able to cheat by importing characters with traits, loadouts and so on. Sanitize imported and saved profiles on the server.

## In game

- [ ] Not walking by default (check whether the old setting carried over; the cvar default is true and is archived client side).
- [ ] Running does not cost stamina.
- [ ] Lighter does not light the increased range it had in the old build.
- [ ] World lighting on the dev map and EdgeOfEntropy must go, they are underground (no ambient day/night, `MapLight` and `LightCycle` on those maps).
- [ ] Rat king has a localized Russian name.
- [ ] Some maints floors are missing a texture.
- [ ] Combat logs: the bleeding lines are too large ("hits you are fine"). SS14 chat cannot do larger fonts with a proper offset, so limit them to the size the hit lines use. Hit color orange, not yellow. Bleeding red.
- [ ] "Say last words" does not kill me. Same with "succumb".

## Found in the server log (not reported by the player)

- [x] `station_record_key_storage` errors on every spawn: the Human Error lighter sits in the id slot and station records treat it as an ID card. Fixed in `StationRecordsSystem` (needs a rebuild and server restart to take effect).
- [ ] "MainLoop: Cannot keep up" appeared 5 times in the first session. Check what spikes (map load, loot spawners, many DespawnItem items?).
- [ ] Map warning: `PlushieLizard` is an obsolete prototype used by a map (6 times).

## Round 2 (playtest reports)

- [x] Weapon damage showed in the examine text and on the button. The button stays, the text no longer repeats it.
- [x] MP5, Remington, Benelli, MP9, Vector and Thompson are two-handed like their upstream class (`BaseGunWieldable`).
- [x] Remington long pump sound restored (`/Audio/_White/Guns/shotgun_rack.ogg`).
- [x] "ERROR: YOU ARE DEAD" gets two blank lines above it.
- [x] Metal and other material doors were airtight and kept a pocket of vacuum. Airtight removed from `BaseMaterialDoor`.
- [x] Bone spear is a craft again: wooden shaft plus 2 bones, no web silk.
- [x] Combat logs: orange to red for hits, bleeding lines small and red.
- [x] "Say last words" and "succumb" now kill (they ran the disabled ghost command).
- [ ] Mob loot feels rarer than the old build. Measured: a fresh pool gives the 70% rate, tables are identical to the old fork, and the floor spawners take 30-40% of the pool at map load. No cause found yet; needs numbers from play (kills and drops).
- [ ] Crafts that do not fit (AI core, clown suit and more): list presented to the owner, waiting for the decision.
- ("Cannot keep up" is a local-hosting artifact, ignore.)
