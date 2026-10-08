using Content.Shared._ERRORGATE.LootManager;
using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.LootManager;

/// <summary>
/// Goes on a map entity. Loads the global loot table the loot spawners and mob loot draw from.
/// </summary>
[RegisterComponent]
public sealed partial class LootManagerComponent : Component
{
    [DataField(required: true)]
    public ProtoId<GlobalLootTablePrototype> GlobalLootTablePrototype;
}
