namespace Content.Server._ERRORGATE.LootManager;

/// <summary>
/// A marker that now and then spawns one item from the loot manager's global table.
/// </summary>
[RegisterComponent]
public sealed partial class LootSpawnerComponent : Component
{
    // Loot Rarity used by the loot manager to determine possible loot
    [DataField]
    public int Rarity = 1;

    // Loot Location used by the loot manager to determine possible loot
    [DataField]
    public string Location = "Living";

    // Try to spawn that many entities per hour (3600 seconds)
    [DataField]
    public float SpawnRate = 3f;

    // The interval between the attempts to spawn an entity. Does not affect the spawn probability.
    [DataField]
    public int IntervalSeconds = 600;

    /// <summary>
    /// When the next attempt happens. Not saved, set again when the spawner starts.
    /// </summary>
    public TimeSpan NextAttempt;
}
