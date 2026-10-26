using Content.Server.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>The gun an NPC last held: it picks it up again from the ground near it (after a fall, a knock-down) in <see cref="NpcGunSystem.TryDraw"/>.</summary>
[RegisterComponent]
public sealed partial class NpcGunMemoryComponent : Component
{
    public EntityUid Gun;
}

/// <summary>
///     What a soldier or a bandit does with a gun: draws one from its clothes and bag when it has none in hand, and loads a new magazine
///     (or a full cylinder or a new pair of shells) out of nowhere when it is empty. Used by the HTN operators in this folder.
/// </summary>
public sealed class NpcGunSystem : EntitySystem
{
    private const string MagazineSlot = "gun_magazine";
    private const string ChamberSlot = "gun_chamber";

    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    /// <summary>True if the NPC holds a gun after the call: it already did, or it took one out of its clothes or the bag it wears.</summary>
    public bool TryDraw(EntityUid owner)
    {
        if (_gun.TryGetGun(owner, out var held, out _))
        {
            EnsureComp<NpcGunMemoryComponent>(owner).Gun = held;
            return true;
        }

        if (!TryFindGun(owner, out var gun))
            return false;

        EnsureComp<NpcGunMemoryComponent>(owner).Gun = gun;

        // A gun worn in a slot comes off by force first (a rack on the back or the suit has no one to take it off), then into a free hand
        if (_inventory.TryGetContainingSlot(gun, out var worn))
            _inventory.TryUnequip(owner, worn.Name, out _, silent: true, force: true);

        return _hands.TryPickupAnyHand(owner, gun, checkActionBlocker: false, animate: false) && _hands.TrySelect(owner, gun);
    }

    /// <summary>
    ///     Out of combat: the NPC has no gun in hand and the one it dropped (a knockdown, an arc fault) lies near it: it picks that one up.
    ///     Guns in the holster stay there. True if it picked it up.
    /// </summary>
    public bool TryPickUpDropped(EntityUid owner)
    {
        if (_gun.TryGetGun(owner, out _, out _) || !TryComp<NpcGunMemoryComponent>(owner, out var memory) || !Exists(memory.Gun)
            || !HasComp<GunComponent>(memory.Gun) || _container.IsEntityOrParentInContainer(memory.Gun)
            || !Transform(owner).Coordinates.TryDistance(EntityManager, Transform(memory.Gun).Coordinates, out var distance) || distance > 4f)
            return false;

        return _hands.TryPickupAnyHand(owner, memory.Gun, checkActionBlocker: false, animate: false) && _hands.TrySelect(owner, memory.Gun);
    }

    private bool TryFindGun(EntityUid owner, out EntityUid gun)
    {
        // Dropped on the ground within reach
        if (TryComp<NpcGunMemoryComponent>(owner, out var memory) && Exists(memory.Gun) && HasComp<GunComponent>(memory.Gun)
            && !_container.IsEntityOrParentInContainer(memory.Gun)
            && Transform(owner).Coordinates.TryDistance(EntityManager, Transform(memory.Gun).Coordinates, out var distance) && distance <= 2.5f)
        {
            gun = memory.Gun;
            return true;
        }

        var slots = _inventory.GetSlotEnumerator(owner);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } item)
                continue;

            if (HasComp<GunComponent>(item))
            {
                gun = item;
                return true;
            }

            // A holster, a belt or a bag: one level in
            if (!TryComp<ContainerManagerComponent>(item, out var manager))
                continue;

            foreach (var container in _container.GetAllContainers(item, manager))
            {
                foreach (var inside in container.ContainedEntities)
                {
                    if (!HasComp<GunComponent>(inside))
                        continue;

                    gun = inside;
                    return true;
                }
            }
        }

        gun = default;
        return false;
    }

    /// <summary>
    ///     Whether the gun in the NPC's hand can fire: it holds a gun with a live round. A revolver full of spent casings counts as
    ///     empty (its ammo count does not).
    /// </summary>
    public bool HasRounds(EntityUid owner)
    {
        if (!_gun.TryGetGun(owner, out var gun, out _))
            return false;

        if (TryComp<RevolverAmmoProviderComponent>(gun, out var revolver))
        {
            for (var i = 0; i < revolver.Capacity; i++)
            {
                if (revolver.Chambers[i] == true)
                    return true;

                if (revolver.AmmoSlots[i] is { } cartridge && TryComp<CartridgeAmmoComponent>(cartridge, out var round) && !round.Spent)
                    return true;
            }

            return false;
        }

        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(gun, ref ev);
        return ev.Count > 0;
    }

    /// <summary>How long loading the gun in the NPC's hand takes, in seconds: a magazine, a cylinder, shells one by one.</summary>
    public float ReloadTime(EntityUid owner)
    {
        if (!_gun.TryGetGun(owner, out var gun, out _))
            return 0f;

        if (HasComp<RevolverAmmoProviderComponent>(gun))
            return 4f;

        return HasComp<BallisticAmmoProviderComponent>(gun) ? 3f : 2.2f;
    }

    /// <summary>The sound of the old ammunition coming out, as the reload starts.</summary>
    public void PlayReloadStart(EntityUid owner)
    {
        if (!_gun.TryGetGun(owner, out var gun, out _))
            return;

        if (TryComp<RevolverAmmoProviderComponent>(gun, out var revolver))
            _audio.PlayPvs(revolver.SoundEject, gun);
        else if (TryComp<ItemSlotsComponent>(gun, out var slots) && _itemSlots.TryGetSlot(gun, MagazineSlot, out var slot, slots))
            _audio.PlayPvs(slot.EjectSound, gun);
    }

    /// <summary>Loads the gun in the NPC's hand with new ammunition if it has no live round left. True if it did.</summary>
    public bool TryReload(EntityUid owner)
    {
        if (!_gun.TryGetGun(owner, out var gun, out _) || HasRounds(owner))
            return false;

        if (!Refill(gun))
            return false;

        if (TryComp<RevolverAmmoProviderComponent>(gun, out var revolver))
            _audio.PlayPvs(revolver.SoundInsert, gun);
        else if (TryComp<BallisticAmmoProviderComponent>(gun, out var ballistic))
            _audio.PlayPvs(ballistic.SoundInsert, gun);
        else if (TryComp<ItemSlotsComponent>(gun, out var slots) && _itemSlots.TryGetSlot(gun, MagazineSlot, out var slot, slots))
            _audio.PlayPvs(slot.InsertSound, gun);

        return true;
    }

    private bool Refill(EntityUid gun)
    {
        if (TryComp<RevolverAmmoProviderComponent>(gun, out var revolver))
        {
            _gun.RefillRevolver(gun, revolver);
            return true;
        }

        if (TryComp<BallisticAmmoProviderComponent>(gun, out var ballistic))
        {
            _gun.SetBallisticUnspawned((gun, ballistic), ballistic.Capacity - ballistic.Container.ContainedEntities.Count);
            return true;
        }

        if (!TryComp<ItemSlotsComponent>(gun, out var slots) || !_itemSlots.TryGetSlot(gun, MagazineSlot, out var magSlot, slots))
            return false;

        // A new magazine of the kind it has, or the one the gun comes with
        var magProto = magSlot.Item is { } old ? MetaData(old).EntityPrototype?.ID : magSlot.StartingItem;
        if (magProto == null)
            return false;

        if (magSlot.Item != null && _itemSlots.TryEject(gun, magSlot, null, out var oldMag))
            QueueDel(oldMag);

        var coords = Transform(gun).Coordinates;
        if (!_itemSlots.TryInsert(gun, magSlot, Spawn(magProto, coords), null))
            return false;

        // A rifle that fired its last round has an empty chamber
        if (_itemSlots.TryGetSlot(gun, ChamberSlot, out var chamber, slots) && chamber.Item == null && chamber.StartingItem != null)
            _itemSlots.TryInsert(gun, chamber, Spawn(chamber.StartingItem, coords), null);

        return true;
    }
}
