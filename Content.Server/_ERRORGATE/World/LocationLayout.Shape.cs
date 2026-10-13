using System.Linq;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.World;

public sealed partial class LocationLayout
{
    /// <summary>The box that holds the playable shape. For a rectangle it is the size of the location.</summary>
    public Vector2i Bounds;

    private bool[,] _in = new bool[0, 0];
    private byte[,] _dist = new byte[0, 0];

    /// <summary>Tiles inside the shape.</summary>
    public IEnumerable<Vector2i> ShapeTiles()
    {
        for (var x = 0; x < Bounds.X; x++)
        {
            for (var y = 0; y < Bounds.Y; y++)
            {
                if (_in[x, y])
                    yield return new Vector2i(x, y);
            }
        }
    }

    public bool InShape(Vector2i t) => t.X >= 0 && t.Y >= 0 && t.X < Bounds.X && t.Y < Bounds.Y && _in[t.X, t.Y];

    /// <summary>How many tiles it is to the edge of the shape (1 on the outermost playable tile, 0 outside it).</summary>
    public int Dist(Vector2i t) => InShape(t) ? _dist[t.X, t.Y] : 0;

    /// <summary>A random tile of the box (it may be outside the shape; callers check).</summary>
    private Vector2i RandomTile(Random rng) => new(rng.Next(0, Bounds.X), rng.Next(0, Bounds.Y));

    /// <summary>
    ///     The shape of the location. A rectangle, or an organic blob with the same area as the nominal size: an ellipse of a random
    ///     squashing whose edge is bent by smooth noise, with the size found by search, then the biggest piece kept and the holes
    ///     filled. The box that holds it is bigger than the nominal size.
    /// </summary>
    private void BuildShape(Random rng, Vector2i size, string kind)
    {
        if (kind != "organic")
        {
            Bounds = size;
            _in = new bool[size.X, size.Y];
            for (var x = 0; x < size.X; x++)
            {
                for (var y = 0; y < size.Y; y++)
                {
                    _in[x, y] = true;
                }
            }

            ComputeDistances();
            return;
        }

        var area = size.X * size.Y;
        var box = new Vector2i((int) (size.X * 1.5), (int) (size.Y * 1.5));
        Bounds = box;
        var aspect = 0.8 + rng.NextDouble() * 0.4;
        var rx = Math.Sqrt(area / Math.PI * aspect);
        var ry = Math.Sqrt(area / Math.PI / aspect);
        var cx = box.X / 2.0;
        var cy = box.Y / 2.0;

        var coarse = new ValueNoise(rng, box, 30);
        var fine = new ValueNoise(rng, box, 13);

        var field = new double[box.X, box.Y];
        for (var x = 0; x < box.X; x++)
        {
            for (var y = 0; y < box.Y; y++)
            {
                var nx = (x + 0.5 - cx) / rx;
                var ny = (y + 0.5 - cy) / ry;
                field[x, y] = Math.Sqrt(nx * nx + ny * ny) + 0.26 * (0.7 * coarse.At(x, y) + 0.3 * fine.At(x, y));
            }
        }

        // The threshold that gives the wanted area
        double lo = 0.2, hi = 2.5;
        for (var i = 0; i < 26; i++)
        {
            var mid = (lo + hi) / 2;
            var count = 0;
            for (var x = 0; x < box.X; x++)
            {
                for (var y = 0; y < box.Y; y++)
                {
                    if (field[x, y] <= mid && x > 1 && y > 1 && x < box.X - 2 && y < box.Y - 2)
                        count++;
                }
            }

            if (count < area)
                lo = mid;
            else
                hi = mid;
        }

        _in = new bool[box.X, box.Y];
        for (var x = 2; x < box.X - 2; x++)
        {
            for (var y = 2; y < box.Y - 2; y++)
            {
                _in[x, y] = field[x, y] <= hi;
            }
        }

        KeepBiggestPiece();
        FillHoles();
        ComputeDistances();
    }

    private static readonly Vector2i[] Four = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private void KeepBiggestPiece()
    {
        var seen = new bool[Bounds.X, Bounds.Y];
        List<Vector2i>? best = null;
        for (var x = 0; x < Bounds.X; x++)
        {
            for (var y = 0; y < Bounds.Y; y++)
            {
                if (!_in[x, y] || seen[x, y])
                    continue;

                var piece = new List<Vector2i>();
                var stack = new Stack<Vector2i>();
                stack.Push(new Vector2i(x, y));
                seen[x, y] = true;
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    piece.Add(c);
                    foreach (var d in Four)
                    {
                        var n = c + d;
                        if (n.X < 0 || n.Y < 0 || n.X >= Bounds.X || n.Y >= Bounds.Y || !_in[n.X, n.Y] || seen[n.X, n.Y])
                            continue;

                        seen[n.X, n.Y] = true;
                        stack.Push(n);
                    }
                }

                if (best == null || piece.Count > best.Count)
                    best = piece;
            }
        }

        _in = new bool[Bounds.X, Bounds.Y];
        if (best != null)
        {
            foreach (var t in best)
            {
                _in[t.X, t.Y] = true;
            }
        }
    }

    /// <summary>Anything not playable that cannot be reached from the edge of the box is a hole, and becomes playable.</summary>
    private void FillHoles()
    {
        var outside = new bool[Bounds.X, Bounds.Y];
        var stack = new Stack<Vector2i>();
        for (var x = 0; x < Bounds.X; x++)
        {
            foreach (var y in new[] { 0, Bounds.Y - 1 })
            {
                if (!_in[x, y] && !outside[x, y])
                {
                    outside[x, y] = true;
                    stack.Push(new Vector2i(x, y));
                }
            }
        }

        for (var y = 0; y < Bounds.Y; y++)
        {
            foreach (var x in new[] { 0, Bounds.X - 1 })
            {
                if (!_in[x, y] && !outside[x, y])
                {
                    outside[x, y] = true;
                    stack.Push(new Vector2i(x, y));
                }
            }
        }

        while (stack.Count > 0)
        {
            var c = stack.Pop();
            foreach (var d in Four)
            {
                var n = c + d;
                if (n.X < 0 || n.Y < 0 || n.X >= Bounds.X || n.Y >= Bounds.Y || _in[n.X, n.Y] || outside[n.X, n.Y])
                    continue;

                outside[n.X, n.Y] = true;
                stack.Push(n);
            }
        }

        for (var x = 0; x < Bounds.X; x++)
        {
            for (var y = 0; y < Bounds.Y; y++)
            {
                if (!_in[x, y] && !outside[x, y])
                    _in[x, y] = true;
            }
        }
    }

    /// <summary>The distance to the edge of the shape in tiles, with diagonal steps (so it is a square, not a diamond).</summary>
    private void ComputeDistances()
    {
        _dist = new byte[Bounds.X, Bounds.Y];
        var queue = new Queue<Vector2i>();
        for (var x = 0; x < Bounds.X; x++)
        {
            for (var y = 0; y < Bounds.Y; y++)
            {
                if (!_in[x, y])
                    continue;

                // A playable tile next to something that is not playable (or the edge of the box) is at distance 1
                var edge = false;
                for (var dx = -1; dx <= 1 && !edge; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= Bounds.X || ny >= Bounds.Y || !_in[nx, ny])
                        {
                            edge = true;
                            break;
                        }
                    }
                }

                if (edge)
                {
                    _dist[x, y] = 1;
                    queue.Enqueue(new Vector2i(x, y));
                }
            }
        }

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            var next = (byte) Math.Min(250, _dist[c.X, c.Y] + 1);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var nx = c.X + dx;
                    var ny = c.Y + dy;
                    if (nx < 0 || ny < 0 || nx >= Bounds.X || ny >= Bounds.Y || !_in[nx, ny] || _dist[nx, ny] != 0)
                        continue;

                    _dist[nx, ny] = next;
                    queue.Enqueue(new Vector2i(nx, ny));
                }
            }
        }
    }

    /// <summary>Smooth random values on a lattice, blended between the points; the same rng gives the same noise.</summary>
    private sealed class ValueNoise
    {
        private readonly double[,] _values;
        private readonly int _spacing;

        public ValueNoise(Random rng, Vector2i box, int spacing)
        {
            _spacing = spacing;
            _values = new double[box.X / spacing + 3, box.Y / spacing + 3];
            for (var x = 0; x < _values.GetLength(0); x++)
            {
                for (var y = 0; y < _values.GetLength(1); y++)
                {
                    _values[x, y] = rng.NextDouble() * 2 - 1;
                }
            }
        }

        public double At(int x, int y)
        {
            var fx = (double) x / _spacing;
            var fy = (double) y / _spacing;
            var ix = (int) fx;
            var iy = (int) fy;
            var tx = Smooth(fx - ix);
            var ty = Smooth(fy - iy);
            var a = _values[ix, iy] + (_values[ix + 1, iy] - _values[ix, iy]) * tx;
            var b = _values[ix, iy + 1] + (_values[ix + 1, iy + 1] - _values[ix, iy + 1]) * tx;
            return a + (b - a) * ty;
        }

        private static double Smooth(double t) => t * t * (3 - 2 * t);
    }
}
