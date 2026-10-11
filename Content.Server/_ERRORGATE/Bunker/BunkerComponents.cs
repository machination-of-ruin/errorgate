using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._ERRORGATE.Bunker;

/// <summary>
///     The hatch on the surface. The bunker key opens it for a while, then it locks again. Using it while it is open
///     takes the user (and whatever they pull) to the bunker vestibule.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerHatchComponent : Component
{
    [DataField]
    public float OpenSeconds = 15f;

    [ViewVariables]
    public bool Open;

    [ViewVariables]
    public TimeSpan CloseAt;

    /// <summary>Where arrivals appear in the vestibule.</summary>
    [ViewVariables]
    public EntityCoordinates? Arrival;

    /// <summary>The vestibule controller to tell about an arrival.</summary>
    [ViewVariables]
    public EntityUid? Vestibule;
}

/// <summary>
///     The way back up, inside the vestibule. Locked until the vestibule has cycled for someone coming out.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerLadderComponent : Component
{
    [ViewVariables]
    public bool Locked = true;

    /// <summary>Where the way up comes out on the surface.</summary>
    [ViewVariables]
    public EntityCoordinates? Surface;
}

public enum BunkerVestibuleState : byte
{
    Idle,
    CyclingIn,
    CyclingOut,
}

/// <summary>
///     The timed airlock between the surface and the bunker. Arriving from the hatch or coming in from the bunker starts a cycle:
///     both exits are locked, lights turn and a siren sounds. Afterwards the exit on the far side opens.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerVestibuleComponent : Component
{
    [ViewVariables]
    public BunkerVestibuleState State;

    [ViewVariables]
    public TimeSpan Until;

    /// <summary>The door into the bunker.</summary>
    [ViewVariables]
    public EntityUid? Door;

    [ViewVariables]
    public EntityUid? Ladder;

    [ViewVariables]
    public List<EntityUid> Lights = new();

    /// <summary>Floor of the room, in the tile coordinates of its grid. Whoever stands in it takes part in a cycle.</summary>
    [ViewVariables]
    public Box2 Room;
}

/// <summary>
///     Marks the vestibule door so the system can tell when it closes.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerVestibuleDoorComponent : Component
{
    [ViewVariables]
    public EntityUid Vestibule;
}
