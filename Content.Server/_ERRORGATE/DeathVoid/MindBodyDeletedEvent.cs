using Content.Shared.Mind;

namespace Content.Server._ERRORGATE.DeathVoid;

/// <summary>
///     Raised by <c>MindSystem</c> when the body of a connected player was deleted while the round is running,
///     right before it would spawn a ghost. Handle it to take the player somewhere else instead.
/// </summary>
[ByRefEvent]
public record struct MindBodyDeletedEvent(EntityUid MindId, MindComponent Mind, bool Handled = false);

/// <summary>
///     Raised by <c>MindSystem</c> when a mind is about to be pushed out of its body because another mind takes the body
///     over. Handle it to put the evicted mind somewhere else, otherwise it would be turned into a ghost.
/// </summary>
[ByRefEvent]
public record struct MindEvictedEvent(EntityUid MindId, bool Handled = false);
