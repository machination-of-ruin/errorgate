# >>> SQUADS

NPCs in a squad walk, hold and fight together. Code: `Content.Server/_ERRORGATE/NPC/Squad`.

## >>> HOW IT WORKS

- **A squad** is an entity with `SquadComponent` on the map; each member has `NpcSquadMemberComponent` (squad, slot). The leader is the member with the lowest slot; when it dies the next takes over. Dead members are dropped (the squad says "mandown").
- **Spawning:** the world generator gives each member spawner a `SquadSlotComponent`; when `SmartMobSpawner` spawns the mob it raises `SmartMobSpawnedEvent` and the mob joins.
- **Thinking:** `SquadSystem` runs every 0.5 s: picks a goal from the task, walks the leader there in legs of 14 tiles checked with the pathfinder (4 failed tries drop the goal), holds at the goal for a while, and gives every member a `MovePoint` (a wedge while moving, a ring while holding). Members far behind follow the leader's trail; the leader waits for stragglers beyond 9 tiles (at most 15 s, a stuck member does not stop the squad); the aim is a formation of 5 to 10 tiles. A member in the middle of its idle plan is woken (`WakeIdlers`) when the squad gives it a place. At a building goal the members each check a spot of their own inside, within 6 tiles of the leader.
- **Combat:** a member that has a target in its HTN blackboard puts the squad in combat; combat behavior (`ErrorgateHumanoidHostileCompound`) comes before squad movement in the HTN, so fights always win.
- **Idle:** a chance to pick up a loose item (`PickLoot`, `StowLoot`: backpack, else hand) and to talk to a neighbor.

## >>> TASKS

Extend `SquadTask` (`SquadTasks.cs`): `NextGoal`, `IsDone`, `HoldTime`, `GoalIsInside`.

- `PatrolTask`: 55% of goals are building points not yet visited, the rest open ground 25 to 50 tiles away.
- `GuardTask(home, radius)`: stays near a place. Used for the resort boss squad (`task: guard` on the structure squad).
- `InvestigateTask(place)`: a patrol that could hear a shot (the range of the distant gunfire, `GunfireHeardEvent` from `DistantGunfireSystem`; not silenced guns, not its own members, not while fighting, once per 45 s) goes to look (a free tile within 6 of the place, on the grid there or the ground), stays 8 to 16 s, then patrols again; a squad still more than 12 tiles away when it gives up a way tries again, 3 times at most.
- `FollowTask(entity)`, `HuntTask` (nearest player), `EliminateSquadTask(squad)`.

Admin commands: `squads` lists them, `squadtask <squad or member id> patrol|guard|hunt|follow <id>|eliminate <id>` gives a task.

## >>> DEBUGGING

- `showsquads` (client console, needs the `Debug` admin flag): draws every squad in red, labelled with its faction and squad id (state, task, goal ring, guard radius), the leader marked LEADER, each member with the step of its plan and a line to where it is going, the leader as a filled circle, soldiers and bandits without a squad (single spawners) as "no squad", and the building points patrols pick from as white rings. Code: `SquadDebugSystem` on both sides, `Content.Shared/_ERRORGATE/NPC/SquadDebug.cs`.
- `pathfinder` (the NPC window): the walking nodes and polygons of the pathfinder. `showhtn`: the HTN plan text over each NPC.
- `squads` and `squadtask` (server console): see above.

## >>> MOVEMENT AND NEEDS

- Out of combat squads walk, including the idle wander (`ErrorgateIdleCompound`); a member more than 8 tiles from the leader runs to catch up (`SquadSystem.Pace`). A leader that does not move for 8 s drops its leg and after 4 legs its goal (`CheckStuck`). Formation spots are snapped to a free tile on a grid (`OnGround`): a spot off the tiles made the HTN `MoveTo` fail, which left members standing in their idle plan. (`walk: true` on the `MoveTo` of `ErrorgateSquadCompound`, `NPCSteeringComponent.Walk`); combat movement still runs.
- Soldiers and bandits open doors and airlocks (`NavInteract`, `NavPry`, `Prying` with `pryPowered`) (not fences: with `NavClimb` they got stuck on them).
- **Fences:** the pathfinder samples points 0.25 tiles apart and a fence (0.2 wide, in the tile centre) fell between them, so it was invisible and NPCs walked into `FenceWoodHighStraight` and `FenceMetalStraight`. `PathfindingSystem.Grid.cs` (marked `ERRORGATE`) now tests the whole sample cell for thin walls (under 0.5 wide, at least 0.8 long). Throwaway probe on real maps: 12 of 12 wood fences (village) and 12 of 12 metal fences (snow city) were walked straight through before, 0 of 12 after.
- Loot goes into the first worn container it fits in; never into the hands.
- **Guns:** the best gun they carry is drawn (`NpcWeaponRanking`, `Factions/weapon_ranking.yml`, higher is better, `RandomMag` stripped, unlisted 10); out of combat a carried gun that outranks the one in hand is swapped in (`NpcGunSystem.TryUpgrade`). A gun counts as loaded only if the chamber holds a live round: a fresh pistol has an empty chamber and used to click forever, a dry NPC now racks the first round of the new magazine. An empty gun is reloaded even with no enemy in sight.
- **Loot caps:** NPC kits are not counted against the loot caps for now (an earlier try, raising the gun caps of `LootTableGlobal` by what the NPCs carry, was reverted: the model is still open, see the discussion on respawning kits).
- **Fire:** an NPC on fire stops, drops and rolls (`FlammableSystem.Resist`, checked every second in `NpcStandSystem`).
- **Faults:** for now NPCs always keep away from world faults: the steering treats every fault within 12 tiles as an obstacle of its danger radius plus 0.8 (`AvoidHazards`), and squads plan around every fault within 40 tiles of a member (silently beyond 8 tiles; closer they spot it and say so).
- **Ice:** on a slippery tile an NPC that moves across or against its goal pushes against its motion until it slows (`NPCSteeringSystem.BrakeOnIce`); before, it circled its goal for ever.
- Hunger and thirst are held at "okay" (`NpcNeedsSystem`).
- A dropped gun within 2.5 tiles is picked up again before another is drawn (`NpcGunMemoryComponent`).

## >>> FAULTS, STUCK AND LOST

- **World faults** (`anomalies.md`): a member within 8 tiles of a fault has a 50% chance per second to spot it (always within the danger radius plus 1); a member hurt by one learns of it at once. It says a line (`fault` / `faulthurt`, no lines yet: they need approval) and the whole squad knows the fault (`SquadComponent.KnownFaults`, `SquadFaultSystem`). From then on the squad does not choose a leg that crosses the danger zone plus 1.5 tiles (another heading, in the end another goal), keeps formation spots out of it and steps out of it if it stands in one. Only the straight line of a leg is checked: the walk the pathfinder picks between the points may still touch a zone.
- **Stuck:** a member that has a place to be, is not there and has not moved 0.7 tiles gets a new plan after 5 s and again after 12 s. After 18 s, or after 8 s when it is more than 25 tiles from the leader, or after 30 s far from the leader, it is put on a free tile near the leader (the leader: 3 to 5 tiles on), but only if no player is within 22 tiles of either spot (`SquadSystem.Unstick`).
- **Getting up:** `NpcStandSystem` stands lying soldiers and bandits up (when not knocked down) and picks up the dropped gun again (4 tiles) whether in combat or not.

## >>> RESPAWN

Squads respawn per member: every `SmartMobSpawner` of a member keeps its `SquadSlotComponent`; when it spawns a new mob (after the usual respawn time, `3600 / spawnRate` seconds) `SmartMobSpawnedEvent` puts the mob back into its squad and slot. The squad entity stays on the map when all members are dead. Not tested over a full respawn cycle.

## >>> OPEN

- Formation quality: members still straggle on long patrols (spread of 13 to 35 tiles seen in tests).
- Loot pickup and the stow step are not confirmed in a running game.
- Server cost of many squads is unmeasured.
