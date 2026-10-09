# >>> PLAYTEST TODO <<<

Found while playing the `port/beyond` build. One line each, newest last.

- [x] Loading screen tips (the station tips dataset, `Resources/Locale/en-US/tips.ftl`, `Datasets/tips.yml`) need to be rewritten in the ERRORGATE voice or removed.
- [x] Rules and playtesting tips lose their line breaks (`ServerInfo/Guidebook/ServerRules/ErrorgateRules.xml` is plain text inside `<Document>`, the guidebook parser needs `\n` or blank lines).

## Window, lobby, menus

- [x] OS window title adds "myserver". It must always read ERRORGATE.
- [x] Guidebook goes (button, hotkey, F1 help links).
- [x] Lobby text "Hi and welcome to Space Station 14" becomes "welcome to ERRORGATE".
- [x] Lobby music is vanilla, not ERRORGATE. Only ERRORGATE tracks should play (check the lobby sound collection and the `Audio/Lobby` tracks).
- [x] "Have a happy lesbian day": holiday greetings are on. Disable the server holiday cvar.
- [x] Crew manifest lobby button is not needed, remove it.

## Character setup

- [x] Remove the animal voices (bark voice list: humans' voices only).
- [x] Remove traits (tab and any data).
- [x] Remove the custom fluff / flavor description.
- [x] Remove pronouns: default to the ones that go with the chosen sex.
- [x] Players must not be able to cheat by importing characters with traits, loadouts and so on. Sanitize imported and saved profiles on the server.

## In game

- [x] Not walking by default (check whether the old setting carried over; the cvar default is true and is archived client side).
- [x] Running does not cost stamina.
- [x] Lighter does not light the increased range it had in the old build.
- [x] World lighting on the dev map and EdgeOfEntropy must go, they are underground (no ambient day/night, `MapLight` and `LightCycle` on those maps).
- [x] Rat king has a localized Russian name.
- [x] Some maints floors are missing a texture.
- [x] Combat logs: the bleeding lines are too large ("hits you are fine"). SS14 chat cannot do larger fonts with a proper offset, so limit them to the size the hit lines use. Hit color orange, not yellow. Bleeding red.
- [x] "Say last words" does not kill me. Same with "succumb".

## Found in the server log (not reported by the player)

- [x] `station_record_key_storage` errors on every spawn: the Human Error lighter sits in the id slot and station records treat it as an ID card. Fixed in `StationRecordsSystem` (needs a rebuild and server restart to take effect).
- [x] (ignored, normal) "MainLoop: Cannot keep up" appeared 5 times in the first session. Check what spikes (map load, loot spawners, many DespawnItem items?).
- [x] (it comes from the ghost bar map, which is gone; the plushies are in the WWDP migration as null) Map warning: `PlushieLizard` is an obsolete prototype used by a map (6 times).

## Round 2 (playtest reports)

- [x] Weapon damage showed in the examine text and on the button. The button stays, the text no longer repeats it.
- [x] MP5, Remington, Benelli, MP9, Vector and Thompson are two-handed like their upstream class (`BaseGunWieldable`).
- [x] Remington long pump sound restored (`/Audio/_White/Guns/shotgun_rack.ogg`).
- [x] "ERROR: YOU ARE DEAD" gets two blank lines above it.
- [x] Metal and other material doors were airtight and kept a pocket of vacuum. Airtight removed from `BaseMaterialDoor`.
- [x] Bone spear is a craft again: wooden shaft plus 2 bones, no web silk.
- [x] Combat logs: orange to red for hits, bleeding lines small and red.
- [x] "Say last words" and "succumb" now kill (they ran the disabled ghost command).
- [x] (left as is by the owner) Mob loot feels rarer than the old build. Measured: a fresh pool gives the 70% rate, tables are identical to the old fork, and the floor spawners take 30-40% of the pool at map load. No cause found yet; needs numbers from play (kills and drops).
- [x] Crafts that do not fit: clown, mime and joke recipes plus AI core and all bot recipes removed. Cult, station machinery, furniture and HUD glasses recipes stay for now.
- ("Cannot keep up" is a local-hosting artifact, ignore.)


- Notes on round 2: the bark list lost Meow, Tem and Gav (Undertale-character voices stay). The names, verbs and adjectives datasets are English again (taken from the errorgate repo). Running drains 10 stamina per second until 80%, then you are tired and slowed. Floors FloorTechMaint2, FloorSteelLime (Edge) and FloorBlue (Kuznetsk) were deprecated placeholders and were replaced in the maps. Walk by default is a client setting, an old saved value may override it. Lighter ranges are the old ones (3 and 4). 

- Round 3: EmergencyLight radius is 10 again, battery starts full (3 000 000) and an unpowered lamp lights red at round start even without a station (`EmergencyLightSystem.UpdateState`, test `EmergencyLightTest`). Loading screen tips are the ten playtest tips verbatim from `ErrorgateRules.xml`.

## Round 3 (playtest reports)

- [x] Lobby "Welcome to ..." text red like ERRORGATE.
- [x] Remove the WDpass button and option from the lobby.
- [x] Character setup: remove clown, mime, cyborg and AI nickname fields; remove the backgrounds option.
- [x] Loading tip about combined LMB attacks is still in the rules; replace it.
- [x] Remove the tutorial tab from the rules.
- [x] Lobby text over the join button says "white dream": replace with >>> ERRORGATE <<<.
- [x] Lobby backgrounds are the animated WWDP ones: use the ERRORGATE ones (errorgate-station branch of the old repo).
- [x] In-game chat width starts as narrow as possible.
- [x] Being tired from sprinting locks the player in place; it should slow down / force walking (see the old build).
- [x] "No obvious wounds" examine text green.
- [x] "Holding an item" examine text red.
- [x] Damage text on yourself is self-aware.
- [x] Crowbar (and similar items) fit on the sling.
- [x] Allow putting items into equipped backpacks and duffelbags without having them in hand.
- [x] Simonov inventory grid size: copy from the Mosin.
- [x] Unique sawn-off shotgun fires twice without reload like the double barrel.
- [x] Modern-day double barrel: the old fork has only the IZh-43 (`WeaponShotgunDoubleBarreled`), already ported. No newer model exists; waiting for a name if another gun was meant.
- [x] Loading tips color too bright on the white background; custom font?
- [x] Loading screen "white dream" becomes ERRORGATE.
- [x] Unpowered airlocks open faster by hand (about 1 second).
- [x] Lighters spend fuel much slower, as in the old build.
- [x] "ERROR: YOU ARE DEAD" does not offset from the bottom: new chat messages overlap it when the chat is shrunk and large letters take two lines.
- [x] Runner living 2 should be normal size, runner 1 (school) the smaller model: swap them.
- [x] Unpowered conveyor belts look like they move.
- [x] Port the ammo box textures from the old build.
- [x] Guns in loot tables spawn with randomly filled magazines (new prototypes parented to each gun).
- [x] Server crashed once: kill tracking KeyNotFound when a mob kills a mob (fixed).

## Round 5 (playtest reports)

- [x] Lobby info text: all `[color=blue]` values are red now (`game-ticker.ftl`).
- [x] Item examine: the whole "a <item>" is one muted red (`#b04848`), no inner paleturquoise.
- [x] 7.62 and 5.56 200-round boxes use the big crate art; all ammo box round counts, sizes, names and suffixes match the old fork (Big = 60 rounds, Normal; base boxes 20, sniper 5, shotgun boxes 12).
- [x] Walk/run speeds: base values (walk 3.5, sprint 5) and every mob speed in `_ERRORGATE/MobSpawners` are identical to the old fork.
- [x] Human bodies butcher with a knife: 3 human meat, a skull and 4 bones (old commit 0c1e3c06e5, was marked done by mistake). Skull craft into a bone helmet (slice, 2 bones).
- [x] `locale.culture` default was ru-RU, now en-US (ru-RU plural function registered only when that culture is chosen).
- [x] Old `ERRORGATE/errorgate.toml` server preset ported (`Resources/ConfigPresets/ERRORGATE/errorgate.toml`, no rules_file: the rules are the guidebook page).
- [x] (dropped by the owner) Still not ported from the old fork: starter backpack fills (`StarterGear`), night vision and thermal goggles cherry-pick (check upstream), old `errorgate_rules.txt`.

## Round 6 (playtest reports)

- [x] "RULES" header on the rules page.
- [x] Lobby round-status lines black.
- [x] A gibbed player ends in the death void: gibbing never drops organs, dropped parts lose their organs, a brain that receives a player's mind sends it to the void (`DeathVoidTest.GibbedPlayerGoesToTheVoidNotToABrain`).
- [x] Explosions break floors down to plating only, never to space.
- [x] Loading tips synced with the rules tips (12 tips; added worn bags insert and satchels always accessible).
- [x] Respawn cooldown CVar `errorgate.respawn_cooldown` (seconds, default 0).
- [x] Map cleanup: shuttles, CentComm, salvage, ghost bar, station and other fork maps deleted and their loaders switched off; Lavaland, Ruins and Dungeon kept for the planet plan. Dead map paths in prototypes and component defaults point at `/Maps/Test/empty.yml`.
- [x] Design docs updated (maps, death, survival loop) and committed.

## Round 7 (playtest reports)

- [x] Loading screen "Точка доступа" is now "Server address:" (`connecting-address`).
- [x] Mobs no longer list "It provides the following protection" (armor examine skips entities with a mob state).
- [x] Surgery tool, body part and organ examine list removed (`SurgeryToolExamineSystem` verb off).
- [x] SIG P226 uses the old build sprite (`_ERRORGATE/Objects/Weapons/Guns/Pistols/p226.rsi`, copied from beyond/master `mk58.rsi`).
- [x] No bullet ammo counter on the pistol and revolver bases (SMG, rifle, shotgun, sniper and LMG bases already had none).
- [x] Hand-prying an unpowered door took no time at all: WD "Neglect" made every time under 5 s instant. Now only tools are instant, hands take about a second (`SmallTweaksTest.UnpoweredDoorTakesAboutASecondToPryByHand`).
- [x] Unconscious combat log: "The <weapon> <verb> you in the <part>!" instead of "Someone ... with the <weapon>".
- [x] Minibomb gibbing left hands and feet: gibbing now removes every part of the body, not only the ones that can gib.

## Round 8 (playtest reports)

- [x] Combat log font smaller (11 to 19, was 14 to 30).
- [x] Unseen attackers read "Something" (could be a mob or an item). The separate unseen-weapon line from round 7 is reverted.
- [x] Hands and feet dropped on an explosion gib: explosions no longer sever limbs, a gibbed part always deletes itself, its child parts and its organs (`ExplosionLeavesNoLimbsBehind` uses a real explosion).
- [x] "Your body cools down 15% slower" removed from mobs (temperature protection examine skips entities with a mob state).
- [x] `PosterContrabandBeachStarYamamoto` on Edge of Entropy draws over the secret door (`drawdepth: Overdoors` on the map entity).
- [x] Ammonia gas hurts and makes players vomit again: the old build values for the reagent were never ported (Poison 100 and 60% vomit in the lungs, Poison 5 and 10% vomit in the blood). Test: `BreathingAmmoniaPoisonsTheLungs`. Not checked against the map's own atmosphere.
- [x] Look far (Space) reaches 12 tiles, about a screen ahead (`EyeCursorOffset maxOffset` on humans, was 3).
- [x] Altars no longer say "This altar can be used to sacrifice Psionics" (`SharedSacrificialAltarSystem.OnExamined`, marked `// ERRORGATE`). **Revert this if psionics are ever implemented.**
