#nullable enable
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Everything the examine window used to hide behind buttons is printed in the text.
/// </summary>
[TestFixture]
public sealed class InlineExamineTest
{
    private static string Examine(IEntityManager entMan, EntityUid target, EntityUid examiner) =>
        entMan.System<ExamineSystemShared>().GetExamineText(target, examiner).ToString();

    [Test]
    public async Task DetailsAndHealthAreInline()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            entMan.System<Robust.Server.GameObjects.MapSystem>().CreateMap(out var mapId);
            var pos = new MapCoordinates(0, 0, mapId);
            var viewer = entMan.SpawnEntity("MobHuman", pos);
            var other = entMan.SpawnEntity("MobHuman", pos);

            var armor = entMan.SpawnEntity("ClothingOuterArmorBulletproof", pos);
            Assert.That(Examine(entMan, armor, viewer), Does.Contain(Loc.GetString("armor-examine")), "Armor stats should be inline.");

            var knife = entMan.SpawnEntity("CombatKnife", pos);
            Assert.That(Examine(entMan, knife, viewer), Does.Not.Contain("does the following"), "Weapon damage stays a button, it must not repeat in the text.");

            Assert.That(Examine(entMan, other, viewer), Does.Contain("no obvious wounds"),
                "An unhurt person should read as healthy.");
            Assert.That(Examine(entMan, other, viewer), Does.Contain("man.").And.Not.Contain("human."),
                "Humans read as a man or a woman, not as a human.");

            var burn = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(60));
            entMan.System<DamageableSystem>().TryChangeDamage(other, burn);
            Assert.That(Examine(entMan, other, viewer), Does.Not.Contain("no obvious wounds"),
                "A wounded person should show wounds, not the healthy line.");
        });

        await pair.CleanReturnAsync();
    }
}
