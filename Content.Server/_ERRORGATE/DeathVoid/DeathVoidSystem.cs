using System.Linq;
using System.Numerics;
using Content.Server._ERRORGATE.LifeLog;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server._White.MobThresholdSounds;
using Content.Server.Ghost;
using Content.Server.Mobs;
using Content.Server.Body.Components;
using Content.Shared._ERRORGATE.DeathVoid;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ERRORGATE.DeathVoid;

/// <summary>
///     A dead player is hard-disconnected from the world: no ghost, no view of the surroundings,
///     no hearing and no in-game chat. Their mind keeps owning the corpse but visits an empty entity on an
///     empty map instead. The player can only wait for a revival or respawn.
/// </summary>
public sealed class DeathVoidSystem : EntitySystem
{
    private static readonly EntProtoId VoidPrototype = "ErrorgateDeathVoid";

    // The void each mind was sent to, so a corpse that is gibbed or crushed later does not send it there again
    private readonly Dictionary<EntityUid, EntityUid> _voids = new();
    private static readonly EntProtoId RespawnAction = "ActionDeadRespawn";

    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly LifeLogSystem _lifeLog = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityUid? _voidMap;
    private MapId _voidMapId;

    public override void Initialize()
    {
        base.Initialize();

        // Before the death gasp, so the player is already in the void when the emote goes out and does not see their own
        SubscribeLocalEvent<MindContainerComponent, MobStateChangedEvent>(OnMobStateChanged, before: new[] { typeof(DeathgaspSystem) });
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<MindBodyDeletedEvent>(OnMindBodyDeleted);
        SubscribeLocalEvent<MindEvictedEvent>(OnMindEvicted);
        SubscribeLocalEvent<BrainComponent, MindAddedMessage>(OnBrainMindAdded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _voidMap = null;
            _voids.Clear();
        });

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
        var voidEnt = VoidFor(args.MindId, args.Mind);
        _mind.TransferTo(args.MindId, voidEnt, mind: args.Mind);
        args.Handled = true;
    }

    /// <summary>
    ///     A brain that ends up outside a body (organ dropped, removed by surgery or gibbed) takes the mind with it.
    ///     A player does not live on in a brain: they go to the void instead.
    /// </summary>
    private void OnBrainMindAdded(EntityUid uid, BrainComponent brain, MindAddedMessage args)
    {
        if (args.Mind.Comp.UserId == null)
            return;

        var voidEnt = VoidFor(args.Mind.Owner, args.Mind.Comp);
        _mind.TransferTo(args.Mind.Owner, voidEnt, mind: args.Mind.Comp);
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

        var voidEnt = VoidFor(args.MindId, mind);
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

    /// <summary>
    ///     The void a mind goes to. A mind that is already in one (it died, and now its corpse is gibbed, deleted or
    ///     crushed) keeps that void: no second void and no second death message.
    /// </summary>
    private EntityUid VoidFor(EntityUid mindId, MindComponent mind)
    {
        // Remembered, because the mind does not point at the void any more once a brain or another body took it
        if (_voids.TryGetValue(mindId, out var existing) && !TerminatingOrDeleted(existing))
            return existing;

        return SpawnVoid(mindId, mind);
    }

    private EntityUid SpawnVoid(EntityUid mindId, MindComponent mind)
    {
        var voidEnt = Spawn(VoidPrototype, new MapCoordinates(Vector2.Zero, EnsureVoidMap()));
        _voids[mindId] = voidEnt;
        var cooldown = _cfg.GetCVar(ErrorgateCVars.RespawnCooldown);
        if (cooldown > 0f)
        {
            var voidComp = EnsureComp<DeathVoidComponent>(voidEnt);
            voidComp.RespawnAt = _timing.CurTime + TimeSpan.FromSeconds(cooldown);
            Dirty(voidEnt, voidComp);
        }
        _actions.AddAction(voidEnt, RespawnAction);

        if (mind.Session is { } session)
        {
            // One message: the title, the life log, then the subtitle at the bottom. Large text is broken into short
            // lines by hand: a wrapped large font line overlaps the following messages in a narrow chat panel.
            // Trailing newlines keep later messages offset from it.
            var message = Loc.GetString("errorgate-death-void-title");
            var lines = message.Replace(": ", ":\n");
            var wrapped = $"\n\n[font size=32][bold]{lines}[/bold][/font]\n\n";

            // What happened to this character, as a technical log (ERRORGATE: life log)
            if (_lifeLog.BuildDeathLog(mindId) is { } log)
                wrapped += $"[font size=10][bold]{FormattedMessage.EscapeText(string.Join("\n", log))}[/bold][/font]\n\n";

            wrapped += $"[bold]{Loc.GetString("errorgate-death-void-subtitle")}[/bold]\n\n\n";

            _chat.ChatMessageToOne(ChatChannel.Server,
                message,
                wrapped,
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
        TryRespawn(actor.PlayerSession, out _);
    }

    /// <summary>
    ///     Sends the player back to the lobby unless the respawn cooldown is still running.
    /// </summary>
    /// <param name="remaining">Time left until the player may respawn when this returns false.</param>
    public bool TryRespawn(ICommonSession session, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (session.AttachedEntity is { } attached
            && TryComp<DeathVoidComponent>(attached, out var voidComp)
            && voidComp.RespawnAt is { } at
            && at > _timing.CurTime)
        {
            remaining = at - _timing.CurTime;
            _chat.DispatchServerMessage(session,
                Loc.GetString("errorgate-death-void-wait", ("seconds", (int) Math.Ceiling(remaining.TotalSeconds))));
            return false;
        }

        _ticker.Respawn(session);
        return true;
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
