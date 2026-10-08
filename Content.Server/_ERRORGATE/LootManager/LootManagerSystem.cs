using Content.Shared.GameTicking;
using Content.Shared._ERRORGATE.LootManager;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ERRORGATE.LootManager;

public sealed class LootManagerEntry
{
    public int Count;
    public int MaxCount;
    public List<int> RarityTiers;
    public List<string> Locations;
    public List<string> ChildEntries;

    public LootManagerEntry(int count, int maxCount, List<int> rarityTiers, List<string> locations, List<string> childEntries)
    {
        Count = count;
        MaxCount = maxCount;
        RarityTiers = rarityTiers;
        Locations = locations;
        ChildEntries = childEntries;
    }
}

/// <summary>
/// Keeps the world's loot finite: every item has a cap, spawners and mobs only draw what is left of it.
/// </summary>
public sealed class LootManagerSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public Dictionary<string, LootManagerEntry> LootManager = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LootManagerComponent, ComponentInit>(OnManagerInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => LootManager = new());
    }

    private void OnManagerInit(EntityUid uid, LootManagerComponent component, ComponentInit args)
    {
        LootManager = new();

        if (!_prototypeManager.TryIndex(component.GlobalLootTablePrototype, out var proto))
        {
            Log.Error("Could not find a global loot table");
            return;
        }

        foreach (var (id, maxCount) in proto.Entries)
        {
            if (!_prototypeManager.TryIndex<LootEntryPrototype>(id, out var loot))
            {
                Log.Error($"Could not find a loot entry prototype for {id}");
                continue;
            }

            LootManager.Add(id, new LootManagerEntry(0, maxCount, loot.RarityTiers, loot.Locations, loot.ChildEntries));
        }
    }

    /// <summary>
    /// Every item that may be drawn for this rarity and location, with how many of it are left.
    /// </summary>
    public Dictionary<string, int> GetLootTable(int rarity, string location)
    {
        var table = new Dictionary<string, int>();

        foreach (var (id, entry) in LootManager)
        {
            if (entry.RarityTiers.Contains(rarity) && entry.Locations.Contains(location))
                table[id] = Math.Max(0, entry.MaxCount - entry.Count);
        }

        return table;
    }

    /// <summary>
    /// Weighted pick of one item. Fails if nothing is left to draw.
    /// </summary>
    public bool TryPickLoot(Dictionary<string, int> table, out string? proto)
    {
        proto = null;

        var sum = 0;
        foreach (var weight in table.Values)
        {
            sum += weight;
        }

        if (sum <= 0)
            return false;

        var roll = _random.Next(sum);
        foreach (var (id, weight) in table)
        {
            if (weight <= 0)
                continue;

            if (roll < weight)
            {
                proto = id;
                return true;
            }

            roll -= weight;
        }

        return false;
    }

    /// <summary>
    /// Counts one more of this item (and its children) as existing in the world.
    /// </summary>
    public void RegisterSpawn(string proto)
    {
        if (!LootManager.TryGetValue(proto, out var entry))
            return;

        entry.Count += 1;

        foreach (var child in entry.ChildEntries)
        {
            if (LootManager.TryGetValue(child, out var childEntry))
                childEntry.Count += 1;
        }
    }

    /// <summary>
    /// An item is gone, its place in the cap is free again.
    /// </summary>
    public void Refund(string proto)
    {
        if (LootManager.TryGetValue(proto, out var entry))
            entry.Count = Math.Max(0, entry.Count - 1);
    }
}
