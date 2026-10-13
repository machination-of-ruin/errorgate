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

    public static LocationLayout Generate(int seed, Vector2i size, IReadOnlyList<StructureInfo> pool, float density)
    {
        var rng = new Random(seed);
        var layout = new LocationLayout();
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
            if (layout.Structures.Any(other => Overlaps(placement, other)))
            {
                failures++;
                continue;
            }

            layout.Structures.Add(placement);
            failures = 0;
        }

        layout.BuildRoads(rng, size);
        return layout;
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
                        if (tile.X >= Margin - 2 && tile.Y >= Margin - 2 && tile.X < size.X - Margin + 2 && tile.Y < size.Y - Margin + 2)
                            Roads.Add(tile);
                    }
                }
            }
        }
    }
}
