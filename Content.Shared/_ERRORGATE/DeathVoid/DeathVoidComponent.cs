using Content.Shared.Actions;

namespace Content.Shared._ERRORGATE.DeathVoid;

/// <summary>
///     Marks the empty entity a dead player is attached to instead of a ghost.
///     It lives on an otherwise empty map, so the player cannot see, hear or read anything
///     happening around their corpse.
/// </summary>
[RegisterComponent]
public sealed partial class DeathVoidComponent : Component;

/// <summary>
///     Raised by the respawn action available while in the death void.
/// </summary>
public sealed partial class DeadRespawnEvent : InstantActionEvent;
