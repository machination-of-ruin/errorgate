using System.Linq;
using Content.Shared._ERRORGATE.Bunker;
using Content.Shared.Audio;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Bunker;

/// <summary>
///     The timed airlock. While it cycles both exits are locked, the lights turn and a siren sounds, so nobody can
///     mistake what is going on. When it is done the exit on the far side opens.
/// </summary>
public sealed class BunkerVestibuleSystem : EntitySystem
{
    private static readonly SoundSpecifier StartSound = new SoundPathSpecifier("/Audio/Machines/airlock_creaking.ogg");
    private static readonly SoundSpecifier DoneSound = new SoundPathSpecifier("/Audio/Machines/boltsup.ogg");

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedDoorSystem _door = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BunkerVestibuleDoorComponent, DoorStateChangedEvent>(OnDoorStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BunkerVestibuleComponent>();
        while (query.MoveNext(out var uid, out var vestibule))
        {
            if (vestibule.State != BunkerVestibuleState.Idle && now >= vestibule.Until)
                Finish(uid, vestibule);
        }
    }

    /// <summary>
    ///     Someone came down the hatch. Whoever was about to leave has to wait too.
    /// </summary>
    public void OnArrival(EntityUid uid, BunkerVestibuleComponent? vestibule = null)
    {
        if (!Resolve(uid, ref vestibule))
            return;

        Start(uid, vestibule, BunkerVestibuleState.CyclingIn);
    }

    private void OnDoorStateChanged(Entity<BunkerVestibuleDoorComponent> ent, ref DoorStateChangedEvent args)
    {
        // The door into the bunker has closed behind someone: they are on their way out
        if (args.State != DoorState.Closed
            || !TryComp<BunkerVestibuleComponent>(ent.Comp.Vestibule, out var vestibule)
            || vestibule.State != BunkerVestibuleState.Idle
            || !AnyoneInside(ent.Comp.Vestibule, vestibule))
        {
            return;
        }

        Start(ent.Comp.Vestibule, vestibule, BunkerVestibuleState.CyclingOut);
    }

    private bool AnyoneInside(EntityUid uid, BunkerVestibuleComponent vestibule)
    {
        if (Transform(uid).GridUid is not { } grid)
            return false;

        var found = new HashSet<EntityUid>();
        _lookup.GetLocalEntitiesIntersecting(grid, vestibule.Room, found);
        return found.Any(HasComp<MobStateComponent>);
    }

    private void Start(EntityUid uid, BunkerVestibuleComponent vestibule, BunkerVestibuleState state)
    {
        var restarted = vestibule.State != BunkerVestibuleState.Idle;
        vestibule.State = state;
        vestibule.Until = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(BunkerCVars.CycleSeconds));

        // Both exits shut and locked
        if (vestibule.Door is { } door)
        {
            _door.TryClose(door);
            if (TryComp<DoorBoltComponent>(door, out var bolt))
                _door.TrySetBoltDown((door, bolt), true);
        }

        if (vestibule.Ladder is { } ladder && TryComp<BunkerLadderComponent>(ladder, out var ladderComp))
            ladderComp.Locked = true;

        SetLights(vestibule, true);

        if (!restarted)
            _audio.PlayPvs(StartSound, uid);
    }

    private void Finish(EntityUid uid, BunkerVestibuleComponent vestibule)
    {
        var state = vestibule.State;
        vestibule.State = BunkerVestibuleState.Idle;
        SetLights(vestibule, false);
        _audio.PlayPvs(DoneSound, uid);

        if (state == BunkerVestibuleState.CyclingIn)
        {
            // Arrived: the door into the bunker opens
            if (vestibule.Door is { } door && TryComp<DoorBoltComponent>(door, out var bolt))
            {
                _door.TrySetBoltDown((door, bolt), false);
                _door.TryOpen(door);
            }
        }
        else if (vestibule.Ladder is { } ladder && TryComp<BunkerLadderComponent>(ladder, out var ladderComp))
        {
            // Leaving: the way up is free, the door stays shut
            ladderComp.Locked = false;
            if (vestibule.Door is { } door && TryComp<DoorBoltComponent>(door, out var bolt))
                _door.TrySetBoltDown((door, bolt), false);
        }
    }

    private void SetLights(BunkerVestibuleComponent vestibule, bool on)
    {
        foreach (var light in vestibule.Lights)
        {
            if (TerminatingOrDeleted(light))
                continue;

            _lights.SetEnabled(light, on);
            _ambient.SetAmbience(light, on);
        }
    }
}
