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
    private static readonly EntProtoId ReactorProto = "BunkerReactor";
    private static readonly EntProtoId CatwalkProto = "Catwalk";
    private static readonly EntProtoId RoomDoorProto = "BunkerRoomDoor";

    // What a room holds. Loot spawners are tier 3, drawn from the global loot table (shared with the surface). Middle props stand in
    // the room, props along the walls.
    private sealed record RoomSpec(
        string[] Props,
        string[] Middle,
        string[] Loot,
        string[] Mobs,
        bool Military,
        string Floor);

    private static readonly Dictionary<BunkerRoomType, RoomSpec> Specs = new()
    {
        [BunkerRoomType.Decon] = new(
            new[] { "LockerSteel", "LockerSteel" },
            new[] { "TableReinforced", "TableReinforced" },
            new[] { "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3" },
            new[] { "MobSpawnerRunner" }, false, "FloorShowroom"),
        [BunkerRoomType.Barracks] = new(
            new[] { "LockerSteel", "LockerSteel", "LockerSteel", "TableWood" },
            new[] { "Bed", "Bed", "Bed", "Bed", "Bed", "Bed" },
            new[] { "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerLivingTier3", "LootSpawnerLivingTier3" },
            new[] { "MobSpawnerWalkerArmoredMilitary" }, true, "FloorLino"),
        [BunkerRoomType.Mess] = new(
            new[] { "TableWood", "TableWood" },
            new[] { "TableWood", "TableWood", "TableWood", "TableWood", "ChairWood", "ChairWood", "ChairWood", "ChairWood" },
            new[] { "LootSpawnerKitchenTier3", "LootSpawnerKitchenTier3", "LootSpawnerLivingTier3" },
            new[] { "MobSpawnerWalkerLiving", "MobSpawnerRunner" }, false, "FloorKitchen"),
        [BunkerRoomType.Control] = new(
            new[] { "ComputerBroken", "ComputerBroken", "ComputerBroken", "ComputerBroken" },
            new[] { "TableReinforced", "TableReinforced", "ChairOfficeDark", "ChairOfficeDark" },
            new[] { "LootSpawnerSecurityTier3", "LootSpawnerSecurityTier3", "LootSpawnerIndustrialTier3" },
            new[] { "MobSpawnerRunner" }, false, "FloorGreenCircuit"),
        [BunkerRoomType.Generator] = new(
            new[] { "CrateEngineeringGear", "CrateEngineeringGear" },
            new[] { "GeneratorBasic15kW", "GeneratorBasic15kW", "GeneratorBasic15kW" },
            new[] { "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3" },
            new[] { "MobSpawnerWalkerArmored" }, false, "FloorSteelDirty"),
        [BunkerRoomType.Reactor] = new(
            new[] { "CrateEngineeringGear", "CrateEngineeringGear" },
            new string[] { },
            new[] { "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3", "LootSpawnerMedicalTier3" },
            new[] { "MobSpawnerWalkerArmoredMilitary", "MobSpawnerWalkerArmoredMilitary" }, false, "FloorReinforced"),
        [BunkerRoomType.Medical] = new(
            new[] { "CrateMedical", "CrateMedical", "LockerSteel" },
            new[] { "MedicalBed", "MedicalBed", "MedicalBed", "MedicalBed" },
            new[] { "LootSpawnerMedicalTier3", "LootSpawnerMedicalTier3", "LootSpawnerMedicalTier3" },
            new[] { "MobSpawnerRunner" }, false, "FloorWhite"),
        [BunkerRoomType.Storage] = new(
            new[] { "ShelfMetal", "ShelfMetal", "ShelfMetal", "ShelfMetal" },
            new[] { "CrateGenericSteel", "CrateGenericSteel", "CrateGenericSteel", "CrateGenericSteel" },
            new[] { "LootSpawnerIndustrialTier3", "LootSpawnerIndustrialTier3", "LootSpawnerLivingTier3" },
            new[] { "MobSpawnerRunner", "MobSpawnerWalkerArmored" }, false, "FloorConcrete"),
        [BunkerRoomType.AmmoDepot] = new(
            new[] { "Rack", "Rack", "Rack" },
            new[] { "CrateGenericSteel", "CrateGenericSteel", "Rack" },
            new[] { "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3" },
            new string[] { }, true, "FloorDarkMono"),
        [BunkerRoomType.Armory] = new(
            new[] { "GunSafe", "GunSafe", "Rack" },
            new[] { "Rack", "Rack" },
            new[] { "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerMilitaryTier3", "LootSpawnerSecurityTier3" },
            new[] { "MobSpawnerWalkerArmoredMilitary" }, true, "FloorBlueCircuit"),
        [BunkerRoomType.Archive] = new(
            new[] { "filingCabinet", "filingCabinet", "ShelfMetal", "ShelfMetal" },
            new[] { "filingCabinet", "filingCabinet", "TableWood" },
            new[] { "LootSpawnerSecurityTier3", "LootSpawnerLivingTier3", "LootSpawnerLivingTier3" },
            new[] { "MobSpawnerRunner" }, false, "FloorConcreteSmooth"),
        [BunkerRoomType.Shaft] = new(
            new string[] { },
            new string[] { },
            new[] { "LootSpawnerIndustrialTier3" },
            new string[] { }, false, "FloorElevatorShaft"),
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
            floors[tile] = "FloorSteel";
        }

        // Every kind of room has a floor of its own, so it can be told apart
        foreach (var room in layout.Rooms)
        {
            for (var x = room.X0; x <= room.X1; x++)
            {
                for (var y = room.Y0; y <= room.Y1; y++)
                {
                    floors[new Vector2i(x, y)] = Specs[room.Type].Floor;
                }
            }
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

        // The second console, inside the bunker by the door, outside the airlock room
        var innerConsole = Spawn(ConsoleProto, At(6, 2));
        Comp<BunkerConsoleComponent>(innerConsole).Vestibule = vestibule;

        // The doors of the rooms, opened by hand
        foreach (var roomDoor in layout.Doors)
        {
            Spawn(RoomDoorProto, AtTile(roomDoor.Tile));
        }

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
            var doors = layout.Doors.Where(d => d.Room == i).ToList();

            // Never filled: the three tiles in front of every door inside the room
            var reserved = new HashSet<Vector2i>();
            foreach (var door in doors)
            {
                var side = new Vector2i(Math.Abs(door.Dir.Y), Math.Abs(door.Dir.X));
                for (var k = 1; k <= 3; k++)
                {
                    for (var l = -1; l <= 1; l++)
                    {
                        reserved.Add(door.Tile - door.Dir * k + side * l);
                    }
                }
            }

            var occupied = new HashSet<Vector2i>(); // blocks walking
            var used = new HashSet<Vector2i>(); // has something on it

            bool Free(Vector2i t) => room.Contains(t) && !used.Contains(t) && !reserved.Contains(t);

            // Everything still free can be reached from the doors
            bool Connected()
            {
                if (doors.Count == 0)
                    return true;

                var start = doors[0].Tile - doors[0].Dir;
                var seen = new HashSet<Vector2i> { start };
                var stack = new Stack<Vector2i>();
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    foreach (var d in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
                    {
                        var next = cur + d;
                        if (room.Contains(next) && !occupied.Contains(next) && seen.Add(next))
                            stack.Push(next);
                    }
                }

                var area = (room.X1 - room.X0 + 1) * (room.Y1 - room.Y0 + 1);
                return seen.Count == area - occupied.Count;
            }

            bool TryBlock(Vector2i t)
            {
                if (!Free(t))
                    return false;

                occupied.Add(t);
                if (!Connected())
                {
                    occupied.Remove(t);
                    return false;
                }

                used.Add(t);
                return true;
            }

            bool IsEdge(Vector2i t) => t.X == room.X0 || t.X == room.X1 || t.Y == room.Y0 || t.Y == room.Y1;

            List<Vector2i> Tiles(bool edge)
            {
                var list = new List<Vector2i>();
                for (var x = room.X0; x <= room.X1; x++)
                {
                    for (var y = room.Y0; y <= room.Y1; y++)
                    {
                        var t = new Vector2i(x, y);
                        if (IsEdge(t) == edge && Free(t))
                            list.Add(t);
                    }
                }

                Shuffle(list, rng);
                return list;
            }

            bool Isolated(Vector2i t)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        if (occupied.Contains(new Vector2i(t.X + dx, t.Y + dy)))
                            return false;
                    }
                }

                return true;
            }

            var area = (room.X1 - room.X0 + 1) * (room.Y1 - room.Y0 + 1);

            // The reactor in the middle, catwalks around it and from every door
            if (room.Type == BunkerRoomType.Reactor)
            {
                var c = room.Center;
                Spawn(ReactorProto, at(c));
                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var t = new Vector2i(c.X + dx, c.Y + dy);
                        occupied.Add(t);
                        used.Add(t);
                    }
                }

                var walk = new HashSet<Vector2i>();
                for (var dx = -2; dx <= 2; dx++)
                {
                    for (var dy = -2; dy <= 2; dy++)
                    {
                        if (Math.Abs(dx) == 2 || Math.Abs(dy) == 2)
                            walk.Add(new Vector2i(c.X + dx, c.Y + dy));
                    }
                }

                foreach (var door in doors)
                {
                    var t = door.Tile - door.Dir;
                    while (Math.Abs(t.X - c.X) > 2 || Math.Abs(t.Y - c.Y) > 2)
                    {
                        walk.Add(t);

                        // In along the door's way until level with the reactor, then across
                        var rel = t - c;
                        if (door.Dir.X != 0 ? Math.Abs(rel.X) > 2 : Math.Abs(rel.Y) > 2)
                            t -= door.Dir;
                        else
                            t -= new Vector2i(Math.Sign(rel.X) * (door.Dir.X != 0 ? 0 : 1), Math.Sign(rel.Y) * (door.Dir.Y != 0 ? 0 : 1));
                    }
                }

                foreach (var t in walk)
                {
                    if (!room.Contains(t))
                        continue;

                    Spawn(CatwalkProto, at(t));
                    used.Add(t);
                    reserved.Add(t);
                }
            }

            // Line of sight blockers: concrete columns and short wall pieces in the middle of the room
            if (area >= 40)
            {
                var columns = area >= 70 ? rng.Next(3, 6) : rng.Next(1, 3);
                foreach (var t in Tiles(false))
                {
                    if (columns == 0)
                        break;

                    if (!Isolated(t) || !TryBlock(t))
                        continue;

                    Spawn(RoomWallProto, at(t));
                    columns--;
                }
            }

            if (area >= 80)
            {
                var pieces = rng.Next(1, 3);
                foreach (var t in Tiles(false))
                {
                    if (pieces == 0)
                        break;

                    var along = rng.Next(2) == 0 ? new Vector2i(1, 0) : new Vector2i(0, 1);
                    var cells = new[] { t, t + along, t + along * 2 };
                    if (!cells.All(x => Free(x) && !IsEdge(x) && Isolated(x)))
                        continue;

                    var placed = new List<Vector2i>();
                    foreach (var cell in cells)
                    {
                        if (TryBlock(cell))
                        {
                            placed.Add(cell);
                        }
                        else
                        {
                            foreach (var undo in placed)
                            {
                                occupied.Remove(undo);
                                used.Remove(undo);
                            }

                            placed.Clear();
                            break;
                        }
                    }

                    foreach (var cell in placed)
                    {
                        Spawn(RoomWallProto, at(cell));
                    }

                    if (placed.Count > 0)
                        pieces--;
                }
            }

            // Cover: crates in the middle
            for (var n = Math.Min(3, area / 35); n > 0; n--)
            {
                foreach (var t in Tiles(false))
                {
                    if (TryBlock(t))
                    {
                        Spawn("CrateGenericSteel", at(t));
                        break;
                    }
                }
            }

            // What makes the room what it is: the middle first, then along the walls
            foreach (var prop in spec.Middle)
            {
                foreach (var t in Tiles(false).Concat(Tiles(true)))
                {
                    if (TryBlock(t))
                    {
                        Spawn(prop, at(t));
                        break;
                    }
                }
            }

            foreach (var prop in spec.Props)
            {
                foreach (var t in Tiles(true).Concat(Tiles(false)))
                {
                    if (TryBlock(t))
                    {
                        Spawn(prop, at(t));
                        break;
                    }
                }
            }

            void Put(string proto)
            {
                foreach (var t in Tiles(false).Concat(Tiles(true)))
                {
                    used.Add(t);
                    Spawn(proto, at(t));
                    return;
                }
            }

            foreach (var loot in spec.Loot)
            {
                Put(loot);
            }

            foreach (var mob in spec.Mobs)
            {
                Put(mob);
            }

            // One boss in every room with military loot
            if (spec.Military)
                Put(BossProto);
        }
    }

    private void FillTunnels(BunkerLayout layout, int seed, Func<Vector2i, EntityCoordinates> at)
    {
        var rng = new Random(seed * 104729);

        var path = layout.TunnelPath.Distinct().Where(t => layout.Tunnel.Contains(t)).ToList();
        var doorTiles = layout.Doors.Select(d => d.Tile).ToList();

        bool NearDoor(Vector2i t, int range) => doorTiles.Any(d => Math.Max(Math.Abs(d.X - t.X), Math.Abs(d.Y - t.Y)) <= range);

        // Radiation barrels and crates for cover stand at the edge of the corridor, not in the way. The barrels are far apart
        // (they reach only a few tiles) and never near a door or the way in.
        var barrels = new List<Vector2i>();
        var crates = new List<Vector2i>();
        foreach (var tile in path.OrderBy(t => t.X * 1000 + t.Y))
        {
            foreach (var side in new[] { new Vector2i(0, 1), new Vector2i(1, 0), new Vector2i(0, -1), new Vector2i(-1, 0) })
            {
                var spot = tile + side;
                if (!layout.Tunnel.Contains(spot) || !HasWallNeighbour(layout, spot) || NearDoor(spot, 3))
                    continue;

                if (barrels.Contains(spot) || crates.Contains(spot))
                    break;

                var fromEntry = (spot - layout.Entry).LengthSquared;
                if (fromEntry > 169 && !NearDoor(spot, 6) && barrels.All(p => (p - spot).LengthSquared >= 100))
                {
                    barrels.Add(spot);
                    Spawn(BarrelProto, at(spot));
                }
                else if (fromEntry > 100 && crates.All(p => (p - spot).LengthSquared >= 64) && rng.NextDouble() < 0.5)
                {
                    crates.Add(spot);
                    Spawn("CrateGenericSteel", at(spot));
                }

                break;
            }
        }

        // Emergency lights along the corridor walls, the only light in the bunker
        var lightSpots = layout.Tunnel.Where(t => HasWallNeighbour(layout, t) && !NearDoor(t, 1)).OrderBy(t => t.X * 1000 + t.Y).ToList();
        Shuffle(lightSpots, rng);
        PlaceLights(layout, lightSpots, 4096, at, 7);

        // A few runners and walkers in the corridors
        var mobSpots = path.Where(t => (t - layout.Entry).LengthSquared > 400 && !NearDoor(t, 3)).ToList();
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
