using Content.Server.Light.EntitySystems;
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

    [Dependency] private readonly HandheldLightSystem _handheld = default!;
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
            var held = FindHeld(uid, hands);
            if (held == null && dim)
                held = TakeOut(uid);

            if (held is not { } light || !TryComp<HandheldLightComponent>(light, out var handheld))
                continue;

            if (dim && !handheld.Activated)
                _handheld.TurnOn(uid, (light, handheld));
            else if (!dim && handheld.Activated)
                _handheld.TurnOff((light, handheld), false);
        }
    }

    private EntityUid? FindHeld(EntityUid uid, HandsComponent hands)
    {
        foreach (var item in _hands.EnumerateHeld(uid, hands))
        {
            if (HasComp<HandheldLightComponent>(item))
                return item;
        }

        return null;
    }

    /// <summary>The flashlight out of the pockets or belt into a free hand.</summary>
    private EntityUid? TakeOut(EntityUid uid)
    {
        var slots = _inventory.GetSlotEnumerator(uid);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } item || !HasComp<HandheldLightComponent>(item))
                continue;

            if (_hands.TryGetEmptyHand(uid, out _) && _inventory.TryUnequip(uid, slot.ID, out _, silent: true, force: true) && _hands.TryPickupAnyHand(uid, item, checkActionBlocker: false, animate: false))
                return item;
        }

        return null;
    }
}
