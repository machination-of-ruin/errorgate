using Content.Server.GameTicking;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Events;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._ERRORGATE.World;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;

namespace Content.Server._ERRORGATE.World;

/// <summary>
///     Starts the world with a round (when <c>errorgate.world.enabled</c> is on and the game map is <c>World</c>): the locations are
///     built after the (empty) map has loaded and before the players spawn, and the ground of the location with the spawn zone joins
///     the station, so people spawn there. Cleans up when the round ends.
/// </summary>
public sealed class WorldRoundSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameMapManager _gameMaps = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly WorldSystem _world = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    private bool _active;
    private EntityUid? _stationUid;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _active = false;
            _stationUid = null;
            _world.Forget();
        });
        SubscribeLocalEvent<WorldSystem.WorldLocationBuilt>(OnLocationBuilt);
        SubscribeLocalEvent<IsJobAllowedEvent>(OnJobAllowed);
    }

    /// <summary>
    ///     The location with the spawn spots is built again after a storm: its new ground joins the station, so people spawn there.
    /// </summary>
    private void OnLocationBuilt(WorldSystem.WorldLocationBuilt ev)
    {
        if (!_active || _stationUid is not { } station || !_world.Locations.TryGetValue(ev.Id, out var loc) || loc.Layout is not { SpawnTiles.Count: > 0 })
            return;

        _station.AddGridToStation(station, loc.Ground);
    }

    /// <summary>Nobody is spawned while the spawn location is being built again or has a storm coming: they are told to wait.</summary>
    private void OnJobAllowed(ref IsJobAllowedEvent ev)
    {
        if (!_active || _world.SpawnReady())
            return;

        ev.Cancelled = true;
        _chat.DispatchServerMessage(ev.Player, Loc.GetString("world-spawn-wait"));
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        if (!_cfg.GetCVar(WorldCVars.Enabled) || _gameMaps.GetSelectedMap()?.ID != "World")
            return;

        var graph = _cfg.GetCVar(WorldCVars.Graph);
        if (!_world.TryBuildGraph(graph, null, out var error))
        {
            Log.Error($"The world was not built: {error}");
            return;
        }

        var stations = EntityQueryEnumerator<StationDataComponent>();
        if (_world.SpawnGrid() is not { } grid || !stations.MoveNext(out var station, out _))
        {
            Log.Error("The world has no location with a spawn zone, or the map made no station.");
            return;
        }

        _station.AddGridToStation(station, grid);
        _stationUid = station;
        _active = true;
        Log.Info("The world is built and the ground of its spawn zone belongs to the station.");
    }
}
