using Content.Shared._ERRORGATE.CCVar;
using Content.Shared._ERRORGATE.DistantGunfire;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Enums;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.DistantGunfire;

/// <summary>
///     Gunshots are only played to players in PVS range. This plays a muffled, lower, echoing copy of the shot to
///     players on the same grid (or on the same map when the shooter is outside any grid) who are too far away to hear
///     the real one but still within the range of the caliber. Silenced guns are never heard from afar.
/// </summary>
/// <remarks>

///     per player, like the far sound of explosions, so it is not culled by PVS. Robust's audio effects (reverb
///     presets) are auxiliary slots bound to the audio entity and are not worth the trouble for global sounds,
///     so the muffled feel is pitch, volume and a delayed second copy.
/// </remarks>
public sealed class DistantGunfireSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    /// <summary>Range for cartridges without <see cref="DistantGunfireAudibleComponent"/>.</summary>
    public const float DefaultBallisticRange = 50f;

    /// <summary>Range for hitscan and energy projectiles.</summary>
    public const float DefaultEnergyRange = 25f;

    private const float NearVolume = -10f;
    private const float FarVolume = -32f;
    private const float MinPitch = 0.55f;
    private const float MaxPitch = 0.7f;
    private const float EchoMinDelay = 0.35f;
    private const float EchoMaxDelay = 0.8f;
    private const float EchoVolumeOffset = -8f;
    private const float EchoPitchScale = 0.85f;

    // The sound is played from a point this far from the listener, in the direction of the shot. The real distance is
    // not used: the audio entity has to stay inside the listener's PVS, the volume carries the distance instead.
    private const float VirtualDistance = 14f;
    private const float VirtualMaxDistance = 40f;
    private const string EffectPreset = "DistantGunfire";

    private static readonly TimeSpan RecipientWindow = TimeSpan.FromSeconds(1);
    // Only a safety net against lag, every shot of a burst is heard: no per-gun cooldown.
    private const int RecipientMaxPerWindow = 24;

    // Ranges of cartridges that are about to be spent, the cartridge can be deleted before GunShotEvent is raised.
    private readonly Dictionary<EntityUid, float> _ammoRanges = new();
    private readonly Dictionary<ICommonSession, RecipientState> _recipients = new();
    private readonly List<Echo> _echoes = new();
    private TimeSpan _nextPrune;

    private bool _enabled = true;
    private float _rangeScale = 1f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DistantGunfireAudibleComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);

        Subs.CVar(_cfg, ErrorgateCVars.DistantGunfireEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, ErrorgateCVars.DistantGunfireRangeScale, v => _rangeScale = v, true);
    }

    private void OnAmmoShot(Entity<DistantGunfireAudibleComponent> ent, ref AmmoShotEvent args)
    {
        if (_ammoRanges.Count > 64)
            _ammoRanges.Clear();

        _ammoRanges[ent.Owner] = ent.Comp.Range;
    }

    private void OnGunShot(Entity<GunComponent> ent, ref GunShotEvent args)
    {
        var range = GetRange(ent.Owner, ent.Comp, args.Ammo);
        _ammoRanges.Clear();

        if (range <= 0f || !_enabled)
            return;

        var pvsRange = GetHearingRange();
        if (range <= pvsRange)
            return;

        var sound = ent.Comp.SoundGunshotModified ?? ent.Comp.SoundGunshot;
        if (sound == null)
            return;

        // The real shot is only heard as far as its own sound range (15 tiles unless the sound says otherwise), well inside
        // PVS. The distant copy starts right where the real one fades out, there is no gap of silence.
        var audible = Math.Min(pvsRange, sound.Params.MaxDistance);

        var now = _timing.CurTime;

        var origin = _xform.GetMapCoordinates(ent.Owner);
        var grid = Exists(args.User) ? Transform(args.User).GridUid : Transform(ent.Owner).GridUid;
        var audience = GetAudience(origin, grid, range, args.User, audible);
        if (audience.Count == 0)
            return;

        var resolved = _audio.ResolveSound(sound);
        var baseParams = sound.Params;

        foreach (var (session, distance) in audience)
        {
            if (!TryUseRecipientSlot(session, now))
                continue;

            var t = Math.Clamp((distance - audible) / (range - audible), 0f, 1f);
            var volume = NearVolume + t * (FarVolume - NearVolume);
            var pitch = _random.NextFloat(MinPitch, MaxPitch);

            if (!TryGetVirtualSource(session, origin, out var coords))
                continue;

            var shotParams = baseParams.AddVolume(volume).WithPitchScale(baseParams.Pitch * pitch);
            PlayDistant(resolved, session, coords, shotParams);

            _echoes.Add(new Echo
            {
                At = now + TimeSpan.FromSeconds(EchoMinDelay + t * (EchoMaxDelay - EchoMinDelay)),
                Session = session,
                Sound = resolved,
                Coords = coords,
                Params = baseParams.AddVolume(volume + EchoVolumeOffset)
                    .WithPitchScale(baseParams.Pitch * pitch * EchoPitchScale),
            });
        }
    }

    /// <summary>
    ///     The range in tiles at which a shot of this gun is heard as distant gunfire, 0 if it never is.
    ///     Consumes the cartridge ranges collected for the shot.
    /// </summary>
    public float GetRange(EntityUid gun, GunComponent comp, IReadOnlyList<(EntityUid? Uid, IShootable Shootable)> ammo)
    {
        // Silenced guns have no muzzle flash and no distant report.
        if (comp.MuzzleEffectRadius is <= 0f)
            return 0f;

        var range = 0f;
        foreach (var (uid, shootable) in ammo)
        {
            float found;
            if (uid != null && _ammoRanges.TryGetValue(uid.Value, out var cached))
                found = cached;
            else if (uid != null && TryComp<DistantGunfireAudibleComponent>(uid, out var audible))
                found = audible.Range;
            else if (shootable is CartridgeAmmoComponent)
                found = DefaultBallisticRange;
            else
                found = DefaultEnergyRange;

            range = Math.Max(range, found);
        }

        if (range <= 0f)
            return 0f;

        // The cartridge range is the distance the shot carries on top of the normal hearing range (which is already
        // twice the PVS range), otherwise nothing would ever be heard "beyond PVS".
        var hearing = GetHearingRange();
        if (float.IsInfinity(hearing))
            return 0f;

        return hearing + range * Math.Max(_rangeScale, 0f);
    }

    /// <summary>The distance within which the normal, PVS filtered shot is heard.</summary>
    public float GetHearingRange()
    {
        // The same range Filter.Pvs uses for its default multiplier. Without PVS everyone hears the real shot.
        // Sounds are entities, they only reach clients that have them in PVS: the PVS range, not twice that.
        return _cfg.GetCVar(CVars.NetPVS) ? _cfg.GetCVar(CVars.NetMaxUpdateRange) : float.PositiveInfinity;
    }

    /// <summary>
    ///     Players that should hear the distant copy of a shot fired at <paramref name="origin"/>: attached entities
    ///     on the same map, on <paramref name="grid"/> when it is not null, at least the normal hearing range away
    ///     but closer than <paramref name="range"/>.
    /// </summary>
    public List<(ICommonSession Session, float Distance)> GetAudience(
        MapCoordinates origin,
        EntityUid? grid,
        float range,
        EntityUid? shooter = null,
        float? nearest = null)
    {
        var result = new List<(ICommonSession, float)>();
        var hearing = nearest ?? GetHearingRange();

        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var actor, out var xform))
        {
            if (uid == shooter || xform.MapID != origin.MapId)
                continue;

            if (grid != null && xform.GridUid != grid)
                continue;

            var distance = (_xform.GetWorldPosition(xform) - origin.Position).Length();
            if (distance < hearing || distance >= range)
                continue;

            result.Add((actor.PlayerSession, distance));
        }

        return result;
    }

    /// <summary>
    ///     A point inside the listener's view that lies in the direction of the shot, so the shot has a direction.
    /// </summary>
    private bool TryGetVirtualSource(ICommonSession session, MapCoordinates origin, out EntityCoordinates coords)
    {
        coords = default;
        if (session.AttachedEntity is not { } listener || !Exists(listener))
            return false;

        var listenerPos = _xform.GetMapCoordinates(listener);
        if (listenerPos.MapId != origin.MapId)
            return false;

        var delta = origin.Position - listenerPos.Position;
        if (delta.LengthSquared() < 0.01f)
            return false;

        var point = listenerPos.Position + System.Numerics.Vector2.Normalize(delta) * VirtualDistance;
        var map = _map.GetMapOrInvalid(origin.MapId);
        if (!map.IsValid())
            return false;

        coords = _xform.ToCoordinates(map, new MapCoordinates(point, origin.MapId));
        return true;
    }

    private void PlayDistant(ResolvedSoundSpecifier sound, ICommonSession session, EntityCoordinates coords, AudioParams audioParams)
    {
        // an echo can outlive its map (round restart)
        if (TerminatingOrDeleted(coords.EntityId))
            return;

        // no distance falloff from the virtual point, the volume already carries the real distance
        var played = _audio.PlayStatic(sound, session, coords,
            audioParams.WithMaxDistance(VirtualMaxDistance).WithRolloffFactor(0f));

        if (played is { } audio && _audio.Auxiliaries.ContainsKey(EffectPreset))
            _audio.SetEffect(audio.Entity, audio.Component, EffectPreset);
    }

    private bool TryUseRecipientSlot(ICommonSession session, TimeSpan now)
    {
        var state = _recipients.GetValueOrDefault(session);
        if (now - state.WindowStart >= RecipientWindow)
            state = new RecipientState { WindowStart = now };

        if (state.Count >= RecipientMaxPerWindow)
        {
            _recipients[session] = state;
            return false;
        }

        state.Count++;
        _recipients[session] = state;
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        for (var i = _echoes.Count - 1; i >= 0; i--)
        {
            var echo = _echoes[i];
            if (echo.At > now)
                continue;

            _echoes.RemoveAt(i);
            if (echo.Session.Status == SessionStatus.InGame)
                PlayDistant(echo.Sound, echo.Session, echo.Coords, echo.Params);
        }

        if (now < _nextPrune)
            return;

        _nextPrune = now + TimeSpan.FromSeconds(10);
        var stale = new List<ICommonSession>();
        foreach (var (session, state) in _recipients)
        {
            if (now - state.WindowStart >= RecipientWindow)
                stale.Add(session);
        }

        foreach (var session in stale)
        {
            _recipients.Remove(session);
        }
    }

    private struct RecipientState
    {
        public TimeSpan WindowStart;
        public int Count;
    }

    private struct Echo
    {
        public TimeSpan At;
        public ICommonSession Session;
        public ResolvedSoundSpecifier Sound;
        public EntityCoordinates Coords;
        public AudioParams Params;
    }
}
