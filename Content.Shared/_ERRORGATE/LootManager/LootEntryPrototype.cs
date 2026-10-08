using Robust.Shared.Prototypes;

namespace Content.Shared._ERRORGATE.LootManager;

/// <summary>
/// A loot entry: which spawners (by rarity tier and location) may produce an item.
/// </summary>
[Prototype("lootEntry")]
public sealed class LootEntryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<int> RarityTiers = [];

    [DataField]
    public List<string> Locations = [];

    /// <summary>
    /// Entries that are counted together with this one (a gun uses up part of its magazines' cap).
    /// </summary>
    [DataField]
    public List<string> ChildEntries = [];
}
