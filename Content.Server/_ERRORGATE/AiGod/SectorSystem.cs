using System.Numerics;
using System.Text;
using Content.Shared._ERRORGATE.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Optional per-map override of the sector size (<c>errorgate.god.sector_size</c>). Put it on a map's grid.
/// </summary>
[RegisterComponent]
public sealed partial class SectorGridComponent : Component
{
    /// <summary>
    ///     Side of a sector in tiles.
    /// </summary>
    [DataField]
    public int CellSize = 64;
}

/// <summary>
///     Splits a map's grid into named squares: columns are lettered from west to east, rows numbered from north to south
///     (A1 is the north-west corner). It is her vocabulary for places, used to say where things happen and where subjects
///     are. Only squares with some floor in them exist.
/// </summary>
public sealed class SectorSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    ///     A square needs at least this share of its tiles to be floor to exist.
    /// </summary>
    private const float MinFloorShare = 0.05f;

    private readonly Dictionary<EntityUid, GridSectors> _grids = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridRemovalEvent>(args => _grids.Remove(args.EntityUid));
    }

    /// <summary>
    ///     The name of the sector an entity stands in, null when it is not on a grid or the square has no floor.
    /// </summary>
    public string? SectorOf(EntityUid entity)
    {
        var xform = Transform(entity);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var tile = _map.CoordinatesToTile(gridUid, grid, xform.Coordinates);
        return SectorOf(gridUid, grid, tile);
    }

    public string? SectorOf(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var sectors = SectorsOf(gridUid, grid);
        var name = sectors.NameOf(tile);
        return name != null && sectors.Names.Contains(name) ? name : null;
    }

    /// <summary>
    ///     Every sector of a grid that has floor, in reading order (A1, B1, ..., A2, ...).
    /// </summary>
    public IReadOnlyList<string> SectorNames(EntityUid gridUid, MapGridComponent grid)
    {
        return SectorsOf(gridUid, grid).Ordered;
    }

    /// <summary>
    ///     Whether any grid in the world has a sector of this name.
    /// </summary>
    public bool AnyGridHas(string sector)
    {
        var query = EntityQueryEnumerator<MapGridComponent>();
        while (query.MoveNext(out var uid, out var grid))
        {
            if (grid.ChunkCount > 0 && SectorsOf(uid, grid).Names.Contains(sector))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The center of a sector in grid tile coordinates, for placing things in it.
    /// </summary>
    public bool TryGetCenter(EntityUid gridUid, MapGridComponent grid, string sector, out Vector2 center)
    {
        return SectorsOf(gridUid, grid).TryGetCenter(sector, out center);
    }

    /// <summary>
    ///     Forgets the floor counts, for when a grid has been changed a lot.
    /// </summary>
    public void Reset()
    {
        _grids.Clear();
    }

    private GridSectors SectorsOf(EntityUid gridUid, MapGridComponent grid)
    {
        if (_grids.TryGetValue(gridUid, out var existing))
            return existing;

        var size = TryComp<SectorGridComponent>(gridUid, out var custom) ? custom.CellSize : _cfg.GetCVar(ErrorgateCVars.GodSectorSize);
        var made = GridSectors.Build(_map, gridUid, grid, Math.Max(8, size));
        _grids[gridUid] = made;
        return made;
    }

    /// <summary>
    ///     The squares of one grid. Plain data and arithmetic, so the naming can be tested without a map.
    /// </summary>
    public sealed class GridSectors
    {
        public int CellSize;
        public Vector2i Min;
        public int Columns;
        public int Rows;
        public int TopRow;
        public readonly HashSet<string> Names = new();
        public readonly List<string> Ordered = new();

        public static GridSectors Build(SharedMapSystem map, EntityUid gridUid, MapGridComponent grid, int cellSize)
        {
            var floor = new Dictionary<Vector2i, int>();
            var min = new Vector2i(int.MaxValue, int.MaxValue);
            var max = new Vector2i(int.MinValue, int.MinValue);

            var enumerator = map.GetAllTilesEnumerator(gridUid, grid);
            while (enumerator.MoveNext(out var tile))
            {
                var indices = tile.Value.GridIndices;
                min = new Vector2i(Math.Min(min.X, indices.X), Math.Min(min.Y, indices.Y));
                max = new Vector2i(Math.Max(max.X, indices.X), Math.Max(max.Y, indices.Y));
            }

            var result = new GridSectors { CellSize = cellSize };
            if (min.X == int.MaxValue)
                return result;

            result.Define(min, max, cellSize);

            enumerator = map.GetAllTilesEnumerator(gridUid, grid);
            floor.Clear();
            while (enumerator.MoveNext(out var tile))
            {
                var cell = result.CellOfTile(tile.Value.GridIndices);
                floor[cell] = floor.GetValueOrDefault(cell) + 1;
            }

            result.Fill(floor);
            return result;
        }

        /// <summary>
        ///     Lays the squares over the bounds of the grid.
        /// </summary>
        public void Define(Vector2i min, Vector2i max, int cellSize)
        {
            CellSize = cellSize;
            Min = min;
            Columns = (max.X - min.X) / cellSize + 1;
            Rows = (max.Y - min.Y) / cellSize + 1;
            TopRow = max.Y;
        }

        /// <summary>
        ///     The square a tile is in, as (column, row from the north), zero based.
        /// </summary>
        public Vector2i CellOfTile(Vector2i tile)
        {
            return new Vector2i(
                Math.Max(0, (tile.X - Min.X) / CellSize),
                Math.Max(0, (TopRow - tile.Y) / CellSize));
        }

        /// <summary>
        ///     Decides which squares exist from the number of floor tiles in each.
        /// </summary>
        public void Fill(Dictionary<Vector2i, int> floorPerCell)
        {
            Names.Clear();
            Ordered.Clear();

            var needed = Math.Max(1, (int) (CellSize * CellSize * MinFloorShare));
            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    if (floorPerCell.GetValueOrDefault(new Vector2i(column, row)) < needed)
                        continue;

                    var name = Label(column, row);
                    Names.Add(name);
                    Ordered.Add(name);
                }
            }
        }

        public string? NameOf(Vector2i tile)
        {
            if (Columns == 0 || tile.X < Min.X || tile.Y > TopRow)
                return null;

            var cell = CellOfTile(tile);
            return cell.X >= Columns || cell.Y >= Rows ? null : Label(cell.X, cell.Y);
        }

        public bool TryGetCenter(string sector, out Vector2 center)
        {
            center = default;
            if (!Names.Contains(sector))
                return false;

            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    if (Label(column, row) != sector)
                        continue;

                    center = new Vector2(Min.X + (column + 0.5f) * CellSize, TopRow + 1f - (row + 0.5f) * CellSize);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     A, B, ... Z, AA, AB, ... for the column, then the row number from one.
        /// </summary>
        public static string Label(int column, int row)
        {
            var letters = new StringBuilder();
            var n = column;
            do
            {
                letters.Insert(0, (char) ('A' + n % 26));
                n = n / 26 - 1;
            }
            while (n >= 0);

            return letters.Append(row + 1).ToString();
        }
    }
}
