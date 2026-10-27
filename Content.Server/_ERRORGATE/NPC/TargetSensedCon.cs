using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._ERRORGATE.NPC;

/// <summary>1 if the NPC can make the target out by light or sound (see <see cref="NpcSenseSystem"/>), else 0. Evaluated in <c>NPCUtilitySystem</c>.</summary>
public sealed partial class TargetSensedCon : UtilityConsideration
{
}
