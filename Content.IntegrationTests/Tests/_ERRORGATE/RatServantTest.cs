#nullable enable
using System.Linq;
using Content.Server.NPC.HTN;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Playtest: rat servants placed by mob spawners (no rat king, so no orders) did nothing at all, because every branch of their
///     task needs an order. NPC behaviour cannot be run in the test world (planning fails for every NPC there), so this checks
///     the data: there is a branch that needs no order.
/// </summary>
[TestFixture]
public sealed class RatServantTest
{
    [Test]
    public async Task ARatServantWithoutOrdersStillHasSomethingToDo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var compound = server.ProtoMan.Index<HTNCompoundPrototype>("RatServantCompound");
            Assert.That(compound.Branches.Any(b => b.Preconditions.Count == 0), Is.True, "A branch that needs no order.");
        });

        await pair.CleanReturnAsync();
    }
}
