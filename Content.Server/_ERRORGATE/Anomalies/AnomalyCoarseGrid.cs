using System.Numerics;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     A coarse copy of a grid (square cells of several tiles) used to find which ground is reachable and
///     to trace routes quickly. A cell is walkable if most of its tiles are free floor.
/// </summary>
public sealed class AnomalyCoarseGrid
{
    private static readonly (int X, int Y, float Cost)[] Neighbours =
    {
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.4142f), (1, -1, 1.4142f), (-1, 1, 1.4142f), (-1, -1, 1.4142f),
    };

    public readonly int CellSize;

    /// <summary>
    ///     Tile coordinates of the corner of cell (0, 0).
    /// </summary>
    public readonly Vector2i Origin;

    public readonly int Width;
    public readonly int Height;

    /// <summary>Floor tiles in each cell.</summary>
    public readonly int[] Floor;

    /// <summary>Floor tiles in each cell with something solid on them.</summary>
    public readonly int[] Blocked;

    public readonly bool[] Walkable;
    public bool[] Reachable;

    // Path search scratch space, reused between searches
    private readonly float[] _cost;
    private readonly int[] _from;
    private readonly int[] _stamp;
    private int _currentStamp;

    public AnomalyCoarseGrid(int cellSize, Vector2i minTile, Vector2i maxTile)
    {
        CellSize = Math.Max(1, cellSize);
        Origin = minTile;
        Width = (maxTile.X - minTile.X) / CellSize + 1;
        Height = (maxTile.Y - minTile.Y) / CellSize + 1;

        var count = Width * Height;
        Floor = new int[count];
        Blocked = new int[count];
        Walkable = new bool[count];
        Reachable = new bool[count];
        _cost = new float[count];
        _from = new int[count];
        _stamp = new int[count];
    }

    public int Count => Width * Height;

    public bool TryCellOf(Vector2i tile, out int index)
    {
        var x = (tile.X - Origin.X) / CellSize;
        var y = (tile.Y - Origin.Y) / CellSize;

        if (tile.X < Origin.X || tile.Y < Origin.Y || x >= Width || y >= Height)
        {
            index = -1;
            return false;
        }

        index = y * Width + x;
        return true;
    }

    public bool TryCellOf(Vector2 position, out int index)
    {
        return TryCellOf(new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y)), out index);
    }

    /// <summary>
    ///     The middle of a cell, in tile coordinates.
    /// </summary>
    public Vector2 CellCenter(int index)
    {
        var x = index % Width;
        var y = index / Width;
        return new Vector2(Origin.X + (x + 0.5f) * CellSize, Origin.Y + (y + 0.5f) * CellSize);
    }

    /// <summary>
    ///     Decides which cells are walkable once <see cref="Floor"/> and <see cref="Blocked"/> are filled in.
    /// </summary>
    public void Finish(float minFreeFraction)
    {
        var area = CellSize * CellSize;
        for (var i = 0; i < Walkable.Length; i++)
        {
            var free = Floor[i] - Blocked[i];
            Walkable[i] = free > 0 && free >= area * minFreeFraction;
        }
    }

    /// <summary>
    ///     The walkable cell closest to <paramref name="index"/>, looking at most <paramref name="maxRing"/> cells out.
    /// </summary>
    public bool TryNearestWalkable(int index, int maxRing, out int result)
    {
        if (index >= 0 && Walkable[index])
        {
            result = index;
            return true;
        }

        result = -1;
        if (index < 0)
            return false;

        var cx = index % Width;
        var cy = index / Width;

        for (var ring = 1; ring <= maxRing; ring++)
        {
            var best = -1;
            var bestDistance = float.MaxValue;

            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                        continue;

                    var x = cx + dx;
                    var y = cy + dy;
                    if (x < 0 || y < 0 || x >= Width || y >= Height || !Walkable[y * Width + x])
                        continue;

                    var distance = dx * dx + dy * dy;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = y * Width + x;
                    }
                }
            }

            if (best >= 0)
            {
                result = best;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Marks every cell connected to the start cells as <see cref="Reachable"/>.
    /// </summary>
    /// <returns>How many cells are reachable.</returns>
    public int FloodFill(IEnumerable<int> starts)
    {
        Reachable = new bool[Walkable.Length];
        var queue = new Queue<int>();

        foreach (var start in starts)
        {
            if (start >= 0 && Walkable[start] && !Reachable[start])
            {
                Reachable[start] = true;
                queue.Enqueue(start);
            }
        }

        var reached = queue.Count;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var cx = current % Width;
            var cy = current / Width;

            for (var i = 0; i < 4; i++)
            {
                var x = cx + Neighbours[i].X;
                var y = cy + Neighbours[i].Y;
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    continue;

                var next = y * Width + x;
                if (!Walkable[next] || Reachable[next])
                    continue;

                Reachable[next] = true;
                reached++;
                queue.Enqueue(next);
            }
        }

        return reached;
    }

    /// <summary>
    ///     A* over the reachable cells.
    /// </summary>
    /// <returns>The cells from start to goal, or null if there is no way.</returns>
    public List<int>? FindPath(int start, int goal)
    {
        if (start < 0 || goal < 0 || !Reachable[start] || !Reachable[goal])
            return null;

        _currentStamp++;
        var goalX = goal % Width;
        var goalY = goal / Width;

        var open = new PriorityQueue<int, float>();
        _stamp[start] = _currentStamp;
        _cost[start] = 0f;
        _from[start] = -1;
        open.Enqueue(start, 0f);

        while (open.TryDequeue(out var current, out _))
        {
            if (current == goal)
            {
                var path = new List<int>();
                for (var cell = goal; cell >= 0; cell = _from[cell])
                {
                    path.Add(cell);
                }

                path.Reverse();
                return path;
            }

            var cx = current % Width;
            var cy = current / Width;

            foreach (var (dx, dy, step) in Neighbours)
            {
                var x = cx + dx;
                var y = cy + dy;
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    continue;

                var next = y * Width + x;
                if (!Reachable[next])
                    continue;

                var cost = _cost[current] + step;
                if (_stamp[next] == _currentStamp && cost >= _cost[next])
                    continue;

                _stamp[next] = _currentStamp;
                _cost[next] = cost;
                _from[next] = current;

                var hx = x - goalX;
                var hy = y - goalY;
                open.Enqueue(next, cost + MathF.Sqrt(hx * hx + hy * hy));
            }
        }

        return null;
    }
}
