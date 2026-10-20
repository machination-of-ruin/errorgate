using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     A structure file, the size of its footprint (walls to walls), the corner of the footprint in the grid's own tiles, the side its
///     front door is on (0 south, 1 east, 2 north, 3 west) and the tile of that door, and what the structure is for (house,
///     outbuilding, civic, shop, industrial). Measured once.
/// </summary>
public sealed record StructureInfo(string Path, int Width, int Height, Vector2i LocalMin, int FrontSide, Vector2i FrontDoor, string Role, float Weight, bool Unique = false, string Group = "", bool CutStreets = false, IReadOnlyList<string>? Mobs = null, int MobCount = 0);

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

/// <summary>A gate: where it stands, the tile people arrive on when they come through from the other side, and the side it faces.</summary>
public sealed record GatePlacement(Vector2i Tile, Vector2i Arrival, int Facing);

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

    /// <summary>Street lights (city) and the trees along the roads (village).</summary>
    public List<Vector2i> Posts = new();

    public List<Vector2i> StreetTrees = new();
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
        var layout = new LocationLayout();
        layout.BuildShape(rng, size, options.Shape);
        layout._size = layout.Bounds;
        layout.BuildShore(rng, options.Shore);
        layout.MarkNearShore();

        if (options.Style == "city")
            layout.BuildCityStreets(rng, size);
        else
            layout.BuildVillageStreets(rng, size);

        layout.MendJunctions();
        layout.PlaceGates(rng, options.Gates, options.BorderGates ?? new List<string>());
        layout.Walks.ExceptWith(layout.Roads);
        if (pool.Count > 0)
            layout.FillFrontage(rng, size, pool, density, options.Style, options.UniqueRules ?? new List<(string Group, int Min, int Max)>());

        if (options.SpawnZone)
            layout.PlaceSpawnZone(rng);

        layout.PlaceFeatures(rng, options.Landmarks ?? new List<(string Kind, int Count)>());
        layout.DressStreets(rng, options.Style);
        layout.Scatter(rng, size, options);
        return layout;
    }

    // -- Streets --------------------------------------------------------------------------------------------------

    private bool Placeable(Vector2i t) => Dist(t) >= 3 && !Terrain.ContainsKey(t);

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
                {
                    Roads.Add(tile);
                    if (main)
                        MainRoadTiles.Add(tile);
                }
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

    /// <summary>
    ///     Streets that stop just short of each other or meet without a corner are joined: where the ends of a horizontal and a vertical
    ///     street lie within three tiles of the square where they would cross, both are extended to cover it.
    /// </summary>
    private void MendJunctions()
    {
        var segments = _segments.ToList();
        var count = _segments.Count;
        foreach (var h in segments.Where(s => s.Horizontal))
        {
            foreach (var v in segments.Where(s => !s.Horizontal))
            {
                var hh = h.Width / 2;
                var vh = v.Width / 2;
                var rx0 = v.A.X - vh;
                var rx1 = v.A.X + vh;
                var ry0 = h.A.Y - hh;
                var ry1 = h.A.Y + hh;
                var hGap = h.B.X < rx0 ? rx0 - h.B.X : h.A.X > rx1 ? h.A.X - rx1 : 0;
                var vGap = v.B.Y < ry0 ? ry0 - v.B.Y : v.A.Y > ry1 ? v.A.Y - ry1 : 0;
                if (hGap > 3 || vGap > 3)
                    continue;

                var hCovers = h.A.X <= rx0 && h.B.X >= rx1;
                var vCovers = v.A.Y <= ry0 && v.B.Y >= ry1;
                if (hCovers && vCovers)
                    continue; // they cross

                if (h.B.X < rx1 && h.B.X >= rx0 - 3)
                    AddRoad(new Vector2i(h.B.X + 1, h.A.Y), new Vector2i(rx1, h.A.Y), h.Width, h.Walk, false);
                else if (h.A.X > rx0 && h.A.X <= rx1 + 3)
                    AddRoad(new Vector2i(rx0, h.A.Y), new Vector2i(h.A.X - 1, h.A.Y), h.Width, h.Walk, false);

                if (v.B.Y < ry1 && v.B.Y >= ry0 - 3)
                    AddRoad(new Vector2i(v.A.X, v.B.Y + 1), new Vector2i(v.A.X, ry1), v.Width, v.Walk, false);
                else if (v.A.Y > ry0 && v.A.Y <= ry1 + 3)
                    AddRoad(new Vector2i(v.A.X, ry0), new Vector2i(v.A.X, v.A.Y - 1), v.Width, v.Walk, false);
            }
        }

        // The pieces are joins, no lots along them
        _segments.RemoveRange(count, _segments.Count - count);
    }

    /// <summary>The tiles of the main street (and the way out to its gate): a different, harder surface.</summary>
    public HashSet<Vector2i> MainRoadTiles = new();

    private readonly HashSet<Vector2i> _nearShore = new();

    /// <summary>The tiles within eight of the shore, which streets keep away from.</summary>
    private void MarkNearShore()
    {
        foreach (var t in Terrain.Keys)
        {
            for (var dx = -8; dx <= 8; dx++)
            {
                for (var dy = -8; dy <= 8; dy++)
                {
                    _nearShore.Add(new Vector2i(t.X + dx, t.Y + dy));
                }
            }
        }
    }

    private static Vector2i At(bool horizontal, int along, int across) => horizontal ? new Vector2i(along, across) : new Vector2i(across, along);

    /// <summary>The stretches of a line (a row if horizontal, a column if not) that lie well inside the shape and clear of the shore.</summary>
    private List<(int Lo, int Hi)> Runs(bool horizontal, int across, int minLength)
    {
        var runs = new List<(int, int)>();
        var length = horizontal ? Bounds.X : Bounds.Y;
        var start = -1;
        for (var t = 0; t <= length; t++)
        {
            var ok = false;
            if (t < length)
            {
                var tile = At(horizontal, t, across);
                ok = Dist(tile) >= Margin + 2 && !_nearShore.Contains(tile);
            }

            if (ok && start < 0)
            {
                start = t;
            }
            else if (!ok && start >= 0)
            {
                if (t - start >= minLength)
                    runs.Add((start, t - 1));

                start = -1;
            }
        }

        return runs;
    }

    /// <summary>A main street across the village, with lanes branching off it on one side or both.</summary>
    private void BuildVillageStreets(Random rng, Vector2i size)
    {
        // The main street runs across the whole village, from the edge of the map to the edge: it is the line near the middle with the
        // longest free stretch, preferring one whose both ends reach the edge (not the shore), in either direction
        bool EdgeReached(bool h, int across, int end, int sign)
        {
            for (var k = 1; k <= 40; k++)
            {
                var tile = At(h, end + sign * k, across);
                if (Terrain.ContainsKey(tile) || _nearShore.Contains(tile))
                    return false;

                if (Dist(tile) <= 3)
                    return true;
            }

            return false;
        }

        var firstHorizontal = rng.Next(2) == 0;
        var horizontal = firstHorizontal;
        var best = (Across: 0, Run: (Lo: 0, Hi: -1));
        var bestScore = int.MinValue;
        foreach (var h in new[] { firstHorizontal, !firstHorizontal })
        {
            var crossLength = h ? Bounds.Y : Bounds.X;
            var window = crossLength / 5;
            for (var c = crossLength / 2 - window; c <= crossLength / 2 + window; c++)
            {
                foreach (var run in Runs(h, c, 30))
                {
                    var score = run.Hi - run.Lo + (EdgeReached(h, c, run.Lo, -1) && EdgeReached(h, c, run.Hi, 1) ? 1000 : 0);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = (c, run);
                        horizontal = h;
                    }
                }
            }
        }

        if (best.Run.Hi <= best.Run.Lo)
            return;

        var pos = best.Across;
        var lo = best.Run.Lo + 2;
        var hi = best.Run.Hi - 2;
        AddRoad(At(horizontal, lo, pos), At(horizontal, hi, pos), 3, false, true);

        var t = lo + 14 + rng.Next(0, 12);
        var up = rng.Next(2) == 0;
        var lanes = new List<(int T, int Dir, int End)>();
        while (t < hi - 12)
        {
            var len = rng.Next(28, 64);
            var both = rng.NextDouble() < 0.3;
            foreach (var dir in both ? new[] { 1, -1 } : new[] { up ? 1 : -1 })
            {
                // As far as the lane can go before it leaves the shape or meets the shore
                var end = pos;
                for (var k = 1; k <= len; k++)
                {
                    var tile = At(horizontal, t, pos + dir * k);
                    if (Dist(tile) < Margin + 1 || _nearShore.Contains(tile))
                        break;

                    end = pos + dir * k;
                }

                if (Math.Abs(end - pos) < 12)
                    continue;

                AddRoad(At(horizontal, t, pos), At(horizontal, t, end), 3, false, false);
                lanes.Add((t, dir, end));
            }

            up = !up;
            t += rng.Next(24, 38);
        }

        // A back lane runs beside the main street and joins the lanes that reach it, so the village has loops, not only dead ends
        foreach (var dir in new[] { 1, -1 })
        {
            if (rng.NextDouble() < 0.35)
                continue;

            var offset = rng.Next(28, 40);
            var reaching = lanes.Where(l => l.Dir == dir && Math.Abs(l.End - pos) >= offset + 2).ToList();
            if (reaching.Count < 2)
                continue;

            AddRoad(At(horizontal, reaching.Min(l => l.T), pos + dir * offset), At(horizontal, reaching.Max(l => l.T), pos + dir * offset), 3, false, false);
        }
    }

    /// <summary>A grid of streets with sidewalks; the middle ones are avenues. Streets exist only where the shape is.</summary>
    private void BuildCityStreets(Random rng, Vector2i size)
    {
        foreach (var horizontal in new[] { false, true })
        {
            var length = horizontal ? Bounds.Y : Bounds.X;
            var count = Math.Max(3, length / 44 - 1);
            for (var i = 1; i <= count; i++)
            {
                var across = length * i / (count + 1) + rng.Next(-3, 4);
                var avenue = i == (count + 1) / 2;
                foreach (var run in Runs(horizontal, across, 24))
                {
                    AddRoad(At(horizontal, run.Lo + 2, across), At(horizontal, run.Hi - 2, across), avenue ? 5 : 3, true, avenue);
                }
            }
        }
    }

    // -- The spawn points ----------------------------------------------------------------------------------------

    /// <summary>
    ///     Seven places near the edge of the map where people come into the world (Kuznetsk had seven too): each gets a round start point
    ///     and a late join point on the same tile. They are on open ground six to sixteen tiles from the edge, far from the gates and
    ///     from each other, and clear of roads, buildings and the shore.
    /// </summary>
    private void PlaceSpawnZone(Random rng)
    {
        const int Count = 7;

        bool Clear(Vector2i c)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dy = -2; dy <= 2; dy++)
                {
                    var t = new Vector2i(c.X + dx, c.Y + dy);
                    if (!InShape(t) || Terrain.ContainsKey(t) || _nearShore.Contains(t) || _occupied.Contains(t) || Roads.Contains(t) || Walks.Contains(t))
                        return false;
                }
            }

            return true;
        }

        var ground = new List<Vector2i>();
        for (var x = 0; x < Bounds.X; x++)
        {
            for (var y = 0; y < Bounds.Y; y++)
            {
                var c = new Vector2i(x, y);
                if (InShape(c) && Dist(c) >= 6 && Dist(c) <= 16 && Clear(c))
                    ground.Add(c);
            }
        }

        // Spread out: each next point is one of the few that lie farthest from those already taken. If the map has no room for seven
        // that far from the gates and from each other, the distances are lowered step by step
        List<Vector2i> Try(int fromGates, int apart, Random r)
        {
            var candidates = ground.Where(c => !GatePlaces.Values.Any(g => (g.Tile - c).Length < fromGates)).ToList();
            var chosen = new List<Vector2i>();
            while (chosen.Count < Count && candidates.Count > 0)
            {
                var ranked = chosen.Count == 0
                    ? candidates.OrderBy(_ => r.Next()).Take(1).ToList()
                    : candidates
                        .Select(c => (Tile: c, Gap: chosen.Min(p => (p - c).Length)))
                        .OrderByDescending(c => c.Gap)
                        .Take(12)
                        .Select(c => c.Tile)
                        .ToList();

                var pick = ranked[r.Next(ranked.Count)];
                chosen.Add(pick);
                candidates.RemoveAll(c => (c - pick).Length < apart);
            }

            return chosen;
        }

        var best = new List<Vector2i>();
        foreach (var (fromGates, apart) in new[] { (45, 30), (40, 24), (35, 18), (30, 14), (25, 10) })
        {
            best = Try(fromGates, apart, new Random(rng.Next()));
            if (best.Count >= Count)
                break;
        }

        foreach (var pick in best)
        {
            SpawnTiles.Add(pick);
            for (var dx = -3; dx <= 3; dx++)
            {
                for (var dy = -3; dy <= 3; dy++)
                {
                    _occupied.Add(new Vector2i(pick.X + dx, pick.Y + dy));
                }
            }
        }
    }

    // -- Gates ----------------------------------------------------------------------------------------------------

    /// <summary>
    ///     The plaza a gate stands on: a grid of its own, <c>f</c> tiles forward (toward the gate and the edge of the map, from -3 in
    ///     front of it to 1 behind) and <c>l</c> tiles to the side (-3 to 3). The gate is at f 0, l 0.
    /// </summary>
    public static IEnumerable<(Vector2i Tile, int F, int L)> PlazaTiles(GatePlacement gate)
    {
        for (var f = -3; f <= 1; f++)
        {
            for (var l = -3; l <= 3; l++)
            {
                yield return (PlazaTile(gate, f, l), f, l);
            }
        }
    }

    public static Vector2i PlazaTile(GatePlacement gate, int f, int l)
    {
        var d = Dir(gate.Facing);
        return gate.Tile + d * f + new Vector2i(-d.Y, d.X) * l;
    }

    /// <summary>Where each gate stands, and the road tile people arrive on.</summary>
    public Dictionary<string, GatePlacement> GatePlaces = new();

    /// <summary>
    ///     Puts the gates at the ends of the roads, far from each other, the main streets first. A gate stands five tiles beyond the end of
    ///     its road (the road is made longer to reach it) and keeps a clear square around it.
    /// </summary>
    private void PlaceGates(Random rng, IReadOnlyList<string> ids, IReadOnlyList<string> borderIds)
    {
        if (ids.Count == 0)
            return;

        var candidates = new List<(Vector2i End, int Dir, bool Main, Segment Seg)>();
        foreach (var seg in _segments)
        {
            candidates.Add((seg.A, seg.Horizontal ? 3 : 0, seg.Main, seg));
            candidates.Add((seg.B, seg.Horizontal ? 1 : 2, seg.Main, seg));
        }

        bool Fits((Vector2i End, int Dir, bool Main, Segment Seg) c)
        {
            var gate = new GatePlacement(c.End + Dir(c.Dir) * 5, c.End + Dir(c.Dir) * 2, c.Dir);
            foreach (var (t, _, _) in PlazaTiles(gate))
            {
                if (Dist(t) < 3 || Terrain.ContainsKey(t) || _occupied.Contains(t))
                    return false;
            }

            return true;
        }

        // A gate in the border: it stands where the border is (two tiles from the edge), the road runs out to its plaza, and the plaza
        // (the border rocks are left out there) is the opening in the wall. Null if the street end has no such place in front of it
        GatePlacement? BorderPlace((Vector2i End, int Dir, bool Main, Segment Seg) c)
        {
            for (var k = 5; k < 100; k++)
            {
                var tile = c.End + Dir(c.Dir) * k;
                if (Dist(tile) > 3)
                    continue;

                // The gate is on the third tile from the edge: the inner row of the border is cut away for its plaza, the outer row stays
                // whole behind it, so there is rock and no void behind the gate
                if (Dist(tile) != 3)
                    return null;

                var gate = new GatePlacement(tile, tile - Dir(c.Dir) * 3, c.Dir);
                foreach (var (t, _, _) in PlazaTiles(gate))
                {
                    if (!InShape(t) || Dist(t) < 2 || Terrain.ContainsKey(t) || _nearShore.Contains(t) || _occupied.Contains(t))
                        return null;
                }

                return gate;
            }

            return null;
        }

        var placed = new List<Vector2i>();
        foreach (var id in ids.OrderByDescending(i => borderIds.Contains(i)))
        {
            if (borderIds.Contains(id))
            {
                // The ends of the streets, and spurs that branch off the streets sideways every few tiles
                var borderCandidates = new List<(Vector2i End, int Dir, bool Main, Segment Seg)>(candidates);
                foreach (var seg in _segments)
                {
                    var lo = seg.Horizontal ? seg.A.X : seg.A.Y;
                    var hi = seg.Horizontal ? seg.B.X : seg.B.Y;
                    for (var s = lo + 6; s <= hi - 6; s += 4)
                    {
                        var at = seg.Horizontal ? new Vector2i(s, seg.A.Y) : new Vector2i(seg.A.X, s);
                        foreach (var d in seg.Horizontal ? new[] { 0, 2 } : new[] { 1, 3 })
                        {
                            borderCandidates.Add((at, d, seg.Main, seg));
                        }
                    }
                }

                var borderOptions = borderCandidates
                    .Select(c => (Candidate: c, Place: BorderPlace(c)))
                    .Where(c => c.Place != null)
                    .ToList();
                if (borderOptions.Count > 0)
                {
                    var pick = borderOptions
                        .OrderByDescending(c => (c.Candidate.Main ? 1000 : 0) + (placed.Count == 0 ? rng.Next(0, 100) : placed.Min(p => (p - c.Candidate.End).Length)))
                        .First();
                    var place = pick.Place!;
                    AddRoad(pick.Candidate.End, place.Tile - Dir(place.Facing) * 4, pick.Candidate.Seg.Width, pick.Candidate.Seg.Walk, pick.Candidate.Seg.Main);
                    _segments.RemoveAt(_segments.Count - 1);
                    GatePlaces[id] = place;
                    foreach (var (t, _, _) in PlazaTiles(place))
                    {
                        _occupied.Add(t);
                    }

                    placed.Add(pick.Candidate.End);
                    candidates.Remove(pick.Candidate);
                    continue;
                }
            }

            var options = candidates.Where(Fits).ToList();
            if (options.Count == 0)
                break;

            // The first gate at a main street end; the others as far from the gates already placed as the roads allow
            var best = options
                .OrderByDescending(c => (c.Main ? 1000 : 0) + (placed.Count == 0 ? rng.Next(0, 100) : placed.Min(p => (p - c.End).Length)))
                .First();

            var tile = best.End + Dir(best.Dir) * 5;
            var arrival = best.End + Dir(best.Dir) * 2;
            AddRoad(best.End, best.End + Dir(best.Dir) * 4, best.Seg.Width, best.Seg.Walk, best.Seg.Main);
            _segments.RemoveAt(_segments.Count - 1); // no lots along the stub

            GatePlaces[id] = new GatePlacement(tile, arrival, best.Dir);
            foreach (var (t, _, _) in PlazaTiles(GatePlaces[id]))
            {
                _occupied.Add(t);
            }

            placed.Add(best.End);
            candidates.Remove(best);
        }
    }

    // -- Dressing of the streets ------------------------------------------------------------------------------------

    private bool FreeGround(Vector2i t) => Placeable(t) && !_occupied.Contains(t) && !Roads.Contains(t) && !Walks.Contains(t);

    /// <summary>Street lights along the sidewalks of the city, and trees along the roads of the village, where nothing else stands.</summary>
    private void DressStreets(Random rng, string style)
    {
        foreach (var seg in _segments)
        {
            var half = seg.Width / 2;
            var center = seg.Horizontal ? seg.A.Y : seg.A.X;
            var lo = (seg.Horizontal ? seg.A.X : seg.A.Y) + 3;
            var hi = (seg.Horizontal ? seg.B.X : seg.B.Y) - 3;
            foreach (var sign in new[] { 1, -1 })
            {
                if (style == "city")
                {
                    // Right beyond the sidewalk, every fourteen tiles
                    for (var a = lo + rng.Next(0, 6); a < hi; a += 14)
                    {
                        var across = center + sign * (half + 2);
                        var tile = seg.Horizontal ? new Vector2i(a, across) : new Vector2i(across, a);
                        if (FreeGround(tile))
                            Posts.Add(tile);
                    }
                }
                else
                {
                    // Beside the road, every five to seven tiles, with gaps
                    var a = lo + rng.Next(0, 5);
                    while (a < hi)
                    {
                        var across = center + sign * (half + 1);
                        var tile = seg.Horizontal ? new Vector2i(a, across) : new Vector2i(across, a);
                        if (rng.NextDouble() < 0.7 && FreeGround(tile))
                            StreetTrees.Add(tile);

                        a += rng.Next(5, 8);
                    }
                }
            }
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

    /// <summary>
    ///     Puts a unique building where it fits once the lanes and sidewalks under it are taken away (never an avenue, never near the border,
    ///     the gates or the shore), turned so that its front door looks at a street within a few tiles. The streets are cut by it.
    /// </summary>
    private bool PlaceUniqueByCulling(Random rng, StructureInfo info)
    {
        // Which tiles are not available (outside the shape, near the border or the shore, taken, avenues), as sums, so a whole rectangle
        // can be asked about at once
        var sums = new int[Bounds.X + 1, Bounds.Y + 1];
        for (var x = 0; x < Bounds.X; x++)
        {
            for (var y = 0; y < Bounds.Y; y++)
            {
                var t = new Vector2i(x, y);
                var bad = !InShape(t) || Dist(t) < Margin + 2 || Terrain.ContainsKey(t) || _nearShore.Contains(t) || _occupied.Contains(t) || MainRoadTiles.Contains(t);
                sums[x + 1, y + 1] = sums[x, y + 1] + sums[x + 1, y] - sums[x, y] + (bad ? 1 : 0);
            }
        }

        bool Clear(int x0, int y0, int x1, int y1)
        {
            if (x0 < 0 || y0 < 0 || x1 >= Bounds.X || y1 >= Bounds.Y)
                return false;

            return sums[x1 + 1, y1 + 1] - sums[x0, y1 + 1] - sums[x1 + 1, y0] + sums[x0, y0] == 0;
        }

        // Every place and turn where it fits with three clear tiles around it, in random order
        var candidates = new List<(int Turns, Vector2i Min)>();
        for (var turns = 0; turns < 4; turns++)
        {
            var shape = StructurePlacement.Create(info, turns, Vector2i.Zero);
            for (var x = 3; x + shape.Width + 3 < Bounds.X; x += 2)
            {
                for (var y = 3; y + shape.Height + 3 < Bounds.Y; y += 2)
                {
                    if (Clear(x - 2, y - 2, x + shape.Width + 1, y + shape.Height + 1))
                        candidates.Add((turns, new Vector2i(x, y)));
                }
            }
        }

        foreach (var (turns, min) in candidates.OrderBy(_ => rng.Next()).Take(400))
        {
            var placement = StructurePlacement.Create(info, turns, min);
            var rect = (MinX: min.X, MinY: min.Y, MaxX: min.X + placement.Width - 1, MaxY: min.Y + placement.Height - 1);
            var cut = new List<Vector2i>();
            for (var x = rect.MinX - 2; x <= rect.MaxX + 2; x++)
            {
                for (var y = rect.MinY - 2; y <= rect.MaxY + 2; y++)
                {
                    var t = new Vector2i(x, y);
                    if (Roads.Contains(t) || Walks.Contains(t))
                        cut.Add(t);
                }
            }

            // The front door must have a street ahead of it, within reach of the path that is cut to it (not one of the tiles that are cut)
            var step = Dir(placement.Front);
            var cutSet = new HashSet<Vector2i>(cut);
            var reach = false;
            for (var k = 2; k <= 28 && !reach; k++)
            {
                var ahead = placement.Door + step * k;
                reach = (Roads.Contains(ahead) || Walks.Contains(ahead)) && !cutSet.Contains(ahead);
            }

            if (!reach)
                continue;

            foreach (var t in cut)
            {
                Roads.Remove(t);
                Walks.Remove(t);
            }

            Occupy(rect);
            Structures.Add(placement);
            _doorPaths.Add((placement.Door, placement.Front));
            return true;
        }

        return false;
    }

    /// <summary>Streets that a unique building cut off from the rest, and that are too short to matter, are taken away.</summary>
    private void PruneRoadFragments(int smallerThan)
    {
        var all = new HashSet<Vector2i>(Roads);
        all.UnionWith(Walks);
        var seen = new HashSet<Vector2i>();
        var stack = new Stack<Vector2i>();
        foreach (var start in all)
        {
            if (!seen.Add(start))
                continue;

            var part = new List<Vector2i> { start };
            stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                foreach (var d in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
                {
                    var n = c + d;
                    if (all.Contains(n) && seen.Add(n))
                    {
                        part.Add(n);
                        stack.Push(n);
                    }
                }
            }

            if (part.Count >= smallerThan || part.Any(t => MainRoadTiles.Contains(t)))
                continue;

            foreach (var t in part)
            {
                Roads.Remove(t);
                Walks.Remove(t);
            }
        }
    }

    private bool Fits((int MinX, int MinY, int MaxX, int MaxY) r, int gap)
    {
        if (TouchesShore(r.MinX, r.MinY, r.MaxX, r.MaxY))
            return false;

        for (var x = r.MinX - gap; x <= r.MaxX + gap; x++)
        {
            for (var y = r.MinY - gap; y <= r.MaxY + gap; y++)
            {
                var t = new Vector2i(x, y);
                if (Dist(t) < Margin || _occupied.Contains(t) || Roads.Contains(t) || Walks.Contains(t))
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

    private void FillFrontage(Random rng, Vector2i size, IReadOnlyList<StructureInfo> pool, float density, string style, IReadOnlyList<(string Group, int Min, int Max)> uniqueRules)
    {
        // The unique buildings are put out once each below; the random lots draw from the rest
        // Only some of each group of unique buildings go out (a block of flats or two, not all of them); the others are left out. The ones that
        // may cut the streets (the factories) are placed first, since they need the most room
        var allUniques = pool.Where(i => i.Unique).ToList();
        var uniques = new List<StructureInfo>();
        foreach (var rule in uniqueRules)
        {
            var members = allUniques.Where(u => u.Group == rule.Group).OrderBy(_ => rng.Next()).ToList();
            uniques.AddRange(members.Take(rng.Next(rule.Min, rule.Max + 1)));
        }

        uniques.AddRange(allUniques.Where(u => !uniqueRules.Any(r => r.Group == u.Group)));
        uniques = uniques.OrderByDescending(u => u.CutStreets).ThenBy(_ => rng.Next()).ToList();
        var ordinary = pool.Where(i => !i.Unique).ToList();
        if (ordinary.Count == 0)
            ordinary = pool.ToList();

        var byRole = ordinary.GroupBy(i => i.Role).ToDictionary(g => g.Key, g => g.ToList());
        List<StructureInfo> Of(string role)
        {
            if (byRole.TryGetValue(role, out var list) && list.Count > 0)
                return list;

            return byRole.TryGetValue("house", out var houses) && houses.Count > 0 ? houses : ordinary;
        }

        var skip = Math.Clamp(0.12 + (1 - density) * 0.4, 0.02, 0.7);

        // The unique buildings first: each goes on the first lot along a street where it fits, the streets and the places in random order
        foreach (var landmark in uniques.OrderBy(_ => rng.Next()))
        {
            var kind = landmark.Role == "industrial" ? LotKind.FencedIndustrial : landmark.Role == "shop" ? LotKind.Shop : LotKind.Civic;
            var done = false;
            foreach (var seg in _segments.OrderByDescending(s => s.Main).ThenBy(_ => rng.Next()))
            {
                foreach (var sign in new[] { 1, -1 }.OrderBy(_ => rng.Next()))
                {
                    var front = seg.Horizontal ? (sign > 0 ? 0 : 2) : (sign > 0 ? 3 : 1);
                    var center = seg.Horizontal ? seg.A.Y : seg.A.X;
                    var edge = center + sign * (seg.Width / 2 + (seg.Walk ? 1 : 0) + 1 + (style == "city" ? 1 : 3));
                    var lo = (seg.Horizontal ? seg.A.X : seg.A.Y) + 2;
                    var hi = (seg.Horizontal ? seg.B.X : seg.B.Y) - 2;
                    for (var cursor = lo + rng.Next(0, 4); cursor < hi - 3 && !done; cursor += 4)
                    {
                        done = TryLot(rng, kind, front, edge, cursor, Of, style, landmark) > 0;
                    }

                    if (done)
                        break;
                }

                if (done)
                    break;
            }

            // Too big for any lot along a street (a factory, a block of flats): it goes where there is room and the streets that ran
            // through that place are cut by it
            if (!done && landmark.CutStreets)
                PlaceUniqueByCulling(rng, landmark);
        }

        if (uniques.Count > 0)
            PruneRoadFragments(40);

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
    private int TryLot(Random rng, LotKind kind, int front, int edge, int cursor, Func<string, List<StructureInfo>> of, string style, StructureInfo? forced = null)
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

        var main = forced ?? Pick(rng, of(role));
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
