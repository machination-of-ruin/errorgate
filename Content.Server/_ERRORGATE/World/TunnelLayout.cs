using System.Linq;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     The plan of a tunnel: a small location between two bigger ones. A winding corridor three tiles wide with a gate at each end,
///     a rest stop or two, rubble that narrows the way (never closes it), crates for cover, lights and the places for mobs. Plain data,
///     the same seed gives the same tunnel.
/// </summary>
public sealed class TunnelLayout
{
    public Vector2i Bounds;

    public HashSet<Vector2i> Floor = new();

    /// <summary>The rock around the floor.</summary>
    public HashSet<Vector2i> Walls = new();

    public Dictionary<string, GatePlacement> Gates = new();

    public HashSet<Vector2i> Rubble = new();

    public List<Vector2i> Cover = new();

    public List<Vector2i> MobPoints = new();

    public List<Vector2i> Lights = new();

    /// <summary>Radioactive barrels: at the east end and along the way.</summary>
    public List<Vector2i> Radiation = new();

    /// <summary>The middle line of the corridor from the first gate to the last.</summary>
    public List<Vector2i> Path = new();

    private static readonly Vector2i[] Eight =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    public static TunnelLayout Generate(int seed, Vector2i size, IReadOnlyList<string> gateIds, int mobCount, int radiation = 0)
    {
        var rng = new Random(seed);
        var l = new TunnelLayout { Bounds = size };

        // The middle line: east, with jogs up and down
        var y = size.Y / 2;
        var x = 5;
        l.Path.Add(new Vector2i(x, y));
        while (x < size.X - 12)
        {
            var run = rng.Next(10, 24);
            for (var k = 0; k < run && x < size.X - 7; k++)
            {
                x++;
                l.Path.Add(new Vector2i(x, y));
            }

            if (x >= size.X - 14)
                break;

            var jog = rng.Next(5, 13) * (rng.Next(2) == 0 ? 1 : -1);
            if (y + jog < 9 || y + jog > size.Y - 10)
                jog = -jog;

            var step = Math.Sign(jog);
            for (var k = 0; k < Math.Abs(jog); k++)
            {
                y += step;
                l.Path.Add(new Vector2i(x, y));
            }
        }

        // Three wide
        foreach (var c in l.Path)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    l.Floor.Add(new Vector2i(c.X + dx, c.Y + dy));
                }
            }
        }

        // Rest stops: a wider room on the way, away from the ends
        var stops = rng.Next(1, 3);
        for (var s = 0; s < stops; s++)
        {
            var at = l.Path[rng.Next(l.Path.Count / 4, l.Path.Count * 3 / 4)];
            for (var dx = -5; dx <= 5; dx++)
            {
                for (var dy = -3; dy <= 3; dy++)
                {
                    var t = new Vector2i(at.X + dx, at.Y + dy);
                    if (t.X >= 4 && t.Y >= 4 && t.X < size.X - 4 && t.Y < size.Y - 4)
                        l.Floor.Add(t);
                }
            }
        }

        foreach (var t in l.Floor)
        {
            foreach (var d in Eight)
            {
                var n = t + d;
                if (!l.Floor.Contains(n))
                    l.Walls.Add(n);
            }
        }

        // The gates: the first at the west end, the last at the east end, each facing out of the tunnel
        if (gateIds.Count > 0)
            l.Gates[gateIds[0]] = new GatePlacement(l.Path[0], l.Path[3], 3);

        if (gateIds.Count > 1)
            l.Gates[gateIds[1]] = new GatePlacement(l.Path[^1], l.Path[^4], 1);

        // Rubble that narrows the corridor on one or two lanes of three, never all of them
        var rubbleSpots = rng.Next(4, 8);
        var used = new List<int>();
        for (var i = 0; i < rubbleSpots; i++)
        {
            var index = rng.Next(12, l.Path.Count - 12);
            if (used.Any(u => Math.Abs(u - index) < 7))
                continue;

            used.Add(index);
            var here = l.Path[index];
            var ahead = l.Path[index + 1];
            var along = ahead - here;
            if (along == Vector2i.Zero)
                continue;

            var across = new Vector2i(along.Y != 0 ? 1 : 0, along.X != 0 ? 1 : 0);
            var lanes = new List<int> { -1, 0, 1 };
            lanes = lanes.OrderBy(_ => rng.Next()).Take(rng.Next(1, 3)).ToList();
            foreach (var lane in lanes)
            {
                var t = here + across * lane;
                // Not on the way in or out, and not in a rest stop (those are wide)
                if (l.Floor.Contains(t + across * 2) || l.Floor.Contains(t - across * 2))
                    continue;

                l.Rubble.Add(t);
                if (rng.Next(2) == 0)
                    l.Rubble.Add(t + along);
            }
        }

        // Crates at the edge of the corridor, and the lights
        for (var i = 20; i < l.Path.Count - 20; i += rng.Next(14, 26))
        {
            var here = l.Path[i];
            var along = l.Path[i + 1] - here;
            if (along == Vector2i.Zero)
                continue;

            var across = new Vector2i(along.Y != 0 ? 1 : 0, along.X != 0 ? 1 : 0);
            var t = here + across * (rng.Next(2) == 0 ? 1 : -1);
            if (!l.Rubble.Contains(t))
                l.Cover.Add(t);
        }

        for (var i = 10; i < l.Path.Count - 8; i += 12)
        {
            var here = l.Path[i];
            var along = l.Path[i + 1] - here;
            var across = new Vector2i(along.Y != 0 ? 1 : 0, along.X != 0 ? 1 : 0);
            var t = here + across;
            if (!l.Rubble.Contains(t) && !l.Cover.Contains(t))
                l.Lights.Add(t);
        }

        // Radioactive barrels: two at the east end, four tiles from the gate (the arrival tile and the gate are inside their reach), and the
        // rest along the corridor, alternating sides
        if (radiation > 0 && l.Path.Count > 30)
        {
            var taken = new HashSet<Vector2i>(l.Rubble);
            taken.UnionWith(l.Cover);
            taken.UnionWith(l.Lights);
            Vector2i Across(int index)
            {
                var along = l.Path[index + 1] - l.Path[index];
                return new Vector2i(along.Y != 0 ? 1 : 0, along.X != 0 ? 1 : 0);
            }

            var end = l.Path.Count - 5;
            foreach (var side in new[] { 1, -1 })
            {
                var t = l.Path[end] + Across(end) * side;
                if (taken.Add(t))
                    l.Radiation.Add(t);
            }

            var rest = Math.Max(0, radiation - 2);
            for (var i = 0; i < rest; i++)
            {
                var index = 14 + (int) ((i + 0.5) * (l.Path.Count - 40) / Math.Max(1, rest));
                index = Math.Clamp(index, 10, l.Path.Count - 14);
                var t = l.Path[index] + Across(index) * (i % 2 == 0 ? 1 : -1);
                if (l.Floor.Contains(t) && taken.Add(t))
                    l.Radiation.Add(t);
            }
        }

        // Mobs spread along the way, well away from the gates
        if (mobCount > 0)
        {
            var span = l.Path.Count - 40;
            for (var m = 0; m < mobCount && span > 0; m++)
            {
                var index = 20 + (int) ((m + 0.5) * span / mobCount) + rng.Next(-4, 5);
                index = Math.Clamp(index, 20, l.Path.Count - 21);
                l.MobPoints.Add(l.Path[index]);
            }
        }

        return l;
    }
}
