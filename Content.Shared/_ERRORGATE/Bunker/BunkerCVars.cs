using Robust.Shared.Configuration;

namespace Content.Shared._ERRORGATE.Bunker;

[CVarDefs]
public sealed class BunkerCVars
{
    /// <summary>
    ///     The hidden bunker: a hatch somewhere remote on the map that leads to a generated bunker on a map of its own.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("errorgate.bunker.enabled", true, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Game maps (ids, separated by commas) that get a bunker.
    /// </summary>
    public static readonly CVarDef<string> Maps =
        CVarDef.Create("errorgate.bunker.maps", "Kuznetsk", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Seconds each siren sounds, outside or inside. Every door is locked meanwhile.
    /// </summary>
    public static readonly CVarDef<float> CycleSeconds =
        CVarDef.Create("errorgate.bunker.cycle_seconds", 15f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Seconds the hatches stay open after the outside siren, when nobody presses the console inside. They lock again afterwards.
    /// </summary>
    public static readonly CVarDef<float> OuterOpenSeconds =
        CVarDef.Create("errorgate.bunker.outer_open_seconds", 90f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How far the hatch has to be from the nearest loot spawner, tiles. It is its own point of interest.
    ///     Lowered step by step when the map has no such place.
    /// </summary>
    public static readonly CVarDef<float> MinPoiDistance =
        CVarDef.Create("errorgate.bunker.min_poi_distance", 30f, CVar.SERVERONLY | CVar.ARCHIVE);
}
