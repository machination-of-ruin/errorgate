using Content.Shared.Mind;
using Content.Shared.Mobs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._ERRORGATE.CritDeathSounds;

/// <summary>
///     A heartbeat that only the player hears while they are in critical condition, and a death sound when they die.
/// </summary>
/// <remarks>
///     The death sound is sent to the player's session directly. By then they have usually been moved into the
///     death void, so a sound tied to the corpse would never reach them.
/// </remarks>
public sealed class CritDeathSoundsSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    private readonly Dictionary<EntityUid, EntityUid> _heartbeats = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CritDeathSoundsComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CritDeathSoundsComponent, PlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<CritDeathSoundsComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMobStateChanged(EntityUid uid, CritDeathSoundsComponent component, MobStateChangedEvent args)
    {
        switch (args.NewMobState)
        {
            case MobState.Critical:
                StartHeartbeat(uid, component);
                break;
            case MobState.Dead:
                StopHeartbeat(uid);
                PlayDeathSound(uid, component);
                break;
            default:
                StopHeartbeat(uid);
                break;
        }
    }

    private void StartHeartbeat(EntityUid uid, CritDeathSoundsComponent component)
    {
        StopHeartbeat(uid);

        if (_audio.PlayEntity(component.HeartSounds, uid, uid, AudioParams.Default.WithLoop(true)) is { } stream)
            _heartbeats[uid] = stream.Entity;
    }

    private void StopHeartbeat(EntityUid uid)
    {
        if (!_heartbeats.Remove(uid, out var stream))
            return;

        _audio.Stop(stream);
    }

    private void PlayDeathSound(EntityUid uid, CritDeathSoundsComponent component)
    {
        if (_mind.TryGetMind(uid, out _, out var mind) && mind.Session is { } session)
            _audio.PlayGlobal(component.DeathSounds, session, AudioParams.Default);
    }

    private void OnDetached(EntityUid uid, CritDeathSoundsComponent component, PlayerDetachedEvent args)
    {
        StopHeartbeat(uid);
    }

    private void OnShutdown(EntityUid uid, CritDeathSoundsComponent component, ComponentShutdown args)
    {
        StopHeartbeat(uid);
    }
}
