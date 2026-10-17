namespace Content.Shared._ERRORGATE.Bark;

/// <summary>
///     A worn mask that muffles the voice barks of whoever wears it: a little lower and a little quieter. Heavy masks only (gas masks and
///     the like). A mask that can be pulled down stops muffling while it is down (see <see cref="BarkMuffleSystem"/>).
/// </summary>
[RegisterComponent]
public sealed partial class BarkMuffleComponent : Component
{
    /// <summary>Added to the pitch of every bark (1 is the normal pitch).</summary>
    [DataField]
    public float PitchShift = -0.15f;

    /// <summary>Added to the volume of every bark, in decibels (negative is quieter).</summary>
    [DataField]
    public float VolumeShift = -4f;
}
