#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._ERRORGATE.Anomalies;
using Content.Server._ERRORGATE.LootManager;
using Content.Server.Beam.Components;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Spawners.Components;
using Content.Shared._ERRORGATE.Anomalies;
using Content.Shared.Damage;
using Content.Shared.Gravity;
using Content.Shared.Throwing;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     World faults: the field places packs of them, each kind hurts in its own way and only inside its radius,
///     and they show themselves when something is thrown into them or when they hurt someone.
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

    /// <summary>
    ///     Loot spawners (the points of interest) in small groups around each of the given centers, and a spawn point.
    /// </summary>
    private static async Task<(List<Vector2> Pois, Vector2 Spawn)> AddPois(TestPair pair, MapId mapId, IEnumerable<Vector2> centers, Vector2 spawn)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var pois = new List<Vector2>();

        await server.WaitPost(() =>
        {
            foreach (var center in centers)
            {
                foreach (var offset in new[] { new Vector2(0, 0), new Vector2(3, 2), new Vector2(-2, 4) })
                {
                    var position = center + offset;
                    var spawner = entMan.SpawnEntity(null, new MapCoordinates(position, mapId));
                    entMan.AddComponent(spawner, new LootSpawnerComponent { SpawnRate = 0f });
                    pois.Add(position);
                }
            }

            entMan.SpawnEntity("SpawnPointLatejoin", new MapCoordinates(spawn, mapId));
        });

        return (pois, spawn);
    }

    private static readonly Vector2[] PoiCenters =
    {
        new(-60, -60), new(-60, 50), new(0, 0), new(50, -50), new(60, 60), new(-20, 70), new(70, 0),
    };

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

    private static float Danger(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<ErrorgateAnomalyComponent>(uid).EffectiveDanger;
    }

    // Placement

    [Test]
    public async Task FieldPlacesTheWantedCountInPacksAwayFromSpawnsAndLoot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();

        var (grid, mapId) = await CreateFloor(pair, 100);
        var (pois, spawn) = await AddPois(pair, mapId, PoiCenters, new Vector2(-90, -90));

        await server.WaitPost(() => entMan.AddComponent(grid, new AnomalyFieldComponent { Count = 40 }));
        await pair.RunTicksSync(5);

        // Place them again, timed. Several hundred thousand tiles worth of work must stay well below two seconds.
        var watch = new System.Diagnostics.Stopwatch();
        var placed = 0;
        await server.WaitPost(() =>
        {
            watch.Start();
            placed = entMan.System<AnomalyFieldSystem>().SpawnAnomalies(grid, entMan.GetComponent<AnomalyFieldComponent>(grid));
            watch.Stop();
        });
        await pair.RunTicksSync(5);

        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000), "Placing the anomalies took too long.");
        Assert.That(placed, Is.EqualTo(40), "The count is the total number of faults wanted.");

        await server.WaitAssertion(() =>
        {
            var field = entMan.GetComponent<AnomalyFieldComponent>(grid);
            var anomalies = GetAnomalies(entMan);

            Assert.That(anomalies, Has.Count.EqualTo(40));
            Assert.That(field.Spawned, Has.Count.EqualTo(40));
            Assert.That(field.Packs.Sum(p => p.Members.Count), Is.EqualTo(40));
            Assert.That(field.Packs.Select(p => p.Proto.Id).Distinct().Count(), Is.EqualTo(3), "All three kinds should be used.");

            foreach (var pack in field.Packs)
            {
                // One kind per pack
                Assert.That(pack.Members.Select(m => ProtoOf(entMan, m)).Distinct().Single(), Is.EqualTo(pack.Proto.Id));

                // Inside the blob
                foreach (var member in pack.Members)
                {
                    Assert.That((xformSys.GetWorldPosition(member) - pack.Center).Length(), Is.LessThanOrEqualTo(pack.Radius + 1.5f),
                        "A fault lies outside the blob of its pack.");
                }

                // Spaced by their danger zones, never overlapping
                for (var i = 0; i < pack.Members.Count; i++)
                {
                    for (var j = i + 1; j < pack.Members.Count; j++)
                    {
                        var distance = (xformSys.GetWorldPosition(pack.Members[i]) - xformSys.GetWorldPosition(pack.Members[j])).Length();
                        Assert.That(distance, Is.GreaterThanOrEqualTo(field.MinSpacingInPack - 0.01f));
                        Assert.That(distance, Is.GreaterThanOrEqualTo(Danger(entMan, pack.Members[i]) + Danger(entMan, pack.Members[j]) - 0.01f),
                            "Two danger zones overlap.");
                    }
                }

                // Near loot
                Assert.That(pois.Min(p => (p - pack.Center).Length()), Is.LessThanOrEqualTo(60f), "A pack is far from every point of interest.");
            }

            var gridComp = entMan.GetComponent<MapGridComponent>(grid);
            foreach (var anomaly in anomalies)
            {
                var position = xformSys.GetWorldPosition(anomaly.Owner);
                Assert.That((position - spawn).Length(), Is.GreaterThanOrEqualTo(field.MinDistanceFromSpawns), "A fault is too close to a spawn point.");
                Assert.That(pois.Min(p => (p - position).Length()), Is.GreaterThanOrEqualTo(field.MinDistanceFromPoi), "A fault is too close to loot.");

                var coords = entMan.GetComponent<TransformComponent>(anomaly.Owner).Coordinates;
                Assert.That(mapSys.TryGetTileRef(grid, gridComp, coords, out var tile), Is.True);
                Assert.That(tile.Tile.IsEmpty, Is.False, "Anomalies belong on floor tiles.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PacksCanMostlyBeWalkedThroughAndGapsAreMostlyWide()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();

        var (grid, mapId) = await CreateFloor(pair, 100);
        await AddPois(pair, mapId, PoiCenters, new Vector2(-90, -90));
        await server.WaitPost(() => entMan.AddComponent(grid, new AnomalyFieldComponent { Count = 40 }));
        await pair.RunTicksSync(3);

        var packs = 0;
        var passable = 0;
        var faults = 0;
        var wide = 0;

        // Several random fields
        for (var run = 0; run < 5; run++)
        {
            await server.WaitPost(() => entMan.System<AnomalyFieldSystem>().SpawnAnomalies(grid, entMan.GetComponent<AnomalyFieldComponent>(grid)));
            await pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                foreach (var pack in entMan.GetComponent<AnomalyFieldComponent>(grid).Packs)
                {
                    packs++;
                    if (pack.Passable)
                        passable++;

                    // The gap to the nearest neighbour in the pack
                    foreach (var member in pack.Members)
                    {
                        var nearest = float.MaxValue;
                        foreach (var other in pack.Members)
                        {
                            if (other == member)
                                continue;

                            var distance = (xformSys.GetWorldPosition(member) - xformSys.GetWorldPosition(other)).Length();
                            nearest = MathF.Min(nearest, distance - Danger(entMan, member) - Danger(entMan, other));
                        }

                        if (pack.Members.Count < 2)
                            continue;

                        faults++;
                        Assert.That(nearest, Is.GreaterThanOrEqualTo(-0.01f), "Two danger zones overlap.");
                        if (nearest >= 1.5f)
                            wide++;
                    }
                }
            });
        }

        Assert.That(packs, Is.GreaterThan(10));
        Assert.That(passable / (float) packs, Is.GreaterThanOrEqualTo(0.7f), $"Only {passable} of {packs} packs can be walked through.");
        Assert.That(wide / (float) faults, Is.GreaterThanOrEqualTo(0.45f), $"Only {wide} of {faults} faults have a gap of 1.5 tiles to their nearest neighbour.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RespawnReplacesTheAnomalies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var (grid, mapId) = await CreateFloor(pair, 60);
        await AddPois(pair, mapId, new[] { new Vector2(0, 0), new Vector2(-30, 30) }, new Vector2(-55, -55));

        await server.WaitPost(() => entMan.AddComponent(grid, new AnomalyFieldComponent { Count = 10 }));
        await pair.RunTicksSync(5);

        var firstCount = GetAnomalies(entMan).Count;
        Assert.That(firstCount, Is.GreaterThan(0));
        var first = GetAnomalies(entMan).Select(a => a.Owner).ToHashSet();

        var placed = 0;
        await server.WaitPost(() => placed = entMan.System<AnomalyFieldSystem>()
            .SpawnAnomalies(grid, entMan.GetComponent<AnomalyFieldComponent>(grid)));
        await pair.RunTicksSync(5);

        var second = GetAnomalies(entMan);
        Assert.That(second, Has.Count.EqualTo(placed), "The old anomalies should be gone.");
        Assert.That(second.Any(a => first.Contains(a.Owner)), Is.False);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TooLittleFloorPlacesFewer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        // 11x11 tiles: no room for 36 faults
        var (grid, _) = await CreateFloor(pair, 5);

        await server.WaitPost(() => entMan.AddComponent(grid, new AnomalyFieldComponent()));
        await pair.RunTicksSync(5);

        Assert.That(GetAnomalies(entMan).Count, Is.LessThan(36), "A small floor cannot hold the default count.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NothingIsPlacedBehindAWallRing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();

        var (grid, mapId) = await CreateFloor(pair, 100);

        // A one tile wall ring around the playable area, like Kuznetsk's mountains, with floor and loot beyond it
        await server.WaitPost(() =>
        {
            for (var i = -60; i <= 60; i++)
            {
                foreach (var tile in new[] { new Vector2(i, -60), new Vector2(i, 60), new Vector2(-60, i), new Vector2(60, i) })
                {
                    entMan.SpawnEntity("WallSolid", new MapCoordinates(tile + new Vector2(0.5f, 0.5f), mapId));
                }
            }
        });
        await pair.RunTicksSync(3);

        await AddPois(pair, mapId, new[] { new Vector2(-30, -30), new Vector2(30, 30), new Vector2(-10, 30), new Vector2(0, 0), new Vector2(-85, 80), new Vector2(80, -85), new Vector2(85, 70), new Vector2(-80, -20) }, new Vector2(-50, -50));

        await server.WaitPost(() => entMan.AddComponent(grid, new AnomalyFieldComponent { Count = 25, MinDistanceFromSpawns = 10, MinDistanceBetweenPacks = 15 }));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var anomalies = GetAnomalies(entMan);
            Assert.That(anomalies, Is.Not.Empty);

            foreach (var anomaly in anomalies)
            {
                var position = xformSys.GetWorldPosition(anomaly.Owner);
                Assert.That(Math.Abs(position.X), Is.LessThan(60f), $"A fault stands beyond the wall ring at {position}.");
                Assert.That(Math.Abs(position.Y), Is.LessThan(60f), $"A fault stands beyond the wall ring at {position}.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KuznetskGetsPacksOfOneKindAwayFromSpawns()
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

        var field = entMan.EntityQuery<AnomalyFieldComponent>().Single();
        var anomalies = GetAnomalies(entMan);

        Assert.That(anomalies, Has.Count.EqualTo(field.Spawned.Count));
        Assert.That(anomalies.Count, Is.InRange(field.Count - 8, field.Count), $"Kuznetsk should carry about {field.Count} anomalies.");

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

            foreach (var pack in field.Packs)
            {
                Assert.That(pack.Members.Select(m => ProtoOf(entMan, m)).Distinct().Count(), Is.EqualTo(1), "A pack is of one kind.");
            }
        });

        await pair.CleanReturnAsync();
    }

    // Effects

    private static async Task<float> ReadDamage(TestPair pair, EntityUid human, string type)
    {
        var entMan = pair.Server.EntMan;
        var result = 0f;
        await pair.Server.WaitPost(() =>
        {
            if (entMan.EntityExists(human)
                && entMan.GetComponent<DamageableComponent>(human).Damage.DamageDict.TryGetValue(type, out var value))
            {
                result = value.Float();
            }
        });
        return result;
    }

    [Test]
    public async Task HeatIsLethalInsideAndHarmlessOutside()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var (_, mapId) = await CreateFloor(pair, 20);

        EntityUid inner = default;
        EntityUid edge = default;
        EntityUid outside = default;
        EntityUid heat = default;
        await server.WaitPost(() =>
        {
            heat = entMan.SpawnEntity(HeatProto, new MapCoordinates(0.5f, 0.5f, mapId));
            inner = entMan.SpawnEntity("MobHuman", new MapCoordinates(1.5f, 0.5f, mapId));
            edge = entMan.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 4.9f, mapId));
            outside = entMan.SpawnEntity("MobHuman", new MapCoordinates(-6.5f, 0.5f, mapId));
        });

        // A few seconds at the edge are survivable
        await pair.RunSeconds(3);
        Assert.That(await ReadDamage(pair, edge, "Heat"), Is.GreaterThan(0f).And.LessThan(100f), "The edge of the heat should hurt, not kill, in three seconds.");

        await pair.RunSeconds(2);
        Assert.That(await ReadDamage(pair, inner, "Heat"), Is.GreaterThanOrEqualTo(100f), "Five seconds close to the heat fault should take a human down.");
        Assert.That(await ReadDamage(pair, outside, "Heat"), Is.LessThan(5f), "Six tiles away is outside the heat (the airless test floor adds a little Heat by itself).");

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ErrorgateAnomalyComponent>(heat).RevealedUntil, Is.Not.Null, "Hurting someone reveals a fault.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArcReachesFourTilesAndNeverChainsShocks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();

        var (_, mapId) = await CreateFloor(pair, 20);

        // Three tiles from the arc is outside the shock zone but inside the reach of an arc, six is out of reach
        EntityUid near = default;
        EntityUid far = default;
        await server.WaitPost(() =>
        {
            entMan.SpawnEntity(ArcProto, new MapCoordinates(0.5f, 0.5f, mapId));
            near = entMan.SpawnEntity("MobHuman", new MapCoordinates(3.5f, 0.5f, mapId));
            far = entMan.SpawnEntity("MobHuman", new MapCoordinates(0.5f, -8.5f, mapId));
        });

        // The arc fires at once when someone is in reach
        await pair.RunSeconds(1);
        Assert.That(await ReadDamage(pair, near, "Shock"), Is.GreaterThanOrEqualTo(40f), "Someone within four tiles should be hit by an arc.");
        Assert.That(await ReadDamage(pair, far, "Shock"), Is.EqualTo(0f), "Eight tiles away nothing reaches.");

        // ... and then recharges: the same fault does not fire again for a while
        Assert.That(entMan.EntityQuery<ArcFaultComponent>().Single().NextArc, Is.GreaterThan(timing.CurTime), "The fault should be recharging.");

        // Inside the fault the shocks are as frequent as can be, but not chained
        EntityUid inside = default;
        await server.WaitPost(() =>
        {
            foreach (var fault in entMan.EntityQuery<ArcFaultComponent>())
            {
                fault.InnerShockChance = 1f;
            }

            // A human in the middle and no one else in reach of the arcs
            entMan.DeleteEntity(near);
            inside = entMan.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 0.5f, mapId));
        });

        // The fault recharges for five seconds after the earlier shock, then shocks the newcomer
        var immune = false;
        for (var i = 0; i < 40 && !immune; i++)
        {
            await pair.RunSeconds(0.25f);
            await server.WaitPost(() => immune = entMan.HasComponent<ArcShockImmunityComponent>(inside));
        }

        Assert.That(immune, Is.True, "A shocked person should be immune for a moment.");

        // The first shock was in the first second. With an immunity of 2.5 seconds there can be at most three more in
        // six more seconds (without it there would be one every second).
        await pair.RunSeconds(6);
        var shock = await ReadDamage(pair, inside, "Shock");
        var arc = entMan.EntityQuery<ArcFaultComponent>().Single();
        var biggest = Math.Max(arc.InnerShockDamage, arc.ArcDamage);
        Assert.That(shock, Is.GreaterThanOrEqualTo(arc.ArcDamage));
        Assert.That(shock, Is.LessThanOrEqualTo(biggest * 4f), "Shocks must not chain.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArcFiresAtAThrownObjectAndRecharges()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var timing = server.ResolveDependency<IGameTiming>();
        var (_, mapId) = await CreateFloor(pair, 20);

        EntityUid arc = default;
        await server.WaitPost(() =>
        {
            arc = entMan.SpawnEntity(ArcProto, new MapCoordinates(0.5f, 0.5f, mapId));
            var crowbar = entMan.SpawnEntity("Crowbar", new MapCoordinates(3.5f, 0.5f, mapId));
            // Mid flight: the component is what marks it, and it stays while the item is moving
            entMan.System<ThrowingSystem>().TryThrow(crowbar, new Vector2(-1.5f, 0f), 6f);
        });

        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<BeamComponent>().Count(), Is.GreaterThan(0), "A thrown object should set the arc off.");
            Assert.That(entMan.GetComponent<ArcFaultComponent>(arc).NextArc, Is.GreaterThan(timing.CurTime));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CollapseSwitchesOnAndOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var (_, mapId) = await CreateFloor(pair, 20);

        EntityUid anomaly = default;
        EntityUid human = default;
        await server.WaitPost(() =>
        {
            anomaly = entMan.SpawnEntity(CollapseProto, new MapCoordinates(0.5f, 0.5f, mapId));
            human = entMan.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 4.5f, mapId));
        });

        var seenOn = false;
        var seenOff = false;
        for (var i = 0; i < 160; i++)
        {
            await pair.RunSeconds(0.25f);
            await server.WaitPost(() =>
            {
                if (entMan.GetComponent<ErrorgateAnomalyComponent>(anomaly).Active)
                    seenOn = true;
                else
                    seenOff = true;
            });

            if (seenOn && seenOff)
                break;
        }

        Assert.That(seenOn && seenOff, Is.True, "A collapse should switch on and off.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CollapseWaitsUntilSomethingComesInReach()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var (_, mapId) = await CreateFloor(pair, 20);

        EntityUid anomaly = default;
        await server.WaitPost(() => anomaly = entMan.SpawnEntity(CollapseProto, new MapCoordinates(0.5f, 0.5f, mapId)));

        // A new fault is on for at least a second, and waits while it is alone
        await pair.RunSeconds(0.3f);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ErrorgateAnomalyComponent>(anomaly);
            Assert.That(comp.Active, Is.True);
            Assert.That(comp.Engaged, Is.False, "An empty collapse should only wait.");
        });

        await server.WaitPost(() => entMan.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 4.5f, mapId)));
        await pair.RunSeconds(0.3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ErrorgateAnomalyComponent>(anomaly).Engaged, Is.True, "Someone walking in should set it off.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CollapseHoldsOnToSomeoneLyingAtItsCenter()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var (_, mapId) = await CreateFloor(pair, 20);

        EntityUid anomaly = default;
        await server.WaitPost(() =>
        {
            anomaly = entMan.SpawnEntity(CollapseProto, new MapCoordinates(0.5f, 0.5f, mapId));
            var comp = entMan.GetComponent<ErrorgateAnomalyComponent>(anomaly);

            // A short window, and no damage so the victim survives: someone in its grip must keep it on past both
            comp.ActiveSeconds = 2f;
            comp.Damage = new DamageSpecifier();
            entMan.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 0.5f, mapId));
        });

        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ErrorgateAnomalyComponent>(anomaly);
            Assert.That(comp.Active && comp.Engaged, Is.True, "A collapse should not let go of someone lying at its center.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArcIsQuietWhenNobodyIsInReach()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var (_, mapId) = await CreateFloor(pair, 20);

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity(ArcProto, new MapCoordinates(0.5f, 0.5f, mapId));

            // Objects are no targets, and a human beyond four tiles is out of reach
            entMan.SpawnEntity("Crowbar", new MapCoordinates(2.5f, 0.5f, mapId));
            entMan.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 6.5f, mapId));
        });

        // Longer than the longest time between two arcs
        await pair.RunSeconds(10);

        Assert.That(entMan.EntityQuery<BeamComponent>().Count(), Is.EqualTo(0), "An arc fault with nobody in reach must stay dark.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CollapsePullsPeopleAndLooseThingsIn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();

        var (_, mapId) = await CreateFloor(pair, 20);

        EntityUid anomaly = default;
        EntityUid item = default;
        EntityUid human = default;
        EntityUid outside = default;
        await server.WaitPost(() =>
        {
            anomaly = entMan.SpawnEntity(CollapseProto, new MapCoordinates(0.5f, 0.5f, mapId));
            // Always on, the cycle has its own test
            entMan.GetComponent<ErrorgateAnomalyComponent>(anomaly).ActiveSeconds = 0f;
            item = entMan.SpawnEntity("Crowbar", new MapCoordinates(4.5f, 0.5f, mapId));
            human = entMan.SpawnEntity("MobHuman", new MapCoordinates(0.5f, 5.5f, mapId));
            outside = entMan.SpawnEntity("MobHuman", new MapCoordinates(-9.5f, 0.5f, mapId));
        });

        float Distance(EntityUid uid) => (xformSys.GetWorldPosition(uid) - xformSys.GetWorldPosition(anomaly)).Length();

        var itemBefore = 0f;
        var outsideBefore = 0f;
        await server.WaitPost(() =>
        {
            itemBefore = Distance(item);
            outsideBefore = Distance(outside);
        });

        // A standing person is dragged in: at least two tiles closer, or already crushed and torn apart
        var closest = 5f;
        var gone = false;
        for (var i = 0; i < 5; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                if (!entMan.EntityExists(human))
                    gone = true;
                else
                    closest = Math.Min(closest, Distance(human));
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(gone || closest <= 3.5f, Is.True, $"A person five tiles away should be dragged at least a tile and a half closer, got to {closest}.");
            Assert.That(Distance(item), Is.LessThan(itemBefore - 0.5f), "A loose item should be dragged toward the collapse.");
            Assert.That(Math.Abs(Distance(outside) - outsideBefore), Is.LessThan(0.2f), "Nothing beyond the pull range should move.");
        });

        await pair.CleanReturnAsync();
    }

    // Damage by kind, and the reveal

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
            entMan.GetComponent<ErrorgateAnomalyComponent>(anomaly).ActiveSeconds = 0f;

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
    public async Task ThrowingSomethingInRevealsAFaultForAWhile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();

        var (_, mapId) = await CreateFloor(pair, 20);

        EntityUid arc = default;
        EntityUid crowbar = default;
        await server.WaitPost(() =>
        {
            arc = entMan.SpawnEntity(ArcProto, new MapCoordinates(0.5f, 0.5f, mapId));
            crowbar = entMan.SpawnEntity("Crowbar", new MapCoordinates(7.5f, 0.5f, mapId));
        });

        bool Revealed()
        {
            var comp = entMan.GetComponent<ErrorgateAnomalyComponent>(arc);
            return comp.RevealedUntil is { } until && until > timing.CurTime;
        }

        await pair.RunSeconds(1);
        await server.WaitAssertion(() => Assert.That(Revealed(), Is.False, "A fault starts hidden."));

        // Throw it right through the fault
        await server.WaitPost(() => entMan.System<ThrowingSystem>().TryThrow(crowbar, new Vector2(-8, 0), 8f, playSound: false));
        await pair.RunSeconds(1);
        await server.WaitAssertion(() => Assert.That(Revealed(), Is.True, "Something thrown into a fault should reveal it."));

        // Nothing else happens: the item has come to rest and the reveal runs out
        await pair.RunSeconds(15);
        await server.WaitAssertion(() => Assert.That(Revealed(), Is.False, "The reveal should end after its timer."));

        await pair.CleanReturnAsync();
    }
}
