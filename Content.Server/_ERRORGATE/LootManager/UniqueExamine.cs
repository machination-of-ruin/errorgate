using Content.Server._ERRORGATE.Despawn;
using Content.Shared.Examine;

namespace Content.Server._ERRORGATE.LootManager;

public sealed class UniqueExamine : EntitySystem
{
    [Dependency] private readonly LootManagerSystem _lootManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DespawnItemComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, DespawnItemComponent component, ExaminedEvent args)
    {
        if (MetaData(uid).EntityPrototype is not { } proto)
            return;

        if (_lootManager.LootManager.TryGetValue(proto.ID, out var value) && value.MaxCount == 1)
            args.PushMarkup("[color=red][italic]>>> Unique <<<[/italic][/color]", 200);
    }
}
