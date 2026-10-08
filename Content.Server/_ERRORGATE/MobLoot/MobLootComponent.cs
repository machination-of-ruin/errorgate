namespace Content.Server._ERRORGATE.MobLoot;

/// <summary>
/// Drops one item from the loot manager's global table when the mob dies.
/// </summary>
[RegisterComponent]
public sealed partial class MobLootComponent : Component
{
    // Loot Rarity used by the loot manager to determine possible loot
    [DataField]
    public int Rarity = 1;

    // Loot Location used by the loot manager to determine possible loot
    [DataField]
    public string Location = "Living";

    [DataField]
    public float DropChance = 1.0f;

    [DataField]
    public bool GibOnDrop;

    // So it only attempts to drop once
    public bool HasDropped;
}
