using Content.Server.Light.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.NPC;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>Added to NPCs that carry a flashlight and use it when it is dim (see <see cref="NpcFlashlightSystem"/>).</summary>
[RegisterComponent]
public sealed partial class NpcFlashlightComponent : Component
{
    public TimeSpan Next;
}

/// <summary>
///     At dusk and night an NPC with <see cref="NpcFlashlightComponent"/> takes its flashlight into a free hand and turns it on; by day it
///     turns it off. The lit NPC can be seen by others (see <see cref="NpcSenseSystem"/>), as its light is a light near them.
/// </summary>
public sealed class NpcFlashlightSystem : EntitySystem
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly HandheldLightSystem _handheld = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly NpcSenseSystem _sense = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<NpcFlashlightComponent, ActiveNPCComponent, HandsComponent>();
        while (query.MoveNext(out var uid, out var flashlight, out _, out var hands))
        {
            if (flashlight.Next > now)
                continue;

            flashlight.Next = now + Interval;
            var dim = _sense.IsDim(Transform(uid).MapID);
            var light = FindLight(uid, hands);
            if (light is not { } lamp || !TryComp<HandheldLightComponent>(lamp, out var handheld))
                continue;

            TopUp(lamp);

            // The light stays where it is (the id slot, a hand): worn lights shine as well
            if (dim && !handheld.Activated)
                _handheld.TurnOn(uid, (lamp, handheld));
            else if (!dim && handheld.Activated)
                _handheld.TurnOff((lamp, handheld), false);
        }
    }

    /// <summary>The flashlight of an NPC: in a hand, else in one of its slots (the id slot).</summary>
    private EntityUid? FindLight(EntityUid uid, HandsComponent hands)
    {
        foreach (var item in _hands.EnumerateHeld(uid, hands))
        {
            if (HasComp<HandheldLightComponent>(item))
                return item;
        }

        var slots = _inventory.GetSlotEnumerator(uid);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } item && HasComp<HandheldLightComponent>(item))
                return item;
        }

        return null;
    }

    private void TopUp(EntityUid light)
    {
        if (_powerCell.TryGetBatteryFromSlot(light, out var batteryEnt, out var battery) && battery.CurrentCharge < battery.MaxCharge)
            _battery.SetCharge(batteryEnt.Value, battery.MaxCharge, battery);
    }
}
