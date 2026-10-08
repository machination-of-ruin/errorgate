using Content.Server._ERRORGATE.LootManager;
using Content.Server.Body.Systems;
using Content.Shared.Mobs;
using Content.Shared.Throwing;
using Robust.Shared.Random;

namespace Content.Server._ERRORGATE.MobLoot;

public sealed class MobLootSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly BodySystem _bodySystem = default!;
    [Dependency] private readonly LootManagerSystem _lootManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobLootComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(EntityUid uid, MobLootComponent component, MobStateChangedEvent args)
    {
        if (component.HasDropped || args.NewMobState != MobState.Dead)
            return;

        component.HasDropped = true;

        TrySpawnLoot(uid, component);

        if (component.GibOnDrop)
            _bodySystem.GibBody(uid);
    }

    private void TrySpawnLoot(EntityUid uid, MobLootComponent component)
    {
        if (!_random.Prob(component.DropChance))
            return;

        var table = _lootManager.GetLootTable(component.Rarity, component.Location);

        if (!_lootManager.TryPickLoot(table, out var entityProto) || entityProto == null)
        {
            Log.Debug($"Loot table for {ToPrettyString(uid)} is empty");
            return;
        }

        _lootManager.RegisterSpawn(entityProto);

        var lootDrop = SpawnNextToOrDrop(entityProto, uid);

        var landEvent = new LandEvent(lootDrop, true); // Make it fall
        RaiseLocalEvent(lootDrop, ref landEvent);
    }
}
