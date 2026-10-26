using Content.Server._ERRORGATE.LootManager;
using Content.Shared.Clothing.Loadouts.Systems;
using Robust.Shared.Containers;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>
///     The guns, magazines and tools a soldier or a bandit is born with are loot like any other: they drop when the NPC dies, and an NPC
///     that respawns brings a new kit. They count against the caps of the loot manager, so NPCs do not add to the loot of the world on top
///     of what the spawners hand out.
/// </summary>
public sealed class NpcKitLootSystem : EntitySystem
{
    [Dependency] private readonly LootManagerSystem _lootManager = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcNeedsComponent, MapInitEvent>(OnMapInit, after: [typeof(SharedLoadoutSystem)]);
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
                    if (MetaData(item).EntityPrototype is { } proto)
                        _lootManager.RegisterSpawn(proto.ID, ent);

                    pending.Push(item);
                }
            }
        }
    }
}
