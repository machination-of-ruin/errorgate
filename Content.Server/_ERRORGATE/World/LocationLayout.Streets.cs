using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     A structure file, the size of its footprint (walls to walls), the corner of the footprint in the grid's own tiles, the side its
///     front door is on (0 south, 1 east, 2 north, 3 west) and the tile of that door, and what the structure is for (house,
///     outbuilding, civic, shop, industrial). Measured once.
/// </summary>
public sealed record StructureInfo(string Path, int Width, int Height, Vector2i LocalMin, int FrontSide, Vector2i FrontDoor, string Role, float Weight);

/// <summary>
///     A structure placed in a location: its footprint rectangle after the turns, where its grid origin goes, the side its front
///     faces now and the tile of its front door now.
/// </summary>
public sealed record StructurePlacement(StructureInfo Info, int Turns, Vector2i Min, int Width, int Height, Vector2 Origin, int Front, Vector2i Door)
{
    public Vector2i Max => new(Min.X + Width - 1, Min.Y + Height - 1);

    public static StructurePlacement Create(StructureInfo info, int turns, Vector2i min)
    {
        turns = (turns % 4 + 4) % 4;
        var w = turns % 2 == 0 ? info.Width : info.Height;
        var h = turns % 2 == 0 ? info.Height : info.Width;

        // Where the grid origin goes so that the turned footprint starts at min
        var a = LocationLayout.RotateVector(new Vector2(info.LocalMin.X, info.LocalMin.Y), turns);
        var b = LocationLayout.RotateVector(new Vector2(info.LocalMin.X + info.Width, info.LocalMin.Y + info.Height), turns);
        var rotatedMin = new Vector2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
        var origin = new Vector2(min.X, min.Y) - rotatedMin;

        var c = LocationLayout.RotateVector(new Vector2(info.FrontDoor.X + 0.5f, info.FrontDoor.Y + 0.5f), turns) + origin;
        var door = new Vector2i((int) MathF.Floor(c.X), (int) MathF.Floor(c.Y));
        return new StructurePlacement(info, turns, min, w, h, origin, (info.FrontSide + turns) % 4, door);
    }
}

public enum FenceKind : byte
{
    Straight,
    Corner,
    Gate,
}

/// <summary>A piece of fence on a tile, turned by <see cref="Quarter"/> quarter turns (counter-clockwise).</summary>
public sealed record FencePiece(Vector2i Tile, FenceKind Kind, int Quarter);

public sealed partial class LocationLayout
{
    private Vector2i _size;
    private readonly HashSet<Vector2i> _occupied = new();
    private readonly List<Segment> _segments = new();
    private readonly List<(Vector2i Door, int Front)> _doorPaths = new();

    private sealed record Segment(Vector2i A, Vector2i B, bool Horizontal, int Width, bool Walk, bool Main);

    private enum LotKind
    {
        Homestead,
        House,
        Civic,
        Shop,
        Industrial,
        FencedIndustrial,
        Outbuilding,
    }

    /// <summary>0 south, 1 east, 2 north, 3 west.</summary>
    public static Vector2i Dir(int side) => (side % 4) switch
    {
        0 => new Vector2i(0, -1),
        1 => new Vector2i(1, 0),
        2 => new Vector2i(0, 1),
        _ => new Vector2i(-1, 0),
    };

    public static Vector2 RotateVector(Vector2 v, int turns)
    {
        return (turns % 4) switch
        {
            1 => new Vector2(-v.Y, v.X),
            2 => new Vector2(-v.X, -v.Y),
            3 => new Vector2(v.Y, -v.X),
            _ => v,
        };
    }

    public static LocationLayout Generate(int seed, Vector2i size, IReadOnlyList<StructureInfo> pool, float density, Options options)
    {
        var rng = new Random(seed);
        var layout = new LocationLayout { _size = size };
        layout.BuildShore(rng, size, options.Shore);

        if (options.Style == "city")
            layout.BuildCityStreets(rng, size);
        else
            layout.BuildVillageStreets(rng, size);

        layout.Walks.ExceptWith(layout.Roads);
        if (pool.Count > 0)
            layout.FillFrontage(rng, size, pool, density, options.Style);

        layout.Scatter(rng, size, options);
        return layout;
    }

    // -- Streets --------------------------------------------------------------------------------------------------

    private bool Placeable(Vector2i t) => t.X >= 3 && t.Y >= 3 && t.X < _size.X - 3 && t.Y < _size.Y - 3 && !Terrain.ContainsKey(t);

    /// <summary>A straight road from one point to another along an axis, with sidewalks if asked for. Width is odd.</summary>
    private void AddRoad(Vector2i a, Vector2i b, int width, bool walk, bool main)
    {
        var horizontal = a.Y == b.Y;
        var lo = horizontal ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
        var hi = horizontal ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
        var c = horizontal ? a.Y : a.X;
        var half = width / 2;

        for (var t = lo; t <= hi; t++)
        {
            for (var o = -half; o <= half; o++)
            {
                var tile = horizontal ? new Vector2i(t, c + o) : new Vector2i(c + o, t);
                if (Placeable(tile))
                    Roads.Add(tile);
            }

            if (!walk)
                continue;

            foreach (var o in new[] { -(half + 1), half + 1 })
            {
                var tile = horizontal ? new Vector2i(t, c + o) : new Vector2i(c + o, t);
                if (Placeable(tile))
                    Walks.Add(tile);
            }
        }

        _segments.Add(new Segment(horizontal ? new Vector2i(lo, c) : new Vector2i(c, lo), horizontal ? new Vector2i(hi, c) : new Vector2i(c, hi), horizontal, width, walk, main));
    }

    /// <summary>Whether the band along a line, <paramref name="clear"/> tiles to either side, has no shore in it.</summary>
    private bool LineFree(bool horizontal, int c, int clear)
    {
        var length = horizontal ? _size.X : _size.Y;
        for (var t = 0; t < length; t++)
        {
            for (var o = -clear; o <= clear; o++)
            {
                var tile = horizontal ? new Vector2i(t, c + o) : new Vector2i(c + o, t);
                if (Terrain.ContainsKey(tile))
                    return false;
            }
        }

        return true;
    }

    /// <summary>A main street across the village, with lanes branching off it on one side or both.</summary>
    private void BuildVillageStreets(Random rng, Vector2i size)
    {
        var horizontal = rng.Next(2) == 0;
        var length = horizontal ? size.X : size.Y;
        var cross = horizontal ? size.Y : size.X;

        // As near the middle as the shore allows
        var start = cross / 2 + rng.Next(-cross / 6, cross / 6 + 1);
        var pos = start;
        for (var step = 0; step < cross; step++)
        {
            var candidate = start + (step % 2 == 0 ? step / 2 : -(step + 1) / 2);
            if (candidate < Margin + 12 || candidate > cross - Margin - 12)
                continue;

            if (LineFree(horizontal, candidate, 12))
            {
                pos = candidate;
                break;
            }
        }

        var lo = Margin + 2;
        var hi = length - Margin - 3;
        Vector2i P(int along, int across) => horizontal ? new Vector2i(along, across) : new Vector2i(across, along);
        AddRoad(P(lo, pos), P(hi, pos), 3, false, true);

        var t = lo + 16 + rng.Next(0, 12);
        var up = rng.Next(2) == 0;
        while (t < hi - 14)
        {
            var len = rng.Next(28, 64);
            var both = rng.NextDouble() < 0.3;
            foreach (var dir in both ? new[] { 1, -1 } : new[] { up ? 1 : -1 })
            {
                var end = Math.Clamp(pos + dir * len, Margin + 2, cross - Margin - 3);
                AddRoad(P(t, pos), P(t, end), 3, false, false);
            }

            up = !up;
            t += rng.Next(30, 52);
        }
    }

    /// <summary>A grid of streets with sidewalks; the middle ones are avenues.</summary>
    private void BuildCityStreets(Random rng, Vector2i size)
    {
        var columns = 3 + rng.Next(2);
        var rows = 3 + rng.Next(2);
        for (var i = 1; i <= columns; i++)
        {
            var x = Margin + (size.X - 2 * Margin) * i / (columns + 1) + rng.Next(-3, 4);
            var avenue = i == (columns + 1) / 2;
            AddRoad(new Vector2i(x, Margin + 2), new Vector2i(x, size.Y - Margin - 3), avenue ? 5 : 3, true, avenue);
        }

        for (var i = 1; i <= rows; i++)
        {
            var y = Margin + (size.Y - 2 * Margin) * i / (rows + 1) + rng.Next(-3, 4);
            var avenue = i == (rows + 1) / 2;
            AddRoad(new Vector2i(Margin + 2, y), new Vector2i(size.X - Margin - 3, y), avenue ? 5 : 3, true, avenue);
        }
    }

    // -- Lots -----------------------------------------------------------------------------------------------------

    private static StructureInfo Pick(Random rng, List<StructureInfo> list)
    {
        var total = list.Sum(s => (double) s.Weight);
        var roll = rng.NextDouble() * total;
        foreach (var s in list)
        {
            roll -= s.Weight;
            if (roll <= 0)
                return s;
        }

        return list[^1];
    }

    /// <summary>Quarter turns that make the structure's front face the side <paramref name="front"/>.</summary>
    private static int TurnsFor(StructureInfo info, int front) => (front - info.FrontSide + 8) % 4;

    /// <summary>The length along the street and the depth away from it of a structure facing the side <paramref name="front"/>.</summary>
    private static (int Along, int Depth) Dims(StructureInfo info, int front)
    {
        var turns = TurnsFor(info, front);
        var w = turns % 2 == 0 ? info.Width : info.Height;
        var h = turns % 2 == 0 ? info.Height : info.Width;
        return front % 2 == 0 ? (w, h) : (h, w);
    }

    /// <summary>
    ///     A rectangle in the lot's own terms (along the street from the cursor, and depth away from the first free line) as world
    ///     tiles. <paramref name="front"/> is the side the street is on, seen from the lot.
    /// </summary>
    private static (int MinX, int MinY, int MaxX, int MaxY) LocalRect(int front, int cursor, int edge, int a0, int d0, int aw, int dh)
    {
        return front switch
        {
            0 => (cursor + a0, edge + d0, cursor + a0 + aw - 1, edge + d0 + dh - 1),
            2 => (cursor + a0, edge - (d0 + dh - 1), cursor + a0 + aw - 1, edge - d0),
            3 => (edge + d0, cursor + a0, edge + d0 + dh - 1, cursor + a0 + aw - 1),
            _ => (edge - (d0 + dh - 1), cursor + a0, edge - d0, cursor + a0 + aw - 1),
        };
    }

    private bool Fits((int MinX, int MinY, int MaxX, int MaxY) r, int gap)
    {
        if (r.MinX - gap < Margin || r.MinY - gap < Margin || r.MaxX + gap >= _size.X - Margin || r.MaxY + gap >= _size.Y - Margin)
            return false;

        if (TouchesShore(r.MinX, r.MinY, r.MaxX, r.MaxY))
            return false;

        for (var x = r.MinX - gap; x <= r.MaxX + gap; x++)
        {
            for (var y = r.MinY - gap; y <= r.MaxY + gap; y++)
            {
                var t = new Vector2i(x, y);
                if (_occupied.Contains(t) || Roads.Contains(t) || Walks.Contains(t))
                    return false;
            }
        }

        return true;
    }

    private void Occupy((int MinX, int MinY, int MaxX, int MaxY) r)
    {
        for (var x = r.MinX; x <= r.MaxX; x++)
        {
            for (var y = r.MinY; y <= r.MaxY; y++)
            {
                _occupied.Add(new Vector2i(x, y));
            }
        }
    }

    private LotKind Roll(Random rng, string style, bool main)
    {
        var r = rng.NextDouble();
        if (style == "city")
        {
            if (main)
                return r < 0.28 ? LotKind.Shop : r < 0.4 ? LotKind.Civic : r < 0.62 ? LotKind.House : r < 0.8 ? LotKind.FencedIndustrial : LotKind.Industrial;

            return r < 0.15 ? LotKind.Shop : r < 0.25 ? LotKind.Civic : r < 0.55 ? LotKind.House : r < 0.75 ? LotKind.FencedIndustrial : r < 0.9 ? LotKind.Industrial : LotKind.Outbuilding;
        }

        if (main)
            return r < 0.55 ? LotKind.Homestead : r < 0.7 ? LotKind.Civic : r < 0.82 ? LotKind.Shop : LotKind.House;

        return r < 0.75 ? LotKind.Homestead : r < 0.9 ? LotKind.House : LotKind.Outbuilding;
    }

    private void FillFrontage(Random rng, Vector2i size, IReadOnlyList<StructureInfo> pool, float density, string style)
    {
        var byRole = pool.GroupBy(i => i.Role).ToDictionary(g => g.Key, g => g.ToList());
        List<StructureInfo> Of(string role)
        {
            if (byRole.TryGetValue(role, out var list) && list.Count > 0)
                return list;

            return byRole.TryGetValue("house", out var houses) && houses.Count > 0 ? houses : pool.ToList();
        }

        var skip = Math.Clamp(0.12 + (1 - density) * 0.4, 0.02, 0.7);

        // The main streets first, so the buildings that matter get the best frontage
        foreach (var seg in _segments.OrderByDescending(s => s.Main).ThenBy(_ => rng.Next()))
        {
            foreach (var sign in new[] { 1, -1 })
            {
                var front = seg.Horizontal ? (sign > 0 ? 0 : 2) : (sign > 0 ? 3 : 1);
                var half = seg.Width / 2;
                var center = seg.Horizontal ? seg.A.Y : seg.A.X;
                var setback = style == "city" ? 1 : 3;
                var edge = center + sign * (half + (seg.Walk ? 1 : 0) + 1 + setback);
                var lo = (seg.Horizontal ? seg.A.X : seg.A.Y) + 2;
                var hi = (seg.Horizontal ? seg.B.X : seg.B.Y) - 2;

                var cursor = lo + rng.Next(0, 5);
                while (cursor < hi - 3)
                {
                    if (rng.NextDouble() < skip)
                    {
                        cursor += rng.Next(4, 9);
                        continue;
                    }

                    var used = 0;
                    for (var attempt = 0; attempt < 3 && used == 0; attempt++)
                        used = TryLot(rng, Roll(rng, style, seg.Main), front, edge, cursor, Of, style);

                    cursor += used > 0 ? used + rng.Next(1, 4) : 3;
                }
            }
        }

        // A path from every front door to the street
        foreach (var (door, front) in _doorPaths)
        {
            var step = Dir(front);
            var cur = door + step;
            for (var i = 0; i < 30; i++)
            {
                if (Roads.Contains(cur) || Walks.Contains(cur) || Terrain.ContainsKey(cur))
                    break;

                Walks.Add(cur);
                cur += step;
            }
        }

        Walks.ExceptWith(Roads);
    }

    /// <summary>
    ///     Tries to put one lot at the cursor. A homestead is a house and up to two small buildings inside a fence with a gate towards
    ///     the street; the others are a single building, a fenced one with a yard. Returns the length it takes along the street, or 0.
    /// </summary>
    private int TryLot(Random rng, LotKind kind, int front, int edge, int cursor, Func<string, List<StructureInfo>> of, string style)
    {
        var fenced = kind is LotKind.Homestead or LotKind.FencedIndustrial;
        var role = kind switch
        {
            LotKind.Civic => "civic",
            LotKind.Shop => "shop",
            LotKind.Industrial or LotKind.FencedIndustrial => "industrial",
            LotKind.Outbuilding => "outbuilding",
            _ => "house",
        };

        var main = Pick(rng, of(role));
        var (mainAlong, mainDepth) = Dims(main, front);

        // The other buildings of a homestead: one beside the house, one behind it
        StructureInfo? beside = null, behind = null;
        if (fenced)
        {
            var small = of("outbuilding");
            if (kind == LotKind.Homestead || rng.NextDouble() < 0.3)
            {
                if (rng.NextDouble() < 0.7)
                    beside = Pick(rng, small);

                if (rng.NextDouble() < 0.35)
                    behind = Pick(rng, small);
            }
        }

        // Everything in the lot's own terms: a is along the street, d is the depth away from it. The fence ring is the outer line of
        // the lot, so the buildings stand two tiles inside it
        const int inset = 2;
        int aw, dh;
        (int A, int D, StructureInfo Info)[] items;
        if (fenced)
        {
            var contentA = inset + mainAlong;
            var list = new List<(int, int, StructureInfo)> { (inset, inset, main) };
            var contentD = inset + mainDepth;
            if (beside != null)
            {
                var (al, de) = Dims(beside, front);
                list.Add((contentA + 2, inset, beside));
                contentA += 2 + al;
                contentD = Math.Max(contentD, inset + de);
            }

            if (behind != null)
            {
                var (al, de) = Dims(behind, front);
                list.Add((inset + Math.Max(0, (mainAlong - al) / 2), inset + mainDepth + 2, behind));
                contentA = Math.Max(contentA, inset + al);
                contentD = Math.Max(contentD, inset + mainDepth + 2 + de);
            }

            aw = contentA + 2;
            dh = contentD + 2;
            items = list.ToArray();
        }
        else
        {
            aw = mainAlong;
            dh = mainDepth;
            items = new[] { (0, 0, main) };
        }

        var lot = LocalRect(front, cursor, edge, 0, 0, aw, dh);
        if (!Fits(lot, fenced && style == "village" ? 2 : 1))
            return 0;

        Occupy(lot);
        StructurePlacement? house = null;
        foreach (var (a, d, info) in items)
        {
            var (al, de) = Dims(info, front);
            var r = LocalRect(front, cursor, edge, a, d, al, de);
            var placement = StructurePlacement.Create(info, TurnsFor(info, front), new Vector2i(r.MinX, r.MinY));
            Structures.Add(placement);
            house ??= placement;
        }

        if (house != null)
            _doorPaths.Add((house.Door, front));

        if (fenced && house != null)
            AddFence(lot, front, house.Door);

        return aw;
    }

    /// <summary>The fence around a lot: corners, straight pieces, and a gate on the side that faces the street, in line with the door.</summary>
    private void AddFence((int MinX, int MinY, int MaxX, int MaxY) r, int front, Vector2i door)
    {
        var gate = front switch
        {
            0 => new Vector2i(Math.Clamp(door.X, r.MinX + 1, r.MaxX - 1), r.MinY),
            2 => new Vector2i(Math.Clamp(door.X, r.MinX + 1, r.MaxX - 1), r.MaxY),
            3 => new Vector2i(r.MinX, Math.Clamp(door.Y, r.MinY + 1, r.MaxY - 1)),
            _ => new Vector2i(r.MaxX, Math.Clamp(door.Y, r.MinY + 1, r.MaxY - 1)),
        };

        // Corner pieces: the quarter says which two neighbours it joins. 0 north and west, 1 south and west, 2 south and east,
        // 3 north and east. Straight pieces: 0 runs north to south, 1 east to west. A gate runs the other way round from a straight
        // piece: 0 east to west, 1 north to south.
        for (var x = r.MinX; x <= r.MaxX; x++)
        {
            for (var y = r.MinY; y <= r.MaxY; y++)
            {
                var onX = x == r.MinX || x == r.MaxX;
                var onY = y == r.MinY || y == r.MaxY;
                if (!onX && !onY)
                    continue;

                var tile = new Vector2i(x, y);
                if (onX && onY)
                {
                    var quarter = (x == r.MinX, y == r.MinY) switch
                    {
                        (true, true) => 3,
                        (false, true) => 0,
                        (true, false) => 2,
                        _ => 1,
                    };

                    Fences.Add(new FencePiece(tile, FenceKind.Corner, quarter));
                }
                else if (tile == gate)
                {
                    Fences.Add(new FencePiece(tile, FenceKind.Gate, onY ? 0 : 1));
                }
                else
                {
                    Fences.Add(new FencePiece(tile, FenceKind.Straight, onY ? 1 : 0));
                }
            }
        }
    }
}
