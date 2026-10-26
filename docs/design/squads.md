# >>> SQUADS

NPCs in a squad walk, hold and fight together. Code: `Content.Server/_ERRORGATE/NPC/Squad`.

## >>> HOW IT WORKS

- **A squad** is an entity with `SquadComponent` on the map; each member has `NpcSquadMemberComponent` (squad, slot). The leader is the member with the lowest slot; when it dies the next takes over. Dead members are dropped (the squad says "mandown").
- **Spawning:** the world generator gives each member spawner a `SquadSlotComponent`; when `SmartMobSpawner` spawns the mob it raises `SmartMobSpawnedEvent` and the mob joins.
- **Thinking:** `SquadSystem` runs every 0.5 s: picks a goal from the task, walks the leader there in legs of 14 tiles checked with the pathfinder (4 failed tries drop the goal), holds at the goal for a while, and gives every member a `MovePoint` (a wedge while moving, a ring while holding). Members far behind follow the leader's trail; the leader waits for stragglers beyond 22 tiles.
- **Combat:** a member that has a target in its HTN blackboard puts the squad in combat; combat behavior (`ErrorgateHumanoidHostileCompound`) comes before squad movement in the HTN, so fights always win.
- **Idle:** a chance to pick up a loose item (`PickLoot`, `StowLoot`: backpack, else hand) and to talk to a neighbor.

## >>> TASKS

Extend `SquadTask` (`SquadTasks.cs`): `NextGoal`, `IsDone`, `HoldTime`, `GoalIsInside`.

- `PatrolTask`: 55% of goals are building points not yet visited, the rest open ground 25 to 50 tiles away.
- `GuardTask(home, radius)`: stays near a place. Used for the resort boss squad (`task: guard` on the structure squad).
- `FollowTask(entity)`, `HuntTask` (nearest player), `EliminateSquadTask(squad)`.

Admin commands: `squads` lists them, `squadtask <squad or member id> patrol|guard|hunt|follow <id>|eliminate <id>` gives a task.

## >>> DEBUGGING

- `showsquads` (client console, needs the `Debug` admin flag): draws every squad in its own colour (state, task, goal ring, guard radius), each member with the step of its plan and a line to where it is going, the leader as a filled circle, soldiers and bandits without a squad in grey, and the building points patrols pick from as white rings. Code: `SquadDebugSystem` on both sides, `Content.Shared/_ERRORGATE/NPC/SquadDebug.cs`.
- `pathfinder` (the NPC window): the walking nodes and polygons of the pathfinder. `showhtn`: the HTN plan text over each NPC.
- `squads` and `squadtask` (server console): see above.

## >>> MOVEMENT AND NEEDS

- Out of combat squads walk (`walk: true` on the `MoveTo` of `ErrorgateSquadCompound`, `NPCSteeringComponent.Walk`); combat movement still runs.
- Soldiers and bandits open doors and airlocks (`NavInteract`, `NavPry`, `Prying` with `pryPowered`) and climb obstacles.
- Loot goes into the first worn container it fits in; never into the hands.
- Hunger and thirst are held at "okay" (`NpcNeedsSystem`).
- A dropped gun within 2.5 tiles is picked up again before another is drawn (`NpcGunMemoryComponent`).

## >>> OPEN

- Formation quality: members still straggle on long patrols (spread of 13 to 35 tiles seen in tests).
- Loot pickup and the stow step are not confirmed in a running game.
- Server cost of many squads is unmeasured.
