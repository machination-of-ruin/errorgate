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

## Round 9 (playtest reports)

- [x] Combat log messages all use the largest size (19).
- [x] Torso vanished after chest hits: my round 8 change made every gibbed part delete itself, including the root torso. The torso and parts that cannot be severed are left alone again (`TorsoSurvivesChestHits`).
- [x] Look far PVS pop-in: the server PVS range now grows with the look-far offset (`LookFarPvsSystem`, `pvsIncrease` 0.4 on humans = range 35).
- [x] Bonfire recipe removed (campfire replaces it).
- [x] Campfire sets players walking over it on fire (step trigger + ignite).
- [x] Campfire is in the planks radial craft menu.
- [x] Campfire did not cook: the heater ignored a fire without power. `EntityHeater` has `requiresPower` again (`CampfireCooksHumanMeat`).
- [x] Bone helmet is made from a human skull only (slice with a knife, then 2 bones). The 4-bones recipe, build menu entry and radial entry are gone.
- [x] ERRORGATE crafts in radial menus: campfire (planks) and bone spear (bones) are there; no other ERRORGATE specific recipes exist.

## Round 10 (playtest reports)

- [x] Species selection: the Russian "Люди" came from the species guidebook page behind the info button. The button is hidden (the guidebook is gone).
- [x] Examining a human reads "a man", "a woman" or "a person" instead of "a human" (`SharedHumanoidAppearanceSystem.OnExamined`).
- [x] (confirmed working in play, damage and vomit) Toxic gas: `AmmoniaMapTest` loads Kuznetsk and Edge of Entropy, puts a human on the tile with the most ammonia and the human gets Poison damage (200 on Kuznetsk after 25 s); `AmmoniaInTheAirPoisonsPlayers` shows even 12 mol in 2500 L poisons. It works in tests, so the live report needs the exact spot: which map, which gas cloud, how long the player stood in it.
- [x] PVS: the owner set `net.pvs_range` to 30 and the pop-in is gone. The `LookFarPvsSystem` is removed, `pvs_range = 30` is in the ERRORGATE and development config presets.
- [x] Barks: humans have no species speech sound any more (`speechSounds: null`), only the bark plays.
- [x] Speech bubble font experiments (Hombre, Cygre) were tried and reverted: bubbles use Bedstead again. The server wraps speech in its own `[font="Default"]` tag, so a font swap has to replace that tag (see git history, commit 5c858e3aec). Fonts with full Cyrillic: Cinzel, Cygre, Hombre, Marauders Map, Boxfont Round, NK57 condensed, Bedstead, LCD14.

## Round 11 (playtest reports)

- [x] "Люди" was the species window's info text (`ServerInfo/Guidebook/Mobs/Human.xml`), now English in the ERRORGATE voice.
- [x] Ammonia did not make anyone vomit: `ChemVomit` cancelled itself on any partial metabolism scale, the old build had that check commented out. `AmmoniaInTheAirPoisonsPlayers` now also checks for vomit and fails without the fix.

## Features batch 1

- [x] Kuznetsk's locked factory doors (`AirlockFactoryEntrance`) shock whoever hits them while powered (5 Shock, `ShockOnHit`, `DoorShockTest`).
- [x] Loading screen line is "RISE ON THE EDGE OF ENTROPY."
- [x] Pill canisters only take pills (they took steaks: no whitelist; `PillCanisterTest` fails without the fix).
- [x] The floodlight is not an item any more: it can be dragged and toggled, not picked up (assumed to be the "spotlight"; `FloodlightBroken` stays an item).
- [x] Pointing is back: the keybind and the right-click "Point at" verb share one path (the old `PointingEnabled = false` guards were removed).
- [x] Combat log and bleeding text size 16 (was 19).
- [x] Dead walkers and runners lie down (`RotationVisuals` 90 on `MobWalker` and its children).
- [x] Glowsticks replace part of the flares in the loot tables (global table Flare 10 -> 5 plus five glowsticks; Kuznetsk Flare 5 -> 3 plus two).

## Features batch 2

- [x] Broken floodlight is a structure too (cannot be picked up).
- [x] Pointing reworked: "Point at" is an interaction under Interactions in the right click menu (`PointAt`, popups like "Look at"), the pointing keybind runs the same interaction on the entity under the cursor, no arrow, no popup text for bare floor.
- [x] Distant gunshots (`DistantGunfireSystem`): shooters are heard beyond normal hearing range (twice net.pvs_range: 50 tiles, 60 with pvs_range 30), on the same grid only, up to a further range tied to the caliber (the numbers below are the extra distance beyond normal hearing, times errorgate.distant_gunfire_range_scale 0.7; the first version used absolute ranges that were below the hearing range, so nothing was ever heard) (9x19 45, .45 50, 5.56 65, 7.62x39 70, .357 60, 6mm 55, 12 gauge 60, .338 100, energy and unknown 25 to 50, silenced and toy none). The original shot sound is played lower in pitch and quieter with distance, with a delayed echo copy. CVars `errorgate.distant_gunfire_enabled` and `errorgate.distant_gunfire_range_scale`. Only the audience rules are tested; how it sounds needs a listen.
- [x] Distant gunfire v3 (after the first listen): normal hearing is the PVS range (sounds are entities, they only reach clients that have them in PVS), not twice it, so the gap to the distant sound is gone. The distant shot is played from a virtual point 14 tiles from the listener in the direction of the shooter, so it has a direction; much quieter (-10 to -32 dB), pitch 0.55 to 0.7, a delayed echo copy, and a dark long reverb (`DistantGunfire` audio preset, needs OpenAL EFX on the client).
- [x] Distant gunfire v4: the real shot is only audible about 15 tiles (the default sound range), so the distant copy now starts at the sound's own `MaxDistance` instead of the PVS range (no gap of silence); the per-gun burst cooldown is gone, every shot of a burst is heard (only a 24 per second per listener safety cap remains).
- [x] Distant gunfire v5: the volume only drops from -10 to -16 dB over the range and then cuts off at the end (so the listener's game volume matters less); the distance is carried by a chain of three echo copies (later, lower, 4 dB quieter each) and a longer, stronger reverb (`DistantGunfire` preset: decay 6 s, gain 0.8, late reverb 0.7). `errorgate.distant_gunfire_range_scale` defaults to 3 (the owner's value for Kuznetsk).
- [x] Distant gunfire v6: the echo copies are gone. The shot itself is drawn out and given bass by lowering the pitch with distance (0.55 near to 0.4 far, a lower pitch plays slower, so the sound is also longer) and the reverb's low frequencies ring longer (`decayLfRatio` 1.8).

## Looking far, round 2

- [x] `net.pvs_range` is 50 in the ERRORGATE and development presets (things loaded in chunks at 30).
- [x] Look-far aim "offset" and the 2 tile view jump on Space: the telescope's neutral point was the middle of the WINDOW, but the player is drawn in the middle of the game view, which sits right of the chat panel (126 px, 2 tiles at 62 px per tile in the debug log). Holding the cursor at the top of the window therefore pointed 2 tiles east of the player, and the view jumped 2 tiles on Space. The offset now measures from the middle of the game view (`EyeCursorOffsetSystem`, `MainViewport`). The shooting maths was exact all along (server log: shot angle equals the cursor direction; `AimAccuracyTest`). The earlier guesses (aim relative to the grid, lighter Simonov recoil) were reverted, no weapon stats changed.

- [x] Simonov stab animation: the old build swung it with `wideAnimationRotation: 270` (LMB heavy swing); in this build LMB is a light stab, which uses `animationRotation` (default 0), so the rifle was drawn unrotated. `animationRotation: 270` added to the Simonov (same value as the old swing) so the bayonet leads the stab.

## Ports

- [x] wwdpublic #984 mob collisions (see the ledger). Retest in play: two players walking into each other, crowds of walkers, dead bodies and lying players, conveyors, pulling.
- [ ] Retest the Wizden ports in play: heat haze (`setatmostemp` on a tile), vacuum greying (remove gas), radio indicator, mobs around doors and walls (pathfinding rework), weather volume.
