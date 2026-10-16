#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server._ERRORGATE.World;
using Content.Server.GameTicking;
using Content.Shared._ERRORGATE.World;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tmp;

// Throwaway: a whole round on the World map (not committed).
[TestFixture]
public sealed class WorldRound
{
    [Test]
    public async Task Round()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, InLobby = true, Dirty = true });
        pair.Server.CfgMan.SetCVar(CCVars.GameMap, "World");
        pair.Server.CfgMan.SetCVar(WorldCVars.Enabled, true);
        var ticker = pair.Server.System<GameTicker>();
        var world = pair.Server.System<WorldSystem>();

        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(20);

        var user = pair.Client.User!.Value;
        TestContext.Out.WriteLine($"ROUND run level {ticker.RunLevel}, game status {ticker.PlayerGameStatuses[user]}");
        var uid = (pair.Server.PlayerMan.SessionsDict.TryGetValue(user, out var sess) ? sess.AttachedEntity : null);
        TestContext.Out.WriteLine($"ROUND player entity {uid}");
        if (uid != null)
        {
            var xf = pair.Server.EntMan.GetComponent<TransformComponent>(uid.Value);
            var where = world.Locations.FirstOrDefault(l => l.Value.MapId == xf.MapID).Key ?? "?";
            world.Locations.TryGetValue(where, out var loc);
            var spawn = loc?.Layout?.SpawnTiles;
            var pos = pair.Server.EntMan.System<SharedTransformSystem>().GetWorldPosition(uid.Value);
            var gpos = pair.Server.EntMan.System<SharedTransformSystem>().GetWorldPosition(loc!.Ground);
            var near = spawn != null && spawn.Any(t => (new System.Numerics.Vector2(t.X + 0.5f, t.Y + 0.5f) + gpos - pos).Length() < 3);
            TestContext.Out.WriteLine($"ROUND the player is in {where} at {pos}, next to a spawn point of the zone: {near}");
        }

        for (var step = 0; step < 8 && uid != null; step++)
        {
            await pair.RunTicksSync(150);
            await pair.Server.WaitPost(() =>
            {
                var em = pair.Server.EntMan;
                var atmos = em.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();
                var air = atmos.GetContainingMixture(uid.Value, true, true);
                var dmg = em.TryGetComponent<Content.Shared.Damage.DamageableComponent>(uid.Value, out var d) ? d.TotalDamage.ToString() : "?";
                var x = em.GetComponent<TransformComponent>(uid.Value);
                TestContext.Out.WriteLine($"ROUND t={step * 150} ticks: pressure {(air?.Pressure ?? -1):0.0} kPa, O2 {(air?.GetMoles(Content.Shared.Atmos.Gas.Oxygen) ?? -1):0.00}, temp {(air?.Temperature ?? -1):0}, damage {dmg}, grid {x.GridUid}");
            });
        }

        bool readyBefore = false, readyDuring = true, readyAfter = false, inStation = false;
        await pair.Server.WaitPost(() => { readyBefore = world.SpawnReady(); world.Strike("Village"); readyDuring = world.SpawnReady(); });
        await pair.RunTicksSync(1200);
        await pair.Server.WaitPost(() =>
        {
            readyAfter = world.SpawnReady();
            var em = pair.Server.EntMan;
            var q = em.AllEntityQueryEnumerator<Content.Server.Station.Components.StationDataComponent>();
            while (q.MoveNext(out _, out var sd))
                foreach (var g in sd.Grids) if (g == world.Locations["Village"].Ground) inStation = true;
        });
        TestContext.Out.WriteLine($"ROUND spawn ready before the wipe {readyBefore}, right after it {readyDuring}, when rebuilt {readyAfter}; the new ground is part of the station {inStation}");
        TestContext.Out.WriteLine($"ROUND locations built: {string.Join(", ", world.Locations.Keys)}");
        await pair.Server.WaitPost(() => ticker.RestartRound());
        await pair.RunTicksSync(10);
        TestContext.Out.WriteLine($"ROUND after the round: locations known {world.Locations.Count}");
        await pair.CleanReturnAsync();
    }
}
