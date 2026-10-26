using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>
///     Loads the gun in the NPC's hand with new ammunition out of nowhere (see <see cref="NpcGunSystem"/>), after the time such a reload
///     takes: the old magazine comes out with its sound, the NPC stands for a while, the new one goes in.
/// </summary>
public sealed partial class ReloadGunOperator : HTNOperator
{
    private const string TimerKey = "ReloadTimer";

    [Dependency] private readonly IEntityManager _entManager = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var guns = _entManager.System<NpcGunSystem>();

        if (!blackboard.TryGetValue<float>(TimerKey, out var timer, _entManager))
        {
            guns.PlayReloadStart(owner);
            _entManager.System<NpcSpeechSystem>().TrySay(owner, "reload", 0.6f);
            blackboard.SetValue(TimerKey, guns.ReloadTime(owner));
            return HTNOperatorStatus.Continuing;
        }

        timer -= frameTime;
        if (timer > 0f)
        {
            blackboard.SetValue(TimerKey, timer);
            return HTNOperatorStatus.Continuing;
        }

        blackboard.Remove<float>(TimerKey);
        return guns.TryReload(owner) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        blackboard.Remove<float>(TimerKey);
    }
}
