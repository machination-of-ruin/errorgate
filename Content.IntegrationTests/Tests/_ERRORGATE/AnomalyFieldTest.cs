#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._ERRORGATE.Anomalies;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Spawners.Components;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Damage;
using Content.Shared.Gravity;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     World faults: the field places them, each one hurts in its own way, they leave bystanders alone.
/// </summary>
[TestFixture]
public sealed class AnomalyFieldTest
{
    private const string HeatProto = "ErrorgateAnomalyHeat";
    private const string ArcProto = "ErrorgateAnomalyArc";
    private const string CollapseProto = "ErrorgateAnomalyCollapse";

    /// <summary>
    ///     A square plating floor with gravity, <paramref name="half"/> tiles each way from the origin.
    /// </summary>
    private static async Task<(EntityUid Grid, MapId Map)> CreateFloor(TestPair pair, int half)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        EntityUid gridUid = default;
        MapId mapId = default;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out mapId);
            var grid = server.MapMan.CreateGridEntity(mapId);
            gridUid = grid.Owner;

            var plating = new Tile(tileDefs["Plating"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -half; x <= half; x++)
            {
                for (var y = -half; y <= half; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            mapSys.SetTiles(grid.Owner, grid.Comp, tiles);

            var gravity = entMan.EnsureComponent<GravityComponent>(grid.Owner);
            gravity.Enabled = true;
            gravity.Inherent = true;
        });

        await pair.RunTicksSync(2);
        return (gridUid, mapId);
    }

    private static List<Entity<ErrorgateAnomalyComponent>> GetAnomalies(IEntityManager entMan)
    {
        var anomalies = new List<Entity<ErrorgateAnomalyComponent>>();
        var query = entMan.EntityQueryEnumerator<ErrorgateAnomalyComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            anomalies.Add((uid, comp));
        }

        return anomalies;
    }

    private static string ProtoOf(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? string.Empty;
    }

    [Test]
    public async Task FieldPlacesOneOfEachApart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();

        var (grid, _) = await CreateFloor(pair, 45);

        await server.WaitPost(() => entMan.AddComponent(grid, new AnomalyFieldComponent()));
        await pair.RunTicksSync(5);

        var field = entMan.GetComponent<AnomalyFieldComponent>(grid);
        var anomalies = GetAnomalies(entMan);

        Assert.That(anomalies, Has.Count.EqualTo(3), "The field should have placed exactly three anomalies.");
        Assert.That(field.Spawned, Has.Count.EqualTo(3));
        Assert.That(anomalies.Select(a => ProtoOf(entMan, a.Owner)).Distinct().Count(), Is.EqualTo(3),
            "Every prototype should be used exactly once.");

        await server.WaitAssertion(() =>
        {
            foreach (var anomaly in anomalies)
            {
                var coords = entMan.GetComponent<TransformComponent>(anomaly.Owner).Coordinates;
                Assert.That(mapSys.TryGetTileRef(grid, entMan.GetComponent<MapGridComponent>(grid), coords, out var tile), Is.True);
                Assert.That(tile.Tile.IsEmpty, Is.False, "Anomalies belong on floor tiles.");
            }

            for (var i = 0; i < anomalies.Count; i++)
            {
                for (var j = i + 1; j < anomalies.Count; j++)
                {
                    var distance = (xformSys.GetWorldPosition(anomalies[i].Owner) - xformSys.GetWorldPosition(anomalies[j].Owner)).Length();
                    Assert.That(distance, Is.GreaterThanOrEqualTo(field.MinDistanceBetween),
                        "Anomalies must keep their distance from each other.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RespawnReplacesTheAnomalies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var (grid, _) = await CreateFloor(pair, 45);

        await server.WaitPost(() => entMan.AddComponent(grid, new AnomalyFieldComponent()));
        await pair.RunTicksSync(5);
        Assert.That(GetAnomalies(entMan), Has.Count.EqualTo(3));

        var first = GetAnomalies(entMan).Select(a => a.Owner).ToHashSet();

        var placed = 0;
        await server.WaitPost(() => placed = entMan.System<AnomalyFieldSystem>()
            .SpawnAnomalies(grid, entMan.GetComponent<AnomalyFieldComponent>(grid)));
        await pair.RunTicksSync(5);

        Assert.That(placed, Is.EqualTo(3));
        var second = GetAnomalies(entMan);
        Assert.That(second, Has.Count.EqualTo(3), "The old anomalies should be gone.");
        Assert.That(second.Any(a => first.Contains(a.Owner)), Is.False);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TooLittleFloorPlacesFewer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        // 11x11 tiles: room for one anomaly, never three 25 tiles apart
        var (grid, _) = await CreateFloor(pair, 5);

        await server.WaitPost(() => entMan.AddComponent(grid, new AnomalyFieldComponent()));
        await pair.RunTicksSync(5);

        Assert.That(GetAnomalies(entMan).Count, Is.LessThan(3), "A small floor cannot hold three anomalies.");
        await pair.CleanReturnAsync();
    }

    // The test floor has no air, so the vacuum adds a little Blunt damage: the collapse is held to a high bar
    [TestCase(HeatProto, "Heat", 20f)]
    [TestCase(ArcProto, "Shock", 20f)]
    [TestCase(CollapseProto, "Blunt", 70f)]
    public async Task AnomalyHurtsWhatStandsInIt(string proto, string damageType, float minimum)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var (_, mapId) = await CreateFloor(pair, 15);

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            var coords = new MapCoordinates(0.5f, 0.5f, mapId);
            var anomaly = entMan.SpawnEntity(proto, coords);

            // Make the shock certain, the real chance is below one
            if (entMan.TryGetComponent(anomaly, out ArcFaultComponent? arc))
                arc.InnerShockChance = 1f;

            human = entMan.SpawnEntity("MobHuman", coords);
        });

        var seen = 0f;
        var gone = false;
        for (var i = 0; i < 6; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                if (!entMan.EntityExists(human))
                {
                    gone = true;
                    return;
                }

                var damage = entMan.GetComponent<DamageableComponent>(human).Damage.DamageDict;
                if (damage.TryGetValue(damageType, out var value))
                    seen = Math.Max(seen, value.Float());
            });
        }

        // A body crushed to death is torn apart, which is as good as damage
        Assert.That(seen >= minimum || gone, Is.True,
            $"A human standing in {proto} should take at least {minimum} {damageType} damage, took {seen}.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BystandersFarAwayAreUnharmed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var (_, mapId) = await CreateFloor(pair, 60);

        EntityUid farHuman = default;
        EntityUid edgeHuman = default;
        var farStart = Vector2.Zero;
        await server.WaitPost(() =>
        {
            entMan.SpawnEntity(HeatProto, new MapCoordinates(0.5f, 0.5f, mapId));
            entMan.SpawnEntity(ArcProto, new MapCoordinates(40.5f, 0.5f, mapId));
            entMan.SpawnEntity(CollapseProto, new MapCoordinates(-40.5f, 0.5f, mapId));

            // 40 tiles from all of them
            farHuman = entMan.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 40.5f, mapId));

            // Just outside the heat zone: the heat has no pull and no reach past its radius
            edgeHuman = entMan.SpawnEntity("MobHuman", new MapCoordinates(5.5f, 0.5f, mapId));

            farStart = entMan.System<SharedTransformSystem>().GetWorldPosition(farHuman);
        });

        await pair.RunSeconds(6);

        await server.WaitAssertion(() =>
        {
            // The floor has no air: both humans take the same environmental damage (a little Heat and Blunt).
            // The one 40 tiles away from everything is the baseline, the one at the edge of the heat zone must match it.
            float Dmg(EntityUid human, string type) =>
                entMan.GetComponent<DamageableComponent>(human).Damage.DamageDict.TryGetValue(type, out var v) ? v.Float() : 0f;

            Assert.That(Dmg(edgeHuman, "Heat"), Is.EqualTo(Dmg(farHuman, "Heat")).Within(0.5f),
                "A human outside the heat zone should not burn.");
            foreach (var human in new[] { farHuman, edgeHuman })
            {
                Assert.That(Dmg(human, "Shock"), Is.EqualTo(0f), "A human outside every anomaly should not be shocked.");
            }

            var moved = (entMan.System<SharedTransformSystem>().GetWorldPosition(farHuman) - farStart).Length();
            Assert.That(moved, Is.LessThan(0.5f), "A human far from the collapse should not be pulled.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CollapsePullsLooseThingsIn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();

        var (_, mapId) = await CreateFloor(pair, 15);

        EntityUid anomaly = default;
        EntityUid item = default;
        await server.WaitPost(() =>
        {
            anomaly = entMan.SpawnEntity(CollapseProto, new MapCoordinates(0.5f, 0.5f, mapId));
            item = entMan.SpawnEntity("Crowbar", new MapCoordinates(5.5f, 0.5f, mapId));
        });

        var before = 0f;
        await server.WaitPost(() => before = (xformSys.GetWorldPosition(item) - xformSys.GetWorldPosition(anomaly)).Length());

        await pair.RunSeconds(2);

        var after = 0f;
        await server.WaitPost(() => after = (xformSys.GetWorldPosition(item) - xformSys.GetWorldPosition(anomaly)).Length());

        Assert.That(after, Is.LessThan(before - 0.5f), "A loose item should be dragged toward the collapse.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KuznetskGetsOneOfEachAwayFromSpawns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();

        await server.WaitPost(() =>
        {
            var ticker = entMan.System<GameTicker>();
            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(server.ProtoMan.Index<GameMapPrototype>("Kuznetsk"), out _, opts);
        });
        await pair.RunTicksSync(10);

        var anomalies = GetAnomalies(entMan);
        Assert.That(anomalies, Has.Count.EqualTo(3), "Kuznetsk should carry exactly three anomalies.");
        Assert.That(anomalies.Select(a => ProtoOf(entMan, a.Owner)).Distinct().Count(), Is.EqualTo(3),
            "Kuznetsk should carry one of each anomaly.");

        var field = entMan.EntityQuery<AnomalyFieldComponent>().Single();

        await server.WaitAssertion(() =>
        {
            var spawns = entMan.EntityQuery<SpawnPointComponent>().Select(s => xformSys.GetWorldPosition(s.Owner)).ToList();
            Assert.That(spawns, Is.Not.Empty, "Kuznetsk should have spawn points.");

            foreach (var anomaly in anomalies)
            {
                var position = xformSys.GetWorldPosition(anomaly.Owner);
                foreach (var spawn in spawns)
                {
                    Assert.That((position - spawn).Length(), Is.GreaterThanOrEqualTo(field.MinDistanceFromSpawns),
                        "Nobody should spawn inside an anomaly.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
