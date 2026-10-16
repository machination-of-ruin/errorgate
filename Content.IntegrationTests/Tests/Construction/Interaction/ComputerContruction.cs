using Content.IntegrationTests.Tests.Interaction;

namespace Content.IntegrationTests.Tests.Construction.Interaction;

public sealed class ComputerConstruction : InteractionTest
{
    private const string Computer = "Computer";
    private const string ComputerId = "ComputerId";
    private const string ComputerFrame = "ComputerFrame";
    private const string IdBoard = "IDComputerCircuitboard";

    [Test]
    public async Task DeconstructComputer()
    {
        // Spawn initial entity
        await StartDeconstruction(ComputerId);

        // Initial interaction turns id computer into generic computer
        await InteractUsing(Pry);
        AssertPrototype(ComputerFrame);

        // Perform deconstruction steps
        await Interact(
            Pry,
            Cut,
            Screw,
            Pry,
            Wrench,
            Screw);

        // construction finished, entity no longer exists.
        AssertDeleted();

        // Check expected entities were dropped.
        await AssertEntityLookup(
            IdBoard,
            (Cable, 5),
            (Steel, 5),
            (Glass, 2));
    }

    [Test]
    public async Task ChangeComputer()
    {
        // Spawn initial entity
        await SpawnTarget(ComputerId);

        // Initial interaction turns id computer into generic computer
        await InteractUsing(Pry);
        AssertPrototype(ComputerFrame);

        // Perform partial deconstruction steps
        await Interact(
            Pry,
            Cut,
            Screw,
            Pry);

        // Entity should still exist
        AssertPrototype(ComputerFrame);

        // Begin re-constructing with a new circuit board
        await Interact(
            "CargoRequestComputerCircuitboard",
            Screw,
            (Cable, 5),
            (Glass, 2),
            Screw);

        // Construction finished, target entity was replaced with a new one:
        AssertPrototype("ComputerCargoOrders");
    }
}

