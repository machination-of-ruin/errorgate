using Content.Shared._ERRORGATE.LootManager;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared._ERRORGATE.World;

/// <summary>
///     The web of locations (docs/design/world.md): which locations exist and which gates join them. A gate is named
///     <c>Location.gate</c>, for example <c>Village.east</c>; a link joins two gates.
/// </summary>
[Prototype("worldGraph")]
public sealed partial class WorldGraphPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<ProtoId<WorldLocationPrototype>> Locations = new();

    [DataField(required: true)]
    public List<WorldLink> Links = new();

    /// <summary>The loot table of the whole world: one loot manager counts every location against the same caps.</summary>
    [DataField]
    public ProtoId<GlobalLootTablePrototype> LootTable = "LootTableGlobal";
}

/// <summary>Two gates that lead to each other, as <c>Location.gate</c>.</summary>
[DataDefinition]
public sealed partial class WorldLink
{
    [DataField(required: true)]
    public string A = string.Empty;

    [DataField(required: true)]
    public string B = string.Empty;
}

[CVarDefs]
public sealed class WorldCVars
{
    /// <summary>The world of generated locations is built at the start of a round. Off: the old maps are played as before.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("errorgate.world.enabled", false, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>The graph the world is built from.</summary>
    public static readonly CVarDef<string> Graph =
        CVarDef.Create("errorgate.world.graph", "Default", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>Seconds of warning before a location is wiped by the storm.</summary>
    public static readonly CVarDef<float> WarningSeconds =
        CVarDef.Create("errorgate.world.warning_seconds", 300f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>The sound of the storm warning, played to the whole location with the first notice and the last.</summary>
    public static readonly CVarDef<string> WarningSound =
        CVarDef.Create("errorgate.world.warning_sound", "/Audio/Misc/delta_alt.ogg", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>Seconds two locations are kept apart in time when their resets are scheduled.</summary>
    public static readonly CVarDef<float> ResetSpacingSeconds =
        CVarDef.Create("errorgate.world.reset_spacing_seconds", 3600f, CVar.SERVERONLY | CVar.ARCHIVE);
}
