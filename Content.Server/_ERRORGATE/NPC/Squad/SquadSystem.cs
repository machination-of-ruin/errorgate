using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Server._ERRORGATE.SmartMobSpawner;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Player;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.NPC.Squad;

/// <summary>
///     Runs the squads (docs/design/squads.md): who belongs to which, who leads, the formation, the goal of the leader from the task, standing at
///     a reached goal, picking up things lying about and talking. Fighting is not here: the HTN of each member (see
///     <c>ErrorgateHumanoidHostileCompound</c>) fights when it senses an enemy, and the squad goes on when it is over.
/// </summary>
public sealed class SquadSystem : EntitySystem
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(0.5);

    private const float LegLength = 14f;
    // The squad keeps together: the leader waits when a member is further than StragglerRange (5 to 10 tiles is the aim)
    private const float StragglerRange = 9f;
    private const float FollowRange = 7f;
    private const float CatchUpRange = 8f;
    private static readonly TimeSpan StuckTime = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(15);
    private const float TrailStep = 3f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly NpcSpeechSystem _speech = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly PathfindingSystem _pathfinding = default!;
    [Dependency] public readonly IRobustRandom Random = default!;

    /// <summary>Points inside the buildings of a map, found once in a while: one list per map.</summary>
    private readonly Dictionary<MapId, (TimeSpan At, List<(EntityUid Grid, EntityCoordinates Coordinates)> Points)> _buildings = new();

    private readonly Dictionary<MapId, EntityUid> _ground = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SquadSlotComponent, SmartMobSpawnedEvent>(OnSpawned);
    }

    // -- Creating squads ---------------------------------------------------------------------------------------------

    /// <summary>A new squad on a map (the entity stands on the map, not on a grid). Members come in with <see cref="AddMember"/> or a <see cref="SquadSlotComponent"/>.</summary>
    public Entity<SquadComponent> CreateSquad(EntityCoordinates onMap, SquadTask task)
    {
        var uid = Spawn(null, onMap);
        var squad = EnsureComp<SquadComponent>(uid);
        squad.Task = task;
        squad.NextChatter = _timing.CurTime + TimeSpan.FromSeconds(Random.Next(10, 25));
        return (uid, squad);
    }

    public void AddMember(Entity<SquadComponent> squad, EntityUid member, int slot)
    {
        var comp = EnsureComp<NpcSquadMemberComponent>(member);
        comp.Squad = squad;
        comp.Slot = slot;
        if (!squad.Comp.Members.Contains(member))
            squad.Comp.Members.Add(member);
    }

    public void SetTask(Entity<SquadComponent> squad, SquadTask task)
    {
        squad.Comp.Task = task;
        squad.Comp.Goal = null;
        squad.Comp.Holding = false;
        squad.Comp.Leg = null;
        squad.Comp.LegTries = 0;
    }

    private void OnSpawned(Entity<SquadSlotComponent> spawner, ref SmartMobSpawnedEvent args)
    {
        if (!TryComp<SquadComponent>(spawner.Comp.Squad, out var squad))
            return;

        AddMember((spawner.Comp.Squad, squad), args.Mob, spawner.Comp.Slot);
    }

    // -- Helpers for the tasks ---------------------------------------------------------------------------------------

    public SquadComponent? GetSquad(EntityUid uid) => TryComp<SquadComponent>(uid, out var squad) ? squad : null;

    public EntityCoordinates? CoordinatesOf(EntityUid uid) => Exists(uid) && !TerminatingOrDeleted(uid) ? Transform(uid).Coordinates : null;

    public MapId MapOf(Entity<SquadComponent> squad)
    {
        var anchor = squad.Comp.Leader ?? squad.Owner;
        return Transform(anchor).MapID;
    }

    /// <summary>The nearest player on the squad's map that is alive.</summary>
    public EntityUid? NearestPlayer(Entity<SquadComponent> squad)
    {
        var from = _transform.GetMapCoordinates(squad.Comp.Leader ?? squad.Owner);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var state, out var xform))
        {
            if (state.CurrentState != MobState.Alive || xform.MapID != from.MapId)
                continue;

            var distance = (_transform.GetWorldPosition(xform) - from.Position).LengthSquared();
            if (distance < bestDistance)
            {
                best = uid;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The grid with the most area on the map: the open ground.</summary>
    private EntityUid? GroundOf(MapId map)
    {
        if (_ground.TryGetValue(map, out var cached) && Exists(cached))
            return cached;

        EntityUid? best = null;
        var bestArea = 0f;
        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID != map)
                continue;

            var area = grid.LocalAABB.Width * grid.LocalAABB.Height;
            if (area > bestArea)
            {
                best = uid;
                bestArea = area;
            }
        }

        if (best != null)
            _ground[map] = best.Value;

        return best;
    }

    /// <summary>A free floor tile (no wall, no machine) of the grid at the coordinates, between <paramref name="min"/> and <paramref name="max"/> tiles from them.</summary>
    public EntityCoordinates? RandomFreeTileAround(EntityCoordinates around, float min, float max)
    {
        if (_transform.GetGrid(around) is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var centre = _transform.ToMapCoordinates(around).Position;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var angle = Random.NextAngle();
            var distance = min + (max - min) * Random.NextFloat();
            var world = centre + angle.ToVec() * distance;
            var tile = _map.WorldToTile(gridUid, grid, world);
            if (_map.GetTileRef(gridUid, grid, tile).Tile.IsEmpty)
                continue;

            if (_map.GetAnchoredEntities(gridUid, grid, tile).Any())
                continue;

            return _map.GridTileToLocal(gridUid, grid, tile);
        }

        return null;
    }

    /// <summary>A free spot on the open ground of the squad's map, <paramref name="min"/> to <paramref name="max"/> tiles from the leader.</summary>
    public EntityCoordinates? RandomOpenGround(Entity<SquadComponent> squad, float min, float max)
    {
        var map = MapOf(squad);
        if (GroundOf(map) is not { } ground)
            return null;

        var from = _transform.GetMapCoordinates(squad.Comp.Leader ?? squad.Owner);
        return RandomFreeTileAround(new EntityCoordinates(ground, Vector2.Transform(from.Position, _transform.GetInvWorldMatrix(ground))), min, max);
    }

    /// <summary>A few free spots inside every building (every grid on the map but the ground), found again every few minutes.</summary>
    public List<(EntityUid Grid, EntityCoordinates Coordinates)> GetBuildingPoints(MapId map)
    {
        if (_buildings.TryGetValue(map, out var cached) && _timing.CurTime - cached.At < TimeSpan.FromMinutes(3))
            return cached.Points;

        var ground = GroundOf(map);
        var points = new List<(EntityUid, EntityCoordinates)>();
        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID != map || uid == ground)
                continue;

            var centre = new EntityCoordinates(uid, grid.LocalAABB.Center);
            for (var i = 0; i < 3; i++)
            {
                if (RandomFreeTileAround(centre, 0f, Math.Max(1f, Math.Min(grid.LocalAABB.Width, grid.LocalAABB.Height) / 2f)) is { } point)
                    points.Add((uid, point));
            }
        }

        _buildings[map] = (_timing.CurTime, points);
        return points;
    }

    // -- The loop ----------------------------------------------------------------------------------------------------

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SquadComponent>();
        while (query.MoveNext(out var uid, out var squad))
        {
            if (squad.NextThink > now)
                continue;

            squad.NextThink = now + Tick;
            Think((uid, squad), now);
        }
    }

    private bool IsUp(EntityUid member) =>
        Exists(member) && !TerminatingOrDeleted(member) && TryComp<MobStateComponent>(member, out var state) && state.CurrentState == MobState.Alive;

    /// <summary>True if the member has an enemy it is going after.</summary>
    private bool IsFighting(EntityUid member)
    {
        return TryComp<HTNComponent>(member, out var htn)
            && htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager)
            && Exists(target)
            && TryComp<MobStateComponent>(target, out var state)
            && state.CurrentState == MobState.Alive;
    }

    private void Think(Entity<SquadComponent> squad, TimeSpan now)
    {
        var comp = squad.Comp;

        // The ones who are gone
        for (var i = comp.Members.Count - 1; i >= 0; i--)
        {
            var member = comp.Members[i];
            if (IsUp(member))
                continue;

            comp.Members.RemoveAt(i);
            if (Exists(member) && TryComp<NpcSquadMemberComponent>(member, out var gone))
                gone.MovePoint = null;

            if (comp.Members.Count > 0)
                _speech.TrySay(Random.Pick(comp.Members), "mandown", 0.7f);
        }

        if (comp.Members.Count == 0)
        {
            comp.Leader = null;
            comp.Goal = null;
            comp.Holding = false;
            return;
        }

        comp.Members.Sort((a, b) => Slot(a).CompareTo(Slot(b)));
        comp.Leader = comp.Members[0];
        var leader = comp.Leader.Value;

        if (comp.Task.IsDone(this, squad))
            SetTask(squad, new PatrolTask());

        // A fight is the members' own business; the squad takes up again when it is over
        var fighting = comp.Members.Any(IsFighting);
        if (fighting != comp.InCombat)
        {
            comp.InCombat = fighting;
            comp.GoalSince = now;
            _speech.TrySay(fighting ? Random.Pick(comp.Members) : leader, fighting ? "contact" : "clear", fighting ? 0.8f : 0.6f);
        }

        if (fighting)
            return;

        var leaderPos = _transform.GetMapCoordinates(leader);

        // The goal
        if (comp.Task.Continuous)
        {
            comp.Goal = comp.Task.NextGoal(this, squad);
            comp.Holding = comp.Goal == null || Distance(leaderPos, comp.Goal.Value) < 4f;
        }
        else if (comp.Goal == null)
        {
            comp.Goal = comp.Task.NextGoal(this, squad);
            comp.GoalSince = now;
            comp.GoalIsInside = comp.Task.GoalIsInside;
            comp.Holding = false;
            if (comp.Goal != null && comp.GoalIsInside)
                _speech.TrySay(leader, "arrive", 0.6f);
        }
        else if (!comp.Holding)
        {
            if (Distance(leaderPos, comp.Goal.Value) < 2.5f)
            {
                comp.Holding = true;
                comp.HoldUntil = now + comp.Task.HoldTime(Random);
            }
            else if (now - comp.GoalSince > TimeSpan.FromSeconds(120))
            {
                // Could not get there: another goal
                comp.Goal = null;
            }
        }
        else if (now >= comp.HoldUntil)
        {
            comp.Goal = null;
            comp.Holding = false;
        }

        if (comp.Goal is { } goal && !comp.Holding && Distance(leaderPos, goal) > 1f)
        {
            var heading = _transform.ToMapCoordinates(goal).Position - leaderPos.Position;
            if (heading.LengthSquared() > 1f)
                comp.Heading = Vector2.Normalize(heading);
        }

        CheckStuck(comp, leaderPos, now);
        UpdateTrail(comp, leaderPos);
        Formation(squad, leaderPos, now);
        WakeIdlers(comp);
        Pace(comp, leaderPos);

        if (comp.Holding && comp.Goal != null)
            Idle(squad, now);
    }

    /// <summary>
    ///     A member that is in the middle of the idle plan (wandering, waiting several seconds) does not see its new place: the plan is
    ///     dropped so that it takes up the squad's one.
    /// </summary>
    private void WakeIdlers(SquadComponent comp)
    {
        foreach (var uid in comp.Members)
        {
            if (!TryComp<NpcSquadMemberComponent>(uid, out var member) || member.MovePoint is not { } point
                || !TryComp<HTNComponent>(uid, out var htn) || htn.Plan is not { } plan)
                continue;

            var first = plan.Tasks[0].Operator;
            if (first is not Content.Server.NPC.HTN.PrimitiveTasks.Operators.PickAccessibleOperator)
                continue;

            if (_transform.GetMapCoordinates(uid).MapId != _transform.ToMapCoordinates(point).MapId
                || (_transform.GetWorldPosition(uid) - _transform.ToMapCoordinates(point).Position).Length() > 2.5f)
            {
                _htn.ShutdownPlan(htn);
                _htn.Replan(htn);
            }
        }
    }

    /// <summary>A leader that has a leg to walk but does not move (a fence, a corner) gives that leg up; after a few such legs the goal.</summary>
    private void CheckStuck(SquadComponent comp, MapCoordinates leaderPos, TimeSpan now)
    {
        if (comp.Goal == null || comp.Holding || (leaderPos.Position - comp.StuckPos).Length() > 1.5f)
        {
            comp.StuckPos = leaderPos.Position;
            comp.StuckSince = now;
            return;
        }

        // Waiting for the others is not being stuck
        if (comp.WaitSince != null || comp.Leg == null || now - comp.StuckSince < StuckTime)
            return;

        comp.Leg = null;
        comp.LegTries++;
        comp.StuckSince = now;
        if (comp.LegTries >= 4)
        {
            comp.Goal = null;
            comp.LegTries = 0;
        }
    }

    /// <summary>Out of combat the squad walks; a member that has fallen behind the leader runs to catch up.</summary>
    private void Pace(SquadComponent comp, MapCoordinates leaderPos)
    {
        foreach (var uid in comp.Members)
        {
            if (!TryComp<NPCSteeringComponent>(uid, out var steering))
                continue;

            steering.Walk = Distance(leaderPos, Transform(uid).Coordinates) <= CatchUpRange;
        }
    }

    private int Slot(EntityUid member) => TryComp<NpcSquadMemberComponent>(member, out var comp) ? comp.Slot : int.MaxValue;

    private float Distance(MapCoordinates from, EntityCoordinates to)
    {
        var target = _transform.ToMapCoordinates(to);
        return target.MapId != from.MapId ? float.MaxValue : (target.Position - from.Position).Length();
    }

    /// <summary>
    ///     The leader goes to the goal; the others take their places: in a wedge behind the leader when it moves, in a ring around it when it
    ///     stands.
    /// </summary>
    private void Formation(Entity<SquadComponent> squad, MapCoordinates leaderPos, TimeSpan now)
    {
        var comp = squad.Comp;
        var perpendicular = new Vector2(-comp.Heading.Y, comp.Heading.X);
        for (var i = 0; i < comp.Members.Count; i++)
        {
            if (!TryComp<NpcSquadMemberComponent>(comp.Members[i], out var member))
                continue;

            if (i == 0)
            {
                // The leader waits for the ones who fell behind
                var behind = comp.Members.Skip(1).Any(m => Distance(leaderPos, Transform(m).Coordinates) > StragglerRange);
                if (!behind)
                    comp.WaitSince = null;
                else
                    comp.WaitSince ??= now;

                // A member that cannot come (stuck) does not stop the squad for ever
                var waits = behind && now - comp.WaitSince < MaxWait;
                member.MovePoint = (comp.Goal is { } goal && !waits ? UpdateLeg(squad, leaderPos, goal, now) : null) ?? _transform.ToCoordinates(leaderPos);
                continue;
            }

            Vector2 offset;
            if (comp.Holding && comp.GoalIsInside && comp.Goal is { } inside)
            {
                // Sweeping a building: every member checks a spot of its own inside, near the leader
                if (member.SweepAt <= now || member.MovePoint == null)
                {
                    member.SweepAt = now + TimeSpan.FromSeconds(Random.Next(6, 12));
                    member.MovePoint = RandomFreeTileAround(inside, 1.5f, 6f) ?? inside;
                }

                continue;
            }

            if (comp.Holding || comp.Goal == null)
            {
                var angle = Angle.FromDegrees(137.5 * i);
                offset = angle.ToVec() * (1.6f + 0.6f * ((i - 1) / 3));
            }
            else
            {
                var row = (i + 1) / 2;
                var side = i % 2 == 1 ? -1f : 1f;
                offset = -comp.Heading * (1.8f * row) + perpendicular * (side * 1.4f * row);
            }

            var place = new MapCoordinates(leaderPos.Position + offset, leaderPos.MapId);
            var here = _transform.GetMapCoordinates(comp.Members[i]);

            // Far behind: along the way the leader came
            member.MovePoint = here.MapId == place.MapId && (place.Position - here.Position).Length() > FollowRange
                ? OnGround(comp.Members[i], new MapCoordinates(AlongTrail(comp, here.Position), place.MapId))
                : OnGround(comp.Members[i], place);
        }
    }

    /// <summary>
    ///     A free tile at or near the point, relative to a grid: the MoveTo of the HTN needs a grid under its target, and a point that is
    ///     off the tiles (a formation spot over a wall or the void) would be relative to the map and the plan would fail.
    /// </summary>
    private EntityCoordinates OnGround(EntityUid member, MapCoordinates world)
    {
        var coordinates = _transform.ToCoordinates(world);
        if (_transform.GetGrid(coordinates) == null)
        {
            if ((Transform(member).GridUid ?? GroundOf(world.MapId)) is not { } grid)
                return coordinates;

            coordinates = new EntityCoordinates(grid, Vector2.Transform(world.Position, _transform.GetInvWorldMatrix(grid)));
        }

        return RandomFreeTileAround(coordinates, 0f, 1.5f) ?? coordinates;
    }

    private void UpdateTrail(SquadComponent comp, MapCoordinates leaderPos)
    {
        if (comp.TrailMap != leaderPos.MapId)
        {
            comp.Trail.Clear();
            comp.TrailMap = leaderPos.MapId;
        }

        if (comp.Trail.Count == 0 || (comp.Trail[^1] - leaderPos.Position).Length() > TrailStep)
        {
            comp.Trail.Add(leaderPos.Position);
            if (comp.Trail.Count > 80)
                comp.Trail.RemoveAt(0);
        }
    }

    /// <summary>A few points further along the trail than the point of it nearest to <paramref name="from"/>; the leader's own place if the trail is empty.</summary>
    private Vector2 AlongTrail(SquadComponent comp, Vector2 from)
    {
        if (comp.Trail.Count == 0)
            return from;

        var nearest = 0;
        var best = float.MaxValue;
        for (var i = 0; i < comp.Trail.Count; i++)
        {
            var distance = (comp.Trail[i] - from).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        return comp.Trail[Math.Min(nearest + 3, comp.Trail.Count - 1)];
    }

    /// <summary>
    ///     The pathfinder gives up on long paths and on goals behind fences, so the leader goes to a goal in legs: the goal itself when it is
    ///     near, else a free spot <see cref="LegLength"/> tiles towards it, and a leg is walked only after the pathfinder found a way to it.
    ///     Null while there is no leg (the leader stands); after a few legs that could not be reached the goal is given up.
    /// </summary>
    private EntityCoordinates? UpdateLeg(Entity<SquadComponent> squad, MapCoordinates from, EntityCoordinates goal, TimeSpan now)
    {
        var comp = squad.Comp;
        var leader = comp.Leader!.Value;

        if (comp.LegCheck is { IsCompleted: true } check)
        {
            #pragma warning disable RA0004 // the task is completed here
            if (check.IsCompletedSuccessfully && check.Result.Result == PathResult.Path && comp.LegCandidate is { } good)
#pragma warning restore RA0004
            {
                comp.Leg = good;
                comp.LegAt = now;
                comp.LegTries = 0;
            }
            else
            {
                comp.LegTries++;
            }

            comp.LegCheck = null;
            comp.LegCandidate = null;
        }

        if (comp.Leg is { } leg && now - comp.LegAt < TimeSpan.FromSeconds(25) && Distance(from, leg) > 2.5f)
            return leg;

        comp.Leg = null;
        if (comp.LegCheck != null)
            return null;

        if (comp.LegTries >= 4)
        {
            // Nothing reachable towards this goal: another goal
            comp.Goal = null;
            comp.LegTries = 0;
            return null;
        }

        var candidate = PickLeg(from, goal, comp.LegTries);
        comp.LegCandidate = candidate;
        comp.LegCancel?.Cancel();
        comp.LegCancel = new CancellationTokenSource();
        comp.LegCheck = _pathfinding.GetPath(leader, Transform(leader).Coordinates, candidate, 1.5f, comp.LegCancel.Token, _pathfinding.GetFlags(leader));
        return null;
    }

    /// <summary>The goal if it is near, else a free spot towards it, turned a bit away from the line on each new try.</summary>
    private EntityCoordinates PickLeg(MapCoordinates from, EntityCoordinates goal, int tries)
    {
        var target = _transform.ToMapCoordinates(goal);
        var offset = target.Position - from.Position;
        if (target.MapId != from.MapId || offset.Length() <= LegLength + 2f)
            return goal;

        var direction = new Angle(offset).RotateVec(Vector2.UnitX);
        var turn = Angle.FromDegrees(tries == 0 ? 0 : (tries % 2 == 1 ? 1 : -1) * 35 * ((tries + 1) / 2));
        var heading = turn.RotateVec(direction);
        for (var length = LegLength; length >= 4f; length -= 4f)
        {
            var point = _transform.ToCoordinates(new MapCoordinates(from.Position + heading * length, from.MapId));
            if (RandomFreeTileAround(point, 0f, 2.5f) is { } free)
                return free;
        }

        return goal;
    }

    /// <summary>The squad stands at its goal: members look for things to carry, and now and then talk.</summary>
    private void Idle(Entity<SquadComponent> squad, TimeSpan now)
    {
        var comp = squad.Comp;

        foreach (var uid in comp.Members)
        {
            if (!TryComp<NpcSquadMemberComponent>(uid, out var member) || member.LootTarget != null || member.NextLoot > now)
                continue;

            member.NextLoot = now + TimeSpan.FromSeconds(Random.Next(5, 12));
            if (Random.Prob(0.35f))
                member.LootTarget = FindLoot(uid);
        }

        if (comp.Replier is { } replier && comp.ReplyAt <= now)
        {
            _speech.TrySay(replier, "chatter", 1f);
            comp.Replier = null;
        }

        if (comp.NextChatter <= now)
        {
            comp.NextChatter = now + TimeSpan.FromSeconds(Random.Next(14, 32));
            var speaker = Random.Pick(comp.Members);
            if (_speech.TrySay(speaker, "chatter", 1f) && comp.Members.Count > 1)
            {
                comp.Replier = Random.Pick(comp.Members.Where(m => m != speaker).ToList());
                comp.ReplyAt = now + TimeSpan.FromSeconds(Random.Next(2, 5));
            }
        }
    }

    private EntityUid? FindLoot(EntityUid member)
    {
        var coords = _transform.GetMapCoordinates(member);
        var candidates = new List<EntityUid>();
        foreach (var item in _lookup.GetEntitiesInRange<ItemComponent>(coords, 6f))
        {
            if (TerminatingOrDeleted(item) || Transform(item).Anchored || _container.IsEntityOrParentInContainer(item.Owner))
                continue;

            // What lies on the ground: not what someone is wearing, and guns less often
            if (HasComp<GunComponent>(item) && Random.Prob(0.5f))
                continue;

            candidates.Add(item);
        }

        return candidates.Count == 0 ? null : Random.Pick(candidates);
    }
}
