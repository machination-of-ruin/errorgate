using System.Linq;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.World;

/// <summary>A structure file, the size of its footprint (walls to walls) and the corner of the footprint in the grid's own tiles, measured once.</summary>
public sealed record StructureInfo(string Path, int Width, int Height, Vector2i LocalMin);

/// <summary>A structure placed in a location: its footprint rectangle (after rotation) and how many quarter turns it was turned.</summary>
public sealed record StructurePlacement(StructureInfo Info, int Turns, Vector2i Min, int Width, int Height)
{
    public Vector2i Max => new(Min.X + Width - 1, Min.Y + Height - 1);
    public Vector2i Center => new(Min.X + Width / 2, Min.Y + Height / 2);
}

/// <summary>
///     The plan of a location, in plain data: where each structure goes and where the roads run. The same seed gives the same plan.
///     Nothing is spawned here.
/// </summary>
public sealed class LocationLayout
{
    /// <summary>Tiles kept clear between two structures.</summary>
    public const int Gap = 4;

    /// <summary>Tiles kept clear along the border (the border walls are inside it).</summary>
    public const int Margin = 6;

    public List<StructurePlacement> Structures = new();

    /// <summary>Road tiles, three wide.</summary>
    public HashSet<Vector2i> Roads = new();

    /// <summary>Tiles of indestructible rock, in formations.</summary>
    public HashSet<Vector2i> Rocks = new();

    /// <summary>Where trees stand.</summary>
    public List<Vector2i> Trees = new();

    /// <summary>What the edge of the map is on its shore side: sand, water or ice. Tiles not in the dictionary are plain ground.</summary>
    public Dictionary<Vector2i, ShoreTile> Terrain = new();

    /// <summary>Where each kind of prop and each kind of decal goes, in the order of the location's lists.</summary>
    public List<List<Vector2i>> PropTiles = new();

    public List<List<Vector2i>> DecalTiles = new();

    public sealed record Options(int Rocks, int TreeClumps, int TreeSingles, string Shore, IReadOnlyList<(int Count, int Clump)> Props, IReadOnlyList<(int Count, int Clump)> Decals);

    public static LocationLayout Generate(int seed, Vector2i size, IReadOnlyList<StructureInfo> pool, float density, Options options)
    {
        var rng = new Random(seed);
        var layout = new LocationLayout();
        layout.BuildShore(rng, size, options.Shore);
        if (pool.Count == 0)
            return layout;

        // About one structure in 700 tiles at the normal density
        var target = Math.Max(1, (int) (size.X * size.Y / 700f * density));

        // Small structures are more common than big ones
        var weights = pool.Select(s => 400.0 / Math.Max(40, s.Width * s.Height)).ToArray();
        var total = weights.Sum();

        var failures = 0;
        while (layout.Structures.Count < target && failures < 400)
        {
            var pick = rng.NextDouble() * total;
            var index = 0;
            while (index < weights.Length - 1 && pick > weights[index])
            {
                pick -= weights[index];
                index++;
            }

            var info = pool[index];
            var turns = rng.Next(4);
            var w = turns % 2 == 0 ? info.Width : info.Height;
            var h = turns % 2 == 0 ? info.Height : info.Width;

            var maxX = size.X - Margin - w;
            var maxY = size.Y - Margin - h;
            if (maxX < Margin || maxY < Margin)
            {
                failures++;
                continue;
            }

            var min = new Vector2i(rng.Next(Margin, maxX + 1), rng.Next(Margin, maxY + 1));
            var placement = new StructurePlacement(info, turns, min, w, h);
            if (layout.Structures.Any(other => Overlaps(placement, other)) || layout.TouchesShore(placement))
            {
                failures++;
                continue;
            }

            layout.Structures.Add(placement);
            failures = 0;
        }

        layout.BuildRoads(rng, size);
        layout.Scatter(rng, size, options);
        return layout;
    }

    /// <summary>
    ///     Rock formations and trees in the free ground: away from the structures and the roads, and not on top of each other.
    ///     Formations grow tile by tile from a random one, so they are uneven blobs and ridges, not squares.
    /// </summary>
    private void Scatter(Random rng, Vector2i size, Options options)
    {
        var rockCount = options.Rocks;
        var treeClumps = options.TreeClumps;
        var treeSingles = options.TreeSingles;
        var blocked = new HashSet<Vector2i>();
        foreach (var s in Structures)
        {
            for (var x = s.Min.X - 2; x <= s.Max.X + 2; x++)
            {
                for (var y = s.Min.Y - 2; y <= s.Max.Y + 2; y++)
                {
                    blocked.Add(new Vector2i(x, y));
                }
            }
        }

        foreach (var road in Roads)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    blocked.Add(new Vector2i(road.X + dx, road.Y + dy));
                }
            }
        }

        bool Free(Vector2i t) => t.X >= Margin && t.Y >= Margin && t.X < size.X - Margin && t.Y < size.Y - Margin && !blocked.Contains(t) && !Terrain.ContainsKey(t);
        var dirs = new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };

        // Rock formations: a blob or a ridge, a few tiles to a few dozen, with a free tile around it
        for (var i = 0; i < rockCount; i++)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var start = new Vector2i(rng.Next(Margin, size.X - Margin), rng.Next(Margin, size.Y - Margin));
                if (!Free(start))
                    continue;

                var want = rng.Next(4, 46);
                var ridge = rng.Next(3) == 0; // a ridge keeps growing the same way, a blob in any way
                var heading = dirs[rng.Next(4)];
                var tiles = new HashSet<Vector2i> { start };
                var tip = start;
                for (var step = 0; step < want * 6 && tiles.Count < want; step++)
                {
                    var from = ridge && rng.Next(3) != 0 ? tip : tiles.ElementAt(rng.Next(tiles.Count));
                    var next = from + (ridge && rng.Next(4) != 0 ? heading : dirs[rng.Next(4)]);
                    if (!Free(next) || tiles.Contains(next))
                        continue;

                    tiles.Add(next);
                    tip = next;
                }

                if (tiles.Count < 3)
                    continue;

                foreach (var t in tiles)
                {
                    Rocks.Add(t);
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            blocked.Add(new Vector2i(t.X + dx, t.Y + dy));
                        }
                    }
                }

                break;
            }
        }

        // Trees in clumps, and a few alone; never closer than two tiles to another tree
        var treeSet = new HashSet<Vector2i>();
        bool TryTree(Vector2i t)
        {
            if (!Free(t))
                return false;

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (treeSet.Contains(new Vector2i(t.X + dx, t.Y + dy)))
                        return false;
                }
            }

            treeSet.Add(t);
            Trees.Add(t);
            return true;
        }

        for (var c = 0; c < treeClumps; c++)
        {
            var center = new Vector2i(rng.Next(Margin, size.X - Margin), rng.Next(Margin, size.Y - Margin));
            var radius = rng.Next(4, 11);
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (dx * dx + dy * dy > radius * radius || rng.NextDouble() > 0.28)
                        continue;

                    TryTree(new Vector2i(center.X + dx, center.Y + dy));
                }
            }
        }

        for (var i = 0; i < treeSingles; i++)
        {
            TryTree(new Vector2i(rng.Next(Margin, size.X - Margin), rng.Next(Margin, size.Y - Margin)));
        }

        // Props keep clear of trees and rocks and of each other; decals may lie close together
        foreach (var t in treeSet)
        {
            blocked.Add(t);
        }

        foreach (var (count, clump) in options.Props)
        {
            PropTiles.Add(PlaceMany(rng, size, count, clump, 2, Free, blocked));
        }

        foreach (var (count, clump) in options.Decals)
        {
            DecalTiles.Add(PlaceMany(rng, size, count, clump, 0, Free, null));
        }
    }

    private static List<Vector2i> PlaceMany(Random rng, Vector2i size, int count, int clump, int spacing, Func<Vector2i, bool> free, HashSet<Vector2i>? blocked)
    {
        var result = new List<Vector2i>();
        var taken = new HashSet<Vector2i>();
        bool Clear(Vector2i t)
        {
            if (!free(t) || taken.Contains(t))
                return false;

            for (var dx = -spacing; dx <= spacing; dx++)
            {
                for (var dy = -spacing; dy <= spacing; dy++)
                {
                    if (taken.Contains(new Vector2i(t.X + dx, t.Y + dy)))
                        return false;
                }
            }

            return true;
        }

        for (var i = 0; i < count; i++)
        {
            var center = new Vector2i(rng.Next(Margin, size.X - Margin), rng.Next(Margin, size.Y - Margin));
            if (clump <= 0)
            {
                if (Clear(center))
                {
                    taken.Add(center);
                    result.Add(center);
                }

                continue;
            }

            for (var dx = -clump; dx <= clump; dx++)
            {
                for (var dy = -clump; dy <= clump; dy++)
                {
                    var t = new Vector2i(center.X + dx, center.Y + dy);
                    if (dx * dx + dy * dy > clump * clump || rng.NextDouble() > 0.3 || !Clear(t))
                        continue;

                    taken.Add(t);
                    result.Add(t);
                }
            }
        }

        return result;
    }

    // -- Shore ----------------------------------------------------------------------------------------------------

    public enum ShoreTile : byte
    {
        Sand,
        Water,
        Ice,
    }

    /// <summary>One side of the map is a shore, with a wavy edge: sand and shallow water, or ice.</summary>
    private void BuildShore(Random rng, Vector2i size, string kind)
    {
        if (kind is not ("water" or "ice"))
            return;

        var side = rng.Next(4);
        var length = side % 2 == 0 ? size.X : size.Y;
        var depthBase = kind == "ice" ? 16.0 : 15.0;
        var p1 = rng.NextDouble() * 6.28;
        var p2 = rng.NextDouble() * 6.28;
        var p3 = rng.NextDouble() * 6.28;

        for (var t = 0; t < length; t++)
        {
            var depth = depthBase + 4.0 * Math.Sin(t * 0.06 + p1) + 2.0 * Math.Sin(t * 0.19 + p2);
            var sandWidth = 4.0 + 1.5 * Math.Sin(t * 0.11 + p3);
            for (var d = 0; d < depth; d++)
            {
                var tile = side switch
                {
                    0 => new Vector2i(t, d),
                    1 => new Vector2i(t, size.Y - 1 - d),
                    2 => new Vector2i(d, t),
                    _ => new Vector2i(size.X - 1 - d, t),
                };

                if (kind == "ice")
                    Terrain[tile] = ShoreTile.Ice;
                else
                    Terrain[tile] = d < depth - sandWidth ? ShoreTile.Water : ShoreTile.Sand;
            }
        }
    }

    private bool TouchesShore(StructurePlacement p)
    {
        if (Terrain.Count == 0)
            return false;

        for (var x = p.Min.X - 2; x <= p.Max.X + 2; x++)
        {
            for (var y = p.Min.Y - 2; y <= p.Max.Y + 2; y++)
            {
                if (Terrain.ContainsKey(new Vector2i(x, y)))
                    return true;
            }
        }

        return false;
    }

    private static bool Overlaps(StructurePlacement a, StructurePlacement b)
    {
        return a.Min.X - Gap <= b.Max.X && a.Max.X + Gap >= b.Min.X
            && a.Min.Y - Gap <= b.Max.Y && a.Max.Y + Gap >= b.Min.Y;
    }

    /// <summary>Joins every structure to the nearest one already joined (the cheapest tree), with roads three tiles wide.</summary>
    private void BuildRoads(Random rng, Vector2i size)
    {
        if (Structures.Count < 2)
            return;

        var joined = new List<int> { 0 };
        var rest = Enumerable.Range(1, Structures.Count - 1).ToList();
        while (rest.Count > 0)
        {
            var best = (From: -1, To: -1, Cost: double.MaxValue);
            foreach (var a in joined)
            {
                foreach (var b in rest)
                {
                    var cost = (Structures[a].Center - Structures[b].Center).Length;
                    if (cost < best.Cost)
                        best = (a, b, cost);
                }
            }

            Road(Structures[best.From].Center, Structures[best.To].Center, rng.Next(2) == 0, size);
            joined.Add(best.To);
            rest.Remove(best.To);
        }
    }

    private void Road(Vector2i from, Vector2i to, bool horizontalFirst, Vector2i size)
    {
        var corner = horizontalFirst ? new Vector2i(to.X, from.Y) : new Vector2i(from.X, to.Y);
        Band(from, corner, size);
        Band(corner, to, size);
    }

    private void Band(Vector2i a, Vector2i b, Vector2i size)
    {
        for (var x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
        {
            for (var y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var tile = new Vector2i(x + dx, y + dy);
                        if (!Terrain.ContainsKey(tile) && tile.X >= Margin - 2 && tile.Y >= Margin - 2 && tile.X < size.X - Margin + 2 && tile.Y < size.Y - Margin + 2)
                            Roads.Add(tile);
                    }
                }
            }
        }
    }
}
