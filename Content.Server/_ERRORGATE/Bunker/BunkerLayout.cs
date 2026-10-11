using System.Linq;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.Bunker;

public enum BunkerRoomType : byte
{
    /// <summary>Right behind the vestibule.</summary>
    Decon,
    Barracks,
    Mess,
    Control,
    Generator,
    Reactor,
    Medical,
    Storage,
    AmmoDepot,
    Armory,
    Archive,
    /// <summary>A small junction room.</summary>
    Shaft,
}

/// <summary>A door in the wall ring of a room: the room, its tile and the way out of the room.</summary>
public sealed record BunkerDoor(int Room, Vector2i Tile, Vector2i Dir);

/// <summary>A room: its floor is X0..X1 by Y0..Y1 (inclusive), the wall ring is one tile outside.</summary>
public sealed class BunkerRoom
{
    public BunkerRoomType Type;
    public int X0, Y0, X1, Y1;

    /// <summary>The walls of this room are grey brick instead of concrete.</summary>
    public bool BrickWalls;

    public Vector2i Center => new((X0 + X1) / 2, (Y0 + Y1) / 2);

    public bool Contains(Vector2i tile) => tile.X >= X0 && tile.X <= X1 && tile.Y >= Y0 && tile.Y <= Y1;

    /// <summary>The floor and the wall ring around it.</summary>
    public bool InRing(Vector2i tile) => tile.X >= X0 - 1 && tile.X <= X1 + 1 && tile.Y >= Y0 - 1 && tile.Y <= Y1 + 1;
}

/// <summary>
///     A generated bunker: rooms with doors, joined by corridors three tiles wide. The entrance room and the small junction rooms
///     (hubs) are joined to each other, every other room is a dead end with one door (a few have two). The same seed gives the same
///     bunker. Pure data, nothing is spawned here.
/// </summary>
public sealed class BunkerLayout
{
    public const int CellSize = 20;
    public const int Columns = 5;
    public const int Rows = 4;

    /// <summary>The west edge of the first cell. The vestibule is to the west of it.</summary>
    public const int OriginX = 8;

    /// <summary>The south edge of the first row: the entrance cell (column 0, row 1) is centered on y = 0.</summary>
    public const int OriginY = -30;

    public List<BunkerRoom> Rooms = new();

    /// <summary>Every floor tile: the rooms, the doors and the corridors.</summary>
    public HashSet<Vector2i> Floor = new();

    /// <summary>The floor tiles that are corridor (or door) and not room.</summary>
    public HashSet<Vector2i> Tunnel = new();

    /// <summary>Wall tiles. True when the wall belongs to a room (its ring), false along a corridor.</summary>
    public Dictionary<Vector2i, bool> Walls = new();

    /// <summary>The centers of the corridors, for placing things along them.</summary>
    public List<Vector2i> TunnelPath = new();

    /// <summary>Pairs of room indexes that are joined.</summary>
    public List<(int A, int B)> Links = new();

    /// <summary>The doors between rooms and corridors.</summary>
    public List<BunkerDoor> Doors = new();

    /// <summary>The tile outside the vestibule door where the first corridor begins.</summary>
    public Vector2i Entry;

    public static BunkerLayout Generate(int seed, Vector2i entry)
    {
        var rng = new Random(seed);
        var layout = new BunkerLayout { Entry = entry };

        layout.PlaceRooms(rng);
        layout.Connect(rng);
        layout.Carve();
        layout.BuildWalls();
        return layout;
    }

    // -- Rooms ----------------------------------------------------------------------------------------------------

    private static readonly (BunkerRoomType Type, int MinW, int MaxW, int MinH, int MaxH)[] Sizes =
    {
        (BunkerRoomType.Decon, 7, 8, 7, 8),
        (BunkerRoomType.Barracks, 10, 12, 7, 9),
        (BunkerRoomType.Mess, 9, 11, 7, 9),
        (BunkerRoomType.Control, 9, 11, 8, 10),
        (BunkerRoomType.Generator, 8, 11, 8, 11),
        (BunkerRoomType.Reactor, 11, 12, 11, 12),
        (BunkerRoomType.Medical, 8, 10, 8, 10),
        (BunkerRoomType.Storage, 8, 12, 7, 10),
        (BunkerRoomType.AmmoDepot, 7, 9, 7, 9),
        (BunkerRoomType.Armory, 9, 11, 9, 11),
        (BunkerRoomType.Archive, 8, 10, 7, 9),
        (BunkerRoomType.Shaft, 5, 6, 5, 6),
    };

    private void PlaceRooms(Random rng)
    {
        // 16 to 20 rooms: the fixed ones, the rest small junction rooms
        var types = new List<BunkerRoomType>
        {
            BunkerRoomType.Control, BunkerRoomType.Generator, BunkerRoomType.Medical, BunkerRoomType.Mess,
            BunkerRoomType.Barracks, BunkerRoomType.Barracks, BunkerRoomType.Storage, BunkerRoomType.Storage,
            BunkerRoomType.AmmoDepot, BunkerRoomType.Archive,
        };

        var total = rng.Next(16, 21);
        while (types.Count + 3 < total) // + the entrance, the reactor and the armory
        {
            types.Add(BunkerRoomType.Shaft);
        }

        // Cells; the entrance cell is taken
        var cells = new List<(int C, int R)>();
        for (var c = 0; c < Columns; c++)
        {
            for (var r = 0; r < Rows; r++)
            {
                if (c != 0 || r != 1)
                    cells.Add((c, r));
            }
        }

        Shuffle(cells, rng);

        AddRoom(BunkerRoomType.Decon, (0, 1), rng);

        // The reactor and the armory are deep inside
        foreach (var deep in new[] { BunkerRoomType.Reactor, BunkerRoomType.Armory })
        {
            var index = cells.FindIndex(cell => cell.C >= 3);
            AddRoom(deep, cells[index], rng);
            cells.RemoveAt(index);
        }

        Shuffle(types, rng);
        for (var i = 0; i < types.Count && i < cells.Count; i++)
        {
            AddRoom(types[i], cells[i], rng);
        }

        // Some of the rooms have grey brick walls
        foreach (var room in Rooms)
        {
            room.BrickWalls = room.Type is not (BunkerRoomType.Armory or BunkerRoomType.Reactor or BunkerRoomType.Decon) && rng.NextDouble() < 0.4;
        }
    }

    private void AddRoom(BunkerRoomType type, (int C, int R) cell, Random rng)
    {
        var size = Sizes.First(s => s.Type == type);
        var w = rng.Next(size.MinW, size.MaxW + 1);
        var h = rng.Next(size.MinH, size.MaxH + 1);

        // Three tiles of margin on every side, so the corridors have room
        var left = OriginX + cell.C * CellSize + 3;
        var bottom = OriginY + cell.R * CellSize + 3;
        var x0 = left + rng.Next(0, CellSize - 6 - w + 1);
        var y0 = bottom + rng.Next(0, CellSize - 6 - h + 1);

        Rooms.Add(new BunkerRoom { Type = type, X0 = x0, Y0 = y0, X1 = x0 + w - 1, Y1 = y0 + h - 1 });
    }

    // -- Corridors ----------------------------------------------------------------------------------------------

    private static readonly Vector2i[] Dirs = { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };

    private static bool IsHub(BunkerRoom room) => room.Type is BunkerRoomType.Decon or BunkerRoomType.Shaft;

    private void Connect(Random rng)
    {
        // The hubs are joined first: the cheapest way, then some loops
        var hubs = Enumerable.Range(0, Rooms.Count).Where(i => IsHub(Rooms[i])).ToList();
        var joined = new HashSet<int> { 0 };
        while (joined.Count < hubs.Count)
        {
            var best = (A: -1, B: -1, Cost: double.MaxValue);
            foreach (var a in joined)
            {
                foreach (var b in hubs)
                {
                    if (joined.Contains(b))
                        continue;

                    var cost = (Rooms[a].Center - Rooms[b].Center).Length;
                    if (cost < best.Cost)
                        best = (a, b, cost);
                }
            }

            Links.Add((best.A, best.B));
            joined.Add(best.B);
        }

        foreach (var a in hubs)
        {
            foreach (var b in hubs)
            {
                if (b <= a || Links.Contains((a, b)) || Links.Contains((b, a)))
                    continue;

                if ((Rooms[a].Center - Rooms[b].Center).Length < CellSize * 2 && rng.NextDouble() < 0.3)
                    Links.Add((a, b));
            }
        }

        // Every other room is a dead end with one door onto the nearest hub, a few with a second one
        for (var i = 0; i < Rooms.Count; i++)
        {
            if (IsHub(Rooms[i]))
                continue;

            var near = hubs.OrderBy(h => (Rooms[h].Center - Rooms[i].Center).Length).ToList();
            Links.Add((i, near[0]));
            if (near.Count > 1 && Rooms[i].Type is not (BunkerRoomType.Armory or BunkerRoomType.Reactor) && rng.NextDouble() < 0.25)
                Links.Add((i, near[1]));
        }
    }

    /// <summary>Cells a corridor cannot use: the rooms and two tiles around them, and the vestibule.</summary>
    private readonly HashSet<Vector2i> _blocked = new();

    private void Carve()
    {
        foreach (var room in Rooms)
        {
            for (var x = room.X0; x <= room.X1; x++)
            {
                for (var y = room.Y0; y <= room.Y1; y++)
                {
                    Floor.Add(new Vector2i(x, y));
                }
            }

            for (var x = room.X0 - 2; x <= room.X1 + 2; x++)
            {
                for (var y = room.Y0 - 2; y <= room.Y1 + 2; y++)
                {
                    _blocked.Add(new Vector2i(x, y));
                }
            }
        }

        for (var x = -3; x <= 6; x++)
        {
            for (var y = -3; y <= 5; y++)
            {
                _blocked.Add(new Vector2i(x, y));
            }
        }

        // From the vestibule door to the west side of the entrance room
        if (TryAddDoor(0, new Vector2i(-1, 0), out var entryDoor))
        {
            var start = new Vector2i(Entry.X + 1, Entry.Y);
            var path = Route(start, entryDoor.Tile + entryDoor.Dir * 3);
            if (path != null)
            {
                Floor.Add(Entry);
                Floor.Add(Entry + new Vector2i(0, 1));
                Floor.Add(Entry + new Vector2i(0, -1));
                TunnelPath.Add(Entry);
                Carve(path);
                Stub(entryDoor);
            }
        }

        foreach (var (a, b) in Links)
        {
            var d = Rooms[b].Center - Rooms[a].Center;
            var dirA = Math.Abs(d.X) >= Math.Abs(d.Y) ? new Vector2i(Math.Sign(d.X), 0) : new Vector2i(0, Math.Sign(d.Y));
            var dirB = new Vector2i(-dirA.X, -dirA.Y);

            if (!TryAddDoor(a, dirA, out var doorA) || !TryAddDoor(b, dirB, out var doorB))
                continue;

            var path = Route(doorA.Tile + dirA * 3, doorB.Tile + dirB * 3);
            if (path == null)
                continue;

            Carve(path);
            Stub(doorA);
            Stub(doorB);
        }

        // Doors that lead nowhere are dropped
        Doors.RemoveAll(d => !Floor.Contains(d.Tile));

        foreach (var tile in Floor)
        {
            if (!Rooms.Any(room => room.Contains(tile)))
                Tunnel.Add(tile);
        }
    }

    /// <summary>A door in the wall ring of a room, on the given side: the first free spot from the middle of the side.</summary>
    private bool TryAddDoor(int room, Vector2i dir, out BunkerDoor door)
    {
        // A side that has a door already uses it
        var existing = Doors.FirstOrDefault(d => d.Room == room && d.Dir == dir);
        if (existing != null)
        {
            door = existing;
            return true;
        }

        var r = Rooms[room];
        var horizontal = dir.X != 0;
        var mid = horizontal ? (r.Y0 + r.Y1) / 2 : (r.X0 + r.X1) / 2;
        var lo = (horizontal ? r.Y0 : r.X0) + 2;
        var hi = (horizontal ? r.Y1 : r.X1) - 2;

        foreach (var off in new[] { 0, 3, -3, 1, -1 })
        {
            var along = mid + off;
            if (along < lo || along > hi)
                continue;

            var tile = dir.X > 0 ? new Vector2i(r.X1 + 1, along)
                : dir.X < 0 ? new Vector2i(r.X0 - 1, along)
                : dir.Y > 0 ? new Vector2i(along, r.Y1 + 1)
                : new Vector2i(along, r.Y0 - 1);

            if (Doors.Any(d => d.Room == room && (d.Tile - tile).LengthSquared < 16))
                continue;

            door = new BunkerDoor(room, tile, dir);
            Doors.Add(door);
            return true;
        }

        door = default!;
        return false;
    }

    /// <summary>
    ///     The door tile and the row of three tiles outside it. Only that row is carved beside the door, so the wall ring stays whole.
    /// </summary>
    private void Stub(BunkerDoor door)
    {
        Floor.Add(door.Tile);
        var outer = door.Tile + door.Dir;
        var side = new Vector2i(Math.Abs(door.Dir.Y), Math.Abs(door.Dir.X));
        Floor.Add(outer);
        Floor.Add(outer + side);
        Floor.Add(outer - side);
        TunnelPath.Add(outer);
    }

    /// <summary>Carves a corridor three tiles wide along the cells.</summary>
    private void Carve(List<Vector2i> path)
    {
        foreach (var cell in path)
        {
            TunnelPath.Add(cell);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    Floor.Add(new Vector2i(cell.X + dx, cell.Y + dy));
                }
            }
        }
    }

    /// <summary>The cheapest way over free cells, with few turns.</summary>
    private List<Vector2i>? Route(Vector2i from, Vector2i to)
    {
        const int minX = 6, maxX = 112, minY = -32, maxY = 52;
        var cost = new Dictionary<(Vector2i, int), double>();
        var prev = new Dictionary<(Vector2i, int), (Vector2i, int)>();
        var queue = new PriorityQueue<(Vector2i Pos, int Dir), double>();

        for (var d = 0; d < 4; d++)
        {
            cost[(from, d)] = 0;
            queue.Enqueue((from, d), 0);
        }

        (Vector2i Pos, int Dir)? goal = null;
        while (queue.TryDequeue(out var cur, out var c))
        {
            if (c > cost[cur])
                continue;

            if (cur.Pos == to)
            {
                goal = cur;
                break;
            }

            for (var d = 0; d < 4; d++)
            {
                var next = cur.Pos + Dirs[d];
                if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY || _blocked.Contains(next))
                    continue;

                var nc = c + 1 + (d == cur.Dir ? 0 : 2);
                var key = (next, d);
                if (cost.TryGetValue(key, out var old) && old <= nc)
                    continue;

                cost[key] = nc;
                prev[key] = cur;
                queue.Enqueue(key, nc);
            }
        }

        if (goal == null)
            return null;

        var result = new List<Vector2i>();
        var node = goal.Value;
        while (true)
        {
            result.Add(node.Pos);
            if (!prev.TryGetValue(node, out var p))
                break;

            node = p;
        }

        return result;
    }

    // -- Walls --------------------------------------------------------------------------------------------------

    private void BuildWalls()
    {
        foreach (var tile in Floor)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var next = new Vector2i(tile.X + dx, tile.Y + dy);
                    if (Floor.Contains(next))
                        continue;

                    Walls[next] = Rooms.Any(room => room.InRing(next));
                }
            }
        }
    }

    /// <summary>The room whose wall ring contains the tile, if any.</summary>
    public BunkerRoom? RoomOfWall(Vector2i tile) => Rooms.FirstOrDefault(room => room.InRing(tile));

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
