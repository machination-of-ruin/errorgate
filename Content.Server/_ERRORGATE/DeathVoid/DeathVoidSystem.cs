using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server._White.MobThresholdSounds;
using Content.Server.Ghost;
using Content.Shared._ERRORGATE.DeathVoid;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.DeathVoid;

/// <summary>
///     A dead player is hard-disconnected from the world: no ghost, no view of the surroundings,
///     no hearing and no in-game chat. Their mind keeps owning the corpse but visits an empty entity on an
///     empty map instead. The player can only wait for a revival or respawn.
/// </summary>
public sealed class DeathVoidSystem : EntitySystem
{
    private static readonly EntProtoId VoidPrototype = "ErrorgateDeathVoid";
    private static readonly EntProtoId RespawnAction = "ActionDeadRespawn";

    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    private EntityUid? _voidMap;
    private MapId _voidMapId;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MindContainerComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<MindBodyDeletedEvent>(OnMindBodyDeleted);
        SubscribeLocalEvent<MindEvictedEvent>(OnMindEvicted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _voidMap = null);

        SubscribeLocalEvent<DeathVoidComponent, DeadRespawnEvent>(OnRespawn);
        SubscribeLocalEvent<DeathVoidComponent, MindUnvisitedMessage>(OnUnvisited);
        SubscribeLocalEvent<DeathVoidComponent, MindRemovedMessage>(OnMindRemoved);
    }

    private void OnMobStateChanged(EntityUid uid, MindContainerComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            SendToVoid(uid);
        else if (args.OldMobState == MobState.Dead)
            ReturnFromVoid(uid);
    }

    /// <summary>
    ///     Ghosting is not allowed. If the body is dead (e.g. gibbed), the mind is moved to the void instead.
    /// </summary>
    private void OnGhostAttempt(GhostAttemptHandleEvent args)
    {
        args.Handled = true;
        args.Result = false;

        if (args.Mind.OwnedEntity is { } body && _mobState.IsDead(body))
            SendToVoid(body);
    }

    /// <summary>
    ///     The body of a player was deleted (chasm, gibbing, admin delete, ...). They end up in the void, not on a ghost.
    ///     The mind has no body left at this point, so the void becomes its entity.
    /// </summary>
    private void OnMindBodyDeleted(ref MindBodyDeletedEvent args)
    {
        var voidEnt = SpawnVoid(args.MindId, args.Mind);
        _mind.TransferTo(args.MindId, voidEnt, mind: args.Mind);
        args.Handled = true;
    }

    /// <summary>
    ///     Another mind takes over a body (mind swap, admin control, ...). The previous player ends up in the void,
    ///     minds without a player are simply detached.
    /// </summary>
    private void OnMindEvicted(ref MindEvictedEvent args)
    {
        args.Handled = true;

        if (!TryComp<MindComponent>(args.MindId, out var mind))
            return;

        if (mind.UserId == null)
        {
            _mind.TransferTo(args.MindId, null, createGhost: false, mind: mind);
            return;
        }

        var voidEnt = SpawnVoid(args.MindId, mind);
        _mind.TransferTo(args.MindId, voidEnt, mind: mind);
    }

    private void SendToVoid(EntityUid body)
    {
        if (!_mind.TryGetMind(body, out var mindId, out var mind) || mind.UserId == null)
            return;

        if (mind.VisitingEntity != null)
            return;

        var voidEnt = SpawnVoid(mindId, mind);
        _mind.Visit(mindId, voidEnt, mind);
        PlayDeathSound(body, mind);
    }

    /// <summary>
    ///     WWDP plays the death sound to the body, but the client only plays sounds meant for the entity the player is
    ///     attached to, and that is the void by now. Play the same sound to the player directly.
    /// </summary>
    private void PlayDeathSound(EntityUid body, MindComponent mind)
    {
        if (mind.Session is { } session && TryComp<MobThresholdSoundsComponent>(body, out var sounds))
            _audio.PlayGlobal(sounds.DeathSounds, session);
    }

    private EntityUid SpawnVoid(EntityUid mindId, MindComponent mind)
    {
        var voidEnt = Spawn(VoidPrototype, new MapCoordinates(Vector2.Zero, EnsureVoidMap()));
        _actions.AddAction(voidEnt, RespawnAction);

        if (mind.Session is { } session)
        {
            var message = Loc.GetString("errorgate-death-void-title");
            _chat.ChatMessageToOne(ChatChannel.Server,
                message,
                $"[font size=40][bold]{message}[/bold][/font]\n[bold]{Loc.GetString("errorgate-death-void-subtitle")}[/bold]",
                EntityUid.Invalid,
                false,
                session.Channel,
                Color.Red);
        }

        return voidEnt;
    }

    private void ReturnFromVoid(EntityUid body)
    {
        if (!_mind.TryGetMind(body, out var mindId, out var mind))
            return;

        if (mind.VisitingEntity is { } visiting && HasComp<DeathVoidComponent>(visiting))
            _mind.UnVisit(mindId, mind);
    }

    private MapId EnsureVoidMap()
    {
        if (_voidMap is not { } map || TerminatingOrDeleted(map))
        {
            map = _map.CreateMap(out _voidMapId);
            _voidMap = map;
        }

        return _voidMapId;
    }

    private void OnRespawn(EntityUid uid, DeathVoidComponent component, DeadRespawnEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(uid, out var actor))
            return;

        args.Handled = true;
        _ticker.Respawn(actor.PlayerSession);
    }

    private void OnUnvisited(EntityUid uid, DeathVoidComponent component, MindUnvisitedMessage args)
    {
        QueueDel(uid);
    }

    private void OnMindRemoved(EntityUid uid, DeathVoidComponent component, MindRemovedMessage args)
    {
        QueueDel(uid);
    }
}
