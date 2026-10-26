using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>Takes a gun out of the NPC's clothes or bag into its hand; fails if it carries none.</summary>
public sealed partial class DrawGunOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _entManager.System<NpcGunSystem>().TryDraw(owner) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }
}
