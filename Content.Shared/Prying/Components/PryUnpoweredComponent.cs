using Robust.Shared.GameStates;

namespace Content.Shared.Prying.Components;

///<summary>
/// Applied to entities that can be pried open without tools while unpowered
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PryUnpoweredComponent : Component
{
    [DataField]
    // ERRORGATE: hand-prying time = door PryTime (1.5s) / PryModifier, so 1.5 gives about 1 second (was 0.1, 15 seconds)
    public float PryModifier = 1.5f;
}
