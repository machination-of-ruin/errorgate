using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Goes on a grid (or a map) entity. When the map initializes, it scatters packs of world faults
///     (heat, arc, collapse) over the reachable ground of the grid, along the routes between the points of interest
///     (loot spawners) and away from spawn points. See <see cref="AnomalyFieldSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class AnomalyFieldComponent : Component
{
    /// <summary>
    ///     How many faults are wanted in total, over all packs. Fewer are placed (and a warning is logged)
    ///     when there is not enough room.
    /// </summary>
    [DataField]
    public int Count = 36;

    /// <summary>
    ///     Fault prototypes to choose from. Each pack is of one of them, and they are used evenly.
    /// </summary>
    [DataField]
    public List<EntProtoId> Prototypes = new()
    {
        "ErrorgateAnomalyHeat",
        "ErrorgateAnomalyArc",
        "ErrorgateAnomalyCollapse",
    };

    /// <summary>
    ///     Faults per pack. Keep the maximum at 5 or less: the singularity shader draws five distortions at most, so in a
    ///     bigger pack of collapse faults some of the active ones would show only their sprite and no lens.
    /// </summary>
    [DataField]
    public int PackSizeMin = 3;

    [DataField]
    public int PackSizeMax = 5;

    /// <summary>
    ///     Smallest radius (tiles) of the blob a pack is scattered in. The blob grows with the number of faults and the
    ///     size of their danger zone, so that people can walk between them, see <see cref="AnomalyPack.Radius"/>.
    /// </summary>
    [DataField]
    public float PackRadius = 9f;

    /// <summary>
    ///     Lower limit (tiles) of the distance between the centers of two faults in a pack. Otherwise the distance
    ///     is the two danger radii plus a gap: 70% of the time 1.5 to 3 tiles, 30% of the time 0 to 1 tile.
    /// </summary>
    [DataField]
    public float MinSpacingInPack = 4f;

    /// <summary>
    ///     Minimum distance (tiles) between the centers of two packs. Relaxed (and logged) if there is no room.
    /// </summary>
    [DataField]
    public float MinDistanceBetweenPacks = 30f;

    /// <summary>
    ///     Minimum distance (tiles) from every <c>SpawnPoint</c>, so nobody starts inside a fault.
    /// </summary>
    [DataField]
    public float MinDistanceFromSpawns = 25f;

    /// <summary>
    ///     Minimum distance (tiles) from every point of interest (loot spawner), so the loot is not inside a fault.
    /// </summary>
    [DataField]
    public float MinDistanceFromPoi = 8f;

    /// <summary>
    ///     Packs are only placed within this distance (tiles) of a point of interest.
    /// </summary>
    [DataField]
    public float MaxDistanceFromPoi = 40f;

    /// <summary>
    ///     Radius (tiles) around a fault that must be floor with nothing solid on it.
    /// </summary>
    [DataField]
    public float FreeRadius = 3f;

    /// <summary>
    ///     Put the packs on the likely routes between points of interest and spawn points.
    ///     Otherwise they are placed at random reachable places near points of interest.
    /// </summary>
    [DataField]
    public bool UseRoutes = true;

    /// <summary>
    ///     Points of interest closer than this to each other (a grid of cells this big, tiles) form one cluster.
    /// </summary>
    [DataField]
    public float PoiClusterSize = 15f;

    /// <summary>
    ///     Each cluster is joined by a route to this many of its nearest neighbours.
    /// </summary>
    [DataField]
    public int RoutesPerCluster = 2;

    /// <summary>
    ///     Size (tiles) of a cell of the coarse map used for reachability and routes.
    /// </summary>
    [DataField]
    public int CellSize = 4;

    /// <summary>
    ///     How many candidate positions are looked at before giving up.
    /// </summary>
    [DataField]
    public int MaxAttempts = 20000;

    /// <summary>
    ///     Set on map init, the faults are placed on the next update when everything is loaded.
    /// </summary>
    [ViewVariables]
    public bool Pending;

    [ViewVariables]
    public List<EntityUid> Spawned = new();

    /// <summary>
    ///     The packs of the last placement.
    /// </summary>
    [ViewVariables]
    public List<AnomalyPack> Packs = new();
}

/// <summary>
///     One pack of faults of the same type.
/// </summary>
public sealed class AnomalyPack
{
    public EntProtoId Proto;

    /// <summary>Center of the blob, in world position.</summary>
    public Vector2 Center;

    /// <summary>Radius of the blob the faults were scattered in.</summary>
    public float Radius;

    /// <summary>Average distance between two faults of the pack (twice the danger radius plus about two tiles).</summary>
    public float Spacing;

    /// <summary>Whether there is a way through the pack that stays out of every danger zone.</summary>
    public bool Passable;

    public List<EntityUid> Members = new();
}
