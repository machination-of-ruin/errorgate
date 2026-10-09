using Content.Server.Electrocution;
using Content.Server.Power.Components;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._ERRORGATE.DoorShock;

public sealed class ShockOnHitSystem : EntitySystem
{
    [Dependency] private readonly ElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShockOnHitComponent, AttackedEvent>(OnAttacked);
    }

    private void OnAttacked(EntityUid uid, ShockOnHitComponent comp, AttackedEvent args)
    {
        if (TryComp<ApcPowerReceiverComponent>(uid, out var power) && !power.Powered)
            return;

        // the electrocution plays its own sound and popup, add a hint of why
        if (_electrocution.TryDoElectrocution(args.User, uid, comp.ShockDamage, comp.ShockTime, true, comp.SiemensCoefficient))
            _popup.PopupEntity(Loc.GetString("errorgate-door-shock"), uid, args.User, PopupType.SmallCaution);
    }
}
