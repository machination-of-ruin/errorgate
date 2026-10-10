using Robust.Shared.Serialization;

namespace Content.Shared._ERRORGATE.AiGod;

/// <summary>
///     Sent to a player when she glitches their screen: static for <see cref="Seconds"/>, fading out. No text.
/// </summary>
[Serializable, NetSerializable]
public sealed class GodGlitchEvent : EntityEventArgs
{
    public readonly float Seconds;

    /// <summary>0 to 1.</summary>
    public readonly float Strength;

    public GodGlitchEvent(float seconds, float strength)
    {
        Seconds = seconds;
        Strength = strength;
    }
}
