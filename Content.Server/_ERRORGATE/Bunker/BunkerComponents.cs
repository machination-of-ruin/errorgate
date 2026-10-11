using Robust.Shared.Map;

namespace Content.Server._ERRORGATE.Bunker;

/// <summary>
///     The hatch on the surface. Locked until the vestibule says otherwise. The key on the user starts the sequence (the vestibule
///     decides whether that is possible just now). While it is unlocked, using it takes the user (and whatever they pull) down to the
///     vestibule.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerHatchComponent : Component
{
    [ViewVariables]
    public bool Locked = true;

    /// <summary>Where arrivals appear in the vestibule.</summary>
    [ViewVariables]
    public EntityCoordinates? Arrival;

    /// <summary>The vestibule controller.</summary>
    [ViewVariables]
    public EntityUid? Vestibule;
}

/// <summary>
///     The hatch up, in the west wall of the vestibule. Locked until the vestibule says otherwise.
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

/// <summary>
///     The console in the middle of the vestibule, by the north wall. Pressing it starts the siren inside.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerConsoleComponent : Component
{
    [ViewVariables]
    public EntityUid? Vestibule;
}

/// <summary>
///     Where the sequence is. Every door is locked while a siren sounds.
/// </summary>
public enum BunkerVestibuleState : byte
{
    /// <summary>Everything is shut. The key on the surface hatch starts the outside siren; the console can start the one inside.</summary>
    Locked,

    /// <summary>The siren by the surface hatch. Everything stays locked.</summary>
    SirenOutside,

    /// <summary>The surface hatch and the hatch up are open, people pass freely. The bunker door is shut.</summary>
    OuterOpen,

    /// <summary>The siren inside the vestibule. Everything stays locked.</summary>
    SirenInside,

    /// <summary>The door into the bunker is open, the hatches are shut.</summary>
    InnerOpen,
}

/// <summary>
///     The controller of the timed airlock between the surface and the bunker. It has no body: it holds the state and the
///     things it works (the hatches, the bunker door, the sirens). See docs/design/bunker.md for the sequence.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerVestibuleComponent : Component
{
    [ViewVariables]
    public BunkerVestibuleState State;

    /// <summary>When the current siren, or the open outer hatch, runs out.</summary>
    [ViewVariables]
    public TimeSpan Until;

    /// <summary>Where the inside siren leads: <see cref="BunkerVestibuleState.InnerOpen"/> or <see cref="BunkerVestibuleState.OuterOpen"/>.</summary>
    [ViewVariables]
    public BunkerVestibuleState SirenTarget;

    /// <summary>The door into the bunker.</summary>
    [ViewVariables]
    public EntityUid? Door;

    /// <summary>Since when the door has not been shut although it should be.</summary>
    [ViewVariables]
    public TimeSpan? DoorOpenSince;

    /// <summary>The hatch up, inside.</summary>
    [ViewVariables]
    public EntityUid? Ladder;

    /// <summary>The hatch on the surface.</summary>
    [ViewVariables]
    public EntityUid? Hatch;

    /// <summary>The siren by the surface hatch.</summary>
    [ViewVariables]
    public EntityUid? OutsideSiren;

    /// <summary>The turning lights inside the vestibule.</summary>
    [ViewVariables]
    public List<EntityUid> Lights = new();
}
