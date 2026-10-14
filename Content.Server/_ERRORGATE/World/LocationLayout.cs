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

    /// <summary>Wrecks and barricades in the middle of the wide avenues: pairs of tiles, never closing the road.</summary>
    public List<Vector2i> RoadblockTiles = new();

    /// <summary>The roadblock tiles whose barricade lies north to south (the street runs east to west), so it is turned a quarter.</summary>
    public HashSet<Vector2i> RoadblockVertical = new();

    /// <summary>Spawn points of the clearing where players come into the world.</summary>
    public List<Vector2i> SpawnTiles = new();

    public sealed record Options(string Style, string Shape, int Rocks, int TreeClumps, int TreeSingles, string Shore, IReadOnlyList<(int Count, int Clump)> Props, IReadOnlyList<(int Count, int Clump)> Decals, IReadOnlyList<string> Gates, int MobCount, bool SpawnZone, int Roadblocks = 0);

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

        // Roadblocks close a street completely: the whole width of road and sidewalk, one or two tiles deep
        if (options.Roadblocks > 0)
        {
            var roadList = Roads.OrderBy(t => t.X).ThenBy(t => t.Y).ToList();
            bool IsStreet(Vector2i c) => Roads.Contains(c) || Walks.Contains(c);
            int Run(Vector2i c, Vector2i d)
            {
                var n = 1;
                for (var k = 1; k < 20 && IsStreet(c + d * k); k++) n++;
                for (var k = 1; k < 20 && IsStreet(c - d * k); k++) n++;
                return n;
            }

            var blocks = 0;
            var centres = new List<Vector2i>();
            for (var attempt = 0; attempt < options.Roadblocks * 60 && blocks < options.Roadblocks && roadList.Count > 0; attempt++)
            {
                var t = roadList[rng.Next(roadList.Count)];
                var wx = Run(t, new Vector2i(1, 0));
                var wy = Run(t, new Vector2i(0, 1));
                if (Math.Abs(wx - wy) < 4 || Math.Min(wx, wy) > 11)
                    continue; // a crossing, not a straight street

                // The street runs along the longer run; the block lies across it
                var across = wx < wy ? new Vector2i(1, 0) : new Vector2i(0, 1);
                var along = wx < wy ? new Vector2i(0, 1) : new Vector2i(1, 0);
                if (Dist(t) < 10 || centres.Any(c => Math.Abs(c.X - t.X) < 8 && Math.Abs(c.Y - t.Y) < 8)
                    || GatePlaces.Values.Any(g => Math.Abs(g.Arrival.X - t.X) < 10 && Math.Abs(g.Arrival.Y - t.Y) < 10))
                {
                    continue;
                }

                var depth = 1; // a single row
                var section = new List<Vector2i>();
                for (var d = 0; d < depth; d++)
                {
                    var start = t + along * d;
                    if (!IsStreet(start))
                        break;

                    var lo = 0;
                    while (IsStreet(start - across * (lo + 1)) && lo < 12) lo++;
                    var hi = 0;
                    while (IsStreet(start + across * (hi + 1)) && hi < 12) hi++;
                    for (var k = -lo; k <= hi; k++)
                    {
                        section.Add(start + across * k);
                    }
                }

                if (section.Count == 0)
                    continue;

                RoadblockTiles.AddRange(section);
                if (across.Y != 0)
                    RoadblockVertical.UnionWith(section);

                centres.Add(t);
                blocks++;
            }
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

    /// <summary>
    ///     A stretch of the map's edge is a shore: sand and shallow water, or ice. The band is measured from the edge of the shape
    ///     (the distance to the edge), so it follows every bend of the edge, with a wavy inner line and ends that taper off.
    /// </summary>
    private void BuildShore(Random rng, string kind)
    {
        if (kind is not ("water" or "ice"))
            return;

        // The middle of the shape, and the direction the shore faces
        double sumX = 0, sumY = 0;
        var count = 0;
        for (var x = 0; x < Bounds.X; x++)
        {
            for (var y = 0; y < Bounds.Y; y++)
            {
                if (!_in[x, y])
                    continue;

                sumX += x;
                sumY += y;
                count++;
            }
        }

        if (count == 0)
            return;

        var centre = (X: sumX / count, Y: sumY / count);
        var facing = rng.NextDouble() * Math.PI * 2;
        var halfArc = 1.05 + rng.NextDouble() * 0.25;
        var radius = Math.Sqrt(count / Math.PI);
        var depthBase = kind == "ice" ? 16.0 : 15.0;
        var p1 = rng.NextDouble() * 6.28;
        var p2 = rng.NextDouble() * 6.28;
        var p3 = rng.NextDouble() * 6.28;

        for (var x = 0; x < Bounds.X; x++)
        {
            for (var y = 0; y < Bounds.Y; y++)
            {
                var tile = new Vector2i(x, y);
                if (!InShape(tile))
                    continue;

                var edge = Dist(tile) - 1; // 0 on the outermost tile
                if (edge > 30)
                    continue;

                var angle = Math.Atan2(y - centre.Y, x - centre.X);
                var off = Math.Abs(Math.IEEERemainder(angle - facing, Math.PI * 2));
                if (off >= halfArc)
                    continue;

                // Full depth in the middle of the arc, fading to nothing at its ends
                var taper = Math.Sqrt(Math.Clamp(1.0 - off / halfArc, 0.0, 1.0));
                var along = angle * radius;
                var depth = (depthBase + 4.0 * Math.Sin(along * 0.06 + p1) + 2.0 * Math.Sin(along * 0.19 + p2)) * taper;
                var sandWidth = (4.0 + 1.5 * Math.Sin(along * 0.11 + p3)) * Math.Max(taper, 0.35);
                if (edge >= depth)
                    continue;

                if (kind == "ice")
                    Terrain[tile] = ShoreTile.Ice;
                else
                    Terrain[tile] = edge < depth - sandWidth ? ShoreTile.Water : ShoreTile.Sand;
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
