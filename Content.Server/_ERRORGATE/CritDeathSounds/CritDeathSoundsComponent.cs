using Robust.Shared.Audio;

namespace Content.Server._ERRORGATE.CritDeathSounds;

/// <summary>
///     Plays a heartbeat to the player while in critical condition and a death sound when they die.
///     Only the player hears either of them.
/// </summary>
[RegisterComponent]
public sealed partial class CritDeathSoundsComponent : Component
{
    [DataField]
    public SoundSpecifier DeathSounds = new SoundCollectionSpecifier("ErrorgateDeathSounds");

    [DataField]
    public SoundSpecifier HeartSounds = new SoundCollectionSpecifier("ErrorgateHeartSounds");
}
