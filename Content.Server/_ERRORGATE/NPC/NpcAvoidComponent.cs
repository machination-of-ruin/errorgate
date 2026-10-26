namespace Content.Server._ERRORGATE.NPC;

/// <summary>
///     A thing NPCs keep away from although it blocks nothing (a campfire sets whoever walks over it on fire). The steering of an NPC
///     treats it as an obstacle within <see cref="Radius"/> tiles (<c>NPCSteeringSystem.AvoidHazards</c>), and squads do not pick up
///     items that lie near it.
/// </summary>
[RegisterComponent]
public sealed partial class NpcAvoidComponent : Component
{
    [DataField]
    public float Radius = 1.6f;
}
