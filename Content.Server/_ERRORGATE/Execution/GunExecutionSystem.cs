using Content.Shared.Damage;
using Content.Shared.Execution;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.Execution;

/// <summary>
///     Executing someone with a gun. WWDP's execution system only knows about melee, so a gun execution used to be a
///     pistol whip. Here the gun fires one round point blank and the damage of that round is multiplied.
/// </summary>
public sealed class GunExecutionSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedExecutionSystem _execution = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunComponent, ExecutionDoAfterEvent>(OnExecutionDoAfter);
    }

    private void OnExecutionDoAfter(Entity<GunComponent> gun, ref ExecutionDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target == null)
            return;

        var attacker = args.User;
        var victim = args.Target.Value;
        var weapon = gun.Owner;

        if (!_execution.CanBeExecuted(victim, attacker, weapon))
            return;

        args.Handled = true;
        var multiplier = CompOrNull<ExecutionComponent>(weapon)?.DamageMultiplier ?? 20f;

        // Check if any system wants to block the shot.
        var prevention = new ShotAttemptedEvent { User = attacker, Used = gun };
        RaiseLocalEvent(weapon, ref prevention);
        if (prevention.Cancelled)
            return;

        RaiseLocalEvent(attacker, ref prevention);
        if (prevention.Cancelled)
            return;

        var attempt = new AttemptShootEvent(attacker, null);
        RaiseLocalEvent(weapon, ref attempt);
        if (attempt.Cancelled)
        {
            if (attempt.Message != null)
                _popup.PopupEntity(attempt.Message, weapon, attacker);

            return;
        }

        // One round.
        var take = new TakeAmmoEvent(1, new List<(EntityUid? Entity, IShootable Shootable)>(), Transform(attacker).Coordinates, attacker);
        RaiseLocalEvent(weapon, take);

        if (take.Ammo.Count <= 0)
        {
            _audio.PlayPvs(gun.Comp.SoundEmpty, weapon);

            // "Gun not bolted" and the like tell the player what is wrong with the gun.
            if (take.Reason != null)
                _popup.PopupEntity(take.Reason, weapon, attacker, PopupType.Medium);
            else
                Popup("execution-popup-gun-empty", attacker, victim, weapon, Filter.Pvs(weapon), PopupType.Medium);

            return;
        }

        var (ammoUid, shootable) = take.Ammo[0];
        var damage = new DamageSpecifier();

        switch (shootable)
        {
            case CartridgeAmmoComponent cartridge:
                if (cartridge.Spent)
                {
                    _audio.PlayPvs(gun.Comp.SoundEmpty, weapon);
                    Popup("execution-popup-gun-empty", attacker, victim, weapon, Filter.Pvs(weapon), PopupType.Medium);
                    return;
                }

                damage = PrototypeDamage(cartridge.Prototype);

                if (ammoUid is { } cartridgeUid)
                {
                    cartridge.Spent = true;
                    _appearance.SetData(cartridgeUid, AmmoVisuals.Spent, true);
                    Dirty(cartridgeUid, cartridge);
                }

                break;
            case AmmoComponent:
                if (TryComp<ProjectileComponent>(ammoUid, out var shot))
                    damage = shot.Damage * (TryComp<ProjectileSpreadComponent>(ammoUid, out var pellets) ? pellets.Count : 1);

                QueueDel(ammoUid);
                break;
            case HitscanPrototype hitscan:
                if (hitscan.Damage != null)
                    damage = hitscan.Damage;

                break;
        }

        _damageable.TryChangeDamage(victim, damage * multiplier, true, origin: attacker);
        _audio.PlayPvs(gun.Comp.SoundGunshot, weapon);

        if (attacker == victim)
        {
            Popup("execution-popup-gun-self-complete-internal", attacker, victim, weapon, Filter.Entities(attacker), PopupType.LargeCaution);
            Popup("execution-popup-gun-self-complete-external", attacker, victim, weapon, Filter.PvsExcept(attacker), PopupType.LargeCaution);
        }
        else
        {
            Popup("execution-popup-gun-complete-internal", attacker, victim, weapon, Filter.Entities(attacker), PopupType.Medium);
            Popup("execution-popup-gun-complete-external", attacker, victim, weapon, Filter.PvsExcept(attacker), PopupType.LargeCaution);
        }
    }

    /// <summary>
    ///     The damage the projectile of a prototype deals, multiplied by its pellets for shotgun shells.
    /// </summary>
    private DamageSpecifier PrototypeDamage(string prototype)
    {
        var proto = _prototypes.Index<EntityPrototype>(prototype);

        if (proto.TryGetComponent<ProjectileSpreadComponent>(out var spread, _componentFactory)
            && _prototypes.Index(spread.Proto).TryGetComponent<ProjectileComponent>(out var pellet, _componentFactory))
        {
            return pellet.Damage * spread.Count;
        }

        return proto.TryGetComponent<ProjectileComponent>(out var projectile, _componentFactory)
            ? projectile.Damage
            : new DamageSpecifier();
    }

    private void Popup(string message, EntityUid attacker, EntityUid victim, EntityUid weapon, Filter filter, PopupType type)
    {
        _popup.PopupEntity(Loc.GetString(message, ("attacker", attacker), ("victim", victim), ("weapon", weapon)),
            attacker, filter, true, type);
    }
}
