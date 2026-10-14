using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Gravity;
using Content.Server.Gravity;
using Robust.Shared.ContentPack;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     Shows one structure of the pool on a map of its own and moves the admin there, to look at it, to cut it into parts or to
///     fix it: <c>structureview village/kuz-wood-XL-3</c>. Without a name it lists the files.
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed class StructureViewCommand : LocalizedEntityCommands
{
    private const string Root = "/Maps/_ERRORGATE/Structures/";

    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly GravitySystem _gravity = default!;

    public override string Command => "structureview";
    public override string Description => "Shows a structure of the pool on a map of its own: structureview <theme/name>. No name lists them.";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(_res.ContentFindFiles(new ResPath(Root)).Where(p => p.Extension == "yml").Select(p => p.ToString()[Root.Length..^4]).OrderBy(p => p), "<theme/name>")
            : CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            foreach (var path in _res.ContentFindFiles(new ResPath(Root)).Where(p => p.Extension == "yml").OrderBy(p => p.ToString()))
            {
                shell.WriteLine(path.ToString()[Root.Length..^4]);
            }

            return;
        }

        var file = new ResPath(Root + args[0].Trim('/') + ".yml");
        // Not initialized: nothing in the structure runs (spawners, doors, power), so it stays as it was saved and can be edited and saved again
        var mapUid = _map.CreateMap(out var mapId, runMapInit: false);
        if (!_loader.TryLoadGrid(mapId, file, out var grid))
        {
            _map.DeleteMap(mapId);
            shell.WriteError($"Could not load {file}. Run it without a name to see the files.");
            return;
        }

        _map.SetAmbientLight(mapId, Color.FromHex("#B0B0B0"));
        EntityManager.EnsureComponent<GravityComponent>(mapUid);
        _gravity.EnableGravity(mapUid);

        var center = grid.Value.Comp.LocalAABB.Center;
        if (shell.Player?.AttachedEntity is { } player)
            _transform.SetMapCoordinates(player, new MapCoordinates(center, mapId));

        shell.WriteLine($"{args[0]}: map {mapId} (not initialized), grid {EntityManager.GetNetEntity(grid.Value.Owner)}, {grid.Value.Comp.LocalAABB.Width:0} x {grid.Value.Comp.LocalAABB.Height:0} tiles. Save an edited copy with: savegrid {EntityManager.GetNetEntity(grid.Value.Owner)} <file>. Delete the map with deletemap {mapId}.");
    }
}
