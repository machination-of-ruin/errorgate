using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.Anomalies;

/// <summary>
///     Goes on a grid (or a map) entity. When the map initializes, it scatters a few world faults
///     (heat, arc, collapse) over the walkable ground of the grid. See <see cref="AnomalyFieldSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class AnomalyFieldComponent : Component
{
    /// <summary>
    ///     How many faults to place. Fewer are placed (and a warning is logged) when not enough valid positions exist.
    /// </summary>
    [DataField]
    public int Count = 3;

    /// <summary>
    ///     Fault prototypes to choose from.
    /// </summary>
    [DataField]
    public List<EntProtoId> Prototypes = new()
    {
        "ErrorgateAnomalyHeat",
        "ErrorgateAnomalyArc",
        "ErrorgateAnomalyCollapse",
    };

    /// <summary>
    ///     Each prototype is used at most once.
    /// </summary>
    [DataField]
    public bool Unique = true;

    /// <summary>
    ///     Minimum distance (tiles) between two faults.
    /// </summary>
    [DataField]
    public float MinDistanceBetween = 25f;

    /// <summary>
    ///     Minimum distance (tiles) from every <c>SpawnPoint</c>, so nobody starts inside a fault.
    /// </summary>
    [DataField]
    public float MinDistanceFromSpawns = 20f;

    /// <summary>
    ///     Radius (tiles) around the fault that must be floor with nothing solid on it.
    /// </summary>
    [DataField]
    public float FreeRadius = 3f;

    /// <summary>
    ///     How many candidate tiles are looked at before giving up.
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
}
