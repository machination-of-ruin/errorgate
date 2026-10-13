using System.Linq;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     The plan of a location, in plain data: the streets, the lots along them with their buildings and fences, the shore, the rocks and
///     the trees. The same seed gives the same plan. Nothing is spawned here. The streets and lots are in LocationLayout.Streets.cs.
/// </summary>
public sealed partial class LocationLayout
{
    /// <summary>Tiles kept clear along the border (the border walls are inside it).</summary>
    public const int Margin = 6;

    public List<StructurePlacement> Structures = new();

    /// <summary>Road tiles.</summary>
    public HashSet<Vector2i> Roads = new();

    /// <summary>Walkways: the sidewalks of the city streets and the paths from a door to the street.</summary>
    public HashSet<Vector2i> Walks = new();

    /// <summary>The pieces of fence around the lots.</summary>
    public List<FencePiece> Fences = new();

    /// <summary>Tiles of indestructible rock, in formations.</summary>
    public HashSet<Vector2i> Rocks = new();

    /// <summary>Where trees stand.</summary>
    public List<Vector2i> Trees = new();

    /// <summary>What the edge of the map is on its shore side: sand, water or ice. Tiles not in the dictionary are plain ground.</summary>
    public Dictionary<Vector2i, ShoreTile> Terrain = new();

    /// <summary>Where each kind of prop and each kind of decal goes, in the order of the location's lists.</summary>
    public List<List<Vector2i>> PropTiles = new();

    public List<List<Vector2i>> DecalTiles = new();

    /// <summary>Places for mob spawners on open ground.</summary>
    public List<Vector2i> MobTiles = new();

    /// <summary>Spawn points of the clearing where players come into the world.</summary>
    public List<Vector2i> SpawnTiles = new();

    public sealed record Options(string Style, string Shape, int Rocks, int TreeClumps, int TreeSingles, string Shore, IReadOnlyList<(int Count, int Clump)> Props, IReadOnlyList<(int Count, int Clump)> Decals, IReadOnlyList<string> Gates, int MobCount, bool SpawnZone);

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
        foreach (var tile in _occupied)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    blocked.Add(new Vector2i(tile.X + dx, tile.Y + dy));
                }
            }
        }

        foreach (var road in Roads.Concat(Walks))
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    blocked.Add(new Vector2i(road.X + dx, road.Y + dy));
                }
            }
        }

        bool Free(Vector2i t) => Dist(t) >= Margin && !blocked.Contains(t) && !Terrain.ContainsKey(t);
        var dirs = new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };

        // Rock formations: a blob or a ridge, a few tiles to a few dozen, with a free tile around it
        for (var i = 0; i < rockCount; i++)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var start = RandomTile(rng);
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
            var center = RandomTile(rng);
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
            TryTree(RandomTile(rng));
        }

        // Props keep clear of trees and rocks and of each other; decals may lie close together
        foreach (var t in treeSet)
        {
            blocked.Add(t);
        }

        foreach (var (count, clump) in options.Props)
        {
            PropTiles.Add(PlaceMany(rng, count, clump, 2, Free));
        }

        foreach (var (count, clump) in options.Decals)
        {
            DecalTiles.Add(PlaceMany(rng, count, clump, 0, Free));
        }

        // Mobs stand apart on the open ground
        MobTiles = PlaceMany(rng, options.MobCount, 0, 14, Free);
    }

    private List<Vector2i> PlaceMany(Random rng, int count, int clump, int spacing, Func<Vector2i, bool> free)
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
            var center = RandomTile(rng);
            if (clump <= 0)
            {
                // A single one: a few tries, the ground is not all free
                for (var attempt = 0; attempt < 40 && !Clear(center); attempt++)
                {
                    center = RandomTile(rng);
                }

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

    /// <summary>One side of the map is a shore, with a wavy edge: sand and shallow water, or ice. It follows the edge of the shape.</summary>
    private void BuildShore(Random rng, string kind)
    {
        if (kind is not ("water" or "ice"))
            return;

        var side = rng.Next(4);
        var length = side % 2 == 0 ? Bounds.X : Bounds.Y;
        var depthBase = kind == "ice" ? 16.0 : 15.0;
        var p1 = rng.NextDouble() * 6.28;
        var p2 = rng.NextDouble() * 6.28;
        var p3 = rng.NextDouble() * 6.28;

        for (var t = 0; t < length; t++)
        {
            // The outermost playable tile of this column or row on the shore side
            var start = -1;
            var steps = side % 2 == 0 ? Bounds.Y : Bounds.X;
            for (var k = 0; k < steps; k++)
            {
                var from = side == 0 || side == 2 ? k : steps - 1 - k;
                var probe = side % 2 == 0 ? new Vector2i(t, from) : new Vector2i(from, t);
                if (InShape(probe))
                {
                    start = from;
                    break;
                }
            }

            if (start < 0)
                continue;

            var depth = depthBase + 4.0 * Math.Sin(t * 0.06 + p1) + 2.0 * Math.Sin(t * 0.19 + p2);
            var sandWidth = 4.0 + 1.5 * Math.Sin(t * 0.11 + p3);
            for (var d = 0; d < depth; d++)
            {
                // side 0 is the low y edge, 1 the high y edge, 2 the low x edge, 3 the high x edge
                var tile = side switch
                {
                    0 => new Vector2i(t, start + d),
                    1 => new Vector2i(t, start - d),
                    2 => new Vector2i(start + d, t),
                    _ => new Vector2i(start - d, t),
                };

                if (!InShape(tile))
                    continue;

                if (kind == "ice")
                    Terrain[tile] = ShoreTile.Ice;
                else
                    Terrain[tile] = d < depth - sandWidth ? ShoreTile.Water : ShoreTile.Sand;
            }
        }
    }

    private bool TouchesShore(int minX, int minY, int maxX, int maxY)
    {
        if (Terrain.Count == 0)
            return false;

        for (var x = minX - 2; x <= maxX + 2; x++)
        {
            for (var y = minY - 2; y <= maxY + 2; y++)
            {
                if (Terrain.ContainsKey(new Vector2i(x, y)))
                    return true;
            }
        }

        return false;
    }
}
