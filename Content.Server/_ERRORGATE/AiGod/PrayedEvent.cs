using Robust.Shared.Player;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     Raised (broadcast) when somebody has prayed at something prayable. MACHINATION OF RUIN only hears prayers made at an
///     altar, see <see cref="GodObserverSystem"/>.
/// </summary>
public sealed class PrayedEvent : EntityEventArgs
{
    public readonly ICommonSession Sender;

    /// <summary>The thing prayed at.</summary>
    public readonly EntityUid Target;

    public readonly string Message;

    public PrayedEvent(ICommonSession sender, EntityUid target, string message)
    {
        Sender = sender;
        Target = target;
        Message = message;
    }
}
