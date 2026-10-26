using System.Numerics;
using System.Threading;
using PathTask = System.Threading.Tasks.Task<Content.Server.NPC.Pathfinding.PathResultEvent>;
using Robust.Shared.Map;

namespace Content.Server._ERRORGATE.NPC.Squad;

/// <summary>
///     A squad: a group of NPCs that keep together, walk in formation behind a leader and follow one <see cref="SquadTask"/>. The squad is an
///     entity of its own on the map and outlives its members: a spawner that makes a new soldier for a dead one puts it back into the same squad.
///     See <see cref="SquadSystem"/> and docs/design/squads.md.
/// </summary>
[RegisterComponent]
public sealed partial class SquadComponent : Component
{
    /// <summary>The living members, in the order of their slots.</summary>
    public List<EntityUid> Members = new();

    public EntityUid? Leader;

    public SquadTask Task = new PatrolTask();

    /// <summary>The way the squad faces when it moves; the formation trails behind it.</summary>
    public Vector2 Heading = Vector2.UnitY;

    /// <summary>Where the leader is going now, null while it has no goal.</summary>
    public EntityCoordinates? Goal;

    public bool GoalIsInside;

    public TimeSpan GoalSince;

    /// <summary>Reached the goal and standing there until <see cref="HoldUntil"/>.</summary>
    public bool Holding;

    public TimeSpan HoldUntil;

    public bool InCombat;

    /// <summary>Where the leader has been, a point every few tiles (the newest last): members far behind walk along it, as it is a way the pathfinder can do.</summary>
    public List<Vector2> Trail = new();

    public MapId TrailMap;

    // The way to the goal in legs the pathfinder can do: the leg being walked, the candidate being checked and the check
    public EntityCoordinates? Leg;

    public TimeSpan LegAt;

    public EntityCoordinates? LegCandidate;

    public PathTask? LegCheck;

    public CancellationTokenSource? LegCancel;

    public int LegTries;

    public TimeSpan NextThink;

    /// <summary>Since when the leader waits for a straggler; null while it does not.</summary>
    public TimeSpan? WaitSince;

    public TimeSpan NextChatter;

    public EntityUid? Replier;

    public TimeSpan ReplyAt;
}

/// <summary>On an NPC that belongs to a squad: its slot in the formation and what the squad has told it to do.</summary>
[RegisterComponent]
public sealed partial class NpcSquadMemberComponent : Component
{
    public EntityUid Squad;

    /// <summary>0 is the leader's slot; the lowest slot alive leads.</summary>
    public int Slot;

    /// <summary>Where this member stands or walks to: its place in the formation, or the goal for the leader.</summary>
    public EntityCoordinates? MovePoint;

    /// <summary>An item on the ground the member is going to pick up and carry.</summary>
    public EntityUid? LootTarget;

    public TimeSpan NextLoot;

    /// <summary>When this member picks a new spot to check while the squad sweeps a building.</summary>
    public TimeSpan SweepAt;
}

/// <summary>On a mob spawner: the mob it makes joins this squad in this slot.</summary>
[RegisterComponent]
public sealed partial class SquadSlotComponent : Component
{
    public EntityUid Squad;

    public int Slot;
}
