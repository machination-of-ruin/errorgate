using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Paper;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     Builds the outline map for the structure pool (docs/design/structures.md): one small grid per structure variant, floor tiles
///     in the size of its footprint class (walls included, no apron), the origin tile (0, 0) in green, and a paper label beside it.
///     Each structure is built on its own grid and saved with <c>savegrid</c>, so the saved file holds exactly that structure.
///     Keep the list in sync with the tables in the doc.
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed class StructureOutlineCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;

    public override string Command => "structureoutline";
    public override string Description => "Builds the structure outline map: one grid per structure variant, to build in and save with savegrid.";

    private static readonly Dictionary<string, Vector2i> Classes = new()
    {
        ["XS"] = new(5, 5),
        ["S"] = new(8, 6),
        ["M"] = new(10, 8),
        ["L"] = new(14, 10),
        ["XL"] = new(18, 14),
        ["XXL"] = new(24, 18),
    };

    // Theme, name, class, variants, variants in the first-slice minimum, loot location. Mirrors docs/design/structures.md.
    private static readonly (string Theme, string Name, string Class, int Variants, int Minimum, string Loot)[] Pool =
    {
        // 1 Village and Shore Resort
        ("village", "small-house", "S", 6, 2, "Living"),
        ("village", "medium-house", "M", 4, 2, "Living"),
        ("village", "farmstead", "L", 2, 0, "Living, Industrial"),
        ("village", "barn-shed", "S", 3, 0, "Industrial"),
        ("village", "general-store", "M", 2, 1, "Kitchen, Living"),
        ("village", "church", "L", 1, 1, "Church"),
        ("village", "school", "L", 1, 1, "School"),
        ("village", "clinic", "M", 1, 0, "Medical"),
        ("village", "props", "XS", 3, 0, "none"),
        ("village", "hotel", "XL", 2, 1, "Living, Kitchen"),
        ("village", "bungalow", "S", 4, 0, "Living"),
        ("village", "beach-bar", "M", 2, 0, "Kitchen"),
        ("village", "boathouse-pier", "L", 2, 0, "Industrial"),
        ("village", "lighthouse", "XS", 1, 0, "none"),
        ("village", "kiosk", "XS", 3, 0, "Kitchen"),
        ("village", "campsite", "S", 2, 0, "Living"),
        ("village", "spawn-zone", "M", 2, 1, "none"),
        ("village", "gate-tunnel-to-2", "M", 2, 1, "none"),

        // 2 Snow City
        ("city", "apartment-block", "XL", 3, 1, "Living"),
        ("city", "row-house", "M", 3, 0, "Living"),
        ("city", "shop-small", "S", 2, 1, "Kitchen, Living"),
        ("city", "shop-medium", "M", 2, 1, "Kitchen, Living"),
        ("city", "police-station", "L", 1, 1, "Security, Prison"),
        ("city", "hospital", "XL", 1, 1, "Medical"),
        ("city", "warehouse", "L", 3, 1, "Industrial"),
        ("city", "garage-gas-station", "M", 2, 0, "Industrial"),
        ("city", "factory", "XXL", 1, 0, "Industrial, Military"),
        ("city", "school", "L", 1, 0, "School"),
        ("city", "church", "L", 1, 0, "Church"),
        ("city", "checkpoint", "S", 3, 0, "Military, Security"),
        ("city", "substation", "S", 2, 0, "Industrial"),
        ("city", "barricades-wrecks", "XS", 4, 0, "none"),
        ("city", "bus-depot", "L", 1, 0, "Industrial"),
        ("city", "gate-tunnel", "M", 2, 2, "none"),
        ("city", "gate-military-base", "L", 1, 1, "Military"),

        // 3 Vast Underground Bunker-Laboratory (rooms)
        ("bunker", "lab-hall", "L", 3, 1, "Industrial, Medical"),
        ("bunker", "containment-block", "L", 2, 0, "Security, Prison"),
        ("bunker", "server-room", "M", 2, 1, "Industrial"),
        ("bunker", "control-room", "M", 2, 1, "Security"),
        ("bunker", "reactor-hall", "XL", 1, 0, "Industrial"),
        ("bunker", "medical-lab", "M", 2, 1, "Medical"),
        ("bunker", "barracks", "L", 2, 1, "Military"),
        ("bunker", "armory", "M", 1, 0, "Military"),
        ("bunker", "ammo-depot", "M", 1, 0, "Military"),
        ("bunker", "canteen", "M", 2, 0, "Kitchen, Living"),
        ("bunker", "generator-room", "M", 2, 0, "Industrial"),
        ("bunker", "archive", "M", 2, 0, "Security, Living"),
        ("bunker", "storage", "M", 3, 1, "Industrial"),
        ("bunker", "cryo-bay", "L", 1, 0, "Medical"),
        ("bunker", "airlock-vestibule", "S", 2, 0, "none"),
        ("bunker", "junction", "XS", 3, 0, "Industrial"),
        ("bunker", "gate-elevator-to-2", "M", 2, 1, "none"),

        // Tunnels
        ("tunnel", "straight", "XS", 3, 0, "none"),
        ("tunnel", "junction-bend", "XS", 2, 0, "none"),
        ("tunnel", "collapse-flooded", "S", 2, 0, "none"),
        ("tunnel", "rest-stop", "M", 2, 0, "none"),
        ("tunnel", "gate", "S", 2, 0, "none"),
    };

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var mapSystem = EntityManager.System<SharedMapSystem>();
        var transform = EntityManager.System<SharedTransformSystem>();
        var meta = EntityManager.System<MetaDataSystem>();
        var paper = EntityManager.System<PaperSystem>();

        var mapUid = mapSystem.CreateMap(out var mapId);
        meta.SetEntityName(mapUid, "structure outline");

        var floor = new Tile(_tileDefs["FloorDark"].TileId);
        var origin = new Tile(_tileDefs["FloorGreenCircuit"].TileId);

        // Rows by theme, wrapped at a fixed width; the bottom of each row leaves room for the labels
        const int rowWidth = 260;
        var cursor = new Vector2i(0, 0);
        var rowHeight = 0;
        var lastTheme = "";
        var count = 0;

        foreach (var (theme, name, className, variants, minimum, loot) in Pool)
        {
            if (theme != lastTheme)
            {
                // A new theme starts on a new row
                cursor = new Vector2i(0, cursor.Y + rowHeight + 14);
                rowHeight = 0;
                lastTheme = theme;
            }

            var size = Classes[className];
            for (var v = 1; v <= variants; v++)
            {
                if (cursor.X + size.X + 6 > rowWidth)
                {
                    cursor = new Vector2i(0, cursor.Y + rowHeight + 10);
                    rowHeight = 0;
                }

                var grid = _mapManager.CreateGridEntity(mapId);
                transform.SetLocalPosition(grid.Owner, new System.Numerics.Vector2(cursor.X, cursor.Y));
                var star = v <= minimum ? "*" : "";
                var label = $"{theme}/{name}-{v}{star} | {className} {size.X}x{size.Y} | loot: {loot}";
                meta.SetEntityName(grid.Owner, $"{theme}/{name}-{v}");

                for (var x = 0; x < size.X; x++)
                {
                    for (var y = 0; y < size.Y; y++)
                    {
                        mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), floor);
                    }
                }

                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(0, 0), origin);

                // The label is on the map, below the grid, so it is not saved with the structure
                var note = EntityManager.SpawnEntity("Paper", new MapCoordinates(cursor.X + 0.5f, cursor.Y - 3f, mapId));
                meta.SetEntityName(note, label);
                if (EntityManager.TryGetComponent(note, out PaperComponent? paperComp))
                    paper.SetContent((note, paperComp), label + "\n(Build inside the floor. The green tile is the origin. Save with savegrid.)");

                cursor += new Vector2i(size.X + 6, 0);
                rowHeight = Math.Max(rowHeight, size.Y + 4);
                count++;
            }
        }

        shell.WriteLine($"Built {count} outlines on map {mapId} ({EntityManager.GetNetEntity(mapUid)}). Use mapping {mapId} to go there; save each structure with savegrid <its grid> <file>.");
    }
}
