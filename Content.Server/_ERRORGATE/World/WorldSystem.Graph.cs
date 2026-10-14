using System.Diagnostics;
using System.Linq;
using Content.Server.Atmos.Components;
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
    private static readonly SoundSpecifier WarningSound = new SoundPathSpecifier("/Audio/Machines/alarm.ogg");
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

    /// <summary>Forgets everything built (the round ended and its maps are gone).</summary>
    public void Forget()
    {
        _locations.Clear();
        _gates.Clear();
        _storm.Clear();
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

    private void PlaceGate(string location, string gate, Entity<MapGridComponent> ground, Vector2i tile, Vector2i arrival, int facing)
    {
        var proto = _proto.Index<WorldLocationPrototype>(location);
        var entity = Spawn(proto.Gate, _map.GridTileToLocal(ground.Owner, ground.Comp, tile));
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
        return IsBuilt(location) && (!_storm.TryGetValue(location, out var state) || state.State == LocationState.Open);
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
        EnsureComp<GridAtmosphereComponent>(ground.Owner);
        var floorDef = (ContentTileDefinition) _tileDefs[proto.Ground];
        var tiles = new List<(Vector2i, Tile)>();
        foreach (var t in layout.Floor.Concat(layout.Walls))
        {
            tiles.Add((t, new Tile(floorDef.TileId, 0, _tile.PickVariant(floorDef))));
        }

        _map.SetTiles(ground.Owner, ground.Comp, tiles);
        _physics.SetBodyType(ground.Owner, BodyType.Static);

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
            PlaceGate(proto.ID, gateId, ground, place.Tile, place.Arrival, place.Facing);
        }

        _pendingAir.Add(new PendingAir(ground.Owner, layout.Floor.ToList(), _timing.CurTime + TimeSpan.FromSeconds(120)));

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
            Tunnel = layout,
        };

        Log.Info($"World: built {proto.ID} (seed {useSeed}): tunnel of {layout.Path.Count} tiles, {_locations[proto.ID].Entities} entities, {watch.Elapsed.TotalMilliseconds:0} ms");
        return true;
    }

    /// <summary>The tier 3 laboratory: the generated bunker, entered by a gate (see <see cref="Bunker.BunkerSystem.BuildLaboratory"/>).</summary>
    private bool BuildLaboratory(WorldLocationPrototype proto, int? seed, List<string> gates, out string error)
    {
        error = string.Empty;
        var watch = Stopwatch.StartNew();
        var entitiesBefore = EntityManager.EntityCount;
        var useSeed = seed ?? _random.Next();

        var mapUid = CreateLocationMap(proto, out var mapId);
        var ground = _mapManager.CreateGridEntity(mapId);
        EnsureComp<GridAtmosphereComponent>(ground.Owner);
        var floor = _bunker.BuildLaboratory(ground.Owner, ground.Comp, useSeed);
        _physics.SetBodyType(ground.Owner, BodyType.Static);

        if (gates.Count > 0)
            PlaceGate(proto.ID, gates[0], ground, Bunker.BunkerSystem.LaboratoryGate, Bunker.BunkerSystem.LaboratoryArrival, 3);

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
        };

        Log.Info($"World: built {proto.ID} (seed {useSeed}): laboratory of {floor.Count} floor tiles, {_locations[proto.ID].Entities} entities, {watch.Elapsed.TotalMilliseconds:0} ms");
        return true;
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

        EnsureComp<GridAtmosphereComponent>(grid.Value.Owner);
        _physics.SetBodyType(grid.Value.Owner, BodyType.Static);
        RetierLoot(grid.Value, proto.Tier);

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
    private const string CoverProto = "CrateGenericSteel";

    // -- The map of a location ------------------------------------------------------------------------------------

    private EntityUid CreateLocationMap(WorldLocationPrototype proto, out MapId mapId)
    {
        var mapUid = _map.CreateMap(out mapId, runMapInit: true);
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

        foreach (var name in FollowersOf(id).Append(id))
        {
            if (_locations.TryGetValue(name, out var loc))
                _audio.PlayGlobal(WarningSound, Filter.BroadcastMap(loc.MapId), true);
        }

        Log.Info($"World: the storm is coming for {id}");
    }

    /// <summary>The storm wipes a location: everything in it goes with the map, and it is built again with a new layout.</summary>
    public void Strike(string id)
    {
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
