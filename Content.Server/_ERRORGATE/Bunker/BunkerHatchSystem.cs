using Content.Shared._ERRORGATE.Bunker;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Bunker;

/// <summary>
///     The surface hatch and the ladder inside the vestibule: both move people between the surface map and the bunker map.
///     Neither says anything, there are only sounds.
/// </summary>
public sealed class BunkerHatchSystem : EntitySystem
{
    private static readonly SoundSpecifier OpenSound = new SoundPathSpecifier("/Audio/Machines/airlock_ext_open.ogg");
    private static readonly SoundSpecifier CloseSound = new SoundPathSpecifier("/Audio/Machines/airlock_ext_close.ogg");
    private static readonly SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");
    private static readonly SoundSpecifier ClimbSound = new SoundPathSpecifier("/Audio/Machines/blastdoor.ogg");

    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly BunkerVestibuleSystem _vestibule = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BunkerHatchComponent, ActivateInWorldEvent>(OnHatchActivate);
        SubscribeLocalEvent<BunkerHatchComponent, InteractUsingEvent>(OnHatchInteractUsing);
        SubscribeLocalEvent<BunkerLadderComponent, ActivateInWorldEvent>(OnLadderActivate);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BunkerHatchComponent>();
        while (query.MoveNext(out var uid, out var hatch))
        {
            if (!hatch.Open || now < hatch.CloseAt)
                continue;

            SetOpen(uid, hatch, false);
            _audio.PlayPvs(CloseSound, uid);
        }
    }

    private void OnHatchActivate(Entity<BunkerHatchComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        UseHatch(ent, args.User);
    }

    private void OnHatchInteractUsing(Entity<BunkerHatchComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        UseHatch(ent, args.User);
    }

    private void UseHatch(Entity<BunkerHatchComponent> ent, EntityUid user)
    {
        if (!ent.Comp.Open)
        {
            // The key on the user (in hand, or on them) unlocks it for a while
            if (!_access.IsAllowed(user, ent))
            {
                _audio.PlayPvs(DenySound, ent);
                return;
            }

            ent.Comp.CloseAt = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.OpenSeconds);
            SetOpen(ent, ent.Comp, true);
            _audio.PlayPvs(OpenSound, ent);
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):user} unlocked the bunker hatch");
            return;
        }

        if (ent.Comp.Arrival is not { } arrival)
            return;

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):user} went down the bunker hatch");
        Teleport(user, arrival);

        if (ent.Comp.Vestibule is { } vestibule)
            _vestibule.OnArrival(vestibule);
    }

    private void OnLadderActivate(Entity<BunkerLadderComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;

        if (ent.Comp.Locked || ent.Comp.Surface is not { } surface)
        {
            _audio.PlayPvs(DenySound, ent);
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.User):user} came up the bunker ladder");
        Teleport(args.User, surface);
    }

    private void SetOpen(EntityUid uid, BunkerHatchComponent hatch, bool open)
    {
        hatch.Open = open;
        _appearance.SetData(uid, BunkerHatchVisuals.Open, open);
    }

    /// <summary>
    ///     Moves a person and whatever they pull. The pull is taken up again on the other side.
    /// </summary>
    private void Teleport(EntityUid user, EntityCoordinates to)
    {
        EntityUid? pulled = null;
        if (_pulling.TryGetPulledEntity(user, out var pulling) && TryComp<PullableComponent>(pulling, out var pullable))
        {
            pulled = pulling;
            _pulling.TryStopPull(pulling.Value, pullable, user);
        }

        _audio.PlayPvs(ClimbSound, user);
        _transform.SetCoordinates(user, to);
        _transform.AttachToGridOrMap(user);

        if (pulled is { } other)
        {
            _transform.SetCoordinates(other, to);
            _transform.AttachToGridOrMap(other);
            _pulling.TryStartPull(user, other);
        }

        _audio.PlayPvs(ClimbSound, user);
    }
}
