using System.Linq;
using System.Threading.Tasks;
using Content.Server._ERRORGATE.World;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tmp;

// Throwaway engine check of the web: build, walk through the gates, the storm (not committed).
[TestFixture]
public sealed class WorldGraphCheck
{
    [Test]
    public async Task Graph()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var world = server.EntMan.System<WorldSystem>();

        await server.WaitPost(() => Assert.That(world.TryBuildGraph("Default", 11, out var e), e));
        await server.WaitRunTicks(400);

        await server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine("GRAPH locations: " + string.Join(", ", world.Locations.Keys));
            TestContext.Out.WriteLine("GRAPH gates: " + string.Join(", ", world.GateNames.Select(g => $"{g.Location}.{g.Gate}")));
            foreach (var id in world.Locations.Keys)
            {
                var (state, strike) = world.StormOf(id);
                TestContext.Out.WriteLine($"GRAPH {id}: {state}, strike in {(strike is { } s ? (s - world.Now).TotalMinutes.ToString("0") : "-")} min");
            }

            var em2 = server.EntMan;
            foreach (var id in world.Locations.Keys)
            {
                var map = world.Locations[id].MapId;
                var rarity = new System.Collections.Generic.Dictionary<int, int>();
                int spawnPoints = 0, mobs = 0;
                var q2 = em2.AllEntityQueryEnumerator<TransformComponent, MetaDataComponent>();
                while (q2.MoveNext(out var uid2, out var x2, out var meta2))
                {
                    if (x2.MapID != map) continue;
                    var proto = meta2.EntityPrototype?.ID ?? "";
                    if (proto.StartsWith("SpawnPoint")) spawnPoints++;
                    if (proto.StartsWith("MobSpawner")) mobs++;
                    if (em2.TryGetComponent<Content.Server._ERRORGATE.LootManager.LootSpawnerComponent>(uid2, out var ls))
                        rarity[ls.Rarity] = (rarity.TryGetValue(ls.Rarity, out var rc) ? rc : 0) + 1;
                }
                TestContext.Out.WriteLine($"GRAPH {id}: spawn points {spawnPoints}, mob spawners {mobs}, loot spawners by tier " + string.Join(" ", rarity.OrderBy(r => r.Key).Select(r => $"{r.Key}:{r.Value}")));
            }

            foreach (var id in world.Locations.Keys)
            {
                var map = world.Locations[id].MapId;
                int items = 0;
                var qi = em2.AllEntityQueryEnumerator<Content.Server._ERRORGATE.Despawn.DespawnItemComponent, TransformComponent>();
                while (qi.MoveNext(out _, out _, out var xi))
                    if (xi.MapID == map) items++;
                var lm = server.EntMan.System<Content.Server._ERRORGATE.LootManager.LootManagerSystem>();
                int spawnersTotal = 0, spawnersWithLoot = 0, drawable = 0;
                var qs = em2.AllEntityQueryEnumerator<Content.Server._ERRORGATE.LootManager.LootSpawnerComponent, TransformComponent>();
                while (qs.MoveNext(out var su, out var sc, out var sx))
                {
                    if (sx.MapID != map) continue;
                    spawnersTotal++;
                    var tab = lm.GetLootTable(sc.Rarity, sc.Location, su);
                    if (tab.Values.Any(v => v > 0)) { spawnersWithLoot++; drawable += tab.Count; }
                }
                TestContext.Out.WriteLine($"GRAPH {id}: loot spawners {spawnersTotal}, with something to draw {spawnersWithLoot}");
                TestContext.Out.WriteLine($"GRAPH {id}: loot items {items}");
            }

            foreach (var id in world.Locations.Keys)
            {
                var gr = world.Locations[id].Ground;
                if (gr == default) continue;
                var gg = em2.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(gr);
                var wc = System.Numerics.Vector2.Transform(gg.LocalAABB.Center, em2.System<SharedTransformSystem>().GetWorldMatrix(gr));
                if (world.Locations[id].Layout is { } lay2 && lay2.Features.Count > 0)
                    TestContext.Out.WriteLine($"GRAPH {id}: features {string.Join(", ", lay2.Features.GroupBy(f => f.Kind).Select(g => $"{g.Key} {g.Count()}"))}, feature entities {lay2.Items.Count}, entities in the location {world.Locations[id].Entities}");
                if (world.Locations[id].Layout is { } lay3)
                    TestContext.Out.WriteLine($"GRAPH {id}: gates and their distance to the edge: {string.Join(", ", lay3.GatePlaces.Select(g => g.Key + " " + lay3.Dist(g.Value.Tile)))}");
                TestContext.Out.WriteLine($"GRAPH {id}: ground centre in world coordinates {wc.X:0.0}, {wc.Y:0.0}, size {gg.LocalAABB.Width:0} x {gg.LocalAABB.Height:0}");
            }

            foreach (var id in world.Locations.Keys)
            {
                var lmap = world.Locations[id].Map;
                if (!em2.TryGetComponent<Content.Server._ERRORGATE.Anomalies.AnomalyFieldComponent>(lmap, out var af)) continue;
                var xs = em2.System<SharedTransformSystem>();
                var faults = af.Spawned.Where(f => !em2.Deleted(f)).Select(f => xs.GetWorldPosition(f)).ToList();
                var exits = new System.Collections.Generic.List<System.Numerics.Vector2>();
                var mobs = new System.Collections.Generic.List<System.Numerics.Vector2>();
                var qe = em2.AllEntityQueryEnumerator<TransformComponent>();
                while (qe.MoveNext(out var eu, out var ex))
                {
                    if (ex.MapID != world.Locations[id].MapId) continue;
                    if (em2.HasComponent<WorldTransitionComponent>(eu) || em2.HasComponent<Content.Server._ERRORGATE.Bunker.BunkerHatchComponent>(eu)) exits.Add(xs.GetWorldPosition(ex));
                    if (em2.HasComponent<Content.Server._ERRORGATE.SmartMobSpawner.SmartMobSpawnerComponent>(eu)) mobs.Add(xs.GetWorldPosition(ex));
                }
                double Min(System.Collections.Generic.List<System.Numerics.Vector2> o) => faults.Count == 0 || o.Count == 0 ? -1 : faults.Min(f => o.Min(e => (e - f).Length()));
                TestContext.Out.WriteLine($"GRAPH {id}: {faults.Count} faults, nearest to a gate or hatch {Min(exits):0.0} tiles, nearest to a mob spawner {Min(mobs):0.0} tiles");
            }

            foreach (var id in world.Locations.Keys)
            {
                var mapId2 = world.Locations[id].MapId;
                var stacks = new System.Collections.Generic.Dictionary<(EntityUid, Vector2i), int>();
                var qd = em2.AllEntityQueryEnumerator<Content.Server._ERRORGATE.Despawn.DespawnItemComponent, TransformComponent>();
                while (qd.MoveNext(out var du, out _, out var dx))
                {
                    if (dx.MapID != mapId2 || dx.GridUid is not { } dg || !em2.TryGetComponent<Robust.Shared.Map.Components.MapGridComponent>(dg, out var dgc)) continue;
                    if (em2.System<Robust.Shared.Containers.SharedContainerSystem>().IsEntityInContainer(du)) continue;
                    var key = (dg, em2.System<SharedMapSystem>().TileIndicesFor(dg, dgc, dx.Coordinates));
                    stacks[key] = (stacks.TryGetValue(key, out var sc) ? sc : 0) + 1;
                }
                TestContext.Out.WriteLine($"GRAPH {id}: loose items {stacks.Values.Sum()} on {stacks.Count} tiles, tiles with more than one item {stacks.Values.Count(v => v > 1)}");
            }

            foreach (var id in world.Locations.Keys)
            {
                var m3 = world.Locations[id].MapId;
                int keys = 0, cards = 0;
                var qm = em2.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (qm.MoveNext(out _, out var mm, out var xm))
                {
                    if (xm.MapID != m3) continue;
                    if (mm.EntityPrototype?.ID == "BunkerKey") keys++;
                    if (mm.EntityPrototype?.ID == "FactoryCard") cards++;
                }
                int barrels = 0, broken = 0, posts = 0, campfires = 0;
                var qb = em2.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (qb.MoveNext(out _, out var mb, out var xb))
                {
                    if (xb.MapID != m3) continue;
                    var pid = mb.EntityPrototype?.ID;
                    if (pid == "BunkerRadiationBarrel") barrels++;
                    if (pid == "LightPostSmallBroken") broken++;
                    if (pid == "LightPostSmall") posts++;
                    if (pid == "CampfireWorld") campfires++;
                }
                TestContext.Out.WriteLine($"GRAPH {id}: radiation barrels {barrels}, post lights {posts} whole and {broken} broken, campfires {campfires}");
                if (keys + cards > 0) TestContext.Out.WriteLine($"GRAPH {id}: bunker keys {keys}, factory cards {cards}");
            }

            // The tunnel is passable from one gate to the other without digging
            var tunnel = world.Locations["TunnelVillageCity"].Tunnel!;
            var start = tunnel.Gates["west"].Arrival;
            var goal = tunnel.Gates["east"].Arrival;
            var seen = new System.Collections.Generic.HashSet<Vector2i> { start };
            var stack = new System.Collections.Generic.Stack<Vector2i>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                foreach (var d in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
                {
                    var n = c + d;
                    if (tunnel.Floor.Contains(n) && !tunnel.Rubble.Contains(n) && !tunnel.Cover.Contains(n) && seen.Add(n))
                        stack.Push(n);
                }
            }

            TestContext.Out.WriteLine($"GRAPH tunnel: {tunnel.Path.Count} path tiles, {tunnel.Floor.Count} floor, rubble {tunnel.Rubble.Count}, cover {tunnel.Cover.Count}, east gate reachable without digging: {seen.Contains(goal)}");
        });

        // Walk: a person at the village gate goes through to the tunnel and on to the city
        EntityUid person = default;
        await server.WaitPost(() =>
        {
            Assert.That(world.TryGetArrival("Village", "east", out var arrival));
            person = server.EntMan.SpawnEntity("MobHuman", arrival);
        });
        await server.WaitRunTicks(3);

        async Task Use(string location, string gate)
        {
            await server.WaitPost(() =>
            {
                var em = server.EntMan;
                var gateEnt = em.AllEntityQueryEnumerator<WorldTransitionComponent>();
                while (gateEnt.MoveNext(out var uid, out var comp))
                {
                    if (comp.Location == location && comp.Gate == gate)
                    {
                        
                        em.System<SharedInteractionSystem>().InteractionActivate(person, uid, checkCanInteract: false, checkUseDelay: false, checkAccess: false, complexInteractions: true);
                        return;
                    }
                }

                Assert.Fail($"no gate {location}.{gate}");
            });
        }

        string Where()
        {
            var map = server.EntMan.GetComponent<TransformComponent>(person).MapID;
            return world.Locations.FirstOrDefault(l => l.Value.MapId == map).Key ?? "?";
        }

        await Use("Village", "east");
        await server.WaitRunTicks(2);
        string where1 = "";
        await server.WaitPost(() => where1 = Where());
        await Use("TunnelVillageCity", "east");
        await server.WaitRunTicks(2);
        string where2 = "";
        await server.WaitPost(() => where2 = Where());
        TestContext.Out.WriteLine($"GRAPH walk: village gate -> {where1}; tunnel east gate -> {where2}");

        await Use("SnowCity", "east");
        await server.WaitRunTicks(2);
        string where3 = "";
        await server.WaitPost(() => where3 = Where());
        // The way into the laboratory is the bunker hatch: without the key it stays shut, with it the airlock cycles
        EntityUid hatchEnt = default;
        await server.WaitPost(() =>
        {
            var em = server.EntMan;
            var q = em.AllEntityQueryEnumerator<Content.Server._ERRORGATE.Bunker.BunkerHatchComponent>();
            while (q.MoveNext(out var uid, out _))
                hatchEnt = uid;
            TestContext.Out.WriteLine($"GRAPH hatch exists {hatchEnt != default}, on the map of TunnelCityLab {hatchEnt != default && em.GetComponent<TransformComponent>(hatchEnt).MapID == world.Locations["TunnelCityLab"].MapId}");
        });
        async Task UseHatch()
        {
            await server.WaitPost(() => server.EntMan.System<SharedInteractionSystem>().InteractionActivate(person, hatchEnt, checkCanInteract: false, checkUseDelay: false, checkAccess: false, complexInteractions: true));
        }
        await UseHatch();
        await server.WaitRunTicks(2);
        string whereNoKey = "";
        await server.WaitPost(() => whereNoKey = Where());
        await server.WaitPost(() =>
        {
            var em = server.EntMan;
            var key = em.SpawnEntity("BunkerKey", em.GetComponent<TransformComponent>(person).Coordinates);
            Assert.That(em.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickupAnyHand(person, key));
        });
        await UseHatch();
        await server.WaitRunTicks(1100);
        await UseHatch();
        await server.WaitRunTicks(2);
        string where4 = "";
        await server.WaitPost(() => where4 = Where());
        TestContext.Out.WriteLine($"GRAPH hatch without key stayed in {whereNoKey}");
        TestContext.Out.WriteLine($"GRAPH walk 2: city east gate -> {where3}; tunnel east gate -> {where4}");

        await Use("MilitaryBase", "gate");
        await server.WaitRunTicks(2);
        string where5 = "";
        await server.WaitPost(() => where5 = Where());
        TestContext.Out.WriteLine($"GRAPH walk 3: base gate -> {where5}");

        // The storm: a warning closes the way in, then the location is built again with a new layout and the person in it is gone
        int seedBefore = 0;
        await server.WaitPost(() => seedBefore = world.Locations["SnowCity"].Seed);
        await server.WaitPost(() => world.StartWarning("SnowCity", world.Now + System.TimeSpan.FromSeconds(70)));
        await server.WaitRunTicks(400);
        await server.WaitPost(() =>
        {
            var f = typeof(WorldSystem).GetField("_notice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var d = (System.Collections.IDictionary) f.GetValue(world)!;
            var v = d["SnowCity"]!;
            TestContext.Out.WriteLine("GRAPH notice state after 13 s of 70: " + v);
        });
        await server.WaitPost(() => world.StartWarning("SnowCity", world.Now + System.TimeSpan.FromSeconds(30)));
        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            var wm = world.Locations["SnowCity"].Map;
            var wc = server.EntMan.GetComponent<Content.Shared.Weather.WeatherComponent>(wm);
            TestContext.Out.WriteLine("GRAPH weather during the warning: " + string.Join(", ", wc.Weather.Keys));
        });
        await server.WaitRunTicks(450);
        await server.WaitPost(() =>
        {
            var wm = world.Locations["SnowCity"].Map;
            var wc = server.EntMan.GetComponent<Content.Shared.Weather.WeatherComponent>(wm);
            TestContext.Out.WriteLine("GRAPH weather 15 s later (15 s left): " + string.Join(", ", wc.Weather.Keys.Select(k => k.ToString() + ":" + wc.Weather[k].State)));
        });
        bool canEnterWarning = true;
        await server.WaitPost(() => canEnterWarning = world.CanEnter("SnowCity"));
        await server.WaitPost(() => world.Strike("SnowCity"));
        await server.WaitRunTicks(600);
        bool canEnterAfter = false, personAlive = true;
        int seedAfter = 0;
        await server.WaitPost(() =>
        {
            canEnterAfter = world.CanEnter("SnowCity");
            personAlive = server.EntMan.EntityExists(person);
            seedAfter = world.Locations["SnowCity"].Seed;
        });
        TestContext.Out.WriteLine($"GRAPH storm: can enter during the warning {canEnterWarning}, after the wipe {canEnterAfter}, the person survived {personAlive}, new seed {seedAfter != seedBefore}, tunnel still built {world.Locations.ContainsKey("TunnelVillageCity")}");
        await server.WaitPost(() => world.Strike("Laboratory"));
        await server.WaitRunTicks(450);
        await server.WaitPost(() =>
        {
            var em = server.EntMan;
            var lab = world.Locations["Laboratory"];
            var tun = world.Locations["TunnelCityLab"];
            var linked = tun.Hatch is { } h && !em.Deleted(h) && em.GetComponent<Content.Server._ERRORGATE.Bunker.BunkerHatchComponent>(h).Vestibule == lab.Vestibule;
            TestContext.Out.WriteLine($"GRAPH relink after the laboratory was wiped: hatch joined to the new vestibule {linked}, one hatch only {em.Count<Content.Server._ERRORGATE.Bunker.BunkerHatchComponent>() == 1}");
        });
        await pair.CleanReturnAsync();
    }
}
