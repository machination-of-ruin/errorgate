#nullable enable
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.UserInterface;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ERRORGATE;

[TestFixture]
public sealed class PlayerLifeNoticeTest
{
    [TestCase(210)]
    [TestCase(900)]
    public async Task AdminsAreToldWhenAPlayerDies(int damage)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var session = server.ResolveDependency<IPlayerManager>().Sessions[0];
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var testMap = await pair.CreateTestMap();

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var mind = entMan.System<SharedMindSystem>();
            mind.TransferTo(mind.CreateMind(session.UserId), human);
            var spec = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(damage));
            entMan.System<DamageableSystem>().TryChangeDamage(human, spec);
        });
        await pair.RunSeconds(2);

        var found = false;
        await pair.Client.WaitPost(() => found = chat.History.Any(m => m.Msg.Message.StartsWith("DEATH: ") && m.Msg.Message.Contains(session.Name)));
        var dump = "";
        await pair.Client.WaitPost(() => dump = string.Join(" ;; ", chat.History.Select(m => m.Msg.Channel + ":" + m.Msg.Message).Take(30)));
        Assert.That(found, Is.True, "The admin chat has a death notice. " + dump);

        await pair.CleanReturnAsync();
    }
}
