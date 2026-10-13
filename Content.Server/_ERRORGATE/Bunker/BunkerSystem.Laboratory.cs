using System.Linq;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.Bunker;

public sealed partial class BunkerSystem
{
    /// <summary>Where the gate stands in the elevator hall of a laboratory, and the tile people arrive on.</summary>
    public static readonly Vector2i LaboratoryGate = new(2, 1);

    public static readonly Vector2i LaboratoryArrival = new(3, 1);

    /// <summary>
    ///     Builds the bunker as the tier 3 laboratory of the world (docs/design/world.md): the same rooms, corridors, doors, loot and
    ///     bosses, but the airlock vestibule is an elevator hall with a plain door, and its gate is placed by the caller at
    ///     <see cref="LaboratoryGate"/>. Returns the floor tiles, which need air.
    /// </summary>
    public List<Vector2i> BuildLaboratory(EntityUid gridUid, MapGridComponent grid, int seed)
    {
        BuildBunker(gridUid, grid, seed, out var vestibule, out _);

        // No airlock, no sirens, no hatch up
        var vestibuleComp = Comp<BunkerVestibuleComponent>(vestibule);
        if (vestibuleComp.Door is { } door)
            QueueDel(door);

        if (vestibuleComp.Ladder is { } ladder)
            QueueDel(ladder);

        foreach (var light in vestibuleComp.Lights)
        {
            QueueDel(light);
        }

        var consoles = EntityQueryEnumerator<BunkerConsoleComponent>();
        while (consoles.MoveNext(out var console, out _))
        {
            if (Transform(console).GridUid == gridUid)
                QueueDel(console);
        }

        QueueDel(vestibule);

        // The doorway is an ordinary door opened by hand and the hole where the hatch was is wall
        Spawn("BunkerRoomDoor", _map.GridTileToLocal(gridUid, grid, new Vector2i(5, 1)));
        Spawn(RoomWallProto, _map.GridTileToLocal(gridUid, grid, HatchUpTile));

        var floor = _airTiles.ToList();
        _airTiles = new List<Vector2i>(); // the world fills the air, not the bunker system
        return floor;
    }
}
