# >>> PORT LEDGER <<<

Notes for replaying the old survival fork (`C:\Games\SS14\errorgate-beyond-ruined`, git remote ref `beyond/master`) onto this repo.
**Read this before touching a PR. Update it after every PR.** Its job is to stop us doing the same work twice.

Branch: `port/beyond`. Shared history base: `8068dda469` (Sep 2024). PR list: `git log --first-parent --reverse 8068dda469..beyond/master`.

## Rules (learned the hard way)

1. **Check upstream first.** A large part of the old fork was written by the owner and later upstreamed to WWDP (the `Vaaankas` account, git author `vanx`). Before discussing a PR:
   - search `master` (`git log master -i --grep=<topic>`) and the code (`grep` for the class / prototype ids);
   - look at the "WWDP PRs by the owner" table below;
   - open the PR on GitHub (`WWhiteDreamProject/wwdpublic`) with WebFetch if unsure.
   Mistakes so far: porting `CritDeathSounds` when WWDP already had `MobThresholdSounds` (#915); drafting #20 and #21 as if nothing existed (The Gunnening #285 and right-click gun melee #24 already did it).
2. **Verify claims with code or a test**, not by reading one code path. (A real examine probe disproved "magazine guns show ammo counts".)
3. **Discuss every PR with the owner before applying it.** Present: what it does, what upstream already has, numbered questions, a recommendation.
4. **Never edit RobustToolbox.** Engine-specific changes of the old fork (custom `corrupted-toolbox` v0.96) are dropped.
5. **Prefer the WWDP version** of a feature over the old fork's homebrew version.
6. **Narrow test runs** (`--filter`), never the whole integration suite. Always run the YAML linter after prototype/locale changes.
7. Keep new code in `_ERRORGATE` folders and keep upstream edits small and marked `// ERRORGATE`.
8. Do not touch the owner's uncommitted design docs; stage files by explicit path.

## Standing decisions

| Topic | Decision |
|---|---|
| Species | Humans only. Other species prototypes stay (see `docs/design/humans-only.md`). |
| Interactions | WWDP "Immersive Interactions" (#288, #351), switchable with `errorgate.immersive_interactions`. The old fork's #19/#22/#27/#51/#72 "must be in hand" family is obsolete. |
| Death | No ghosts. Death void (`Content.Server/_ERRORGATE/DeathVoid`). `rise` and `respawn` for players, `forcerespawn` for admins. OOC stays readable for the dead. |
| Observe | Admin only; spectating for non-admins is a later idea. |
| Presets/jobs | One `ERRORGATE` preset (no rules yet, the AI GOD rule goes there). One selectable job `HumanError`; other jobs hidden from setup, prototypes kept. |
| Ammo/HUD info | Gunnening (#285) is the baseline: no fire rate or ammo count on guns. Handheld gun and mech ammo HUD counters that upstream left are kept. |
| Melee/gun bash | Keep upstream behaviour and stats until the melee milestone (#37, #61, #84). |
| Lobby music | Owner chose to ship the 16-track playlist as is. Attributions state honestly that most are commercial. |
| Tips | Off by default; real tips come with #91. |
| LOOC | Off by default; plain OOC stays. |
| WWDP "[Port]" features the old fork reverted (#68) | Keep all as upstream has them. |
| Loot / mobs / despawn / day cycle | Port the **final** versions as one "world systems" milestone, not PR by PR. |

## Ledger

Status: **DONE** = ported (commit), **SKIP** = will not port (reason), **COVERED** = WWDP already has it, **MILESTONE** = port with a group, **TODO** = to discuss.

### Done / decided (#1 to #32)

| PR | Name | Status | Notes |
|---|---|---|---|
| 1 | corrupted-toolbox | SKIP | Engine pointer. |
| 2 | respawn-2 | DONE `765da4a1ee` | Reworked into the death void. |
| 3 | no-russian | DONE `987cf0980d` | Plus `28c85d3a40` for strings that only existed in ru-RU. |
| 4 | character-creation | DONE `affa868b99` | Humans only. |
| 5 | git-cleanup | DONE `65c4227820` | Branding, README, info text, `>>> STYLE <<<` in CLAUDE.md. |
| 6 | BIGTEXT-engineswitch | DONE `fe48d09605` | Only the combat logs; engine parts dropped. Combat logs rewritten and extended. |
| 7, 8 | errorgate-solution | SKIP | Solution rename, repo keeps `SpaceStation14.sln`. |
| 9 | no-combat-mode-popup | COVERED | WWDP already commented the popup out. |
| 10 | roundstart | DONE `38252cc113` | Spawn flavour removed, SSD indicator and OwO hack removed. Psionics part already gone upstream. |
| 11 | maptest | DONE `3b96b43e83` | Dev map `EdgeOfEntropyDev`; atmos part already upstream. |
| 12 | obeserve-button | DONE `98f061a25a` | Admin only. |
| 13 | no-ghost-in-chasm | DONE `98f061a25a` | Hooks in `MindSystem` send deleted/evicted minds to the void; `rise`. |
| 14, 53 | (no PR) | n/a | Numbers do not exist in the old repo. |
| 15 | roles-loadouts | DONE `8ecaea836a` | HumanError job. |
| 16 | test-engine | SKIP | Engine pointer. |
| 17 | game-preset | DONE `8ecaea836a` | ERRORGATE preset. |
| 18 | lobby-decor | DONE `4922de655f` | Music, lobby art. |
| 19 | disable-slot-swap | SKIP | Replaced by WWDP #288/#351, DONE `e637e60742`. |
| 20 | Guns-descriptions | DONE `a9b1d5df75` | Only the item size line; rest is the Gunnening. |
| 21 | Gun-melee | COVERED | Right-click gun melee (WD #24) exists; keep upstream stats and bash cooldown. |
| 22, 27 | backpack-fix, pocket-gun-fix | SKIP | Patches of the old #19. |
| 23 | cvar | DONE `75418641b5` | Tips, LOOC, LOOC bubbles off. Screen shake 0.2 was already upstream. |
| 24 | loot-spawners | First draft of the loot system | none | DONE (world systems batch 1) |
| 25 | gunslotvisual | DONE `75418641b5` | Only the 11 missing worn-gun sprites. Prototype path edits dropped (Windows backslashes). |
| 26 | suitslot-no-suit | DONE `75418641b5` | Human template only. |
| 28, 29 | gun-cycle | DONE `96154c2a36` | Muzzle flash radius on ammo (default 5), slower shotguns. `AutoCycle` already upstream. |
| 30 | combatlogs-idetity | DONE `0b1bcb23dc` | Per-weapon verbs; identity masking was already in the new combat logs. |
| 31 | fix-deathgasp | SKIP | Obsolete (death void text). Deathgasp always plays via `needsCritical: false` (#13). |
| 32 | realdeathgasp | COVERED `65e07a6e60` | WWDP #915 `MobThresholdSounds`. The void plays the death sound to the player directly. |

### Still to do (#33 to #91 and the reverts)

Hints below come from commit messages, file lists and quick greps. **They are leads, not verdicts: verify before presenting.**

| PR | Name | What it is | Upstream overlap / lead | Plan |
|---|---|---|---|---|
| 33, 34, 35 | mobspawner, mob-loot-system(2) | `SmartMobSpawner`, `MobLoot`, `DespawnDeadBody` | none | DONE (world systems batch 1) |
| 36 | blood-evaporate | Blood, insect blood, vomit and copper blood evaporate in puddles, 10 s tick, no sparkle | none in master | DONE (small-PR batch). Owner chose scope (b): **every puddle evaporates**, which also covers the evaporation part of #65. Mops still use `EvaporationReagents` (water) |
| 37 | melee-tweak | RMB disarm instead of heavy attack, no heavy damage examine, thrust wide animation, human punch effect, melee prototype tweaks | Partly overlaps WWDP #264 melee visuals, #623 aiming and hitting. Heavy attack still present in master | SKIP (owner keeps master melee) |
| 38 | character-creation | Remove job/antag/traits/loadout tabs, spawn priority, disclaimer; default to HUMANERROR | WWDP #334 touched departments only | DONE (batch 2): Jobs and Antags tabs hidden, spawn priority hidden, traits and loadouts off through `game.traits_enabled` / `game.loadouts_enabled` (default false), two genders (old genders normalised), `FallbackOverflowJob = HumanError`, new wait-in-lobby text |
| 39 | item-pickup-sounds | Equip rustle for guns and backpacks, gun interact sounds, rifle sound fix | The Gunnening added equip sound collections; WWDP #591 belt gear sounds | COVERED by the Gunnening (`equip.yml` collections, `EmitSoundOnPickup/Drop` on guns) |
| 40 | no-pointing | Pointing disabled (client and server) | none | DONE (small-PR batch) |
| 41 | body-fixes | Gib does not spawn organs | Shitmed gib options | DONE (small-PR batch): default `GibContentsOption.Skip` in `GibBody` and `GibBehavior` (tested, no organs left) |
| 42 | weapons-resize | Smaller guns, bat tweaks, sniper fixes | WWDP #284 Weapons Resize (same author) | COVERED, check leftovers |
| 43 | character-inspect | Character examine rework, only 2 genders | WWDP #269 Better Character Examine | COVERED (system). Gender menu trim is humans-only housekeeping |
| 44 | no-id-card-verb | Health/bleeding/damage text pushed inline into examine, ID card verb removed, radiation inspect | WWDP keeps health examine as a verb (`SharedHealthExaminableSystem`) | DONE with #50 (see below) |
| 45 | embed-fix | Embedded items can be pulled out | Tested: embedded items are reachable under WWDP interactions | SKIP (not needed) |
| 46, 52 | shove-2, mag-inspect-logs | Separate shove (`ShoveAttackEvent`, flip animation `FlipCharacter`, `FlipAnimationEffect`) | WWDP shove (#280 #295 #304 #315 #347 #565 #582 #978) tunes the disarm-as-shove; no `ShoveAttackEvent` in master | SKIP (owner keeps WWDP shove) |
| 47 | mag-inspect-fix | Ballistic fill delay 0.5 to 1 s, magazine examine tweak | Gunnening shows magazine/chamber | DONE: fill delay 0.75 s (owner choice), rest COVERED |
| 48 | gun-calibers | "The gunnening" of the old fork: real-world calibers (9x19, 5.56x45, 7.62x39, .357/.44, lapua, 6mm caseless), ammo and gun roster cut, station maps deleted (about 184 files), loot-entry names by caliber | Master uses fictional calibers (`.35 auto`, `.30 rifle`, `.50`); the roster is intact | DONE (guns milestone) |
| 49 | 357 | The gunnening part 2 (.357/.44 rename, speed loaders) | same | DONE (guns milestone) |
| 50 | no-examine | Damage examine for melee/ranged/thrown pushed inline, magazine examine | WWDP #549 #574 #925 fix damage examine | DONE with #44 (see below) |
| 51 | clothes-innteraction | Patch of old must-be-in-hand | n/a | SKIP |
| 54 | stamina-fix2 | Stamina crit buffer 3 s to 0, recover from crit at 10% | master stamina already has no slowdown and no `OnShoved` | DONE (batch 2): no crit buffer, coming out of crit leaves 10% stamina. Shove stamina part not applicable |
| 55 | announce | No roundstart announcement | none | DONE (small-PR batch): `AnnounceRound` removed |
| 56 | revert-engine | Engine revert | n/a | SKIP |
| 57 | lobby-fixes | Lobby server name ">>> ERRORGATE <<<" | none | DONE (small-PR batch): `ui-lobby-title` locale string |
| 58 | loot-tables | 36 loot spawner and table files, starter kits, smaller satchel, no breaking floor | none | DONE (loot tables, entries and spawners); starter fills not ported |
| 59 | playtest-preparation | Main map `EdgeOfEntropy.yml` (94k lines), no flashlight/gas tank actions, identity "Unknown" grammar, butchering allowed, no dreams | Map not in master | DONE (map and tweaks) |
| 60, 62, 63, 64 | mob-fixes, loot-spawners, branchchchc x2 | Loot/mob/despawn tweaks, `DespawnItem`, map update | none | DONE (world systems batch 1) |
| 61 | better-melee | Combined melee attack, 36 melee prototype tweaks | none | SKIP (owner keeps master melee) |
| 65 | fixes | Executions return (x20 damage), every reagent evaporates, no ghosts (death void covers), no evac shuttle CVars, carps move on water, cold damage examinable, cigarettes in loot | partly covered by #13 | DONE (batch 2): executions (below), cold burn examine, no evacuation CVars. Evaporation was done in the small batch. `baseTurf` edits deferred to world, cigarettes with loot |
| 66 | loot-manager | Loot Manager 1.0 (global loot tables, LootEntries per category) | none | DONE (world systems batch 1) |
| 67 | deaf-component | `DeafComponent` in `_ERRORGATE/Hearing` | WWDP deafness #265 #270 #384 #390 #395 and `HearingSystem` | COVERED |
| 68 | no-dmca-revert | **Nine reverts of WWDP "[Port]" PRs + "no-dmca-fixes"** (hobo, maid, e-sword, betrayal knife, aspects, telescope, lying down, blink, uplink discounts, advanced prying, melee block) | All nine exist in master | **SKIP, decided: leave all nine as in upstream.** The loot-related part of `no-dmca-fixes` (armor entries, global loot table) is handled with the world systems milestone |
| 69 | tweaks | Combined melee attack on LMB, extract cartridge action, silenced guns no muzzle flash, stacking | Extract round is in the Gunnening | DONE (silenced guns); combined melee SKIP |
| 70 | clothes-rebalance | Inventory rework: human template slots moved (pockets removed from the hotbar group, UI positions), "sling" and "light" examine strings, held-bag slowdown, equip delays, storage grids on clothing | Owner has inventory-size commits in master (belts, waist bags, March 2025). Held bag slowdown + delays came with #288 | DONE (owner chose all): no pocket slots, jumpsuit storage 4x2 (starting-gear pocket items go there), ID slot is the shoulder light slot (lights fit it), suit storage is the Sling, held-bag slowdown 0.8 on backpack and satchel, merc backpack grid, outer storage size limit. Equip-while-moving and duffel delay were already in master. Smart-equip pocket hotkeys left alone |
| 71 | Guns-resprite | Drozd resprite to MP5 | none | DONE (MP5 imported as its own gun with mp5.rsi) |
| 72 | fixes-333 | Revolver spin, outer clothes butcherable, hoodie storage, shove back, chug jug crash | #613 butcherable clothes, hoodie storage (`ClothingOuterStorageBase` with WWDP larger grid) already upstream | DONE/COVERED: nothing left to port. Revolver spin skipped by the owner, chug jug skipped |
| 73 | bad-water | Contaminated water reagents/tiles | none | DONE (own reagent and tile) |
| 74 | mobs | Walker mobs, mob loot uses the global manager, mob spawner prototypes | none | DONE (world systems batch 1) |
| 75, 80 | weather, doors | Weather off on concrete, doors and keys, tough airlocks | none | DONE (weather tiles checked, tough airlock variant, factory door) |
| 76 | tweaks | Second map prototype Kuznetsk, mask popup fix, circular saw buff, disable pulling items, invisible tiny fan | WWDP #975 pull attempt cooldown | DONE (batch 2): saws buffed (`Saw`, `SawElectric`, `SawAdvanced`), items cannot be pulled (`Pullable` removed from `BaseItem`), invisible tiny fan prototype `AtmosDeviceFanTinyInvisible`. Mask popup left upstream. Kuznetsk map with world milestone |
| 77 | crafting-cannibalism | Human butchering, bone crafts, campfires despawn, cooking, crafting cull | WWDP #578 Diegetic Crafting, #619 Construction | DONE: 263 build/craft recipes removed from the menus (prototypes and graphs stay), meat not sliceable and rots in 15 min, human meat cooks into human steak, spear damage up, bat starts as a wooden shaft. Bone spear already upstream (needs web silk), campfire craft DONE (`CampfireCraftable`, 10 planks, burns out in 5 min, cooks), skull helmet recipe still open |
| 78 | lootpool | Kuznetsk loot table | none | PARTLY: Kuznetsk loot table ported, its map never existed |
| 79 | lights | `DayCycle` (client and server), traps, starvation, all masks hide identity, wield uses identity | WWDP #917 masks hide identity; #305 booby traps exist | PARTLY DONE: wield identity and masks already upstream; hunger 0.1 with starvation bloodloss, head bandana covers face, dresser holds large items, barotrauma off. Day cycle, traps (and the Grenade tag), stone-door airtight come with world systems |
| 81 | less-ammo | Guns start empty, randomised magazines, more damage | none | DONE (random-mag prototypes only) |
| 82 | sprinting-release | **Movement-based gun accuracy rework**: new `GunComponent.Ergonomics`, spread grows with speed and decays over time (`GunSystem.Update`), crosshair size shows spread, no examine block in combat mode; plus Tired popup and stamina | `Ergonomics` is NOT in master. WWDP #623 (aiming and hitting) and #630 (gunplay) are different tweaks. #889 sprinting is in master | Accuracy rework SKIPPED (owner). Stamina part DONE: sprint drain already in master; added "Too tired!" popup and slowdown at 80%, recovery cooldown 1 s, base walk speed 3.5 |
| 83 | fixes | Butcher delay 8 to 5 s, faster gauze, cloth buff, gaiter ingestion block, sturdier trees, campfire craft, body despawn 5 to 10 min | none | DONE: butcher 5 s, gauze 2 s, cloth healing, tree 100, wood modifiers; gaiter already blocks. Despawn 10 min and campfire craft come with world systems |
| 84 | brutal-melee | Fire axe, melee balance | WWDP #926 Fix Fireaxe | SKIP (WWDP fireaxe fix, owner keeps master melee) |
| 85 | bugfix | Cherry-picks (#935, #933, #815, #889) plus "look far", shotgun fix | #935 #933 #815 #889 are all in master | COVERED. "Look far" DONE: humans get `EyeCursorOffset` (master replaced the old `Telescope`), Space toggles it |
| 86 | tweaks | "Galactic Common" renamed "Common", `SolCommon` removed from humans, power cells and chainsaw tweaks, sprite `unshaded` commented out on many cells, fence/crate tweaks, loot table edits | language system exists upstream | DONE (batch 2): language rename (`TauCetiBasic` is shown as "Common"), humans no longer know `SolCommon`, chainsaw, fences cut faster, power cell and crate glow off, sniper bayonet (`Sharp`). Loot table edits with world systems |
| 87 | upstream | Footprints port, e-sword fix | Footprints are in master (#1867, #1439 ...) | COVERED |
| 88 | upstream-chatstack | Chat stacking | `Add Chatstack (#1422)` in master | COVERED |
| 89 | port-nvg | Night vision overlays | Master has `Content.Shared/Overlays/Switchable/NightVision*` | COVERED, check leftovers |
| 90 | port-k | Religious headgear | In master (`religious.yml`, `headGroup.yml`) | COVERED |
| 91 | rules-tips | Rules and tips text | none | DONE for now: the old fork rules and playtesting tips verbatim as guide entry `ErrorgateRuleset` (default rules). Tips dataset still the station one; rewrite later in `>>> STYLE <<<` |

### Execution rules (decided, part of #65)
Anyone can be executed, not only incapacitated victims. The damage multiplier is 20 (`ExecutionComponent.DamageMultiplier`). WWDP's execution system only knows melee, so a gun with `Execution` used to pistol whip: guns now have their own verb and a server handler that fires one round point blank (`SharedGunExecutionSystem`, server `GunExecutionSystem`, tests in `ExecutionTest`). A gun that is not racked or has no round says why instead of firing.

### Invisible tiny fan
`AtmosDeviceFanTinyInvisible` is for walling off toxic gas areas invisibly. The final `EdgeOfEntropy.yml` has one normal `AtmosDeviceFanTiny` (the dev map has none): swap them when the map is ported.

## Milestones

### Station strip, step 1 - DONE
Deleted all station maps (22 root maps, 7 White maps, Almagest, both CentComm mains) and their gameMap prototypes, including the meteor arena. `DefaultMapPool` and the deathmatch pool now hold `EdgeOfEntropyDev`. `PostMapInitTest.GameMaps` trimmed to Dev, EdgeOfEntropyDev, TestTeg, Lavatest, CentCommHub. Still there on purpose because C# loads them: Shuttles (arrivals, cargo, emergency, pirate radio), Salvage, CentComm hub, Lavaland, Dungeon templates, procedural themes. They go when arrivals, cargo and salvage code is stripped.

### World systems batch 1 - DONE (code and prototypes, not yet placed on a real map)
LootManager (global table, spawners, finite caps with refund), DespawnItem (20 min on grid, on every item via `BaseItem`), DespawnDeadBody (10 min, gibs, on `BaseMob`), MobLoot, SmartMobSpawner plus walker/runner/animal spawners, toxic water (own reagent `ContaminatedWater`, tile `FloorWaterEntityToxic`), spike trap, `AirlockTough`, factory door and card. Ported with fixes: timers replaced by update sweeps, pick can no longer loop forever, round restart resets the caps, `Timer` leftover and `size:` removed, vTier3 typo fixed, shotgun and syringe/hardhat ids remapped. Day cycle: the upstream `LightCycle` and a `LootManager` are on the `EdgeOfEntropyDev` map entity. Tests: `LootManagerTest`, `WorldSystemsTest`. Caught by the spawn-everything test: jumpsuit storage must be Small or jumpsuits no longer fit in toolboxes. Still open: the real `EdgeOfEntropy` map (2.2 MB, needs the 30 missing spawner ids now present), Kuznetsk (old map file never existed), campfire, skull helmet, spawner placement.

### EdgeOfEntropy map - DONE
Imported the old fork map (2.2 MB) as `Resources/Maps/edge_of_entropy/EdgeOfEntropy.yml` with gameMap `EdgeOfEntropy`, now the only entry in `DefaultMapPool` and the deathmatch pool. Changes to the map: `AtmosDeviceFanTiny` swapped for the invisible fan, `LootSpawnervTier3` fixed to `LootSpawnerMedicalTier3`, 21 `SpawnPointHumanError` points added at the latejoin positions, `MapLight` and upstream `LightCycle` on the map entity. The loot manager comes from the grid entity as before. Test: `EdgeOfEntropyLootTest` loads the map and checks the table and spawned loot. Kuznetsk stays unported (the old fork never had its map file).

### #59 playtest tweaks - DONE
No toggle actions for flashlights, lanterns and gas tanks (verbs only), butchering allowed inside containers, no dreams, strangers read as "Unknown man/woman" and keep the proper-noun grammar, CVars off: restricted names, punctuation fixing, name casing, restart ambience, restart votes. Tools, knives, machete and bat fit the Sling with the old worn sprites; ore box holds anything. Not ported: pickaxe wide animation (file moved), campfire craft, skull helmet recipe, starter backpack fills (backpack lighter fill already irrelevant: HumanError spawns with a lighter in the shoulder slot).

### World systems decisions (owner)
Item despawn with loot refund (old fork), corpses gib after 10 min, upstream `LightCycle` on the map (min 0.2, max 1.25), toxic water as its own tile, spike trap only (WWDP grenade trap stays), separate tough-airlock variant. Research notes per system were collected by read-only agents; known old-fork bugs to fix while porting: `TryPickLoot` can loop forever, spawner `type: Timer` leftover, `MobRunner` uses `size:` instead of `scale:`, `LootSpawnervTier3` typo, spike trap `acts:` indentation.

### World systems (decided: port the final version once)
`SmartMobSpawner`, `MobLoot`, `Despawn` (dead bodies + items), `LootManager` (final form with `GlobalLootTable`, `LootEntries`, spawners), `DayCycle` (client `LightCycleSystem`, shared `LightCycleComponent`), contaminated water, doors/keys, traps, weather rules, maps (`EdgeOfEntropy`, Kuznetsk). Source of truth: final code under `Content.*/_ERRORGATE` and `Resources/Prototypes/_ERRORGATE` in `beyond/master`. Discuss one system at a time. Needs `docs/design/survival-loop.md` ("loot is finite per wipe").

### Examine (#44, #50) - DONE
Owner rule: everything is in the examine window, no extra buttons. `ExamineSystemShared.GetInlineDetails` runs the normal examine-verb event and prints what armor, clothing speed, temperature protection, shield blocking, surgery tools and weapon damage would have shown, only in detail range. Health prints inline (unhurt people read as healthy, self-aware text when examining yourself). Removed: contraband, ID card, character flavor text and guidebook buttons. Kept on purpose: the reagent "examine solution" button. Test: `InlineExamineTest`.

### Guns (#48, #49, #81, #82, #69 parts) - DONE
Decided with the owner: port all real-world calibers and the 21 real-world guns, keep every existing gun prototype, apply the old higher bullet damage, random-load magazines only in separate prototypes, skip #82 (upstream spread stays), MP5 is its own gun beside the Drozd.
- Calibers: WWDP ids renamed in place (variants kept) to 9x19, 5.56x45, 7.62x39, 6mm caseless, .338 Lapua, .357 Magnum (revolvers, lever, repeater) and .45 ACP (magazine guns). References across all prototypes rewritten, `Resources/Migrations` extended so maps still load. Missing Big boxes, AP variants and magazines imported from the old fork. Old damage applied only where higher (5.56, 7.62x39, 6mm, 9x19); .357/.45/Lapua keep the WWDP values.
- Guns: `Resources/Prototypes/_ERRORGATE/Entities/Weapons/real_world_guns.yml` (20 prototypes; the DeltaV cyborg M249 was not imported). Random loads: `random_magazines.yml` and `random_mag_guns.yml` (`<Gun>RandomMag`); the normal ones always spawn full. `BallisticAmmoProvider.RandomizeAmmo`.
- #69 silenced: `GunComponent.MuzzleEffectRadius` override, 0 shows no flash (Beretta, AS Val, Cobra).
- Tests: `RealWorldGunsTest`. Loot tables were not touched (they come with the world-systems milestone). Not run: full `PostMapInitTest` (a run used about 24 GB and was stopped, cause not found); `EdgeOfEntropyDev` passes.

### Melee and shove (#37, #46, #52, #61, #69, #72, #84) - DECIDED: keep master
Owner decision: keep master's melee attacks (light and heavy) and WWDP shove. Skip #46, #52, #37 (RMB disarm), #61 and the combined attack of #69. #84 is covered by WWDP #926; any leftover balance numbers can come up later if wanted.

### Examine (#44, #50)
Inline examine text instead of verbs. Check WWDP #549/#574/#919/#925 first.

### Reverts (#68)
Decided: **keep** the nine WWDP "[Port]" features as upstream has them (telescope, lying down, blink, uplink discounts, aspects, advanced prying, energy sword, melee block, hobo and maid). Nothing to do.

## WWDP PRs by the owner already in master (overlap lookup)

Shove: #280 #295 #304 #315 #347 #565 #582 #978. Examine: #269 #549 #574 #919 #925 #954 #972. Guns: #24 (right-click gun melee) #284 #285 #293 #312 #325 #329 #330 #336 #344 #356 #539 #545 #546 #547 #591 #630 #928 #1003. Melee: #264 #273 #286 #622 #623 #926 #960. Deafness: #265 #270 #384 #390 #395. Crafting: #578 #613 #619. Immersive interactions: #288 #351 (reverted upstream by #358; ported here). Character/identity: #334 #917 #956. Movement and crit: #259 #272 #403 #568 #593 #975. Sounds: #915 (death/crit). Misc removals: #596 popup spam, #962 lobby animations, #972 examine chat logging, #992 MRP+++ popup, #920 slop.
