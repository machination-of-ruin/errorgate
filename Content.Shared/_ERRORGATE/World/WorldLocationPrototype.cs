using Robust.Shared.Prototypes;

namespace Content.Shared._ERRORGATE.World;

/// <summary>
///     One location of the world (docs/design/world.md): a finite map with a theme and a tier that is regenerated completely
///     when it resets. The generator fills it with structures from the theme's pool.
/// </summary>
[Prototype("worldLocation")]
public sealed partial class WorldLocationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Only for the admin tools and logs.</summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>1 to 3. Also the tier of the loot spawners that structures hold.</summary>
    [DataField]
    public int Tier = 1;

    /// <summary>The folder under <c>/Maps/_ERRORGATE/Structures/</c> with the structure pool.</summary>
    [DataField]
    public string Theme = string.Empty;

    /// <summary>Playable size in tiles, the border walls are inside it.</summary>
    [DataField]
    public Vector2i Size = new(160, 160);

    /// <summary>The terrain tile (a tile definition id).</summary>
    [DataField]
    public string Ground = "FloorDirt";

    /// <summary>The tile of roads between the structures.</summary>
    [DataField]
    public string Road = "FloorAsphalt";

    /// <summary>The wall of the border.</summary>
    [DataField]
    public EntProtoId Border = "WorldBorderWall";

    /// <summary>1 is the normal amount of structures for the size, less is emptier.</summary>
    [DataField]
    public float Density = 1f;

    /// <summary>Trees: the prototypes picked from, how many clumps of them, and how many single ones.</summary>
    [DataField]
    public List<EntProtoId> Trees = new();

    [DataField]
    public int TreeClumps = 10;

    [DataField]
    public int TreeSingles = 40;

    /// <summary>How many indestructible rock formations break up the sight lines.</summary>
    [DataField]
    public int Rocks = 12;

    [DataField]
    public Color AmbientLight = Color.FromHex("#6A6A6A");
}
