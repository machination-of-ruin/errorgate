using Content.IntegrationTests.Tests.Interaction;

namespace Content.IntegrationTests.Tests.Construction.Interaction;

public sealed class CraftingTests : InteractionTest
{
    /// <summary>
    /// Craft a simple instant recipe
    /// </summary>
    [Test]
    public async Task CraftRods()
    {
        await PlaceInHands(Steel);
        await CraftItem(Rod);
        await FindEntity((Rod, 2));
    }
}
