using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>Met when the NPC holds a gun with a live round (<see cref="NpcGunSystem.HasRounds"/>); with <c>invert</c>, when it does not.</summary>
public sealed partial class GunLoadedPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField]
    public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _entManager.System<NpcGunSystem>().HasRounds(owner) != Invert;
    }
}
