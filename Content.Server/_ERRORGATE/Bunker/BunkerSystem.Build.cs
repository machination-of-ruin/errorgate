using System.Linq;
using System.Numerics;
using Content.Shared._ERRORGATE.Bunker;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._ERRORGATE.Bunker;

/// <summary>
///     Builds the bunker from a <see cref="BunkerLayout"/>: the vestibule by hand, then walls, props, loot, bosses, radiation and lights
///     for the generated rooms and tunnels.
/// </summary>
public sealed partial class BunkerSystem
{
    private static readonly EntProtoId BrickWallProto = "N14WallBrickGraySlantedIndestructible";
    private static readonly EntProtoId BarrelProto = "BunkerRadiationBarrel";
    private static readonly EntProtoId EmergencyLightProto = "EmergencyLight";
    private static readonly EntProtoId BossProto = "MobSpawnerWalkerBoss";

    // What a room holds. Loot spawners are tier 3, drawn from the global loot table (shared with the surface).
    private sealed record RoomSpec(
        string[] Props,
        string[] Loot,
        string[] Mobs,
        int Barrels,
        bool Military);

    private static readonly Dictionary<BunkerRoomType, RoomSpec> Specs = new()
    {
        [BunkerRoomType.Decon] = new(
            new[] { "LockerSteel", "LockerSteel" },
            new[] { "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3" },
            new[] { "MobSpawnerRunner" }, 1, false),
        [BunkerRoomType.Barracks] = new(
            new[] { "Bed", "Bed", "Bed", "Bed", "LockerSteel", "LockerSteel", "LockerSteel", "TableWood" },
            new[] { "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerLivingTier3", "LootSpawnerLivingTier3" },
            new[] { "MobSpawnerWalkerArmoredMilitary" }, 2, true),
        [BunkerRoomType.Mess] = new(
            new[] { "TableWood", "TableWood", "TableWood", "ChairWood", "ChairWood", "ChairWood", "ChairWood" },
            new[] { "LootSpawnerKitchenTier3", "LootSpawnerKitchenTier3", "LootSpawnerLivingTier3" },
            new[] { "MobSpawnerWalkerLiving", "MobSpawnerRunner" }, 2, false),
        [BunkerRoomType.Control] = new(
            new[] { "ComputerBroken", "ComputerBroken", "ComputerBroken", "ComputerBroken", "TableReinforced", "TableReinforced", "ChairWood" },
            new[] { "LootSpawnerSecurityTier3", "LootSpawnerSecurityTier3", "LootSpawnerIndustrialTier3" },
            new[] { "MobSpawnerRunner" }, 2, false),
        [BunkerRoomType.Generator] = new(
            new[] { "GeneratorBasic15kW", "GeneratorBasic15kW", "CrateEngineeringGear", "CrateEngineeringGear" },
            new[] { "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3" },
            new[] { "MobSpawnerWalkerArmored" }, 2, false),
        [BunkerRoomType.Reactor] = new(
            new[] { "CrateEngineeringGear", "CrateEngineeringGear" },
            new[] { "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3", "LootSpawnerMedicalTier3" },
            new[] { "MobSpawnerWalkerArmoredMilitary", "MobSpawnerWalkerArmoredMilitary" }, 5, false),
        [BunkerRoomType.Medical] = new(
            new[] { "MedicalBed", "MedicalBed", "MedicalBed", "CrateMedical", "CrateMedical", "LockerSteel" },
            new[] { "LootSpawnerMedicalTier3", "LootSpawnerMedicalTier3", "LootSpawnerMedicalTier3" },
            new[] { "MobSpawnerRunner" }, 2, false),
        [BunkerRoomType.Storage] = new(
            new[] { "ShelfMetal", "ShelfMetal", "ShelfMetal", "ShelfMetal", "CrateGenericSteel", "CrateGenericSteel", "CrateGenericSteel" },
            new[] { "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3", "LootSpawnerLivingTier3" },
            new[] { "MobSpawnerRunner", "MobSpawnerWalkerArmored" }, 2, false),
        [BunkerRoomType.AmmoDepot] = new(
            new[] { "Rack", "Rack", "Rack", "CrateGenericSteel", "CrateGenericSteel", "CrateGenericSteel" },
            new[] { "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3" },
            new string[] { }, 2, true),
        [BunkerRoomType.Armory] = new(
            new[] { "GunSafe", "GunSafe", "Rack", "Rack", "Rack" },
            new[] { "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerSecurityTier3" },
            new[] { "MobSpawnerWalkerArmoredMilitary" }, 2, true),
        [BunkerRoomType.Archive] = new(
            new[] { "filingCabinet", "filingCabinet", "filingCabinet", "filingCabinet", "TableWood", "ShelfMetal", "ShelfMetal" },
            new[] { "LootSpawnerSecurityTier3", "LootSpawnerLivingTier3", "LootSpawnerLivingTier3" },
            new[] { "MobSpawnerRunner" }, 2, false),
        [BunkerRoomType.Shaft] = new(
            new string[] { },
            new[] { "LootSpawnerIndustrialTier3" },
            new string[] { }, 1, false),
    };

    private void BuildBunker(EntityUid gridUid, MapGridComponent grid, int seed, out EntityUid vestibule, out EntityCoordinates arrival)
    {
        var layout = BunkerLayout.Generate(seed, new Vector2i(6, 1));

        EntityCoordinates At(int x, int y) => _map.GridTileToLocal(gridUid, grid, new Vector2i(x, y));
        EntityCoordinates AtTile(Vector2i tile) => _map.GridTileToLocal(gridUid, grid, tile);

        // -- Floor ---------------------------------------------------------------------------------------------
        var floors = new Dictionary<Vector2i, string>();

        // The vestibule is 5 by 3 (0..4, 0..2), its door into the bunker is on the east wall at (5, 1)
        for (var x = 0; x <= 4; x++)
        {
            for (var y = 0; y <= 2; y++)
            {
                floors[new Vector2i(x, y)] = "FloorDark";
            }
        }

        floors[new Vector2i(5, 1)] = "FloorSteel";

        foreach (var tile in layout.Floor)
        {
            floors[tile] = layout.Tunnel.Contains(tile) ? "FloorSteel" : "FloorDark";
        }

        foreach (var (pos, tile) in floors)
        {
            _map.SetTile(gridUid, grid, pos, new Tile(_tileDefs[tile].TileId));
        }

        // -- Walls ---------------------------------------------------------------------------------------------
        var built = new HashSet<Vector2i>(floors.Keys);

        // The ring of the vestibule; the hatch up takes the place of one tile of the west wall
        for (var x = -1; x <= 5; x++)
        {
            for (var y = -1; y <= 3; y++)
            {
                var pos = new Vector2i(x, y);
                if (!built.Add(pos))
                    continue;

                _map.SetTile(gridUid, grid, pos, new Tile(_tileDefs["FloorDark"].TileId));
                if (pos != HatchUpTile)
                    Spawn(RoomWallProto, AtTile(pos));
            }
        }

        foreach (var (pos, roomWall) in layout.Walls)
        {
            if (!built.Add(pos))
                continue;

            _map.SetTile(gridUid, grid, pos, new Tile(_tileDefs["FloorDark"].TileId));

            // Some rooms have grey brick walls, the tunnels never do
            var brick = roomWall && layout.RoomOfWall(pos)?.BrickWalls == true;
            Spawn(brick ? BrickWallProto : RoomWallProto, AtTile(pos));
        }

        // -- The vestibule -------------------------------------------------------------------------------------
        vestibule = Spawn(ControllerProto, At(2, 1));
        var door = Spawn(DoorProto, At(5, 1));
        var ladder = Spawn(LadderProto, At(HatchUpTile.X, HatchUpTile.Y));
        _appearance.SetData(ladder, BunkerExitVisuals.Locked, true);

        var console = Spawn(ConsoleProto, At(2, 2));
        Comp<BunkerConsoleComponent>(console).Vestibule = vestibule;

        var vestibuleComp = Comp<BunkerVestibuleComponent>(vestibule);
        vestibuleComp.Door = door;
        vestibuleComp.Ladder = ladder;

        vestibuleComp.Lights.Add(Spawn(LightProto, At(1, 2)));
        var southLight = Spawn(LightProto, At(3, 0));
        _transform.SetLocalRotation(southLight, Angle.FromDegrees(180));
        vestibuleComp.Lights.Add(southLight);

        // -- Contents ------------------------------------------------------------------------------------------
        FillRooms(layout, seed, AtTile);
        FillTunnels(layout, seed, AtTile);

        _airTiles = floors.Keys.ToList();
        arrival = At(0, 1); // the tile next to the hatch
    }

    private void FillRooms(BunkerLayout layout, int seed, Func<Vector2i, EntityCoordinates> at)
    {
        for (var i = 0; i < layout.Rooms.Count; i++)
        {
            var room = layout.Rooms[i];
            var spec = Specs[room.Type];
            var rng = new Random(seed * 7919 + i);

            // Tiles next to a tunnel opening stay empty, so nothing blocks a way in
            bool NearOpening(Vector2i tile)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    for (var dy = -2; dy <= 2; dy++)
                    {
                        if (layout.Tunnel.Contains(new Vector2i(tile.X + dx, tile.Y + dy)))
                            return true;
                    }
                }

                return false;
            }

            var edge = new List<Vector2i>();
            var inner = new List<Vector2i>();
            for (var x = room.X0; x <= room.X1; x++)
            {
                for (var y = room.Y0; y <= room.Y1; y++)
                {
                    var tile = new Vector2i(x, y);
                    if (NearOpening(tile))
                        continue;

                    if (x == room.X0 || x == room.X1 || y == room.Y0 || y == room.Y1)
                        edge.Add(tile);
                    else
                        inner.Add(tile);
                }
            }

            Shuffle(edge, rng);
            Shuffle(inner, rng);

            Vector2i? Take(List<Vector2i> list)
            {
                if (list.Count == 0)
                    return null;

                var tile = list[^1];
                list.RemoveAt(list.Count - 1);
                return tile;
            }

            // Furniture along the walls first, the rest in the room; loot and mobs inside
            foreach (var prop in spec.Props)
            {
                if ((Take(edge) ?? Take(inner)) is { } tile)
                    Spawn(prop, at(tile));
            }

            foreach (var loot in spec.Loot)
            {
                if ((Take(inner) ?? Take(edge)) is { } tile)
                    Spawn(loot, at(tile));
            }

            foreach (var mob in spec.Mobs)
            {
                if ((Take(inner) ?? Take(edge)) is { } tile)
                    Spawn(mob, at(tile));
            }

            // One boss in every room with military loot
            if (spec.Military && (Take(inner) ?? Take(edge)) is { } bossTile)
                Spawn(BossProto, at(bossTile));

            // Radiation, so no part of the bunker is safe
            for (var b = 0; b < spec.Barrels; b++)
            {
                if ((Take(edge) ?? Take(inner)) is { } tile)
                    Spawn(BarrelProto, at(tile));
            }

            // Emergency lights on the wall tiles of the room, spread out
            var lights = new List<Vector2i>();
            for (var x = room.X0; x <= room.X1; x++)
            {
                foreach (var y in new[] { room.Y0, room.Y1 })
                {
                    if (!NearOpening(new Vector2i(x, y)))
                        lights.Add(new Vector2i(x, y));
                }
            }

            for (var y = room.Y0; y <= room.Y1; y++)
            {
                foreach (var x in new[] { room.X0, room.X1 })
                {
                    if (!NearOpening(new Vector2i(x, y)))
                        lights.Add(new Vector2i(x, y));
                }
            }

            Shuffle(lights, rng);
            PlaceLights(layout, lights, 6, at, 4);
        }
    }

    private void FillTunnels(BunkerLayout layout, int seed, Func<Vector2i, EntityCoordinates> at)
    {
        var rng = new Random(seed * 104729);

        // Radiation barrels along the middle of the tunnels
        var path = layout.TunnelPath.Distinct().Where(t => layout.Tunnel.Contains(t)).ToList();
        var placed = new List<Vector2i>();
        foreach (var tile in path.OrderBy(t => t.X * 1000 + t.Y))
        {
            // The barrel stands at the edge of the three wide tunnel, not in the way
            foreach (var side in new[] { new Vector2i(0, 1), new Vector2i(1, 0), new Vector2i(0, -1), new Vector2i(-1, 0) })
            {
                var spot = tile + side;
                if (!layout.Tunnel.Contains(spot) || !HasWallNeighbour(layout, spot))
                    continue;

                if (placed.Any(p => (p - spot).LengthSquared < 36) || (spot - layout.Entry).LengthSquared < 100)
                    break;

                placed.Add(spot);
                Spawn(BarrelProto, at(spot));
                break;
            }
        }

        // Emergency lights along the tunnel walls
        var lightSpots = layout.Tunnel.Where(t => HasWallNeighbour(layout, t)).OrderBy(t => t.X * 1000 + t.Y).ToList();
        Shuffle(lightSpots, rng);
        PlaceLights(layout, lightSpots, 4096, at, 7);

        // A few runners and walkers in the tunnels
        var mobSpots = path.Where(t => (t - layout.Entry).LengthSquared > 400).ToList();
        Shuffle(mobSpots, rng);
        var mobs = new[] { "MobSpawnerRunner", "MobSpawnerWalkerArmoredMilitary", "MobSpawnerRunner", "MobSpawnerWalkerLiving", "MobSpawnerRunnerSmall" };
        for (var i = 0; i < mobs.Length && i < mobSpots.Count; i++)
        {
            Spawn(mobs[i], at(mobSpots[i]));
        }
    }

    private static bool HasWallNeighbour(BunkerLayout layout, Vector2i tile)
    {
        return layout.Walls.ContainsKey(tile + new Vector2i(0, 1)) || layout.Walls.ContainsKey(tile + new Vector2i(0, -1))
            || layout.Walls.ContainsKey(tile + new Vector2i(1, 0)) || layout.Walls.ContainsKey(tile + new Vector2i(-1, 0));
    }

    /// <summary>
    ///     Always-on emergency lights on the floor tiles beside a wall, turned to face away from it, at least
    ///     <paramref name="spacing"/> tiles apart.
    /// </summary>
    private void PlaceLights(BunkerLayout layout, List<Vector2i> candidates, int max, Func<Vector2i, EntityCoordinates> at, int spacing)
    {
        var picked = new List<Vector2i>();
        foreach (var tile in candidates)
        {
            if (picked.Count >= max)
                break;

            if (picked.Any(p => (p - tile).LengthSquared < spacing * spacing))
                continue;

            // Which way the wall is: the light faces the other way (it hangs on the wall like the ones on the surface map)
            double? angle = null;
            if (layout.Walls.ContainsKey(tile + new Vector2i(0, 1)))
                angle = 0;
            else if (layout.Walls.ContainsKey(tile + new Vector2i(0, -1)))
                angle = 180;
            else if (layout.Walls.ContainsKey(tile + new Vector2i(1, 0)))
                angle = 270;
            else if (layout.Walls.ContainsKey(tile + new Vector2i(-1, 0)))
                angle = 90;

            if (angle == null)
                continue;

            picked.Add(tile);
            var light = Spawn(EmergencyLightProto, at(tile));
            _transform.SetLocalRotation(light, Angle.FromDegrees(angle.Value));
        }
    }

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
