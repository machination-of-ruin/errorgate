using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._ERRORGATE.DeathVoid;

/// <summary>
///     Marks the empty entity a dead player is attached to instead of a ghost.
///     It lives on an otherwise empty map, so the player cannot see, hear or read anything
///     happening around their corpse.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DeathVoidComponent : Component
{
    /// <summary>
    ///     When the player may respawn. Null means right away.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? RespawnAt;
}

/// <summary>
///     Raised by the respawn action available while in the death void.
/// </summary>
public sealed partial class DeadRespawnEvent : InstantActionEvent;
