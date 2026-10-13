namespace Content.Server._ERRORGATE.World;

/// <summary>
///     A gate between two locations. Using it moves the user, and what they pull, to the gate it is linked to, unless one of the two
///     locations is being wiped by the storm. Mobs do not use gates.
/// </summary>
[RegisterComponent]
public sealed partial class WorldTransitionComponent : Component
{
    /// <summary>The location this gate is in.</summary>
    [ViewVariables]
    public string Location = string.Empty;

    /// <summary>The name of this gate in its location.</summary>
    [ViewVariables]
    public string Gate = string.Empty;
}
