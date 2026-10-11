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
///     A generated bunker: rooms joined by tunnels three tiles wide. The same seed gives the same bunker. Pure data, nothing is spawned here.
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

    /// <summary>Every floor tile: the rooms and the tunnels.</summary>
    public HashSet<Vector2i> Floor = new();

    /// <summary>The floor tiles that are tunnel and not room.</summary>
    public HashSet<Vector2i> Tunnel = new();

    /// <summary>Wall tiles. True when the wall belongs to a room (its ring), false along a tunnel.</summary>
    public Dictionary<Vector2i, bool> Walls = new();

    /// <summary>The centers of the tunnels, for placing things along them.</summary>
    public List<Vector2i> TunnelPath = new();

    /// <summary>Pairs of room indexes that are joined.</summary>
    public List<(int A, int B)> Links = new();

    /// <summary>The tile outside the vestibule door where the first tunnel begins.</summary>
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

        // Three tiles of margin on every side, so the tunnels have room
        var left = OriginX + cell.C * CellSize + 3;
        var bottom = OriginY + cell.R * CellSize + 3;
        var x0 = left + rng.Next(0, CellSize - 6 - w + 1);
        var y0 = bottom + rng.Next(0, CellSize - 6 - h + 1);

        Rooms.Add(new BunkerRoom { Type = type, X0 = x0, Y0 = y0, X1 = x0 + w - 1, Y1 = y0 + h - 1 });
    }

    // -- Tunnels ------------------------------------------------------------------------------------------------

    private void Connect(Random rng)
    {
        // The cheapest way to join every room (Prim), then some extra loops
        var joined = new HashSet<int> { 0 };
        while (joined.Count < Rooms.Count)
        {
            var best = (A: -1, B: -1, Cost: double.MaxValue);
            foreach (var a in joined)
            {
                for (var b = 0; b < Rooms.Count; b++)
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

        for (var a = 0; a < Rooms.Count; a++)
        {
            for (var b = a + 1; b < Rooms.Count; b++)
            {
                if (Links.Contains((a, b)) || Links.Contains((b, a)))
                    continue;

                if ((Rooms[a].Center - Rooms[b].Center).Length < CellSize * 1.3 && rng.NextDouble() < 0.3)
                    Links.Add((a, b));
            }
        }

        _horizontalFirst = Links.Select(_ => rng.NextDouble() < 0.5).ToList();
    }

    private List<bool> _horizontalFirst = new();

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
        }

        // The tunnel from the vestibule to the first room
        CarveTunnel(Entry, Rooms[0].Center, true);

        for (var i = 0; i < Links.Count; i++)
        {
            CarveTunnel(Rooms[Links[i].A].Center, Rooms[Links[i].B].Center, _horizontalFirst[i]);
        }

        foreach (var tile in Floor)
        {
            if (!Rooms.Any(room => room.Contains(tile)))
                Tunnel.Add(tile);
        }
    }

    /// <summary>A tunnel three tiles wide, one leg along x and one along y.</summary>
    private void CarveTunnel(Vector2i from, Vector2i to, bool horizontalFirst)
    {
        var corner = horizontalFirst ? new Vector2i(to.X, from.Y) : new Vector2i(from.X, to.Y);
        Band(from, corner);
        Band(corner, to);
    }

    private void Band(Vector2i a, Vector2i b)
    {
        var horizontal = a.Y == b.Y;
        for (var x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
        {
            for (var y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
            {
                for (var side = -1; side <= 1; side++)
                {
                    var tile = horizontal ? new Vector2i(x, y + side) : new Vector2i(x + side, y);
                    Floor.Add(tile);
                }

                TunnelPath.Add(new Vector2i(x, y));
            }
        }
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
