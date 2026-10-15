using System.Linq;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.World;

/// <summary>One thing the features of a location put on a tile. The prototype can be a sentinel: <c>@water</c> and <c>@tree</c> take the location's own.</summary>
public sealed record FeatureItem(Vector2i Tile, string Proto, int Quarter = 0);

/// <summary>A place worth walking to: farms, camps, ruins, orchards, ponds, wells. See docs/design/world.md.</summary>
public sealed record Feature(string Kind, Vector2i Center);

public sealed partial class LocationLayout
{
    public List<FeatureItem> Items = new();

    public List<Feature> Features = new();

    /// <summary>Ground that is not the usual ground of the location (the tilled rows of a farm), by tile prototype.</summary>
    public Dictionary<Vector2i, string> GroundOverrides = new();

    private static readonly string[] CampLoot =
    {
        "LootSpawnerMilitaryTier1", "LootSpawnerMedicalTier1", "LootSpawnerIndustrialTier1", "LootSpawnerSecurityTier1", "LootSpawnerLivingTier1",
    };

    private static readonly string[] RuinLoot =
    {
        "LootSpawnerLivingTier1", "LootSpawnerIndustrialTier1", "LootSpawnerMedicalTier1", "LootSpawnerMilitaryTier1", "LootSpawnerKitchenTier1",
    };

    /// <summary>
    ///     The places that make the empty ground worth crossing: farms behind a fence, camps by a fire, burnt ruins, orchards, ponds and wells,
    ///     and footpaths from them to the streets. Every one holds loot, the camps and ruins sometimes a mob spawner; the farther from where
    ///     people arrive, the more there is. They keep clear of everything else and of the gates, the spawn spots and each other.
    /// </summary>
    private void PlaceFeatures(Random rng, IReadOnlyList<(string Kind, int Count)> wanted)
    {
        var centres = new List<Vector2i>();

        bool Free(Vector2i t) =>
            InShape(t) && Dist(t) >= 8 && !Terrain.ContainsKey(t) && !_nearShore.Contains(t) && !_occupied.Contains(t) && !Roads.Contains(t) && !Walks.Contains(t);

        bool AreaFree(Vector2i c, int rx, int ry)
        {
            for (var dx = -rx - 1; dx <= rx + 1; dx++)
            {
                for (var dy = -ry - 1; dy <= ry + 1; dy++)
                {
                    if (!Free(new Vector2i(c.X + dx, c.Y + dy)))
                        return false;
                }
            }

            return GatePlaces.Values.All(g => (g.Tile - c).Length > 24)
                   && SpawnTiles.All(s => (s - c).Length > 26)
                   && centres.All(o => (o - c).Length > 9);
        }

        Vector2i? Find(int rx, int ry)
        {
            for (var attempt = 0; attempt < 600; attempt++)
            {
                var c = new Vector2i(rng.Next(10, Bounds.X - 10), rng.Next(10, Bounds.Y - 10));
                if (AreaFree(c, rx, ry))
                    return c;
            }

            return null;
        }

        void Claim(Vector2i c, int rx, int ry)
        {
            centres.Add(c);
            for (var dx = -rx - 1; dx <= rx + 1; dx++)
            {
                for (var dy = -ry - 1; dy <= ry + 1; dy++)
                {
                    _occupied.Add(new Vector2i(c.X + dx, c.Y + dy));
                }
            }
        }

        // The farther from where people arrive, the more loot
        int Bonus(Vector2i c) => SpawnTiles.Count == 0 ? 0 : SpawnTiles.Min(s => (s - c).Length) > 70 ? 1 : 0;
        void Put(Vector2i tile, string proto, int quarter = 0) => Items.Add(new FeatureItem(tile, proto, quarter));
        Vector2i At(Vector2i c, int dx, int dy) => new(c.X + dx, c.Y + dy);

        foreach (var (kind, count) in wanted)
        {
            for (var n = 0; n < count; n++)
            {
                switch (kind)
                {
                    case "farm":
                    {
                        const int rx = 6, ry = 5;
                        if (Find(rx, ry) is not { } c)
                            break;

                        Claim(c, rx, ry);
                        Features.Add(new Feature(kind, c));

                        // The gate faces the nearest street
                        var side = Roads.Count == 0 ? 0 : NearestSide(c);
                        AddFence((c.X - rx, c.Y - ry, c.X + rx, c.Y + ry), side, c);

                        // Tilled rows, a cart at the gate, barrels and a crate, loot in the rows
                        for (var y = -ry + 2; y <= ry - 2; y += 2)
                        {
                            for (var x = -rx + 2; x <= rx - 2; x++)
                            {
                                GroundOverrides[At(c, x, y)] = "FloorPlanetDirt";
                            }
                        }

                        Put(At(c, rx - 2, ry - 1), "N14BlackBarrel");
                        Put(At(c, -rx + 2, ry - 1), "N14CrateWooden");
                        Put(At(c, -rx + 1, -ry + 1), "N14DecorMinecart");
                        Put(At(c, 1, 1), "LootSpawnerKitchenTier1");
                        Put(At(c, -3, -1), "LootSpawnerKitchenTier1");
                        Put(At(c, 3, -1), "LootSpawnerLivingTier1");
                        if (Bonus(c) > 0)
                            Put(At(c, -4, 2), "LootSpawnerIndustrialTier1");

                        break;
                    }

                    case "camp":
                    {
                        const int r = 3;
                        if (Find(r, r) is not { } c)
                            break;

                        Claim(c, r, r);
                        Features.Add(new Feature(kind, c));
                        Put(c, "CampfireWorld");
                        Put(At(c, -2, 1), "N14Bedroll", 1);
                        Put(At(c, 2, -1), "N14Bedroll", 3);
                        Put(At(c, 1, 3), "N14Bedroll", 2);
                        Put(At(c, -3, -2), "N14CrateWooden");
                        Put(At(c, 3, 2), "N14BlackBarrel");
                        Put(At(c, -1, -3), CampLoot[rng.Next(CampLoot.Length)]);
                        Put(At(c, 2, 1), CampLoot[rng.Next(CampLoot.Length)]);
                        if (Bonus(c) > 0)
                            Put(At(c, -2, 3), CampLoot[rng.Next(CampLoot.Length)]);

                        // Someone is usually at home
                        if (rng.NextDouble() < 0.7)
                            Put(At(c, 3, 3), "MobSpawnerWalkerLiving");

                        break;
                    }

                    case "ruin":
                    {
                        const int rx = 5, ry = 4;
                        if (Find(rx, ry) is not { } c)
                            break;

                        Claim(c, rx, ry);
                        Features.Add(new Feature(kind, c));

                        // The outline of a house with most of its walls gone, rubble inside and around
                        var door = rng.Next(4);
                        for (var x = -rx + 1; x <= rx - 1; x++)
                        {
                            for (var y = -ry + 1; y <= ry - 1; y++)
                            {
                                var edge = Math.Abs(x) == rx - 1 || Math.Abs(y) == ry - 1;
                                var tile = At(c, x, y);
                                if (edge)
                                {
                                    var doorway = door switch { 0 => x == 0 && y == -ry + 1, 1 => x == 0 && y == ry - 1, 2 => y == 0 && x == -rx + 1, _ => y == 0 && x == rx - 1 };
                                    if (!doorway && rng.NextDouble() < 0.55)
                                        Put(tile, "WallBrick");
                                    else if (rng.NextDouble() < 0.5)
                                        Put(tile, "N14DecorFloorBrickrubble");
                                }
                                else if (rng.NextDouble() < 0.16)
                                {
                                    Put(tile, rng.Next(2) == 0 ? "N14DecorFloorBrickrubble" : "N14DecorFloorScrapwood");
                                }
                            }
                        }

                        Put(At(c, -2, 0), RuinLoot[rng.Next(RuinLoot.Length)]);
                        Put(At(c, 2, 1), RuinLoot[rng.Next(RuinLoot.Length)]);
                        Put(At(c, 0, -2), RuinLoot[rng.Next(RuinLoot.Length)]);
                        if (Bonus(c) > 0)
                            Put(At(c, 1, 2), "LootSpawnerMilitaryTier1");

                        if (rng.NextDouble() < 0.5)
                            Put(At(c, rx, ry), "MobSpawnerWalkerLiving");

                        break;
                    }

                    case "orchard":
                    {
                        const int rx = 6, ry = 4;
                        if (Find(rx, ry) is not { } c)
                            break;

                        Claim(c, rx, ry);
                        Features.Add(new Feature(kind, c));
                        for (var x = -rx + 1; x <= rx - 1; x += 3)
                        {
                            for (var y = -ry + 1; y <= ry - 1; y += 3)
                            {
                                Put(At(c, x, y), "@tree");
                            }
                        }

                        Put(At(c, 1, 1), "LootSpawnerKitchenTier1");
                        Put(At(c, -3, -2), "LootSpawnerKitchenTier1");
                        if (Bonus(c) > 0)
                            Put(At(c, 4, 2), "LootSpawnerLivingTier1");

                        break;
                    }

                    case "windbreak":
                    {
                        // A line of trees along a field, with a gap or two
                        var horizontal = rng.Next(2) == 0;
                        var rx = horizontal ? 13 : 1;
                        var ry = horizontal ? 1 : 13;
                        if (Find(rx, ry) is not { } c)
                            break;

                        Claim(c, rx, ry);
                        Features.Add(new Feature(kind, c));
                        for (var i = -12; i <= 12; i += 2)
                        {
                            if (rng.NextDouble() < 0.12)
                                continue;

                            Put(horizontal ? At(c, i, 0) : At(c, 0, i), "@tree");
                        }

                        break;
                    }

                    case "pond":
                    {
                        const int r = 6;
                        if (Find(r, r) is not { } c)
                            break;

                        Claim(c, r, r);
                        Features.Add(new Feature(kind, c));

                        // An uneven blob of shallow water with a shore of mud, and something on the bank
                        var radius = 3.0 + rng.NextDouble() * 2.0;
                        var p1 = rng.NextDouble() * 6.28;
                        var p2 = rng.NextDouble() * 6.28;
                        for (var dx = -r; dx <= r; dx++)
                        {
                            for (var dy = -r; dy <= r; dy++)
                            {
                                var angle = Math.Atan2(dy, dx);
                                var edge = radius + 1.2 * Math.Sin(angle * 3 + p1) + 0.8 * Math.Sin(angle * 5 + p2);
                                var d = Math.Sqrt(dx * dx + dy * dy);
                                if (d < edge)
                                    Put(At(c, dx, dy), "@water");
                                else if (d < edge + 1.6)
                                    GroundOverrides[At(c, dx, dy)] = "FloorPlanetDirt";
                            }
                        }

                        Put(At(c, (int) Math.Round(radius) + 2, 1), "LootSpawnerLivingTier1");
                        if (Bonus(c) > 0)
                            Put(At(c, -(int) Math.Round(radius) - 2, -1), "LootSpawnerIndustrialTier1");

                        break;
                    }

                    case "parking":
                    {
                        const int rx = 6, ry = 3;
                        if (Find(rx, ry) is not { } c)
                            break;

                        Claim(c, rx, ry);
                        Features.Add(new Feature(kind, c));

                        // A paved lot with two rows of wrecks and what was left in them
                        for (var x = -rx; x <= rx; x++)
                        {
                            for (var y = -ry; y <= ry; y++)
                            {
                                GroundOverrides[At(c, x, y)] = "FloorGrayConcrete";
                            }
                        }

                        for (var x = -rx + 1; x <= rx - 1; x += 3)
                        {
                            if (rng.NextDouble() < 0.75)
                                Put(At(c, x, -ry + 1), rng.Next(3) == 0 ? "N14DecorMinecart" : "N14CrateTrashcart");

                            if (rng.NextDouble() < 0.6)
                                Put(At(c, x, ry - 1), rng.Next(3) == 0 ? "N14DecorMinecart" : "N14CrateTrashcart");
                        }

                        Put(At(c, -rx, 0), "N14BurningBarrel");
                        Put(At(c, 1, 0), "LootSpawnerIndustrialTier1");
                        Put(At(c, -3, 0), "LootSpawnerIndustrialTier1");
                        Put(At(c, 4, 0), "LootSpawnerLivingTier1");
                        if (Bonus(c) > 0)
                            Put(At(c, 2, 1), "LootSpawnerMilitaryTier1");

                        break;
                    }

                    case "dump":
                    {
                        const int r = 5;
                        if (Find(r, r) is not { } c)
                            break;

                        Claim(c, r, r);
                        Features.Add(new Feature(kind, c));

                        // Radioactive waste: a patch of burnt ground, barrels that glow green and are deadly close by, and what the people
                        // who left it were guarding (needs a radiation suit to reach)
                        for (var dx = -r; dx <= r; dx++)
                        {
                            for (var dy = -r; dy <= r; dy++)
                            {
                                if (dx * dx + dy * dy <= (r - 1) * (r - 1) + rng.Next(0, 4))
                                    GroundOverrides[At(c, dx, dy)] = "FloorSteelDamaged";
                            }
                        }

                        var barrels = rng.Next(5, 9);
                        for (var i = 0; i < barrels; i++)
                        {
                            var angle = rng.NextDouble() * Math.PI * 2;
                            var dist = 1.5 + rng.NextDouble() * 2.6;
                            var tile = At(c, (int) Math.Round(Math.Cos(angle) * dist), (int) Math.Round(Math.Sin(angle) * dist));
                            if (Items.All(it => it.Tile != tile) && tile != c)
                                Put(tile, i < 4 ? "BunkerRadiationBarrel" : "N14BlackBarrel");
                        }

                        Put(c, "LootSpawnerIndustrialTier1");
                        Put(At(c, 1, 1), "LootSpawnerMedicalTier1");
                        Put(At(c, -1, 1), "LootSpawnerIndustrialTier1");
                        if (Bonus(c) > 0)
                            Put(At(c, 0, -1), "LootSpawnerMilitaryTier1");

                        break;
                    }

                    case "checkpoint":
                    {
                        const int rx = 4, ry = 3;
                        if (Find(rx, ry) is not { } c)
                            break;

                        Claim(c, rx, ry);
                        Features.Add(new Feature(kind, c));

                        // A held position: sandbags and tank traps around a few crates
                        for (var x = -rx; x <= rx; x++)
                        {
                            Put(At(c, x, -ry), x % 2 == 0 ? "N14BarricadeSandbagSingle" : "N14BarricadeTanktrap");
                            Put(At(c, x, ry), x % 2 == 0 ? "N14BarricadeSandbagSingle" : "N14BarricadeTanktrap");
                        }

                        Put(At(c, -2, 0), "N14CrateMilitary");
                        Put(At(c, 2, 1), "N14CrateMilitary");
                        Put(At(c, 0, -1), "LootSpawnerMilitaryTier1");
                        Put(At(c, 1, 1), "LootSpawnerMilitaryTier1");
                        Put(At(c, -1, 1), "LootSpawnerSecurityTier1");
                        if (rng.NextDouble() < 0.6)
                            Put(At(c, 0, 0), "MobSpawnerWalkerArmored");

                        break;
                    }

                    case "construction":
                    {
                        const int rx = 6, ry = 4;
                        if (Find(rx, ry) is not { } c)
                            break;

                        Claim(c, rx, ry);
                        Features.Add(new Feature(kind, c));
                        for (var x = -rx; x <= rx; x++)
                        {
                            for (var y = -ry; y <= ry; y++)
                            {
                                GroundOverrides[At(c, x, y)] = "FloorGrayConcrete";
                            }
                        }

                        for (var i = 0; i < 9; i++)
                        {
                            var tile = At(c, rng.Next(-rx + 1, rx), rng.Next(-ry + 1, ry));
                            if (Items.All(it => it.Tile != tile))
                                Put(tile, rng.Next(4) switch { 0 => "N14CrateBreakPlain", 1 => "N14BlackBarrel", 2 => "N14CrateBreakPlainDamaged", _ => "N14DecorFloorScrapwood" });
                        }

                        Put(At(c, -3, 0), "LootSpawnerIndustrialTier1");
                        Put(At(c, 2, -1), "LootSpawnerIndustrialTier1");
                        Put(At(c, 4, 2), "LootSpawnerIndustrialTier1");
                        if (Bonus(c) > 0)
                            Put(At(c, 0, 2), "LootSpawnerSecurityTier1");

                        break;
                    }

                    case "well":
                    {
                        if (Find(2, 2) is not { } c)
                            break;

                        Claim(c, 2, 2);
                        Features.Add(new Feature(kind, c));
                        Put(c, "N14WellBoarded");
                        Put(At(c, 2, 0), "LootSpawnerLivingTier1");
                        Put(At(c, -1, 2), "N14BlackBarrel");
                        break;
                    }
                }
            }
        }

        // Footpaths from the camps, ruins and ponds to the nearest street
        foreach (var feature in Features.Where(f => f.Kind is "camp" or "ruin" or "pond" or "farm" or "well"))
        {
            CutTrail(rng, feature.Center);
        }
    }

    /// <summary>Which side of a place the nearest street lies on (0 south, 1 east, 2 north, 3 west).</summary>
    private int NearestSide(Vector2i c)
    {
        var best = Roads.OrderBy(r => (r - c).LengthSquared).First();
        var d = best - c;
        return Math.Abs(d.X) > Math.Abs(d.Y) ? (d.X > 0 ? 1 : 3) : (d.Y > 0 ? 2 : 0);
    }

    /// <summary>A one tile wide footpath from a place to the nearest road tile, wandering a little, over free ground only.</summary>
    private void CutTrail(Random rng, Vector2i from)
    {
        if (Roads.Count == 0)
            return;

        var target = Roads.OrderBy(r => (r - from).LengthSquared).First();
        var at = from;
        var steps = 0;
        var itemTiles = new HashSet<Vector2i>(Items.Select(i => i.Tile));
        while (at != target && steps++ < 400)
        {
            var d = target - at;
            var horizontal = Math.Abs(d.X) > Math.Abs(d.Y) ? rng.NextDouble() < 0.85 : rng.NextDouble() < 0.15;
            var next = horizontal ? new Vector2i(at.X + Math.Sign(d.X), at.Y) : new Vector2i(at.X, at.Y + Math.Sign(d.Y));
            if (next == at)
                next = new Vector2i(at.X + Math.Sign(d.X), at.Y + Math.Sign(d.Y));

            at = next;
            if (Roads.Contains(at) || Walks.Contains(at))
                break;

            // Not through water, rock, fences or what the features put down; a trail that meets one just ends there
            if (!InShape(at) || Dist(at) < 4 || Terrain.ContainsKey(at) || itemTiles.Contains(at) || Fences.Any(f => f.Tile == at))
                break;

            if (!_occupied.Contains(at) || Features.Any(f => (f.Center - at).Length < 10))
                Walks.Add(at);
        }
    }
}
