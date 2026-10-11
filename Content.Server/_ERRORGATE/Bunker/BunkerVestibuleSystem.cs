using System.Linq;
using Content.Server.Body.Systems;
using Content.Shared._ERRORGATE.Bunker;
using Content.Shared.Audio;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Bunker;

/// <summary>
///     The timed airlock between the surface and the bunker. The sequence:
///     <list type="number">
///     <item>The key is used on the surface hatch: a siren sounds outside, every door is locked.</item>
///     <item>The siren stops, the surface hatch and the hatch up are open: people pass freely.</item>
///     <item>Someone presses the console inside: the siren sounds inside, every door is locked.</item>
///     <item>The door into the bunker opens, the hatches are shut.</item>
///     <item>The console from inside, or the key outside, starts the cycle again: step 3 leads back to step 2, the key to step 1 and then 2.</item>
///     </list>
///     The door into the bunker always shuts before it is bolted, and crushes whatever stands in the way.
/// </summary>
public sealed class BunkerVestibuleSystem : EntitySystem
{
    private static readonly SoundSpecifier StartSound = new SoundPathSpecifier("/Audio/Machines/airlock_creaking.ogg");
    private static readonly EntProtoId DoorProto = "BunkerVestibuleDoor";
    private static readonly SoundSpecifier DoneSound = new SoundPathSpecifier("/Audio/Machines/boltsup.ogg");

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly SharedDoorSystem _door = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BunkerVestibuleComponent>();
        while (query.MoveNext(out var uid, out var vestibule))
        {
            if (now >= vestibule.Until)
                OnTimerEnd(uid, vestibule);

            KeepDoor(vestibule);
        }
    }

    /// <summary>
    ///     The key was used on the surface hatch. Returns whether the sequence started.
    /// </summary>
    public bool OnKey(EntityUid uid, BunkerVestibuleComponent? vestibule = null)
    {
        if (!Resolve(uid, ref vestibule) || vestibule.State is not (BunkerVestibuleState.Locked or BunkerVestibuleState.InnerOpen))
            return false;

        SetState(uid, vestibule, BunkerVestibuleState.SirenOutside);
        return true;
    }

    /// <summary>
    ///     The console inside was pressed. Returns whether the sequence started.
    /// </summary>
    public bool OnConsole(EntityUid uid, BunkerVestibuleComponent? vestibule = null)
    {
        if (!Resolve(uid, ref vestibule))
            return false;

        switch (vestibule.State)
        {
            // Open to the surface: the siren leads to the bunker
            case BunkerVestibuleState.OuterOpen:
                vestibule.SirenTarget = BunkerVestibuleState.InnerOpen;
                break;

            // The bunker door is open (or everything timed out): the siren leads back to the surface
            case BunkerVestibuleState.InnerOpen:
            case BunkerVestibuleState.Locked:
                vestibule.SirenTarget = BunkerVestibuleState.OuterOpen;
                break;

            default:
                return false;
        }

        SetState(uid, vestibule, BunkerVestibuleState.SirenInside);
        return true;
    }

    private void OnTimerEnd(EntityUid uid, BunkerVestibuleComponent vestibule)
    {
        switch (vestibule.State)
        {
            case BunkerVestibuleState.SirenOutside:
                SetState(uid, vestibule, BunkerVestibuleState.OuterOpen);
                break;

            case BunkerVestibuleState.SirenInside:
                SetState(uid, vestibule, vestibule.SirenTarget);
                break;

            // Nobody pressed the console: the hatches lock again
            case BunkerVestibuleState.OuterOpen:
                SetState(uid, vestibule, BunkerVestibuleState.Locked);
                break;
        }
    }

    private void SetState(EntityUid uid, BunkerVestibuleComponent vestibule, BunkerVestibuleState state)
    {
        var sirenSeconds = TimeSpan.FromSeconds(_cfg.GetCVar(BunkerCVars.CycleSeconds));
        vestibule.State = state;
        vestibule.Until = state switch
        {
            BunkerVestibuleState.SirenOutside or BunkerVestibuleState.SirenInside => _timing.CurTime + sirenSeconds,
            BunkerVestibuleState.OuterOpen => _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(BunkerCVars.OuterOpenSeconds)),
            _ => TimeSpan.MaxValue,
        };

        // Hatches: open to people in the second stage only
        var hatchesOpen = state == BunkerVestibuleState.OuterOpen;
        if (vestibule.Hatch is { } hatch && TryComp<BunkerHatchComponent>(hatch, out var hatchComp))
        {
            hatchComp.Locked = !hatchesOpen;
            _appearance.SetData(hatch, BunkerHatchVisuals.Locked, !hatchesOpen);
        }

        if (vestibule.Ladder is { } ladder && TryComp<BunkerLadderComponent>(ladder, out var ladderComp))
        {
            ladderComp.Locked = !hatchesOpen;
            _appearance.SetData(ladder, BunkerExitVisuals.Locked, !hatchesOpen);
        }

        // Sirens
        SetSiren(vestibule.OutsideSiren, state == BunkerVestibuleState.SirenOutside);
        foreach (var light in vestibule.Lights)
        {
            SetSiren(light, state == BunkerVestibuleState.SirenInside);
        }

        // The door into the bunker: open in the fourth stage, shut and bolted otherwise (see KeepDoor)
        if (state == BunkerVestibuleState.InnerOpen && vestibule.Door is { } door && TryComp<DoorBoltComponent>(door, out var bolt))
        {
            _door.TrySetBoltDown((door, bolt), false);
            _door.TryOpen(door);
        }

        _audio.PlayPvs(state is BunkerVestibuleState.SirenOutside or BunkerVestibuleState.SirenInside ? StartSound : DoneSound, uid);
    }

    /// <summary>
    ///     Keeps the door into the bunker as the state wants it: when it is to be locked it is closed first (crushing anything in the
    ///     way, the door has no safety) and bolted only once it is shut, so it can never be bolted open.
    /// </summary>
    private void KeepDoor(BunkerVestibuleComponent vestibule)
    {
        if (vestibule.Door is not { } door
            || vestibule.State == BunkerVestibuleState.InnerOpen
            || !TryComp<DoorComponent>(door, out var doorComp)
            || !TryComp<DoorBoltComponent>(door, out var bolt))
        {
            return;
        }

        // Whatever went wrong, a door that will not shut for this long is replaced by a new one that is shut
        if (doorComp.State == DoorState.Closed)
        {
            vestibule.DoorOpenSince = null;
        }
        else
        {
            vestibule.DoorOpenSince ??= _timing.CurTime;
            if (_timing.CurTime - vestibule.DoorOpenSince > TimeSpan.FromSeconds(6))
            {
                ReplaceDoor(vestibule, door);
                return;
            }
        }

        // A door crushes what stands in it and then opens again so nobody stays trapped, which would keep it open for good.
        // Here whatever is left in the way (loose things, bodies) is removed so it can shut.
        if (doorComp.State != DoorState.Closed && TryComp<PhysicsComponent>(door, out var physics))
            ClearDoorway(door, physics);

        switch (doorComp.State)
        {
            case DoorState.Open:
                // A bolted open door could not close: unbolt it first if it ever got that way
                if (bolt.BoltsDown)
                    _door.TrySetBoltDown((door, bolt), false);

                // A victim that was removed (gibbed, deleted) while the door was open stays in this list, and the door would
                // open again by itself every time it closes, for good
                doorComp.CurrentlyCrushing.Clear();
                _door.TryClose(door);
                break;

            case DoorState.Closed:
                if (!bolt.BoltsDown)
                    _door.TrySetBoltDown((door, bolt), true);

                break;
        }
    }

    private void ReplaceDoor(BunkerVestibuleComponent vestibule, EntityUid old)
    {
        var coordinates = Transform(old).Coordinates;
        var rotation = Transform(old).LocalRotation;
        Del(old);

        var door = Spawn(DoorProto, coordinates);
        Transform(door).LocalRotation = rotation;
        vestibule.Door = door;
        vestibule.DoorOpenSince = null;
        Log.Warning("The bunker door would not shut and was replaced by a shut one");
    }

    private void ClearDoorway(EntityUid door, PhysicsComponent physics)
    {
        // Loose things lying on the door tile (a dropped card opens the door by touching it, items block it) go too
        foreach (var ent in _lookup.GetEntitiesInRange(Transform(door).Coordinates, 0.7f, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (HasComp<ItemComponent>(ent) && !Transform(ent).Anchored)
                QueueDel(ent);
        }

        foreach (var ent in _door.GetColliding(door, physics).ToList())
        {
            
            // Living people are crushed by the door itself, bolted-down things stay
            if (TryComp<MobStateComponent>(ent, out var mob))
            {
                if (_mobState.IsDead(ent, mob))
                    _body.GibBody(ent);

                continue;
            }

            if (!Transform(ent).Anchored)
                QueueDel(ent);
        }
    }

    private void SetSiren(EntityUid? uid, bool on)
    {
        if (uid is not { } light || TerminatingOrDeleted(light))
            return;

        _lights.SetEnabled(light, on);
        _ambient.SetAmbience(light, on);
    }
}
