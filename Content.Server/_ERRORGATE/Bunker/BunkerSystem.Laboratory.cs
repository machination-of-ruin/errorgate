using System.Linq;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.Bunker;

public sealed partial class BunkerSystem
{
    /// <summary>
    ///     Builds the bunker as the tier 3 laboratory of the world (docs/design/world.md): the same rooms, corridors, doors, loot,
    ///     bosses and the same airlock vestibule. The way in is the surface hatch, placed by the world at the end of the tunnel that leads
    ///     here (<see cref="PlaceHatch"/>): it needs a bunker key and goes through the timed airlock. Returns the floor tiles, which need air.
    /// </summary>
    public List<Vector2i> BuildLaboratory(EntityUid gridUid, MapGridComponent grid, int seed, out EntityUid vestibule, out EntityCoordinates arrival)
    {
        BuildBunker(gridUid, grid, seed, out vestibule, out arrival);

        var floor = _airTiles.ToList();
        _airTiles = new List<Vector2i>(); // the world fills the air, not the bunker system
        return floor;
    }
}
