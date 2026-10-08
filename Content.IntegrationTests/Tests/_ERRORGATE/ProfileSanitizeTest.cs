#nullable enable
using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     A profile imported with traits, flavor text or pronouns must lose them on the server.
/// </summary>
[TestFixture]
public sealed class ProfileSanitizeTest
{
    [Test]
    public async Task ImportedProfilesAreStripped()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithSex(Sex.Female)
                .WithGender(Gender.Male)
                .WithFlavorText("a long fluff text")
                .WithCustomSpeciesName("Fluffy")
                .WithDisplayPronouns("zer/zim");

            var trait = server.ProtoMan.EnumeratePrototypes<TraitPrototype>().First().ID;
            profile = profile.WithTraitPreference(trait, true);
            Assert.That(profile.TraitPreferences, Is.Not.Empty, "the test profile should carry a trait");

            profile.EnsureValid(session, server.InstanceDependencyCollection);

            Assert.That(profile.TraitPreferences, Is.Empty, "Traits must be stripped.");
            Assert.That(profile.FlavorText, Is.Empty, "Flavor text must be stripped.");
            Assert.That(profile.Customspeciename, Is.Empty, "A custom species name must be stripped.");
            Assert.That(profile.DisplayPronouns, Is.Null, "Cosmetic pronouns must be stripped.");
            Assert.That(profile.Gender, Is.EqualTo(Gender.Female), "The gender follows the sex.");
        });

        await pair.CleanReturnAsync();
    }
}
