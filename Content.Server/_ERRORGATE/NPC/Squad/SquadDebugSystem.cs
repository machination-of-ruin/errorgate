using System.Linq;
using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Server.NPC.HTN;
using Content.Shared._ERRORGATE.NPC;
using Content.Shared.Administration;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.NPC.Squad;

/// <summary>
///     Feeds the squad debug overlay (<c>showsquads</c> on the client) of the admins who asked for it: every squad with its task, state,
///     goal and members, the soldiers and bandits who belong to none, and the building points patrols pick from.
/// </summary>
public sealed class SquadDebugSystem : EntitySystem
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SquadSystem _squads = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<ICommonSession> _subscribed = new();
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SquadDebugRequestEvent>(OnRequest);
        _players.PlayerStatusChanged += OnStatus;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnStatus;
    }

    private void OnStatus(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _subscribed.Remove(args.Session);
    }

    private void OnRequest(SquadDebugRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_admin.HasAdminFlag(args.SenderSession, AdminFlags.Debug))
            return;

        if (ev.Enable)
            _subscribed.Add(args.SenderSession);
        else
            _subscribed.Remove(args.SenderSession);
    }

    public override void Update(float frameTime)
    {
        if (_subscribed.Count == 0 || _timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        var squads = new List<SquadDebugSquad>();
        var maps = new HashSet<MapId>();
        var inSquad = new HashSet<EntityUid>();

        var query = EntityQueryEnumerator<SquadComponent>();
        while (query.MoveNext(out var uid, out var squad))
        {
            var map = _squads.MapOf((uid, squad));
            maps.Add(map);
            var members = new List<SquadDebugMember>();
            foreach (var member in squad.Members)
            {
                inSquad.Add(member);
                if (Member(member, squad.Leader == member) is { } data)
                    members.Add(data);
            }

            Vector2? home = null;
            var radius = 0f;
            if (squad.Task is GuardTask guard)
            {
                home = _transform.ToMapCoordinates(guard.Home).Position;
                radius = guard.Radius;
            }

            var faction = "-";
            if (squad.Leader is { } lead && TryComp<NpcFactionMemberComponent>(lead, out var factions) && factions.Factions.Count > 0)
                faction = factions.Factions.First().Id;

            squads.Add(new SquadDebugSquad(
                GetNetEntity(uid),
                faction,
                squad.Task.Name,
                squad.InCombat ? "fighting" : squad.Holding ? "holding" : squad.Goal != null ? "moving" : "idle",
                map,
                squad.Goal is { } goal ? _transform.ToMapCoordinates(goal).Position : null,
                home,
                radius,
                members));
        }

        // The ones without a squad
        var loners = new List<SquadDebugMember>();
        var lonerQuery = EntityQueryEnumerator<NpcNeedsComponent, HTNComponent>();
        while (lonerQuery.MoveNext(out var uid, out _, out _))
        {
            if (inSquad.Contains(uid))
                continue;

            maps.Add(Transform(uid).MapID);
            if (Member(uid, false) is { } data)
                loners.Add(data);
        }

        if (loners.Count > 0)
            squads.Add(new SquadDebugSquad(NetEntity.Invalid, "-", "none", "alone", MapId.Nullspace, null, null, 0f, loners));

        var points = new List<(MapId, Vector2)>();
        foreach (var map in maps)
        {
            foreach (var (_, coordinates) in _squads.GetBuildingPoints(map))
                points.Add((map, _transform.ToMapCoordinates(coordinates).Position));
        }

        var filter = Filter.Empty().AddPlayers(_subscribed);
        RaiseNetworkEvent(new SquadDebugEvent(squads, points), filter);
    }

    private SquadDebugMember? Member(EntityUid uid, bool leader)
    {
        if (!Exists(uid) || TerminatingOrDeleted(uid))
            return null;

        var plan = "-";
        if (TryComp<HTNComponent>(uid, out var htn) && htn.Plan != null)
            plan = htn.Plan.CurrentTask.Operator.GetType().Name.Replace("Operator", "");

        Vector2? target = null;
        if (TryComp<NpcSquadMemberComponent>(uid, out var member) && member.MovePoint is { } point)
            target = _transform.ToMapCoordinates(point).Position;

        var dead = TryComp<MobStateComponent>(uid, out var state) && state.CurrentState != MobState.Alive;
        return new SquadDebugMember(GetNetEntity(uid), MetaData(uid).EntityName, _transform.GetWorldPosition(uid), target, plan, leader, dead);
    }
}
