namespace Content.Shared._ERRORGATE.Reputation;

/// <summary>
///     What killing this mob does to the killer's standing with the factions (docs/design/reputation.md, kill matrix): faction name to
///     change. Only a player who deals the killing blow is paid, and only once per mob. Players are not mobs with this component; their
///     values are in <c>ReputationKillSystem</c>.
/// </summary>
[RegisterComponent]
public sealed partial class ReputationRewardComponent : Component
{
    [DataField]
    public Dictionary<string, int> Standing = new();

    /// <summary>Already paid out (a revived mob does not pay twice).</summary>
    public bool Paid;
}
