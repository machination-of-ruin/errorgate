using System.Linq;
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
/// Keeps the world's loot finite: every item has a cap, spawners and mobs only draw what is left of it. Every map that has a
/// loot manager counts for itself, so a location of the world has its own caps and gives them back when it is wiped.
/// </summary>
public sealed class LootManagerSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly Dictionary<EntityUid, Dictionary<string, LootManagerEntry>> _tables = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LootManagerComponent, ComponentInit>(OnManagerInit);
        SubscribeLocalEvent<LootManagerComponent, ComponentShutdown>(OnManagerShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _tables.Clear());
    }

    private void OnManagerShutdown(EntityUid uid, LootManagerComponent component, ComponentShutdown args)
    {
        _tables.Remove(uid);
    }

    private void OnManagerInit(EntityUid uid, LootManagerComponent component, ComponentInit args)
    {
        var entries = new Dictionary<string, LootManagerEntry>();
        _tables[uid] = entries;

        // a blank component (no table set) has nothing to load
        if (string.IsNullOrEmpty(component.GlobalLootTablePrototype.Id))
            return;

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

            entries.Add(id, new LootManagerEntry(0, maxCount, loot.RarityTiers, loot.Locations, loot.ChildEntries));
        }
    }

    /// <summary>The caps that count for something at this place: those of its map, else those of the first loot manager there is.</summary>
    private Dictionary<string, LootManagerEntry>? EntriesFor(EntityUid? source)
    {
        if (source is { } uid && Transform(uid).MapUid is { } map && _tables.TryGetValue(map, out var own))
            return own;

        foreach (var table in _tables.Values)
        {
            return table;
        }

        return null;
    }

    /// <summary>The caps of the first loot manager there is (the one of a single map).</summary>
    public Dictionary<string, LootManagerEntry> LootManager => EntriesFor(null) ?? new();

    /// <summary>The entry of an item in any loot manager (for items that exist only once).</summary>
    public bool TryGetEntry(string proto, out LootManagerEntry? entry)
    {
        foreach (var table in _tables.Values)
        {
            if (table.TryGetValue(proto, out entry))
                return true;
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Every item that may be drawn for this rarity and location, with how many of it are left.
    /// </summary>
    public Dictionary<string, int> GetLootTable(int rarity, string location, EntityUid? source = null)
    {
        var table = new Dictionary<string, int>();
        if (EntriesFor(source) is not { } entries)
            return table;

        foreach (var (id, entry) in entries)
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

    /// <summary>The entry that counts an item: its own, else that of its random-magazine variant (what the loot tables list, a gear gun is the plain one).</summary>
    private static bool TryResolve(Dictionary<string, LootManagerEntry> entries, string proto, out LootManagerEntry entry)
    {
        return entries.TryGetValue(proto, out entry!) || entries.TryGetValue(proto + "RandomMag", out entry!);
    }

    /// <summary>
    /// Counts one more of this item (and its children) as existing in the world.
    /// </summary>
    public void RegisterSpawn(string proto, EntityUid? source = null)
    {
        if (EntriesFor(source) is not { } entries || !TryResolve(entries, proto, out var entry))
            return;

        entry.Count += 1;

        foreach (var child in entry.ChildEntries)
        {
            if (entries.TryGetValue(child, out var childEntry))
                childEntry.Count += 1;
        }
    }

    /// <summary>
    /// An item that comes on top of the loot tables (the kit of an NPC): counts as existing and raises the cap of its entry (and its
    /// children) by one, so it takes nothing away from what the spawners may hand out. Returns the key of the entry and the map that
    /// counts it, for <see cref="ShrinkCap"/> when the item is gone.
    /// </summary>
    public bool TryExpandCap(string proto, EntityUid? source, out EntityUid owner, out string key)
    {
        owner = default;
        key = string.Empty;
        var entries = EntriesFor(source);
        if (entries == null || !TryResolve(entries, proto, out var entry))
            return false;

        foreach (var (table, tableEntries) in _tables)
        {
            if (tableEntries != entries)
                continue;

            owner = table;
            break;
        }

        key = entries.First(e => e.Value == entry).Key;
        entry.MaxCount++;
        entry.Count++;
        foreach (var child in entry.ChildEntries)
        {
            if (!entries.TryGetValue(child, out var childEntry))
                continue;

            childEntry.MaxCount++;
            childEntry.Count++;
        }

        return true;
    }

    /// <summary>The item that <see cref="TryExpandCap"/> counted is gone: the cap goes back down (the slot of the item itself is freed by <see cref="Refund"/>, those of its children here).</summary>
    public void ShrinkCap(EntityUid owner, string key)
    {
        if (!_tables.TryGetValue(owner, out var entries) || !entries.TryGetValue(key, out var entry))
            return;

        entry.MaxCount = Math.Max(0, entry.MaxCount - 1);
        foreach (var child in entry.ChildEntries)
        {
            if (!entries.TryGetValue(child, out var childEntry))
                continue;

            childEntry.MaxCount = Math.Max(0, childEntry.MaxCount - 1);
            childEntry.Count = Math.Max(0, childEntry.Count - 1); // the count of a child is never refunded by its own item
        }
    }

    /// <summary>
    /// An item is gone, its place in the cap is free again.
    /// </summary>
    public void Refund(string proto, EntityUid? source = null)
    {
        if (EntriesFor(source) is { } entries && TryResolve(entries, proto, out var entry))
            entry.Count = Math.Max(0, entry.Count - 1);
    }
}
