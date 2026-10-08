using Content.Server._ERRORGATE.LootManager;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.Despawn;

public sealed class DespawnItemSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly LootManagerSystem _lootManager = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DespawnItemComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<DespawnItemComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<DespawnItemComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private void OnShutdown(EntityUid uid, DespawnItemComponent component, ComponentShutdown args)
    {
        // The item is gone, its place in the loot cap is free again
        if (MetaData(uid).EntityPrototype is { } proto)
            _lootManager.Refund(proto.ID);
    }

    private void OnMapInit(EntityUid uid, DespawnItemComponent component, MapInitEvent args)
    {
        UpdateTimer(uid, component);
    }

    private void OnParentChanged(EntityUid uid, DespawnItemComponent component, ref EntParentChangedMessage args)
    {
        if (args.OldParent == args.Transform.ParentUid || !args.Transform.ParentUid.IsValid())
            return;

        UpdateTimer(uid, component);
    }

    private void UpdateTimer(EntityUid uid, DespawnItemComponent component)
    {
        // Only items lying on a grid count, anything carried or stored is safe
        if (component.Despawn && HasComp<MapGridComponent>(Transform(uid).ParentUid))
            component.DespawnAt = _timing.CurTime + TimeSpan.FromSeconds(component.DespawnAfterSeconds);
        else
            component.DespawnAt = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        if (curTime < _nextCheck)
            return;

        _nextCheck = curTime + CheckInterval;

        var query = EntityQueryEnumerator<DespawnItemComponent>();
        while (query.MoveNext(out var uid, out var despawn))
        {
            if (despawn.DespawnAt is { } at && at <= curTime)
                QueueDel(uid);
        }
    }
}
