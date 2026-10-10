using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared.Jittering;

[Access(typeof(SharedJitteringSystem))]
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class JitteringComponent : Component
{
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Amplitude { get; set; }

    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Frequency { get; set; }

    /// <summary>
    ///     ERRORGATE: how far the sprite shakes, as a fraction of the usual (the shake from low stamina is gentle).
    ///     0 means not set, which is the usual full shake.
    /// </summary>
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Scale { get; set; }

    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 LastJitter { get; set; }

    /// <summary>
    ///     The offset that an entity had before jittering started,
    ///     so that we can reset it properly.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 StartOffset = Vector2.Zero;
}
