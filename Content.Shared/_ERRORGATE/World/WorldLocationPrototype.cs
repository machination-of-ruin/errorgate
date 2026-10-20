using Content.Shared.Weather;
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

    /// <summary>The shape of the playable ground: <c>organic</c> (a blob of the same area as the size, different every time) or <c>rect</c>.</summary>
    [DataField]
    public string Shape = "organic";

    /// <summary>How the streets and lots are laid out: <c>village</c> (a main street with lanes, homesteads with fences) or <c>city</c> (a grid of streets with sidewalks).</summary>
    [DataField]
    public string Style = "village";

    /// <summary>The tile of the sidewalks and of the paths from a door to the street.</summary>
    [DataField]
    public string Walkway = "FloorLowDesert";

    /// <summary>A street light along the sidewalks (the city). Empty for none.</summary>
    [DataField]
    public EntProtoId? Post;

    /// <summary>The pieces of the fences around the lots.</summary>
    [DataField]
    public EntProtoId FenceStraight = "FenceWoodHighStraight";

    [DataField]
    public EntProtoId FenceCorner = "FenceWoodHighCorner";

    [DataField]
    public EntProtoId FenceGate = "FenceWoodHighGate";

    /// <summary>The tile of roads between the structures.</summary>
    [DataField]
    public string Road = "FloorAsphalt";

    /// <summary>The surface of the main street, if it differs from the other roads.</summary>
    [DataField]
    public string MainRoad = string.Empty;

    /// <summary>The far end of the main street is a collapsed tunnel mouth (a plaza of rubble in the border) and not a gate.</summary>
    [DataField]
    public bool RubbleEnd;

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

    /// <summary>Things scattered on free ground, besides trees and rocks: boulders, barricades, wrecks.</summary>
    [DataField]
    public List<WorldScatterEntry> Props = new();

    /// <summary>Decals scattered on free ground: grass, brushwood.</summary>
    [DataField]
    public List<WorldDecalEntry> Decals = new();

    /// <summary>The edge of the map on one side: <c>water</c> (sand, then shallow water), <c>ice</c>, or <c>none</c>.</summary>
    [DataField]
    public string Shore = "none";

    /// <summary>The tile of a sandy shore.</summary>
    [DataField]
    public string Sand = "FloorDesert";

    /// <summary>The tile of an icy shore.</summary>
    [DataField]
    public string IceTile = "FloorIce";

    /// <summary>The water that lies on the shallow part of a shore.</summary>
    [DataField]
    public EntProtoId Water = "FloorWaterEntity";

    /// <summary>
    ///     A hand-built grid to load instead of generating the location, for example <c>/Maps/_ERRORGATE/Locations/MilitaryBase.yml</c>.
    ///     Its gates are <c>WorldGate</c> entities with the gate name set; people arrive on the tile in front of the gate (the way
    ///     the entity faces, south by default). If the file is not there yet, the location is generated as its style says.
    /// </summary>
    [DataField]
    public string? MapPath;

    /// <summary>Players without a faction come into the world here: a clearing with spawn points.</summary>
    [DataField]
    public bool SpawnZone;

    /// <summary>Underground: no sun, no weather, no day and night.</summary>
    [DataField]
    public bool Underground;

    /// <summary>
    ///     The storm wipes this location every so many minutes, a random time between the two (0 for never). A location that has
    ///     <see cref="ResetWith"/> goes with that one instead.
    /// </summary>
    [DataField]
    public int ResetMinMinutes;

    [DataField]
    public int ResetMaxMinutes;

    [DataField]
    public string? ResetWith;

    /// <summary>The gate entity of this location's gates.</summary>
    [DataField]
    public EntProtoId Gate = "WorldGate";

    /// <summary>Mob spawners placed in tunnels, and how many.</summary>
    [DataField]
    public List<EntProtoId> Mobs = new();

    [DataField]
    public int MobCount = 4;

    /// <summary>A weather that never ends in this location.</summary>
    [DataField]
    public ProtoId<WeatherPrototype>? Weather;

    [DataField]
    public Color AmbientLight = Color.FromHex("#6A6A6A");

    /// <summary>The gates that stand in the border of the map (the way out is an opening in the wall of rock, with a road leading to it).</summary>
    [DataField]
    public List<string> BorderGates = new();

    /// <summary>The places that make the empty ground worth crossing: farm, camp, ruin, orchard, windbreak, pond, well.</summary>
    [DataField]
    public List<WorldLandmarkEntry> Landmarks = new();

    /// <summary>Radioactive barrels in a tunnel (a few along the way and two at the end that leads to a laboratory): a sign that a suit is needed.</summary>
    [DataField]
    public int RadiationBarrels;

    /// <summary>How many world faults (see docs/design/anomalies.md) are scattered over the location, along the routes between the loot.</summary>
    [DataField]
    public int Faults;

    /// <summary>The gates whose plaza has radioactive barrels (the way towards the laboratory), so people know a suit is needed.</summary>
    [DataField]
    public List<string> RadiationGates = new();

    /// <summary>How many of the unique buildings (see WorldStructurePrototype) of each group a build puts out, from min to max.</summary>
    [DataField]
    public List<WorldUniqueGroup> UniqueGroups = new();

    /// <summary>How many roadblocks (wrecks, barricades) stand on the wide avenues, and what they are made of.</summary>
    [DataField]
    public int Roadblocks;

    [DataField]
    public List<EntProtoId> RoadblockProtos = new();

    /// <summary>
    ///     The tiers of the loot spawners of the structures: each spawner takes one of them at random. Empty is the tier of the location.
    ///     Which items a tier gives is the loot manager's business, the same one for the whole world.
    /// </summary>
    [DataField]
    public List<int> LootTiers = new();

    /// <summary>Items that every build of the location puts out for certain, on a loot spawner where they fit (the keys).</summary>
    [DataField]
    public List<EntProtoId> Guaranteed = new();
}

/// <summary>A kind of place the layout puts into the empty ground and how many of it (see LocationLayout.Features.cs).</summary>
/// <summary>How many of the unique buildings of a group a location puts out.</summary>
[DataDefinition]
public sealed partial class WorldUniqueGroup
{
    [DataField(required: true)]
    public string Group = string.Empty;

    [DataField]
    public int Min = 1;

    [DataField]
    public int Max = 1;
}

[DataDefinition]
public sealed partial class WorldLandmarkEntry
{
    [DataField(required: true)]
    public string Kind = string.Empty;

    [DataField]
    public int Count = 1;
}

/// <summary>A kind of entity scattered on the ground: one of the prototypes, <see cref="Count"/> places, in clumps of radius <see cref="Clump"/> (0 is single ones).</summary>
[DataDefinition]
public sealed partial class WorldScatterEntry
{
    [DataField(required: true)]
    public List<EntProtoId> Protos = new();

    [DataField]
    public int Count = 10;

    [DataField]
    public int Clump;
}

/// <summary>A kind of decal scattered on the ground, in the same way.</summary>
[DataDefinition]
public sealed partial class WorldDecalEntry
{
    [DataField(required: true)]
    public List<string> Decals = new();

    [DataField]
    public int Count = 100;

    [DataField]
    public int Clump = 4;
}

/// <summary>
///     What one structure of the pool is for, so the generator puts it where it belongs: a <c>house</c> in a homestead or along a street,
///     an <c>outbuilding</c> (a shed, a sauna) beside a house, a <c>civic</c> building (a church, a school, a clinic), a <c>shop</c> or an
///     <c>industrial</c> building. The id is the theme and the file name, for example <c>village-kuz-wood-XL-3</c>. A structure without a
///     prototype is an outbuilding if it is small and a house if it is not.
/// </summary>
[Prototype("worldStructure")]
public sealed partial class WorldStructurePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Role = "house";

    /// <summary>How often it is picked compared to the others of its role.</summary>
    [DataField]
    public float Weight = 1f;

    /// <summary>The group a unique building belongs to: the location puts out only some of each group (see UniqueGroups).</summary>
    [DataField]
    public string Group = string.Empty;

    /// <summary>
    ///     A unique building that may take the streets under it away if it fits nowhere else (the factories). Without it, a unique building
    ///     is left out when no lot along a street takes it.
    /// </summary>
    [DataField]
    public bool CutStreets;

    /// <summary>
    ///     Mob spawners (one of them is picked for each) put on free floor tiles inside the building when it is built: the runners in a
    ///     factory. Empty for none.
    /// </summary>
    [DataField]
    public List<string> Mobs = new();

    /// <summary>How many of the <see cref="Mobs"/> go inside, a few steps apart.</summary>
    [DataField]
    public int MobCount;

    /// <summary>
    ///     A building that is put out once in every location of its theme, on a lot picked for it (a hand-cut landmark), and is not part
    ///     of the random pool.
    /// </summary>
    [DataField]
    public bool Unique;

    /// <summary>
    ///     Stands on the land just inland of the shore (the resort), inside a fence with a park in front of it, its front door turned away
    ///     from the water. Used with <see cref="Unique"/>; a location without a shore does not place it.
    /// </summary>
    [DataField]
    public bool Shore;

    /// <summary>
    ///     The side of the building (<c>south</c>, <c>east</c>, <c>north</c> or <c>west</c>, as it is built) whose widest doorless gap in
    ///     the outer wall (floor on the edge of the footprint with no wall, window or door on it) is the front; the path to the street is
    ///     as wide as the gap. For buildings cut out of a bigger place with an open entrance. Empty: the front is the side with the doors.
    /// </summary>
    [DataField]
    public string OpenFront = string.Empty;
}
