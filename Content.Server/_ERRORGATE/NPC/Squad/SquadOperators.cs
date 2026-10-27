using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.Preconditions;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Server._ERRORGATE.NPC.Squad;

public enum SquadMemberMode : byte
{
    /// <summary>The squad has given the NPC a place to be.</summary>
    Move,

    /// <summary>The NPC has an item to go and pick up.</summary>
    Loot,
}

/// <summary>Met when the NPC is in a squad and has a place to go (<c>Move</c>) or something to pick up (<c>Loot</c>).</summary>
public sealed partial class SquadPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField(required: true)]
    public SquadMemberMode Mode;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<NpcSquadMemberComponent>(owner, out var member))
            return false;

        return Mode == SquadMemberMode.Move ? member.MovePoint != null : member.LootTarget != null;
    }
}

/// <summary>Sets <c>TargetCoordinates</c> to the place the squad wants the NPC at, and a short wait before the squad is asked again.</summary>
public sealed partial class PickSquadPositionOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<NpcSquadMemberComponent>(owner, out var member) || member.MovePoint is not { } point)
            return Task.FromResult<(bool Valid, Dictionary<string, object>? Effects)>((false, null));

        return Task.FromResult<(bool Valid, Dictionary<string, object>? Effects)>((true, new Dictionary<string, object>
        {
            { "TargetCoordinates", point },
            { "SquadRange", 1.1f },
            { "SquadWait", 0.8f },
        }));
    }
}

/// <summary>Sets <c>TargetCoordinates</c> to the item the NPC is going to pick up; fails (and forgets it) if the item is gone.</summary>
public sealed partial class PickLootOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<NpcSquadMemberComponent>(owner, out var member))
            return Task.FromResult<(bool Valid, Dictionary<string, object>? Effects)>((false, null));

        if (member.LootTarget is not { } item || !_entManager.EntityExists(item) || _entManager.System<SharedContainerSystem>().IsEntityOrParentInContainer(item))
        {
            member.LootTarget = null;
            return Task.FromResult<(bool Valid, Dictionary<string, object>? Effects)>((false, null));
        }

        return Task.FromResult<(bool Valid, Dictionary<string, object>? Effects)>((true, new Dictionary<string, object>
        {
            { "TargetCoordinates", _entManager.GetComponent<TransformComponent>(item).Coordinates },
            { "SquadRange", 1.2f },
        }));
    }
}

/// <summary>Puts the item the NPC walked to into the first of its worn containers (backpack, belt, ...) it fits in; never into the hands.</summary>
public sealed partial class StowLootOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<NpcSquadMemberComponent>(owner, out var member) || member.LootTarget is not { } item)
            return HTNOperatorStatus.Failed;

        member.LootTarget = null;
        if (!_entManager.EntityExists(item) || _entManager.System<SharedContainerSystem>().IsEntityOrParentInContainer(item))
            return HTNOperatorStatus.Failed;

        var xform = _entManager.GetComponent<TransformComponent>(owner);
        if (!xform.Coordinates.TryDistance(_entManager, _entManager.GetComponent<TransformComponent>(item).Coordinates, out var distance) || distance > 2.5f)
            return HTNOperatorStatus.Failed;

        var inventory = _entManager.System<InventorySystem>();
        var storage = _entManager.System<SharedStorageSystem>();
        var stowed = false;
        var slots = inventory.GetSlotEnumerator(owner);
        while (!stowed && slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } bag && _entManager.HasComponent<StorageComponent>(bag))
                stowed = storage.Insert(bag, item, out _, owner);
        }

        if (stowed)
            _entManager.System<NpcSpeechSystem>().TrySay(owner, "loot", 0.5f);

        return stowed ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }
}
