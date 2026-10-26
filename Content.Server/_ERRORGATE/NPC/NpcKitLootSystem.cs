using Content.Server._ERRORGATE.LootManager;
using Content.Shared.Clothing.Loadouts.Systems;
using Robust.Shared.Containers;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>An item of an NPC's kit that was added on top of a loot cap (see <see cref="NpcKitLootSystem"/>).</summary>
[RegisterComponent]
public sealed partial class NpcKitItemComponent : Component
{
    public EntityUid Map;

    public string Key = string.Empty;
}

/// <summary>
///     The guns, magazines and tools a soldier or a bandit is born with are loot like any other: they drop when the NPC dies, and an NPC
///     that respawns brings a new kit. They count as existing and raise the caps of the loot manager by one each (and back down when the item is gone), so they
///     take nothing from what the spawners hand out.
/// </summary>
public sealed class NpcKitLootSystem : EntitySystem
{
    [Dependency] private readonly LootManagerSystem _lootManager = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcNeedsComponent, MapInitEvent>(OnMapInit, after: [typeof(SharedLoadoutSystem)]);
        SubscribeLocalEvent<NpcKitItemComponent, ComponentShutdown>(OnItemGone);
    }

    private void OnItemGone(Entity<NpcKitItemComponent> item, ref ComponentShutdown args)
    {
        _lootManager.ShrinkCap(item.Comp.Map, item.Comp.Key);
    }

    private void OnMapInit(Entity<NpcNeedsComponent> ent, ref MapInitEvent args)
    {
        // Everything the NPC carries, worn or stowed, at any depth
        var pending = new Stack<EntityUid>();
        pending.Push(ent);
        while (pending.TryPop(out var holder))
        {
            if (!TryComp<ContainerManagerComponent>(holder, out var manager))
                continue;

            foreach (var container in _container.GetAllContainers(holder, manager))
            {
                foreach (var item in container.ContainedEntities)
                {
                    if (MetaData(item).EntityPrototype is { } proto && _lootManager.TryExpandCap(proto.ID, ent, out var owner, out var key))
                    {
                        var kit = EnsureComp<NpcKitItemComponent>(item);
                        kit.Map = owner;
                        kit.Key = key;
                    }

                    pending.Push(item);
                }
            }
        }
    }
}
