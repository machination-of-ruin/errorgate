#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._ERRORGATE.World;
using Content.Server.GameTicking;
using Content.Shared._ERRORGATE.World;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tmp;

// Throwaway: what the client holds right after spawning in the village, and after going away and coming back (not committed).
[TestFixture]
public sealed class ClientLoad
{
    private static void Report(Content.IntegrationTests.Pair.TestPair pair, string when)
    {
        var c = pair.Client.EntMan;
        TestContext.Out.WriteLine($"CLOAD {when}: client entities {c.EntityCount}, sprites {c.Count<Robust.Client.GameObjects.SpriteComponent>()}, lights {c.Count<Robust.Client.GameObjects.PointLightComponent>()}, occluders {c.Count<Robust.Shared.GameObjects.OccluderComponent>()}, physics bodies {c.Count<Robust.Shared.Physics.Components.PhysicsComponent>()}, grids {c.Count<Robust.Shared.Map.Components.MapGridComponent>()}, anchored: n/a");
    }

    [Test]
    public async Task Load()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, InLobby = true, Dirty = true });
        pair.Server.CfgMan.SetCVar(CCVars.GameMap, "World");
        pair.Server.CfgMan.SetCVar(WorldCVars.Enabled, true);
        var ticker = pair.Server.System<GameTicker>();
        var world = pair.Server.System<WorldSystem>();
        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await pair.Server.WaitRunTicks(300);
        TestContext.Out.WriteLine($"CTIME SERVER ONLY first 300 ticks: {sw.ElapsedMilliseconds / 300.0:0.00} ms per tick");
        sw.Restart();
        await pair.Client.WaitRunTicks(300);
        TestContext.Out.WriteLine($"CTIME CLIENT ONLY first 300 ticks: {sw.ElapsedMilliseconds / 300.0:0.00} ms per tick");
        sw.Restart();
        await pair.RunTicksSync(300);
        TestContext.Out.WriteLine($"CTIME village after spawn: {sw.ElapsedMilliseconds / 300.0:0.00} ms per tick (client and server together)");
        for (var w = 2; w <= 5; w++)
        {
            sw.Restart();
            await pair.RunTicksSync(300);
            TestContext.Out.WriteLine($"CTIME village window {w} (ticks {300 * (w - 1)} to {300 * w}): {sw.ElapsedMilliseconds / 300.0:0.00} ms per tick");
        }
        await pair.RunTicksSync(5);
        var villageMap = world.Locations["Village"].MapId;
        var posBefore = new System.Collections.Generic.Dictionary<EntityUid, System.Numerics.Vector2>();
        var c0 = pair.Client.EntMan;
        var xs0 = c0.System<SharedTransformSystem>();
        await pair.Client.WaitPost(() =>
        {
            var q = c0.AllEntityQueryEnumerator<Robust.Shared.Map.Components.MapGridComponent, TransformComponent, Robust.Shared.Physics.Components.PhysicsComponent>();
            int dyn = 0, awake = 0, total = 0;
            while (q.MoveNext(out var gu, out _, out var gx, out var gp))
            {
                if (gx.MapID != villageMap) continue;
                total++;
                if (gp.BodyType != Robust.Shared.Physics.BodyType.Static) dyn++;
                if (gp.Awake) awake++;
                posBefore[gu] = xs0.GetWorldPosition(gx);
            }
            TestContext.Out.WriteLine($"CGRID village grids on the client: {total}, not static {dyn}, awake {awake}");
        });
        await pair.RunTicksSync(10);
        await pair.Client.WaitPost(() =>
        {
            var moved = 0;
            foreach (var (gu, pos) in posBefore)
            {
                if ((xs0.GetWorldPosition(gu) - pos).Length() > 0.0001f)
                {
                    moved++;
                    if (moved <= 5) TestContext.Out.WriteLine($"CGRID grid {c0.GetComponent<MetaDataComponent>(gu).EntityName} moved {(xs0.GetWorldPosition(gu) - pos).Length():0.0000} in 10 ticks, body {c0.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(gu).BodyType}");
                }
            }
            TestContext.Out.WriteLine($"CGRID grids that moved in 10 ticks: {moved}");
        });
        {
            var s0 = pair.Server.EntMan;
            var smap = s0.System<SharedMapSystem>();
            var ticks1 = new System.Collections.Generic.Dictionary<EntityUid, uint>();
            await pair.Server.WaitPost(() =>
            {
                var q = s0.AllEntityQueryEnumerator<Robust.Shared.Map.Components.MapGridComponent, TransformComponent>();
                while (q.MoveNext(out var gu, out var gc, out var gx))
                    if (gx.MapID == villageMap) ticks1[gu] = gc.LastModifiedTick.Value;
            });
            await pair.RunTicksSync(30);
            await pair.Server.WaitPost(() =>
            {
                var changed = new System.Collections.Generic.List<string>();
                var q = s0.AllEntityQueryEnumerator<Robust.Shared.Map.Components.MapGridComponent, TransformComponent>();
                while (q.MoveNext(out var gu, out var gc, out var gx))
                    if (gx.MapID == villageMap && ticks1.TryGetValue(gu, out var before) && gc.LastModifiedTick.Value != before)
                        changed.Add(s0.GetComponent<MetaDataComponent>(gu).EntityName + " (" + before + " to " + gc.LastModifiedTick.Value + ")");
                TestContext.Out.WriteLine($"CGRID SERVER grid components modified in 30 ticks: {changed.Count}: {string.Join("; ", changed.Take(6))}");
            });
        }
        {
            var mapUid = world.Locations["Village"].Map;
            await pair.Server.WaitPost(() => TestContext.Out.WriteLine($"CMOVED SERVER village map: grids flagged as moved while idle {pair.Server.EntMan.GetComponent<Robust.Shared.Map.Components.MovedGridsComponent>(mapUid).MovedGrids.Count}"));
            await pair.Client.WaitPost(() =>
            {
                var cm = pair.Client.EntMan;
                var q = cm.AllEntityQueryEnumerator<Robust.Shared.Map.Components.MovedGridsComponent, TransformComponent>();
                while (q.MoveNext(out var mu, out var mg, out _))
                    TestContext.Out.WriteLine($"CMOVED CLIENT map {cm.GetComponent<MetaDataComponent>(mu).EntityName}: grids flagged as moved while idle {mg.MovedGrids.Count}");
            });
        }
        Report(pair, "just spawned in the village");
        await pair.Server.WaitPost(() =>
        {
            var em = pair.Server.EntMan;
            var map = em.System<SharedMapSystem>();
            foreach (var (id, loc) in world.Locations)
            {
                var sizes = new System.Collections.Generic.List<int>();
                var q = em.AllEntityQueryEnumerator<Robust.Shared.Map.Components.MapGridComponent, TransformComponent>();
                while (q.MoveNext(out var uid, out var g, out var x))
                {
                    if (x.MapID != loc.MapId) continue;
                    sizes.Add(map.GetAllTiles(uid, g).Count());
                }
                if (id == "Laboratory")
                {
                    var shown = 0;
                    var q2 = em.AllEntityQueryEnumerator<Robust.Shared.Map.Components.MapGridComponent, TransformComponent>();
                    while (q2.MoveNext(out var uid2, out var g2, out var x2) && shown < 6)
                    {
                        if (x2.MapID != loc.MapId) continue;
                        var n = map.GetAllTiles(uid2, g2).Count();
                        if (n >= 10) continue;
                        shown++;
                        var kids = new System.Collections.Generic.List<string>();
                        var en = x2.ChildEnumerator;
                        while (en.MoveNext(out var kid)) kids.Add(em.GetComponent<MetaDataComponent>(kid).EntityPrototype?.ID ?? "?");
                        TestContext.Out.WriteLine($"CLOAD tiny grid {em.GetComponent<MetaDataComponent>(uid2).EntityName} at {x2.LocalPosition}: {n} tiles, children {string.Join(",", kids)}");
                    }
                }

                TestContext.Out.WriteLine($"CLOAD server {id}: {sizes.Count} grids, tiny ones (under 10 tiles) {sizes.Count(s => s < 10)}, biggest {sizes.Max()}");
            }
        });

        var user = pair.Client.User!.Value;
        EntityUid? player = pair.Server.PlayerMan.SessionsDict[user].AttachedEntity;
        EntityCoordinates home = default;
        await pair.Server.WaitPost(() =>
        {
            home = pair.Server.EntMan.GetComponent<TransformComponent>(player!.Value).Coordinates;
            Assert.That(world.TryGetArrival("TunnelVillageCity", "west", out var there));
            pair.Server.EntMan.System<SharedTransformSystem>().SetCoordinates(player.Value, there);
        });
        sw.Restart();
        await pair.RunTicksSync(300);
        TestContext.Out.WriteLine($"CTIME tunnel: {sw.ElapsedMilliseconds / 300.0:0.00} ms per tick");
        Report(pair, "in the tunnel");
        await pair.Server.WaitPost(() => pair.Server.EntMan.System<SharedTransformSystem>().SetCoordinates(player!.Value, home));
        sw.Restart();
        await pair.RunTicksSync(300);
        TestContext.Out.WriteLine($"CTIME back in the village: {sw.ElapsedMilliseconds / 300.0:0.00} ms per tick");
        Report(pair, "back in the village");
        await pair.Server.WaitPost(() =>
        {
            Assert.That(world.TryGetArrival("SnowCity", "west", out var cityGate));
            pair.Server.EntMan.System<SharedTransformSystem>().SetCoordinates(player!.Value, cityGate);
        });
        sw.Restart();
        await pair.RunTicksSync(300);
        TestContext.Out.WriteLine($"CTIME city: {sw.ElapsedMilliseconds / 300.0:0.00} ms per tick");
        await pair.CleanReturnAsync();
    }
}
