using System.Linq;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._ERRORGATE.NPC.Squad;

/// <summary>
///     What a squad is doing. A task answers one question, "where should the leader go now?", and the squad system does the rest (formation,
///     fighting when an enemy turns up, standing at the goal, talking, picking things up). New tasks are new subclasses; set one with
///     <see cref="SquadSystem.SetTask"/>, or <c>squadtask</c> in the console.
/// </summary>
public abstract class SquadTask
{
    public abstract string Name { get; }

    /// <summary>
    ///     True if the goal moves (a player, another squad) and is asked again every tick; the squad stands when it is near. False if the
    ///     goal is a place that is walked to, held for <see cref="HoldTime"/>, and then replaced.
    /// </summary>
    public virtual bool Continuous => false;

    /// <summary>The next goal of the squad, or null if there is none now (it is asked again in a few seconds).</summary>
    public abstract EntityCoordinates? NextGoal(SquadSystem system, Entity<SquadComponent> squad);

    /// <summary>How long the squad stays at a goal it has reached.</summary>
    public virtual TimeSpan HoldTime(IRobustRandom random) => TimeSpan.FromSeconds(random.Next(6, 15));

    /// <summary>True when the task is over and the squad goes back to patrolling.</summary>
    public virtual bool IsDone(SquadSystem system, Entity<SquadComponent> squad) => false;

    /// <summary>True if the goal is inside a building (the squad says it is going in).</summary>
    public virtual bool GoalIsInside => false;
}

/// <summary>Walks the map: out in the open, and into the buildings, taking each in turn before it repeats one.</summary>
public sealed class PatrolTask : SquadTask
{
    public override string Name => "patrol";

    private readonly HashSet<EntityUid> _visited = new();

    private bool _inside;

    public override bool GoalIsInside => _inside;

    public override EntityCoordinates? NextGoal(SquadSystem system, Entity<SquadComponent> squad)
    {
        _inside = false;
        var map = system.MapOf(squad);

        if (system.Random.Prob(0.55f))
        {
            var points = system.GetBuildingPoints(map);
            var fresh = points.Where(p => !_visited.Contains(p.Grid)).ToList();
            if (fresh.Count == 0)
            {
                _visited.Clear();
                fresh = points;
            }

            if (fresh.Count > 0)
            {
                var pick = system.Random.Pick(fresh);
                _visited.Add(pick.Grid);
                _inside = true;
                return pick.Coordinates;
            }
        }

        return system.RandomOpenGround(squad, 25f, 50f);
    }

    public override TimeSpan HoldTime(IRobustRandom random) => TimeSpan.FromSeconds(_inside ? random.Next(10, 22) : random.Next(5, 12));
}

/// <summary>Stays around a place (the boss's squad at the resort): picks spots within the radius and stands on them.</summary>
public sealed class GuardTask(EntityCoordinates home, float radius) : SquadTask
{
    public override string Name => "guard";

    public EntityCoordinates Home = home;

    public float Radius = radius;

    public override EntityCoordinates? NextGoal(SquadSystem system, Entity<SquadComponent> squad)
    {
        return system.RandomFreeTileAround(Home, 0f, Radius);
    }

    public override TimeSpan HoldTime(IRobustRandom random) => TimeSpan.FromSeconds(random.Next(8, 20));
}

/// <summary>Walks after an entity and stays near it.</summary>
public sealed class FollowTask(EntityUid target) : SquadTask
{
    public override string Name => "follow";

    public override bool Continuous => true;

    public EntityUid Target = target;

    public override EntityCoordinates? NextGoal(SquadSystem system, Entity<SquadComponent> squad)
    {
        return system.CoordinatesOf(Target);
    }

    public override bool IsDone(SquadSystem system, Entity<SquadComponent> squad) => system.CoordinatesOf(Target) == null;
}

/// <summary>Goes for the nearest living player on the map, and fights what it meets on the way; with no player there it waits.</summary>
public sealed class HuntTask : SquadTask
{
    public override string Name => "hunt";

    public override bool Continuous => true;

    public override EntityCoordinates? NextGoal(SquadSystem system, Entity<SquadComponent> squad)
    {
        return system.NearestPlayer(squad) is { } player ? system.CoordinatesOf(player) : null;
    }
}

/// <summary>Goes for another squad until none of its members is left.</summary>
public sealed class EliminateSquadTask(EntityUid target) : SquadTask
{
    public override string Name => "eliminate";

    public override bool Continuous => true;

    public EntityUid Target = target;

    public override EntityCoordinates? NextGoal(SquadSystem system, Entity<SquadComponent> squad)
    {
        return system.GetSquad(Target) is { Leader: { } leader } ? system.CoordinatesOf(leader) : null;
    }

    public override bool IsDone(SquadSystem system, Entity<SquadComponent> squad)
    {
        return system.GetSquad(Target) is not { Members.Count: > 0 };
    }
}

/// <summary>A shot was heard from afar: goes to look at the place, stays a little, and then patrols again.</summary>
public sealed class InvestigateTask(MapCoordinates place) : SquadTask
{
    public override string Name => "investigate";

    public MapCoordinates Place = place;

    private bool _reached;

    private int _misses;

    private int _retries;

    private bool _done;

    public override EntityCoordinates? NextGoal(SquadSystem system, Entity<SquadComponent> squad)
    {
        // Asked again after the hold at the place, or after the way there was given up: a squad that is still far away tries again
        // (another spot, another way round a fault), three times at most
        if (_reached)
        {
            if (_retries < 3 && system.LeaderDistance(squad, Place) > 12f && system.GoalNear(squad, Place, 6f) is { } again)
            {
                _retries++;
                return again;
            }

            _done = true;
            return null;
        }

        var goal = system.GoalNear(squad, Place, 6f);
        if (goal == null)
        {
            _done = ++_misses >= 5;
            return null;
        }

        _reached = true;
        return goal;
    }

    public override TimeSpan HoldTime(IRobustRandom random) => TimeSpan.FromSeconds(random.Next(8, 16));

    public override bool IsDone(SquadSystem system, Entity<SquadComponent> squad) => _done;
}
