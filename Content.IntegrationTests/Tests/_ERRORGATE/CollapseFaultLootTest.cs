#nullable enable
using System.Numerics;
using Content.Shared._ERRORGATE.Anomalies;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Playtest: collapse faults with loot lying under them never switched off, so the loot could not be taken. Loose items shake
///     around the center while the fault drags them, and that must not count as something walking in.
/// </summary>
[TestFixture]
public sealed class CollapseFaultLootTest
{
    [Test]
    public async Task LooseItemsUnderACollapseFaultDoNotKeepItOn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var xform = entMan.System<SharedTransformSystem>();

        EntityUid fault = default;
        await server.WaitPost(() =>
        {
            fault = entMan.SpawnEntity("ErrorgateAnomalyCollapse", testMap.GridCoords);
            var anomaly = entMan.GetComponent<ErrorgateAnomalyComponent>(fault);
            anomaly.ActiveSeconds = 4f;
            anomaly.IdleSeconds = 4f;
            anomaly.Active = true;
            anomaly.Engaged = true;
            anomaly.NextSwitch = TimeSpan.Zero;

            var center = xform.GetMapCoordinates(fault).Position;
            foreach (var proto in new[] { "WeaponRevolverPython", "Crowbar", "FoodBreadPlain", "SheetSteel1", "ClothingUniformJumpsuitColorGrey" })
            {
                entMan.SpawnEntity(proto, new MapCoordinates(center + new Vector2(0.4f, 0.3f), testMap.MapId));
                entMan.SpawnEntity(proto, new MapCoordinates(center + new Vector2(-1.5f, 0.8f), testMap.MapId));
            }
        });

        var wentIdle = false;
        var disengaged = false;
        for (var second = 0; second < 40; second++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                var anomaly = entMan.GetComponent<ErrorgateAnomalyComponent>(fault);
                if (!anomaly.Engaged)
                    disengaged = true;
                if (!anomaly.Active)
                    wentIdle = true;
            });
        }

        Assert.That(disengaged, Is.True, "Loot alone must let the fault stop pulling.");
        Assert.That(wentIdle, Is.True, "...and switch off, so the loot can be taken.");

        await pair.CleanReturnAsync();
    }
}
