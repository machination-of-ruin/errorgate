using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server._ERRORGATE.Despawn;
using Content.Server._ERRORGATE.LootManager;
using Content.Shared.Item;
using Content.Shared._ERRORGATE.World;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     The web: which locations are joined by which gates, the gates themselves, tunnels, and the storm that wipes a location on a timer
///     (docs/design/world.md). The storm gives a warning, closes the way in, kills whoever is still inside by deleting the map, and
///     builds the location again with a new layout.
/// </summary>
public sealed partial class WorldSystem
{
    private static readonly SoundSpecifier GateSound = new SoundPathSpecifier("/Audio/Machines/blastdoor.ogg");
    private static readonly SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;

    [Dependency] private readonly Bunker.BunkerSystem _bunker = default!;

    private WorldGraphPrototype? _graph;
    private readonly Dictionary<(string Location, string Gate), (EntityUid Gate, EntityCoordinates Arrival)> _gates = new();

    /// <summary>Whether a location is open to people, or the storm is coming for it.</summary>
    public enum LocationState : byte
    {
        Open,
        Warning,
    }

    private readonly Dictionary<string, (LocationState State, TimeSpan? Strike)> _storm = new();

    public WorldGraphPrototype? Graph => _graph;

    /// <summary>The ground grid of the location with the spawn zone, if there is one.</summary>
    public EntityUid? SpawnGrid()
    {
        foreach (var loc in _locations.Values)
        {
            if (loc.Layout is { SpawnTiles.Count: > 0 } && !TerminatingOrDeleted(loc.Ground))
                return loc.Ground;
        }

        return null;
    }

    /// <summary>A location of the world was built (again).</summary>
    public sealed record WorldLocationBuilt(string Id);

    /// <summary>
    ///     Whether people can come into the world now: the location with the spawn spots is built, is not being built again and has no storm
    ///     coming. Nobody is put into the world while it is not (they would land on no ground at all).
    /// </summary>
    public bool SpawnReady()
    {
        if (ActiveGraph() is not { } graph)
            return false;

        foreach (var proto in _proto.EnumeratePrototypes<WorldLocationPrototype>())
        {
            if (!proto.SpawnZone || !graph.Locations.Contains(proto.ID))
                continue;

            if (!IsBuilt(proto.ID) || _building.Contains(proto.ID) || StormOf(proto.ID).State != LocationState.Open)
                return false;
        }

        return true;
    }

    private static readonly Dictionary<string, string[]> GuaranteedPlaces = new()
    {
        ["BunkerKey"] = new[] { "Military", "Security" },
        ["FactoryCard"] = new[] { "Living", "Industrial" },
    };

    /// <summary>
    ///     Puts an item out for certain: on a loot spawner of a fitting kind (the military ones for the laboratory key, the living ones for
    ///     the factory card), on a tile with no item on it. It counts against the world's caps and stays until the location is wiped.
    /// </summary>
    private void PlaceGuaranteed(string item, MapId mapId, int seed)
    {
        var spots = new List<(EntityUid Uid, LootSpawnerComponent Spawner, EntityCoordinates At)>();
        var query = EntityQueryEnumerator<LootSpawnerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawner, out var xform))
        {
            if (xform.MapID == mapId)
                spots.Add((uid, spawner, xform.Coordinates));
        }

        if (spots.Count == 0)
        {
            Log.Warning($"World: no loot spawner to put {item} on");
            return;
        }

        var rng = new Random(seed ^ StableHash(item));
        var preferred = GuaranteedPlaces.TryGetValue(item, out var places) ? spots.Where(s => places.Contains(s.Spawner.Location)).ToList() : spots;
        foreach (var spot in (preferred.Count > 0 ? preferred : spots).OrderBy(_ => rng.Next()).Take(40))
        {
            if (_lookup.GetEntitiesIntersecting(spot.At, LookupFlags.Uncontained).Any(e => HasComp<ItemComponent>(e) || HasComp<DespawnItemComponent>(e)))
                continue;

            var thing = Spawn(item, spot.At);
            if (TryComp<DespawnItemComponent>(thing, out var despawn))
            {
                despawn.Despawn = false;
                despawn.DespawnAt = null;
            }

            _lootManager.RegisterSpawn(item, spot.Uid);
            return;
        }

        Log.Warning($"World: found no free tile for {item}");
    }

    private EntityUid? _lootEntity;

    /// <summary>
    ///     One loot manager for the whole world, on an entity of its own (not on a map): every spawner of every location draws from the same
    ///     table and counts against the same caps, and what a storm deletes is given back (see LootManagerSystem).
    /// </summary>
    private void EnsureLootManager()
    {
        if (_lootEntity is { } existing && !TerminatingOrDeleted(existing))
            return;

        var table = ActiveGraph()?.LootTable ?? "LootTableGlobal";
        var uid = Spawn(null, MapCoordinates.Nullspace);
        _meta.SetEntityName(uid, "World loot manager");
        AddComp(uid, new LootManagerComponent { GlobalLootTablePrototype = table }); // the table is read when the component starts, so it is set first
        _lootEntity = uid;
    }

    /// <summary>Forgets everything built (the round ended and its maps are gone).</summary>
    public void Forget()
    {
        if (_lootEntity is { } loot && !TerminatingOrDeleted(loot))
            QueueDel(loot);

        _lootEntity = null;
        _locations.Clear();
        _gates.Clear();
        _storm.Clear();
        _notice.Clear();
        _pendingAir.Clear();
        _jobs.Clear();
        _building.Clear();
        _graph = null;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WorldTransitionComponent, ActivateInWorldEvent>(OnGateActivate);
    }

    // -- The graph ------------------------------------------------------------------------------------------------

    /// <summary>A hash of a name that is the same on every run (string.GetHashCode is not).</summary>
    private static int StableHash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s)
        {
            h = (h ^ c) * 16777619u;
        }

        return (int) (h & 0x7FFFFFFF);
    }

    private WorldGraphPrototype? ActiveGraph()
    {
        if (_graph != null)
            return _graph;

        return _proto.TryIndex<WorldGraphPrototype>(_cfg.GetCVar(WorldCVars.Graph), out var graph) ? graph : null;
    }

    private static (string Location, string Gate) Split(string gate)
    {
        var dot = gate.IndexOf('.');
        return dot < 0 ? (gate, string.Empty) : (gate[..dot], gate[(dot + 1)..]);
    }

    /// <summary>The names of the gates a location has in the graph, in the order the links list them.</summary>
    private List<string> GatesOf(string location)
    {
        var result = new List<string>();
        if (ActiveGraph() is not { } graph)
            return result;

        foreach (var link in graph.Links)
        {
            foreach (var end in new[] { link.A, link.B })
            {
                var (loc, gate) = Split(end);
                if (loc == location && !result.Contains(gate))
                    result.Add(gate);
            }
        }

        return result;
    }

    private (string Location, string Gate)? PartnerOf(string location, string gate)
    {
        if (ActiveGraph() is not { } graph)
            return null;

        foreach (var link in graph.Links)
        {
            var a = Split(link.A);
            var b = Split(link.B);
            if (a == (location, gate))
                return b;

            if (b == (location, gate))
                return a;
        }

        return null;
    }

    /// <summary>Builds every location of a graph. The storm timers start when the locations are built.</summary>
    public bool TryBuildGraph(string graphId, int? seed, out string error)
    {
        error = string.Empty;
        if (!_proto.TryIndex<WorldGraphPrototype>(graphId, out var graph))
        {
            error = $"no graph '{graphId}'";
            return false;
        }

        foreach (var id in _locations.Keys.ToList())
        {
            Teardown(id);
        }

        _graph = graph;
        var baseSeed = seed ?? _random.Next();
        foreach (var location in graph.Locations)
        {
            if (!TryBuild(location, baseSeed ^ StableHash(location.Id), out error))
                return false;
        }

        foreach (var location in graph.Locations)
        {
            ScheduleStrike(location);
        }

        return true;
    }

    // -- Gates ----------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Barricades whose picture is a strip along the bottom of the tile: sandbags and metal barricades lie east to west as built, so
    ///     they are turned a quarter where the line they make has to run north to south.
    /// </summary>
    private void TurnBarricade(EntityUid barricade, string proto, bool lineRunsNorthSouth)
    {
        if (lineRunsNorthSouth && proto is "N14BarricadeSandbagSingle" or "N14BarricadeMetal" or "N14BarricadeRazorwire")
            _transform.SetLocalRotation(barricade, Angle.FromDegrees(90));
    }

    /// <summary>Decals are placed by their bottom left corner, so a decal on a tile goes half a tile down and left of the tile's middle.</summary>
    private void AddDecal(string id, EntityCoordinates tileCentre)
    {
        _decals.TryAddDecal(id, tileCentre.Offset(new Vector2(-0.5f, -0.5f)), out _);
    }

    private static readonly string[] PlazaBarricades =
    {
        "N14BarricadeMetal", "N14BarricadeSandbagSingle", "N14BarricadeRazorwire", "N14BarricadeTanktrap",
    };

    /// <summary>
    ///     A gate on a plaza of its own (a small grid with a steel floor, hazard marks, lamps and barricades on both flanks), so a way
    ///     into another location is easy to see and to find in the dark. The plaza replaces the ground under it. A hand-built plaza can
    ///     take its place later. Returns the plaza grid.
    /// </summary>
    private EntityUid PlaceGatePlaza(string location, string gateId, Entity<MapGridComponent> ground, MapId mapId, GatePlacement place, LocationLayout layout)
    {
        var plaza = _mapManager.CreateGridEntity(mapId);
        _meta.SetEntityName(plaza.Owner, $"{location} gate plaza ({gateId})");
        _transform.SetWorldPosition(plaza.Owner, _transform.GetWorldPosition(ground.Owner)); // the same offset as the ground, so the tile numbers agree
        var worn = (ContentTileDefinition) _tileDefs["FloorSteelDirty"];
        var paved = (ContentTileDefinition) _tileDefs["FloorSteelPavement"];
        var tiles = new List<(Vector2i, Tile)>();
        var holes = new List<(Vector2i, Tile)>();
        foreach (var (tile, _, l) in LocationLayout.PlazaTiles(place))
        {
            var def = Math.Abs(l) <= 1 ? paved : worn;
            tiles.Add((tile, new Tile(def.TileId, 0, _tile.PickVariant(def))));
            holes.Add((tile, Tile.Empty));
        }

        _map.SetTiles(plaza.Owner, plaza.Comp, tiles);
        _map.SetTiles(ground.Owner, ground.Comp, holes);
        _physics.SetBodyType(plaza.Owner, BodyType.Static);

        EntityCoordinates At(int f, int l) => _map.GridTileToLocal(plaza.Owner, plaza.Comp, LocationLayout.PlazaTile(place, f, l));

        // Where the plaza is cut into the border, the rock continues behind the gate on the plaza's own grid, so the gate is part of the wall
        var borderProto = _proto.Index<WorldLocationPrototype>(location).Border;
        for (var l = -3; l <= 3; l++)
        {
            if (layout.Dist(LocationLayout.PlazaTile(place, 1, l)) <= 2)
                Spawn(borderProto, At(1, l));
        }

        // Hazard marks around the gate, lamps beside it, barricades along both flanks (the middle five tiles stay open)
        foreach (var l in new[] { -1, 0, 1 })
        {
            AddDecal("WarnFull", At(-1, l));
        }

        AddDecal("WarnFull", At(0, -1));
        AddDecal("WarnFull", At(0, 1));

        Spawn("LightPostSmall", At(0, -2));
        Spawn("LightPostSmall", At(0, 2));

        var rng = new Random(place.Tile.X * 7919 + place.Tile.Y);
        // The flanks run along the way to the gate
        var vertical = place.Facing % 2 == 0;
        foreach (var side in new[] { -3, 3 })
        {
            for (var f = -3; f <= -1; f++)
            {
                var kind = PlazaBarricades[rng.Next(PlazaBarricades.Length)];
                TurnBarricade(Spawn(kind, At(f, side)), kind, vertical);
            }

            TurnBarricade(Spawn("N14BarricadeSandbagSingle", At(0, side)), "N14BarricadeSandbagSingle", vertical);
        }

        // People arrive on the tile in front of the gate
        PlaceGate(location, gateId, plaza, place.Tile, LocationLayout.PlazaTile(place, -1, 0), place.Facing);
        return plaza.Owner;
    }

    /// <summary>
    ///     The far end of the village's main street: a collapsed tunnel mouth in the border. A plaza of its own with a cave floor, rock walls
    ///     along both sides and rubble across the end. Nothing leads anywhere: it only closes the road.
    /// </summary>
    private EntityUid PlaceRubblePlaza(string location, Entity<MapGridComponent> ground, MapId mapId, GatePlacement place)
    {
        var plaza = _mapManager.CreateGridEntity(mapId);
        _meta.SetEntityName(plaza.Owner, $"{location} collapsed tunnel plaza");
        _transform.SetWorldPosition(plaza.Owner, _transform.GetWorldPosition(ground.Owner));
        var cave = (ContentTileDefinition) _tileDefs["FloorCave"];
        var tiles = new List<(Vector2i, Tile)>();
        var holes = new List<(Vector2i, Tile)>();
        foreach (var (tile, _, _) in LocationLayout.PlazaTiles(place))
        {
            tiles.Add((tile, new Tile(cave.TileId, 0, _tile.PickVariant(cave))));
            holes.Add((tile, Tile.Empty));
        }

        _map.SetTiles(plaza.Owner, plaza.Comp, tiles);
        _map.SetTiles(ground.Owner, ground.Comp, holes);
        _physics.SetBodyType(plaza.Owner, BodyType.Static);

        EntityCoordinates At(int f, int l) => _map.GridTileToLocal(plaza.Owner, plaza.Comp, LocationLayout.PlazaTile(place, f, l));

        // Rock walls on both sides, and the rubble that blocks the end
        foreach (var side in new[] { -3, 3 })
        {
            for (var f = -3; f <= 1; f++)
            {
                Spawn("WorldBorderWall", At(f, side));
            }
        }

        var rng = new Random(place.Tile.X * 7919 + place.Tile.Y);
        for (var l = -2; l <= 2; l++)
        {
            Spawn("N14WallRockBasalt", At(0, l));
            Spawn("N14WallRockBasalt", At(1, l));
            if (rng.Next(3) != 0)
                Spawn("N14WallRockBasalt", At(-1, l));
        }

        if (rng.Next(2) == 0)
            Spawn("N14WallRockBasalt", At(-2, rng.Next(-2, 3)));

        return plaza.Owner;
    }

    /// <summary>A gate in a tunnel: hazard marks in front of it and a lamp on each side.</summary>
    private void DecorateTunnelGate(string location, string gateId, Entity<MapGridComponent> ground, GatePlacement place)
    {
        foreach (var l in new[] { -1, 0, 1 })
        {
            AddDecal("WarnFull", _map.GridTileToLocal(ground.Owner, ground.Comp, LocationLayout.PlazaTile(place, -1, l)));
        }

        Spawn("LightPostSmall", _map.GridTileToLocal(ground.Owner, ground.Comp, LocationLayout.PlazaTile(place, 0, -1)));
        Spawn("LightPostSmall", _map.GridTileToLocal(ground.Owner, ground.Comp, LocationLayout.PlazaTile(place, 0, 1)));
        PlaceGate(location, gateId, ground, place.Tile, LocationLayout.PlazaTile(place, -1, 0), place.Facing);
    }

    private void PlaceGate(string location, string gate, Entity<MapGridComponent> ground, Vector2i tile, Vector2i arrival, int facing)
    {
        var proto = _proto.Index<WorldLocationPrototype>(location);
        var entity = Spawn(proto.Gate, _map.GridTileToLocal(ground.Owner, ground.Comp, tile));
        if (PartnerOf(location, gate) is { } beyond && _proto.TryIndex<WorldLocationPrototype>(beyond.Location, out var beyondProto))
            _meta.SetEntityName(entity, $"hatch to {beyondProto.Name}");
        var comp = EnsureComp<WorldTransitionComponent>(entity);
        comp.Location = location;
        comp.Gate = gate;
        _gates[(location, gate)] = (entity, _map.GridTileToLocal(ground.Owner, ground.Comp, arrival));
    }

    private void ForgetGates(string location)
    {
        foreach (var key in _gates.Keys.Where(k => k.Location == location).ToList())
        {
            _gates.Remove(key);
        }
    }

    /// <summary>Whether people can come into a location now: it is built and no storm is about to wipe it.</summary>
    public bool CanEnter(string location)
    {
        return IsBuilt(location) && !_building.Contains(location) && !AirPending(location) && (!_storm.TryGetValue(location, out var state) || state.State == LocationState.Open);
    }

    /// <summary>Whether a grid of the location still waits for its air (people would arrive in a vacuum).</summary>
    private bool AirPending(string location)
    {
        if (!_locations.TryGetValue(location, out var loc))
            return false;

        foreach (var pending in _pendingAir)
        {
            if (!TerminatingOrDeleted(pending.Grid) && Transform(pending.Grid).MapID == loc.MapId)
                return true;
        }

        return false;
    }

    private void OnGateActivate(Entity<WorldTransitionComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        if (PartnerOf(ent.Comp.Location, ent.Comp.Gate) is not { } partner
            || !_gates.TryGetValue(partner, out var destination)
            || !CanEnter(partner.Location))
        {
            _audio.PlayPvs(DenySound, ent);
            return;
        }

        Move(args.User, destination.Arrival);
    }

    /// <summary>Moves a person and whatever they pull. The pull is taken up again on the other side.</summary>
    private void Move(EntityUid user, EntityCoordinates to)
    {
        EntityUid? pulled = null;
        if (_pulling.TryGetPulledEntity(user, out var pulling) && TryComp<PullableComponent>(pulling, out var pullable))
        {
            pulled = pulling;
            _pulling.TryStopPull(pulling.Value, pullable, user);
        }

        _audio.PlayPvs(GateSound, user);
        _transform.SetCoordinates(user, to);
        _transform.AttachToGridOrMap(user);

        if (pulled is { } other)
        {
            _transform.SetCoordinates(other, to);
            _transform.AttachToGridOrMap(other);
            _pulling.TryStartPull(user, other);
        }

        _audio.PlayPvs(GateSound, user);
    }

    /// <summary>Where a gate sends people, for the admin command.</summary>
    public bool TryGetArrival(string location, string gate, out EntityCoordinates arrival)
    {
        if (_gates.TryGetValue((location, gate), out var g))
        {
            arrival = g.Arrival;
            return true;
        }

        arrival = default;
        return false;
    }

    public IEnumerable<(string Location, string Gate)> GateNames => _gates.Keys;

    // -- Tunnels --------------------------------------------------------------------------------------------------

    private bool BuildTunnel(WorldLocationPrototype proto, int? seed, List<string> gates, out string error)
    {
        error = string.Empty;
        var watch = Stopwatch.StartNew();
        var entitiesBefore = EntityManager.EntityCount;
        var useSeed = seed ?? _random.Next();
        var layout = TunnelLayout.Generate(useSeed, proto.Size, gates, proto.Mobs.Count > 0 ? proto.MobCount : 0);

        var mapUid = CreateLocationMap(proto, out var mapId);
        var ground = _mapManager.CreateGridEntity(mapId);
        _meta.SetEntityName(ground.Owner, $"{proto.ID} ground");
        var floorDef = (ContentTileDefinition) _tileDefs[proto.Ground];
        var tiles = new List<(Vector2i, Tile)>();
        foreach (var t in layout.Floor.Concat(layout.Walls))
        {
            tiles.Add((t, new Tile(floorDef.TileId, 0, _tile.PickVariant(floorDef))));
        }

        _map.SetTiles(ground.Owner, ground.Comp, tiles);
        _physics.SetBodyType(ground.Owner, BodyType.Static);
        CentreOnOrigin(ground);

        foreach (var wall in layout.Walls)
        {
            Spawn(proto.Border, _map.GridTileToLocal(ground.Owner, ground.Comp, wall));
        }

        foreach (var rubble in layout.Rubble)
        {
            Spawn(RubbleProto, _map.GridTileToLocal(ground.Owner, ground.Comp, rubble));
        }

        foreach (var crate in layout.Cover)
        {
            Spawn(CoverProto, _map.GridTileToLocal(ground.Owner, ground.Comp, crate));
        }

        if (proto.Post is { } post)
        {
            foreach (var light in layout.Lights)
            {
                Spawn(post, _map.GridTileToLocal(ground.Owner, ground.Comp, light));
            }
        }

        var mobRng = new Random(useSeed ^ 0x3B0B);
        foreach (var point in layout.MobPoints)
        {
            Spawn(proto.Mobs[mobRng.Next(proto.Mobs.Count)], _map.GridTileToLocal(ground.Owner, ground.Comp, point));
        }

        foreach (var (gateId, place) in layout.Gates)
        {
            // The way into a laboratory is the bunker hatch (see LinkBunkerEntrances), not a plain gate
            if (PartnerOf(proto.ID, gateId) is { } partner && _proto.TryIndex<WorldLocationPrototype>(partner.Location, out var other) && other.Style == "laboratory")
                continue;

            DecorateTunnelGate(proto.ID, gateId, ground, place);
        }

        // The air is the map's (a grid that grows gets an atmosphere of its own by itself, which starts empty: take it off)
        RemComp<GridAtmosphereComponent>(ground.Owner);

        watch.Stop();
        _locations[proto.ID] = new Location
        {
            Proto = proto,
            Map = mapUid,
            MapId = mapId,
            Seed = useSeed,
            Structures = 0,
            BuildMilliseconds = watch.Elapsed.TotalMilliseconds,
            Entities = EntityManager.EntityCount - entitiesBefore,
            Ground = ground.Owner,
            Tunnel = layout,
        };

        LinkBunkerEntrances();
        Log.Info($"World: built {proto.ID} (seed {useSeed}): tunnel of {layout.Path.Count} tiles, {_locations[proto.ID].Entities} entities, {watch.Elapsed.TotalMilliseconds:0} ms");
        return true;
    }

    /// <summary>The tier 3 laboratory: the generated bunker, entered by the bunker hatch at the end of a tunnel (see <see cref="Bunker.BunkerSystem.BuildLaboratory"/>).</summary>
    private bool BuildLaboratory(WorldLocationPrototype proto, int? seed, List<string> gates, out string error)
    {
        error = string.Empty;
        var watch = Stopwatch.StartNew();
        var entitiesBefore = EntityManager.EntityCount;
        var useSeed = seed ?? _random.Next();

        var mapUid = CreateLocationMap(proto, out var mapId);
        var ground = _mapManager.CreateGridEntity(mapId);
        _meta.SetEntityName(ground.Owner, $"{proto.ID} ground");
        EnsureComp<GridAtmosphereComponent>(ground.Owner);
        var floor = _bunker.BuildLaboratory(ground.Owner, ground.Comp, useSeed, out var vestibule, out var vestibuleArrival);
        _physics.SetBodyType(ground.Owner, BodyType.Static);
        CentreOnOrigin(ground);

        _pendingAir.Add(new PendingAir(ground.Owner, floor, _timing.CurTime + TimeSpan.FromSeconds(120)));

        watch.Stop();
        _locations[proto.ID] = new Location
        {
            Proto = proto,
            Map = mapUid,
            MapId = mapId,
            Seed = useSeed,
            BuildMilliseconds = watch.Elapsed.TotalMilliseconds,
            Entities = EntityManager.EntityCount - entitiesBefore,
            Ground = ground.Owner,
            Vestibule = vestibule,
            VestibuleArrival = vestibuleArrival,
        };

        LinkBunkerEntrances();
        Log.Info($"World: built {proto.ID} (seed {useSeed}): laboratory of {floor.Count} floor tiles, {_locations[proto.ID].Entities} entities, {watch.Elapsed.TotalMilliseconds:0} ms");
        return true;
    }

    /// <summary>
    ///     Joins every laboratory to the tunnel that leads to it: the end of the tunnel has the bunker hatch (a key opens the timed
    ///     airlock, see docs/design/bunker.md) that goes down to the laboratory's vestibule. Runs whenever a tunnel or a laboratory is
    ///     built, so whichever comes second makes the link, and again when one of them is built anew.
    /// </summary>
    private void LinkBunkerEntrances()
    {
        foreach (var (labId, lab) in _locations)
        {
            if (lab.Proto.Style != "laboratory" || lab.Vestibule is not { } vestibule || lab.VestibuleArrival is not { } arrival || TerminatingOrDeleted(vestibule))
                continue;

            foreach (var gate in GatesOf(labId))
            {
                if (PartnerOf(labId, gate) is not { } partner
                    || !_locations.TryGetValue(partner.Location, out var tunnel)
                    || tunnel.Tunnel is not { } layout
                    || !layout.Gates.TryGetValue(partner.Gate, out var place))
                {
                    continue;
                }

                // Already joined to this very vestibule
                if (tunnel.Hatch is { } old && !TerminatingOrDeleted(old)
                    && TryComp<Bunker.BunkerHatchComponent>(old, out var oldComp) && oldComp.Vestibule == vestibule)
                {
                    continue;
                }

                if (tunnel.Hatch is { } stale && !TerminatingOrDeleted(stale))
                    QueueDel(stale);

                var ground = tunnel.Ground;
                if (TerminatingOrDeleted(ground) || !TryComp<MapGridComponent>(ground, out var grid))
                    continue;

                var hatchCoords = _map.GridTileToLocal(ground, grid, place.Tile);
                var outside = _map.GridTileToLocal(ground, grid, LocationLayout.PlazaTile(place, -1, 0));
                tunnel.Hatch = _bunker.PlaceHatch(hatchCoords, outside, vestibule, arrival);
                _meta.SetEntityName(tunnel.Hatch.Value, $"bunker hatch to {lab.Proto.Name}");
                if (TryComp<Bunker.BunkerVestibuleComponent>(vestibule, out var vestibuleComp) && vestibuleComp.Ladder is { } ladder)
                    _meta.SetEntityName(ladder, $"hatch to {tunnel.Proto.Name}");
            }
        }
    }

    /// <summary>A hand-built location: the grid of a map file, with gates it names itself.</summary>
    private bool BuildFromMap(WorldLocationPrototype proto, int? seed, ResPath path, out string error)
    {
        error = string.Empty;
        var watch = Stopwatch.StartNew();
        var entitiesBefore = EntityManager.EntityCount;

        var mapUid = CreateLocationMap(proto, out var mapId);
        if (!_loader.TryLoadGrid(mapId, path, out var grid))
        {
            _map.DeleteMap(mapId);
            error = $"could not load {path}";
            return false;
        }

        _meta.SetEntityName(grid.Value.Owner, $"{proto.ID} map grid");
        CentreOnOrigin(grid.Value);
        EnsureComp<GridAtmosphereComponent>(grid.Value.Owner);
        _physics.SetBodyType(grid.Value.Owner, BodyType.Static);
        RetierLoot(grid.Value, TiersOf(proto), new Random(StableHash(proto.ID)));

        // The gates the map names: people arrive on the tile in front of them
        var enumerator = Transform(grid.Value.Owner).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            if (!TryComp<WorldTransitionComponent>(child, out var gate) || gate.Gate == string.Empty)
                continue;

            gate.Location = proto.ID;
            var facing = Transform(child).LocalRotation.GetCardinalDir();
            var step = facing switch
            {
                Direction.North => new Vector2i(0, 1),
                Direction.East => new Vector2i(1, 0),
                Direction.West => new Vector2i(-1, 0),
                _ => new Vector2i(0, -1),
            };

            var tile = _map.TileIndicesFor(grid.Value.Owner, grid.Value.Comp, Transform(child).Coordinates);
            _gates[(proto.ID, gate.Gate)] = (child, _map.GridTileToLocal(grid.Value.Owner, grid.Value.Comp, tile + step));
        }

        _pendingAir.Add(new PendingAir(grid.Value.Owner, _map.GetAllTiles(grid.Value.Owner, grid.Value.Comp).Select(t => t.GridIndices).ToList(), _timing.CurTime + TimeSpan.FromSeconds(120)));

        watch.Stop();
        _locations[proto.ID] = new Location
        {
            Proto = proto,
            Map = mapUid,
            MapId = mapId,
            Seed = seed ?? 0,
            BuildMilliseconds = watch.Elapsed.TotalMilliseconds,
            Entities = EntityManager.EntityCount - entitiesBefore,
        };

        Log.Info($"World: built {proto.ID} from {path}: {_locations[proto.ID].Entities} entities, {watch.Elapsed.TotalMilliseconds:0} ms");
        return true;
    }

    private const string RubbleProto = "N14WallRockBasalt";
    private const string CoverProto = "OreBox";

    // -- The map of a location ------------------------------------------------------------------------------------

    private EntityUid CreateLocationMap(WorldLocationPrototype proto, out MapId mapId)
    {
        var mapUid = _map.CreateMap(out mapId, runMapInit: true);
        _meta.SetEntityName(mapUid, $"{proto.ID} map");
        _map.SetAmbientLight(mapId, proto.AmbientLight);

        // The day and the night follow the same clock in every location on the surface (no random start, the clock is the server's),
        // and the snow, if the location has it, never stops. Underground there is neither
        if (!proto.Underground)
        {
            var cycle = EnsureComp<LightCycleComponent>(mapUid);
            cycle.OriginalColor = proto.AmbientLight;
            cycle.Duration = TimeSpan.FromSeconds(DayCycleSeconds);
            cycle.InitialOffset = false;
            cycle.Offset = TimeSpan.Zero;
            cycle.MinLightLevel = 0f;
            cycle.MaxLightLevel = 1f;
            Dirty(mapUid, cycle);
            if (proto.Weather is { } weather && _proto.TryIndex(weather, out var weatherProto))
                _weather.SetWeather(mapId, weatherProto, null);
        }

        // The air of the map reaches every grid on it that has no air of its own, like on a planet
        var gas = new GasMixture(Atmospherics.CellVolume) { Temperature = 293.15f };
        gas.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        gas.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        _atmos.SetMapAtmosphere(mapUid, false, gas);
        EnsureComp<GravityComponent>(mapUid);
        _gravity.EnableGravity(mapUid);
        return mapUid;
    }

    // -- The storm ------------------------------------------------------------------------------------------------

    /// <summary>The location whose timer this one follows (a tunnel follows the location it leads to), or itself.</summary>
    private string Leader(string id)
    {
        var current = id;
        for (var i = 0; i < 4 && _proto.TryIndex<WorldLocationPrototype>(current, out var proto) && proto.ResetWith is { } with; i++)
        {
            current = with;
        }

        return current;
    }

    private void ScheduleStrike(string id)
    {
        if (!_proto.TryIndex<WorldLocationPrototype>(id, out var proto) || proto.ResetMaxMinutes <= 0 || proto.ResetWith != null)
        {
            _storm[id] = (LocationState.Open, null);
            return;
        }

        // Locations are wiped one at a time: the new time keeps its distance from the others, if the range allows
        var spacing = TimeSpan.FromSeconds(_cfg.GetCVar(WorldCVars.ResetSpacingSeconds));
        var others = _storm.Where(s => s.Key != id && s.Value.Strike != null).Select(s => s.Value.Strike!.Value).ToList();
        TimeSpan best = default;
        var bestGap = TimeSpan.MinValue;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var minutes = _random.Next(proto.ResetMinMinutes, Math.Max(proto.ResetMinMinutes, proto.ResetMaxMinutes) + 1);
            var at = _timing.CurTime + TimeSpan.FromMinutes(minutes);
            var gap = others.Count == 0 ? TimeSpan.MaxValue : others.Min(o => (o - at).Duration());
            if (gap > bestGap)
            {
                best = at;
                bestGap = gap;
            }

            if (gap >= spacing)
                break;
        }

        _storm[id] = (LocationState.Open, best);
    }

    private IEnumerable<string> FollowersOf(string id) => _locations.Keys.Where(k => k != id && Leader(k) == id);

    private void UpdateStorm()
    {
        if (_storm.Count == 0)
            return;

        var warning = TimeSpan.FromSeconds(_cfg.GetCVar(WorldCVars.WarningSeconds));
        foreach (var (id, (state, strike)) in _storm.ToList())
        {
            if (strike is not { } at)
                continue;

            if (state == LocationState.Open && _timing.CurTime >= at - warning)
                StartWarning(id, at);
            else if (state == LocationState.Warning && _timing.CurTime >= at)
                Strike(id);
            else if (state == LocationState.Warning)
                UpdateNotice(id, at - _timing.CurTime);
        }
    }

    /// <summary>Starts the warning at once for a location (the admin command does this with the time left).</summary>
    public void StartWarning(string id, TimeSpan strike)
    {
        _storm[id] = (LocationState.Warning, strike);
        foreach (var follower in FollowersOf(id))
        {
            _storm[follower] = (LocationState.Warning, null);
        }

        _notice[id] = (int.MaxValue, 0);
        UpdateNotice(id, strike - _timing.CurTime);
        Log.Info($"World: the storm is coming for {id}");
    }

    /// <summary>Seconds before the wipe at which the whole location is told how long is left (and the start of the warning).</summary>
    private static readonly int[] NoticeAt = { 60, 30, 10 };

    private readonly Dictionary<string, (int Last, int Stage)> _notice = new();

    /// <summary>
    ///     While a storm is coming: tells everyone in the location (and in the tunnels that go with it) the exact time left, plays the
    ///     warning sound with the first notice and the last, and thickens the ashfall: light, then ashfall from the middle of the warning,
    ///     then heavy for the last five seconds.
    /// </summary>
    private void UpdateNotice(string id, TimeSpan remaining)
    {
        if (!_notice.TryGetValue(id, out var state))
            state = (int.MaxValue, 0);

        var seconds = Math.Max(0, remaining.TotalSeconds);
        var warning = _cfg.GetCVar(WorldCVars.WarningSeconds);

        // The ashfall
        var stage = seconds <= 5 ? 3 : seconds <= warning / 2 ? 2 : 1;
        if (stage != state.Stage)
        {
            var weather = stage switch { 3 => "AshfallHeavy", 2 => "Ashfall", _ => "AshfallLight" };
            foreach (var name in FollowersOf(id).Append(id))
            {
                if (_locations.TryGetValue(name, out var loc) && !loc.Proto.Underground && _proto.TryIndex<Content.Shared.Weather.WeatherPrototype>(weather, out var weatherProto))
                    _weather.SetWeather(loc.MapId, weatherProto, null);
            }

            state.Stage = stage;
        }

        // The notices: at the first one that is due, once
        var due = int.MaxValue;
        foreach (var at in NoticeAt)
        {
            if (seconds <= at && at < state.Last && at < due)
                due = at;
        }

        var first = state.Last == int.MaxValue;
        if (first || due != int.MaxValue)
        {
            if (!first)
                state.Last = due;
            else
                state.Last = NoticeAt.Where(a => a >= seconds).DefaultIfEmpty(1_000_000).Min(); // marks already past are skipped, the rest are still to come

            var shown = (int) Math.Ceiling(seconds);
            var text = Loc.GetString("world-storm-warning", ("time", $"{shown / 60:00}:{shown % 60:00}"));
            var playSound = first || state.Last == NoticeAt[^1];
            var sound = new SoundPathSpecifier(_cfg.GetCVar(WorldCVars.WarningSound));
            foreach (var name in FollowersOf(id).Append(id))
            {
                if (!_locations.TryGetValue(name, out var loc))
                    continue;

                var filter = Filter.BroadcastMap(loc.MapId);
                _chat.DispatchFilteredAnnouncement(filter, text, sender: Loc.GetString("world-storm-sender"), playSound: false, colorOverride: Color.FromHex("#C03030"));
                if (playSound)
                    _audio.PlayGlobal(sound, filter, true);
            }
        }

        _notice[id] = state;
    }

    /// <summary>The storm wipes a location: everything in it goes with the map, and it is built again with a new layout.</summary>
    public void Strike(string id)
    {
        _notice.Remove(id);
        Log.Info($"World: the storm wipes {id}");
        foreach (var name in FollowersOf(id).Prepend(id).ToList())
        {
            _storm.Remove(name);
            QueueBuild(name, null);
        }

        ScheduleStrike(id);
        foreach (var follower in FollowersOf(id))
        {
            _storm[follower] = (LocationState.Open, null);
        }
    }

    public (LocationState State, TimeSpan? Strike) StormOf(string id) => _storm.TryGetValue(id, out var s) ? s : (LocationState.Open, null);

    public TimeSpan Now => _timing.CurTime;
}
